-- Streams event_attendees (and events) over Supabase Realtime so an open web event screen updates
-- when someone changes an RSVP or offers seats from another client (QA finding F02). Realtime
-- applies each subscriber's RLS, so nobody receives rows they couldn't select. Run once against
-- the live project; the webapp falls back to focus/poll refetches if the socket is down.
--
-- Delete events carry only the primary key unless replica identity is full; the webapp treats
-- any change as "refetch", so the default identity is enough.
alter publication supabase_realtime add table public.event_attendees;
alter publication supabase_realtime add table public.events;
