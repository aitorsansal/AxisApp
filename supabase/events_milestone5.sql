-- Events & Calendar, Milestone 5 (2026-09-07) -- see /EVENTS_PLAN.md
-- ============================================================
-- Event notifications: creation, change, cancellation (all AFTER/BEFORE
-- triggers, immediate) plus a daily advance-reminder cron. Reuses the
-- existing Phase 1 push pipeline (send-push Edge Function,
-- AxisFirebaseMessagingService client-side) -- only send-push itself needs
-- a matching code change (deployed separately via the dashboard), nothing
-- else client-side, since AxisFirebaseMessagingService never branches on
-- the payload's `type` field, only reads title/body/group_id/group_name.
--
-- Real design correction made while writing this, not in the original plan
-- text: the plan said the cancellation trigger should be AFTER DELETE,
-- reading OLD. That's fine for OLD's own columns (title, group_id), but
-- NOT for the recipient list -- event_attendees cascade-deletes with its
-- parent events row, and Postgres's internal FK-cascade ordering relative
-- to a user AFTER DELETE trigger on the same table isn't something worth
-- depending on. Used BEFORE DELETE instead (unambiguous: nothing has
-- cascaded yet when it fires) and, since pg_net's HTTP delivery is
-- asynchronous regardless of trigger timing (the events/event_attendees
-- rows will be long gone by the time send-push actually processes the
-- request either way), the recipient list AND the message content are
-- both computed synchronously inside the trigger and embedded directly in
-- the JSON payload -- no RPC lookup for the cancelled case at all, unlike
-- created/changed/reminder which all still have a live row to query when
-- their (also async) push actually fires.
--
-- Run this once against the already-live project, after everything else in
-- schema.sql (including events_milestone1.sql). Report back the exact
-- error text if any statement fails, same as every other block in this
-- file has needed.
-- ============================================================

begin;

-- ============================================================
-- event_notification_recipients: creation push -- every current group
-- member minus the creator (mirrors expense_notification_recipients'
-- shape exactly).
-- ============================================================
create or replace function public.event_notification_recipients(p_event_id uuid)
returns table (account_id uuid, push_token text, platform text)
language sql
stable
set search_path = public
as $$
  select distinct dt.account_id, dt.push_token, dt.platform
  from events e
  join group_members gm on gm.group_id = e.group_id
  join members m on m.id = gm.member_id
  join device_tokens dt on dt.account_id = m.account_id
  where e.id = p_event_id
    and m.account_id is not null
    and m.account_id <> e.created_by;
$$;

revoke execute on function public.event_notification_recipients(uuid) from public, anon, authenticated;

-- ============================================================
-- event_attendee_notification_recipients: change push -- every current
-- attendee (any event_attendees row, any response) minus whoever made the
-- edit. Deliberately narrower than the creation set: someone who never
-- RSVP'd at all doesn't need to hear that an event they're not tracking
-- got moved, only people who've actually engaged with it.
-- ============================================================
create or replace function public.event_attendee_notification_recipients(p_event_id uuid, p_actor_account_id uuid)
returns table (account_id uuid, push_token text, platform text)
language sql
stable
set search_path = public
as $$
  select distinct dt.account_id, dt.push_token, dt.platform
  from event_attendees ea
  join members m on m.id = ea.member_id
  join device_tokens dt on dt.account_id = m.account_id
  where ea.event_id = p_event_id
    and m.account_id is not null
    and (p_actor_account_id is null or m.account_id <> p_actor_account_id);
$$;

revoke execute on function public.event_attendee_notification_recipients(uuid, uuid) from public, anon, authenticated;

-- ============================================================
-- event_reminder_recipients: the daily advance-reminder cron's recipient
-- set -- going/maybe attendees only (not_going gets no nudge), INCLUDING
-- the creator this time (unlike creation, they need reminding too -- they
-- already know they made the event, they don't already know it's tomorrow).
-- ============================================================
create or replace function public.event_reminder_recipients(p_event_id uuid)
returns table (account_id uuid, push_token text, platform text)
language sql
stable
set search_path = public
as $$
  select distinct dt.account_id, dt.push_token, dt.platform
  from event_attendees ea
  join members m on m.id = ea.member_id
  join device_tokens dt on dt.account_id = m.account_id
  where ea.event_id = p_event_id
    and ea.response in ('going', 'maybe')
    and m.account_id is not null;
$$;

revoke execute on function public.event_reminder_recipients(uuid) from public, anon, authenticated;

-- ============================================================
-- notify_new_event: AFTER INSERT on events. Same Vault-service-role-key
-- pattern and SECURITY DEFINER reasoning as notify_new_expense -- this
-- fires from a plain app-level INSERT by an ordinary signed-in user via
-- Postgrest (role `authenticated`, no grant on the vault schema), so
-- without SECURITY DEFINER it fails with `permission denied for schema
-- vault` (42501) the same way notify_new_expense did before that fix.
-- ============================================================
create or replace function public.notify_new_event()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  perform net.http_post(
    url := 'https://foepkovwmwyygulbdahv.supabase.co/functions/v1/send-push',
    headers := jsonb_build_object(
      'Content-Type', 'application/json',
      'Authorization', 'Bearer ' || (select decrypted_secret from vault.decrypted_secrets where name = 'service_role_key' limit 1)
    ),
    body := jsonb_build_object('event_id', new.id, 'event_type', 'created')
  );
  return new;
end;
$$;

create trigger events_notify_after_insert
  after insert on public.events
  for each row execute function public.notify_new_event();

-- ============================================================
-- notify_event_changed: AFTER UPDATE on events, firing only when
-- starts_at/ends_at/location actually changed -- a description or
-- needs_transport edit stays silent, per /EVENTS_PLAN.md's "Decisions
-- locked" (only the fields that would actually strand or confuse someone
-- who already made plans). auth.uid() here reflects the real caller's JWT
-- regardless of SECURITY DEFINER -- that only elevates SQL execution
-- privileges, not what auth.uid() reports.
-- ============================================================
create or replace function public.notify_event_changed()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  if new.starts_at is distinct from old.starts_at
    or new.ends_at is distinct from old.ends_at
    or new.location is distinct from old.location
  then
    perform net.http_post(
      url := 'https://foepkovwmwyygulbdahv.supabase.co/functions/v1/send-push',
      headers := jsonb_build_object(
        'Content-Type', 'application/json',
        'Authorization', 'Bearer ' || (select decrypted_secret from vault.decrypted_secrets where name = 'service_role_key' limit 1)
      ),
      body := jsonb_build_object('event_id', new.id, 'event_type', 'changed', 'actor_account_id', auth.uid())
    );
  end if;
  return new;
end;
$$;

create trigger events_notify_after_update
  after update on public.events
  for each row execute function public.notify_event_changed();

-- ============================================================
-- notify_event_cancelled: BEFORE DELETE on events (see this file's header
-- comment for why BEFORE, not AFTER, and why recipients/content are both
-- embedded directly rather than looked up via RPC). Must return old --
-- a BEFORE DELETE trigger that returns null would cancel the delete.
-- ============================================================
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

create trigger events_notify_before_delete
  before delete on public.events
  for each row execute function public.notify_event_cancelled();

-- ============================================================
-- send_event_reminders: the daily advance-reminder cron job. Window and
-- run time decided 2026-09-07 (see /EVENTS_PLAN.md's Milestone 5 remarks):
-- daily at 9am UTC (distinct from fetch-exchange-rates' 6am and
-- materialize-recurring-expenses' 8am, still a normal-morning time),
-- scanning events starting in the next 24-30 hours that haven't been
-- reminded yet -- since this only runs once a day, an exact "24h before"
-- isn't achievable anyway; this gives every event its one reminder
-- somewhere in that 24-30h range, whichever daily run first catches it.
-- reminder_sent_at is stamped immediately after a successful queue so a
-- given event is never reminded twice, same "mark-processed" idea as
-- recurring_expenses.last_processed_date.
--
-- Never SECURITY DEFINER -- same reasoning as materialize_recurring_expenses/
-- find_expired_receipts: this only ever runs via pg_cron, as whichever role
-- called cron.schedule() (postgres, which already has Vault access), so
-- there's no permission gap to bridge here the way the trigger-based
-- functions above need one.
-- ============================================================
create or replace function public.send_event_reminders()
returns void
language plpgsql
set search_path = public
as $$
declare
  v_event record;
begin
  for v_event in
    select id from events
    where starts_at >= now()
      and starts_at < now() + interval '30 hours'
      and reminder_sent_at is null
  loop
    perform net.http_post(
      url := 'https://foepkovwmwyygulbdahv.supabase.co/functions/v1/send-push',
      headers := jsonb_build_object(
        'Content-Type', 'application/json',
        'Authorization', 'Bearer ' || (select decrypted_secret from vault.decrypted_secrets where name = 'service_role_key' limit 1)
      ),
      body := jsonb_build_object('event_id', v_event.id, 'event_type', 'reminder')
    );

    update events set reminder_sent_at = now() where id = v_event.id;
  end loop;
end;
$$;

revoke execute on function public.send_event_reminders() from public, anon, authenticated;

select cron.schedule(
  'send-event-reminders',
  '0 9 * * *',
  $$ select public.send_event_reminders(); $$
);

commit;

-- Smoke test (run by hand): as a real signed-in account that's a member of
-- some group G,
--   insert into events (group_id, title, starts_at) values
--     ('<G>', 'Notify test', now() + interval '1 hour') returning id;
-- should complete without error (the trigger's net.http_post is queued
-- asynchronously, so this insert itself won't fail even if send-push isn't
-- deployed yet -- check net._http_response afterward for the actual result).
-- Then:
--   update events set starts_at = now() + interval '2 hours' where id = '<event id>';
--   delete from events where id = '<event id>';
-- To manually trigger a reminder scan without waiting for 9am UTC:
--   select public.send_event_reminders();
