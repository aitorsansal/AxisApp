-- One-account-one-member invariant fix — 2026-09-07
--
-- Run this whole file once in the Supabase SQL editor against the live
-- project. It only replaces two existing functions (create_group,
-- redeem_invite) — no table/column changes, safe to run directly, no
-- backfill of already-existing duplicate members rows (that's a deliberately
-- separate follow-up, not included here). See CLAUDE.md's "One-account-
-- one-member invariant fix (2026-09-07)" section for the full writeup of
-- what was broken and why.
--
-- These are the exact same definitions now committed in supabase/schema.sql
-- — copy-pasted here so both functions can be applied in one paste without
-- hunting through the rest of that file.

-- One-account-one-member invariant fixed 2026-09-07 — this function used to
-- unconditionally insert a fresh members row on every call, so an account
-- creating a second/third group ended up with a second/third members row,
-- each with display_name/birth_date unset (ProfilePage only ever edits the
-- oldest one, per GetMyMemberAsync's ORDER BY created_at). Now reuses the
-- account's existing members row (if any) the same way redeem_invite()'s
-- fresh-join branch does below.
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

  select id into v_member_id from public.members where account_id = auth.uid() limit 1;

  if v_member_id is null then
    insert into public.members (account_id, display_name, created_by)
    values (
      auth.uid(),
      coalesce((select email from auth.users where id = auth.uid()), 'New member'),
      auth.uid()
    )
    returning id into v_member_id;
  end if;

  insert into public.group_members (group_id, member_id)
  values (v_group_id, v_member_id)
  on conflict do nothing;

  return v_group_id;
end;
$$;

-- One-account-one-member invariant fixed 2026-09-07 — two bugs here, both
-- letting a single account end up with more than one members row:
--
-- 1. The fresh-join branch's "reuse if it already belongs to the group"
--    lookup was scoped to gm.group_id = v_invite.group_id, so it only found
--    an existing row when the account happened to already be in *this*
--    group. Joining any group the account wasn't already in — which is the
--    normal case — always fell through to a brand-new members row, even
--    though the account may well have had one already from a different
--    group. Fixed by dropping the group_id scoping entirely: an account
--    only ever gets one members row, full stop.
-- 2. The claim branch (target_member_id is not null) never checked whether
--    the claiming account already had a members row of its own. If it did
--    (claiming a phantom in a second group after already being a real
--    member elsewhere), the phantom got claimed as a *second* claimed row
--    for the same account instead of being merged into the existing one.
--    Fixed by merging: when the account already has a members row, every
--    expenses/expense_shares/recurring_expenses/recurring_expense_shares
--    row pointing at the phantom is repointed at the existing row instead
--    (summing share amounts on the rare case both already hold a share on
--    the same expense/template, to avoid violating the (expense_id,
--    member_id) / (recurring_expense_id, member_id) composite PKs and to
--    keep balances correct), the phantom is added nowhere (the existing
--    row joins the group instead), and the now-empty phantom row is
--    deleted — cascading away its own group_members/invites/member_aliases
--    rows, which is fine, those were about the phantom identity that no
--    longer exists.
create or replace function public.redeem_invite(p_token text)
returns uuid
language plpgsql
security definer
set search_path = public
as $$
declare
  v_invite invites%rowtype;
  v_member_id uuid;
  v_existing_member_id uuid;
begin
  select * into v_invite from invites where token = p_token for update;
  if not found then
    raise exception 'Invalid invite code';
  end if;
  if v_invite.expires_at < now() then
    raise exception 'Invite expired';
  end if;
  if v_invite.use_count >= v_invite.max_uses then
    raise exception 'Invite already used';
  end if;

  if v_invite.target_member_id is not null then
    -- Claiming an existing phantom member.
    select id into v_existing_member_id from members where account_id = auth.uid() limit 1;

    if v_existing_member_id is not null then
      -- Account already has a members row — merge the phantom into it
      -- instead of creating a second claimed row for the same account.
      if not exists (
        select 1 from members where id = v_invite.target_member_id and account_id is null
      ) then
        raise exception 'This invite has already been claimed';
      end if;

      update expenses
         set paid_by_member_id = v_existing_member_id
       where paid_by_member_id = v_invite.target_member_id;

      update recurring_expenses
         set paid_by_member_id = v_existing_member_id
       where paid_by_member_id = v_invite.target_member_id;

      -- Sum share amounts where both rows already hold a share on the same
      -- expense, then drop the phantom's now-redundant row before
      -- repointing whatever's left.
      update expense_shares es
         set share_amount = es.share_amount + p.share_amount,
             share_amount_in_group_currency = es.share_amount_in_group_currency
               + p.share_amount_in_group_currency
        from expense_shares p
       where p.member_id = v_invite.target_member_id
         and es.member_id = v_existing_member_id
         and es.expense_id = p.expense_id;

      delete from expense_shares
       where member_id = v_invite.target_member_id
         and expense_id in (
           select expense_id from expense_shares where member_id = v_existing_member_id
         );

      update expense_shares
         set member_id = v_existing_member_id
       where member_id = v_invite.target_member_id;

      update recurring_expense_shares res
         set share_amount = res.share_amount + p.share_amount
        from recurring_expense_shares p
       where p.member_id = v_invite.target_member_id
         and res.member_id = v_existing_member_id
         and res.recurring_expense_id = p.recurring_expense_id;

      delete from recurring_expense_shares
       where member_id = v_invite.target_member_id
         and recurring_expense_id in (
           select recurring_expense_id from recurring_expense_shares
            where member_id = v_existing_member_id
         );

      update recurring_expense_shares
         set member_id = v_existing_member_id
       where member_id = v_invite.target_member_id;

      -- The phantom may already belong to other groups too (e.g. linked into
      -- them by name via JoinGroupPage before ever being claimed), not just
      -- this invite's group — carry all of those memberships over, or
      -- they'd silently vanish when the phantom row cascades away below.
      insert into group_members (group_id, member_id)
      select group_id, v_existing_member_id
        from group_members
       where member_id = v_invite.target_member_id
      on conflict do nothing;

      delete from members where id = v_invite.target_member_id;

      v_member_id := v_existing_member_id;
    else
      update members
         set account_id = auth.uid()
       where id = v_invite.target_member_id
         and account_id is null
      returning id into v_member_id;

      if v_member_id is null then
        raise exception 'This invite has already been claimed';
      end if;
    end if;
  else
    -- Fresh join: reuse this account's members row if it has one, from any
    -- group, otherwise create one.
    select m.id into v_member_id
      from members m
     where m.account_id = auth.uid()
     limit 1;

    if v_member_id is null then
      insert into members (account_id, display_name, created_by)
      select auth.uid(),
             coalesce((select email from auth.users where id = auth.uid()), 'New member'),
             auth.uid()
      returning id into v_member_id;
    end if;
  end if;

  insert into group_members (group_id, member_id)
  values (v_invite.group_id, v_member_id)
  on conflict do nothing;

  update invites set use_count = use_count + 1 where id = v_invite.id;

  return v_invite.group_id;
end;
$$;
