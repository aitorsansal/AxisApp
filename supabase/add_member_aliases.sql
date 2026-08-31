-- Run this once against the live project to add per-account member aliases
-- and reserve members.avatar_path for later. See schema.sql's "Member
-- aliases + reserved avatar column" remarks for the full explanation.

create table public.member_aliases (
  owner_id uuid not null references auth.users(id) on delete cascade,
  member_id uuid not null references public.members(id) on delete cascade,
  alias text not null,
  primary key (owner_id, member_id)
);

alter table public.member_aliases enable row level security;

create policy "manage your own aliases" on public.member_aliases
  for all using (owner_id = auth.uid()) with check (owner_id = auth.uid());

alter table public.members add column avatar_path text;
