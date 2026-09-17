-- Axis schema for Supabase (Postgres + Auth + RLS).
-- Run this once against a fresh Supabase project's SQL editor.
--
-- Design note: "members" vs. auth accounts.
-- A `members` row is a ledger participant. An `expenses` row always references
-- members, never `auth.users` directly. When `members.account_id` is null, the
-- member is a "phantom" — added by name only, with no linked login (e.g. a
-- relative who hasn't installed the app yet). Expenses against a phantom work
-- exactly like expenses against anyone else. When that person eventually signs
-- up, redeeming an invite that targets their phantom member links their new
-- account to that existing member row instead of starting a fresh, empty one —
-- their whole expense history is already attached to that member id.
--
-- Design note: settlements are expenses, not a separate table.
-- A settle-up ("I paid you back $20") is an Expense with is_settlement = true
-- and exactly one ExpenseShare — there used to be a dedicated `payments` table
-- for this, retired 2026-09-04 once the balance math was confirmed identical:
-- Payment(payer, payee, amount) === Expense(paid_by=payer, shares=[{payee,
-- amount}]). See CLAUDE.md's "Merge payments into expenses" remarks.

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

-- currency: picked once, on NewGroupPage, at the same insert that creates
-- the group. Never editable afterward — enforced by the app (no edit path
-- is ever exposed), not the DB, so the choice is always a deliberate one
-- made before any expense exists, never something that could be perceived
-- as falling out of whatever the first expense happened to use. The check
-- list is Frankfurter's (frankfurter.dev, ECB reference rates) actual
-- supported currency set, verified against its live /v1/currencies
-- response, not guessed — AppConstants.Currencies mirrors this exact list.
-- color/icon: an "appearance" tag for the Groups list (a colored circle showing a Lucide
-- icon glyph, initials fallback otherwise) — added 2026-09-14, member-editable unlike name/
-- currency above (see this file's "update groups you belong to" policy and
-- enforce_group_owner_only_columns() trigger below for how that split is actually enforced).
-- color is an AccentPreset name (Services/AccentPalettes.cs), not a hex value; icon is a key
-- into AppConstants.GroupIcons, mirrored by the check constraint below — keep both in sync.
create table public.groups (
  id uuid primary key default gen_random_uuid(),
  name text not null,
  currency char(3) not null default 'EUR'
    check (currency in (
      'AUD','BRL','CAD','CHF','CNY','CZK','DKK','EUR','GBP','HKD','HUF','IDR',
      'ILS','INR','ISK','JPY','KRW','MXN','MYR','NOK','NZD','PHP','PLN','RON',
      'SEK','SGD','THB','TRY','USD','ZAR'
    )),
  color text not null default 'Blue'
    check (color in ('Blue','Green','Red','Purple','Pink','Amber','Orange','Navy')),
  icon text
    check (icon is null or icon in (
      'home','users','heart','baby','dog','plane','car','ship','bike','tent','map_pin',
      'mountain','utensils_crossed','coffee','beer','party_popper','gift','wallet','piggy_bank',
      'briefcase','dumbbell','graduation_cap','gamepad','film','music','book','shopping_cart','star'
    )),
  created_by uuid not null references auth.users(id),
  created_at timestamptz not null default now()
);

create table public.group_members (
  group_id uuid not null references public.groups(id) on delete cascade,
  member_id uuid not null references public.members(id) on delete cascade,
  added_at timestamptz not null default now(),
  primary key (group_id, member_id)
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
-- Any current member can update a group's row (needed for the member-editable color/icon
-- appearance tag below) — every creator is already a member too (create_group() adds them in
-- the same transaction), so this covers rename/currency-attempting requests as well; which
-- columns those are actually allowed to touch is enforced by the
-- enforce_group_owner_only_columns() trigger, not by this policy.
create policy "update groups you belong to" on public.groups
  for update using (is_group_member(id)) with check (is_group_member(id));
create policy "delete own groups" on public.groups
  for delete using (created_by = auth.uid());

-- Plain RLS can only gate whole rows, not "these columns only if you're the creator" — so
-- name/currency staying creator-only despite the member-wide update policy above is enforced
-- here instead, regardless of how the update request was made (not just what the app's own
-- Set(color/icon)-only update path happens to send).
--
-- id/created_by/created_at locked too (2026-09-17 RLS hardening, rls_hardening.sql): the
-- member-wide update policy otherwise let any member PATCH created_by to themselves and then
-- dissolve the group. The lock is keyed on current_user rather than auth.uid(): an app request
-- via PostgREST runs as `authenticated`, while transfer_group_ownership() (security definer) runs
-- as its owner, so ownership transfer keeps working with no bypass flag. A trigger function that
-- isn't itself security definer inherits the caller's current_user, which is what makes this
-- reliable — the same pattern every other protect_*_columns trigger in this file uses. Locked
-- columns are silently restored rather than raising, so a client sending the whole row back
-- unchanged (MAUI's Update(model)) never breaks a normal edit.
create or replace function public.enforce_group_owner_only_columns()
returns trigger
language plpgsql
as $$
begin
  if current_user in ('authenticated', 'anon') then
    new.id := old.id;
    new.created_by := old.created_by;
    new.created_at := old.created_at;

    if (new.name is distinct from old.name or new.currency is distinct from old.currency)
       and auth.uid() is distinct from old.created_by then
      raise exception 'Only the group creator can change name or currency.';
    end if;
  end if;
  return new;
end;
$$;

create trigger enforce_group_owner_only_columns
  before update on public.groups
  for each row execute function public.enforce_group_owner_only_columns();

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
-- Inserting a member row pointing at someone else's account is not allowed — only a phantom
-- (account_id null) or your own row. handle_new_user_member()/create_group()/redeem_invite()
-- create claimed rows as security definer, so they're unaffected.
create policy "insert phantoms or your own member" on public.members
  for insert with check (
    created_by = auth.uid()
    and (account_id is null or account_id = auth.uid())
  );
-- A phantom's creator edits the phantom; once claimed, only its own account edits it.
create policy "update your own member or a phantom you created" on public.members
  for update
  using ((account_id is null and created_by = auth.uid()) or account_id = auth.uid())
  with check ((account_id is null and created_by = auth.uid()) or account_id = auth.uid());

-- account_id/created_by can never change through an app request (2026-09-17 RLS hardening):
-- before this, the update policy above had no WITH CHECK, so a phantom's creator could set
-- account_id = themselves and inherit every group that phantom was linked into
-- (is_group_member() only looks at account_id). They only change via redeem_invite() /
-- delete_account() now. Same current_user keying as enforce_group_owner_only_columns().
create or replace function public.protect_member_identity_columns()
returns trigger
language plpgsql
as $$
begin
  if current_user in ('authenticated', 'anon') then
    new.id := old.id;
    new.account_id := old.account_id;
    new.created_by := old.created_by;
    new.created_at := old.created_at;
  end if;
  return new;
end;
$$;

create trigger protect_member_identity_columns
  before update on public.members
  for each row execute function public.protect_member_identity_columns();

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
-- group) into this group, not just the creator. Only phantoms, though (2026-09-17 RLS
-- hardening): a real account joins only by redeeming an invite itself, never by someone else's
-- insert. And only a phantom you created or already share a group with — the same visibility
-- the name search (SearchVisibleByNameAsync) respects through members' SELECT policy, enforced
-- here too so a crafted request with a known member id can't link a phantom you can't see.
-- The creator's own row no longer needs a clause here: create_group() inserts it as security
-- definer.
create or replace function public.is_linkable_phantom(p_member_id uuid)
returns boolean
language sql
stable
security definer
set search_path = public
as $$
  select exists (
    select 1
    from members m
    where m.id = p_member_id
      and m.account_id is null
      and (
        m.created_by = auth.uid()
        or exists (
          select 1 from group_members gm
          where gm.member_id = m.id
            and is_group_member(gm.group_id)
        )
      )
  );
$$;

create policy "group members can add phantoms" on public.group_members
  for insert with check (
    is_group_member(group_id)
    and is_linkable_phantom(member_id)
  );
-- No DELETE policy on purpose (2026-09-17 RLS hardening): removal only happens through
-- leave_group() / remove_group_member(), both security definer. The direct-delete policies that
-- used to exist here ("group creator can remove members", "members can remove themselves") let a
-- request skip those functions' balance/creator/phantom-only rules entirely.

-- invites: only existing group members can create/view them; redemption is via
-- the redeem_invite() function below, which runs as SECURITY DEFINER precisely
-- because the redeemer isn't a group member yet at the moment they redeem.
create policy "select invites for your groups" on public.invites
  for select using (is_group_member(group_id));
-- A phantom-claim invite must target a phantom that's actually in the invite's own group
-- (2026-09-17 RLS hardening) — before this, target_member_id was unchecked, so any member could
-- mint a claim invite for any phantom id they'd ever seen. Any group member can still create
-- one (deliberate: if the phantom's creator is unreachable, someone else in the group can send
-- the invite); who's allowed to *redeem* it is enforced in redeem_invite() below.
create or replace function public.is_phantom_in_group(p_member_id uuid, p_group_id uuid)
returns boolean
language sql
stable
security definer
set search_path = public
as $$
  select exists (
    select 1
    from members m
    join group_members gm on gm.member_id = m.id
    where m.id = p_member_id
      and m.account_id is null
      and gm.group_id = p_group_id
  );
$$;

create policy "insert invites for your groups" on public.invites
  for insert with check (
    is_group_member(group_id)
    and created_by = auth.uid()
    and (target_member_id is null or is_phantom_in_group(target_member_id, group_id))
  );
-- Added 2026-09-14 (invites_update_policy.sql) so a group member can edit an
-- existing invite's max_uses/expires_at instead of always minting a new row.
create policy "update invites for your groups" on public.invites
  for update using (is_group_member(group_id)) with check (is_group_member(group_id));
-- Added 2026-09-17 — invites couldn't be revoked at all before.
create policy "delete invites for your groups" on public.invites
  for delete using (is_group_member(group_id));

-- Only max_uses/expires_at stay member-editable (2026-09-17 RLS hardening). use_count in
-- particular could be reset to 0 to reopen a spent invite. Same current_user keying and
-- silent-restore behavior as enforce_group_owner_only_columns(); redeem_invite() increments
-- use_count as security definer, so it's unaffected.
create or replace function public.protect_invite_columns()
returns trigger
language plpgsql
as $$
begin
  if current_user in ('authenticated', 'anon') then
    new.id := old.id;
    new.token := old.token;
    new.group_id := old.group_id;
    new.target_member_id := old.target_member_id;
    new.use_count := old.use_count;
    new.created_by := old.created_by;
    new.created_at := old.created_at;
  end if;
  return new;
end;
$$;

create trigger protect_invite_columns
  before update on public.invites
  for each row execute function public.protect_invite_columns();

-- ============================================================
-- redeem_invite: the one operation allowed to bypass the RLS chicken-and-egg
-- problem of "you must already be a member to be added as a member."
-- ============================================================

-- One-account-one-member invariant fixed 2026-09-07 — two bugs here, both
-- letting a single account end up with more than one members row:
--
-- 1. The fresh-join branch's "reuse if it already belongs to the group"
--    lookup was scoped to gm.group_id = v_invite.group_id, so it only found
--    an existing row when the account happened to already be in *this*
--    group. Joining any group the account wasn't already in — which is the
--    normal case — always fell through to a brand-new members row, even
--    though the account may well have had one already from a different
--    group. Fixed by dropping the group_id scoping entirely: an account
--    only ever gets one members row, full stop.
-- 2. The claim branch (target_member_id is not null) never checked whether
--    the claiming account already had a members row of its own. If it did
--    (claiming a phantom in a second group after already being a real
--    member elsewhere), the phantom got claimed as a *second* claimed row
--    for the same account instead of being merged into the existing one.
--    Fixed by merging: when the account already has a members row, every
--    expenses/expense_shares/recurring_expenses/recurring_expense_shares
--    row pointing at the phantom is repointed at the existing row instead
--    (summing share amounts on the rare case both already hold a share on
--    the same expense/template, to avoid violating the (expense_id,
--    member_id) / (recurring_expense_id, member_id) composite PKs and to
--    keep balances correct), the phantom is added nowhere (the existing
--    row joins the group instead), and the now-empty phantom row is
--    deleted — cascading away its own group_members/invites/member_aliases
--    rows, which is fine, those were about the phantom identity that no
--    longer exists.
--
-- Who can claim, tightened 2026-09-17 (RLS hardening). Claiming still carries
-- over every group the phantom is linked into and merges its history — one
-- invite covers a phantom linked into Trip/House/Parties, by design. New rules:
--   * must be signed in (and anon has no EXECUTE on this function anymore);
--   * the invite's creator must still be a member of its group, so someone
--     removed from the group can't reopen access with an invite they made
--     earlier;
--   * the target must still be a phantom inside the invite's own group;
--   * the redeemer must not already belong to ANY group the phantom is in.
--     Any member can hand out a claim invite (e.g. Bob in Parties, when the
--     phantom's creator isn't reachable), but can't redeem it themselves to
--     absorb the phantom's other groups. Accepted residual risk: Bob could
--     pass the code to an outside, allowlisted account — the database can't
--     tell whether the redeemer really is the person the phantom stands for;
--   * a fresh join by someone already in the group returns without burning a use.
create or replace function public.redeem_invite(p_token text)
returns uuid
language plpgsql
security definer
set search_path = public
as $$
declare
  v_invite invites%rowtype;
  v_member_id uuid;
  v_existing_member_id uuid;
begin
  if auth.uid() is null then
    raise exception 'Not signed in';
  end if;

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

  if v_invite.created_by is null or not exists (
    select 1
    from group_members gm
    join members m on m.id = gm.member_id
    where gm.group_id = v_invite.group_id
      and m.account_id = v_invite.created_by
  ) then
    raise exception 'This invite is no longer valid — ask a current group member for a new one';
  end if;

  select id into v_existing_member_id from members where account_id = auth.uid() limit 1;

  if v_invite.target_member_id is not null then
    -- Claiming an existing phantom member.
    if not exists (
      select 1
      from members m
      join group_members gm on gm.member_id = m.id
      where m.id = v_invite.target_member_id
        and m.account_id is null
        and gm.group_id = v_invite.group_id
    ) then
      raise exception 'This invite has already been claimed';
    end if;

    if v_existing_member_id is not null and exists (
      select 1
      from group_members gm_phantom
      join group_members gm_me
        on gm_me.group_id = gm_phantom.group_id
       and gm_me.member_id = v_existing_member_id
      where gm_phantom.member_id = v_invite.target_member_id
    ) then
      raise exception 'You''re already in a group with this person, so this invite isn''t for you';
    end if;

    if v_existing_member_id is not null then
      -- Account already has a members row — merge the phantom into it
      -- instead of creating a second claimed row for the same account.
      update expenses
         set paid_by_member_id = v_existing_member_id
       where paid_by_member_id = v_invite.target_member_id;

      update recurring_expenses
         set paid_by_member_id = v_existing_member_id
       where paid_by_member_id = v_invite.target_member_id;

      -- Sum share amounts where both rows already hold a share on the same
      -- expense, then drop the phantom's now-redundant row before
      -- repointing whatever's left.
      update expense_shares es
         set share_amount = es.share_amount + p.share_amount,
             share_amount_in_group_currency = es.share_amount_in_group_currency
               + p.share_amount_in_group_currency
        from expense_shares p
       where p.member_id = v_invite.target_member_id
         and es.member_id = v_existing_member_id
         and es.expense_id = p.expense_id;

      delete from expense_shares
       where member_id = v_invite.target_member_id
         and expense_id in (
           select expense_id from expense_shares where member_id = v_existing_member_id
         );

      update expense_shares
         set member_id = v_existing_member_id
       where member_id = v_invite.target_member_id;

      update recurring_expense_shares res
         set share_amount = res.share_amount + p.share_amount
        from recurring_expense_shares p
       where p.member_id = v_invite.target_member_id
         and res.member_id = v_existing_member_id
         and res.recurring_expense_id = p.recurring_expense_id;

      delete from recurring_expense_shares
       where member_id = v_invite.target_member_id
         and recurring_expense_id in (
           select recurring_expense_id from recurring_expense_shares
            where member_id = v_existing_member_id
         );

      update recurring_expense_shares
         set member_id = v_existing_member_id
       where member_id = v_invite.target_member_id;

      -- The phantom may already belong to other groups too (e.g. linked into
      -- them by name via JoinGroupPage before ever being claimed), not just
      -- this invite's group — carry all of those memberships over, or
      -- they'd silently vanish when the phantom row cascades away below.
      insert into group_members (group_id, member_id)
      select group_id, v_existing_member_id
        from group_members
       where member_id = v_invite.target_member_id
      on conflict do nothing;

      delete from members where id = v_invite.target_member_id;

      v_member_id := v_existing_member_id;
    else
      -- No members row yet (only possible if handle_new_user_member() didn't
      -- run for this account) — claim the phantom row itself.
      update members
         set account_id = auth.uid()
       where id = v_invite.target_member_id
         and account_id is null
      returning id into v_member_id;

      if v_member_id is null then
        raise exception 'This invite has already been claimed';
      end if;
    end if;
  else
    -- Fresh join: reuse this account's members row if it has one, from any
    -- group, otherwise create one.
    v_member_id := v_existing_member_id;

    if v_member_id is not null and exists (
      select 1 from group_members
      where group_id = v_invite.group_id and member_id = v_member_id
    ) then
      return v_invite.group_id;
    end if;

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

-- Every function in public is executable by PUBLIC (anon included) by default.
revoke execute on function public.redeem_invite(text) from public, anon;
grant execute on function public.redeem_invite(text) to authenticated;

-- ============================================================
-- Phase 1 additions (see /SCOPE.md): N-way expense splitting, a computed
-- balances view, currency reservation, and push device tokens. This whole
-- block is additive — run it once against the already-live project on top
-- of everything above; nothing here alters existing rows.
-- ============================================================

-- currency: what the expense was actually entered in — may differ from its
-- group's currency (e.g. a foreign-currency purchase in an otherwise-EUR
-- group). amount_in_group_currency/exchange_rate are snapshotted once, by
-- a BEFORE INSERT/UPDATE trigger (below, after expense_shares), at the
-- moment the row is written — never re-derived later, so a balance never
-- silently drifts just because today's exchange rate moved. See
-- /MULTI_CURRENCY_PLAN.md for the full design.

-- expenses: a bill one member fronted, split across participants via
-- expense_shares. is_settlement marks a settle-up ("I paid you back $20") —
-- always exactly one share when true, no splitting concept — see the "Design
-- note: settlements are expenses, not a separate table" header comment.
create table public.expenses (
  id uuid primary key default gen_random_uuid(),
  group_id uuid references public.groups(id) on delete set null,
  paid_by_member_id uuid not null references public.members(id),
  amount numeric(12,2) not null check (amount > 0),
  currency char(3) not null default 'EUR'
    check (currency in (
      'AUD','BRL','CAD','CHF','CNY','CZK','DKK','EUR','GBP','HKD','HUF','IDR',
      'ILS','INR','ISK','JPY','KRW','MXN','MYR','NOK','NZD','PHP','PLN','RON',
      'SEK','SGD','THB','TRY','USD','ZAR'
    )),
  amount_in_group_currency numeric(12,2) not null,
  exchange_rate numeric(18,8) not null default 1,
  description text not null default '',
  category text not null default '',
  occurred_at timestamptz not null default now(),
  receipt_path text,
  created_by uuid not null references auth.users(id),
  created_at timestamptz not null default now(),
  is_settlement boolean not null default false
  -- event_id (link to the event an expense was booked from) is added via
  -- `alter table` further down, right after the events table itself is
  -- created — this create table runs long before events exists in a linear
  -- fresh-install run of this script, so a forward FK reference here would
  -- fail. See that alter statement's own comment for the design rationale.
);

create table public.expense_shares (
  expense_id uuid not null references public.expenses(id) on delete cascade,
  member_id uuid not null references public.members(id),
  share_amount numeric(12,2) not null check (share_amount >= 0),
  share_amount_in_group_currency numeric(12,2) not null,
  primary key (expense_id, member_id)
);

create index on public.expenses (group_id);
create index on public.expenses (paid_by_member_id);
create index on public.expense_shares (member_id);

alter table public.expenses enable row level security;
alter table public.expense_shares enable row level security;

-- ============================================================
-- exchange_rates — a SINGLETON table (the `id boolean primary key default
-- true check (id)` trick: id can only ever be `true`, and being the primary
-- key, that means at most one row can ever exist). Deliberately not one row
-- per day: every expense snapshots its own converted amount at write time
-- (via the triggers below), so once a row exists, historical rates serve no
-- purpose — only "the latest known rate" is ever needed to compute a *new*
-- snapshot. Written by a daily Edge Function (fetch-exchange-rates, not
-- built yet — see /MULTI_CURRENCY_PLAN.md's Milestone 2) via delete-then-
-- insert, matching this project's existing "delete then insert, not
-- upsert" idiom for enforcing uniqueness (see
-- SupabaseDeviceTokensRepository.RegisterAsync's own remarks). rates is
-- EUR-pivoted (units of X per 1 EUR, matching Frankfurter's native base)
-- and, per Frankfurter's own response shape, does NOT include an "EUR"
-- key — the conversion trigger below treats EUR as implicitly 1 rather
-- than requiring the Edge Function to inject it.
-- ============================================================
create table public.exchange_rates (
  id boolean primary key default true,
  as_of date not null,
  rates jsonb not null,
  constraint exchange_rates_single_row check (id)
);

alter table public.exchange_rates enable row level security;

create policy "authenticated users can read exchange rates" on public.exchange_rates
  for select to authenticated using (true);

-- No insert/update/delete policy for authenticated/anon — only the daily
-- Edge Function (service-role client, bypasses RLS entirely) ever writes
-- this table.

-- ============================================================
-- snapshot_expense_currency_conversion(): BEFORE INSERT/UPDATE on expenses,
-- computes amount_in_group_currency/exchange_rate once at write time. Not
-- security definer — the caller already has legitimate SELECT access to
-- both groups (a real group member, per "select expenses in your groups"'s
-- own is_group_member check) and exchange_rates (the authenticated-read
-- policy above), so there's no permission gap to bypass here, unlike e.g.
-- leave_group()/transfer_group_ownership(). Raises rather than silently
-- treating a missing rate as 1:1 — a blocked save is a better failure mode
-- than a silently wrong conversion in a ledger.
-- ============================================================
create or replace function public.snapshot_expense_currency_conversion()
returns trigger
language plpgsql
as $$
declare
  v_group_currency char(3);
  v_rates jsonb;
  v_from_rate numeric;
  v_to_rate numeric;
begin
  if new.group_id is null then
    -- Unscoped expense (its group was dissolved) — no group currency left
    -- to convert against, and group_balances/pairwise_balances already
    -- exclude group_id is null rows entirely, so this value is unused
    -- beyond keeping the column non-null.
    new.exchange_rate := 1;
    new.amount_in_group_currency := new.amount;
    return new;
  end if;

  -- Same currency as before: reuse the rate the expense was first converted
  -- at, so correcting an old amount doesn't silently apply today's rate.
  -- Safe because groups.currency can't change once a group has expenses
  -- (enforce_group_currency_locked, "Currency integrity" section at the end).
  if tg_op = 'UPDATE' and new.currency = old.currency then
    new.exchange_rate := old.exchange_rate;
    new.amount_in_group_currency := round(new.amount * new.exchange_rate, 2);
    return new;
  end if;

  select currency into v_group_currency from public.groups where id = new.group_id;

  if new.currency = v_group_currency then
    new.exchange_rate := 1;
    new.amount_in_group_currency := new.amount;
    return new;
  end if;

  select rates into v_rates from public.exchange_rates limit 1;

  if v_rates is null then
    raise exception 'No exchange rate data available yet — cannot convert % to %', new.currency, v_group_currency;
  end if;

  v_from_rate := case when new.currency = 'EUR' then 1 else (v_rates->>new.currency)::numeric end;
  v_to_rate := case when v_group_currency = 'EUR' then 1 else (v_rates->>v_group_currency)::numeric end;

  if v_from_rate is null or v_to_rate is null then
    raise exception 'No exchange rate available for % or %', new.currency, v_group_currency;
  end if;

  new.exchange_rate := v_to_rate / v_from_rate;
  new.amount_in_group_currency := round(new.amount * new.exchange_rate, 2);
  return new;
end;
$$;

create trigger expenses_snapshot_currency_conversion
  before insert or update of amount, currency on public.expenses
  for each row execute function public.snapshot_expense_currency_conversion();

-- snapshot_expense_share_currency_conversion(): BEFORE INSERT/UPDATE on
-- expense_shares. Always reads its parent expense's already-computed
-- exchange_rate (set by the trigger above, which has already run by the
-- time a share is inserted/updated — confirmed against
-- SupabaseExpensesRepository.AddAsync/UpdateAsync, both of which await the
-- expense insert/update before touching expense_shares) rather than
-- re-deriving currency codes itself.
create or replace function public.snapshot_expense_share_currency_conversion()
returns trigger
language plpgsql
as $$
declare
  v_rate numeric;
begin
  select exchange_rate into v_rate from public.expenses where id = new.expense_id;
  new.share_amount_in_group_currency := round(new.share_amount * coalesce(v_rate, 1), 2);
  return new;
end;
$$;

create trigger expense_shares_snapshot_currency_conversion
  before insert or update of share_amount on public.expense_shares
  for each row execute function public.snapshot_expense_share_currency_conversion();

-- expenses: visible/writable by any current group member
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
-- No INSERT/UPDATE/DELETE policies on purpose (2026-09-17,
-- supabase/share_writes_via_rpc.sql): every share write goes through
-- save_expense() (security definer, further down this file), never a direct
-- PostgREST call. The direct policies that used to be here let a group member
-- PATCH a single share's amount, which moved real money AND left no audit trail
-- — sync_expense_converted_total propagated the new sum into
-- expenses.amount_in_group_currency, and that column is exactly the one
-- record_expense_history's skip condition excludes, so nothing was logged.
-- Redistributing a split between two members kept the sum intact and was equally
-- silent, so a sum-check constraint alone would not have closed this.
--
-- Funnelling every write through save_expense() is also what makes the audit
-- trail complete: record_expense_history fires AFTER UPDATE on expenses and
-- reads expense_shares at that instant, and save_expense always updates the row
-- BEFORE touching shares — so old_shares is genuinely the pre-edit split. That
-- property only holds while the funnel is mandatory. Don't re-add a direct write
-- policy here without also giving expense_shares its own history trigger.

-- ============================================================
-- fetch-exchange-rates cron (Milestone 2, /MULTI_CURRENCY_PLAN.md) — daily
-- refresh of the exchange_rates singleton above. Same net.http_post + Vault
-- service_role_key pattern the cleanup-receipts cron uses (see that job's
-- own remarks further down this file): pg_net can't reach Supabase Storage
-- or run arbitrary client-library code directly, so the actual Frankfurter
-- fetch + delete-then-insert happens in the paired Edge Function
-- (supabase/functions/fetch-exchange-rates/index.ts), this just invokes it.
-- Daily at 6am UTC — before materialize-recurring-expenses' 8am run, so a
-- recurring expense materializing in a foreign currency that same morning
-- has a same-day rate available rather than falling back to yesterday's
-- stale cache; still off-peak, same reasoning as every other cron job here.
-- Unlike the rest of this file, the URL below is this specific project's —
-- a fresh project would need its own project ref substituted in.
-- ============================================================
select cron.schedule(
  'fetch-exchange-rates',
  '0 6 * * *',
  $$
  select net.http_post(
    url := 'https://foepkovwmwyygulbdahv.supabase.co/functions/v1/fetch-exchange-rates',
    headers := jsonb_build_object(
      'Content-Type', 'application/json',
      'Authorization', 'Bearer ' || (select decrypted_secret from vault.decrypted_secrets where name = 'service_role_key' limit 1)
    ),
    body := '{}'::jsonb
  ) as request_id;
  $$
);

-- ============================================================
-- recurring_expenses / recurring_expense_shares — added 2026-08-31, a
-- template for a periodically auto-generated Expense, split N ways via
-- recurring_expense_shares. Mirrors expenses/expense_shares exactly, plus
-- the schedule columns (frequency/start_date/last_processed_date/
-- is_active) recurring_payments already proved out before being retired
-- (see CLAUDE.md's "Recurring expenses" remarks for why: a Payment(payer,
-- payee, amount) has identical balance math to Expense(paid_by=payer,
-- participants=[payee], share=amount), so a 1-way recurring expense fully
-- replaces what recurring_payments was for — no functional code ever
-- consumed it, so it was deleted rather than kept alongside this).
--
-- Editing a template (e.g. changing an amount) only affects expenses
-- materialized after the edit — a later pg_cron job (not built yet, see
-- SCOPE.md) reads last_processed_date to know what's still due and inserts
-- independent expenses/expense_shares rows; past materialized rows are
-- snapshots, never rewritten by a template edit. This table intentionally
-- has no "unscoped party" visibility widening the way expenses/payments
-- got after group dissolution (is_unscoped_expense_party) — a dissolved
-- group's templates just become creator-only-visible, since nothing
-- materializes from an unscoped template anyway. Flagged as a possible
-- future gap, not a blocker.
-- ============================================================

create table public.recurring_expenses (
  id uuid primary key default gen_random_uuid(),
  group_id uuid references public.groups(id) on delete set null,
  paid_by_member_id uuid not null references public.members(id),
  amount numeric(12,2) not null check (amount > 0),
  currency char(3) not null default 'EUR'
    check (currency in (
      'AUD','BRL','CAD','CHF','CNY','CZK','DKK','EUR','GBP','HKD','HUF','IDR',
      'ILS','INR','ISK','JPY','KRW','MXN','MYR','NOK','NZD','PHP','PLN','RON',
      'SEK','SGD','THB','TRY','USD','ZAR'
    )),
  description text not null default '',
  category text not null default '',
  frequency text not null check (frequency in ('daily','weekly','monthly','yearly')),
  start_date date not null,
  last_processed_date date,
  is_active boolean not null default true,
  created_by uuid not null references auth.users(id),
  created_at timestamptz not null default now()
);

create table public.recurring_expense_shares (
  recurring_expense_id uuid not null references public.recurring_expenses(id) on delete cascade,
  member_id uuid not null references public.members(id),
  share_amount numeric(12,2) not null check (share_amount >= 0),
  primary key (recurring_expense_id, member_id)
);

create index on public.recurring_expenses (group_id);
create index on public.recurring_expense_shares (member_id);

alter table public.recurring_expenses enable row level security;
alter table public.recurring_expense_shares enable row level security;

-- recurring_expenses: same visibility/mutation shape as expenses
create policy "select recurring expenses in your groups" on public.recurring_expenses
  for select using (
    (group_id is null and created_by = auth.uid())
    or (group_id is not null and is_group_member(group_id))
  );
create policy "insert recurring expenses in your groups" on public.recurring_expenses
  for insert with check (
    (group_id is null and created_by = auth.uid())
    or (group_id is not null and is_group_member(group_id))
  );
create policy "update recurring expenses in your groups" on public.recurring_expenses
  for update using (
    (group_id is null and created_by = auth.uid())
    or (group_id is not null and is_group_member(group_id))
  );
create policy "delete recurring expenses in your groups" on public.recurring_expenses
  for delete using (
    (group_id is null and created_by = auth.uid())
    or (group_id is not null and is_group_member(group_id))
  );

-- recurring_expense_shares: visible/writable by whoever can see/write the parent template
create policy "select shares of visible recurring expenses" on public.recurring_expense_shares
  for select using (
    exists (
      select 1 from recurring_expenses re
      where re.id = recurring_expense_shares.recurring_expense_id
        and (
          (re.group_id is null and re.created_by = auth.uid())
          or (re.group_id is not null and is_group_member(re.group_id))
        )
    )
  );
-- No INSERT/UPDATE/DELETE policies, same reasoning as expense_shares above
-- (2026-09-17, supabase/share_writes_via_rpc.sql): writes go through
-- save_recurring_expense() only.

-- group_balances: net balance per member per group. A settlement is just an
-- expense with is_settlement true and one share, so expense_payer_net +
-- expense_share_net alone cover it — no separate payment_net CTE needed
-- (there used to be one; see the "Design note: settlements are expenses"
-- header comment for why it was retired 2026-09-04). security_invoker so
-- this enforces RLS as the querying user, not the view owner (Postgres 15+,
-- which Supabase runs).
--
-- Sign convention, still worth stating explicitly since it bit this exact
-- view once already (found inverted 2026-08-25, which would have doubled
-- every debt instead of clearing it the first time a settle-up ran): the
-- fronting party (paid_by_member_id) moves toward being owed (balance up),
-- every share-holder moves toward owing (balance down) — a settlement's
-- paid_by is the discharging party and its one share-holder is who was
-- owed, so this same rule correctly nets a settlement toward zero too.
create view public.group_balances
with (security_invoker = true) as
with expense_payer_net as (
  select group_id, paid_by_member_id as member_id, amount_in_group_currency as delta
  from expenses
  where group_id is not null
),
expense_share_net as (
  select e.group_id, es.member_id, -es.share_amount_in_group_currency as delta
  from expense_shares es
  join expenses e on e.id = es.expense_id
  where e.group_id is not null
)
select group_id, member_id, sum(delta) as balance
from (
  select * from expense_payer_net
  union all
  select * from expense_share_net
) all_deltas
group by group_id, member_id;

-- device_tokens: per-account push tokens (FCM registration tokens — Android via
-- Xamarin.Firebase.Messaging, web via the Firebase JS SDK), for the notification
-- feature. A token can only be registered once. 'windows' is a placeholder for a
-- platform whose IPushRegistrationService implementation is a deliberate no-op —
-- see CLAUDE.md's push-notifications remarks.
create table public.device_tokens (
  id uuid primary key default gen_random_uuid(),
  account_id uuid not null references auth.users(id) on delete cascade,
  push_token text not null unique,
  platform text not null check (platform in ('android', 'windows', 'web')),
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
-- Pairwise balances — added 2026-08-25 alongside the group_balances sign fix
-- above. group_balances collapses each member down to one net number
-- against the group's shared pot, which the app was (wrongly) displaying as
-- if it were a personal debt to whoever was looking at the screen — showing a
-- third member's uninvolved balance as "owes you" to someone who wasn't even
-- part of that expense. These views instead track genuine two-party debts,
-- derived from the same expense_shares/expenses rows group_balances already
-- reads, just aggregated per counterparty instead of collapsed to one total.
-- See the app-side design discussion the same day for the "simplified vs
-- pairwise" balance display split this feeds.
-- ============================================================

-- pairwise_balances: net balance between every two members who've actually
-- shared money in a group, one row per unordered pair. Convention: balance is
-- how much member_b (the row's higher member id) owes member_a (the lower
-- id) — negative means member_a owes member_b instead. Every expense
-- (settlement or real split — see group_balances' remarks) contributes one
-- edge per non-payer share-holder: they owe the payer their share, same
-- "payer is owed, share-holders owe" convention as expense_payer_net/
-- expense_share_net above.
create view public.pairwise_balances
with (security_invoker = true) as
with edges as (
  select e.group_id, es.member_id as debtor_id, e.paid_by_member_id as creditor_id, es.share_amount_in_group_currency as amount
  from expense_shares es
  join expenses e on e.id = es.expense_id
  where e.group_id is not null and es.member_id <> e.paid_by_member_id
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

-- ============================================================
-- create_group — added 2026-08-28. SupabaseGroupsRepository.CreateAsync used
-- to do three sequential client-side inserts (groups, then members, then
-- group_members) with no way to undo earlier ones if a later call failed —
-- Postgrest has no client-side transaction API, so a failure on insert #2 or
-- #3 left a group behind with no members, invisible to everyone including
-- its own creator. Same fix shape as redeem_invite() below: move the whole
-- multi-step write into one Postgres function, which runs as a single
-- transaction — if any statement fails, all of it rolls back.
-- Every individual insert here is already permitted to the calling user
-- under the RLS policies at the time (see "insert groups"/"insert members"/
-- "group members can add members" above — the last one is phantom-only since
-- 2026-09-17, so the creator's own row now genuinely depends on this running
-- as security definer) — atomicity, not a permission gap,
-- was the original reason for wrapping this in a function. It still needs
-- `security definer`, though: the `auth.users` lookup below (for the
-- creator's email, same as redeem_invite() does) is a plain table-grant
-- issue, not RLS — `authenticated` has no SELECT grant on auth.users at
-- all, so that sub-select fails with 42501 unless the function runs as its
-- owner. Same fix shape as redeem_invite(); the query itself still only
-- ever reads the caller's own row (`id = auth.uid()`), so this doesn't
-- widen access to any other tables/rows the way granting SELECT on
-- auth.users directly to `authenticated` would.
-- Run this against the live project the same way every other block in this
-- file has needed to be — it isn't applied automatically.
--
-- p_currency added 2026-09-05 (/MULTI_CURRENCY_PLAN.md Milestone 3) —
-- required, no default, so picking it is always a deliberate choice at
-- creation time rather than something that could fall out of a default (see
-- the plan doc's "Decisions locked" section). groups.currency's own DB check
-- constraint is what actually enforces "must be one of the supported
-- codes" — this function just passes the value through.
-- ============================================================

-- One-account-one-member invariant fixed 2026-09-07 — this function used to
-- unconditionally insert a fresh members row on every call, so an account
-- creating a second/third group ended up with a second/third members row,
-- each with display_name/birth_date unset (ProfilePage only ever edits the
-- oldest one, per GetMyMemberAsync's ORDER BY created_at). Now reuses the
-- account's existing members row (if any) the same way redeem_invite()'s
-- fresh-join branch does below.
create or replace function public.create_group(p_name text, p_currency char(3))
returns uuid
language plpgsql
security definer
set search_path = public
as $$
declare
  v_group_id uuid;
  v_member_id uuid;
begin
  insert into public.groups (name, currency, created_by)
  values (p_name, p_currency, auth.uid())
  returning id into v_group_id;

  select id into v_member_id from public.members where account_id = auth.uid() limit 1;

  if v_member_id is null then
    insert into public.members (account_id, display_name, created_by)
    values (
      auth.uid(),
      coalesce((select email from auth.users where id = auth.uid()), 'New member'),
      auth.uid()
    )
    returning id into v_member_id;
  end if;

  insert into public.group_members (group_id, member_id)
  values (v_group_id, v_member_id)
  on conflict do nothing;

  return v_group_id;
end;
$$;

revoke execute on function public.create_group(text, char) from public, anon;
grant execute on function public.create_group(text, char) to authenticated;

-- ============================================================
-- Leave / transfer ownership / dissolve — added 2026-08-31 (see the app-side
-- design discussion the same day). Entirely additive on top of everything
-- above; run this block once against the already-live project.
--
-- group_members' only existing delete policy was "group creator can remove
-- members" — there was no policy letting a member remove *themselves*, so
-- leaving a group was RLS-impossible, not just missing UI. Originally fixed by
-- adding a second permissive delete policy. Superseded 2026-09-17 (RLS
-- hardening): both direct-delete policies were dropped, and leave_group()/
-- remove_group_member() are the only removal paths, both security definer.
--
-- Separately: expenses/recurring_expenses already go to `group_id is null`
-- (not deleted) when their group is dissolved, by design (ON DELETE SET
-- NULL on group_id) — the ledger survives. But their SELECT policies only
-- granted the `group_id is null` branch to `created_by`, meaning once a
-- group dissolves, only whoever *recorded* each transaction keeps access to
-- it — the actual payer/share-holder participants (who may not be the same
-- account) permanently lose visibility into their own financial history.
-- New additive SELECT policies below extend that
-- branch to any account that's an actual party to the row, not just its
-- recorder. Unscoped rows stay update/delete-restricted to created_by only
-- (unchanged) — deliberately read-only for everyone else once unscoped, so
-- one ex-member can't silently edit a record other ex-members can no longer
-- discuss in-app.
-- ============================================================

-- is_own_member_row: security definer for the same reason as
-- is_group_member()/is_unscoped_expense_party() above — without it, the
-- group_members DELETE policy below would query `members` directly (a plain
-- table reference, subject to members' own RLS), and members' SELECT policy
-- in turn queries `group_members` directly to check shared-group visibility.
-- That's a two-table mutual reference: Postgres's RLS rewriter inlines each
-- policy at the table reference it's currently expanding, and a cycle back
-- to the relation already being expanded (group_members -> members ->
-- group_members) trips "infinite recursion detected in policy for relation
-- group_members" (42P17) even though each individual hop looks like it
-- would terminate — hit live via leave_group()'s final delete. A security
-- definer function's internal query bypasses RLS, so it never triggers
-- members' policy at all, breaking the cycle at this edge.
create or replace function public.is_own_member_row(p_member_id uuid)
returns boolean
language sql
stable
security definer
set search_path = public
as $$
  select exists (select 1 from members where id = p_member_id and account_id = auth.uid());
$$;

-- group_members: there used to be a "members can remove themselves" DELETE
-- policy here (using is_own_member_row(member_id)). Dropped 2026-09-17 (RLS
-- hardening) — a direct delete skipped leave_group()'s balance/creator guards
-- entirely; leave_group() now runs as security definer instead (below).
-- is_own_member_row() is kept only because it still exists live; nothing in
-- this file references it anymore.

-- is_unscoped_expense_party: whether the current account is the payer or a
-- share-holder on a specific unscoped (dissolved-group) expense. Has to be
-- security definer, same reasoning as is_group_member() above — without it,
-- expense_shares' own policy below would query expense_shares from inside
-- its own USING clause to check "is there a share row for me on this
-- expense", which makes Postgres re-evaluate that same policy on the
-- sub-query and recurse infinitely (42P17 "infinite recursion detected in
-- policy for relation expense_shares" — hit live via leave_group() reading
-- group_balances, which sums expense_shares). A security definer function
-- runs as the table owner, which bypasses RLS on the tables it queries
-- internally, so this check doesn't re-trigger the calling policy.
create or replace function public.is_unscoped_expense_party(p_expense_id uuid)
returns boolean
language sql
stable
security definer
set search_path = public
as $$
  select exists (
    select 1 from expenses e
    where e.id = p_expense_id
      and e.group_id is null
      and (
        exists (select 1 from members m where m.id = e.paid_by_member_id and m.account_id = auth.uid())
        or exists (
          select 1 from expense_shares es
          join members m on m.id = es.member_id
          where es.expense_id = e.id and m.account_id = auth.uid()
        )
      )
  );
$$;

-- expenses: unscoped rows are also visible to the payer or any share-holder.
create policy "select unscoped expenses you're a party to" on public.expenses
  for select using (is_unscoped_expense_party(id));

-- expense_shares: same party check, keyed off the parent expense.
create policy "select unscoped shares you're a party to" on public.expense_shares
  for select using (is_unscoped_expense_party(expense_shares.expense_id));

-- leave_group: self-service leave for a non-creator member. The creator can't
-- leave via this path (they'd orphan `created_by` on groups/the visibility
-- fallback above) — they must transfer ownership or dissolve instead, both
-- below.
--
-- security definer since 2026-09-17 (RLS hardening): it used to run as the
-- caller and rely on a "members can remove themselves" DELETE policy, which
-- also let a plain request skip these guards. That policy is gone, so this
-- has to run as owner; every rule it enforces is explicit below and keyed on
-- auth.uid(). Also removes the leaver's RSVPs to this group's events, so they
-- stop receiving that group's event pushes and stop counting in transport
-- totals.
create or replace function public.leave_group(p_group_id uuid)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_member_id uuid;
  v_balance numeric;
begin
  if auth.uid() is null then
    raise exception 'Not signed in';
  end if;

  if exists (select 1 from groups where id = p_group_id and created_by = auth.uid()) then
    raise exception 'The group creator cannot leave directly — transfer ownership or dissolve the group instead';
  end if;

  select m.id into v_member_id
    from members m
    join group_members gm on gm.member_id = m.id
   where m.account_id = auth.uid()
     and gm.group_id = p_group_id
   limit 1;

  if v_member_id is null then
    raise exception 'You are not a member of this group';
  end if;

  select balance into v_balance
    from group_balances
   where group_id = p_group_id and member_id = v_member_id;
  v_balance := coalesce(v_balance, 0);

  if v_balance <> 0 then
    raise exception 'Settle your balance in this group before leaving';
  end if;

  delete from event_attendees ea
   using events e
   where e.id = ea.event_id
     and e.group_id = p_group_id
     and ea.member_id = v_member_id;

  delete from group_members where group_id = p_group_id and member_id = v_member_id;
end;
$$;

revoke execute on function public.leave_group(uuid) from public, anon;
grant execute on function public.leave_group(uuid) to authenticated;

-- transfer_group_ownership: hands `groups.created_by` to another current,
-- claimed (real-account) member. security definer, same reasoning as
-- redeem_invite() — the plain "update own groups" policy has no explicit
-- WITH CHECK, so it implicitly reuses its USING clause (created_by =
-- auth.uid()) as the check too, which would reject the resulting row the
-- instant created_by no longer equals the caller. Rather than juggle
-- multi-policy OR semantics on top of that, this validates everything
-- explicitly and bypasses RLS the same deliberate way redeem_invite() does.
create or replace function public.transfer_group_ownership(p_group_id uuid, p_new_owner_member_id uuid)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_new_owner_account uuid;
begin
  if not exists (select 1 from groups where id = p_group_id and created_by = auth.uid()) then
    raise exception 'Only the current owner can transfer this group';
  end if;

  select account_id into v_new_owner_account from members where id = p_new_owner_member_id;

  if v_new_owner_account is null then
    raise exception 'Ownership can only be transferred to a member with an account';
  end if;

  if not exists (
    select 1 from group_members
    where group_id = p_group_id and member_id = p_new_owner_member_id
  ) then
    raise exception 'That member does not belong to this group';
  end if;

  update groups set created_by = v_new_owner_account where id = p_group_id;
end;
$$;

revoke execute on function public.transfer_group_ownership(uuid, uuid) from public, anon;
grant execute on function public.transfer_group_ownership(uuid, uuid) to authenticated;

-- Dissolve itself needs no new function: `groups`' existing "delete own
-- groups" policy (created_by = auth.uid()) already permits it, and the FK
-- cascade shape already does the right thing — group_members/invites are
-- ON DELETE CASCADE (membership and pending invites vanish), expenses/
-- recurring_expenses are ON DELETE SET NULL (history survives, newly
-- readable by the visibility-widening policies above). A plain
-- `delete from groups where id = ...` is the whole operation; any
-- outstanding-balance warning before calling it is a client-side confirm,
-- not a DB guard, since forcing an entire group to fully settle before its
-- creator can walk away is a much bigger ask than the one-person case
-- leave_group() enforces above.

-- ============================================================
-- remove_group_member — added 2026-08-31, alongside the Members page (see
-- the app-side design discussion the same day). Lets any current group
-- member remove a phantom from the group — deliberately not restricted to
-- the creator, mirroring "group members can add members" above (adding a
-- phantom was already widened past creator-only for the Link flow; leaving
-- removal creator-only while anyone can add would be an odd asymmetry).
-- security definer for the same recursion-avoidance reason as leave_group()
-- and transfer_group_ownership() — running as invoker here would combine
-- group_members/members references in one function body the same way
-- leave_group() originally risked, and this function's checks (phantom-only,
-- balance-zero) are explicit business rules anyway, not something RLS alone
-- expresses. A claimed member can only ever remove themselves (LeaveGroup),
-- never be removed by someone else's action — same "a real account joins by
-- its own action, never someone else's" principle CLAUDE.md documents for
-- adding members.
create or replace function public.remove_group_member(p_group_id uuid, p_member_id uuid)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_target_account uuid;
  v_balance numeric;
begin
  if not is_group_member(p_group_id) then
    raise exception 'You are not a member of this group';
  end if;

  if not exists (
    select 1 from group_members
    where group_id = p_group_id and member_id = p_member_id
  ) then
    raise exception 'That member does not belong to this group';
  end if;

  select account_id into v_target_account from members where id = p_member_id;

  if v_target_account is not null then
    raise exception 'Only a phantom member can be removed this way — a real account must leave on its own';
  end if;

  select balance into v_balance
    from group_balances
   where group_id = p_group_id and member_id = p_member_id;
  v_balance := coalesce(v_balance, 0);

  if v_balance <> 0 then
    raise exception 'Settle this member''s balance before removing them';
  end if;

  delete from group_members where group_id = p_group_id and member_id = p_member_id;
end;
$$;

revoke execute on function public.remove_group_member(uuid, uuid) from public, anon;
grant execute on function public.remove_group_member(uuid, uuid) to authenticated;

-- ============================================================
-- Member aliases + reserved avatar column — added 2026-08-31 (see the
-- app-side design discussion the same day). Both additive.
-- ============================================================

-- member_aliases: a private, per-account nickname override for how a member
-- is displayed — e.g. seeing "Dave" instead of "David Kim". Deliberately
-- keyed off member_id, not an account/auth id, so a phantom (no account at
-- all) can be aliased exactly like a claimed member. Fully self-owned data,
-- same "for all using/with check (owner = auth.uid())" shape as
-- device_tokens above — no other account ever needs to read your aliases.
create table public.member_aliases (
  owner_id uuid not null references auth.users(id) on delete cascade,
  member_id uuid not null references public.members(id) on delete cascade,
  alias text not null,
  primary key (owner_id, member_id)
);

alter table public.member_aliases enable row level security;

create policy "manage your own aliases" on public.member_aliases
  for all using (owner_id = auth.uid()) with check (owner_id = auth.uid());

-- members.avatar_path: reserved the same way payments/expenses reserved
-- `currency` before conversion logic existed (see that block above) — no
-- Storage bucket, upload pipeline, or MediaPicker wiring exists yet for
-- this. That's real new infra (a Storage bucket + policies, plus
-- client.Storage's exact API shape, which — unlike .Rpc()/.From<T>() — is
-- not yet confirmed against this installed Supabase 1.6.0 package) and is
-- deliberately left as a separate follow-up rather than bundled in here.
-- Reserving the column now means Services/MemberDisplay.cs's AvatarUrl
-- already has a real (if always-null-for-now) field to resolve once that
-- follow-up lands, instead of needing a second migration later.
alter table public.members add column avatar_path text;

-- ============================================================
-- Avatar photos — added 2026-08-31, following up on the reserved
-- members.avatar_path column above. client.Storage's API shape (Upload/
-- GetPublicUrl/Remove) is now confirmed against a real build of the
-- installed Supabase 1.6.0 package (reflection probe against
-- Supabase.Storage 2.7.0, not docs) — see the app-side design discussion.
--
-- Deliberately public bucket, unlike the private `receipts` bucket planned
-- in SCOPE.md: an avatar is low-sensitivity (not a financial record), and a
-- public bucket means GetPublicUrl is a pure deterministic string build with
-- no signed-URL expiry/refresh plumbing — MemberDisplay.AvatarUrl stays a
-- plain sync method. Path is `{member_id}/{new guid}.webp` per upload
-- (never overwritten in place) specifically so changing a photo gets a new
-- URL — an overwritten same-path file would leave stale copies in any
-- client-side image cache showing the old photo forever.
--
-- Phantoms deliberately get NO avatar support at all, not just
-- creator-restricted: a profile picture is self-presentation, and a phantom
-- has no way to see, object to, or remove whatever anyone else uploads "for"
-- it. This is enforced twice: the storage policy below only matches a
-- claimed member's own account (account_id is null for every phantom, so
-- they're excluded automatically, no extra check needed), and the check
-- constraint on members makes it a real database invariant rather than
-- trusting every future code path to respect it — members' own "update
-- members you created or claim yourself" policy would otherwise let a
-- phantom's creator set avatar_path directly, bypassing Storage entirely.
insert into storage.buckets (id, name, public) values ('avatars', 'avatars', true);

create policy "anyone can view avatars" on storage.objects
  for select using (bucket_id = 'avatars');

create policy "manage your own avatar" on storage.objects
  for insert with check (
    bucket_id = 'avatars'
    and exists (
      select 1 from members m
      where m.account_id = auth.uid()
        and m.id::text = (storage.foldername(name))[1]
    )
  );

create policy "delete your own avatar" on storage.objects
  for delete using (
    bucket_id = 'avatars'
    and exists (
      select 1 from members m
      where m.account_id = auth.uid()
        and m.id::text = (storage.foldername(name))[1]
    )
  );

alter table public.members add constraint avatar_requires_account
  check (avatar_path is null or account_id is not null);

-- ============================================================
-- Receipts — added 2026-08-31 (see SCOPE.md's "Supporting infra" note and
-- the app-side design discussion the same day). expenses.receipt_path was
-- already reserved (Phase 1 additions block above); this is the Storage
-- bucket + policies to actually back it. Private, unlike the public
-- `avatars` bucket — a receipt is a financial document, not
-- self-presentation, so it needs real access control, not just an
-- unguessable path.
--
-- Path is `{group_id}/{guid}.webp`, scoped by group rather than by the
-- specific expense it'll attach to — deliberately, so a photo can be
-- captured/uploaded from Add Expense before that expense has been saved at
-- all (no expense id exists yet to scope a policy against, unlike avatars'
-- claimed-member-always-has-a-row case). Scoping storage RLS by group
-- membership directly (is_group_member(), no join to `expenses`) sidesteps
-- that chicken-and-egg entirely. An upload that never ends up attached (the
-- Add Expense flow gets cancelled) is exactly the "orphaned receipt" case
-- SCOPE.md's cleanup Edge Function already expects to purge after 3 months
-- — not a new failure mode this design introduces.
insert into storage.buckets (id, name, public) values ('receipts', 'receipts', false);

create policy "group members can view receipts" on storage.objects
  for select using (
    bucket_id = 'receipts'
    and is_group_member((storage.foldername(name))[1]::uuid)
  );

create policy "group members can upload receipts" on storage.objects
  for insert with check (
    bucket_id = 'receipts'
    and is_group_member((storage.foldername(name))[1]::uuid)
  );

create policy "group members can delete receipts" on storage.objects
  for delete using (
    bucket_id = 'receipts'
    and is_group_member((storage.foldername(name))[1]::uuid)
  );

-- ============================================================
-- materialize_recurring_expenses — added 2026-08-31, the pg_cron follow-up
-- flagged when recurring_expenses was first built (see "Recurring expenses"
-- remarks). Requires the pg_cron extension enabled on the project (Database
-- -> Extensions in the dashboard, or `create extension pg_cron;`) before
-- the cron.schedule() call at the bottom of this block will succeed.
--
-- Scans recurring_expenses for due templates and inserts real
-- expenses/expense_shares rows for every missed occurrence since
-- last_processed_date — not just the next one — capped at 24 per template
-- per run, so a long-stale template (app unused for months, or the cron job
-- itself paused) can't flood a group's ledger with hundreds of backdated
-- expenses in a single run. The very first occurrence for a brand-new
-- template is always exactly start_date, regardless of frequency — stepping
-- "anchor + one period" from a null last_processed_date would land a weekly
-- template's first occurrence 6 days late.
--
-- Monthly/yearly stepping uses Postgres's native `date + interval`
-- arithmetic, which does NOT clamp to end-of-month — `date '2026-01-31' +
-- interval '1 month'` overflows to 2026-03-03, not 2026-02-28. A template
-- anchored on day 29-31 will drift forward a few days whenever it crosses a
-- shorter month, and never land back on day 31. Deliberately not fixed with
-- extra end-of-month clamping logic — a wrong date on the materialized
-- expense is just as editable as any other expense field, and building real
-- clamping (LEAST(...) against end-of-month) is real complexity for an edge
-- case that's cheap to correct after the fact.
--
-- Never security definer: pg_cron runs a scheduled job as whichever role
-- called cron.schedule() (the SQL editor's role, effectively postgres),
-- which already bypasses RLS entirely — there's no real permission gap to
-- elevate here, unlike leave_group()/transfer_group_ownership()/etc. What
-- *does* matter is the explicit revoke below: without it, every function in
-- `public` is reachable via PostgREST's /rpc/ by any authenticated user by
-- default, and this one has zero caller-scoping (it processes every
-- group's templates, not just the caller's) — a genuine privilege issue if
-- left reachable.
-- ============================================================

create or replace function public.materialize_recurring_expenses()
returns void
language plpgsql
set search_path = public
as $$
declare
  v_template recurring_expenses%rowtype;
  v_occurrence date;
  v_count int;
  v_new_expense_id uuid;
begin
  -- Added 2026-09-17 (RLS hardening): a template whose group was dissolved, or
  -- whose payer / any share-holder is no longer in the group, is deactivated
  -- instead of silently charging people who can't see it — dissolved groups'
  -- templates used to keep materializing unscoped expenses (and pushes) every
  -- day. Reactivating one is the normal pause/resume toggle (SetActiveAsync)
  -- after fixing its participants.
  update recurring_expenses re
     set is_active = false
   where re.is_active
     and (
       re.group_id is null
       or not exists (
         select 1 from group_members gm
         where gm.group_id = re.group_id and gm.member_id = re.paid_by_member_id
       )
       or exists (
         select 1 from recurring_expense_shares res
         where res.recurring_expense_id = re.id
           and not exists (
             select 1 from group_members gm
             where gm.group_id = re.group_id and gm.member_id = res.member_id
           )
       )
     );

  for v_template in
    select * from recurring_expenses
    where is_active
      and group_id is not null
      and start_date <= current_date
      and (last_processed_date is null or last_processed_date < current_date)
  loop
    v_occurrence := v_template.last_processed_date;
    v_count := 0;

    loop
      v_occurrence := case
        when v_occurrence is null then v_template.start_date
        when v_template.frequency = 'daily' then v_occurrence + 1
        when v_template.frequency = 'weekly' then v_occurrence + 7
        when v_template.frequency = 'monthly' then (v_occurrence + interval '1 month')::date
        when v_template.frequency = 'yearly' then (v_occurrence + interval '1 year')::date
      end;

      exit when v_occurrence > current_date or v_count >= 24;

      insert into expenses (group_id, paid_by_member_id, amount, currency, description, category, occurred_at, created_by)
      values (v_template.group_id, v_template.paid_by_member_id, v_template.amount, v_template.currency,
              v_template.description, v_template.category, v_occurrence, v_template.created_by)
      returning id into v_new_expense_id;

      insert into expense_shares (expense_id, member_id, share_amount)
      select v_new_expense_id, member_id, share_amount
      from recurring_expense_shares
      where recurring_expense_id = v_template.id;

      update recurring_expenses set last_processed_date = v_occurrence where id = v_template.id;
      v_count := v_count + 1;
    end loop;
  end loop;
end;
$$;

revoke execute on function public.materialize_recurring_expenses() from public, anon, authenticated;

-- Daily at 8am UTC — well past any reasonable notification-quiet-hours window
-- (chosen specifically to avoid the 3am-wakeup problem a naive middle-of-the-
-- night run would risk once push notifications exist), and daily is already
-- the finest grain any recurring_expenses.frequency needs — the job just
-- checks "is anything due yet," it doesn't need to align with a template's
-- own frequency.
select cron.schedule('materialize-recurring-expenses', '0 8 * * *', $$select public.materialize_recurring_expenses();$$);

-- ============================================================
-- find_expired_receipts — added 2026-08-31, the read-only half of the
-- receipt cleanup infra SCOPE.md's "Supporting infra" describes. Pure SQL,
-- no deletion here on purpose: a plain `DELETE FROM storage.objects` only
-- removes the metadata row, not the underlying stored file — real deletion
-- has to go through the Storage API (`.storage.from(bucket).remove(...)`),
-- which only the paired `cleanup-receipts` Edge Function
-- (supabase/functions/cleanup-receipts/index.ts) can do. This function just
-- answers "what qualifies," the Edge Function decides what to do about it.
--
-- Two categories, both measured off storage.objects.created_at (the file's
-- own upload time — neither expenses nor recurring_expenses stores a
-- separate "receipt attached at" timestamp):
--   'orphan'   — a file in the receipts bucket no expenses.receipt_path
--                points at, uploaded more than 3 months ago.
--   'attached' — a file an expense's receipt_path DOES point at, uploaded
--                more than 6 months ago; the expense itself is untouched,
--                only its receipt_path gets nulled out and the file deleted.
-- Never security definer: called only by the cleanup-receipts Edge
-- Function using the service-role key (which already bypasses RLS
-- entirely), same reasoning as materialize_recurring_expenses. Revoked
-- from anon/authenticated for the same reason too — this has zero
-- caller-scoping and must never be reachable via PostgREST's /rpc/.
-- ============================================================

create or replace function public.find_expired_receipts()
returns table (path text, kind text, expense_id uuid)
language sql
stable
set search_path = public
as $$
  select o.name as path, 'orphan'::text as kind, null::uuid as expense_id
  from storage.objects o
  where o.bucket_id = 'receipts'
    and o.created_at < now() - interval '3 months'
    and not exists (select 1 from expenses e where e.receipt_path = o.name)

  union all

  select e.receipt_path as path, 'attached'::text as kind, e.id as expense_id
  from expenses e
  join storage.objects o on o.bucket_id = 'receipts' and o.name = e.receipt_path
  where e.receipt_path is not null
    and o.created_at < now() - interval '6 months';
$$;

revoke execute on function public.find_expired_receipts() from public, anon, authenticated;

-- Weekly, Sunday 4am UTC — same off-peak reasoning as the 8am recurring-
-- expense job, just weekly since a 3/6-month retention window doesn't need
-- daily attention. Calls the deployed cleanup-receipts Edge Function via
-- pg_net, authenticating with the service-role key stored in Supabase
-- Vault (Integrations -> Vault -> Secrets, name 'service_role_key') rather
-- than hardcoding it here — this file is checked into git. Unlike the rest
-- of this file, the URL below is this specific project's — a fresh project
-- would need its own project ref substituted in before running this block.
select cron.schedule(
  'cleanup-receipts',
  '0 4 * * 0',
  $$
  select net.http_post(
    url := 'https://foepkovwmwyygulbdahv.supabase.co/functions/v1/cleanup-receipts',
    headers := jsonb_build_object(
      'Content-Type', 'application/json',
      'Authorization', 'Bearer ' || (select decrypted_secret from vault.decrypted_secrets where name = 'service_role_key' limit 1)
    ),
    body := '{}'::jsonb
  ) as request_id;
  $$
);

-- ============================================================
-- Profile page (2026-08-31) — birth_date, reserved for future birthday-
-- related features (not built yet). Self-only field: no RLS change
-- needed, the existing "update members you created or claim yourself"
-- policy already covers a claimed member updating their own row.
-- ============================================================
alter table public.members add column birth_date date;

-- ============================================================
-- Push notifications — server-side trigger (2026-09-03), the follow-up to
-- the client-side registration work (device_tokens + IPushRegistrationService,
-- see CLAUDE.md). Scoped to the actual parties to each transaction, not the
-- whole group: paid_by_member_id plus every expense_shares row on it — for a
-- settlement (is_settlement true) that's just the two parties, since it's
-- always exactly one share. A "balance <> 0" check would have been wrong
-- here — that's the group's overall net position, unrelated to who's
-- actually in *this* expense. Excludes whoever recorded the row
-- (created_by) — no one needs to be notified about their own action — and
-- naturally excludes phantom members, since they have no account_id to
-- match a device_tokens row against.
--
-- Plain SQL (not security definer — see materialize_recurring_expenses'
-- remarks on why: called only by the send-push Edge Function using the
-- service-role key, which already bypasses RLS, so there's no permission
-- gap to bridge, only the usual revoke-from-anon/authenticated so it's
-- never reachable via PostgREST's /rpc/ by an ordinary user). Test directly
-- in the SQL editor with a real expense id before trusting the trigger to
-- have wired it up correctly, same as materialize_recurring_expenses/
-- find_expired_receipts were before their own cron jobs went live.
-- ============================================================

-- member_id added 2026-09-16 (BigTextStyle push upgrade) so send-push can look up each
-- recipient's own expense_shares row and show their real "you owe €X" amount instead of sending
-- every recipient the same expense-total body text. Changing a returns-table signature isn't a
-- plain create-or-replace in Postgres — the deployed version must be dropped first:
--   drop function public.expense_notification_recipients(uuid);
-- before re-running this definition, or the CREATE OR REPLACE will fail with 42P13.
--
-- created_by is nullable in practice (a client that omits it on insert — found live 2026-09-16,
-- the webapp's AddExpensePage only set it on edit, never on create) even though it's declared
-- not null above; whatever the actual cause, `<> i.created_by` against a NULL evaluates to NULL
-- in SQL's three-valued logic, not TRUE, which silently excludes every single candidate row from
-- this WHERE clause — a NULL creator meant nobody ever got notified, not "notify everyone since
-- nobody's excluded". Guarding it the same way event_attendee_notification_recipients already
-- guards p_actor_account_id below.
--
-- Current-members-only since 2026-09-17 (RLS hardening): a payer/share-holder who isn't (or is no
-- longer) in the expense's group doesn't get pushed that group's name and amounts. An unscoped
-- expense (dissolved group) has no membership to check, so it still notifies its parties.
create or replace function public.expense_notification_recipients(p_expense_id uuid)
returns table (account_id uuid, push_token text, platform text, member_id uuid)
language sql
stable
set search_path = public
as $$
  with involved as (
    select e.paid_by_member_id as member_id, e.created_by, e.group_id
    from expenses e
    where e.id = p_expense_id
    union
    select es.member_id, e.created_by, e.group_id
    from expense_shares es
    join expenses e on e.id = es.expense_id
    where es.expense_id = p_expense_id
  )
  select distinct dt.account_id, dt.push_token, dt.platform, i.member_id
  from involved i
  join members m on m.id = i.member_id
  join device_tokens dt on dt.account_id = m.account_id
  where m.account_id is not null
    and (i.created_by is null or m.account_id <> i.created_by)
    and (
      i.group_id is null
      or exists (
        select 1 from group_members gm
        where gm.group_id = i.group_id and gm.member_id = i.member_id
      )
    );
$$;

revoke execute on function public.expense_notification_recipients(uuid) from public, anon, authenticated;

-- AFTER INSERT trigger, firing the send-push Edge Function via pg_net — same
-- net.http_post + Vault service_role_key pattern the cleanup-receipts cron
-- job already uses, just fired by a row event instead of a schedule.
-- pg_net queues the HTTP call asynchronously and returns immediately, so
-- this never blocks or slows down the actual expense insert, and a
-- send-push failure can never roll back or fail the transaction that
-- triggered it. materialize_recurring_expenses inserting into expenses
-- server-side means a materialized recurring expense fires this too, same
-- as any other expense — deliberately left as-is for v1 rather than
-- suppressed, see CLAUDE.md's recurring-expenses remarks.
--
-- SECURITY DEFINER, unlike materialize_recurring_expenses/find_expired_receipts
-- above — a real permission gap this time, not just the usual habit: those two
-- only ever run via pg_cron, as whichever role called cron.schedule() (postgres,
-- which already has Vault access). These triggers fire on a plain app-level
-- INSERT done by an ordinary signed-in user through Postgrest, so without
-- SECURITY DEFINER the function body runs as `authenticated` — which has no
-- grant on the vault schema at all and fails with `permission denied for
-- schema vault` (42501, hit for real the first time an expense was saved
-- after this trigger went live). The caller never sees the decrypted secret
-- itself, only this function body does, so this isn't a broader elevation
-- than the one specific read it needs.
create or replace function public.notify_new_expense()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  perform net.http_post(
    url := 'https://foepkovwmwyygulbdahv.supabase.co/functions/v1/send-push',
    headers := jsonb_build_object(
      'Content-Type', 'application/json',
      'Authorization', 'Bearer ' || (select decrypted_secret from vault.decrypted_secrets where name = 'service_role_key' limit 1)
    ),
    body := jsonb_build_object('expense_id', new.id)
  );
  return new;
end;
$$;

create trigger expenses_notify_after_insert
  after insert on public.expenses
  for each row execute function public.notify_new_expense();

-- ============================================================
-- Account deletion (2026-09-04)
-- ============================================================
-- `auth.users(id)` was referenced with no ON DELETE action (i.e. RESTRICT) by
-- every `created_by` column below, so deleting an auth user would fail with a
-- foreign-key violation the moment they'd ever created anything. Relaxed to
-- ON DELETE SET NULL — same "row survives, its reference nulls out" treatment
-- already used for expenses/recurring_expenses.group_id when a group
-- dissolves. groups.created_by deliberately keeps its original RESTRICT
-- behavior — delete_account() below explicitly deletes every group the
-- account still owns before the account row itself goes, so no groups row
-- should ever survive with a dangling created_by.
alter table public.members alter column created_by drop not null;
alter table public.members drop constraint members_created_by_fkey;
alter table public.members add constraint members_created_by_fkey
  foreign key (created_by) references auth.users(id) on delete set null;

alter table public.invites alter column created_by drop not null;
alter table public.invites drop constraint invites_created_by_fkey;
alter table public.invites add constraint invites_created_by_fkey
  foreign key (created_by) references auth.users(id) on delete set null;

alter table public.expenses alter column created_by drop not null;
alter table public.expenses drop constraint expenses_created_by_fkey;
alter table public.expenses add constraint expenses_created_by_fkey
  foreign key (created_by) references auth.users(id) on delete set null;

alter table public.recurring_expenses alter column created_by drop not null;
alter table public.recurring_expenses drop constraint recurring_expenses_created_by_fkey;
alter table public.recurring_expenses add constraint recurring_expenses_created_by_fkey
  foreign key (created_by) references auth.users(id) on delete set null;

-- Cleans up this account's app-level data and reports its avatar path (so the
-- delete-account Edge Function knows what Storage file to remove), but does
-- NOT delete the auth.users row itself — that has to go through the Auth
-- Admin API (supabase.auth.admin.deleteUser), which is the only thing that
-- correctly cleans up GoTrue's own internal session/identity tables, not a
-- plain `delete from auth.users`.
--
-- security definer: unlinking members.account_id for a member the caller
-- *claimed* (rather than created themselves) would otherwise fail the plain
-- "update members you created or claim yourself" policy's implicit
-- WITH CHECK (it reuses the USING clause — created_by = auth.uid() or
-- account_id = auth.uid() — which is no longer true once account_id is
-- nulled), same footgun transfer_group_ownership() already works around.
--
-- Known minor limitation, not worth guarding against: if this account ever
-- ended up with more than one members row (the duplicate-row scenario
-- flagged as unconfirmed-but-plausible for GetMyMemberAsync), the `select
-- ... into` below only picks one row's avatar_path arbitrarily, so a second
-- stale avatar file could survive in Storage — same harmless-orphan bucket
-- the receipt-cleanup function already tolerates.
create or replace function public.delete_account()
returns text
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid uuid := auth.uid();
  v_avatar_path text;
  v_blocked_names text;
begin
  select string_agg(g.name, ', ' order by g.name) into v_blocked_names
  from groups g
  where g.created_by = v_uid
    and exists (
      select 1
      from group_members gm
      join members m on m.id = gm.member_id
      where gm.group_id = g.id
        and m.account_id is distinct from v_uid
    );

  if v_blocked_names is not null then
    raise exception 'Transfer ownership or dissolve before deleting your account: %', v_blocked_names;
  end if;

  delete from device_tokens where account_id = v_uid;
  delete from groups where created_by = v_uid;

  select avatar_path into v_avatar_path from members where account_id = v_uid;
  update members set account_id = null, avatar_path = null where account_id = v_uid;

  return v_avatar_path;
end;
$$;

revoke execute on function public.delete_account() from public, anon;
grant execute on function public.delete_account() to authenticated;

-- ============================================================
-- Provision a members row at signup (2026-09-07) — follow-up to the
-- one-account-one-member invariant fix above. create_group()/redeem_invite()
-- stopped *duplicating* members rows, but a members row still only ever got
-- created lazily, the first time an account created or joined a group — a
-- brand-new account with zero groups had no members row at all, so
-- ProfilePage's display name/birthday couldn't be edited until then.
--
-- Fixed with a trigger on auth.users instead of a per-launch client-side
-- check: a check on every app open would mean a wasted network round trip
-- on every single launch for the rest of the account's life, for something
-- that only ever needs to happen once. A trigger fires exactly once, at the
-- one moment it's actually needed, and — unlike wiring this into
-- SupabaseAuthService.SignUpAsync — it catches every account-creation path
-- uniformly (email/password sign-up and both Google sign-in flows) with no
-- risk of missing one, the same "let the database enforce it" preference
-- this file already uses for notify_new_expense/materialize_recurring_
-- expenses/etc.
--
-- auth.uid() is NOT used here (unlike create_group()/redeem_invite()) —
-- this trigger fires from GoTrue's own internal insert into auth.users, not
-- through a PostgREST request, so there's no JWT/request.jwt.claims in
-- scope and auth.uid() would just read null. new.id is the actual account
-- id being created, used directly instead. security definer (owned by
-- postgres, which bypasses RLS in this project the same way every other
-- security definer function here relies on) is required regardless, since
-- there's no authenticated request context for the "insert members" RLS
-- policy's created_by = auth.uid() check to ever pass on its own.
--
-- display_name defaults to the account's email, same fallback create_group()/
-- redeem_invite() already use for a fresh join — ProfilePage's own save is
-- expected to be the first real edit for most people, this is just a
-- non-null placeholder until then.
--
-- Not yet run against the live project or tested against a real signup —
-- creating a trigger on auth.users needs the same "run this once in the SQL
-- editor" treatment as everything else in this file; report back the exact
-- error if a real sign-up doesn't produce a members row.
-- ============================================================

-- display_name/birth_date from sign-up metadata added 2026-09-17
-- (signup_profile_metadata.sql), ahead of turning on "Confirm email": with
-- confirmation on, sign-up returns no session, so the Register page can't
-- update the new member row itself anymore — it sends both as
-- raw_user_meta_data on the sign-up request instead. Only those two keys are
-- read (Google sign-ins set neither, so they keep the email fallback), and a
-- malformed birth_date is ignored rather than raised, since this runs inside
-- account creation and an optional field must never block a signup.
create or replace function public.handle_new_user_member()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
declare
  v_display_name text := nullif(btrim(new.raw_user_meta_data->>'display_name'), '');
  v_birth_date_text text := new.raw_user_meta_data->>'birth_date';
  v_birth_date date;
begin
  if v_birth_date_text ~ '^\d{4}-\d{2}-\d{2}$' then
    begin
      v_birth_date := v_birth_date_text::date;
    exception when others then
      v_birth_date := null;
    end;
  end if;

  insert into public.members (account_id, display_name, birth_date, created_by)
  values (
    new.id,
    coalesce(left(v_display_name, 100), new.email, 'New member'),
    v_birth_date,
    new.id
  );
  return new;
end;
$$;

create trigger on_auth_user_created_provision_member
  after insert on auth.users
  for each row execute function public.handle_new_user_member();

-- ============================================================
-- Signup allow-list (2026-09-07) — added once a web build existed at a real
-- public URL (app.axisapp.aitorsansal.com), making "someone stumbles onto
-- the link and signs in with Google" a real risk for the first time (the
-- MAUI app was never discoverable the same way). This is not the app's
-- long-term intended shape (SCOPE.md still assumes open signup eventually),
-- just a stopgap while it's really only meant for a couple of named people.
--
-- BEFORE INSERT, not AFTER like handle_new_user_member() above — raising an
-- exception here aborts the whole insert (and therefore the account
-- creation itself), rather than letting the account get created and only
-- then failing to provision a members row for it. Fires for every signup
-- path uniformly (email/password and both Google flows), same reasoning
-- handle_new_user_member()'s own remarks give for using a trigger here
-- instead of an app-side check in SupabaseAuthService.
--
-- Deliberately a real table, not a hardcoded list in the function body —
-- adding a friend later is `insert into allowed_signup_emails values
-- ('...')`, no redeploy of anything. RLS enabled with no policies at all:
-- nothing needs to read this except the security definer function below
-- (same "postgres bypasses RLS" reasoning as every other security definer
-- function in this file), so no authenticated/anon policy is needed or
-- wanted — this list should never be readable from the app itself.
-- ============================================================

create table public.allowed_signup_emails (
  email text primary key
);

alter table public.allowed_signup_emails enable row level security;

insert into public.allowed_signup_emails (email) values
  ('aitorsansal@gmail.com');
  -- add each additional person here, e.g.:
  -- ('yourfriend@gmail.com');

create or replace function public.restrict_signup_to_allowlist()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  if not exists (
    select 1 from allowed_signup_emails where email = lower(new.email)
  ) then
    raise exception 'Signups are invite-only right now — ask the app owner to add your email.';
  end if;
  return new;
end;
$$;

create trigger on_auth_user_created_restrict_signup
  before insert on auth.users
  for each row execute function public.restrict_signup_to_allowlist();

-- ============================================================
-- events / event_attendees — Phase 2, Milestone 1 (2026-09-07, see
-- /EVENTS_PLAN.md). Same additive-table shape as recurring_expenses: one
-- row per group event, one row per (event, member) RSVP.
--
-- needs_transport is editable after creation, unlike groups.currency's
-- deliberate lock — an organizer may not know transport will be an issue
-- until people start RSVPing. reminder_sent_at is a mark-processed column
-- for Milestone 5's reminder cron, same idea as recurring_expenses
-- .last_processed_date.
--
-- event_attendees.response is a 3-state (going/maybe/not_going), not a
-- plain yes/no — it drives both the transport headcount and the reminder
-- recipient list. car_status is one tri-state field (none/offering/
-- needs_ride) rather than two booleans, so "offering a ride" and "needs a
-- ride" can never both be true at once. RSVP and car status are coupled at
-- the app layer, not the DB: whenever a write sets response to
-- 'not_going', that same write must also reset car_status/
-- car_offered_seats to 'none'/null, or a declined attendee would keep
-- corrupting the transport shortfall math (see Milestone 3a's repository
-- notes in the plan doc) — there is deliberately no DB trigger for this,
-- since it's a single call site (the RSVP save path), matching this
-- project's general preference for app-level logic over a trigger when
-- there's exactly one writer to coordinate.
--
-- members.car_extra_seats is a per-profile default seat count, named to
-- mean "extra seats beyond the driver" everywhere (DB and UI both) — the
-- original idea's phrasing ("car places = 6" meaning "me + 6") was an
-- off-by-one footgun waiting to happen otherwise.
--
-- created_by on events is nullable with `on delete set null` from the
-- start, unlike members/invites/expenses/recurring_expenses above (which
-- all began `not null` and had to be relaxed later, once account deletion
-- was built — see delete_account()'s remarks) — events didn't exist yet at
-- that point, so there's no reason to reintroduce the same bug just to
-- "match" the older tables.
-- ============================================================

create table public.events (
  id uuid primary key default gen_random_uuid(),
  group_id uuid not null references public.groups(id) on delete cascade,
  title text not null,
  description text,
  location text,
  starts_at timestamptz not null,
  ends_at timestamptz,
  needs_transport boolean not null default false,
  reminder_sent_at timestamptz,
  created_by uuid references auth.users(id) on delete set null,
  created_at timestamptz not null default now()
);

create table public.event_attendees (
  event_id uuid not null references public.events(id) on delete cascade,
  member_id uuid not null references public.members(id) on delete cascade,
  response text not null default 'going'
    check (response in ('going', 'maybe', 'not_going')),
  car_status text not null default 'none'
    check (car_status in ('none', 'offering', 'needs_ride')),
  car_offered_seats int,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  primary key (event_id, member_id)
);

alter table public.members add column car_extra_seats int;

-- expenses.event_id — optional link to the event an expense was booked from
-- (see event_expenses.sql, added after events already existed live). On
-- delete set null, same "the expense survives, only its reference nulls
-- out" treatment expenses.group_id already gets when a group dissolves. The
-- participant set for an event-linked expense is a snapshot taken by
-- AddExpenseViewModel at add/edit time from that event's current "going"
-- attendees, not re-derived live from event_id on every read — see
-- CLAUDE.md's event-expenses design discussion.
alter table public.expenses add column event_id uuid references public.events(id) on delete set null;
create index on public.expenses (event_id);

-- events(group_id, starts_at): the grouped-by-date list (Milestone 3b) and
-- the reminder cron scan (Milestone 5) both filter/sort on this.
create index on public.events (group_id, starts_at);
-- event_attendees(event_id): attendee lookups per event, including the
-- transport aggregate (Milestone 4) and the notification recipient
-- functions (Milestone 5).
create index on public.event_attendees (event_id);

alter table public.events enable row level security;
alter table public.event_attendees enable row level security;

-- events: any current group member can select/insert/update — same shape
-- as expenses/recurring_expenses. Delete is creator-only, a deliberate
-- divergence: an event is more ownership-flavored than an expense (having
-- one you organized deleted out from under you by another member is a
-- worse surprise, compounded by attendees possibly having arranged
-- carpooling around it already) — see the plan doc's "Decisions locked".
create policy "select events in your groups" on public.events
  for select using (is_group_member(group_id));
create policy "insert events in your groups" on public.events
  for insert with check (is_group_member(group_id));
create policy "update events in your groups" on public.events
  for update using (is_group_member(group_id));
create policy "delete own events" on public.events
  for delete using (created_by = auth.uid());

-- Column locks (2026-09-17 RLS hardening). Before this, any member could set
-- created_by = themselves on update and then delete someone else's event —
-- defeating the creator-only delete above — or flip is_birthday. On insert,
-- created_by is always the caller and the birthday/reminder columns start
-- clean; on update, organizer/group/birthday columns are silently restored
-- and a birthday event can't be edited at all. Also fixes duplicate reminders:
-- an edit only clears reminder_sent_at when starts_at actually moved (clients
-- send a fresh Event object with ReminderSentAt null on every edit).
-- References is_birthday/member_id/birthday_notified_at, added further down
-- ("Birthday events") — fine, plpgsql only resolves columns at execution time.
-- Same current_user keying as enforce_group_owner_only_columns(): the
-- birthday/reminder cron jobs run as postgres and aren't affected.
create or replace function public.protect_event_columns()
returns trigger
language plpgsql
as $$
begin
  if current_user not in ('authenticated', 'anon') then
    return new;
  end if;

  if tg_op = 'INSERT' then
    new.created_by := auth.uid();
    new.is_birthday := false;
    new.member_id := null;
    new.reminder_sent_at := null;
    new.birthday_notified_at := null;
    return new;
  end if;

  if old.is_birthday then
    raise exception 'Birthday events can''t be edited';
  end if;

  new.id := old.id;
  new.group_id := old.group_id;
  new.created_by := old.created_by;
  new.created_at := old.created_at;
  new.is_birthday := old.is_birthday;
  new.member_id := old.member_id;
  new.birthday_notified_at := old.birthday_notified_at;
  new.reminder_sent_at := case
    when new.starts_at is distinct from old.starts_at then null
    else old.reminder_sent_at
  end;
  return new;
end;
$$;

create trigger protect_event_columns
  before insert or update on public.events
  for each row execute function public.protect_event_columns();

-- event_attendees: select follows the parent event's visibility. Writes are
-- restricted to your own row AND require you to actually be a member of
-- that event's group — the "own row" check alone isn't enough on its own,
-- since one account's member_id is shared across every group it belongs to
-- (see the one-account-one-member invariant elsewhere in this file); without
-- the group-membership check too, an account could RSVP to an event in a
-- group it was never invited into, just by referencing its own member_id.
create policy "select attendees of visible events" on public.event_attendees
  for select using (
    exists (
      select 1 from events e
      where e.id = event_attendees.event_id
        and is_group_member(e.group_id)
    )
  );
create policy "insert your own rsvp" on public.event_attendees
  for insert with check (
    exists (
      select 1 from events e
      where e.id = event_attendees.event_id
        and is_group_member(e.group_id)
    )
    and exists (
      select 1 from members m
      where m.id = event_attendees.member_id
        and m.account_id = auth.uid()
    )
  );
-- Update requires current membership too, with an explicit WITH CHECK
-- (2026-09-17 RLS hardening). Before, it only checked "this is my member row",
-- so an RSVP could still be edited after leaving the group, or moved onto an
-- event in another group — putting its owner on that event's push recipients.
create policy "update your own rsvp" on public.event_attendees
  for update
  using (
    exists (
      select 1 from events e
      where e.id = event_attendees.event_id
        and is_group_member(e.group_id)
    )
    and exists (
      select 1 from members m
      where m.id = event_attendees.member_id
        and m.account_id = auth.uid()
    )
  )
  with check (
    exists (
      select 1 from events e
      where e.id = event_attendees.event_id
        and is_group_member(e.group_id)
    )
    and exists (
      select 1 from members m
      where m.id = event_attendees.member_id
        and m.account_id = auth.uid()
    )
  );
-- Delete stays own-row only (no membership check): removing your own stale
-- RSVP after leaving is harmless and only cleans up.
create policy "delete your own rsvp" on public.event_attendees
  for delete using (
    exists (
      select 1 from members m
      where m.id = event_attendees.member_id
        and m.account_id = auth.uid()
    )
  );

-- An RSVP's event/member can't be repointed by a client update (2026-09-17).
create or replace function public.protect_rsvp_keys()
returns trigger
language plpgsql
as $$
begin
  if current_user in ('authenticated', 'anon') then
    new.event_id := old.event_id;
    new.member_id := old.member_id;
    new.created_at := old.created_at;
  end if;
  return new;
end;
$$;

create trigger protect_rsvp_keys
  before update on public.event_attendees
  for each row execute function public.protect_rsvp_keys();

-- ============================================================
-- Event notifications — Phase 2, Milestone 5 (2026-09-07, see
-- /EVENTS_PLAN.md). Creation/change/cancellation are immediate triggers;
-- the reminder is a daily pg_cron scan. Reuses the existing send-push Edge
-- Function and AxisFirebaseMessagingService client-side unchanged — the
-- client never branches on the payload's `type` field, only reads
-- title/body/group_id/group_name.
--
-- The cancellation trigger is BEFORE DELETE, not AFTER — a deliberate
-- correction from an earlier draft of this plan, which assumed AFTER
-- DELETE with OLD would be enough. That's fine for OLD's own columns, but
-- not for the recipient list: event_attendees cascade-deletes with its
-- parent events row, and Postgres's internal FK-cascade ordering relative
-- to a user AFTER DELETE trigger on the same table isn't worth depending
-- on. BEFORE DELETE is unambiguous — nothing has cascaded yet. And since
-- pg_net's HTTP delivery is asynchronous regardless of trigger timing (the
-- row will be long gone by the time send-push actually processes the
-- request either way), the recipient list AND the message content are
-- both computed synchronously inside the trigger and embedded directly in
-- the JSON payload — no RPC lookup for the cancelled case, unlike
-- created/changed/reminder which all still have a live row to query when
-- their own (also async) push fires.
-- ============================================================

-- event_notification_recipients: creation push — every current group
-- member minus the creator (mirrors expense_notification_recipients's
-- shape exactly, including the NULL-created_by guard — see that function's remarks).
create or replace function public.event_notification_recipients(p_event_id uuid)
returns table (account_id uuid, push_token text, platform text)
language sql
stable
set search_path = public
as $$
  select distinct dt.account_id, dt.push_token, dt.platform
  from events e
  join group_members gm on gm.group_id = e.group_id
  join members m on m.id = gm.member_id
  join device_tokens dt on dt.account_id = m.account_id
  where e.id = p_event_id
    and m.account_id is not null
    and (e.created_by is null or m.account_id <> e.created_by);
$$;

revoke execute on function public.event_notification_recipients(uuid) from public, anon, authenticated;

-- event_attendee_notification_recipients: change push — every current
-- attendee (any event_attendees row, any response) minus whoever made the
-- edit. Deliberately narrower than the creation set: someone who never
-- RSVP'd at all doesn't need to hear that an event they're not tracking
-- got moved, only people who've actually engaged with it. Joined against
-- group_members since 2026-09-17 (RLS hardening), same for the reminder and
-- cancellation recipients below: an attendee row left behind by someone no
-- longer in the group no longer gets that group's event pushes.
create or replace function public.event_attendee_notification_recipients(p_event_id uuid, p_actor_account_id uuid)
returns table (account_id uuid, push_token text, platform text)
language sql
stable
set search_path = public
as $$
  select distinct dt.account_id, dt.push_token, dt.platform
  from event_attendees ea
  join events e on e.id = ea.event_id
  join group_members gm on gm.group_id = e.group_id and gm.member_id = ea.member_id
  join members m on m.id = ea.member_id
  join device_tokens dt on dt.account_id = m.account_id
  where ea.event_id = p_event_id
    and m.account_id is not null
    and (p_actor_account_id is null or m.account_id <> p_actor_account_id);
$$;

revoke execute on function public.event_attendee_notification_recipients(uuid, uuid) from public, anon, authenticated;

-- event_reminder_recipients: the daily advance-reminder cron's recipient
-- set — going/maybe attendees only (not_going gets no nudge), INCLUDING
-- the creator this time (unlike creation, they need reminding too — they
-- already know they made the event, they don't already know it's tomorrow).
create or replace function public.event_reminder_recipients(p_event_id uuid)
returns table (account_id uuid, push_token text, platform text)
language sql
stable
set search_path = public
as $$
  select distinct dt.account_id, dt.push_token, dt.platform
  from event_attendees ea
  join events e on e.id = ea.event_id
  join group_members gm on gm.group_id = e.group_id and gm.member_id = ea.member_id
  join members m on m.id = ea.member_id
  join device_tokens dt on dt.account_id = m.account_id
  where ea.event_id = p_event_id
    and ea.response in ('going', 'maybe')
    and m.account_id is not null;
$$;

revoke execute on function public.event_reminder_recipients(uuid) from public, anon, authenticated;

-- notify_new_event: AFTER INSERT on events. Same Vault-service-role-key
-- pattern and SECURITY DEFINER reasoning as notify_new_expense — this
-- fires from a plain app-level INSERT by an ordinary signed-in user via
-- Postgrest (role `authenticated`, no grant on the vault schema), so
-- without SECURITY DEFINER it fails with `permission denied for schema
-- vault` (42501) the same way notify_new_expense did before that fix.
create or replace function public.notify_new_event()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  -- Birthday events (see the "Birthday events" section near the end of this file) are
  -- materialized well ahead of the actual date by a daily cron job — without this guard,
  -- every group would get pushed the moment that job first creates the row, which could be
  -- months early. send_birthday_notifications() is the one that actually pushes, on the day.
  if new.is_birthday then
    return new;
  end if;

  perform net.http_post(
    url := 'https://foepkovwmwyygulbdahv.supabase.co/functions/v1/send-push',
    headers := jsonb_build_object(
      'Content-Type', 'application/json',
      'Authorization', 'Bearer ' || (select decrypted_secret from vault.decrypted_secrets where name = 'service_role_key' limit 1)
    ),
    body := jsonb_build_object('event_id', new.id, 'event_type', 'created')
  );
  return new;
end;
$$;

create trigger events_notify_after_insert
  after insert on public.events
  for each row execute function public.notify_new_event();

-- notify_event_changed: AFTER UPDATE on events, firing only when
-- starts_at/ends_at/location actually changed — a description or
-- needs_transport edit stays silent, per /EVENTS_PLAN.md's "Decisions
-- locked" (only the fields that would actually strand or confuse someone
-- who already made plans). auth.uid() here reflects the real caller's JWT
-- regardless of SECURITY DEFINER — that only elevates SQL execution
-- privileges, not what auth.uid() reports.
create or replace function public.notify_event_changed()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  if new.starts_at is distinct from old.starts_at
    or new.ends_at is distinct from old.ends_at
    or new.location is distinct from old.location
  then
    perform net.http_post(
      url := 'https://foepkovwmwyygulbdahv.supabase.co/functions/v1/send-push',
      headers := jsonb_build_object(
        'Content-Type', 'application/json',
        'Authorization', 'Bearer ' || (select decrypted_secret from vault.decrypted_secrets where name = 'service_role_key' limit 1)
      ),
      body := jsonb_build_object('event_id', new.id, 'event_type', 'changed', 'actor_account_id', auth.uid())
    );
  end if;
  return new;
end;
$$;

create trigger events_notify_after_update
  after update on public.events
  for each row execute function public.notify_event_changed();

-- notify_event_cancelled: BEFORE DELETE on events (see this section's
-- header comment for why BEFORE, not AFTER, and why recipients/content
-- are both embedded directly rather than looked up via RPC). Must return
-- old — a BEFORE DELETE trigger that returns null would cancel the delete.
create or replace function public.notify_event_cancelled()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
declare
  v_group_name text;
  v_recipients jsonb;
begin
  select g.name into v_group_name from groups g where g.id = old.group_id;

  select coalesce(jsonb_agg(jsonb_build_object(
    'account_id', dt.account_id,
    'push_token', dt.push_token,
    'platform', dt.platform
  )), '[]'::jsonb)
  into v_recipients
  from event_attendees ea
  join group_members gm on gm.group_id = old.group_id and gm.member_id = ea.member_id
  join members m on m.id = ea.member_id
  join device_tokens dt on dt.account_id = m.account_id
  where ea.event_id = old.id
    and m.account_id is not null
    and (auth.uid() is null or m.account_id <> auth.uid());

  perform net.http_post(
    url := 'https://foepkovwmwyygulbdahv.supabase.co/functions/v1/send-push',
    headers := jsonb_build_object(
      'Content-Type', 'application/json',
      'Authorization', 'Bearer ' || (select decrypted_secret from vault.decrypted_secrets where name = 'service_role_key' limit 1)
    ),
    body := jsonb_build_object(
      'event_type', 'cancelled',
      'title', old.title,
      'group_id', old.group_id,
      'group_name', coalesce(v_group_name, 'your group'),
      'recipients', v_recipients
    )
  );
  return old;
end;
$$;

create trigger events_notify_before_delete
  before delete on public.events
  for each row execute function public.notify_event_cancelled();

-- send_event_reminders: the daily advance-reminder cron job. Window and
-- run time decided 2026-09-07 (see /EVENTS_PLAN.md's Milestone 5 remarks):
-- daily at 9am UTC (distinct from fetch-exchange-rates' 6am and
-- materialize-recurring-expenses' 8am, still a normal-morning time),
-- scanning events starting in the next 24-30 hours that haven't been
-- reminded yet — since this only runs once a day, an exact "24h before"
-- isn't achievable anyway; this gives every event its one reminder
-- somewhere in that 24-30h range, whichever daily run first catches it.
-- reminder_sent_at is stamped immediately after a successful queue so a
-- given event is never reminded twice, same "mark-processed" idea as
-- recurring_expenses.last_processed_date.
--
-- Never SECURITY DEFINER — same reasoning as materialize_recurring_expenses/
-- find_expired_receipts: this only ever runs via pg_cron, as whichever role
-- called cron.schedule() (postgres, which already has Vault access), so
-- there's no permission gap to bridge here the way the trigger-based
-- functions above need one.
create or replace function public.send_event_reminders()
returns void
language plpgsql
set search_path = public
as $$
declare
  v_event record;
begin
  for v_event in
    select id from events
    where starts_at >= now()
      and starts_at < now() + interval '30 hours'
      and reminder_sent_at is null
      and not is_birthday
  loop
    perform net.http_post(
      url := 'https://foepkovwmwyygulbdahv.supabase.co/functions/v1/send-push',
      headers := jsonb_build_object(
        'Content-Type', 'application/json',
        'Authorization', 'Bearer ' || (select decrypted_secret from vault.decrypted_secrets where name = 'service_role_key' limit 1)
      ),
      body := jsonb_build_object('event_id', v_event.id, 'event_type', 'reminder')
    );

    update events set reminder_sent_at = now() where id = v_event.id;
  end loop;
end;
$$;

revoke execute on function public.send_event_reminders() from public, anon, authenticated;

select cron.schedule(
  'send-event-reminders',
  '0 9 * * *',
  $$ select public.send_event_reminders(); $$
);

-- ============================================================
-- Birthday events (2026-09-08) — members.birth_date (added 2026-08-31, see this file's own
-- "Profile page" remarks) was explicitly reserved for this. Rather than a separate Birthdays
-- surface, a member's birthday shows up as a real row in the same `events` table everyone
-- already sees in the Upcoming/Past list — no RSVP, no transport, both meaningless for a
-- birthday (there's no "place to go").
--
-- is_birthday distinguishes these from a normal user-created event (same shape as
-- expenses.is_settlement); member_id is whose birthday it is. created_by is deliberately left
-- null on every birthday event, not set to that member's own account — see the discussion that
-- led here: setting it would hand that one person a working "delete own event" button via the
-- existing generic policy, except the next day's materialize_birthday_events() run would just
-- recreate it (birth_date unchanged), making delete look like it silently failed. Leaving
-- created_by null means the existing "delete own events" policy (created_by = auth.uid())
-- already blocks everyone from deleting it, for free, with no new RLS. The looser
-- "update events in your groups" policy (any group member, not creator-gated) is NOT locked
-- down at the DB layer for this — the app simply never exposes an edit entry point for a
-- birthday row (see GroupEventsViewModel.OpenEvent's guard), which is enough for a friends app
-- but is a soft protection, not a hard one; flagging it rather than pretending otherwise.
--
-- Two separate daily cron jobs, deliberately not one, because they answer different questions:
--   - materialize_birthday_events(): "does the next occurrence of this birthday exist as a row
--     yet, with the right date?" Runs first (7am UTC), creates it well ahead of the actual date
--     (the same day this feature ships, every existing birth_date gets its next occurrence
--     materialized immediately) so it's visible in Upcoming long before it happens, same as any
--     other future event. Also detects a birth_date edit (the existing next-occurrence row's date
--     no longer matches) and a birth_date/membership removal, deleting the stale row.
--   - send_birthday_notifications(): "did today become someone's birthday?" Runs second
--     (7:30am UTC), scans for is_birthday events whose date is today and pushes then — kept
--     entirely separate from the "day-before" reminder job below for a concrete reason: that job
--     (send_event_reminders) scans ALL events with no is_birthday filter and unconditionally
--     stamps reminder_sent_at + fires a push the moment a birthday event enters its 24-30h
--     window, regardless of whether it has real attendees (it never checks). Reusing
--     reminder_sent_at as this feature's own "already notified" marker would have let that job
--     silently consume the flag with a useless empty-recipient push the day before, and this
--     job would then see it already set and skip sending the real one on the actual day — hence
--     both the `and not is_birthday` filter added to send_event_reminders above AND a dedicated
--     birthday_notified_at column here, not a shared one.
--
-- notify_new_event (above) is guarded to skip birthday-event inserts — the AFTER INSERT trigger
-- fires unconditionally on ANY insert into events regardless of who/what issued it, including
-- this cron's own inserts, so without that guard the whole group would get pushed the moment
-- materialize_birthday_events() first creates the row (possibly months early), not on the day.
--
-- Deliberately out of scope for this pass (by explicit user choice): phantom members never get
-- a birthday event — only a claimed member can set their own birth_date (ProfilePage), and
-- nobody edits a phantom's profile on their behalf. A phantom's birth_date simply stays null
-- forever unless/until it's ever claimed.
-- ============================================================

alter table public.events add column is_birthday boolean not null default false;
alter table public.events add column member_id uuid references public.members(id) on delete cascade;
alter table public.events add column birthday_notified_at timestamptz;

-- event_birthday_notification_recipients: every current group member with a device token,
-- minus the member whose birthday it is (via events.member_id) — they don't need telling about
-- their own day. Mirrors event_notification_recipients's shape, just excluding by member_id
-- instead of created_by (which is always null on a birthday event — see above).
create or replace function public.event_birthday_notification_recipients(p_event_id uuid)
returns table (account_id uuid, push_token text, platform text)
language sql
stable
set search_path = public
as $$
  select distinct dt.account_id, dt.push_token, dt.platform
  from events e
  join group_members gm on gm.group_id = e.group_id
  join members m on m.id = gm.member_id
  join device_tokens dt on dt.account_id = m.account_id
  where e.id = p_event_id
    and m.account_id is not null
    and m.id is distinct from e.member_id;
$$;

revoke execute on function public.event_birthday_notification_recipients(uuid) from public, anon, authenticated;

-- materialize_birthday_events: for every (member, group) pair where the member has a
-- birth_date, ensures the next occurrence is a real events row with the correct date — inserting
-- if missing, or replacing it if an existing future one's date no longer matches (a birth_date
-- edit on Profile). The day-of-month is clamped to the target year/month's actual last day (a
-- plain make_date() call would throw outright for a Feb 29 birthday in a non-leap year, unlike
-- recurring_expenses' month-end drift elsewhere in this file, which is a display nuance, not a
-- crash — this has to be handled, not just accepted).
--
-- Never SECURITY DEFINER — same reasoning as send_event_reminders/materialize_recurring_expenses:
-- this only ever runs via pg_cron as postgres, which already bypasses RLS, so there's no
-- permission gap to bridge.
create or replace function public.materialize_birthday_events()
returns void
language plpgsql
set search_path = public
as $$
declare
  v_row record;
  v_year int;
  v_month int;
  v_day int;
  v_last_day_of_month int;
  v_target_date date;
  v_existing record;
begin
  for v_row in
    select m.id as member_id, m.display_name, m.birth_date, gm.group_id
    from members m
    join group_members gm on gm.member_id = m.id
    where m.birth_date is not null
  loop
    v_month := extract(month from v_row.birth_date)::int;
    v_day := extract(day from v_row.birth_date)::int;
    v_year := extract(year from current_date)::int;

    v_last_day_of_month := extract(day from (
      date_trunc('month', make_date(v_year, v_month, 1)) + interval '1 month - 1 day'
    ))::int;
    v_target_date := make_date(v_year, v_month, least(v_day, v_last_day_of_month));

    if v_target_date < current_date then
      v_year := v_year + 1;
      v_last_day_of_month := extract(day from (
        date_trunc('month', make_date(v_year, v_month, 1)) + interval '1 month - 1 day'
      ))::int;
      v_target_date := make_date(v_year, v_month, least(v_day, v_last_day_of_month));
    end if;

    select id, starts_at into v_existing
    from events
    where is_birthday and member_id = v_row.member_id and group_id = v_row.group_id
      and starts_at >= now()
    order by starts_at
    limit 1;

    if v_existing.id is null or v_existing.starts_at::date <> v_target_date then
      if v_existing.id is not null then
        delete from events where id = v_existing.id;
      end if;

      insert into events (group_id, title, starts_at, is_birthday, member_id, needs_transport, created_by)
      values (
        v_row.group_id,
        '🎂 ' || v_row.display_name || '''s Birthday',
        v_target_date + time '12:00',
        true,
        v_row.member_id,
        false,
        null
      );
    end if;
  end loop;

  -- Cleanup: a future birthday event whose member no longer has a birth_date (cleared) or is no
  -- longer in that group.
  delete from events e
  where e.is_birthday
    and e.starts_at >= now()
    and not exists (
      select 1 from members m
      join group_members gm on gm.member_id = m.id
      where m.id = e.member_id and gm.group_id = e.group_id and m.birth_date is not null
    );
end;
$$;

revoke execute on function public.materialize_birthday_events() from public, anon, authenticated;

select cron.schedule(
  'materialize-birthday-events',
  '0 7 * * *',
  $$ select public.materialize_birthday_events(); $$
);

-- send_birthday_notifications: the day-of push — see this section's header comment for why this
-- is separate from send_event_reminders and uses its own birthday_notified_at column rather than
-- reminder_sent_at.
create or replace function public.send_birthday_notifications()
returns void
language plpgsql
set search_path = public
as $$
declare
  v_event record;
begin
  for v_event in
    select id from events
    where is_birthday
      and starts_at::date = current_date
      and birthday_notified_at is null
  loop
    perform net.http_post(
      url := 'https://foepkovwmwyygulbdahv.supabase.co/functions/v1/send-push',
      headers := jsonb_build_object(
        'Content-Type', 'application/json',
        'Authorization', 'Bearer ' || (select decrypted_secret from vault.decrypted_secrets where name = 'service_role_key' limit 1)
      ),
      body := jsonb_build_object('event_id', v_event.id, 'event_type', 'birthday')
    );

    update events set birthday_notified_at = now() where id = v_event.id;
  end loop;
end;
$$;

revoke execute on function public.send_birthday_notifications() from public, anon, authenticated;

select cron.schedule(
  'send-birthday-notifications',
  '30 7 * * *',
  $$ select public.send_birthday_notifications(); $$
);

-- ============================================================
-- Calendar subscription feed (2026-09-14) — lets someone with no Axis account (or one who just
-- doesn't want the app) subscribe to a member's events as a read-only feed in Google/Apple/
-- Outlook calendar, via a plain unauthenticated .ics URL. That URL request carries no Supabase
-- session, so the token in it *is* the credential — same "secret address in iCal format" model
-- Google Calendar's own export links use. This table only governs the authenticated app-side
-- flow (viewing/copying/regenerating your own link); the calendar-feed Edge Function looks the
-- token up with the service-role key, deliberately bypassing this table's RLS, because the
-- request that hits it was never authenticated as any account to begin with.
--
-- One row per member (not per group) — a claimed account has exactly one member row reused
-- across every group it belongs to (see the members-vs-accounts design note in CLAUDE.md), so
-- one feed already covers everything that account has been invited to see. "Regenerate" is a
-- plain `update ... set token = default` from the app — the old link stops resolving the instant
-- the token changes, no separate revoked_at/history table needed.
create table public.calendar_subscriptions (
  id uuid primary key default gen_random_uuid(),
  member_id uuid not null unique references public.members(id) on delete cascade,
  token text not null unique default encode(gen_random_bytes(24), 'base64url'),
  created_at timestamptz not null default now(),
  last_accessed_at timestamptz
);

create index on public.calendar_subscriptions (token);

alter table public.calendar_subscriptions enable row level security;

create policy "manage your own calendar subscription" on public.calendar_subscriptions
  for all using (member_id in (select id from members where account_id = auth.uid()))
  with check (member_id in (select id from members where account_id = auth.uid()));

-- ============================================================
-- Atomic expense/recurring-template save (2026-09-17)
-- ============================================================
-- save_expense() / save_recurring_expense(): write a row and its full share
-- list in one Postgres transaction. Before this, every client saved in
-- separate PostgREST calls (row, then shares — the web app even deleted all
-- shares before re-inserting them), so a failure partway through left an
-- expense with missing/partial shares: balances silently treated the
-- unshared remainder as owed to the payer and nobody else.
--
-- Also the first server-side guard on the split itself: shares must be
-- non-empty, each > 0, and sum exactly to the row's amount. Previously only
-- the two client screens enforced that.
--
-- SECURITY DEFINER since 2026-09-17 (supabase/share_writes_via_rpc.sql). It
-- used to run as the caller — atomicity was the only gap, since every step was
-- already permitted by the expense_shares/recurring_expense_shares RLS policies.
-- Those direct write policies are gone now (see their tables above for why), so
-- there's no policy left for this to run under, and it genuinely needs to run as
-- owner.
--
-- The catch, and the reason the guards below exist: a security definer caller
-- has current_user = the function owner, so every trigger keyed on
-- `current_user in ('authenticated','anon')` now SKIPS this function —
-- enforce_payer_in_group and enforce_share_member_in_group included. Both are
-- re-stated explicitly below with identical semantics (membership only checked
-- for someone being ADDED or CHANGED, so an old expense whose participant has
-- since left stays editable), as is the dropped RLS policies' own rule.
-- protect_expense_columns is skipped too, but this function never writes
-- id/group_id/created_by/created_at on update and sets created_by = auth.uid()
-- on insert, so its guarantees are unchanged. Triggers that still fire normally:
-- both *_snapshot_currency_conversion, record_expense_history,
-- sync_expense_converted_total.
--
-- The escalation risk this introduces is the UPDATE path: with RLS bypassed,
-- `update expenses ... where id = v_id` would let any signed-in account edit ANY
-- expense in ANY group by id. So the update path re-resolves group_id/created_by/
-- paid_by_member_id FROM THE STORED ROW and authorizes against that, never
-- against p_expense's own group_id. Keep it that way.
--
-- Update paths never write created_by/created_at (or a template's
-- last_processed_date/is_active, owned by materialize_recurring_expenses()/
-- SetActiveAsync), so a client can't blank them by sending a fresh object —
-- the footgun that bit Expense edits twice. group_id is also fixed on
-- update: an expense never moves between groups.
--
-- Trigger interplay: expenses_snapshot_currency_conversion runs on the row
-- write before any share is touched; shares always go through
-- `on conflict do update set share_amount`, so share_amount is in the SET
-- list and expense_shares_snapshot_currency_conversion re-runs even when
-- only the currency changed. notify_new_expense's pg_net request is
-- transactional, so a rolled-back save no longer pushes a notification.
--
-- ============================================================

create or replace function public.save_expense(p_expense jsonb, p_shares jsonb)
returns uuid
language plpgsql
security definer
set search_path = public
as $$
declare
  v_id uuid := nullif(p_expense->>'id', '')::uuid;
  v_sent_group_id uuid := nullif(p_expense->>'group_id', '')::uuid;
  v_payer uuid := (p_expense->>'paid_by_member_id')::uuid;
  v_group_id uuid;
  v_created_by uuid;
  v_old_payer uuid;
  v_amount numeric;
  v_share_count int;
  v_share_sum numeric;
begin
  if auth.uid() is null then
    raise exception 'Not signed in';
  end if;

  if p_shares is null or jsonb_typeof(p_shares) <> 'array' or jsonb_array_length(p_shares) = 0 then
    raise exception 'An expense needs at least one share';
  end if;

  if v_id is null then
    v_group_id := v_sent_group_id;
    v_created_by := auth.uid();
  else
    -- Authorize against the STORED row, never against p_expense — this function
    -- bypasses RLS, so trusting the caller's group_id here would let anyone edit
    -- any expense by id.
    select group_id, created_by, paid_by_member_id
      into v_group_id, v_created_by, v_old_payer
      from expenses
     where id = v_id;

    if not found then
      raise exception 'Expense % not found', v_id;
    end if;

    -- Tolerates an omitted group_id (treated as unchanged); rejects a different
    -- one rather than silently ignoring it. An expense never moves between groups.
    if v_sent_group_id is not null and v_sent_group_id is distinct from v_group_id then
      raise exception 'An expense cannot move between groups';
    end if;
  end if;

  -- Replaces the dropped "insert/update expenses in your groups" RLS policies,
  -- including their unscoped (dissolved-group) creator-only branch.
  if v_group_id is not null then
    if not is_group_member(v_group_id) then
      raise exception 'You are not a member of this group';
    end if;
  elsif v_created_by is distinct from auth.uid() then
    raise exception 'You can only edit expenses you created';
  end if;

  -- Replaces enforce_payer_in_group (skipped now this runs as definer), same
  -- "only when being set or changed" rule.
  if v_group_id is not null
     and (v_id is null or v_payer is distinct from v_old_payer)
     and not exists (
       select 1 from group_members
        where group_id = v_group_id and member_id = v_payer
     ) then
    raise exception 'The payer must be a member of this group';
  end if;

  -- Replaces enforce_share_member_in_group, same rule: only someone being ADDED
  -- to the split has to be a current member, so an old expense whose participant
  -- has since left the group stays correctable. A null/garbage member_id also
  -- lands here rather than reaching the insert.
  if v_group_id is not null and exists (
    select 1
      from jsonb_array_elements(p_shares) s
     where not exists (
             select 1 from expense_shares es
              where es.expense_id = v_id
                and es.member_id = (s->>'member_id')::uuid
           )
       and not exists (
             select 1 from group_members gm
              where gm.group_id = v_group_id
                and gm.member_id = (s->>'member_id')::uuid
           )
  ) then
    raise exception 'Everyone in the split must be a member of this group';
  end if;

  if v_id is null then
    insert into expenses (
      group_id, paid_by_member_id, amount, currency, description, category,
      occurred_at, receipt_path, is_settlement, event_id, created_by
    ) values (
      v_group_id,
      v_payer,
      (p_expense->>'amount')::numeric,
      p_expense->>'currency',
      coalesce(p_expense->>'description', ''),
      coalesce(p_expense->>'category', ''),
      coalesce((p_expense->>'occurred_at')::timestamptz, now()),
      nullif(p_expense->>'receipt_path', ''),
      coalesce((p_expense->>'is_settlement')::boolean, false),
      nullif(p_expense->>'event_id', '')::uuid,
      auth.uid()
    )
    returning id into v_id;
  else
    update expenses set
      paid_by_member_id = v_payer,
      amount = (p_expense->>'amount')::numeric,
      currency = p_expense->>'currency',
      description = coalesce(p_expense->>'description', ''),
      category = coalesce(p_expense->>'category', ''),
      occurred_at = coalesce((p_expense->>'occurred_at')::timestamptz, occurred_at),
      receipt_path = nullif(p_expense->>'receipt_path', ''),
      is_settlement = coalesce((p_expense->>'is_settlement')::boolean, is_settlement),
      event_id = nullif(p_expense->>'event_id', '')::uuid
    where id = v_id;

    if not found then
      raise exception 'Expense % not found', v_id;
    end if;

    delete from expense_shares
    where expense_id = v_id
      and member_id not in (select (s->>'member_id')::uuid from jsonb_array_elements(p_shares) s);
  end if;

  insert into expense_shares (expense_id, member_id, share_amount)
  select v_id, (s->>'member_id')::uuid, (s->>'share_amount')::numeric
  from jsonb_array_elements(p_shares) s
  on conflict (expense_id, member_id) do update set share_amount = excluded.share_amount;

  select amount into v_amount from expenses where id = v_id;
  select count(*), coalesce(sum(share_amount), 0) into v_share_count, v_share_sum
  from expense_shares where expense_id = v_id;

  if exists (select 1 from expense_shares where expense_id = v_id and share_amount <= 0) then
    raise exception 'Every share must be greater than 0';
  end if;
  if v_share_sum <> v_amount then
    raise exception 'Shares (%) must add up to the expense amount (%)', v_share_sum, v_amount;
  end if;
  if (select is_settlement from expenses where id = v_id) and v_share_count <> 1 then
    raise exception 'A settlement must have exactly one share';
  end if;

  return v_id;
end;
$$;

revoke execute on function public.save_expense(jsonb, jsonb) from public, anon;
grant execute on function public.save_expense(jsonb, jsonb) to authenticated;

-- Same shape, same reasoning as save_expense above.
create or replace function public.save_recurring_expense(p_template jsonb, p_shares jsonb)
returns uuid
language plpgsql
security definer
set search_path = public
as $$
declare
  v_id uuid := nullif(p_template->>'id', '')::uuid;
  v_sent_group_id uuid := nullif(p_template->>'group_id', '')::uuid;
  v_payer uuid := (p_template->>'paid_by_member_id')::uuid;
  v_group_id uuid;
  v_created_by uuid;
  v_old_payer uuid;
  v_amount numeric;
  v_share_sum numeric;
begin
  if auth.uid() is null then
    raise exception 'Not signed in';
  end if;

  if p_shares is null or jsonb_typeof(p_shares) <> 'array' or jsonb_array_length(p_shares) = 0 then
    raise exception 'A repeating expense needs at least one share';
  end if;

  if v_id is null then
    v_group_id := v_sent_group_id;
    v_created_by := auth.uid();
  else
    select group_id, created_by, paid_by_member_id
      into v_group_id, v_created_by, v_old_payer
      from recurring_expenses
     where id = v_id;

    if not found then
      raise exception 'Repeating expense % not found', v_id;
    end if;

    if v_sent_group_id is not null and v_sent_group_id is distinct from v_group_id then
      raise exception 'A repeating expense cannot move between groups';
    end if;
  end if;

  if v_group_id is not null then
    if not is_group_member(v_group_id) then
      raise exception 'You are not a member of this group';
    end if;
  elsif v_created_by is distinct from auth.uid() then
    raise exception 'You can only edit repeating expenses you created';
  end if;

  if v_group_id is not null
     and (v_id is null or v_payer is distinct from v_old_payer)
     and not exists (
       select 1 from group_members
        where group_id = v_group_id and member_id = v_payer
     ) then
    raise exception 'The payer must be a member of this group';
  end if;

  if v_group_id is not null and exists (
    select 1
      from jsonb_array_elements(p_shares) s
     where not exists (
             select 1 from recurring_expense_shares res
              where res.recurring_expense_id = v_id
                and res.member_id = (s->>'member_id')::uuid
           )
       and not exists (
             select 1 from group_members gm
              where gm.group_id = v_group_id
                and gm.member_id = (s->>'member_id')::uuid
           )
  ) then
    raise exception 'Everyone in the split must be a member of this group';
  end if;

  if v_id is null then
    insert into recurring_expenses (
      group_id, paid_by_member_id, amount, currency, description, category,
      frequency, start_date, created_by
    ) values (
      v_group_id,
      v_payer,
      (p_template->>'amount')::numeric,
      p_template->>'currency',
      coalesce(p_template->>'description', ''),
      coalesce(p_template->>'category', ''),
      p_template->>'frequency',
      (p_template->>'start_date')::date,
      auth.uid()
    )
    returning id into v_id;
  else
    update recurring_expenses set
      paid_by_member_id = v_payer,
      amount = (p_template->>'amount')::numeric,
      currency = p_template->>'currency',
      description = coalesce(p_template->>'description', ''),
      category = coalesce(p_template->>'category', ''),
      frequency = p_template->>'frequency',
      start_date = (p_template->>'start_date')::date
    where id = v_id;

    if not found then
      raise exception 'Repeating expense % not found', v_id;
    end if;

    delete from recurring_expense_shares
    where recurring_expense_id = v_id
      and member_id not in (select (s->>'member_id')::uuid from jsonb_array_elements(p_shares) s);
  end if;

  insert into recurring_expense_shares (recurring_expense_id, member_id, share_amount)
  select v_id, (s->>'member_id')::uuid, (s->>'share_amount')::numeric
  from jsonb_array_elements(p_shares) s
  on conflict (recurring_expense_id, member_id) do update set share_amount = excluded.share_amount;

  select amount into v_amount from recurring_expenses where id = v_id;
  select coalesce(sum(share_amount), 0) into v_share_sum
  from recurring_expense_shares where recurring_expense_id = v_id;

  if exists (select 1 from recurring_expense_shares where recurring_expense_id = v_id and share_amount <= 0) then
    raise exception 'Every share must be greater than 0';
  end if;
  if v_share_sum <> v_amount then
    raise exception 'Shares (%) must add up to the repeating expense amount (%)', v_share_sum, v_amount;
  end if;

  return v_id;
end;
$$;

revoke execute on function public.save_recurring_expense(jsonb, jsonb) from public, anon;
grant execute on function public.save_recurring_expense(jsonb, jsonb) to authenticated;

-- Expense integrity (2026-09-17)
-- ============================================================
-- Follow-up to rls_hardening.sql and atomic_expense_save.sql, for the parts of
-- the audit that sit on the expense tables themselves. RLS decides WHICH rows a
-- group member can write; these triggers decide what a write may contain.
--
-- 1. Server-owned columns on expenses / recurring_expenses. created_by is
--    always the caller on insert (before this, a member could record an
--    expense as someone else, which also hid it from that person's push —
--    expense_notification_recipients skips the creator). id/group_id/
--    created_by/created_at can't change on update, nor a template's
--    last_processed_date (owned by materialize_recurring_expenses()).
-- 2. Converted amounts can't be written directly. The currency-conversion
--    triggers only fire on "update of amount, currency" / "update of
--    share_amount", so an update touching ONLY amount_in_group_currency (or a
--    share's share_amount_in_group_currency) used to rewrite balances with no
--    recomputation. On update, if the source amount/currency didn't change, the
--    converted values are restored. Trigger order matters: Postgres fires
--    same-event row triggers alphabetically, so the *_snapshot_currency_conversion
--    triggers run before these protect_* ones.
-- 3. The payer and every share-holder must be members of the expense's group.
--    Only checked when that person is being set or changed: an existing share
--    or payer who has since left the group stays editable, so old expenses can
--    still be corrected. Upserts (save_expense's "on conflict do update") fire
--    BEFORE INSERT first, so an insert whose (expense, member) row already
--    exists is treated as an update of that existing share.
--
-- All keyed on current_user in ('authenticated','anon'), same pattern as
-- rls_hardening.sql: redeem_invite()'s phantom merge (security definer),
-- materialize_recurring_expenses() (cron, postgres) and Edge Functions
-- (service_role) are unaffected.
--
-- (Applied live via supabase/expense_integrity.sql.)
-- ============================================================


-- ------------------------------------------------------------
-- 1 + 2. expenses / recurring_expenses
-- ------------------------------------------------------------
create or replace function public.protect_expense_columns()
returns trigger
language plpgsql
as $$
begin
  if current_user not in ('authenticated', 'anon') then
    return new;
  end if;

  if tg_op = 'INSERT' then
    new.created_by := auth.uid();
    new.created_at := now();
    if tg_table_name = 'recurring_expenses' then
      new.last_processed_date := null;
    end if;
    return new;
  end if;

  new.id := old.id;
  new.group_id := old.group_id;
  new.created_by := old.created_by;
  new.created_at := old.created_at;

  if tg_table_name = 'recurring_expenses' then
    new.last_processed_date := old.last_processed_date;
  else
    if new.amount = old.amount and new.currency = old.currency then
      new.amount_in_group_currency := old.amount_in_group_currency;
      new.exchange_rate := old.exchange_rate;
    end if;
  end if;

  return new;
end;
$$;

drop trigger if exists protect_expense_columns on public.expenses;
create trigger protect_expense_columns
  before insert or update on public.expenses
  for each row execute function public.protect_expense_columns();

drop trigger if exists protect_expense_columns on public.recurring_expenses;
create trigger protect_expense_columns
  before insert or update on public.recurring_expenses
  for each row execute function public.protect_expense_columns();

-- expense_shares: the share's converted amount only changes through share_amount,
-- and neither key can be repointed. Dead code for app requests since 2026-09-17
-- (there's no direct UPDATE policy on expense_shares anymore), kept as defense in
-- depth if one is ever re-added.
create or replace function public.protect_expense_share_columns()
returns trigger
language plpgsql
as $$
begin
  if current_user not in ('authenticated', 'anon') then
    return new;
  end if;

  new.expense_id := old.expense_id;
  new.member_id := old.member_id;
  if new.share_amount = old.share_amount then
    new.share_amount_in_group_currency := old.share_amount_in_group_currency;
  end if;
  return new;
end;
$$;

drop trigger if exists protect_expense_share_columns on public.expense_shares;
create trigger protect_expense_share_columns
  before update on public.expense_shares
  for each row execute function public.protect_expense_share_columns();

-- ------------------------------------------------------------
-- 3. Payer and share-holders must be group members
-- ------------------------------------------------------------
create or replace function public.enforce_payer_in_group()
returns trigger
language plpgsql
as $$
begin
  if current_user not in ('authenticated', 'anon') or new.group_id is null then
    return new;
  end if;

  if tg_op = 'UPDATE' and new.paid_by_member_id is not distinct from old.paid_by_member_id then
    return new;
  end if;

  if not exists (
    select 1 from public.group_members
    where group_id = new.group_id and member_id = new.paid_by_member_id
  ) then
    raise exception 'The payer must be a member of this group';
  end if;

  return new;
end;
$$;

drop trigger if exists enforce_payer_in_group on public.expenses;
create trigger enforce_payer_in_group
  before insert or update on public.expenses
  for each row execute function public.enforce_payer_in_group();

drop trigger if exists enforce_payer_in_group on public.recurring_expenses;
create trigger enforce_payer_in_group
  before insert or update on public.recurring_expenses
  for each row execute function public.enforce_payer_in_group();

create or replace function public.enforce_share_member_in_group()
returns trigger
language plpgsql
as $$
declare
  v_group_id uuid;
  v_already_exists boolean;
begin
  if current_user not in ('authenticated', 'anon') then
    return new;
  end if;

  if tg_op = 'UPDATE' and new.member_id is not distinct from old.member_id then
    return new;
  end if;

  if tg_table_name = 'expense_shares' then
    select group_id into v_group_id from public.expenses where id = new.expense_id;
    select exists (
      select 1 from public.expense_shares
      where expense_id = new.expense_id and member_id = new.member_id
    ) into v_already_exists;
  else
    select group_id into v_group_id from public.recurring_expenses where id = new.recurring_expense_id;
    select exists (
      select 1 from public.recurring_expense_shares
      where recurring_expense_id = new.recurring_expense_id and member_id = new.member_id
    ) into v_already_exists;
  end if;

  -- An upsert of an existing share (save_expense on an old expense whose
  -- participant has since left) is an update of that share, not a new one.
  if tg_op = 'INSERT' and v_already_exists then
    return new;
  end if;

  if v_group_id is not null and not exists (
    select 1 from public.group_members
    where group_id = v_group_id and member_id = new.member_id
  ) then
    raise exception 'Everyone in the split must be a member of this group';
  end if;

  return new;
end;
$$;

drop trigger if exists enforce_share_member_in_group on public.expense_shares;
create trigger enforce_share_member_in_group
  before insert or update on public.expense_shares
  for each row execute function public.enforce_share_member_in_group();

drop trigger if exists enforce_share_member_in_group on public.recurring_expense_shares;
create trigger enforce_share_member_in_group
  before insert or update on public.recurring_expense_shares
  for each row execute function public.enforce_share_member_in_group();



-- ============================================================
-- Currency integrity + expense history (2026-09-17 audit, SECURITY_AUDIT.md).
--
-- 1. groups.currency is locked once the group has expenses; amount edits keep the
--    original rate (see snapshot_expense_currency_conversion above).
-- 2. expense_history: previous version of an expense (row + shares) on every
--    update and delete. Delete limited to creator, payer or group owner.
-- 3. amount_in_group_currency is kept equal to the sum of the converted shares,
--    so group balances sum to exactly zero despite per-share rounding.
--
-- (Applied live via supabase/currency_integrity.sql.)
-- ============================================================

-- Applies to every role, not just app requests: changing it would leave every
-- stored conversion relative to the old currency.
create or replace function public.enforce_group_currency_locked()
returns trigger
language plpgsql
as $$
begin
  if new.currency is distinct from old.currency
     and exists (select 1 from public.expenses where group_id = old.id) then
    raise exception 'A group''s currency can''t change once it has expenses';
  end if;
  return new;
end;
$$;

drop trigger if exists enforce_group_currency_locked on public.groups;
create trigger enforce_group_currency_locked
  before update of currency on public.groups
  for each row execute function public.enforce_group_currency_locked();


-- ------------------------------------------------------------
-- 2. Expense history + delete permission
-- ------------------------------------------------------------
-- No FK on expense_id/group_id: history outlives the expense and the group.
create table if not exists public.expense_history (
  id bigint generated always as identity primary key,
  expense_id uuid not null,
  group_id uuid,
  action text not null check (action in ('update', 'delete')),
  old_expense jsonb not null,
  old_shares jsonb not null,
  changed_by uuid,
  changed_at timestamptz not null default now()
);

create index if not exists expense_history_expense_id_idx on public.expense_history (expense_id);
create index if not exists expense_history_group_id_idx on public.expense_history (group_id);

alter table public.expense_history enable row level security;

-- Read-only for current group members; rows are only ever written by the trigger below.
drop policy if exists "select expense history in your groups" on public.expense_history;
create policy "select expense history in your groups" on public.expense_history
  for select to authenticated using (group_id is not null and is_group_member(group_id));

-- Security definer: callers have no insert policy on expense_history. auth.uid()
-- still reads the caller's JWT claims, so changed_by is the real person (null for
-- cron / service-role writes).
--
-- Updates: logged AFTER, when save_expense hasn't touched the shares yet, so
-- old_shares is the pre-edit split. save_expense always rewrites the row, so a
-- shares-only edit is still logged. Skipped: updates that only move server-owned
-- columns (the rounding sync below, a group dissolve nulling group_id).
-- Deletes: logged BEFORE, while the cascaded shares still exist.
create or replace function public.record_expense_history()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  if tg_op = 'UPDATE'
     and to_jsonb(old) is distinct from to_jsonb(new)
     and to_jsonb(old) - array['amount_in_group_currency', 'group_id']
         = to_jsonb(new) - array['amount_in_group_currency', 'group_id'] then
    return null;
  end if;

  insert into expense_history (expense_id, group_id, action, old_expense, old_shares, changed_by)
  values (
    old.id,
    old.group_id,
    lower(tg_op),
    to_jsonb(old),
    coalesce((
      select jsonb_agg(to_jsonb(s) order by s.member_id)
      from expense_shares s where s.expense_id = old.id
    ), '[]'::jsonb),
    auth.uid()
  );

  return case when tg_op = 'DELETE' then old else null end;
end;
$$;

revoke execute on function public.record_expense_history() from public, anon, authenticated;

drop trigger if exists record_expense_history_update on public.expenses;
create trigger record_expense_history_update
  after update on public.expenses
  for each row execute function public.record_expense_history();

drop trigger if exists record_expense_history_delete on public.expenses;
create trigger record_expense_history_delete
  before delete on public.expenses
  for each row execute function public.record_expense_history();

-- Raises instead of narrowing the RLS delete policy: a policy would make a
-- refused delete silently affect 0 rows, and both clients would report success.
-- Unscoped expenses are already creator-only through RLS.
create or replace function public.enforce_expense_delete_permission()
returns trigger
language plpgsql
as $$
begin
  if current_user not in ('authenticated', 'anon') or old.group_id is null then
    return old;
  end if;

  if old.created_by = auth.uid()
     or exists (select 1 from public.members where id = old.paid_by_member_id and account_id = auth.uid())
     or exists (select 1 from public.groups where id = old.group_id and created_by = auth.uid()) then
    return old;
  end if;

  raise exception 'Only the person who added or paid this expense, or the group owner, can delete it';
end;
$$;

drop trigger if exists enforce_expense_delete_permission on public.expenses;
create trigger enforce_expense_delete_permission
  before delete on public.expenses
  for each row execute function public.enforce_expense_delete_permission();


-- ------------------------------------------------------------
-- 3. Converted total = sum of converted shares
-- ------------------------------------------------------------
-- Deferred to commit so it sees the final share set (save_expense writes the row
-- first, then the shares one by one). Security definer so its update runs as the
-- owner and isn't reverted by protect_expense_columns. Its own update only sets
-- amount_in_group_currency, which doesn't re-fire the "update of amount, currency"
-- trigger on expenses.
create or replace function public.sync_expense_converted_total()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
declare
  v_expense_id uuid;
  v_total numeric;
begin
  if tg_table_name = 'expenses' then
    v_expense_id := new.id;
  elsif tg_op = 'DELETE' then
    v_expense_id := old.expense_id;
  else
    v_expense_id := new.expense_id;
  end if;

  select sum(share_amount_in_group_currency) into v_total
  from expense_shares where expense_id = v_expense_id;

  if v_total is not null then
    update expenses
       set amount_in_group_currency = v_total
     where id = v_expense_id
       and group_id is not null
       and amount_in_group_currency is distinct from v_total;
  end if;

  return null;
end;
$$;

revoke execute on function public.sync_expense_converted_total() from public, anon, authenticated;

drop trigger if exists sync_expense_converted_total on public.expense_shares;
create constraint trigger sync_expense_converted_total
  after insert or update or delete on public.expense_shares
  deferrable initially deferred
  for each row execute function public.sync_expense_converted_total();

drop trigger if exists sync_expense_converted_total on public.expenses;
create constraint trigger sync_expense_converted_total
  after update of amount, currency on public.expenses
  deferrable initially deferred
  for each row execute function public.sync_expense_converted_total();

