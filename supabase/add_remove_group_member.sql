-- Run this once against the live project to add remove_group_member(),
-- backing the new Members page's "Remove" action for phantoms. See
-- schema.sql's remove_group_member() remarks for the full explanation.

create or replace function public.remove_group_member(p_group_id uuid, p_member_id uuid)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_target_account uuid;
  v_balance numeric;
begin
  if not is_group_member(p_group_id) then
    raise exception 'You are not a member of this group';
  end if;

  if not exists (
    select 1 from group_members
    where group_id = p_group_id and member_id = p_member_id
  ) then
    raise exception 'That member does not belong to this group';
  end if;

  select account_id into v_target_account from members where id = p_member_id;

  if v_target_account is not null then
    raise exception 'Only a phantom member can be removed this way — a real account must leave on its own';
  end if;

  select balance into v_balance
    from group_balances
   where group_id = p_group_id and member_id = p_member_id;
  v_balance := coalesce(v_balance, 0);

  if v_balance <> 0 then
    raise exception 'Settle this member''s balance before removing them';
  end if;

  delete from group_members where group_id = p_group_id and member_id = p_member_id;
end;
$$;
