-- Share writes only through save_expense() / save_recurring_expense() (2026-09-17)
-- ============================================================
-- Closes the last ledger-tampering path left after rls_hardening.sql /
-- expense_integrity.sql / currency_integrity.sql.
--
-- The hole: expense_shares had open INSERT/UPDATE/DELETE policies for any group
-- member, so PostgREST accepted direct share writes that never went through
-- save_expense(). Every split invariant (shares non-empty, each > 0, summing
-- exactly to the row's amount, a settlement having exactly one share) lives
-- inside that function only — there is no constraint backing it on the table. So
--
--   PATCH /rest/v1/expense_shares?expense_id=eq.X&member_id=eq.<someone else>
--   {"share_amount": 500}
--
-- moved real money, and left NO audit trail: sync_expense_converted_total then
-- propagated the new sum into expenses.amount_in_group_currency, and that update
-- touches only that one column — exactly the column record_expense_history's skip
-- condition excludes — so the history trigger wrote nothing. Redistributing a
-- split between two members (Bob 50 -> 80, Carol 50 -> 20) kept the sum intact
-- and was equally silent, which is why a sum-check constraint alone would not
-- have been enough.
--
-- The fix is to make the funnel mandatory rather than conventional. Both clients
-- already write exclusively through the RPC (SupabaseExpensesRepository.SaveAsync,
-- SupabaseRecurringExpensesRepository.SaveAsync, webapp's AddExpensePage /
-- GroupDetailPage settle), and they only ever SELECT the share tables directly,
-- so nothing legitimate loses access. With every share write going through
-- save_expense(), the existing audit trail becomes complete for free:
-- record_expense_history is an AFTER UPDATE trigger on expenses that reads
-- expense_shares at that instant, and save_expense always updates the row BEFORE
-- touching shares — so old_shares is genuinely the pre-edit split. That property
-- only holds while the funnel is mandatory.
--
-- 1. The direct INSERT/UPDATE/DELETE policies on expense_shares /
--    recurring_expense_shares are dropped. SELECT policies stay untouched (both
--    the group one and the unscoped is_unscoped_expense_party one) — reads were
--    never the problem and both clients depend on them.
-- 2. save_expense() / save_recurring_expense() become SECURITY DEFINER, since
--    there is no insert policy left for them to run under as the caller.
--
--    This is the one thing to be careful about: a SECURITY DEFINER caller has
--    current_user = the function owner, so every trigger keyed on
--    `current_user in ('authenticated','anon')` now SKIPS these two functions —
--    including enforce_payer_in_group and enforce_share_member_in_group. Those
--    checks are therefore re-stated explicitly below, with identical semantics
--    (membership only checked for someone being ADDED or CHANGED, so an old
--    expense whose participant has since left the group stays editable). The
--    dropped RLS policies' own rule is re-stated the same way.
--
--    The escalation risk this introduces is the UPDATE path: with RLS bypassed,
--    `update expenses ... where id = v_id` would otherwise let any signed-in
--    account edit ANY expense in ANY group by id. So the update path re-resolves
--    group_id/created_by/paid_by_member_id FROM THE STORED ROW and authorizes
--    against that, never against p_expense's own group_id.
--
--    Triggers that still fire normally (not current_user-keyed, or definer
--    themselves): snapshot_expense_currency_conversion,
--    snapshot_expense_share_currency_conversion, record_expense_history,
--    sync_expense_converted_total. protect_expense_columns is skipped, but
--    save_expense never writes id/group_id/created_by/created_at on update and
--    sets created_by = auth.uid() on insert, so its guarantees are unchanged.
-- 3. Both functions get the revoke-from-anon treatment every other mutating RPC
--    already has (they were executable by PUBLIC; harmless while they ran as the
--    caller and hit RLS, not harmless now they run as owner).
-- 4. protect_expense_share_columns also locks member_id. Dead code for app
--    requests once the update policy is gone, kept as defense in depth if a
--    policy is ever re-added.
--
-- Unaffected, all of which bypass RLS already and so never relied on the dropped
-- policies: materialize_recurring_expenses() (pg_cron, runs as postgres),
-- redeem_invite()'s phantom merge (security definer), delete_account() (security
-- definer), Edge Functions (service_role), and the FK cascade that removes shares
-- when an expense is deleted.
--
-- Pre-existing rows that already violate the split invariants are untouched —
-- they simply can't be re-saved until fixed, same as before this migration.
-- supabase/atomic_expense_save_check.sql lists them.
--
-- Safe to run once. Wrapped in a transaction.
-- ============================================================

begin;

-- ------------------------------------------------------------
-- 1. No direct share writes
-- ------------------------------------------------------------
drop policy if exists "insert shares of your expenses" on public.expense_shares;
drop policy if exists "update shares of your expenses" on public.expense_shares;
drop policy if exists "delete shares of your expenses" on public.expense_shares;

drop policy if exists "insert shares of your recurring expenses" on public.recurring_expense_shares;
drop policy if exists "update shares of your recurring expenses" on public.recurring_expense_shares;
drop policy if exists "delete shares of your recurring expenses" on public.recurring_expense_shares;

-- ------------------------------------------------------------
-- 2. save_expense
-- ------------------------------------------------------------
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
    select group_id, created_by, paid_by_member_id
      into v_group_id, v_created_by, v_old_payer
      from expenses
     where id = v_id;

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

  if v_id is null then
    insert into expenses (
      group_id, paid_by_member_id, amount, currency, description, category,
      occurred_at, receipt_path, is_settlement, event_id, created_by
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
      auth.uid()
    )
    returning id into v_id;
  else
    update expenses set
      paid_by_member_id = v_payer,
      amount = (p_expense->>'amount')::numeric,
      currency = p_expense->>'currency',
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

  return v_id;
end;
$$;

revoke execute on function public.save_expense(jsonb, jsonb) from public, anon;
grant execute on function public.save_expense(jsonb, jsonb) to authenticated;

-- ------------------------------------------------------------
-- 2b. save_recurring_expense — same shape, same reasoning
-- ------------------------------------------------------------
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
    select group_id, created_by, paid_by_member_id
      into v_group_id, v_created_by, v_old_payer
      from recurring_expenses
     where id = v_id;

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

  if v_id is null then
    insert into recurring_expenses (
      group_id, paid_by_member_id, amount, currency, description, category,
      frequency, start_date, created_by
    ) values (
      v_group_id,
      v_payer,
      (p_template->>'amount')::numeric,
      p_template->>'currency',
      coalesce(p_template->>'description', ''),
      coalesce(p_template->>'category', ''),
      p_template->>'frequency',
      (p_template->>'start_date')::date,
      auth.uid()
    )
    returning id into v_id;
  else
    update recurring_expenses set
      paid_by_member_id = v_payer,
      amount = (p_template->>'amount')::numeric,
      currency = p_template->>'currency',
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

  return v_id;
end;
$$;

revoke execute on function public.save_recurring_expense(jsonb, jsonb) from public, anon;
grant execute on function public.save_recurring_expense(jsonb, jsonb) to authenticated;

-- ------------------------------------------------------------
-- 4. member_id can't be repointed either (defense in depth)
-- ------------------------------------------------------------
create or replace function public.protect_expense_share_columns()
returns trigger
language plpgsql
as $$
begin
  if current_user not in ('authenticated', 'anon') then
    return new;
  end if;

  new.expense_id := old.expense_id;
  new.member_id := old.member_id;
  if new.share_amount = old.share_amount then
    new.share_amount_in_group_currency := old.share_amount_in_group_currency;
  end if;
  return new;
end;
$$;

commit;
