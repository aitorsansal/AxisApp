-- Provision a members row at signup — 2026-09-07
--
-- Run this whole file once in the Supabase SQL editor against the live
-- project. Follow-up to one_account_one_member_fix.sql: that stopped
-- create_group()/redeem_invite() from *duplicating* members rows, but a
-- brand-new account with zero groups still had no members row at all until
-- its first group, so ProfilePage's display name/birthday couldn't be
-- edited before then. This adds a trigger on auth.users that provisions the
-- row exactly once, at signup, instead of a per-launch client-side check.
--
-- This is the exact same definition now committed in supabase/schema.sql —
-- copy-pasted here so it can be applied in one paste. No table/column
-- changes to public.* — only a new function + a new trigger on auth.users.

create or replace function public.handle_new_user_member()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  insert into public.members (account_id, display_name, created_by)
  values (new.id, coalesce(new.email, 'New member'), new.id);
  return new;
end;
$$;

create trigger on_auth_user_created_provision_member
  after insert on auth.users
  for each row execute function public.handle_new_user_member();
