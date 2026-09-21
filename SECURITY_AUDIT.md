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
- **Leave / remove gates check pairwise edges, not just the pot net** (`leave_group`,
  `remove_group_member`). A net of zero doesn't mean no individual debts, and an edge naming someone
  who's no longer a member can't be settled (`enforce_*_in_group`). Keep both checks if either function
  is rewritten.
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

### Audit follow-ups — `supabase/audit_followups.sql` (applied live and verified 2026-09-21, folded into `schema.sql`)

Former open items #1, #2, #13, #14, #15.

| Severity | Finding | Fix |
|---|---|---|
| Medium | Leaving / removing a member gated only on the pot net, so a member with net 0 but real pairwise debts (A pays 100 split A/B, B pays 100 split B/C: B nets 0) could leave, stranding two unsettleable edges | `leave_group` / `remove_group_member` also refuse while any `pairwise_balances` edge is non-zero, naming the counterparties in the error (Simplified mode shows no balance, so a bare "settle your balance" would read as a bug) |
| Medium | Dissolve nulls `expenses.group_id` but the history read policy needed `is_group_member(group_id)`, so the group's whole `expense_history` became unreadable | Policy also allows `is_unscoped_expense_party(expense_id)`. Dissolve stays unguarded on purpose (the app already shows an explicit unsettled-balances confirm); no `dissolve_group()` RPC. Residual: history of an expense deleted *before* the dissolve stays unreadable |
| Low | `is_phantom_in_group()` anon-executable and never consults `auth.uid()` | Revoked from `public, anon`. The other `is_*` helpers left alone: they resolve through `auth.uid()` and give anon nothing, and revoking would turn anon-hit policies into permission-denied errors |
| Low | `allowed_signup_emails` could hold mixed-case rows that never match | Existing rows normalised, `check (email = lower(btrim(email)))` |
| Low | Receipt policies cast a raw path segment to uuid, raising `22P02` inside policy evaluation | `receipt_folder_group_id()` returns null for non-UUIDs; the three policies use it |

Pre-apply on live data: 0 stranded pairwise edges, 0 mixed-case allow-list rows.
Post-apply state: both RPCs reference `pairwise_balances`, stay SECURITY DEFINER and not anon-executable;
`is_phantom_in_group` anon = false / authenticated = true; history policy has the party branch; 3
receipt policies use `receipt_folder_group_id`; email check constraint present; `receipt_folder_group_id('not-a-uuid/x.webp')` is null.

Behavior verified live as the real `TestGroupForEvents` accounts, in a throwaway group inside a `DO`
block that ends in a raised exception (guaranteed rollback; a follow-up query confirmed nothing persisted):

| Check | Result |
|---|---|
| A pays 100 split A/B, B pays 100 split B/C: B's pot net | 0.00, with 2 pairwise edges |
| B `leave_group` | refused: "Your overall balance is zero, but you still have individual debts with: Aitor, test@gmail.com. Settle them (Detailed view) before leaving" |
| After both edges settled via settlement expenses, B `leave_group` | allowed, B gone from `group_members` |
| Group dissolved (expenses go unscoped) | history rows for an edited expense: payer 1, share-holder 1, uninvolved member 0 |

Not exercised live: `remove_group_member`'s new branch (same code shape as `leave_group`, but no phantom
with pairwise-only debts exists in live data), and the receipt policies against a real non-UUID upload path.

### Client batch (app / web code, 2026-09-21 — builds clean on both targets; Windows sign-in confirmed working in a Debug build, the rest not yet run)

Former open items #3, #7, #10, #11.

| Severity | Finding | Fix |
|---|---|---|
| Medium | Windows Google sign-in (implicit flow, fixed port, no state): any web page open in the user's browser could send its own tokens to `localhost:48291` during the 2-minute window and the app adopted that session (login CSRF) | Rewritten as hand-built PKCE (`Platforms/Windows/GoogleAuthService.cs`): random `code_verifier` stays in-process, only its SHA-256 challenge goes in the authorize URL, the `?code=` on the loopback redirect is redeemed at `POST /auth/v1/token?grant_type=pkce`. An injected code was issued against the attacker's challenge, so the exchange fails. The fragment-extractor page is gone. Stray requests (favicon, probes) get a 404 and don't consume the listener |
| Low | Android Google sign-in had no nonce | Hashed nonce to `GetGoogleIdOption.SetNonce`, raw nonce to `SignInWithIdToken(nonce:)` (the parameter exists in the installed gotrue) |
| Low | `allowBackup="true"` copied the SecureStorage prefs (the Supabase session) into backups | `allowBackup="false"`. Chosen over an exclude-rules XML because the exact SharedPreferences file name for SecureStorage couldn't be confirmed from the DLL; the only other local state is per-device display prefs that are deliberately not synced |
| Low | Password-reset page left the recovery token in browser history | `history.replaceState` when `PASSWORD_RECOVERY` fires. Whether the JS client itself already pushed a token-less entry over the top wasn't checked |

**Deviation from the original #3 fix note** ("random `state`, `GetUser` before `SetSession`, random port"):
none of those works. Supabase's implicit flow doesn't round-trip a client-supplied `state`; a nonce in the
loopback page doesn't help when an attacker can navigate the browser to the loopback URL with their own
fragment; a random port needs wildcard `localhost` entries in the redirect allow-list (which #6 wants
*removed*). PKCE by hand is what the SDK failed at originally (`bad_oauth_state`, see the class remarks),
but the hand-built version signed in successfully on Windows (Debug build, 2026-09-21), so that failure was
in the SDK's state handling, not server-side. Not tested: a Release build, or an injected `?code=` from
another page (expected to fail the exchange, since it was issued against a different challenge).

Password-reset link checked in a real browser: the token is still in the email (the link has to carry it)
but is gone from the address bar once the page loads. The reset page's hardcoded minimum moved to 8 with #6.

Still needs a real run: Android Google sign-in (Supabase's Google provider must not have "Skip nonce
checks" in a state that rejects a present nonce).

### Optional code fixes (2026-09-21 — `upsert_rsvp` applied live and verified; client builds clean on both targets, not yet run on a device)

Former open items #4, #8 (calendar URL only) and #12.

| Severity | Finding | Fix |
|---|---|---|
| Low | RSVP write read the row then inserted or updated it, so two concurrent RSVPs from one member could both see "no row" and the second hit a primary-key violation | `supabase/upsert_rsvp.sql`: `upsert_rsvp()` does `insert ... on conflict do update` in one statement. Deliberately **security invoker**, so the existing insert/update-your-own-RSVP policies and `protect_rsvp_keys` apply exactly as they do to the web app's PostgREST upsert; `not_going` clears car status and seats as the repository always did. `SupabaseEventsRepository.UpsertRsvpAsync` calls it and parses the returned row. Not a client-side `Upsert(model)`: that would send year-1 `created_at`/`updated_at` and overwrite the stored ones on conflict |
| Low | Attendee counts went stale: loaded on navigation and pull-to-refresh only, and an RSVP tap patched just the viewer's own row into the cache, so everyone else's RSVPs and the transport totals stayed as old as the last full load | Event detail and the group Events tab now follow the instant local patch with a best-effort attendees-only refetch (one query), dropping it if a newer tap overtook it. Event detail also refetches on appearing / app resume when the data is over 60s old, and shows "RSVPs updated X ago" (30s timer, unhooked in `OnDisappearing`). No polling while the page is open |
| Low | Calendar feed URL (a long-lived secret) copied through the normal clipboard | New `ISecretClipboardService` (per-platform, same convention as `IGoogleAuthService`): Android sets `ClipDescription.ExtraIsSensitive` (API 33+, hides the preview; older versions get a plain copy), Windows uses `SetContentWithOptions` with history and roaming off. The invite URL is left as a plain copy on purpose |

Verified live for `upsert_rsvp`, as the real `TestGroupForEvents` accounts in a rolled-back block: insert, update, one row
after two upserts, `not_going` coupling, `created_at` preserved, writing another member's RSVP refused by RLS, anon cannot
execute. Not exercised: the C# side against a real RSVP (the RPC-response parsing in particular), the caption timer, and
both clipboard implementations.

### Unexpected logouts and widget crash (found 2026-09-21 on the phone, via adb + Supabase Auth logs)

The phone got logged out "without being used", with both home-screen widgets on. Evidence, in order:

- Auth log: `POST /logout` (204) from the **PC's** address at 11:53:25, then the phone's first refresh afterwards (a
  widget-triggered process start, 12:12:41) got `400 refresh_token_not_found` at 12:12:44 from the **phone's** address.
  The app's encrypted-prefs file on the device was rewritten 12:12:45 and now holds only the crypto keysets — session gone.
- Cause: **both clients used the SDK's default sign-out, which is global** (revokes every refresh token the account has).
  Signing out on the PC (or the web app) logged out every other device; the widget was just the first thing to try a refresh.
- Fixed: `SupabaseAuthService.SignOutAsync` uses `SignOutScope.Local`; the web app's header logout uses `{ scope: 'local' }`.
  The two delete-account sign-outs stay as they were (the user no longer exists, so nothing else to protect).
- Separate real bug, same log: an unhandled `PGRST303 "JWT issued at future"` (a transient Supabase clock-skew rejection)
  thrown inside `BalancesWidgetRemoteViewsFactory.OnDataSetChanged` — a system-thread callback with no `try/catch` — crashed
  the whole app process at 11:11:52. Anything failing there (including simply being offline) did the same.
  Fixed: both widget factories catch, log under tag `AxisWidget`, and keep the last good rows.
- Not the cause: the widgets' own token handling. The process was started from the widget several times, killed by
  low-memory each time, and that pattern alone didn't lose the session. Whether widget cold starts can still lose a session
  when refresh and kill race is not disproven, only unobserved; a local widget cache (SQLite/JSON) would remove that class
  of risk but was not built.

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

Added 2026-09-21:

| Where | Change |
|---|---|
| Supabase Auth, URL Configuration | Removed `http://localhost:5173/**` from the redirect allow-list (kept `http://localhost:48291/` for Windows sign-in, the app and confirm URLs). Re-add it temporarily if you run the web app locally and need Google / reset redirects there |
| Supabase Auth, Email provider | Minimum password length 6 -> 8 (read back after reload). Only applies when a password is set, existing accounts are unaffected. Web `minLength` / reset-page checks moved to 8 in the same change (login page enforces it on sign-up only, so older 6-7 character passwords can still sign in). The MAUI app has no client-side length check and shows the server's message |

Also: the unused Vault secret `firebase_service_account` was deleted by hand (verified first that no function or
cron job referenced it; `send-push` reads `FIREBASE_SERVICE_ACCOUNT_KEY` from the Edge Function env).
Leaked-password protection can't be enabled (Pro plan only, project is on Free).

Observed, deliberately left off (accepted risk): "Require current password when updating" and "Secure
password change". A stolen live session can therefore change the password without the old one. Turning
"Secure password change" on would break Change password for any session older than 24 hours, so it needs a
two-step flow in both clients first (email a code via `reauthenticate()`, then send code + new password;
gotrue 6.3.0 supports `Reauthenticate` and a nonce). "Require current password" isn't worth it: the MAUI SDK
has no `current_password` support, it's redundant once the first is on, and Google-only accounts have no
password. Revisit if the threat model changes.

Legacy JWT API keys deliberately left enabled too: migrating the Vault `service_role_key` means turning Verify
JWT off on the three Edge Functions and replacing `isServiceRoleCaller()`'s role-claim check with a shared-secret
compare, for little risk reduction (the key only lives in Vault and the function env).

Note: both Google keys showed recent traffic to Google Maps APIs (Directions, Geocoding, Places, …)
that Axis never calls — the keys had been scraped. Now blocked by the restrictions. Billing on
`axisapp-ee018` checked: no charges.

Signing certificates, for reference: Play Store installs are signed by Google's Play App Signing key
(SHA-256 `E4:1B:C6:C9:…`), which is what `web/.well-known/assetlinks.json` and Firebase list. Builds
installed straight from this machine use `axisapp.keystore` (SHA-256 `CF:F3:F3:3C:…`), which is **not**
in `assetlinks.json`, so invite App Links won't auto-verify for those local builds.

---

## Still open

Ordered by severity. Locations are relative to the repo root.

Numbering is kept from the original list, so gaps are items that moved to "Fixed".

### Low

5. **10 expenses with NULL `created_by`** (web app inserts from 2026-09-12 to 2026-09-16, before
   `save_expense`). No reliable way to know the real creator; left as-is.
6. *(closed 2026-09-21: redirect allow-list, min password length and the Vault Firebase key are done, legacy
   JWT keys are accepted risk, leaked-password protection is unavailable on the Free plan — see "Changed
   outside the repo".)*
8. *(calendar feed URL done, see "Optional code fixes". The invite URL copy in `InviteToGroupViewModel.cs`
    is deliberately left as a plain copy: it's short-lived, and it's meant to be pasted into a chat.)*
9. **`MainActivity` acts on intent extras from any app** (`AxisApp/Platforms/Android/MainActivity.cs`,
    `HandleIntent`) — spoofable screen titles, RLS still protects data.
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
- Optional code fixes (below), on a device: RSVP on the event detail page from two accounts and watch the
  other one's count follow; leave the page open past a minute and check the caption; copy the calendar link
  on Android 13+ (no preview shown) and on Windows (not in Win+V history).
- Android Google sign-in with the nonce (Supabase Google provider: watch "Skip nonce checks").
- The background-CPU investigation and the widget cold-start session test, both described in the handoff below.

### Handoff: how to run the outstanding phone tests (written 2026-09-21, for a session that has none of the context)

**State of the investigation.** Two things were found on the phone that day and fixed (global sign-out killing other
devices' sessions, unguarded widget refresh crashing the app — see "Unexpected logouts and widget crash"). One thing
is **open and not understood**: the app process was killed by Android at 16:05 for
`excessive cpu 263990 during 3000476 dur=3122153 limit=2` (about 264 s of CPU over 50 minutes while backgrounded,
against a 2% background limit). The process had been in the background since 14:40 with nothing logged. The session
survived it. Whether it is a real bug or a Debug-build artefact (JIT, debugger agent) is unknown.

**Baseline already measured** (process `31152`, started about 16:10, device time CEST, app opened once then Home, not
swiped away, widgets on): total CPU stayed at **1158 ticks = 11.6 s for 9 minutes**, no thread moved. Startup cost
only (main thread 832 ticks, JIT pool 58, RenderThread 54). So it is *not* a spin from launch; the burn happens later,
probably tied to something periodic (the ~1 h access-token expiry / auto-refresh timer, or the 30-minute widget tick).
That is a hypothesis, not an observation.

**Environment.** Physical phone: model 2412DPC0AG (MIUI/HyperOS-class aggressive process killing), connected over
*wireless* adb, so its serial (`adb-…._adb-tls-connect._tcp`) changes; get it from `adb devices -l`. There is also an
emulator (`emulator-5554`), ignore it. Package `com.aitorsansal.axisapp`. The installed build is a **Debug** build
(`dumpsys package` shows `DEBUGGABLE`), which is what makes `run-as` work; a Release build would not allow it. On
Windows use PowerShell or Git Bash; `Select-Object`, `grep` and `awk` are not available in the other shell. The Chrome
extension tools were used for the Supabase dashboard (already signed in); a tab with unsaved SQL-editor text blocks
navigation, so open a fresh tab instead.

**Test A: find the CPU burn (needs the app left alone 60 to 90 minutes).**
1. Open the app once, press Home, do NOT swipe it away (on MIUI a swipe-clean kills the process and stops widget updates
   until the next launch, which invalidates the test). Leave the screen off, widgets on.
2. Later, with `D=<serial from adb devices -l>`:
   ```
   adb -s $D shell pidof com.aitorsansal.axisapp        # empty = the process was killed
   PID=<that>
   adb -s $D shell "for t in /proc/$PID/task/*; do echo \$(cat \$t/comm | tr ' ' '_') \$(awk '{print \$14+\$15}' \$t/stat) \$(basename \$t); done" | sort -k2 -n -r | head -12
   adb -s $D shell "awk '{print \$14+\$15}' /proc/$PID/stat"     # process total, in 1/100 s
   ```
   Compare with the baseline above. The kernel keeps per-thread totals for the process's whole life, so this works
   without continuous sampling as long as the process wasn't killed. A thread whose ticks grew by tens of thousands is the
   culprit; its name says what it is (`.NET ThreadPool`/`Jit thread pool`, `mono`, `RenderThread`, `glide-…`, the main
   thread `rsansal.axisapp`).
3. If the process was killed, get the reason and time: `adb -s $D logcat -d -b events | grep am_kill | grep axisapp`
   (a line ending `excessive cpu … limit=2` means the burn recurred and is sustained). Then write a sampler that starts
   before the burn (every 1 to 2 minutes, log only when the total moves) and repeat.
4. Interpretation: if it is JIT or debugger-agent work, retest with a **Release** build before changing any code. If it is
   a managed thread, look at what runs periodically: `AutoRefreshToken = true` (MauiProgram.cs), the widget providers
   (`Platforms/Android/Widgets`), and `EventDetailPage`'s caption timer (`IDispatcherTimer`, 30 s; it is unhooked in
   `OnDisappearing`, but MAUI may not raise that when the whole app goes to the background (not verified), so if the event
   detail page was the last screen open the timer could keep ticking; a 30 s label update is cheap, but worth ruling out). Realtime is off
   (`AutoConnectRealtime = false`).

**Test B: does a widget cold start lose the session?** (the original worry). Evidence so far: one widget cold start at
12:36:56 refreshed fine (Auth log `/token` 200 at 12:36:59) and the session was intact after; an earlier failure was
traced to a global sign-out, not the widget. To test properly: open the app, press Home (no swipe), leave 1 to 2 hours,
then confirm widget cold starts happened and the session survived:
```
adb -s $D logcat -d -b events | grep am_proc_start | grep BalancesWidgetProvider      # widget-triggered process starts
adb -s $D shell "run-as com.aitorsansal.axisapp cat shared_prefs/com.aitorsansal.axisapp.microsoft.maui.essentials.preferences.xml" | sed -E 's/>[^<]{12,}</>[masked]</g' | grep -c "<string"
```
The prefs file holds the AndroidX crypto keysets plus, when logged in, the encrypted session: **3** `<string>` entries =
session present, **2** = session gone (that is what a wiped session looks like). Its modification time
(`adb -s $D shell "run-as com.aitorsansal.axisapp ls -l shared_prefs"`) is when the session was last saved or removed.
Do NOT log out of the same account anywhere (PC, web) during the test; with the fix a local sign-out no longer affects
other devices, but any other client's activity muddies the log.

**Reading the server side.** Supabase dashboard, project `foepkovwmwyygulbdahv`: Logs, Auth
(`/dashboard/project/foepkovwmwyygulbdahv/logs/auth-logs`; the URL accepts `?its=<UTC ISO time>` for the start of the
range; the free plan keeps about a day). Look for a WARNING `/token | request completed` and open it, Raw tab:
`error_code` is `refresh_token_not_found` (the token was revoked or the session is gone, usually by a `/logout`) or
`refresh_token_already_used` (two clients refreshed the same token). Also look for `/logout` and its `remote_addr`. Known
addresses that day: the PC `188.64.100.162`; the phone on mobile data `31.4.130.53` (on home Wi-Fi the phone shares the
PC's address, so an address match alone proves nothing; cross-check with `am_proc_start` times on the phone: if the phone
had no live process at that timestamp it did not send the request). A benign `refresh_token_not_found` was seen at
13:54:56 from the PC while the phone had no process: a stale web tab or Windows debug client whose token the 11:53
global logout revoked.

**Other pending tests, quick recipes.**
- *RSVP freshness*: two accounts on the same group event; RSVP from A on the detail page, B's screen should update
  within a minute after appear/resume, and the "RSVPs updated X ago" caption should track. `upsert_rsvp()` itself is
  already verified live in SQL.
- *Notification Settle / push*: another account adds an expense involving you; tap Settle on the notification; expect
  one settlement of min(your share, what you currently owe that person), in the group currency; a second tap adds nothing.
- *Android Google sign-in*: sign out and back in with Google on the phone; if it fails, first check Supabase, Authentication,
  Sign In / Providers, Google, "Skip nonce checks".
- *Calendar link copy*: Android 13+ should not show the copied text in the clipboard preview; Windows should not put
  it in Win+V history.

**Gotchas from the session.** The permission classifier blocks writes to the Vault (secret store); do those by hand in
the SQL editor. Never use `cd` in shell commands here (the working directory is already the project root). Migrations
are applied by hand in the Supabase SQL editor and folded into `supabase/schema.sql`; `supabase/audit_followups.sql`
and `supabase/upsert_rsvp.sql` are already applied live.

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
