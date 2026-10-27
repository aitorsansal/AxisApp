-- Run once against the existing database (2026-10-27). Anchors monthly/yearly
-- recurring occurrences on start_date instead of chaining from the previous
-- occurrence, which drifted day-29..31 templates to the 28th permanently.
-- Already-drifted templates self-heal on their next materialization. Same
-- function body as schema.sql.
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

      insert into expenses (group_id, paid_by_member_id, amount, currency, description, category, occurred_at, created_by)
      values (v_template.group_id, v_template.paid_by_member_id, v_template.amount, v_template.currency,
              v_template.description, v_template.category, v_occurrence, v_template.created_by)
      returning id into v_new_expense_id;

      insert into expense_shares (expense_id, member_id, share_amount)
      select v_new_expense_id, member_id, share_amount
      from recurring_expense_shares
      where recurring_expense_id = v_template.id;

      update recurring_expenses set last_processed_date = v_occurrence where id = v_template.id;
      v_count := v_count + 1;
    end loop;
  end loop;
end;
$$;

revoke execute on function public.materialize_recurring_expenses() from public, anon, authenticated;
