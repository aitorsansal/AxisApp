-- Multi-currency, Milestone 3 (2026-09-05) — see /MULTI_CURRENCY_PLAN.md
-- ============================================================
-- Adds p_currency to create_group() — required, no default, so picking a
-- group's currency at creation is always a deliberate choice (see the plan
-- doc's "Decisions locked" section), never something that could fall out of
-- a default. groups.currency itself and its check constraint already exist
-- live from Milestone 1 — this only changes the RPC signature that writes
-- it.
--
-- Postgres treats a changed parameter list as a new overload rather than
-- replacing the old one, so the old 1-arg create_group(text) has to be
-- dropped explicitly — otherwise it would sit around as dead, callable-but-
-- unused API surface (and Postgrest's schema cache would have to pick one of
-- two overloads for the same RPC name, which is worse than just removing
-- it). Run this only once the app build calling create_group has already
-- been updated to always pass p_currency — see SupabaseGroupsRepository
-- .CreateAsync.
-- ============================================================

drop function if exists public.create_group(text);

create or replace function public.create_group(p_name text, p_currency char(3))
returns uuid
language plpgsql
security definer
set search_path = public
as $$
declare
  v_group_id uuid;
  v_member_id uuid;
begin
  insert into public.groups (name, currency, created_by)
  values (p_name, p_currency, auth.uid())
  returning id into v_group_id;

  insert into public.members (account_id, display_name, created_by)
  values (
    auth.uid(),
    coalesce((select email from auth.users where id = auth.uid()), 'New member'),
    auth.uid()
  )
  returning id into v_member_id;

  insert into public.group_members (group_id, member_id)
  values (v_group_id, v_member_id);

  return v_group_id;
end;
$$;
