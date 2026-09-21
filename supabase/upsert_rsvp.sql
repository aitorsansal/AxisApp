-- upsert_rsvp() — atomic RSVP write (SECURITY_AUDIT.md #12)
-- ============================================================
-- The MAUI repository used to SELECT the (event, member) row and then INSERT or UPDATE it, so two
-- concurrent RSVPs from the same member (e.g. a fast double tap, or the app and a notification
-- action) could both see "no row" and the second INSERT would hit the primary-key violation. The web
-- app already did a real upsert; this gives MAUI the same thing in one statement.
--
-- Deliberately NOT security definer: it runs as the caller, so the existing event_attendees RLS
-- (insert / update your own RSVP, group members only) and protect_rsvp_keys apply exactly as they do
-- to the web app's PostgREST upsert. Nothing here widens access.
--
-- Same coupling rule the repository always applied: a "not_going" response clears the car status
-- and seats. Everything else is passed through as sent.
-- ============================================================
create or replace function public.upsert_rsvp(
  p_event_id uuid,
  p_member_id uuid,
  p_response text,
  p_car_status text default 'none',
  p_car_offered_seats int default null
)
returns public.event_attendees
language sql
as $$
  insert into public.event_attendees as ea (event_id, member_id, response, car_status, car_offered_seats)
  values (
    p_event_id,
    p_member_id,
    p_response,
    case when p_response = 'not_going' then 'none' else p_car_status end,
    case when p_response = 'not_going' then null else p_car_offered_seats end
  )
  on conflict (event_id, member_id) do update
    set response = excluded.response,
        car_status = excluded.car_status,
        car_offered_seats = excluded.car_offered_seats,
        updated_at = now()
  returning ea.*;
$$;

revoke execute on function public.upsert_rsvp(uuid, uuid, text, text, int) from public, anon;
grant execute on function public.upsert_rsvp(uuid, uuid, text, text, int) to authenticated;
