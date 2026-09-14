-- Calendar feed (2026-09-14) — run this against the live project. See schema.sql's own
-- "Calendar subscription feed" section (bottom of file) for the full design rationale; this file
-- just applies the same changes to an already-existing database rather than a fresh install.
--
-- Safe to run once.

create table public.calendar_subscriptions (
  id uuid primary key default gen_random_uuid(),
  member_id uuid not null unique references public.members(id) on delete cascade,
  token text not null unique default encode(gen_random_bytes(24), 'base64url'),
  created_at timestamptz not null default now(),
  last_accessed_at timestamptz
);

create index on public.calendar_subscriptions (token);

alter table public.calendar_subscriptions enable row level security;

create policy "manage your own calendar subscription" on public.calendar_subscriptions
  for all using (member_id in (select id from members where account_id = auth.uid()))
  with check (member_id in (select id from members where account_id = auth.uid()));
