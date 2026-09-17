-- ============================================================
-- Currency integrity + expense history (2026-09-17 audit, SECURITY_AUDIT.md
-- "Still open" 1-3). Run once in the Supabase SQL editor; folded into schema.sql.
--
-- 1. Editing an expense's amount keeps the rate it was first converted at.
--    snapshot_expense_currency_conversion() only fetches today's rate when the
--    currency itself changes (or on insert). groups.currency is locked once the
--    group has any expense, since every stored conversion is relative to it.
-- 2. expense_history: the previous version of an expense (row + shares, as JSON)
--    on every update and delete, with who did it. Deleting an expense is limited
--    to its creator, its payer, or the group owner.
-- 3. Rounding: amount_in_group_currency is kept equal to the sum of the shares'
--    share_amount_in_group_currency, so the payer's credit matches everyone's
--    debits to the cent and group balances sum to exactly zero (leave_group /
--    remove_group_member compare against 0). Done by a deferred constraint
--    trigger that runs once all of a transaction's share writes are in.
-- ============================================================


-- ------------------------------------------------------------
-- 1. Keep the original rate on amount edits
-- ------------------------------------------------------------
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
  -- Safe because groups.currency can't change once a group has expenses.
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

-- One-off: bring existing expenses in line (runs as the SQL editor's owner role,
-- so protect_expense_columns doesn't revert it; history skips it).
update public.expenses e
   set amount_in_group_currency = s.total
  from (
    select expense_id, sum(share_amount_in_group_currency) as total
    from public.expense_shares group by expense_id
  ) s
 where s.expense_id = e.id
   and e.group_id is not null
   and e.amount_in_group_currency is distinct from s.total;
