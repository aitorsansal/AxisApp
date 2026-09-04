-- Merge payments into expenses (2026-09-04)
-- ============================================================
-- payments and expenses always had identical balance math for the single-
-- payee case: Payment(payer, payee, amount) === Expense(paid_by=payer,
-- shares=[{payee, amount}]) — this project already used that exact identity
-- to retire recurring_payments in favor of recurring_expenses (see CLAUDE.md's
-- "Recurring expenses" remarks). Keeping the base payments/expenses split
-- going forward bought nothing but doubled maintenance: two RLS policy sets,
-- two notification-trigger pipelines, and a client-side merge-and-sort of two
-- separate lists for one Recent Activity feed. This migration finishes what
-- recurring_payments' retirement started.
--
-- No data migration: this project is still pre-launch (no real user data),
-- so any existing payments rows are simply dropped along with the table
-- rather than backfilled into expenses/expense_shares.
--
-- Run this once against the already-live project, after everything else in
-- schema.sql. Report back the exact error text if any statement fails, same
-- as every other block in this file has needed.
-- ============================================================

-- Wrapped in one transaction: leave_group()/remove_group_member() both read
-- group_balances by name, so the window between the cascade drop and the
-- views being recreated below must never be left half-applied.
begin;

-- Cascades through: the payments table itself (its 4 RLS policies + index +
-- payments_notify_after_insert trigger go with it), group_balances,
-- my_group_balances, pairwise_balances, my_pairwise_balances (all have a
-- real dependency on payments — every one gets dropped transitively), and
-- payment_notification_recipients() (a `language sql` function, which
-- Postgres parses and dependency-tracks at CREATE time, unlike plpgsql).
drop table public.payments cascade;

-- notify_new_payment() has no dependency link to drop automatically — its
-- body only references new.id and the vault, no table — so it survives
-- the cascade above as a dead function unless dropped explicitly. Its
-- trigger (on payments) is already gone via the cascade.
drop function if exists public.notify_new_payment();

-- The one new thing settlements need: a way to tell "I paid you back $20"
-- apart from "I added a $20 dinner" once both are just expenses. Everything
-- else a settlement needs (currency, description, receipt_path staying
-- unused, category staying '') already exists on expenses.
alter table public.expenses add column is_settlement boolean not null default false;

-- ============================================================
-- group_balances, recreated without payment_net — expense_payer_net +
-- expense_share_net alone now cover settlements too, since a settlement is
-- just an expense with one share. Output columns unchanged
-- (group_id, member_id, balance), so nothing downstream needs to change.
-- ============================================================
create view public.group_balances
with (security_invoker = true) as
with expense_payer_net as (
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
  select * from expense_payer_net
  union all
  select * from expense_share_net
) all_deltas
group by group_id, member_id;

create view public.my_group_balances
with (security_invoker = true) as
select gb.group_id, gb.balance
from group_balances gb
join members m on m.id = gb.member_id
where m.account_id = auth.uid();

-- ============================================================
-- pairwise_balances, recreated without the payments-side edge — a
-- settlement expense's own share row already produces the "payee owes
-- payer" edge that unioned-in payments used to add separately (a
-- settlement's paid_by is the discharging party, its one share-holder is
-- who was owed — same direction expense_shares already uses for a normal
-- split). Output columns unchanged.
-- ============================================================
create view public.pairwise_balances
with (security_invoker = true) as
with edges as (
  select e.group_id, es.member_id as debtor_id, e.paid_by_member_id as creditor_id, es.share_amount as amount
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

create view public.my_pairwise_balances
with (security_invoker = true) as
select
  pb.group_id,
  case when m.id = pb.member_a then pb.member_b else pb.member_a end as other_member_id,
  case when m.id = pb.member_a then pb.balance else -pb.balance end as balance
from pairwise_balances pb
join members m on m.account_id = auth.uid() and (m.id = pb.member_a or m.id = pb.member_b);

commit;
