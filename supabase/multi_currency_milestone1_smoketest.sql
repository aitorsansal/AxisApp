-- Multi-currency, Milestone 1 — smoke test (not part of the schema, throwaway)
-- ============================================================
-- Run PART 1, then PART 2, as two SEPARATE executions in the Supabase SQL
-- editor (select just that block's text, run, read the result, then select
-- and run the other block). Don't run both in one go — Part 2 is *supposed*
-- to fail, and letting that failure land in the same script run as Part 1
-- would abort before Part 1's result ever renders.
--
-- Both parts run inside a transaction that ends in `rollback`, so nothing
-- either one does is kept.
-- ============================================================

-- ============================================================
-- PART 1 — tests A/B/C/D. Sequential statements inside a DO block (not a
-- chained WITH ... CTE — Postgres documents that sibling data-modifying
-- CTEs share one snapshot and cannot see each other's writes to the same
-- target table, which silently no-ops an UPDATE depending on a sibling
-- INSERT's row), writing into a temp table so the results still land in a
-- normal, visible `select` at the end.
-- ============================================================

begin;

delete from exchange_rates;
insert into exchange_rates (as_of, rates) values (current_date, '{"USD": 1.08, "JPY": 160.0}'::jsonb);

create temporary table currency_smoketest_results (
  test text,
  actual text,
  expected text
) on commit drop;

do $$
declare
  v_group_id uuid;
  v_member_id uuid;
  v_creator_id uuid;
  v_expense_id uuid;
  v_rate numeric;
  v_amount numeric;
  v_share_amount numeric;
begin
  select id, created_by into v_group_id, v_creator_id from groups limit 1;
  select member_id into v_member_id from group_members where group_id = v_group_id limit 1;

  -- Test A: 100 USD expense in a EUR group, rate 1 EUR : 1.08 USD.
  insert into expenses (group_id, paid_by_member_id, amount, currency, created_by)
  values (v_group_id, v_member_id, 100.00, 'USD', v_creator_id)
  returning id, exchange_rate, amount_in_group_currency into v_expense_id, v_rate, v_amount;
  insert into currency_smoketest_results values ('A exchange_rate', v_rate::text, '~0.9259');
  insert into currency_smoketest_results values ('A amount_in_group_currency', v_amount::text, '~92.59');

  -- Test B: a 50 USD share of that same expense should convert the same way.
  insert into expense_shares (expense_id, member_id, share_amount)
  values (v_expense_id, v_member_id, 50.00)
  returning share_amount_in_group_currency into v_share_amount;
  insert into currency_smoketest_results values ('B share_amount_in_group_currency', v_share_amount::text, '~46.30');

  -- Test C: editing the amount should re-snapshot against the same rate.
  update expenses set amount = 200.00 where id = v_expense_id
  returning amount_in_group_currency into v_amount;
  insert into currency_smoketest_results values ('C amount_in_group_currency (after edit)', v_amount::text, '~185.19');

  -- Test D: same-currency fast path — EUR expense in a EUR group, exact passthrough.
  insert into expenses (group_id, paid_by_member_id, amount, currency, created_by)
  values (v_group_id, v_member_id, 42.00, 'EUR', v_creator_id)
  returning exchange_rate, amount_in_group_currency into v_rate, v_amount;
  insert into currency_smoketest_results values ('D exchange_rate', v_rate::text, 'exactly 1');
  insert into currency_smoketest_results values ('D amount_in_group_currency', v_amount::text, 'exactly 42.00');
end;
$$;

select * from currency_smoketest_results order by test;

rollback;

-- ============================================================
-- PART 2 — test E. Run this SEPARATELY, after Part 1. With no
-- exchange_rates row at all, this insert MUST fail — the pass condition is
-- seeing a red error in the editor whose message starts with "No exchange
-- rate data available yet". If this succeeds with no error, that's a bug
-- (it means a mismatched currency silently got treated as 1:1).
-- ============================================================

begin;

delete from exchange_rates;

with target as (
  select id as group_id, created_by as creator_id from groups limit 1
),
target_member as (
  select gm.member_id from group_members gm join target t on gm.group_id = t.group_id limit 1
)
insert into expenses (group_id, paid_by_member_id, amount, currency, created_by)
select t.group_id, m.member_id, 10.00, 'USD', t.creator_id
from target t, target_member m;

rollback;
