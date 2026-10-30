-- A group name made only of spaces was accepted (QA finding W6): the web form's `required`
-- passes it and nothing at the data boundary rejected it. Both clients now trim and refuse
-- blank names, and this constraint backs that up for any other writer. Run once against the
-- live project; schema.sql already carries it for fresh installs.
--
-- NOT VALID: checks every new or updated row without scanning existing ones, so a pre-existing
-- blank-named group can't make this fail. Such a group can't be updated (even its colour)
-- until it is renamed. To list them:  select id, name from public.groups where btrim(name) = '';
-- After renaming any, enforce it for all rows:
--   alter table public.groups validate constraint groups_name_not_blank;
alter table public.groups
  add constraint groups_name_not_blank check (btrim(name) <> '') not valid;
