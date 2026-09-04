-- Multi-currency, Milestone 1 (2026-09-04) — see /MULTI_CURRENCY_PLAN.md
-- ============================================================
-- Adds a per-group settlement currency, a per-expense currency (already
-- reserved but unused — see schema.sql's original "currency reservation"
-- remarks), and the conversion machinery between them:
--   - groups.currency: picked once at group creation, never editable
--     afterward (enforced by the app, not the DB — see the plan doc).
--   - exchange_rates: a SINGLE-ROW table (see the singleton constraint
--     below) holding the latest daily EUR-pivoted rate snapshot from
--     Frankfurter. Deliberately not one row per day — every expense
--     snapshots its own converted amount at write time (below), so once a
--     row exists, historical rates serve no purpose; only "the latest known
--     rate" is ever needed to compute a *new* snapshot. Written by
--     Milestone 2's daily Edge Function; not yet populated by this script.
--   - expenses.amount_in_group_currency / exchange_rate, and
--     expense_shares.share_amount_in_group_currency: computed once, at
--     insert/update time, by BEFORE triggers — never by the app, and never
--     re-derived later. This is what lets group_balances/pairwise_balances
--     keep doing a plain sum() with zero currency-awareness of their own,
--     and what keeps a balance from silently drifting day to day as
--     exchange rates move, the same "record the value that was true at the
--     time" principle recurring-expense materialization already relies on.
--
-- Until Milestone 2 ships and its cron job runs at least once,
-- exchange_rates has zero rows — any expense whose currency differs from
-- its group's currency will fail to insert/update with an explicit
-- exception (see snapshot_expense_currency_conversion() below) rather than
-- silently treating it as a 1:1 conversion. To test currency conversion
-- before Milestone 2 exists, seed one row by hand, e.g.:
--   insert into exchange_rates (as_of, rates) values (current_date,
--     '{"USD": 1.08, "JPY": 160.0}'::jsonb);
--
-- Run this once against the already-live project, after everything else in
-- schema.sql. Report back the exact error text if any statement fails, same
-- as every other block in this file has needed.
-- ============================================================

begin;

-- The fixed currency list this app supports, tied 1:1 to what Frankfurter
-- (frankfurter.dev, ECB reference rates, free, no API key) actually
-- publishes rates for — verified against the live
-- https://api.frankfurter.dev/v1/currencies response on 2026-09-04, not
-- guessed. Deliberately not a separate `currencies` lookup table — same
-- "small, fixed, developer-maintained list, not stored data" reasoning
-- `categories` was retired in favor of (see schema.sql's "Categories
-- removed" remarks). AppConstants.Currencies (Milestone 3/4) must mirror
-- this exact list.
-- AUD, BRL, CAD, CHF, CNY, CZK, DKK, EUR, GBP, HKD, HUF, IDR, ILS, INR,
-- ISK, JPY, KRW, MXN, MYR, NOK, NZD, PHP, PLN, RON, SEK, SGD, THB, TRY,
-- USD, ZAR (30 total).

alter table public.groups add column currency char(3) not null default 'EUR'
  check (currency in (
    'AUD','BRL','CAD','CHF','CNY','CZK','DKK','EUR','GBP','HKD','HUF','IDR',
    'ILS','INR','ISK','JPY','KRW','MXN','MYR','NOK','NZD','PHP','PLN','RON',
    'SEK','SGD','THB','TRY','USD','ZAR'
  ));

alter table public.expenses add constraint expenses_currency_check
  check (currency in (
    'AUD','BRL','CAD','CHF','CNY','CZK','DKK','EUR','GBP','HKD','HUF','IDR',
    'ILS','INR','ISK','JPY','KRW','MXN','MYR','NOK','NZD','PHP','PLN','RON',
    'SEK','SGD','THB','TRY','USD','ZAR'
  ));

alter table public.recurring_expenses add constraint recurring_expenses_currency_check
  check (currency in (
    'AUD','BRL','CAD','CHF','CNY','CZK','DKK','EUR','GBP','HKD','HUF','IDR',
    'ILS','INR','ISK','JPY','KRW','MXN','MYR','NOK','NZD','PHP','PLN','RON',
    'SEK','SGD','THB','TRY','USD','ZAR'
  ));

-- ============================================================
-- exchange_rates — singleton table (the `id boolean primary key default
-- true check (id)` trick: id can only ever be `true`, and being the
-- primary key, that means at most one row can ever exist). The daily
-- Edge Function (Milestone 2) writes it via delete-then-insert, matching
-- this project's existing "delete then insert, not upsert" idiom for
-- enforcing uniqueness (see SupabaseDeviceTokensRepository.RegisterAsync's
-- own remarks) rather than trusting an ON CONFLICT target.
-- rates is EUR-pivoted (units of X per 1 EUR, matching Frankfurter's
-- native base) and, per Frankfurter's own response shape, does NOT
-- include an "EUR" key — the conversion trigger below treats EUR as
-- implicitly 1 rather than requiring the Edge Function to inject it.
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

-- No insert/update/delete policy for authenticated/anon — only Milestone
-- 2's Edge Function (service-role client, bypasses RLS entirely) ever
-- writes this table.

-- ============================================================
-- Conversion snapshot columns. Nullable on add so existing rows (this
-- project's own test data, all EUR so far) don't block the ALTER, backfilled
-- immediately below as an implicit 1:1 (correct, since every existing
-- group/expense already defaults to EUR), then locked to NOT NULL.
-- ============================================================
alter table public.expenses add column amount_in_group_currency numeric(12,2);
alter table public.expenses add column exchange_rate numeric(18,8) not null default 1;

update public.expenses set amount_in_group_currency = amount
where amount_in_group_currency is null;

alter table public.expenses alter column amount_in_group_currency set not null;

alter table public.expense_shares add column share_amount_in_group_currency numeric(12,2);

update public.expense_shares set share_amount_in_group_currency = share_amount
where share_amount_in_group_currency is null;

alter table public.expense_shares alter column share_amount_in_group_currency set not null;

-- ============================================================
-- snapshot_expense_currency_conversion(): BEFORE INSERT/UPDATE on expenses.
-- Not security definer — the caller already has legitimate SELECT access
-- to both groups (a real group member, per "select expenses in your
-- groups"'s own is_group_member check) and exchange_rates (the
-- authenticated-read policy above), so there's no permission gap to
-- bypass, unlike e.g. leave_group()/transfer_group_ownership().
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

-- ============================================================
-- snapshot_expense_share_currency_conversion(): BEFORE INSERT/UPDATE on
-- expense_shares. Always reads its parent expense's already-computed
-- exchange_rate (set by the trigger above, which has already run by the
-- time a share is inserted/updated — confirmed against
-- SupabaseExpensesRepository.AddAsync/UpdateAsync, both of which await the
-- expense insert/update before touching expense_shares) rather than
-- re-deriving currency codes itself.
-- ============================================================
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

-- ============================================================
-- group_balances / pairwise_balances, recreated to sum the converted
-- columns instead of the raw ones. Output columns unchanged (group_id,
-- member_id, balance), so my_group_balances/my_pairwise_balances (which
-- just re-select from these) and every downstream reader — DebtSimplifier,
-- GroupDetailViewModel.Settle, leave_group()'s balance-zero check,
-- remove_group_member()'s same check — need no changes at all.
-- ============================================================
create or replace view public.group_balances
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

create or replace view public.pairwise_balances
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

commit;
