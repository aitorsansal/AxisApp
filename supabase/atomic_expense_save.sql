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
-- Not security definer — every step is already permitted by the existing
-- expenses/expense_shares (and recurring_*) RLS policies for a group
-- member, same reasoning as create_group(): the gap is atomicity, not
-- permission.
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
-- Run once against the live project, after schema.sql. Clients
-- (webapp + MAUI) depend on these functions existing.
-- ============================================================

begin;

create or replace function public.save_expense(p_expense jsonb, p_shares jsonb)
returns uuid
language plpgsql
set search_path = public
as $$
declare
  v_id uuid := nullif(p_expense->>'id', '')::uuid;
  v_amount numeric;
  v_share_count int;
  v_share_sum numeric;
begin
  if p_shares is null or jsonb_typeof(p_shares) <> 'array' or jsonb_array_length(p_shares) = 0 then
    raise exception 'An expense needs at least one share';
  end if;

  if v_id is null then
    insert into expenses (
      group_id, paid_by_member_id, amount, currency, description, category,
      occurred_at, receipt_path, is_settlement, event_id, created_by
    ) values (
      nullif(p_expense->>'group_id', '')::uuid,
      (p_expense->>'paid_by_member_id')::uuid,
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
      paid_by_member_id = (p_expense->>'paid_by_member_id')::uuid,
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

create or replace function public.save_recurring_expense(p_template jsonb, p_shares jsonb)
returns uuid
language plpgsql
set search_path = public
as $$
declare
  v_id uuid := nullif(p_template->>'id', '')::uuid;
  v_amount numeric;
  v_share_sum numeric;
begin
  if p_shares is null or jsonb_typeof(p_shares) <> 'array' or jsonb_array_length(p_shares) = 0 then
    raise exception 'A repeating expense needs at least one share';
  end if;

  if v_id is null then
    insert into recurring_expenses (
      group_id, paid_by_member_id, amount, currency, description, category,
      frequency, start_date, created_by
    ) values (
      nullif(p_template->>'group_id', '')::uuid,
      (p_template->>'paid_by_member_id')::uuid,
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
      paid_by_member_id = (p_template->>'paid_by_member_id')::uuid,
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

commit;
