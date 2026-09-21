-- Audit follow-ups (SECURITY_AUDIT.md "Still open" #1, #2, #13, #14, #15)
-- ============================================================
-- Apply by hand in the Supabase SQL editor; already folded into schema.sql.
-- Idempotent (create or replace / drop ... if exists), safe to re-run.
--
-- 1. leave_group() / remove_group_member() also refuse while the member has
--    a non-zero *pairwise* balance with anyone.
--
--    Both used to gate only on the member's group_balances net (against the
--    whole pot). That doesn't imply their pairwise debts are zero: A pays 100
--    split A/B, B pays 100 split B/C -> B's net is +100 -50 -50 = 0, yet B owes
--    A 50 and is owed 50 by C. B could leave, and those two edges survived
--    (pairwise_balances never joins group_members) but became unsettleable,
--    because a settle-up naming B is refused by enforce_payer_in_group /
--    enforce_share_member_in_group. Pairwise is a first-class display mode, so
--    that showed as two permanently dead debts.
--
--    A non-zero net always implies some non-zero pairwise edge (the net is the
--    signed sum of the member's edges), so the net check is kept first with its
--    existing message and the pairwise check only adds the case the net check
--    misses. The message names the counterparties: in Simplified mode the user
--    sees no balance at all, so a bare "settle your balance" would read as a bug.
--
-- 2. expense_history stays readable after the group is dissolved.
--
--    Dissolve sets expenses.group_id = null (ON DELETE SET NULL) while
--    record_expense_history stored old.group_id, and the old read policy
--    required is_group_member(group_id) — group_members cascades away with the
--    group, so the whole group's audit trail became unreadable at exactly the
--    moment members want it. The read policy now also lets the payer or a
--    share-holder of a surviving, unscoped expense read its history
--    (is_unscoped_expense_party, the same rule the expense rows themselves use
--    after a dissolve). Dissolve itself stays unguarded: the app already shows
--    an explicit "this group has unsettled balances" confirm.
--    Residual, accepted: history rows of an expense that was *deleted* before
--    the dissolve have no surviving expense row to key the party check on.
--
-- 13. is_phantom_in_group() is no longer anon-executable. Unlike the other
--    is_* helpers it never consults auth.uid(), so anon could use it as an
--    oracle for "is this member id a phantom in this group" (needs both UUIDs;
--    negligible, but it's the one helper that gave anon something). The other
--    helpers all resolve through auth.uid() and give anon nothing, and revoking
--    them would turn every anon-hit policy into a permission-denied error, so
--    they're left alone.
--
-- 14. allowed_signup_emails.email must already be lower-cased. The signup
--    trigger compares email = lower(new.email), so a row stored as
--    'Friend@Gmail.com' silently never matched (a lockout footgun, not a
--    bypass). Existing rows are normalised first.
--
-- 15. Receipt storage policies no longer raise on a non-UUID first path
--    segment. (storage.foldername(name))[1]::uuid threw 22P02 from inside policy
--    evaluation instead of denying. receipt_folder_group_id() returns null for
--    anything that isn't a UUID, and is_group_member(null) is not true.
-- ============================================================


-- ------------------------------------------------------------
-- 1. leave_group / remove_group_member — pairwise guard
-- ------------------------------------------------------------
create or replace function public.leave_group(p_group_id uuid)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_member_id uuid;
  v_balance numeric;
  v_counterparties text;
begin
  if auth.uid() is null then
    raise exception 'Not signed in';
  end if;

  if exists (select 1 from groups where id = p_group_id and created_by = auth.uid()) then
    raise exception 'The group creator cannot leave directly — transfer ownership or dissolve the group instead';
  end if;

  select m.id into v_member_id
    from members m
    join group_members gm on gm.member_id = m.id
   where m.account_id = auth.uid()
     and gm.group_id = p_group_id
   limit 1;

  if v_member_id is null then
    raise exception 'You are not a member of this group';
  end if;

  select balance into v_balance
    from group_balances
   where group_id = p_group_id and member_id = v_member_id;
  v_balance := coalesce(v_balance, 0);

  if v_balance <> 0 then
    raise exception 'Settle your balance in this group before leaving';
  end if;

  -- Net zero against the pot doesn't mean no individual debts (see header).
  select string_agg(o.display_name, ', ' order by o.display_name) into v_counterparties
    from pairwise_balances pb
    join members o on o.id = case when pb.member_a = v_member_id then pb.member_b else pb.member_a end
   where pb.group_id = p_group_id
     and v_member_id in (pb.member_a, pb.member_b);

  if v_counterparties is not null then
    raise exception 'Your overall balance is zero, but you still have individual debts with: %. Settle them (Detailed view) before leaving', v_counterparties;
  end if;

  delete from event_attendees ea
   using events e
   where e.id = ea.event_id
     and e.group_id = p_group_id
     and ea.member_id = v_member_id;

  delete from group_members where group_id = p_group_id and member_id = v_member_id;
end;
$$;

revoke execute on function public.leave_group(uuid) from public, anon;
grant execute on function public.leave_group(uuid) to authenticated;

create or replace function public.remove_group_member(p_group_id uuid, p_member_id uuid)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_target_account uuid;
  v_balance numeric;
  v_counterparties text;
begin
  if not is_group_member(p_group_id) then
    raise exception 'You are not a member of this group';
  end if;

  if not exists (
    select 1 from group_members
    where group_id = p_group_id and member_id = p_member_id
  ) then
    raise exception 'That member does not belong to this group';
  end if;

  select account_id into v_target_account from members where id = p_member_id;

  if v_target_account is not null then
    raise exception 'Only a phantom member can be removed this way — a real account must leave on its own';
  end if;

  select balance into v_balance
    from group_balances
   where group_id = p_group_id and member_id = p_member_id;
  v_balance := coalesce(v_balance, 0);

  if v_balance <> 0 then
    raise exception 'Settle this member''s balance before removing them';
  end if;

  select string_agg(o.display_name, ', ' order by o.display_name) into v_counterparties
    from pairwise_balances pb
    join members o on o.id = case when pb.member_a = p_member_id then pb.member_b else pb.member_a end
   where pb.group_id = p_group_id
     and p_member_id in (pb.member_a, pb.member_b);

  if v_counterparties is not null then
    raise exception 'This member''s overall balance is zero, but they still have individual debts with: %. Settle them (Detailed view) before removing them', v_counterparties;
  end if;

  delete from group_members where group_id = p_group_id and member_id = p_member_id;
end;
$$;

revoke execute on function public.remove_group_member(uuid, uuid) from public, anon;
grant execute on function public.remove_group_member(uuid, uuid) to authenticated;


-- ------------------------------------------------------------
-- 2. expense_history readable after a dissolve
-- ------------------------------------------------------------
drop policy if exists "select expense history in your groups" on public.expense_history;
create policy "select expense history in your groups" on public.expense_history
  for select to authenticated using (
    (group_id is not null and is_group_member(group_id))
    or is_unscoped_expense_party(expense_id)
  );


-- ------------------------------------------------------------
-- 13. is_phantom_in_group — not anon-executable
-- ------------------------------------------------------------
revoke execute on function public.is_phantom_in_group(uuid, uuid) from public, anon;
grant execute on function public.is_phantom_in_group(uuid, uuid) to authenticated;


-- ------------------------------------------------------------
-- 14. allowed_signup_emails — stored lower-case
-- ------------------------------------------------------------
update public.allowed_signup_emails
   set email = lower(btrim(email))
 where email <> lower(btrim(email));

alter table public.allowed_signup_emails
  drop constraint if exists allowed_signup_emails_email_normalized;
alter table public.allowed_signup_emails
  add constraint allowed_signup_emails_email_normalized check (email = lower(btrim(email)));


-- ------------------------------------------------------------
-- 15. receipts policies — guard the uuid cast
-- ------------------------------------------------------------
-- Pure string function, so it keeps the default grants: the policies below are
-- evaluated for anon storage requests too, and a permission-denied error there
-- would replace a plain deny.
create or replace function public.receipt_folder_group_id(p_name text)
returns uuid
language sql
stable
as $$
  select case
    when (storage.foldername(p_name))[1] ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
      then (storage.foldername(p_name))[1]::uuid
  end;
$$;

drop policy if exists "group members can view receipts" on storage.objects;
create policy "group members can view receipts" on storage.objects
  for select using (
    bucket_id = 'receipts'
    and is_group_member(receipt_folder_group_id(name))
  );

drop policy if exists "group members can upload receipts" on storage.objects;
create policy "group members can upload receipts" on storage.objects
  for insert with check (
    bucket_id = 'receipts'
    and is_group_member(receipt_folder_group_id(name))
  );

drop policy if exists "group members can delete receipts" on storage.objects;
create policy "group members can delete receipts" on storage.objects
  for delete using (
    bucket_id = 'receipts'
    and is_group_member(receipt_folder_group_id(name))
  );
