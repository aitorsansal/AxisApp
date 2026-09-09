-- Event-linked expenses (2026-09-09)
-- ============================================================
-- Adds expenses.event_id — an optional link from an Expense back to the
-- Event it was booked from (e.g. "Trip to Paris" → train tickets, hotel).
-- Nullable, on delete set null, same "the expense survives, only its
-- reference nulls out" treatment expenses.group_id already gets when a
-- group dissolves.
--
-- The participant set for an event-linked expense is a SNAPSHOT taken by
-- AddExpenseViewModel at add/edit time from that event's current "going"
-- attendees — not re-derived live from event_id on every read. Someone who
-- RSVPs going after the expense already exists doesn't retroactively
-- appear in its split, and someone who un-RSVPs afterward doesn't lose
-- their existing share on an edit either.
--
-- No RLS change needed — expenses' existing is_group_member(group_id)
-- policies already cover event_id the same way they cover every other
-- column on the row.
--
-- Run this once against the already-live project, after everything else in
-- schema.sql (including events_milestone1.sql). Report back the exact
-- error text if any statement fails.
-- ============================================================

begin;

alter table public.expenses add column event_id uuid references public.events(id) on delete set null;
create index on public.expenses (event_id);

commit;
