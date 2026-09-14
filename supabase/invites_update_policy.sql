-- Adds the missing UPDATE policy on public.invites. Until now the table only had
-- select/insert policies (see schema.sql's "invites: only existing group members can
-- create/view them" comment) — there was no way for a group member to edit an invite's
-- max_uses/expires_at after creating it. Added alongside JoinGroupPage's invite-reuse +
-- editable max-uses/expiry feature (2026-09-14).
create policy "update invites for your groups" on public.invites
  for update using (is_group_member(group_id)) with check (is_group_member(group_id));
