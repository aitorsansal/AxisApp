-- Birthday events (2026-09-08) — run this against the live project. See schema.sql's own
-- "Birthday events" section (bottom of file) for the full design rationale; this file just
-- applies the same changes to an already-existing database rather than a fresh install.
--
-- Safe to run once. materialize_birthday_events() is NOT invoked automatically by this script —
-- run `select public.materialize_birthday_events();` by hand afterward (same "manual first run"
-- convention as cleanup-receipts/materialize-recurring-expenses) to backfill birthday events for
-- every existing member who already has a birth_date set, rather than waiting for tomorrow's
-- 7am UTC cron run.

-- 1. Guard notify_new_event so a birthday event's creation (which happens well ahead of the
-- actual date) never fires a group-wide push — only send_birthday_notifications() should, on
-- the day.
create or replace function public.notify_new_event()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  if new.is_birthday then
    return new;
  end if;

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

-- 2. Exclude birthday events from the day-before reminder scan — it has no is_birthday filter
-- today and would otherwise consume birthday_notified_at's sibling column with a useless
-- empty-recipient push the day before, since it never checks whether an event has any real
-- attendees before sending.
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
      and not is_birthday
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

-- 3. New columns.
alter table public.events add column is_birthday boolean not null default false;
alter table public.events add column member_id uuid references public.members(id) on delete cascade;
alter table public.events add column birthday_notified_at timestamptz;

-- 4. Recipient function for the day-of push — whole group minus the birthday person.
create or replace function public.event_birthday_notification_recipients(p_event_id uuid)
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
    and m.id is distinct from e.member_id;
$$;

revoke execute on function public.event_birthday_notification_recipients(uuid) from public, anon, authenticated;

-- 5. Materialization — ensures the next occurrence of every member's birthday is a real events
-- row, per group they're in; detects a birth_date edit or removal and corrects/cleans up.
create or replace function public.materialize_birthday_events()
returns void
language plpgsql
set search_path = public
as $$
declare
  v_row record;
  v_year int;
  v_month int;
  v_day int;
  v_last_day_of_month int;
  v_target_date date;
  v_existing record;
begin
  for v_row in
    select m.id as member_id, m.display_name, m.birth_date, gm.group_id
    from members m
    join group_members gm on gm.member_id = m.id
    where m.birth_date is not null
  loop
    v_month := extract(month from v_row.birth_date)::int;
    v_day := extract(day from v_row.birth_date)::int;
    v_year := extract(year from current_date)::int;

    v_last_day_of_month := extract(day from (
      date_trunc('month', make_date(v_year, v_month, 1)) + interval '1 month - 1 day'
    ))::int;
    v_target_date := make_date(v_year, v_month, least(v_day, v_last_day_of_month));

    if v_target_date < current_date then
      v_year := v_year + 1;
      v_last_day_of_month := extract(day from (
        date_trunc('month', make_date(v_year, v_month, 1)) + interval '1 month - 1 day'
      ))::int;
      v_target_date := make_date(v_year, v_month, least(v_day, v_last_day_of_month));
    end if;

    select id, starts_at into v_existing
    from events
    where is_birthday and member_id = v_row.member_id and group_id = v_row.group_id
      and starts_at >= now()
    order by starts_at
    limit 1;

    if v_existing.id is null or v_existing.starts_at::date <> v_target_date then
      if v_existing.id is not null then
        delete from events where id = v_existing.id;
      end if;

      insert into events (group_id, title, starts_at, is_birthday, member_id, needs_transport, created_by)
      values (
        v_row.group_id,
        '🎂 ' || v_row.display_name || '''s Birthday',
        v_target_date + time '12:00',
        true,
        v_row.member_id,
        false,
        null
      );
    end if;
  end loop;

  delete from events e
  where e.is_birthday
    and e.starts_at >= now()
    and not exists (
      select 1 from members m
      join group_members gm on gm.member_id = m.id
      where m.id = e.member_id and gm.group_id = e.group_id and m.birth_date is not null
    );
end;
$$;

revoke execute on function public.materialize_birthday_events() from public, anon, authenticated;

select cron.schedule(
  'materialize-birthday-events',
  '0 7 * * *',
  $$ select public.materialize_birthday_events(); $$
);

-- 6. The day-of notification push.
create or replace function public.send_birthday_notifications()
returns void
language plpgsql
set search_path = public
as $$
declare
  v_event record;
begin
  for v_event in
    select id from events
    where is_birthday
      and starts_at::date = current_date
      and birthday_notified_at is null
  loop
    perform net.http_post(
      url := 'https://foepkovwmwyygulbdahv.supabase.co/functions/v1/send-push',
      headers := jsonb_build_object(
        'Content-Type', 'application/json',
        'Authorization', 'Bearer ' || (select decrypted_secret from vault.decrypted_secrets where name = 'service_role_key' limit 1)
      ),
      body := jsonb_build_object('event_id', v_event.id, 'event_type', 'birthday')
    );

    update events set birthday_notified_at = now() where id = v_event.id;
  end loop;
end;
$$;

revoke execute on function public.send_birthday_notifications() from public, anon, authenticated;

select cron.schedule(
  'send-birthday-notifications',
  '30 7 * * *',
  $$ select public.send_birthday_notifications(); $$
);
