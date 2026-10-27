-- Birthday event titles in Spanish (2026-10-27) — run this against the live project.
-- materialize_birthday_events() wrote "🎂 {name}'s Birthday"; the group is Spanish-speaking,
-- so new rows get "🎂 Cumpleaños de {name}" and every existing birthday row (past and future)
-- is renamed. The title is stored data, read as-is by both clients, the calendar feed and the
-- birthday push (send-push uses it directly as the body).
--
-- Safe to re-run. The UPDATE runs as postgres, so protect_event_columns()' "Birthday events
-- can't be edited" guard (authenticated/anon only) doesn't apply, and notify_event_changed()
-- only fires on starts_at/ends_at/location changes — renaming is silent, no pushes.

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
        '🎂 Cumpleaños de ' || v_row.display_name,
        v_target_date + time '12:00',
        true,
        v_row.member_id,
        false,
        null
      );
    end if;
  end loop;

  -- Cleanup: a future birthday event whose member no longer has a birth_date (cleared) or is no
  -- longer in that group.
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

-- Existing rows. Uses each member's current display name, so this also picks up any rename
-- since the row was created.
update public.events e
set title = '🎂 Cumpleaños de ' || m.display_name
from public.members m
where e.is_birthday
  and e.member_id = m.id
  and e.title is distinct from '🎂 Cumpleaños de ' || m.display_name;

-- Check: should return no rows.
select id, title from public.events where is_birthday and title not like '🎂 Cumpleaños de %';
