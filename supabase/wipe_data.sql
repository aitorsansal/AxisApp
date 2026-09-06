-- Wipes all app data while preserving exchange_rates (the rate-cache
-- singleton) and Supabase's own auth.users / storage.objects rows.
-- Run manually in the Supabase SQL editor. NOT part of schema.sql.
--
-- Note: this only clears table rows. It does NOT delete files from the
-- avatars/receipts Storage buckets — run separately if you also want those
-- gone (e.g. `delete from storage.objects where bucket_id in ('avatars','receipts');`
-- plus the actual Storage API calls to remove the underlying files).

truncate table
  public.recurring_expense_shares,
  public.recurring_expenses,
  public.expense_shares,
  public.expenses,
  public.member_aliases,
  public.device_tokens,
  public.invites,
  public.group_members,
  public.groups,
  public.members
restart identity cascade;

-- exchange_rates deliberately left untouched.
