# Security & ledger audit

Started 2026-09-17. Covers RLS / SECURITY DEFINER functions, money and ledger correctness,
auth and session handling, events/RSVP, and secrets hygiene, across the MAUI app, the web app,
Supabase (project `foepkovwmwyygulbdahv`) and Firebase / Google Cloud (project `axisapp-ee018`).

This file is the catch-up point: what was found, what's fixed (and where), what was changed
outside the repo, and what's still open. Update the "Still open" list as items get done.

---

## Rules that came out of this audit (keep following them)

- **Column locks are `protect_*` BEFORE triggers keyed on `current_user in ('authenticated','anon')`**,
  not `auth.uid()` checks. App requests through PostgREST run as `authenticated`; SECURITY DEFINER
  functions, pg_cron jobs and service-role Edge Functions run as their owner / `service_role`, so
  legitimate server-side paths keep working without bypass flags. Locked columns are *silently
  restored* rather than raising, because MAUI's `Update(model)` sends the whole row back.
- **Same-event row triggers fire alphabetically.** `expenses_snapshot_currency_conversion` must keep
  sorting before `protect_expense_columns` (and the share equivalents), or converted amounts break.
- **`group_members` has no DELETE policy on purpose.** Removal only goes through `leave_group()` /
  `remove_group_member()` (both SECURITY DEFINER). Its INSERT policy is phantom-only.
- **Phantom claims:** any member may create a claim invite, but `redeem_invite()` rejects redeemers
  who already belong to any group the phantom is in. One claim still carries over all linked groups
  (by design). Accepted residual risk: a member could pass a claim code to an outside, allowlisted
  account.
- **Edge Functions `send-push`, `cleanup-receipts`, `fetch-exchange-rates` rely on "Verify JWT" staying
  ON.** Their `isServiceRoleCaller()` trusts the token's `role` claim only because the gateway already
  verified the signature. `calendar-feed` is the only function with Verify JWT off (intended).
- **pg_net triggers/cron authenticate with the legacy `service_role` JWT stored in Vault
  (`service_role_key`).** Don't disable legacy API keys until that Vault secret is migrated.
- **`expense_shares` / `recurring_expense_shares` have no INSERT/UPDATE/DELETE policies on purpose.**
  Every share write goes through `save_expense()` / `save_recurring_expense()` (both SECURITY DEFINER).
  This is what keeps the audit trail complete: `record_expense_history` fires AFTER UPDATE on
  `expenses` and reads `expense_shares` at that instant, and `save_expense` always updates the row
  *before* touching shares, so `old_shares` is the pre-edit split. Don't re-add a direct write policy
  without also giving `expense_shares` its own history trigger.
- **A SECURITY DEFINER function skips every `current_user`-keyed trigger**, so `save_expense()` /
  `save_recurring_expense()` re-state `enforce_payer_in_group` and `enforce_share_member_in_group`
  inline. If either trigger's rule changes, change it in both places. Their UPDATE paths must keep
  re-resolving `group_id`/`created_by`/`paid_by_member_id` from the **stored row**, never from the
  caller's JSON — with RLS bypassed, trusting the input would let anyone edit any expense by id.
- New migrations are applied by hand in the Supabase SQL editor **and** folded into `schema.sql`.

---

## Fixed

### Database / RLS — `supabase/rls_hardening.sql` (commit `47ebfe1`, applied live)

| Severity | Finding | Fix |
|---|---|---|
| Critical | Phantom creator could set `members.account_id` to themselves and inherit every group the phantom was linked into | `protect_member_identity_columns` trigger; update policy limited to own row or own *unclaimed* phantom; insert can't target another account |
| Critical | Any member could mint a claim invite for any phantom id and redeem it themselves, absorbing its history and all its groups | Invite target must be a phantom in the invite's group (`is_phantom_in_group`); `redeem_invite` rejects redeemers already in any of the phantom's groups |
| High | Any member could PATCH `groups.created_by` and then dissolve the group | `enforce_group_owner_only_columns` also locks `id/created_by/created_at` |
| High | Any member could add any member row (including real accounts) to their group | `group_members` INSERT is phantom-only and requires visibility (`is_linkable_phantom`) |
| High | Removed members could rejoin with old invites; `use_count` resettable; invites not revocable | `redeem_invite` requires the inviter to still be a member; `protect_invite_columns`; DELETE policy added |
| Medium | Direct DELETE on `group_members` bypassed `leave_group` / `remove_group_member` rules | Both direct-delete policies dropped; `leave_group` made SECURITY DEFINER (also deletes the leaver's RSVPs) |
| Medium | Event `created_by` / `group_id` / `is_birthday` editable by any member | `protect_event_columns` (also stops duplicate reminders: `reminder_sent_at` only resets when `starts_at` changes) |
| Medium | RSVP rows editable after leaving and movable onto other groups' events | RSVP update policy requires membership + WITH CHECK; `protect_rsvp_keys` |
| Low | Push recipients included ex-members | Recipient functions and `notify_event_cancelled` join current `group_members` |
| Medium | Recurring templates kept charging dissolved groups and ex-members | `materialize_recurring_expenses` deactivates those templates first |
| Low | `anon` could EXECUTE mutating SECURITY DEFINER RPCs | Revoked from `public, anon` on `redeem_invite`, `create_group`, `leave_group`, `remove_group_member`, `transfer_group_ownership`; `auth.uid() is null` guards |
| Low | Live drift: `categories` table (world-readable) and `payment_notification_recipients()` still existed | Dropped |
| — | One live claim invite targeting a phantom outside its group | Expired (not deleted) |

### Expense integrity — `supabase/expense_integrity.sql` (applied live; committed together with this document)

| Severity | Finding | Fix |
|---|---|---|
| Medium | `created_by` spoofable on insert (also hid the expense from the real person's push) | `protect_expense_columns` forces `created_by = auth.uid()`; locks `id/group_id/created_by/created_at` and templates' `last_processed_date` |
| Medium | Converted amounts (`amount_in_group_currency`, `share_amount_in_group_currency`) writable directly, bypassing the conversion triggers | Restored on update unless the source amount/currency changed (`protect_expense_columns`, `protect_expense_share_columns`) |
| Medium | Payer / share-holders not required to be group members | `enforce_payer_in_group`, `enforce_share_member_in_group` (only on add/change, so old expenses with ex-members stay editable) |
| High (partial) | Every edit re-converted foreign expenses at today's rate | Description-only edits now keep the original conversion. Changing the amount still uses today's rate — see "Still open" |

Verified live as an authenticated member of `TestGroupForEvents` inside a rolled-back transaction:
forged conversion restored, non-member share/payer blocked, spoofed creator pinned, normal
`save_expense` edit works.

### Currency integrity, expense history, delete rule — `supabase/currency_integrity.sql` (applied live)

| Severity | Finding | Fix |
|---|---|---|
| High | Changing an old foreign-currency expense's amount re-converted it at today's rate | `snapshot_expense_currency_conversion` keeps `old.exchange_rate` unless the currency changes |
| High | No audit trail; any group member could hard-delete any expense | `expense_history` (old row + shares as JSON, `changed_by`, on every update/delete; readable by group members). Delete limited to creator, payer or group owner (`enforce_expense_delete_permission`, raises so clients show an error; both clients hide the button otherwise). Repeating templates aren't restricted |
| Medium | Per-share rounding left `amount_in_group_currency` a cent off the shares' sum, so group balances didn't sum to zero and leave/remove could get stuck | Deferred constraint trigger `sync_expense_converted_total` sets the total to the sum of converted shares at commit |
| Medium (new) | Group creator could change `groups.currency` after expenses existed, mixing currencies in balances | `enforce_group_currency_locked` |

Verified live in a rolled-back transaction as three real accounts of `TestGroupForEvents`: USD
10.00 split 3.33/3.33/3.34 gave total 8.68 = share sum (old logic 8.67), group sum 0; amount edit
after a rate change kept the rate; currency edit re-fetched it; forged total restored; 3 history rows
with the right `changed_by`; non-creator/payer/owner delete refused; owner and payer deletes allowed;
currency change refused. Live data had no drift and no foreign-currency expenses before applying.

### Share writes — `supabase/share_writes_via_rpc.sql` (applied live 2026-09-18)

Found on a second pass over the same RLS surface, after the three migrations above.

| Severity | Finding | Fix |
|---|---|---|
| High | `expense_shares` had open INSERT/UPDATE/DELETE policies, so a group member could `PATCH` a single share's amount directly and skip `save_expense`'s split invariants (which exist only inside that function — nothing backs them on the table) | The three direct write policies dropped on `expense_shares` and `recurring_expense_shares`; `save_expense()` / `save_recurring_expense()` become SECURITY DEFINER with the dropped policies' rule, `enforce_payer_in_group` and `enforce_share_member_in_group` re-stated inline |
| High | The same direct write left **no audit trail**: `sync_expense_converted_total` propagated the new sum into `expenses.amount_in_group_currency`, and that column is exactly the one `record_expense_history`'s skip condition excludes, so nothing was logged | Closed by the same funnel — with all writes going through `save_expense`, the existing `expenses` history trigger covers shares too |
| Medium | Redistributing a split between two members (Bob 50→80, Carol 50→20) kept the sum intact, so a sum-check constraint alone would not have closed this | Same funnel |
| Low | `save_expense` / `save_recurring_expense` were executable by `public`/`anon` (harmless while they ran as the caller and hit RLS; not harmless now they run as owner) | `revoke ... from public, anon` + `grant ... to authenticated`, matching every other mutating RPC |
| Low | `protect_expense_share_columns` didn't lock `member_id` | Locked (dead code for app requests now, defense in depth) |

Verified in the repo before applying: both clients write exclusively through the RPC
(`SupabaseExpensesRepository.SaveAsync`, `SupabaseRecurringExpensesRepository.SaveAsync`,
webapp `AddExpensePage` / `GroupDetailPage` settle) and only ever SELECT the share tables directly;
both always send `group_id`. `materialize_recurring_expenses` (pg_cron, postgres), `redeem_invite`'s
phantom merge, `delete_account` and the delete FK cascade all bypass RLS already, so none of them
relied on the dropped policies. Function bodies in `share_writes_via_rpc.sql` and `schema.sql` are
byte-identical.

Verified live as authenticated `Aitor` in `TestGroupForEvents`, inside a rolled-back transaction that
applied the migration first (9 checks, all passed):

| | Check | Result |
|---|---|---|
| A | insert via `save_expense` | works |
| B | direct `update expense_shares set share_amount` | 0 rows, amount unchanged |
| C | direct `delete from expense_shares` | 0 rows |
| D | direct `insert into expense_shares` | refused by RLS |
| E | redistribute via `save_expense` | logs `expense_history` (0 → 1) |
| F | split that doesn't sum | refused |
| G | non-member payer | "The payer must be a member of this group" |
| H | **edit an expense in a group I'm not in, by id** | "You are not a member of this group" |
| I | non-member share-holder | "Everyone in the split must be a member of this group" |

H is the escalation `SECURITY DEFINER` introduces, so it's the one that matters most. Live data had no
expense outside this account's own groups, so that fixture was built inside the rolled-back
transaction rather than left untested.

Post-apply state confirmed: both share tables have SELECT policies only; both RPCs `prosecdef = true`,
`anon_exec = false`; `expenses_without_shares = 0`, `share_sum_mismatch = 0`,
`groups_not_summing_to_zero = 0`; no test rows left behind.

Still unverified: an old expense with a *departed* participant staying editable. The inlined
share-member check only fires for someone being added (`not exists (... expense_shares es where
es.expense_id = v_id ...)`), which is the same rule `enforce_share_member_in_group` used, but no live
group currently has a departed participant to exercise it against.

### Notification Settle action and session refresh (app code)

- `NotificationActionReceiver.HandleSettleAsync` settles `min(my share in group currency, current
  pairwise debt)` in the group currency, read from the server at tap time; writes nothing once the
  debt is gone (repeat taps / second device); in-flight guard against double taps.
- New: in-app Settle (`GroupExpensesViewModel.Settle`) didn't set the currency, so in a non-EUR group
  the balance amount was recorded as EUR and re-converted. Now uses the group currency.
- `IAuthService.EnsureFreshSessionAsync`: refreshes when the token expires within a minute (single
  flight). Called on `Window.Resumed`, in `WidgetDataAccess` (widgets and notification actions), and
  by `BaseViewModel.RunSafeAsync` (forced) after a "JWT expired" rejection, then retries once. Before,
  that rejection matched the clock-skew retry (both are PGRST303) and retried with the same expired
  token. Not yet verified on a device; see "Pending checks".

### Ledger atomicity (other session — commit `9ec9289`)

- `save_expense()` / `save_recurring_expense()` (`supabase/atomic_expense_save.sql`): expense + shares
  in one transaction, shares must be > 0 and sum to the amount, settlement must have exactly one share.
- Web app keeps custom splits on edit (split math in cents).

### Edge Functions (applied live; committed together with this document)

- `send-push` (v12), `cleanup-receipts` (v3), `fetch-exchange-rates` (v2): reject any caller whose
  verified token role isn't `service_role` (403). Before, any signed-in user could re-send pushes or
  send arbitrary "event cancelled" text to arbitrary tokens.
- Verified: service-role call via pg_net returns 200 for `send-push` and `fetch-exchange-rates`;
  helper unit-tested against authenticated / anon / missing / garbage / service_role tokens.
  Not verified live: a real signed-in user being refused; `cleanup-receipts` run (would delete files).

### Auth — email confirmation (commits `cf87473`, `8192d63`)

- Supabase Auth "Confirm email" turned ON (was auto-confirm: allowlisted emails could be pre-registered
  by anyone, then Google sign-in would link to the attacker's account).
- `web/confirm/index.html` landing page; `web/email-templates/confirm-signup.html` template.
- MAUI and web sign-up show "check your inbox"; unconfirmed / wrong-password sign-in messages;
  web app resend button with rate-limit message.
- Register page's name/birthday now travel as sign-up metadata, applied by `handle_new_user_member()`
  (`supabase/signup_profile_metadata.sql`).

### Changed outside the repo (dashboard / console only)

| Where | Change |
|---|---|
| Supabase Auth | Confirm email ON; `site_url` → `https://axisapp.aitorsansal.com/confirm/`; `https://axisapp.aitorsansal.com/confirm/` added to redirect allow-list; "Confirm signup" template + subject set |
| Google Cloud — Browser key | Website restriction `https://app.axisapp.aitorsansal.com/*`, `http://localhost:5173/*`; APIs limited to FCM Registration, Firebase Cloud Messaging, Firebase Installations (was 25) |
| Google Cloud — Android key | Android restriction `com.aitorsansal.axisapp` + SHA-1 `23:91:E0:80:CB:18:E2:45:96:E0:37:C2:F7:AC:FF:7E:6D:44:5A:CC` (Play App Signing), `94:66:69:AC:32:92:0B:4F:1E:71:EB:B4:40:B4:6B:36:39:BB:4E:DB` (`axisapp.keystore` upload key), `BE:9C:FC:CC:03:2B:0D:EC:45:39:FF:15:49:F8:61:63:8E:C4:5C:34` (default debug keystore). API list (8 Firebase APIs) unchanged |

Note: both Google keys showed recent traffic to Google Maps APIs (Directions, Geocoding, Places, …)
that Axis never calls — the keys had been scraped. Now blocked by the restrictions. Check Billing on
`axisapp-ee018` if not done yet.

Signing certificates, for reference: Play Store installs are signed by Google's Play App Signing key
(SHA-256 `E4:1B:C6:C9:…`), which is what `web/.well-known/assetlinks.json` and Firebase list. Builds
installed straight from this machine use `axisapp.keystore` (SHA-256 `CF:F3:F3:3C:…`), which is **not**
in `assetlinks.json`, so invite App Links won't auto-verify for those local builds.

---

## Still open

Ordered by severity. Locations are relative to the repo root.

### Medium

1. **Leaving strands pairwise debts** (`supabase/schema.sql`, `leave_group` / `remove_group_member`):
   both gate on the member's `group_balances` net (against the whole pot) being zero, which doesn't
   imply their *pairwise* debts are zero — and Pairwise is a first-class display mode. Reachable with
   three members: A pays 100 split A/B; B pays 100 split B/C. B's net is `+100 − 50 − 50 = 0`, so B may
   leave while owing A 50 and being owed 50 by C. Those rows survive (`pairwise_balances` never joins
   `group_members`) and become **unsettleable**: a settle-up naming B is refused by
   `enforce_payer_in_group` / `enforce_share_member_in_group`. Simplified mode recovers (A +50 / C −50);
   Pairwise shows two dead debts. Fix: check pairwise edges, not just the pot net, in both functions —
   or allow an ex-member specifically on `is_settlement` rows so stranded debts stay clearable.
2. **Dissolve loses the audit trail** (`supabase/schema.sql`, `delete own groups` +
   `select expense history in your groups`): dissolve is owner-only with no balance guard (deliberate,
   client-side confirm only), but it sets `expenses.group_id = null` while `record_expense_history`
   stores `old.group_id` and the history SELECT policy requires `group_id is not null` — so the whole
   group's `expense_history` becomes permanently unreadable at exactly the moment members want it.
   Fix: widen the history read policy with `is_unscoped_expense_party(expense_id)`, and/or replace the
   delete policy with a `dissolve_group()` RPC that refuses while any balance is non-zero.

3. **Windows Google sign-in** (`AxisApp/Platforms/Windows/GoogleAuthService.cs`): implicit flow, fixed
   port 48291, no `state` — any open web page can inject its own tokens during the 2-minute window
   (login CSRF). Fix: random `state` round-tripped and checked, `GetUser` before `SetSession`, random port.
4. **Stale RSVP counts** (`AxisApp/ViewModels/EventDetailViewModel.cs`): loads only on navigation /
   pull-to-refresh; RSVP taps patch a stale cached list. Fix: re-fetch attendees after each write,
   reload on resume if older than ~60s, show "updated X min ago".

### Low

5. **10 expenses with NULL `created_by`** (web app inserts from 2026-09-12 to 2026-09-16, before
   `save_expense`). No reliable way to know the real creator; left as-is.
6. **Supabase config leftovers:** `http://localhost:5173/**` still in the production redirect allow-list;
   minimum password length 6 and leaked-password protection off; legacy JWT API keys still enabled
   (migrate the Vault `service_role_key` to the new secret key first); Vault still holds an unused copy of
   the Firebase service-account key (`firebase_service_account`).
7. **Android Google sign-in has no nonce** (`AxisApp/Platforms/Android/GoogleAuthService.cs`). Fix: hashed
    nonce to `GetGoogleIdOption.SetNonce`, raw nonce to `SignInWithIdToken`.
8. **Long-lived secrets copied to the clipboard** (`ProfileViewModel.cs` calendar feed URL,
    `InviteToGroupViewModel.cs` invite URL). Fix: share sheet, or `EXTRA_IS_SENSITIVE` on Android 13+.
9. **`MainActivity` acts on intent extras from any app** (`AxisApp/Platforms/Android/MainActivity.cs`,
    `HandleIntent`) — spoofable screen titles, RLS still protects data.
10. **`android:allowBackup="true"`** (`AxisApp/Platforms/Android/AndroidManifest.xml`) includes the
    SecureStorage prefs; a restored session can't be decrypted on a new device. Exclude them from backup.
11. **Password-reset page leaves the recovery token in browser history** (`web/reset/index.html`).
    Fix: `history.replaceState` after `PASSWORD_RECOVERY`.
12. **RSVP save reads then inserts** (`AxisApp/Services/SupabaseEventsRepository.cs` `UpsertRsvpAsync`):
    concurrent RSVPs can hit a primary-key error. Fix: real upsert through a small RPC.
13. **`is_phantom_in_group()` is anon-executable** (`supabase/schema.sql`): SECURITY DEFINER and, unlike
    the other `is_*` helpers, never consults `auth.uid()` — so `anon` can use it as an oracle for
    "is this member id a phantom in this group". Needs both UUIDs, so it's negligible in practice, but
    it's the one helper the earlier `revoke ... from public, anon` pass didn't cover. Fix: revoke it
    (and the other helpers, for consistency).
14. **Signup allow-list is case-sensitive on the stored side** (`supabase/schema.sql`,
    `restrict_signup_to_allowlist`): compares `email = lower(new.email)` but nothing normalises
    `allowed_signup_emails.email`, so a row inserted as `Friend@Gmail.com` never matches. Not a bypass
    (unlisted emails are always rejected) — a lockout footgun. Fix: `check (email = lower(email))`.
15. **Receipt storage policies cast an unvalidated path segment** (`supabase/schema.sql`):
    `(storage.foldername(name))[1]::uuid` raises `22P02` from inside policy evaluation when the first
    path segment isn't a UUID, instead of denying. Error-shape only. Fix: guard the cast.
16. **Invite App Links for locally installed builds**: add `axisapp.keystore`'s SHA-256
    (`CF:F3:F3:3C:3A:85:9F:1B:36:5A:51:1C:F4:3A:E2:9A:22:5A:9E:17:61:42:B0:2C:FF:1A:8E:29:09:2B:49:F5`)
    to `web/.well-known/assetlinks.json` if direct installs should open invite links in-app.

### Pending checks (no code)

- On the phone: leave the app in the background for over an hour (screen off), reopen it and do
  something that loads data; it should work without a "JWT expired" error. Same for a notification
  Settle/RSVP tap after a long idle.
- Notification Settle: tap Settle on an expense push; the settlement should be min(your share, what
  you currently owe that person) in the group currency, and a second tap shouldn't add another.
- Real push test after the key restrictions and the `send-push` redeploy (another account adds an
  expense involving you; web app: toggle notifications off/on in Profile).
- Billing on `axisapp-ee018` for any cost from the scraped-key Maps traffic.
- Delete the audit's autosaved "Untitled query" snippets in the Supabase SQL editor.

---

## Re-verifying later

Read-only queries used during the audit (run in the Supabase SQL editor):

```sql
-- RLS on every public table
select relname, relrowsecurity from pg_class
where relnamespace = 'public'::regnamespace and relkind in ('r','p') order by 1;

-- Policies (compare against schema.sql)
select tablename, cmd, policyname, qual, with_check from pg_policies
where schemaname in ('public','storage') order by 1, 2;

-- SECURITY DEFINER functions anon can still execute
select proname, has_function_privilege('anon', oid, 'EXECUTE') as anon_exec
from pg_proc where pronamespace = 'public'::regnamespace and prosecdef order by 1;

-- Audit triggers present
select c.relname, t.tgname from pg_trigger t join pg_class c on c.oid = t.tgrelid
where not t.tgisinternal and (t.tgname like 'protect_%' or t.tgname like 'enforce_%') order by 1, 2;

-- Share tables must have SELECT policies only (share_writes_via_rpc.sql) — expect 0 rows
select tablename, cmd, policyname from pg_policies
where schemaname = 'public'
  and tablename in ('expense_shares', 'recurring_expense_shares')
  and cmd <> 'SELECT';

-- ...and both save RPCs must be SECURITY DEFINER, not anon-executable
select proname, prosecdef, has_function_privilege('anon', oid, 'EXECUTE') as anon_exec
from pg_proc where pronamespace = 'public'::regnamespace
  and proname in ('save_expense', 'save_recurring_expense');

-- Ledger consistency (all should be 0)
select
  (select count(*) from expenses e where not exists (select 1 from expense_shares s where s.expense_id = e.id)) as expenses_without_shares,
  (select count(*) from expenses e where abs(e.amount - coalesce((select sum(share_amount) from expense_shares s where s.expense_id = e.id), 0)) >= 0.01) as share_sum_mismatch,
  (select count(*) from (select group_id from group_balances group by 1 having sum(balance) <> 0) x) as groups_not_summing_to_zero,
  (select count(*) from members where account_id is not null and created_by is distinct from account_id) as claimed_members_created_by_others,
  (select count(*) from invites i where i.target_member_id is not null and i.expires_at > now()
     and not exists (select 1 from group_members gm where gm.group_id = i.group_id and gm.member_id = i.target_member_id)) as live_claim_invites_outside_group;
```

Edge Function settings (Verify JWT per function) and Auth config can be read from the Supabase
Management API: `GET /v1/projects/foepkovwmwyygulbdahv/functions` and `.../config/auth`.
