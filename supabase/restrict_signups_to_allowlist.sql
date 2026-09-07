-- Run this once in the Supabase SQL editor against the live project.
-- See schema.sql's "Signup allow-list" remarks for the full reasoning —
-- this file is the same two objects, paste-ready for an already-live project
-- rather than a fresh install.

create table public.allowed_signup_emails (
  email text primary key
);

alter table public.allowed_signup_emails enable row level security;

insert into public.allowed_signup_emails (email) values
  ('aitorsansal@gmail.com');
  -- add each additional person here, e.g.:
  -- ('yourfriend@gmail.com');

create or replace function public.restrict_signup_to_allowlist()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  if not exists (
    select 1 from allowed_signup_emails where email = lower(new.email)
  ) then
    raise exception 'Signups are invite-only right now — ask the app owner to add your email.';
  end if;
  return new;
end;
$$;

create trigger on_auth_user_created_restrict_signup
  before insert on auth.users
  for each row execute function public.restrict_signup_to_allowlist();
