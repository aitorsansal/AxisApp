-- Adds a per-group color + icon "appearance" tag so the Groups list is scannable by shape/color
-- instead of reading every name — added 2026-09-14. color reuses the app's existing 8
-- AccentPreset names (Services/AccentPalettes.cs) rather than a second color system; icon is a
-- fixed, curated Lucide glyph key (AppConstants.GroupIcons), not free-form emoji or an uploaded
-- photo.
--
-- Deliberately member-editable (any current member, not just the creator) — unlike name/currency,
-- which stay creator-only, this is cosmetic. Plain RLS can only gate whole rows, not "these
-- columns only if you're the creator", so the finer-grained split is enforced by a trigger
-- instead of a second policy: the existing creator-only "update own groups" policy is replaced
-- with a member-wide one (every current member is already covered — create_group() adds the
-- creator as a member in the same transaction, so there's no case where a creator needs update
-- access without also being a member), and a BEFORE UPDATE trigger rejects any attempt to change
-- name/currency unless the caller is still the creator. This holds regardless of how the request
-- was made, not just what the app's Set(color/icon)-only update path happens to send.
alter table public.groups
  add column color text not null default 'Blue'
    check (color in ('Blue','Green','Red','Purple','Pink','Amber','Orange','Navy')),
  add column icon text
    check (icon is null or icon in (
      'home','users','heart','baby','dog','plane','car','ship','bike','tent','map_pin',
      'mountain','utensils_crossed','coffee','beer','party_popper','gift','wallet','piggy_bank',
      'briefcase','dumbbell','graduation_cap','gamepad','film','music','book','shopping_cart','star'
    ));

drop policy "update own groups" on public.groups;
create policy "update groups you belong to" on public.groups
  for update using (is_group_member(id)) with check (is_group_member(id));

create or replace function public.enforce_group_owner_only_columns()
returns trigger
language plpgsql
as $$
begin
  if (new.name is distinct from old.name or new.currency is distinct from old.currency)
     and auth.uid() <> old.created_by then
    raise exception 'Only the group creator can change name or currency.';
  end if;
  return new;
end;
$$;

create trigger enforce_group_owner_only_columns
  before update on public.groups
  for each row execute function public.enforce_group_owner_only_columns();
