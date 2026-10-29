-- ============================================================
-- Converted amount limit: reject currency conversions above numeric(12,2)
-- with a stable error, and isolate recurring templates so one failure cannot
-- abort the daily run. Over-limit templates are paused for correction.
-- Run once in the Supabase SQL editor; folded into schema.sql.
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
  -- Check an unconstrained numeric before assigning to the numeric(12,2) field.
  v_converted numeric;
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
    v_converted := round(new.amount * new.exchange_rate, 2);
    if v_converted > 9999999999.99 then
      raise exception using errcode = '22003', message = 'converted_amount_too_large',
        detail = format('Amount %s %s converts to %s (maximum 9999999999.99)', new.amount, new.currency, v_converted);
    end if;
    new.amount_in_group_currency := v_converted;
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
  v_converted := round(new.amount * new.exchange_rate, 2);
  if v_converted > 9999999999.99 then
    raise exception using errcode = '22003', message = 'converted_amount_too_large',
      detail = format('Amount %s %s converts to %s (maximum 9999999999.99)', new.amount, new.currency, v_converted);
  end if;
  new.amount_in_group_currency := v_converted;
  return new;
end;
$$;

create or replace function public.snapshot_expense_share_currency_conversion()
returns trigger
language plpgsql
as $$
declare
  v_rate numeric;
  v_currency char(3);
  -- Check an unconstrained numeric before assigning to the numeric(12,2) field.
  v_converted numeric;
begin
  select exchange_rate, currency into v_rate, v_currency from public.expenses where id = new.expense_id;
  v_converted := round(new.share_amount * coalesce(v_rate, 1), 2);
  if v_converted > 9999999999.99 then
    raise exception using errcode = '22003', message = 'converted_amount_too_large',
      detail = format('Share amount %s %s converts to %s (maximum 9999999999.99)', new.share_amount, v_currency, v_converted);
  end if;
  new.share_amount_in_group_currency := v_converted;
  return new;
end;
$$;

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

  if v_total > 9999999999.99 then
    raise exception using errcode = '22003', message = 'converted_amount_too_large',
      detail = format('Expense %s has converted share total %s (maximum 9999999999.99)', v_expense_id, v_total);
  end if;

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
    begin
      v_occurrence := v_template.last_processed_date;
      v_count := 0;

      loop
        v_occurrence := case
          when v_occurrence is null then v_template.start_date
          when v_template.frequency = 'daily' then v_occurrence + 1
          when v_template.frequency = 'weekly' then v_occurrence + 7
          when v_template.frequency = 'monthly' then (v_template.start_date + make_interval(months =>
            ((extract(year from v_occurrence) - extract(year from v_template.start_date)) * 12
             + extract(month from v_occurrence) - extract(month from v_template.start_date))::int + 1))::date
          when v_template.frequency = 'yearly' then (v_template.start_date + make_interval(years =>
            (extract(year from v_occurrence) - extract(year from v_template.start_date))::int + 1))::date
        end;

        exit when v_occurrence > current_date or v_count >= 24;

        insert into expenses (group_id, paid_by_member_id, amount, currency, description, category, occurred_at, created_by, split_mode)
        values (v_template.group_id, v_template.paid_by_member_id, v_template.amount, v_template.currency,
                v_template.description, v_template.category, v_occurrence, v_template.created_by, v_template.split_mode)
        returning id into v_new_expense_id;

        insert into expense_shares (expense_id, member_id, share_amount)
        select v_new_expense_id, member_id, share_amount
        from recurring_expense_shares
        where recurring_expense_id = v_template.id;

        update recurring_expenses set last_processed_date = v_occurrence where id = v_template.id;
        v_count := v_count + 1;
      end loop;

      -- Flush both same-named constraint triggers inside this template's subtransaction.
      set constraints public.sync_expense_converted_total immediate;
      set constraints public.sync_expense_converted_total deferred;
    exception
      when numeric_value_out_of_range then
        if sqlerrm = 'converted_amount_too_large' then
          update recurring_expenses set is_active = false where id = v_template.id;
          raise warning 'Recurring expense template % deactivated: %', v_template.id, sqlerrm;
        else
          raise warning 'Recurring expense template % failed: %', v_template.id, sqlerrm;
        end if;
      when others then
        -- Other failures may be transient (for example, a missing exchange rate).
        raise warning 'Recurring expense template % failed: %', v_template.id, sqlerrm;
    end;
  end loop;
end;
$$;
