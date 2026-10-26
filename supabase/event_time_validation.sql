-- Run against the existing database. Existing invalid rows are retained for repair;
-- all new inserts and updates must have a positive event duration.
do $$
begin
  if not exists (
    select 1 from pg_constraint
    where conrelid = 'public.events'::regclass and conname = 'events_end_after_start'
  ) then
    alter table public.events add constraint events_end_after_start
      check (ends_at is null or ends_at > starts_at) not valid;
  end if;
end $$;

-- Inspect existing rows before validating the constraint across historical data.
select id, title, starts_at, ends_at from public.events where ends_at <= starts_at;
