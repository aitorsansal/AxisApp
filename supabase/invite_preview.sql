-- Read-only preview of an invite for the Join screen (QA finding W18): lets the person see which
-- group they are about to join, or which phantom they would claim, before pressing Join. Run
-- once against the live project; schema.sql already carries it for fresh installs. Nothing here
-- alters existing rows.
--
-- Why SECURITY DEFINER: a non-member can't read groups/invites/members under RLS, so there is
-- no plain-policy way to show them this. The exposure is deliberately narrow — only someone who
-- already holds a valid token gets anything, and only for statuses where joining is possible:
--   * the group's name, currency and member COUNT (never the member list);
--   * for a claim invite, the display name of the phantom being claimed.
-- Every other status (invalid, expired, used, inviter_gone, claimed, shared_group) returns the
-- status alone, so a dead token reveals nothing about its group. The inviter's name is left out
-- on purpose: default display names are email addresses.
--
-- The checks mirror redeem_invite() in the same order and take no row lock and burn no use. If
-- redeem_invite's rules change, change this too. 'shared_group' covers redeem_invite's "You're
-- already in a group with this person" rejection; 'already_member' is its no-op fresh-join path.
create or replace function public.preview_invite(p_token text)
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
  v_invite invites%rowtype;
  v_existing_member_id uuid;
  v_group groups%rowtype;
begin
  if auth.uid() is null then
    raise exception 'Not signed in';
  end if;

  select * into v_invite from invites where token = p_token;
  if not found then
    return jsonb_build_object('status', 'invalid');
  end if;
  if v_invite.expires_at < now() then
    return jsonb_build_object('status', 'expired');
  end if;
  if v_invite.use_count >= v_invite.max_uses then
    return jsonb_build_object('status', 'used');
  end if;

  if v_invite.created_by is null or not exists (
    select 1
    from group_members gm
    join members m on m.id = gm.member_id
    where gm.group_id = v_invite.group_id
      and m.account_id = v_invite.created_by
  ) then
    return jsonb_build_object('status', 'inviter_gone');
  end if;

  select id into v_existing_member_id from members where account_id = auth.uid() limit 1;

  if v_invite.target_member_id is not null then
    if not exists (
      select 1
      from members m
      join group_members gm on gm.member_id = m.id
      where m.id = v_invite.target_member_id
        and m.account_id is null
        and gm.group_id = v_invite.group_id
    ) then
      return jsonb_build_object('status', 'claimed');
    end if;

    if v_existing_member_id is not null and exists (
      select 1
      from group_members gm_phantom
      join group_members gm_me
        on gm_me.group_id = gm_phantom.group_id
       and gm_me.member_id = v_existing_member_id
      where gm_phantom.member_id = v_invite.target_member_id
    ) then
      return jsonb_build_object('status', 'shared_group');
    end if;
  elsif v_existing_member_id is not null and exists (
    select 1 from group_members
    where group_id = v_invite.group_id and member_id = v_existing_member_id
  ) then
    select * into v_group from groups where id = v_invite.group_id;
    return jsonb_build_object(
      'status', 'already_member',
      'group_name', v_group.name,
      'group_currency', v_group.currency,
      'member_count', (select count(*) from group_members where group_id = v_group.id)
    );
  end if;

  select * into v_group from groups where id = v_invite.group_id;
  return jsonb_build_object(
    'status', 'ok',
    'group_name', v_group.name,
    'group_currency', v_group.currency,
    'member_count', (select count(*) from group_members where group_id = v_group.id),
    'target_name', (select display_name from members where id = v_invite.target_member_id)
  );
end;
$$;

-- Every function in public is executable by PUBLIC (anon included) by default.
revoke execute on function public.preview_invite(text) from public, anon;
grant execute on function public.preview_invite(text) to authenticated;
