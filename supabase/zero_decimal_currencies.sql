-- ============================================================
-- Zero-decimal currencies (JPY, KRW, ISK): amounts in these currencies have no minor unit, but
-- every money path assumed two decimals, so a JPY group stored and showed values like 333.33.
-- Columns stay numeric(12,2) (whole yen are stored as 1500.00); what changes is how many
-- decimals a currency is allowed to carry and how conversions round.
--   * currency_minor_units(): the one table of decimals per currency. The web app
--     (webapp/src/lib/currencyUnits.ts) and Android (Models/CurrencyUnits.cs) mirror it, and a
--     test in each suite fails if the three lists drift. HUF and IDR stay at 2 on purpose.
--   * New or changed amounts/shares with more decimals than their currency allows are rejected
--     with the stable message 'amount_precision' (SQLSTATE 22003), which both clients translate.
--     Rows that already hold fractional values are not touched; an UPDATE that leaves the amount
--     alone is not re-checked, so old rows stay readable (re-saving one rewrites its shares, which
--     are checked, so it needs a corrected amount).
--   * Conversions round to the GROUP currency's units, for the expense total and every share.
--     expenses.amount_in_group_currency is still synced to the sum of the converted shares by
--     sync_expense_converted_total(), so a whole-yen total can differ from round(total * rate) by
--     about 1 yen, the same way it can differ by a cent today.
-- Run once in the Supabase SQL editor; folded into schema.sql. Rerunnable (create or replace).
-- ============================================================

create or replace function public.currency_minor_units(p_currency text)
returns int
language sql
immutable
as $$
  select case when p_currency in ('ISK', 'JPY', 'KRW') then 0 else 2 end;
$$;

create or replace function public.assert_amount_precision(p_amount numeric, p_currency text)
returns void
language plpgsql
as $$
begin
  if p_amount is distinct from round(p_amount, public.currency_minor_units(p_currency)) then
    raise exception using errcode = '22003', message = 'amount_precision',
      detail = format('%s has more decimals than %s allows (%s)',
        p_amount, p_currency, public.currency_minor_units(p_currency));
  end if;
end;
$$;

create or replace function public.snapshot_expense_currency_conversion()
returns trigger
language plpgsql
as $$
declare
  v_group_currency char(3);
  v_rates jsonb;
  v_from_rate numeric;
  v_to_rate numeric;
  v_units int;
  -- Check an unconstrained numeric before assigning to the numeric(12,2) field.
  v_converted numeric;
begin
  if tg_op = 'INSERT' or new.amount is distinct from old.amount or new.currency is distinct from old.currency then
    perform public.assert_amount_precision(new.amount, new.currency);
  end if;

  if new.group_id is null then
    -- Unscoped expense (its group was dissolved) — no group currency left
    -- to convert against, and group_balances/pairwise_balances already
    -- exclude group_id is null rows entirely, so this value is unused
    -- beyond keeping the column non-null.
    new.exchange_rate := 1;
    new.amount_in_group_currency := new.amount;
    return new;
  end if;

  select currency into v_group_currency from public.groups where id = new.group_id;
  v_units := public.currency_minor_units(v_group_currency);

  -- Same currency as before: reuse the rate the expense was first converted
  -- at, so correcting an old amount doesn't silently apply today's rate.
  -- Safe because groups.currency can't change once a group has expenses.
  if tg_op = 'UPDATE' and new.currency = old.currency then
    new.exchange_rate := old.exchange_rate;
    v_converted := round(new.amount * new.exchange_rate, v_units);
    if v_converted > 9999999999.99 then
      raise exception using errcode = '22003', message = 'converted_amount_too_large',
        detail = format('Amount %s %s converts to %s (maximum 9999999999.99)', new.amount, new.currency, v_converted);
    end if;
    new.amount_in_group_currency := v_converted;
    return new;
  end if;

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
  v_converted := round(new.amount * new.exchange_rate, v_units);
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
  v_group_currency char(3);
  -- Check an unconstrained numeric before assigning to the numeric(12,2) field.
  v_converted numeric;
begin
  select e.exchange_rate, e.currency, g.currency
    into v_rate, v_currency, v_group_currency
  from public.expenses e
  left join public.groups g on g.id = e.group_id
  where e.id = new.expense_id;

  if tg_op = 'INSERT' or new.share_amount is distinct from old.share_amount then
    perform public.assert_amount_precision(new.share_amount, v_currency);
  end if;

  -- Converted shares are in the group currency; an unscoped expense (no group) keeps its own.
  v_converted := round(new.share_amount * coalesce(v_rate, 1),
                       public.currency_minor_units(coalesce(v_group_currency, v_currency)));
  if v_converted > 9999999999.99 then
    raise exception using errcode = '22003', message = 'converted_amount_too_large',
      detail = format('Share amount %s %s converts to %s (maximum 9999999999.99)', new.share_amount, v_currency, v_converted);
  end if;
  new.share_amount_in_group_currency := v_converted;
  return new;
end;
$$;

-- Recurring templates: same precision rule, so a bad template is refused when it is saved instead
-- of being paused later by the daily materializer.
create or replace function public.enforce_recurring_amount_precision()
returns trigger
language plpgsql
as $$
declare
  v_currency char(3);
begin
  if tg_table_name = 'recurring_expenses' then
    if tg_op = 'INSERT' or new.amount is distinct from old.amount or new.currency is distinct from old.currency then
      perform public.assert_amount_precision(new.amount, new.currency);
    end if;
  elsif tg_op = 'INSERT' or new.share_amount is distinct from old.share_amount then
    select currency into v_currency from public.recurring_expenses where id = new.recurring_expense_id;
    perform public.assert_amount_precision(new.share_amount, v_currency);
  end if;
  return new;
end;
$$;

drop trigger if exists recurring_expenses_amount_precision on public.recurring_expenses;
create trigger recurring_expenses_amount_precision
  before insert or update of amount, currency on public.recurring_expenses
  for each row execute function public.enforce_recurring_amount_precision();

drop trigger if exists recurring_expense_shares_amount_precision on public.recurring_expense_shares;
create trigger recurring_expense_shares_amount_precision
  before insert or update of share_amount on public.recurring_expense_shares
  for each row execute function public.enforce_recurring_amount_precision();

-- Quick check after running (read-only):
--   select public.currency_minor_units('JPY'), public.currency_minor_units('EUR');   -- 0, 2
--   select public.assert_amount_precision(100.50, 'JPY');   -- raises amount_precision
--   select public.assert_amount_precision(100.00, 'JPY');   -- ok
