# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

**See `CHANGELOG.md` for the full dated build history** — every feature's
design rationale, real bugs hit and how they were fixed, and confirmation
status. This file only holds current-state facts and architecture rules
that need to be re-loaded every session; anything narrative/historical was
moved out on 2026-09-10 to keep this file under control (was 150.9k chars).
If a decision here seems to need more context on *why*, check CHANGELOG.md
before assuming it's undocumented.

## Project Overview

**Axis** is a .NET MAUI cross-platform shared-expense tracker (Android, iOS,
macOS, Windows) — the "shared household ledger" successor to a previous
local-only personal app (DebtTracker). Where that app kept one person's data
in a local SQLite file, Axis is built around a real backend from day one:
multiple people, each with their own account, sharing a ledger inside groups
they invite each other into.

- **Target Frameworks**: `net10.0-android`, `net10.0-windows10.0.19041.0`
  (iOS/MacCatalyst targets exist in the platform folders but aren't in the
  active `TargetFrameworks` list yet — add them back when there's a Mac to
  build/test against)
- **Single project** structure (`AxisApp/AxisApp.csproj`)
- **Solution file**: `AxisApp.slnx`
- **Backend**: Supabase (Postgres + Auth + Row Level Security). See
  `/supabase/README.md` before touching anything backend-related.
- **`webapp/`**: a lighter-weight React + TypeScript + Vite SPA against the
  same Supabase project, for platforms the MAUI app doesn't cover (iOS
  without a Mac). See CHANGELOG.md's "Web app" entry for scope/gaps.

## Current state (read this before assuming something exists)

**Phase 1 (debt tracker) and Phase 2 (Events & Calendar) are both built and
confirmed working end to end.** `payments` no longer exists as a concept
anywhere in this codebase — merged into `expenses` 2026-09-04 (a settle-up
is just an Expense with `IsSettlement = true`); `IPaymentsRepository` is
gone, not unwired. `IAuthService` is `SupabaseAuthService`, every
`I*Repository` has a concrete `Supabase*Repository` implementation, and
every screen is wired to the real backend — see "Backend abstraction" below
for the full interface list.

What's actually left, in rough order:
1. **Phase 2.5 — Google Calendar sync**, not started at all — per-user
   Google OAuth separate from Supabase Auth, encrypted token storage/
   refresh, a sync strategy, and reconciling Axis's recurrence model
   against Google's RRULE.
2. **iOS/MacCatalyst**, not in the active `TargetFrameworks` (no Mac to
   build/test against).
3. Small known gaps: a live RLS-enforcement pass on the `events`/
   `event_attendees` policies (structurally verified, not yet exercised
   under a real adversarial session); the stray `pnpm-lock.yaml` in
   `webapp/` next to `package-lock.json` (worth standardizing on one
   package manager before this causes real lockfile drift); the web app
   is still missing receipts, recurring expenses, push notifications, and
   a members roster/add-by-name flow.
4. The merge-on-claim code path in `redeem_invite()` (see "One account, one
   member" below) is still not exercised against real data.

See **`SCOPE.md`** for the full product scope and phased roadmap — it's the
source of truth for what's in/out of scope and why.

## The core design decision: members vs. accounts

This is the one thing to understand before changing the schema or the data
layer. See `supabase/schema.sql`'s header comment for the full reasoning; in
short:

- A **member** (`members` table) is a ledger participant — the thing every
  expense/share ultimately points at.
- An **account** (`auth.users`, Supabase Auth) is a real login.
- A member is either **phantom** (`account_id is null` — added by name only,
  like adding a person in the old local app) or **claimed** (`account_id`
  set — a real person who signed in).
- Ledger rows never reference `auth.users` directly, only `members`. This
  means adding "a debt with my dad" works identically whether or not he has
  an account yet.
- **Claiming**: when a phantom's real person signs up, redeeming an invite
  that targets that specific phantom (`invites.target_member_id`) links their
  new account to the *existing* member row — their prior history stays
  attached, nothing is recreated.
- **One account, one member row — an enforced invariant, not just a
  convention** (fixed 2026-09-07 after real production data violated it;
  see CHANGELOG.md's "One-account-one-member invariant fix" for the two
  bugs and the merge-on-claim logic). `create_group()`/`redeem_invite()`
  both look up and reuse an account's existing `members` row rather than
  minting a new one, and a trigger on `auth.users`
  (`handle_new_user_member()`) provisions the row at signup time so it
  always exists, even before the account joins any group.

Don't reintroduce a design where ledger rows reference `auth.users` or a
"Person" that assumes every participant has logged in. That collapses the
phantom-member case, which is the entire point of this schema.

## Balances: simplified vs. pairwise

Two selectable display modes on Group Detail, chosen via a per-device,
per-account, per-group **local-only preference**
(`AppConstants.Preferences.BalanceDisplayModePrefix` — deliberately never
synced; it's a viewing preference, not group state):

- **Simplified** (default): `Services/DebtSimplifier.cs` runs the standard
  greedy debt-simplification algorithm (match biggest creditor against
  biggest debtor, repeat) over every member's `group_balances` net —
  Tricount/Splitwise's "settle up" behavior, where offsetting/cyclic debts
  net out to fewer, smaller transfers than the raw history.
- **Pairwise** ("Detailed" toggle): `pairwise_balances`/`my_pairwise_balances`
  views derive genuine two-party debts directly from `expense_shares`/
  `expenses` — always literally true "owes you"/"you owe" language, may show
  more/smaller line items than Simplified for the same underlying numbers.

Both feed the same "Settle" write primitive (creates an `Expense` with
`IsSettlement = true`). Deferred, not built: letting someone exclude a
specific counterparty from simplification — needs pairwise data as
simplification's *input* (a constrained matching/flow problem), materially
bigger than either display mode.

## Architecture

### Backend abstraction — why it exists, and the one rule

Every data access interface lives in `Services/` (`IAuthService`,
`IMembersRepository`, `IGroupsRepository`,
`IExpensesRepository`, `IBalancesRepository`, `IRecurringExpensesRepository`,
`IInvitesRepository`, `IDeviceTokensRepository`, `IEventsRepository`).
`IPaymentsRepository` and `ICategoriesRepository` existed early on and were
both retired once their tables were (payments 2026-09-04, categories
2026-08-28) — see CHANGELOG.md for the removal details.
**ViewModels depend on these interfaces, never on the
`Supabase.Client` type or the `supabase-csharp` package directly.** The reason:
if this ever moves off Supabase (self-hosted Supabase on a NAS, or a fully
custom backend), that's a new implementation of these interfaces registered
in `MauiProgram.cs` — not a rewrite of every page and ViewModel. Keep it that
way as the app grows.

`Models/` are plain data classes decorated with `Postgrest` attributes
(`using Supabase.Postgrest.Attributes;` / `using Supabase.Postgrest.Models;`
— this **is** the correct namespace for the installed `Supabase` 1.6.0
package) — `[Table]`, `[PrimaryKey]`, `[Column]`, base class `BaseModel` —
so they double as both the app's domain model and the Postgrest ORM's row
mapping. Any get-only computed property on a model (no `[Column]`) needs
`[Newtonsoft.Json.JsonIgnore]`, or Postgrest's serializer will try to send
it as a column on insert/update and PostgREST will reject it (`PGRST204`)
— see `Member.IsPhantom`.

A composite-PK model (e.g. `GroupMember`, `ExpenseShare`) only marks ONE
property `[PrimaryKey]` — harmless for insert/delete, but `.Update(model)`
would match on that single column and silently update every row sharing
it. Use an explicit `.Filter(...).Filter(...).Update(...)` instead of
trusting the implicit PK match on any update path touching these models.
Similarly, a `[PrimaryKey]` property with no DB default (e.g. an owner-id
column) needs `shouldInsert: true` explicitly, or Postgrest drops it from
the insert payload (defaults to `shouldInsert: false`, since PKs are
normally auto-generated) — see CHANGELOG's `MemberAlias.OwnerId` bug.

### MVVM + Dependency Injection

Same conventions as the previous app: **CommunityToolkit.Mvvm**
(`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`), all
services/pages/ViewModels registered in `MauiProgram.cs`. Register a new
repository the same way `IAuthService` is registered: interface → concrete
Supabase-backed implementation, singleton.

Every `[RelayCommand]` async body should run through `BaseViewModel
.RunSafeAsync` rather than executing bare — an unhandled exception from
`CommunityToolkit.Mvvm`'s `AsyncRelayCommand` fail-fasts the whole WinUI
process outside any try/catch otherwise. `RunSafeAsync` sets a plain
`ErrorMessage` string (surfaced via the shared `Controls/ErrorPopup` on
every page) and retries once on a transient Supabase clock-skew rejection
(`PGRST303`). A fresh model object built for an `UpdateAsync` call must
carry over `CreatedBy`/`CreatedAt` (and any other server-set field) from
the original row — building a brand-new object and sending the whole thing
silently blanks those columns; this has bitten `Expense` edits twice.

### Navigation

Uses **MAUI Shell**, routes declared as constants in `AppConstants.Routes`
(`Splash`, `Login`, `Groups`, `GroupDetails`, `Members`, `JoinGroup`,
`AddExpense`, `NewGroup`, `AddEvent`) rather than hardcoded strings — follow
that pattern for any new screen. The Events list itself has **no** separate
route — it lives embedded in `GroupDetailPage`'s Events tab
(`GroupEventsView`), not as its own navigated page; only `AddEventPage`
needed a real route, same shape as `AddExpensePage`. `Splash` is the first
`ShellContent` in `AppShell.xaml`; it decides between `//Login` and
`//Groups` before anything else renders (`SplashPage.OnAppearing` must
`await Task.Yield()` before touching `Shell.Current` — Shell's own initial
navigation to `//Splash` can still be mid-flight otherwise, a real crash
documented in CHANGELOG).

Avoid `Shell.Current.DisplayPromptAsync` entirely — a known WinUI fail-fast
crash on Windows (microsoft/microsoft-ui-xaml#10897). Use a dedicated page
or an inline overlay instead. `DisplayAlert`/`DisplayActionSheet` are
confirmed safe standard MAUI primitives; so is `Toast.Make(...).Show()`
**not** — it throws `COMException` on this unpackaged Win32 build.

### Deep linking (group invites)

An invite link (`https://axisapp.aitorsansal.com/invite?code=...`, built by
`AppConstants.Links.BuildInviteUrl`) has to work both as a plain web page
(no app installed) and as a direct jump into `JoinGroupPage` (app
installed) — that split lives across the mobile project and a separate
static site, so it's easy to change one side and forget the other:

- **`web/`** is a standalone Cloudflare Worker (deployed with
  `npx wrangler deploy` from `web/`, not part of the MAUI build).
  `wrangler.jsonc` points its static-assets root at `.` specifically so both
  `web/invite/index.html` (the fallback landing page — shows the code, links
  to the Play Store) and `web/.well-known/assetlinks.json` (Android's
  Digital Asset Links proof, listing the app's signing-cert SHA-256
  fingerprint) get served; pointing it at `invite/` instead (what the
  Cloudflare setup wizard guesses, since it's the only folder with an
  `index.html`) silently drops `.well-known` and breaks App Links.
- **`AxisApp/Platforms/Android/MainActivity.cs`** declares the App Link via
  an `[IntentFilter(..., DataHost = "axisapp.aitorsansal.com",
  DataPathPrefix = "/invite", AutoVerify = true)]` attribute — there's no
  Android-manifest XML for this, it's all in the C# attribute. `AutoVerify`
  is what makes Android open the link straight in-app instead of a browser,
  and it only succeeds once Android has fetched and matched
  `assetlinks.json` above against the APK's actual signing certificate, so a
  cert mismatch (e.g. testing a debug build against a fingerprint list that
  only has the release key, or vice versa) makes links silently fall back to
  the browser with no error anywhere.
- `MainActivity.OnCreate`/`OnNewIntent` both funnel the incoming `Intent`
  into `App.HandleDeepLink(uri)`, queued in `App.pendingRoute` if the app
  isn't ready to navigate yet (see the `App.isReadyToNavigate` note below —
  this is shared with push-notification taps, not just invite links).
  `SplashPage` calls `App.ReplayPendingDeepLinkAsync()` once it's picked
  Login vs. Groups. `AppConstants.Links.TryExtractCode` is the one place
  that parses a `?code=` query param back out of either a full invite URL
  or a raw platform URI.
- **`App.isReadyToNavigate`, not `Shell.Current is null`, gates queued
  navigation** — a Firebase-triggered process start (see push notifications
  in CHANGELOG) can construct MAUI's whole `Application`/`Window`/`Shell`
  graph with no Activity ever appearing, so `Shell.Current` can be non-null
  well before `SplashPage.OnAppearing` has actually restored a session. Set
  `isReadyToNavigate = true` only once that restore has genuinely finished.
- iOS Universal Links would need the equivalent (`apple-app-site-association`
  under `web/.well-known/`, entitlements on the iOS target) but iOS isn't in
  the active `TargetFrameworks` yet, so this is Android-only today.

### UI

- Dark theme enforced globally (`Application.Current.UserAppTheme =
  AppTheme.Dark` in `App.xaml.cs`).
- Design tokens (`Resources/Styles/Tokens.xaml`) and control styles
  (`Resources/Styles/Styles.xaml`) are carried over unchanged from the
  DebtTracker project — same spacing/radius/type scale, same button/card/input
  style names (`BtnPrimaryStyle`, `ElevatedCard`, `InputBorderStyle`, etc.).
  Consume these from XAML rather than hardcoding values.
- Colors (`Resources/Styles/Colors.xaml`) are Axis's own palette
  (blue/amber baseline), overridable per-device via 8 fixed accent presets
  (`Services/ThemeService.cs`/`AccentPalettes.cs`) — only ~35 accent-derived
  keys change per preset (Primary/Secondary and everything visually derived
  from them), backgrounds/surfaces/status colors stay fixed. Any color meant
  to respond to the accent picker must be consumed as `{DynamicResource
  ...}`, not `{StaticResource ...}` — the latter resolves once at load and
  never picks up a preset change.
- `Resources/AppIcon` and `Resources/Splash` currently hold a placeholder
  geometric mark, not real branding — swap those SVGs whenever real branding
  exists.

## Data model (Postgres, see `supabase/schema.sql` for the authoritative version)

| Table | Purpose |
|---|---|
| `members` | Every ledger participant, phantom or claimed. |
| `groups` | A shared ledger (e.g. "Relaciones", "Family"). |
| `group_members` | Which members belong to which groups. |
| `invites` | A redeemable token to join a group, or to claim a specific phantom member. |
| `expenses` / `expense_shares` | N-way split expenses. `is_settlement` marks a settle-up (exactly one share). `currency`/`amount_in_group_currency`/`exchange_rate` (and the share-level equivalent) support multi-currency groups. |
| `recurring_expenses` / `recurring_expense_shares` | Templates for periodically auto-generated N-way split expenses. |
| `exchange_rates` | Singleton (`id boolean primary key default true`) cache of currency conversion rates, refreshed daily from Frankfurter. |
| `events` / `event_attendees` | Group events with 3-state RSVP (`going`/`maybe`/`not_going`) and transport/carpooling fields. `is_birthday` rows are auto-generated, non-RSVPable member birthdays. |
| `device_tokens` | Per-account push tokens for the notification feature. |
| `member_aliases` | Private, per-account nickname override for how a member is displayed. |

No `categories` table (removed 2026-08-28) — categories are a small fixed
list of keys in `AppConstants.Categories`, localized client-side, not
stored data. No `payments` table (removed 2026-09-04, merged into
`expenses` — see CHANGELOG.md).

Also a public `avatars` Storage bucket and a private `receipts` Storage
bucket (`storage.buckets`/`storage.objects`, not `public.*`).

Read-only views (no primary key, `security_invoker = true` so they enforce
RLS as the querying user, never inserted/updated/deleted):

| View | Purpose |
|---|---|
| `group_balances` | Each member's net balance against a group's whole shared pot. |
| `my_group_balances` | The current account's own row from `group_balances`, one per group — feeds the Groups list. |
| `pairwise_balances` | Real two-party net balance between every pair of members who've actually shared money in a group. |
| `my_pairwise_balances` | `pairwise_balances` reoriented around the current account, sign-normalized to "positive = they owe me". |

RLS is enabled on every table. Several functions intentionally bypass it via
`SECURITY DEFINER` — `redeem_invite()` (adding a `group_members` row for
someone who, by definition, isn't a group member yet),
`transfer_group_ownership()` (the plain `update own groups` policy would
reject the very act of transferring `created_by` away from the caller),
`remove_group_member()`/`delete_account()` (business rules not expressible
as a plain RLS policy), functions reading Vault secrets (`notify_new_expense()`
etc. — the calling role has no grant on the `vault` schema by default), and
the small helper functions `is_group_member()`/`is_own_member_row()`/
`is_unscoped_expense_party()` (used *inside* other policies specifically to
avoid Postgres RLS recursion when two tables' policies would otherwise
reference each other — two real `42P17` recursion bugs documented in
CHANGELOG.md). `leave_group()` and `create_group()` are notably **not**
security definer — every operation they perform is already permitted under
existing RLS, so there's no permission gap to bypass, only atomicity
(`create_group`) or explicit business-rule guards (`leave_group`).
Don't add a new `SECURITY DEFINER` function without a similarly specific
reason (a real permission gap, or breaking an RLS recursion cycle); prefer
expressing access rules as plain RLS policies so Postgres enforces them
uniformly.

Several `pg_cron` jobs call deployed Edge Functions via `pg_net` for work
that can't be pure SQL (Storage file deletion, HTTP calls to external
rate/push APIs): `materialize_recurring_expenses()` (daily), receipt
cleanup (`find_expired_receipts()` + `cleanup-receipts` Edge Function,
weekly), exchange-rate refresh (`fetch-exchange-rates`, daily), event/
birthday reminders and notifications (daily + day-of). See CHANGELOG.md
for each one's design rationale and the real bugs hit building them (Vault
permission grants, `security definer` needs, off-peak scheduling choices).

## NuGet Packages (key)

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Maui.Controls` | 10.0.10 | MAUI runtime |
| `CommunityToolkit.Maui` | 13.0.0 | UI controls & behaviors |
| `CommunityToolkit.Mvvm` | 8.4.0 | MVVM source generators |
| `Supabase` | 1.6.0 | Supabase client (Auth + Postgrest + Realtime + Storage) |
| `SkiaSharp` | 4.151.1 | Client-side image resize/WebP encode for avatar uploads |
| `Xamarin.AndroidX.Credentials` | 1.6.0.1 | Android-only: Credential Manager for native Google sign-in |
| `Xamarin.AndroidX.Credentials.PlayServicesAuth` | 1.6.0.1 | Android-only, paired with the above |
| `Xamarin.Google.Android.Libraries.Identity.GoogleId` | 1.1.0.15 | Android-only: Google ID token credential type |
| `Xamarin.Firebase.Messaging` | 125.1.1.1 | Android-only: FCM push notifications |

Be skeptical of assuming an SDK's API surface without checking a real local
build. This bit the project repeatedly early on (wrong `Postgrest`
namespace, wrong `CreateSignedUrl` return shape, `CommunityToolkit.Maui
.Popup`'s API changing between versions) — see CHANGELOG.md for each case.
The reliable source of truth is still a real local build's compiler errors
or a reflection probe of the actually-installed package, not fetched docs
— when something new comes up that hasn't been build-verified, say so
explicitly rather than asserting an API shape with false confidence.

## Environment

Developed on Windows, same as the previous project. Use Claude's built-in
Grep/Glob tools or PowerShell equivalents, not Unix-only commands like `grep`.

## Build Commands

```bash
# Build for Android
dotnet build AxisApp/AxisApp.csproj -f net10.0-android

# Build for Windows
dotnet build AxisApp/AxisApp.csproj -f net10.0-windows10.0.19041.0
```

```bash
# Deploy the invite-link web page + assetlinks.json (see "Deep linking" above)
# — Cloudflare's GitHub integration auto-deploys web/ on every push to main,
# so this manual command is now only needed for previewing before a push,
# or redeploying without a new commit.
cd web && npx wrangler deploy
```

There are no automated tests in this project yet.
