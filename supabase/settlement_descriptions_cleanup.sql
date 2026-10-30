-- One-off cleanup of generated repayment descriptions (W8/A7). Run in the Supabase SQL editor
-- after both clients stop writing them; safe to rerun.
--
-- Background: an empty repayment note used to be saved as text in the creator's language and
-- from the creator's point of view ("Settlement with Bea" / "Saldar deuda con Bea", and earlier
-- "Settle up" / "Saldar cuenta" from the old settle path and the push notification action).
-- Clients now save an empty description and show each viewer their own localized label, so
-- these stored defaults are cleared to ''. Only settlements are touched, and only exact default
-- shapes: a custom note is left alone unless it literally starts with one of those prefixes, and
-- clearing such a note only changes its title to the generic label.
--
-- Step 1 - look first.
select e.id,
       g.name as group_name,
       e.description,
       e.occurred_at
from public.expenses e
join public.groups g on g.id = e.group_id
where e.is_settlement
  and (e.description ~ '^(Settlement with|Saldar deuda con) .+$'
       or e.description in ('Settle up', 'Saldar cuenta'))
order by g.name, e.occurred_at;

-- Step 2 - clear them. The expense_history trigger is paused so this rewrite doesn't add a
-- "previous version" row for every repayment; the transaction makes the pause atomic.
begin;
alter table public.expenses disable trigger record_expense_history_update;

update public.expenses
set description = ''
where is_settlement
  and (description ~ '^(Settlement with|Saldar deuda con) .+$'
       or description in ('Settle up', 'Saldar cuenta'));

alter table public.expenses enable trigger record_expense_history_update;
commit;
