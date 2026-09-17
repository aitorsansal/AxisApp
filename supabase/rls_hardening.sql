-- RLS hardening (2026-09-17)
-- ============================================================
-- Fixes from the 2026-09-17 security audit (RLS / SECURITY DEFINER section).
-- Expense-write rules (created_by pinning, payer/share membership, the
-- currency re-conversion trigger) are deliberately NOT here — they build on
-- save_expense()/save_recurring_expense() from atomic_expense_save.sql, which
-- another change owns. Run this after that file.
--
-- Column locks use triggers keyed on current_user in ('authenticated',
-- 'anon') rather than auth.uid() checks: a PostgREST request from the app
-- runs as authenticated, while SECURITY DEFINER functions (redeem_invite,
-- delete_account, transfer_group_ownership), pg_cron jobs and Edge Functions
-- using the service-role key run as their owner / service_role — so the
-- legitimate server-side paths that DO need to change these columns keep
-- working without each one setting a bypass flag. A trigger function that is
-- not itself security definer inherits the caller's current_user, which is
-- what makes this distinction reliable.
--
-- Where a client legitimately sends the whole row back on update (MAUI's
-- Update(model) calls), locked columns are silently restored to their old
-- value instead of raising, so an unchanged-but-present column never breaks a
-- normal edit.
--
-- Safe to run once. Wrapped in a transaction: any failure rolls back all of it.
-- ============================================================

begin;

-- ============================================================
-- 1. members — identity columns can't be changed by a client request
-- ============================================================
-- Before: "update members you created or claim yourself" had no WITH CHECK,
-- so a phantom's creator could set account_id = themselves and inherit every
-- group that phantom was linked into (is_group_member() only looks at
-- account_id). account_id/created_by now only change via redeem_invite() /
-- delete_account().

create or replace function public.protect_member_identity_columns()
returns trigger
language plpgsql
as $$
begin
  if current_user in ('authenticated', 'anon') then
    new.id := old.id;
    new.account_id := old.account_id;
    new.created_by := old.created_by;
    new.created_at := old.created_at;
  end if;
  return new;
end;
$$;

drop trigger if exists protect_member_identity_columns on public.members;
create trigger protect_member_identity_columns
  before update on public.members
  for each row execute function public.protect_member_identity_columns();

-- A phantom's creator edits the phantom; once claimed, only its own account
-- edits it (the creator no longer can).
drop policy if exists "update members you created or claim yourself" on public.members;
create policy "update your own member or a phantom you created" on public.members
  for update
  using ((account_id is null and created_by = auth.uid()) or account_id = auth.uid())
  with check ((account_id is null and created_by = auth.uid()) or account_id = auth.uid());

-- Inserting a member pointing at someone else's account is no longer possible.
drop policy if exists "insert members" on public.members;
create policy "insert phantoms or your own member" on public.members
  for insert with check (
    created_by = auth.uid()
    and (account_id is null or account_id = auth.uid())
  );

-- ============================================================
-- 2. groups — created_by is not member-editable
-- ============================================================
-- Before: any member could PATCH created_by to themselves and then dissolve
-- the group. transfer_group_ownership() (security definer) still can.

create or replace function public.enforce_group_owner_only_columns()
returns trigger
language plpgsql
as $$
begin
  if current_user in ('authenticated', 'anon') then
    new.id := old.id;
    new.created_by := old.created_by;
    new.created_at := old.created_at;

    if (new.name is distinct from old.name or new.currency is distinct from old.currency)
       and auth.uid() is distinct from old.created_by then
      raise exception 'Only the group creator can change name or currency.';
    end if;
  end if;
  return new;
end;
$$;

-- ============================================================
-- 3. group_members — only phantoms can be added directly; removal only via RPCs
-- ============================================================
-- A real account joins only by redeeming an invite itself. A phantom can be
-- linked into a group only if you created it or you already share a group with
-- it — the same visibility the name search already respects, now enforced for
-- a crafted request with a known member id too.

create or replace function public.is_linkable_phantom(p_member_id uuid)
returns boolean
language sql
stable
security definer
set search_path = public
as $$
  select exists (
    select 1
    from members m
    where m.id = p_member_id
      and m.account_id is null
      and (
        m.created_by = auth.uid()
        or exists (
          select 1 from group_members gm
          where gm.member_id = m.id
            and is_group_member(gm.group_id)
        )
      )
  );
$$;

drop policy if exists "group members can add members" on public.group_members;
create policy "group members can add phantoms" on public.group_members
  for insert with check (
    is_group_member(group_id)
    and is_linkable_phantom(member_id)
  );

-- Both apps already remove members only through leave_group() /
-- remove_group_member(); these direct-delete policies let a request skip
-- their balance/creator/phantom-only rules entirely.
drop policy if exists "group creator can remove members" on public.group_members;
drop policy if exists "members can remove themselves" on public.group_members;

-- leave_group() used to rely on "members can remove themselves" (it was not
-- security definer). Now that the policy is gone it has to run as owner; every
-- rule it enforces is already explicit in its body and keyed on auth.uid().
-- Also removes the leaver's RSVPs to this group's events, so they stop
-- receiving that group's event pushes and stop counting in transport totals.
create or replace function public.leave_group(p_group_id uuid)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_member_id uuid;
  v_balance numeric;
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

  delete from event_attendees ea
   using events e
   where e.id = ea.event_id
     and e.group_id = p_group_id
     and ea.member_id = v_member_id;

  delete from group_members where group_id = p_group_id and member_id = v_member_id;
end;
$$;

-- ============================================================
-- 4. invites — target must be a phantom in the invite's own group
-- ============================================================

create or replace function public.is_phantom_in_group(p_member_id uuid, p_group_id uuid)
returns boolean
language sql
stable
security definer
set search_path = public
as $$
  select exists (
    select 1
    from members m
    join group_members gm on gm.member_id = m.id
    where m.id = p_member_id
      and m.account_id is null
      and gm.group_id = p_group_id
  );
$$;

drop policy if exists "insert invites for your groups" on public.invites;
create policy "insert invites for your groups" on public.invites
  for insert with check (
    is_group_member(group_id)
    and created_by = auth.uid()
    and (target_member_id is null or is_phantom_in_group(target_member_id, group_id))
  );

drop policy if exists "update invites for your groups" on public.invites;
create policy "update invites for your groups" on public.invites
  for update
  using (is_group_member(group_id))
  with check (is_group_member(group_id));

-- Invites couldn't be revoked at all before.
drop policy if exists "delete invites for your groups" on public.invites;
create policy "delete invites for your groups" on public.invites
  for delete using (is_group_member(group_id));

-- Only max_uses/expires_at stay member-editable. use_count in particular could
-- be reset to 0 to reopen a spent invite.
create or replace function public.protect_invite_columns()
returns trigger
language plpgsql
as $$
begin
  if current_user in ('authenticated', 'anon') then
    new.id := old.id;
    new.token := old.token;
    new.group_id := old.group_id;
    new.target_member_id := old.target_member_id;
    new.use_count := old.use_count;
    new.created_by := old.created_by;
    new.created_at := old.created_at;
  end if;
  return new;
end;
$$;

drop trigger if exists protect_invite_columns on public.invites;
create trigger protect_invite_columns
  before update on public.invites
  for each row execute function public.protect_invite_columns();

-- The one live claim invite found by the audit whose target phantom isn't in
-- the invite's group — expired rather than deleted, so it's still inspectable.
update public.invites i
   set expires_at = now()
 where i.target_member_id is not null
   and i.expires_at > now()
   and not exists (
     select 1 from public.group_members gm
     where gm.group_id = i.group_id and gm.member_id = i.target_member_id
   );

-- ============================================================
-- 5. redeem_invite — who can claim a phantom
-- ============================================================
-- Unchanged: claiming a phantom still carries over every group it's linked
-- into and merges its history (one invite covers Trip/House/Parties).
-- New rules:
--   * must be signed in;
--   * the target must still be a phantom inside the invite's own group;
--   * the redeemer must not already belong to ANY group the phantom is in —
--     so an existing member (e.g. Bob in Parties) can hand out a claim invite
--     but can't redeem it themselves to absorb the phantom's other groups;
--   * inviter must still be a member of the invite's group, so someone
--     removed from it can't reopen access with an invite they made earlier;
--   * a fresh join by someone who's already in the group no longer burns a use.

create or replace function public.redeem_invite(p_token text)
returns uuid
language plpgsql
security definer
set search_path = public
as $$
declare
  v_invite invites%rowtype;
  v_member_id uuid;
  v_existing_member_id uuid;
begin
  if auth.uid() is null then
    raise exception 'Not signed in';
  end if;

  select * into v_invite from invites where token = p_token for update;
  if not found then
    raise exception 'Invalid invite code';
  end if;
  if v_invite.expires_at < now() then
    raise exception 'Invite expired';
  end if;
  if v_invite.use_count >= v_invite.max_uses then
    raise exception 'Invite already used';
  end if;

  if v_invite.created_by is null or not exists (
    select 1
    from group_members gm
    join members m on m.id = gm.member_id
    where gm.group_id = v_invite.group_id
      and m.account_id = v_invite.created_by
  ) then
    raise exception 'This invite is no longer valid — ask a current group member for a new one';
  end if;

  select id into v_existing_member_id from members where account_id = auth.uid() limit 1;

  if v_invite.target_member_id is not null then
    -- Claiming an existing phantom member.
    if not exists (
      select 1
      from members m
      join group_members gm on gm.member_id = m.id
      where m.id = v_invite.target_member_id
        and m.account_id is null
        and gm.group_id = v_invite.group_id
    ) then
      raise exception 'This invite has already been claimed';
    end if;

    if v_existing_member_id is not null and exists (
      select 1
      from group_members gm_phantom
      join group_members gm_me
        on gm_me.group_id = gm_phantom.group_id
       and gm_me.member_id = v_existing_member_id
      where gm_phantom.member_id = v_invite.target_member_id
    ) then
      raise exception 'You''re already in a group with this person, so this invite isn''t for you';
    end if;

    if v_existing_member_id is not null then
      -- Account already has a members row — merge the phantom into it
      -- instead of creating a second claimed row for the same account.
      update expenses
         set paid_by_member_id = v_existing_member_id
       where paid_by_member_id = v_invite.target_member_id;

      update recurring_expenses
         set paid_by_member_id = v_existing_member_id
       where paid_by_member_id = v_invite.target_member_id;

      update expense_shares es
         set share_amount = es.share_amount + p.share_amount,
             share_amount_in_group_currency = es.share_amount_in_group_currency
               + p.share_amount_in_group_currency
        from expense_shares p
       where p.member_id = v_invite.target_member_id
         and es.member_id = v_existing_member_id
         and es.expense_id = p.expense_id;

      delete from expense_shares
       where member_id = v_invite.target_member_id
         and expense_id in (
           select expense_id from expense_shares where member_id = v_existing_member_id
         );

      update expense_shares
         set member_id = v_existing_member_id
       where member_id = v_invite.target_member_id;

      update recurring_expense_shares res
         set share_amount = res.share_amount + p.share_amount
        from recurring_expense_shares p
       where p.member_id = v_invite.target_member_id
         and res.member_id = v_existing_member_id
         and res.recurring_expense_id = p.recurring_expense_id;

      delete from recurring_expense_shares
       where member_id = v_invite.target_member_id
         and recurring_expense_id in (
           select recurring_expense_id from recurring_expense_shares
            where member_id = v_existing_member_id
         );

      update recurring_expense_shares
         set member_id = v_existing_member_id
       where member_id = v_invite.target_member_id;

      -- Carry over every group the phantom is linked into, not just this one.
      insert into group_members (group_id, member_id)
      select group_id, v_existing_member_id
        from group_members
       where member_id = v_invite.target_member_id
      on conflict do nothing;

      delete from members where id = v_invite.target_member_id;

      v_member_id := v_existing_member_id;
    else
      -- No members row yet (only possible if handle_new_user_member() didn't
      -- run for this account) — claim the phantom row itself.
      update members
         set account_id = auth.uid()
       where id = v_invite.target_member_id
         and account_id is null
      returning id into v_member_id;

      if v_member_id is null then
        raise exception 'This invite has already been claimed';
      end if;
    end if;
  else
    -- Fresh join: reuse this account's members row if it has one.
    v_member_id := v_existing_member_id;

    if v_member_id is not null and exists (
      select 1 from group_members
      where group_id = v_invite.group_id and member_id = v_member_id
    ) then
      return v_invite.group_id;
    end if;

    if v_member_id is null then
      insert into members (account_id, display_name, created_by)
      select auth.uid(),
             coalesce((select email from auth.users where id = auth.uid()), 'New member'),
             auth.uid()
      returning id into v_member_id;
    end if;
  end if;

  insert into group_members (group_id, member_id)
  values (v_invite.group_id, v_member_id)
  on conflict do nothing;

  update invites set use_count = use_count + 1 where id = v_invite.id;

  return v_invite.group_id;
end;
$$;

-- ============================================================
-- 6. events — organizer and birthday columns aren't member-editable
-- ============================================================
-- Before: any member could set created_by = themselves and then delete someone
-- else's event, or flip is_birthday. Also fixes duplicate reminders: an edit
-- only clears reminder_sent_at when starts_at actually moved.

create or replace function public.protect_event_columns()
returns trigger
language plpgsql
as $$
begin
  if current_user not in ('authenticated', 'anon') then
    return new;
  end if;

  if tg_op = 'INSERT' then
    new.created_by := auth.uid();
    new.is_birthday := false;
    new.member_id := null;
    new.reminder_sent_at := null;
    new.birthday_notified_at := null;
    return new;
  end if;

  if old.is_birthday then
    raise exception 'Birthday events can''t be edited';
  end if;

  new.id := old.id;
  new.group_id := old.group_id;
  new.created_by := old.created_by;
  new.created_at := old.created_at;
  new.is_birthday := old.is_birthday;
  new.member_id := old.member_id;
  new.birthday_notified_at := old.birthday_notified_at;
  new.reminder_sent_at := case
    when new.starts_at is distinct from old.starts_at then null
    else old.reminder_sent_at
  end;
  return new;
end;
$$;

drop trigger if exists protect_event_columns on public.events;
create trigger protect_event_columns
  before insert or update on public.events
  for each row execute function public.protect_event_columns();

-- ============================================================
-- 7. event_attendees — RSVP writes require current membership
-- ============================================================
-- Before: update only checked "this is my member row", with no WITH CHECK, so
-- an RSVP could be edited after leaving the group or moved onto an event in
-- another group (joining that event's push recipients).

drop policy if exists "update your own rsvp" on public.event_attendees;
create policy "update your own rsvp" on public.event_attendees
  for update
  using (
    exists (
      select 1 from events e
      where e.id = event_attendees.event_id
        and is_group_member(e.group_id)
    )
    and exists (
      select 1 from members m
      where m.id = event_attendees.member_id
        and m.account_id = auth.uid()
    )
  )
  with check (
    exists (
      select 1 from events e
      where e.id = event_attendees.event_id
        and is_group_member(e.group_id)
    )
    and exists (
      select 1 from members m
      where m.id = event_attendees.member_id
        and m.account_id = auth.uid()
    )
  );

create or replace function public.protect_rsvp_keys()
returns trigger
language plpgsql
as $$
begin
  if current_user in ('authenticated', 'anon') then
    new.event_id := old.event_id;
    new.member_id := old.member_id;
    new.created_at := old.created_at;
  end if;
  return new;
end;
$$;

drop trigger if exists protect_rsvp_keys on public.event_attendees;
create trigger protect_rsvp_keys
  before update on public.event_attendees
  for each row execute function public.protect_rsvp_keys();

-- ============================================================
-- 8. Push recipients — only current group members
-- ============================================================
-- Signatures unchanged, so create or replace is enough.

create or replace function public.event_attendee_notification_recipients(p_event_id uuid, p_actor_account_id uuid)
returns table (account_id uuid, push_token text, platform text)
language sql
stable
set search_path = public
as $$
  select distinct dt.account_id, dt.push_token, dt.platform
  from event_attendees ea
  join events e on e.id = ea.event_id
  join group_members gm on gm.group_id = e.group_id and gm.member_id = ea.member_id
  join members m on m.id = ea.member_id
  join device_tokens dt on dt.account_id = m.account_id
  where ea.event_id = p_event_id
    and m.account_id is not null
    and (p_actor_account_id is null or m.account_id <> p_actor_account_id);
$$;

create or replace function public.event_reminder_recipients(p_event_id uuid)
returns table (account_id uuid, push_token text, platform text)
language sql
stable
set search_path = public
as $$
  select distinct dt.account_id, dt.push_token, dt.platform
  from event_attendees ea
  join events e on e.id = ea.event_id
  join group_members gm on gm.group_id = e.group_id and gm.member_id = ea.member_id
  join members m on m.id = ea.member_id
  join device_tokens dt on dt.account_id = m.account_id
  where ea.event_id = p_event_id
    and ea.response in ('going', 'maybe')
    and m.account_id is not null;
$$;

create or replace function public.expense_notification_recipients(p_expense_id uuid)
returns table (account_id uuid, push_token text, platform text, member_id uuid)
language sql
stable
set search_path = public
as $$
  with involved as (
    select e.paid_by_member_id as member_id, e.created_by, e.group_id
    from expenses e
    where e.id = p_expense_id
    union
    select es.member_id, e.created_by, e.group_id
    from expense_shares es
    join expenses e on e.id = es.expense_id
    where es.expense_id = p_expense_id
  )
  select distinct dt.account_id, dt.push_token, dt.platform, i.member_id
  from involved i
  join members m on m.id = i.member_id
  join device_tokens dt on dt.account_id = m.account_id
  where m.account_id is not null
    and (i.created_by is null or m.account_id <> i.created_by)
    and (
      i.group_id is null
      or exists (
        select 1 from group_members gm
        where gm.group_id = i.group_id and gm.member_id = i.member_id
      )
    );
$$;

create or replace function public.notify_event_cancelled()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
declare
  v_group_name text;
  v_recipients jsonb;
begin
  select g.name into v_group_name from groups g where g.id = old.group_id;

  select coalesce(jsonb_agg(jsonb_build_object(
    'account_id', dt.account_id,
    'push_token', dt.push_token,
    'platform', dt.platform
  )), '[]'::jsonb)
  into v_recipients
  from event_attendees ea
  join group_members gm on gm.group_id = old.group_id and gm.member_id = ea.member_id
  join members m on m.id = ea.member_id
  join device_tokens dt on dt.account_id = m.account_id
  where ea.event_id = old.id
    and m.account_id is not null
    and (auth.uid() is null or m.account_id <> auth.uid());

  perform net.http_post(
    url := 'https://foepkovwmwyygulbdahv.supabase.co/functions/v1/send-push',
    headers := jsonb_build_object(
      'Content-Type', 'application/json',
      'Authorization', 'Bearer ' || (select decrypted_secret from vault.decrypted_secrets where name = 'service_role_key' limit 1)
    ),
    body := jsonb_build_object(
      'event_type', 'cancelled',
      'title', old.title,
      'group_id', old.group_id,
      'group_name', coalesce(v_group_name, 'your group'),
      'recipients', v_recipients
    )
  );
  return old;
end;
$$;

-- ============================================================
-- 9. materialize_recurring_expenses — skip dissolved groups and ex-members
-- ============================================================
-- A template whose group was dissolved, or whose payer / any share-holder is
-- no longer in the group, is deactivated instead of silently charging people
-- who can't see it. Reactivating it after fixing the participants is a normal
-- template edit.

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
        when v_template.frequency = 'monthly' then (v_occurrence + interval '1 month')::date
        when v_template.frequency = 'yearly' then (v_occurrence + interval '1 year')::date
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

-- ============================================================
-- 10. Function grants — mutating RPCs are signed-in only
-- ============================================================
-- By default every function in public is executable by PUBLIC (anon
-- included). The read-only helpers used inside RLS policies
-- (is_group_member etc.) are left alone: they return false for anon anyway,
-- and revoking them would turn anon storage reads into permission errors.

revoke execute on function public.redeem_invite(text) from public, anon;
grant execute on function public.redeem_invite(text) to authenticated;

revoke execute on function public.create_group(text, char) from public, anon;
grant execute on function public.create_group(text, char) to authenticated;

revoke execute on function public.leave_group(uuid) from public, anon;
grant execute on function public.leave_group(uuid) to authenticated;

revoke execute on function public.remove_group_member(uuid, uuid) from public, anon;
grant execute on function public.remove_group_member(uuid, uuid) to authenticated;

revoke execute on function public.transfer_group_ownership(uuid, uuid) from public, anon;
grant execute on function public.transfer_group_ownership(uuid, uuid) to authenticated;

-- ============================================================
-- 11. Leftovers still live in production
-- ============================================================
-- categories was removed from schema.sql on 2026-08-28 but never dropped
-- live; its "select using (true)" policy let any account write rows every
-- other account can read. payment_notification_recipients() predates the
-- payments → expenses merge.

drop table if exists public.categories cascade;

do $$
declare
  v_fn regprocedure;
begin
  for v_fn in
    select p.oid::regprocedure
    from pg_proc p
    where p.pronamespace = 'public'::regnamespace
      and p.proname in ('payment_notification_recipients', 'notify_new_payment')
  loop
    execute 'drop function ' || v_fn;
  end loop;
end;
$$;

commit;
