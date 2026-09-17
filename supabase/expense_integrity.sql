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
-- Safe to run once. Wrapped in a transaction.
-- ============================================================

begin;

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

-- expense_shares: the share's converted amount only changes through share_amount.
create or replace function public.protect_expense_share_columns()
returns trigger
language plpgsql
as $$
begin
  if current_user not in ('authenticated', 'anon') then
    return new;
  end if;

  new.expense_id := old.expense_id;
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

commit;
