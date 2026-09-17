-- Pre-flight for atomic_expense_save.sql (read-only).
-- Lists every expense / recurring template that save_expense() /
-- save_recurring_expense() would refuse to save as-is: no shares, a share
-- <= 0, shares not summing to the amount, or a settlement without exactly
-- one share. Rows here aren't broken by the migration — they just can't be
-- re-saved until their split is fixed.

select 'expense' as kind, e.id, e.group_id, e.description, e.amount,
       coalesce(sum(s.share_amount), 0) as share_sum,
       count(s.member_id) as share_count,
       bool_or(s.share_amount <= 0) as has_non_positive_share,
       e.is_settlement, e.created_at
from public.expenses e
left join public.expense_shares s on s.expense_id = e.id
group by e.id
having count(s.member_id) = 0
    or coalesce(sum(s.share_amount), 0) <> e.amount
    or bool_or(s.share_amount <= 0)
    or (e.is_settlement and count(s.member_id) <> 1)

union all

select 'recurring', r.id, r.group_id, r.description, r.amount,
       coalesce(sum(s.share_amount), 0),
       count(s.member_id),
       bool_or(s.share_amount <= 0),
       false, r.created_at
from public.recurring_expenses r
left join public.recurring_expense_shares s on s.recurring_expense_id = r.id
group by r.id
having count(s.member_id) = 0
    or coalesce(sum(s.share_amount), 0) <> r.amount
    or bool_or(s.share_amount <= 0)

order by created_at desc;
