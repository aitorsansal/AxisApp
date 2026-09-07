-- Events & Calendar, Milestone 1 (2026-09-07) — see /EVENTS_PLAN.md
-- ============================================================
-- Adds the two tables Phase 2 is built on:
--   - events: one row per group event (title/description/location/
--     starts_at/ends_at), plus needs_transport (editable after creation,
--     unlike groups.currency's deliberate lock — see the plan doc) and
--     reminder_sent_at (a mark-processed column for Milestone 5's reminder
--     cron, same idea as recurring_expenses.last_processed_date).
--   - event_attendees: one row per (event, member) RSVP, response is a
--     3-state (going/maybe/not_going — not a plain yes/no, see the plan
--     doc), plus the carpooling fields (car_status tri-state,
--     car_offered_seats). RSVP and car status are coupled at the app layer,
--     not the DB — see Milestone 3a in the plan doc: setting response to
--     'not_going' must also reset car_status/car_offered_seats in the same
--     write, or a declined attendee would keep corrupting the transport
--     shortfall math.
--   - members.car_extra_seats: a per-profile default seat count, named to
--     mean "extra seats beyond the driver" everywhere (DB and UI both) —
--     see the plan doc on the off-by-one naming footgun the original idea's
--     phrasing had.
--
-- created_by on events is nullable with `on delete set null` from the
-- start, unlike the older tables in this file (members/invites/expenses/
-- recurring_expenses), which all began as `not null` and had to be relaxed
-- later once account deletion was built (see delete_account()'s remarks
-- further up) — events didn't exist yet at that point, so there's no reason
-- to reintroduce the same bug just to "match" the historical tables.
--
-- Run this once against the already-live project, after everything else in
-- schema.sql. Report back the exact error text if any statement fails, same
-- as every other block in this file has needed.
-- ============================================================

begin;

create table public.events (
  id uuid primary key default gen_random_uuid(),
  group_id uuid not null references public.groups(id) on delete cascade,
  title text not null,
  description text,
  location text,
  starts_at timestamptz not null,
  ends_at timestamptz,
  needs_transport boolean not null default false,
  reminder_sent_at timestamptz,
  created_by uuid references auth.users(id) on delete set null,
  created_at timestamptz not null default now()
);

create table public.event_attendees (
  event_id uuid not null references public.events(id) on delete cascade,
  member_id uuid not null references public.members(id) on delete cascade,
  response text not null default 'going'
    check (response in ('going', 'maybe', 'not_going')),
  car_status text not null default 'none'
    check (car_status in ('none', 'offering', 'needs_ride')),
  car_offered_seats int,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  primary key (event_id, member_id)
);

alter table public.members add column car_extra_seats int;

-- events(group_id, starts_at): the grouped-by-date list (Milestone 3b) and
-- the reminder cron scan (Milestone 5) both filter/sort on this.
create index on public.events (group_id, starts_at);
-- event_attendees(event_id): attendee lookups per event, including the
-- transport aggregate (Milestone 4) and the notification recipient
-- functions (Milestone 5).
create index on public.event_attendees (event_id);

alter table public.events enable row level security;
alter table public.event_attendees enable row level security;

-- events: any current group member can select/insert/update — same shape
-- as expenses/recurring_expenses. Delete is creator-only, a deliberate
-- divergence: an event is more ownership-flavored than an expense (having
-- one you organized deleted out from under you by another member is a
-- worse surprise, compounded by attendees possibly having arranged
-- carpooling around it already) — see the plan doc's "Decisions locked".
create policy "select events in your groups" on public.events
  for select using (is_group_member(group_id));
create policy "insert events in your groups" on public.events
  for insert with check (is_group_member(group_id));
create policy "update events in your groups" on public.events
  for update using (is_group_member(group_id));
create policy "delete own events" on public.events
  for delete using (created_by = auth.uid());

-- event_attendees: select follows the parent event's visibility. Writes are
-- restricted to your own row AND require you to actually be a member of
-- that event's group — the "own row" check alone isn't enough on its own,
-- since one account's member_id is shared across every group it belongs to
-- (see the one-account-one-member invariant elsewhere in this file); without
-- the group-membership check too, an account could RSVP to an event in a
-- group it was never invited into, just by referencing its own member_id.
create policy "select attendees of visible events" on public.event_attendees
  for select using (
    exists (
      select 1 from events e
      where e.id = event_attendees.event_id
        and is_group_member(e.group_id)
    )
  );
create policy "insert your own rsvp" on public.event_attendees
  for insert with check (
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
create policy "update your own rsvp" on public.event_attendees
  for update using (
    exists (
      select 1 from members m
      where m.id = event_attendees.member_id
        and m.account_id = auth.uid()
    )
  );
create policy "delete your own rsvp" on public.event_attendees
  for delete using (
    exists (
      select 1 from members m
      where m.id = event_attendees.member_id
        and m.account_id = auth.uid()
    )
  );

commit;

-- Smoke test (run by hand, then roll back or delete the rows — same
-- discipline multi_currency_milestone1_smoketest.sql followed): as a real
-- signed-in account that's a member of some group G,
--   insert into events (group_id, title, starts_at, needs_transport)
--     values ('<G>', 'Test event', now() + interval '1 day', true)
--     returning id;
--   insert into event_attendees (event_id, member_id, response, car_status, car_offered_seats)
--     values ('<event id above>', '<your own member id>', 'going', 'offering', 3);
-- Both should succeed. Then confirm a second account that is NOT a member
-- of G gets a permission-denied/zero-rows result attempting the same
-- inserts against that event.
