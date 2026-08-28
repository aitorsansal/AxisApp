-- Axis schema for Supabase (Postgres + Auth + RLS).
-- Run this once against a fresh Supabase project's SQL editor.
--
-- Design note: "members" vs. auth accounts.
-- A `members` row is a ledger participant. A `payments` row always references
-- members, never `auth.users` directly. When `members.account_id` is null, the
-- member is a "phantom" — added by name only, with no linked login (e.g. a
-- relative who hasn't installed the app yet). Payments against a phantom work
-- exactly like payments against anyone else. When that person eventually signs
-- up, redeeming an invite that targets their phantom member links their new
-- account to that existing member row instead of starting a fresh, empty one —
-- their whole payment history is already attached to that member id.

create extension if not exists pgcrypto;

-- ============================================================
-- Tables
-- ============================================================

create table public.members (
  id uuid primary key default gen_random_uuid(),
  account_id uuid references auth.users(id) on delete set null,
  display_name text not null,
  created_by uuid not null references auth.users(id),
  created_at timestamptz not null default now()
);

create table public.groups (
  id uuid primary key default gen_random_uuid(),
  name text not null,
  created_by uuid not null references auth.users(id),
  created_at timestamptz not null default now()
);

create table public.group_members (
  group_id uuid not null references public.groups(id) on delete cascade,
  member_id uuid not null references public.members(id) on delete cascade,
  added_at timestamptz not null default now(),
  primary key (group_id, member_id)
);

create table public.payments (
  id uuid primary key default gen_random_uuid(),
  group_id uuid references public.groups(id) on delete set null,
  payer_member_id uuid not null references public.members(id),
  payee_member_id uuid not null references public.members(id),
  amount numeric(12,2) not null check (amount > 0),
  description text not null default '',
  category text not null default '',
  occurred_at timestamptz not null default now(),
  receipt_path text,
  created_by uuid not null references auth.users(id),
  created_at timestamptz not null default now(),
  check (payer_member_id <> payee_member_id)
);

create table public.recurring_payments (
  id uuid primary key default gen_random_uuid(),
  group_id uuid references public.groups(id) on delete set null,
  payer_member_id uuid not null references public.members(id),
  payee_member_id uuid not null references public.members(id),
  amount numeric(12,2) not null check (amount > 0),
  description text not null default '',
  category text not null default '',
  frequency text not null check (frequency in ('daily','weekly','monthly','yearly')),
  start_date date not null,
  last_processed_date date,
  is_active boolean not null default true,
  created_by uuid not null references auth.users(id),
  created_at timestamptz not null default now(),
  check (payer_member_id <> payee_member_id)
);

-- Invites: join a group fresh, or claim a specific phantom member.
create table public.invites (
  id uuid primary key default gen_random_uuid(),
  token text not null unique default encode(gen_random_bytes(9), 'base64url'),
  group_id uuid not null references public.groups(id) on delete cascade,
  target_member_id uuid references public.members(id) on delete cascade,
  created_by uuid not null references auth.users(id),
  expires_at timestamptz not null default (now() + interval '7 days'),
  max_uses int not null default 1,
  use_count int not null default 0,
  created_at timestamptz not null default now()
);

create index on public.group_members (member_id);
create index on public.payments (group_id);
create index on public.payments (payer_member_id);
create index on public.payments (payee_member_id);
create index on public.recurring_payments (group_id);
create index on public.invites (token);

-- ============================================================
-- Helper: is the current account a member of this group?
-- SECURITY DEFINER so it can read group_members/members regardless of the
-- caller's own RLS visibility, without recursing into the policies that call it.
-- ============================================================

create or replace function public.is_group_member(p_group_id uuid)
returns boolean
language sql
stable
security definer
set search_path = public
as $$
  select exists (
    select 1
    from group_members gm
    join members m on m.id = gm.member_id
    where gm.group_id = p_group_id
      and m.account_id = auth.uid()
  );
$$;

-- ============================================================
-- Row Level Security
-- ============================================================

alter table public.members enable row level security;
alter table public.groups enable row level security;
alter table public.group_members enable row level security;
alter table public.payments enable row level security;
alter table public.recurring_payments enable row level security;
alter table public.invites enable row level security;

-- groups
-- "or created_by = auth.uid()" matters at creation time: INSERT ... RETURNING
-- (what every Postgrest client insert does to hand back the new row) also
-- has to pass the SELECT policy, and the creator isn't a group_members row
-- yet at that instant (that row is only added in a follow-up insert). Without
-- this clause, creating a group throws "new row violates row-level security
-- policy for table groups" even though the INSERT's own WITH CHECK passes.
create policy "select groups you belong to" on public.groups
  for select using (is_group_member(id) or created_by = auth.uid());
create policy "insert groups" on public.groups
  for insert with check (created_by = auth.uid());
create policy "update own groups" on public.groups
  for update using (created_by = auth.uid());
create policy "delete own groups" on public.groups
  for delete using (created_by = auth.uid());

-- members: visible if they share a group with you, or it's you
-- "or created_by = auth.uid()" matters at creation time, same reason as groups/group_members
-- above: AddPhantomAsync's INSERT ... RETURNING has to pass this SELECT policy, and a freshly
-- inserted phantom (account_id null, no group_members row yet) satisfies neither of the other
-- two clauses at that exact instant.
create policy "select members you can see" on public.members
  for select using (
    account_id = auth.uid()
    or created_by = auth.uid()
    or exists (
      select 1 from group_members gm
      where gm.member_id = members.id
        and is_group_member(gm.group_id)
    )
  );
create policy "insert members" on public.members
  for insert with check (created_by = auth.uid());
create policy "update members you created or claim yourself" on public.members
  for update using (created_by = auth.uid() or account_id = auth.uid());

-- group_members (normal reads only; joining happens through redeem_invite below)
-- "or is group creator" matters at group-creation time for the same reason
-- as the groups SELECT policy above: the creator's own group_members INSERT
-- ... RETURNING has to pass this SELECT policy, and is_group_member(group_id)
-- is still false at that exact instant (this row is what would make it true).
create policy "select group_members in your groups" on public.group_members
  for select using (
    is_group_member(group_id)
    or exists (select 1 from groups g where g.id = group_id and g.created_by = auth.uid())
  );
-- Any existing group member can add a phantom (or link an existing phantom from another
-- group) into this group, not just the creator — the "created_by" clause stays only for the
-- same chicken-and-egg reason as groups'/invites' SELECT policies: the creator's own
-- group_members row (inserted right after the group itself, in the same CreateAsync call)
-- can't satisfy is_group_member(group_id) yet at that exact instant.
create policy "group members can add members" on public.group_members
  for insert with check (
    is_group_member(group_id)
    or exists (select 1 from groups g where g.id = group_id and g.created_by = auth.uid())
  );
create policy "group creator can remove members" on public.group_members
  for delete using (
    exists (select 1 from groups g where g.id = group_id and g.created_by = auth.uid())
  );

-- payments
create policy "select payments in your groups" on public.payments
  for select using (
    (group_id is null and created_by = auth.uid())
    or (group_id is not null and is_group_member(group_id))
  );
create policy "insert payments in your groups" on public.payments
  for insert with check (
    (group_id is null and created_by = auth.uid())
    or (group_id is not null and is_group_member(group_id))
  );
create policy "update payments in your groups" on public.payments
  for update using (
    (group_id is null and created_by = auth.uid())
    or (group_id is not null and is_group_member(group_id))
  );
create policy "delete payments in your groups" on public.payments
  for delete using (
    (group_id is null and created_by = auth.uid())
    or (group_id is not null and is_group_member(group_id))
  );

-- recurring_payments (same shape as payments)
create policy "select recurring in your groups" on public.recurring_payments
  for select using (
    (group_id is null and created_by = auth.uid())
    or (group_id is not null and is_group_member(group_id))
  );
create policy "insert recurring in your groups" on public.recurring_payments
  for insert with check (
    (group_id is null and created_by = auth.uid())
    or (group_id is not null and is_group_member(group_id))
  );
create policy "update recurring in your groups" on public.recurring_payments
  for update using (
    (group_id is null and created_by = auth.uid())
    or (group_id is not null and is_group_member(group_id))
  );
create policy "delete recurring in your groups" on public.recurring_payments
  for delete using (
    (group_id is null and created_by = auth.uid())
    or (group_id is not null and is_group_member(group_id))
  );

-- invites: only existing group members can create/view them; redemption is via
-- the redeem_invite() function below, which runs as SECURITY DEFINER precisely
-- because the redeemer isn't a group member yet at the moment they redeem.
create policy "select invites for your groups" on public.invites
  for select using (is_group_member(group_id));
create policy "insert invites for your groups" on public.invites
  for insert with check (is_group_member(group_id) and created_by = auth.uid());

-- ============================================================
-- redeem_invite: the one operation allowed to bypass the RLS chicken-and-egg
-- problem of "you must already be a member to be added as a member."
-- ============================================================

create or replace function public.redeem_invite(p_token text)
returns uuid
language plpgsql
security definer
set search_path = public
as $$
declare
  v_invite invites%rowtype;
  v_member_id uuid;
begin
  select * into v_invite from invites where token = p_token for update;
  if not found then
    raise exception 'Invalid invite code';
  end if;
  if v_invite.expires_at < now() then
    raise exception 'Invite expired';
  end if;
  if v_invite.use_count >= v_invite.max_uses then
    raise exception 'Invite already used';
  end if;

  if v_invite.target_member_id is not null then
    -- Claiming an existing phantom member.
    update members
       set account_id = auth.uid()
     where id = v_invite.target_member_id
       and account_id is null
    returning id into v_member_id;

    if v_member_id is null then
      raise exception 'This invite has already been claimed';
    end if;
  else
    -- Fresh join: reuse this account's member row if it already belongs to the
    -- group somehow, otherwise create one.
    select m.id into v_member_id
      from members m
      join group_members gm on gm.member_id = m.id
     where m.account_id = auth.uid()
       and gm.group_id = v_invite.group_id
     limit 1;

    if v_member_id is null then
      insert into members (account_id, display_name, created_by)
      select auth.uid(),
             coalesce((select email from auth.users where id = auth.uid()), 'New member'),
             auth.uid()
      returning id into v_member_id;
    end if;
  end if;

  insert into group_members (group_id, member_id)
  values (v_invite.group_id, v_member_id)
  on conflict do nothing;

  update invites set use_count = use_count + 1 where id = v_invite.id;

  return v_invite.group_id;
end;
$$;

-- ============================================================
-- Phase 1 additions (see /SCOPE.md): N-way expense splitting, a computed
-- balances view, currency reservation, and push device tokens. This whole
-- block is additive — run it once against the already-live project on top
-- of everything above; nothing here alters existing rows.
-- ============================================================

-- Reserve a currency column on the money tables while the schema is still
-- young, even with no conversion logic yet — see SCOPE.md's "multi-currency"
-- note. New tables below get the column baked in from the start.
alter table public.payments add column currency char(3) not null default 'EUR';
alter table public.recurring_payments add column currency char(3) not null default 'EUR';

-- expenses: a bill one member fronted, split across participants via
-- expense_shares. Distinct from `payments`, which is a direct pairwise
-- settle-up ("I paid you back $20") with no splitting concept — that stays
-- exactly as it was above.
create table public.expenses (
  id uuid primary key default gen_random_uuid(),
  group_id uuid references public.groups(id) on delete set null,
  paid_by_member_id uuid not null references public.members(id),
  amount numeric(12,2) not null check (amount > 0),
  currency char(3) not null default 'EUR',
  description text not null default '',
  category text not null default '',
  occurred_at timestamptz not null default now(),
  receipt_path text,
  created_by uuid not null references auth.users(id),
  created_at timestamptz not null default now()
);

create table public.expense_shares (
  expense_id uuid not null references public.expenses(id) on delete cascade,
  member_id uuid not null references public.members(id),
  share_amount numeric(12,2) not null check (share_amount >= 0),
  primary key (expense_id, member_id)
);

create index on public.expenses (group_id);
create index on public.expenses (paid_by_member_id);
create index on public.expense_shares (member_id);

alter table public.expenses enable row level security;
alter table public.expense_shares enable row level security;

-- expenses: same visibility/mutation shape as payments
create policy "select expenses in your groups" on public.expenses
  for select using (
    (group_id is null and created_by = auth.uid())
    or (group_id is not null and is_group_member(group_id))
  );
create policy "insert expenses in your groups" on public.expenses
  for insert with check (
    (group_id is null and created_by = auth.uid())
    or (group_id is not null and is_group_member(group_id))
  );
create policy "update expenses in your groups" on public.expenses
  for update using (
    (group_id is null and created_by = auth.uid())
    or (group_id is not null and is_group_member(group_id))
  );
create policy "delete expenses in your groups" on public.expenses
  for delete using (
    (group_id is null and created_by = auth.uid())
    or (group_id is not null and is_group_member(group_id))
  );

-- expense_shares: visible/writable by whoever can see/write the parent expense
create policy "select shares of visible expenses" on public.expense_shares
  for select using (
    exists (
      select 1 from expenses e
      where e.id = expense_shares.expense_id
        and (
          (e.group_id is null and e.created_by = auth.uid())
          or (e.group_id is not null and is_group_member(e.group_id))
        )
    )
  );
create policy "insert shares of your expenses" on public.expense_shares
  for insert with check (
    exists (
      select 1 from expenses e
      where e.id = expense_shares.expense_id
        and (
          (e.group_id is null and e.created_by = auth.uid())
          or (e.group_id is not null and is_group_member(e.group_id))
        )
    )
  );
create policy "delete shares of your expenses" on public.expense_shares
  for delete using (
    exists (
      select 1 from expenses e
      where e.id = expense_shares.expense_id
        and (
          (e.group_id is null and e.created_by = auth.uid())
          or (e.group_id is not null and is_group_member(e.group_id))
        )
    )
  );
create policy "update shares of your expenses" on public.expense_shares
  for update using (
    exists (
      select 1 from expenses e
      where e.id = expense_shares.expense_id
        and (
          (e.group_id is null and e.created_by = auth.uid())
          or (e.group_id is not null and is_group_member(e.group_id))
        )
    )
  );

-- group_balances: net balance per member per group, combining direct
-- payments and N-way expense shares, so the client queries one view instead
-- of aggregating both tables itself. security_invoker so it enforces RLS as
-- the querying user, not the view owner (Postgres 15+, which Supabase runs).
create view public.group_balances
with (security_invoker = true) as
-- payment_net: a Payment is a settle-up ("I paid you back $20" — see SCOPE.md), so
-- payer_member_id is the one discharging a debt (their balance should move toward zero,
-- i.e. increase) and payee_member_id is the one being paid back (their balance should also
-- move toward zero, i.e. decrease). Found inverted 2026-08-25 during design discussion for
-- the not-yet-built "Settle up" feature — no create-payment UI existed yet to have caught it
-- by testing, so it went live with payer/payee's deltas backwards, which would have doubled
-- every debt instead of clearing it the first time anyone used it.
with payment_net as (
  select group_id, payer_member_id as member_id, amount as delta
  from payments
  where group_id is not null
  union all
  select group_id, payee_member_id as member_id, -amount as delta
  from payments
  where group_id is not null
),
expense_payer_net as (
  select group_id, paid_by_member_id as member_id, amount as delta
  from expenses
  where group_id is not null
),
expense_share_net as (
  select e.group_id, es.member_id, -es.share_amount as delta
  from expense_shares es
  join expenses e on e.id = es.expense_id
  where e.group_id is not null
)
select group_id, member_id, sum(delta) as balance
from (
  select * from payment_net
  union all
  select * from expense_payer_net
  union all
  select * from expense_share_net
) all_deltas
group by group_id, member_id;

-- device_tokens: per-account push tokens (e.g. OneSignal player IDs), for
-- the notification feature. A token can only be registered once.
create table public.device_tokens (
  id uuid primary key default gen_random_uuid(),
  account_id uuid not null references auth.users(id) on delete cascade,
  push_token text not null unique,
  platform text not null check (platform in ('android', 'windows')),
  created_at timestamptz not null default now()
);

create index on public.device_tokens (account_id);

alter table public.device_tokens enable row level security;

create policy "manage your own device tokens" on public.device_tokens
  for all using (account_id = auth.uid()) with check (account_id = auth.uid());

-- my_group_balances: the current account's own net balance in each group it
-- belongs to, one row per group. Built for the Groups list screen (each
-- group card shows "you're owed $X" / "you owe $X" / "Settled up") so it can
-- query this directly instead of fetching every member of every group just
-- to find which member row is "me" in each one.
create view public.my_group_balances
with (security_invoker = true) as
select gb.group_id, gb.balance
from group_balances gb
join members m on m.id = gb.member_id
where m.account_id = auth.uid();

-- ============================================================
-- Pairwise balances — added 2026-08-25 alongside the group_balances/payment_net
-- fix above. group_balances collapses each member down to one net number
-- against the group's shared pot, which the app was (wrongly) displaying as
-- if it were a personal debt to whoever was looking at the screen — showing a
-- third member's uninvolved balance as "owes you" to someone who wasn't even
-- part of that expense. These views instead track genuine two-party debts,
-- derived from the same expense_shares/expenses/payments rows group_balances
-- already reads, just aggregated per counterparty instead of collapsed to one
-- total. See the app-side design discussion the same day for the "simplified
-- vs pairwise" balance display split this feeds.
-- ============================================================

-- pairwise_balances: net balance between every two members who've actually
-- shared money in a group, one row per unordered pair. Convention: balance is
-- how much member_b (the row's higher member id) owes member_a (the lower
-- id) — negative means member_a owes member_b instead. Every expense
-- contributes one edge per non-payer share-holder (they owe the payer their
-- share — the same "payer is owed, share-holders owe" convention as
-- expense_payer_net/expense_share_net above); every payment contributes the
-- reverse edge (payee "owes" payer in this bookkeeping sense, since a
-- payment is the payer discharging a debt to the payee — same
-- direction fix as payment_net above, just kept as a directed edge instead
-- of being netted into a single member's total immediately).
create view public.pairwise_balances
with (security_invoker = true) as
with edges as (
  select e.group_id, es.member_id as debtor_id, e.paid_by_member_id as creditor_id, es.share_amount as amount
  from expense_shares es
  join expenses e on e.id = es.expense_id
  where e.group_id is not null and es.member_id <> e.paid_by_member_id
  union all
  select p.group_id, p.payee_member_id as debtor_id, p.payer_member_id as creditor_id, p.amount
  from payments p
  where p.group_id is not null
)
select
  group_id,
  least(debtor_id, creditor_id) as member_a,
  greatest(debtor_id, creditor_id) as member_b,
  sum(case when debtor_id < creditor_id then -amount else amount end) as balance
from edges
group by group_id, least(debtor_id, creditor_id), greatest(debtor_id, creditor_id)
having sum(case when debtor_id < creditor_id then -amount else amount end) <> 0;

-- my_pairwise_balances: pairwise_balances reoriented around the current
-- account specifically — one row per other member they've shared money with
-- in a group, with balance already flipped to a consistent "positive means
-- they owe me" convention regardless of which side of pairwise_balances'
-- member_a/member_b the current account happened to land on.
create view public.my_pairwise_balances
with (security_invoker = true) as
select
  pb.group_id,
  case when m.id = pb.member_a then pb.member_b else pb.member_a end as other_member_id,
  case when m.id = pb.member_a then pb.balance else -pb.balance end as balance
from pairwise_balances pb
join members m on m.account_id = auth.uid() and (m.id = pb.member_a or m.id = pb.member_b);

-- ============================================================
-- Categories removed — added 2026-08-28. `categories` never had a working
-- "add new category" UI path (ICategoriesRepository.EnsureByNameAsync was
-- dead code, never called), had no seed data (so the chip row rendered
-- empty on any fresh deploy), and its `for select using (true)` policy made
-- every account's custom category visible to every other account app-wide —
-- inconsistent with how everything else in this schema scopes visibility to
-- shared groups. Replaced with a small, fixed, developer-maintained list of
-- keys (AppConstants.Categories.Keys in the app), localized client-side per
-- viewer (see AxisApp.Localization) rather than stored as text — a stored
-- label would bake whichever language the expense's creator happened to be
-- using into the data for every other viewer of a shared expense, forever.
-- expenses.category / payments.category / recurring_payments.category were
-- always plain text columns with no foreign key into categories, so no data
-- migration is needed for them — they just start holding key strings like
-- "food" instead of arbitrary user-typed text going forward.
-- Run this against the live project the same way every other block in this
-- file has needed to be (see CLAUDE.md's "Current state" notes) — it isn't
-- applied automatically.
-- ============================================================
drop table if exists public.categories cascade;
