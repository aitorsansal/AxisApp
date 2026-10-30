-- ============================================================
-- Zero-decimal currencies, part 2: the equal-split fairness check in save_expense() and
-- save_recurring_expense() was hard-coded to one CENT (0.01), so splitting 1501 JPY into 751 + 750
-- was refused with "Equal shares may differ by at most one cent". The allowed difference is now one
-- smallest unit of the expense's currency (public.currency_minor_step: 0.01, or 1 for JPY, KRW and
-- ISK). Everything else in both functions is unchanged. Run once in the Supabase SQL editor
-- (after zero_decimal_currencies.sql); folded into schema.sql. Rerunnable (create or replace).
-- ============================================================

create or replace function public.currency_minor_step(p_currency text)
returns numeric
language sql
immutable
as $$
  select case when public.currency_minor_units(p_currency) = 0 then 1::numeric else 0.01 end;
$$;

create or replace function public.save_expense(p_expense jsonb, p_shares jsonb)
returns uuid
language plpgsql
security definer
set search_path = public
as $$
declare
  v_id uuid := nullif(p_expense->>'id', '')::uuid;
  v_sent_group_id uuid := nullif(p_expense->>'group_id', '')::uuid;
  v_payer uuid := (p_expense->>'paid_by_member_id')::uuid;
  v_group_id uuid;
  v_created_by uuid;
  v_old_payer uuid;
  v_amount numeric;
  v_old_amount numeric;
  v_old_split_mode text;
  v_split_mode text := p_expense->>'split_mode';
  v_share_count int;
  v_share_sum numeric;
begin
  if auth.uid() is null then
    raise exception 'Not signed in';
  end if;

  if p_shares is null or jsonb_typeof(p_shares) <> 'array' or jsonb_array_length(p_shares) = 0 then
    raise exception 'An expense needs at least one share';
  end if;

  if v_id is null then
    v_group_id := v_sent_group_id;
    v_created_by := auth.uid();
  else
    -- Authorize against the STORED row, never against p_expense — this function
    -- bypasses RLS, so trusting the caller's group_id here would let anyone edit
    -- any expense by id.
    select group_id, created_by, paid_by_member_id, amount, split_mode
      into v_group_id, v_created_by, v_old_payer, v_old_amount, v_old_split_mode
      from expenses
     where id = v_id for update;

    if not found then
      raise exception 'Expense % not found', v_id;
    end if;

    -- Tolerates an omitted group_id (treated as unchanged); rejects a different
    -- one rather than silently ignoring it. An expense never moves between groups.
    if v_sent_group_id is not null and v_sent_group_id is distinct from v_group_id then
      raise exception 'An expense cannot move between groups';
    end if;
  end if;

  -- Replaces the dropped "insert/update expenses in your groups" RLS policies,
  -- including their unscoped (dissolved-group) creator-only branch.
  if v_group_id is not null then
    if not is_group_member(v_group_id) then
      raise exception 'You are not a member of this group';
    end if;
  elsif v_created_by is distinct from auth.uid() then
    raise exception 'You can only edit expenses you created';
  end if;

  -- Replaces enforce_payer_in_group (skipped now this runs as definer), same
  -- "only when being set or changed" rule.
  if v_group_id is not null
     and (v_id is null or v_payer is distinct from v_old_payer)
     and not exists (
       select 1 from group_members
        where group_id = v_group_id and member_id = v_payer
     ) then
    raise exception 'The payer must be a member of this group';
  end if;

  -- Replaces enforce_share_member_in_group, same rule: only someone being ADDED
  -- to the split has to be a current member, so an old expense whose participant
  -- has since left the group stays correctable. A null/garbage member_id also
  -- lands here rather than reaching the insert.
  if v_group_id is not null and exists (
    select 1
      from jsonb_array_elements(p_shares) s
     where not exists (
             select 1 from expense_shares es
              where es.expense_id = v_id
                and es.member_id = (s->>'member_id')::uuid
           )
       and not exists (
             select 1 from group_members gm
              where gm.group_id = v_group_id
                and gm.member_id = (s->>'member_id')::uuid
           )
  ) then
    raise exception 'Everyone in the split must be a member of this group';
  end if;

  if v_split_mode is not null and v_split_mode not in ('equal', 'manual') then
    raise exception 'Invalid split mode';
  end if;

  -- Older clients omit the key. Preserve intent on unchanged splits, but
  -- invalidate Equal after an old client changes the amount or any share.
  if not (p_expense ? 'split_mode') and v_id is not null then
    v_split_mode := v_old_split_mode;
    if v_old_split_mode = 'equal' and (
      v_old_amount is distinct from (p_expense->>'amount')::numeric
      or (select jsonb_object_agg(member_id::text, share_amount)
            from expense_shares where expense_id = v_id)
         is distinct from
         (select jsonb_object_agg(((s->>'member_id')::uuid)::text, (s->>'share_amount')::numeric)
            from jsonb_array_elements(p_shares) s)
    ) then
      v_split_mode := null;
    end if;
  end if;

  if coalesce((p_expense->>'is_settlement')::boolean, false) then
    v_split_mode := null;
  end if;

  if v_id is null then
    insert into expenses (
      group_id, paid_by_member_id, amount, currency, description, category,
      occurred_at, receipt_path, is_settlement, event_id, created_by, split_mode
    ) values (
      v_group_id,
      v_payer,
      (p_expense->>'amount')::numeric,
      p_expense->>'currency',
      coalesce(p_expense->>'description', ''),
      coalesce(p_expense->>'category', ''),
      coalesce((p_expense->>'occurred_at')::timestamptz, now()),
      nullif(p_expense->>'receipt_path', ''),
      coalesce((p_expense->>'is_settlement')::boolean, false),
      nullif(p_expense->>'event_id', '')::uuid,
      auth.uid(),
      v_split_mode
    )
    returning id into v_id;
  else
    update expenses set
      paid_by_member_id = v_payer,
      amount = (p_expense->>'amount')::numeric,
      currency = p_expense->>'currency',
      split_mode = v_split_mode,
      description = coalesce(p_expense->>'description', ''),
      category = coalesce(p_expense->>'category', ''),
      occurred_at = coalesce((p_expense->>'occurred_at')::timestamptz, occurred_at),
      receipt_path = nullif(p_expense->>'receipt_path', ''),
      is_settlement = coalesce((p_expense->>'is_settlement')::boolean, is_settlement),
      event_id = nullif(p_expense->>'event_id', '')::uuid
    where id = v_id;

    if not found then
      raise exception 'Expense % not found', v_id;
    end if;

    delete from expense_shares
    where expense_id = v_id
      and member_id not in (select (s->>'member_id')::uuid from jsonb_array_elements(p_shares) s);
  end if;

  insert into expense_shares (expense_id, member_id, share_amount)
  select v_id, (s->>'member_id')::uuid, (s->>'share_amount')::numeric
  from jsonb_array_elements(p_shares) s
  on conflict (expense_id, member_id) do update set share_amount = excluded.share_amount;

  select amount into v_amount from expenses where id = v_id;
  select count(*), coalesce(sum(share_amount), 0) into v_share_count, v_share_sum
  from expense_shares where expense_id = v_id;

  if exists (select 1 from expense_shares where expense_id = v_id and share_amount <= 0) then
    raise exception 'Every share must be greater than 0';
  end if;
  if v_share_sum <> v_amount then
    raise exception 'Shares (%) must add up to the expense amount (%)', v_share_sum, v_amount;
  end if;
  if (select is_settlement from expenses where id = v_id) and v_share_count <> 1 then
    raise exception 'A settlement must have exactly one share';
  end if;

  -- Equal shares may differ by one smallest unit of the expense currency: a cent, or a whole
  -- yen for JPY, KRW and ISK (public.currency_minor_step).
  if v_split_mode = 'equal' and (
    select max(share_amount) - min(share_amount) > public.currency_minor_step(
      (select currency from expenses where id = v_id))
    from expense_shares where expense_id = v_id
  ) then
    raise exception 'Equal shares may differ by at most one smallest currency unit';
  end if;

  return v_id;
end;
$$;

create or replace function public.save_recurring_expense(p_template jsonb, p_shares jsonb)
returns uuid
language plpgsql
security definer
set search_path = public
as $$
declare
  v_id uuid := nullif(p_template->>'id', '')::uuid;
  v_sent_group_id uuid := nullif(p_template->>'group_id', '')::uuid;
  v_payer uuid := (p_template->>'paid_by_member_id')::uuid;
  v_group_id uuid;
  v_created_by uuid;
  v_old_payer uuid;
  v_amount numeric;
  v_old_amount numeric;
  v_old_split_mode text;
  v_split_mode text := p_template->>'split_mode';
  v_share_sum numeric;
begin
  if auth.uid() is null then
    raise exception 'Not signed in';
  end if;

  if p_shares is null or jsonb_typeof(p_shares) <> 'array' or jsonb_array_length(p_shares) = 0 then
    raise exception 'A repeating expense needs at least one share';
  end if;

  if v_id is null then
    v_group_id := v_sent_group_id;
    v_created_by := auth.uid();
  else
    select group_id, created_by, paid_by_member_id, amount, split_mode
      into v_group_id, v_created_by, v_old_payer, v_old_amount, v_old_split_mode
      from recurring_expenses
     where id = v_id for update;

    if not found then
      raise exception 'Repeating expense % not found', v_id;
    end if;

    if v_sent_group_id is not null and v_sent_group_id is distinct from v_group_id then
      raise exception 'A repeating expense cannot move between groups';
    end if;
  end if;

  if v_group_id is not null then
    if not is_group_member(v_group_id) then
      raise exception 'You are not a member of this group';
    end if;
  elsif v_created_by is distinct from auth.uid() then
    raise exception 'You can only edit repeating expenses you created';
  end if;

  if v_group_id is not null
     and (v_id is null or v_payer is distinct from v_old_payer)
     and not exists (
       select 1 from group_members
        where group_id = v_group_id and member_id = v_payer
     ) then
    raise exception 'The payer must be a member of this group';
  end if;

  if v_group_id is not null and exists (
    select 1
      from jsonb_array_elements(p_shares) s
     where not exists (
             select 1 from recurring_expense_shares res
              where res.recurring_expense_id = v_id
                and res.member_id = (s->>'member_id')::uuid
           )
       and not exists (
             select 1 from group_members gm
              where gm.group_id = v_group_id
                and gm.member_id = (s->>'member_id')::uuid
           )
  ) then
    raise exception 'Everyone in the split must be a member of this group';
  end if;

  if v_split_mode is not null and v_split_mode not in ('equal', 'manual') then
    raise exception 'Invalid split mode';
  end if;

  -- Older clients omit the key. Preserve intent on unchanged splits, but
  -- invalidate Equal after an old client changes the amount or any share.
  if not (p_template ? 'split_mode') and v_id is not null then
    v_split_mode := v_old_split_mode;
    if v_old_split_mode = 'equal' and (
      v_old_amount is distinct from (p_template->>'amount')::numeric
      or (select jsonb_object_agg(member_id::text, share_amount)
            from recurring_expense_shares where recurring_expense_id = v_id)
         is distinct from
         (select jsonb_object_agg(((s->>'member_id')::uuid)::text, (s->>'share_amount')::numeric)
            from jsonb_array_elements(p_shares) s)
    ) then
      v_split_mode := null;
    end if;
  end if;

  if v_id is null then
    insert into recurring_expenses (
      group_id, paid_by_member_id, amount, currency, description, category,
      frequency, start_date, created_by, split_mode
    ) values (
      v_group_id,
      v_payer,
      (p_template->>'amount')::numeric,
      p_template->>'currency',
      coalesce(p_template->>'description', ''),
      coalesce(p_template->>'category', ''),
      p_template->>'frequency',
      (p_template->>'start_date')::date,
      auth.uid(),
      v_split_mode
    )
    returning id into v_id;
  else
    update recurring_expenses set
      paid_by_member_id = v_payer,
      amount = (p_template->>'amount')::numeric,
      currency = p_template->>'currency',
      split_mode = v_split_mode,
      description = coalesce(p_template->>'description', ''),
      category = coalesce(p_template->>'category', ''),
      frequency = p_template->>'frequency',
      start_date = (p_template->>'start_date')::date
    where id = v_id;

    if not found then
      raise exception 'Repeating expense % not found', v_id;
    end if;

    delete from recurring_expense_shares
    where recurring_expense_id = v_id
      and member_id not in (select (s->>'member_id')::uuid from jsonb_array_elements(p_shares) s);
  end if;

  insert into recurring_expense_shares (recurring_expense_id, member_id, share_amount)
  select v_id, (s->>'member_id')::uuid, (s->>'share_amount')::numeric
  from jsonb_array_elements(p_shares) s
  on conflict (recurring_expense_id, member_id) do update set share_amount = excluded.share_amount;

  select amount into v_amount from recurring_expenses where id = v_id;
  select coalesce(sum(share_amount), 0) into v_share_sum
  from recurring_expense_shares where recurring_expense_id = v_id;

  if exists (select 1 from recurring_expense_shares where recurring_expense_id = v_id and share_amount <= 0) then
    raise exception 'Every share must be greater than 0';
  end if;
  if v_share_sum <> v_amount then
    raise exception 'Shares (%) must add up to the repeating expense amount (%)', v_share_sum, v_amount;
  end if;

  if v_split_mode = 'equal' and (
    select max(share_amount) - min(share_amount) > public.currency_minor_step(
      (select currency from recurring_expenses where id = v_id))
    from recurring_expense_shares where recurring_expense_id = v_id
  ) then
    raise exception 'Equal shares may differ by at most one smallest currency unit';
  end if;

  return v_id;
end;
$$;

revoke execute on function public.save_expense(jsonb, jsonb) from public, anon;
grant execute on function public.save_expense(jsonb, jsonb) to authenticated;
revoke execute on function public.save_recurring_expense(jsonb, jsonb) from public, anon;
grant execute on function public.save_recurring_expense(jsonb, jsonb) to authenticated;
