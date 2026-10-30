-- One-off cleanup of invites written with broken dates. Optional: nothing depends on it, the
-- clients already skip used and expired rows. Run in the Supabase SQL editor, not rerun-sensitive.
--
-- Background: before the Invite.CreatedAt fix (Android 1.1.1 onwards) every invite the Android app
-- inserted carried created_at = 0001-01-01, and before the earlier ExpiresAt fix some also had
-- expires_at = 0001-01-01. Those rows pile up (GetOrCreateAsync kept minting new ones because it
-- could not tell them apart), and redeem_invite() treats a year-1 expires_at as expired.
--
-- Step 1 - look first. Shows every broken-date invite and whether it is still usable.
select i.id,
       i.token,
       g.name as group_name,
       i.target_member_id is not null as claims_phantom,
       i.created_at,
       i.expires_at,
       i.use_count || '/' || i.max_uses as used,
       (i.expires_at < now() or i.use_count >= i.max_uses) as dead
from public.invites i
join public.groups g on g.id = i.group_id
where i.created_at < timestamptz '2000-01-01' or i.expires_at < timestamptz '2000-01-01'
order by dead desc, g.name, i.expires_at;

-- Step 2 - delete the DEAD ones (expired or fully used). Safe: nobody can redeem these anyway,
-- and no other table references invites. Live invites are left alone, so links and QR codes
-- people were already given keep working.
delete from public.invites
where (created_at < timestamptz '2000-01-01' or expires_at < timestamptz '2000-01-01')
  and (expires_at < now() or use_count >= max_uses);

-- Step 3 (optional) - also collapse LIVE broken-date duplicates, keeping one per
-- (group, target member): the one that expires last. This DOES kill the other links that were
-- already handed out, so only run it if you don't mind that. Uncomment to use.
--
-- delete from public.invites i
-- where i.created_at < timestamptz '2000-01-01'
--   and i.expires_at >= now() and i.use_count < i.max_uses
--   and exists (
--     select 1
--     from public.invites keep
--     where keep.group_id = i.group_id
--       and keep.target_member_id is not distinct from i.target_member_id
--       and keep.expires_at >= now() and keep.use_count < keep.max_uses
--       and (keep.expires_at, keep.id) > (i.expires_at, i.id)
--   );
