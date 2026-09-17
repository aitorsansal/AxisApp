-- Signup profile metadata (2026-09-17)
-- ============================================================
-- Prep for turning on "Confirm email" in Supabase Auth. With confirmation on,
-- sign-up returns no session, so the MAUI Register page can no longer update
-- the new member row's display name/birthday right after signing up (that
-- update needs a signed-in session under RLS). Instead the client sends them
-- as user metadata on the sign-up request itself (raw_user_meta_data), and
-- handle_new_user_member() reads them when it provisions the row.
--
-- Only the keys this app sets are read: display_name and birth_date
-- (YYYY-MM-DD). Google sign-ins don't set either, so they keep the existing
-- email fallback. A malformed birth_date is ignored rather than raised — this
-- trigger runs inside account creation, and a bad optional field must never
-- block a signup.
--
-- Same definition as supabase/schema.sql. Safe to run more than once.
-- ============================================================

create or replace function public.handle_new_user_member()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
declare
  v_display_name text := nullif(btrim(new.raw_user_meta_data->>'display_name'), '');
  v_birth_date_text text := new.raw_user_meta_data->>'birth_date';
  v_birth_date date;
begin
  if v_birth_date_text ~ '^\d{4}-\d{2}-\d{2}$' then
    begin
      v_birth_date := v_birth_date_text::date;
    exception when others then
      v_birth_date := null;
    end;
  end if;

  insert into public.members (account_id, display_name, birth_date, created_by)
  values (
    new.id,
    coalesce(left(v_display_name, 100), new.email, 'New member'),
    v_birth_date,
    new.id
  );
  return new;
end;
$$;
