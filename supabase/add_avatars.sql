-- Run this once against the live project to add avatar photo support. See
-- schema.sql's "Avatar photos" remarks for the full explanation.

insert into storage.buckets (id, name, public) values ('avatars', 'avatars', true);

create policy "anyone can view avatars" on storage.objects
  for select using (bucket_id = 'avatars');

create policy "manage your own avatar" on storage.objects
  for insert with check (
    bucket_id = 'avatars'
    and exists (
      select 1 from members m
      where m.account_id = auth.uid()
        and m.id::text = (storage.foldername(name))[1]
    )
  );

create policy "delete your own avatar" on storage.objects
  for delete using (
    bucket_id = 'avatars'
    and exists (
      select 1 from members m
      where m.account_id = auth.uid()
        and m.id::text = (storage.foldername(name))[1]
    )
  );

alter table public.members add constraint avatar_requires_account
  check (avatar_path is null or account_id is not null);
