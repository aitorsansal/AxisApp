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
   package manager before this causes real lockfile drift — both are kept
   in sync by hand for now); the web app is still missing receipts,
   recurring expenses, and a members roster/add-by-name flow. PWA install +
   web push are implemented (see "Web app: PWA install + push
   notifications" below) but **not yet activated** — `public/sw.js` still
   has `REPLACE_ME` placeholders for the Firebase Web App config, no Web
   app has been registered in the `axisapp-ee018` Firebase project yet, and
   `web_push.sql` hasn't been run against the live database.
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

## Stats tab

Group Detail's third tab (`GroupStatsView`/`GroupStatsViewModel`), added
2026-09-15 — pure read-only aggregation over a group's whole expense
history, no new schema/view (see CHANGELOG.md's "Stats tab" entry for the
full build history and the two real runtime bugs it took to get charts
actually rendering). Current-state facts worth knowing before touching
this area again:

- **`GroupDetailPage`'s tab selector is enum-driven, not bool-driven** —
  `GroupDetailTab { Expenses, Events, Stats }` on `GroupDetailViewModel`,
  with derived `IsXTabSelected` bools for XAML bindings and `SelectedTabIndex`
  feeding `Controls/Juice.cs`'s `SlideIndex` attached property (an N-position
  generalization of the older 2-position `SlideRight`, added alongside it
  rather than replacing it — `GroupEventsView`/`AddExpensePage`'s existing
  2-tab toggles still use `SlideRight` unchanged). Adding a 4th tab anywhere
  in the app means extending this enum pattern, not reintroducing a bool.
- **Lazily loaded, unlike the other two tabs** — `ExpensesVm`/`EventsVm` load
  eagerly in `GroupDetailViewModel.LoadAsync`'s `Task.WhenAll` on every group
  open; `StatsVm.EnsureLoadedAsync` only fires from `OnSelectedTabChanged`
  on the Stats tab's first selection, since most group visits never open it.
- **Charts are `LiveChartsCore.SkiaSharpView.Maui`** (`CartesianChart`,
  `RowSeries<T>`/`ColumnSeries<T>`/`LineSeries<T>`), wired via
  `.UseSkiaSharp().UseLiveCharts()` in `MauiProgram.cs` — **that call order
  matters**, `UseLiveCharts()` alone throws `HandlerNotFoundException` on
  `LiveChartsCore.SkiaSharpView.Maui.Rendering.CPURenderMode` the moment any
  `CartesianChart` is instantiated (which happens as soon as `GroupDetailPage`
  loads, regardless of which tab is active — MAUI builds the whole visual
  tree eagerly, `IsVisible="False"` doesn't defer construction). See the
  NuGet Packages section above for the other real bug this shipped with
  (a `SkiaSharp` version split).
- **One consistent hue (Axis's own Primary accent) for every single-series
  chart**, the validated 8-slot categorical palette
  (`GroupStatsViewModel.CategoricalPalette`, checked with the dataviz
  skill's `validate_palette.js` against Axis's actual dark surface
  `#0B1220`) only for the one genuinely multi-series chart (category spend
  trend over time) — color identity is for distinguishing series, not for
  decorating every bar of a single magnitude chart. A category keeps the
  same palette slot everywhere via its position in
  `AppConstants.Categories.Keys`, never by a chart's own per-render sort
  order.
- **`CategoryDisplay.Label`/`Services/MemberDisplay.cs`** are the shared
  resolvers for a category key → localized label and a member → display
  name — both `GroupStatsViewModel` and `MemberProfileViewModel` (and
  `StatsExporter`) go through these rather than resolving either inline.
- **`MemberProfilePage`** ("you and X") is a related but separate screen,
  opened by tapping a member's name/avatar on `MembersPage` (not "You") —
  merges the "per-member drill-down" and "cross-group pairwise" ideas into
  one screen, entirely composed from existing repository methods
  (`GetMyGroupsAsync`, `GetForGroupAsync`, `GetMyPairwiseForGroupAsync`,
  `GetAllForGroupAsync`, `GetSharesForExpensesAsync`) — no new SQL. Walks
  the viewer's own groups client-side to find which ones the target member
  is also in (members are a global table, so the same `member_id` really
  does recur across every group that person belongs to — see "members vs.
  accounts" above), and for each shared group uses the same payer/
  share-holder edge definition `my_pairwise_balances` uses server-side, not
  the group's whole aggregate.
- **Export (`Services/StatsExporter.cs`)** — CSV is the group's whole raw
  expense history (general-purpose, not Stats-specific); PDF is a
  numbers-table snapshot of the current tab's aggregations, over whichever
  `StatsDateRange` is selected. PDF uses `SkiaSharp.SKDocument.CreatePdf`
  directly — no separate PDF library, confirmed present in the
  already-installed `SkiaSharp` package. Both hand off to the OS share
  sheet via `Share.Default.RequestAsync(ShareFileRequest)`, the same API
  `ProfileViewModel`/`InviteToGroupViewModel` already use for a text share
  — this was the app's first *file* share.

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

**`SupabaseAuthService.RestoreSessionAsync()` is a genuine no-op once
`client.Auth.CurrentSession` is already set in memory** — it returns
immediately rather than re-running `LoadSession()+InitializeAsync()`. Don't
remove that early-return: `InitializeAsync()` calls Gotrue's
`RetrieveSessionAsync()`, which unconditionally calls `RefreshToken()`
whenever a refresh token exists (no check on whether the access token is
actually near expiry), and the SDK already runs its own independent refresh
timer in parallel. Calling `RestoreSessionAsync()` again on an already-live
session races that timer for the same refresh token; Supabase rotates it on
use, so the losing call gets `InvalidRefreshToken` back, which Gotrue
handles by destroying the *entire* session — a real, persisted logout, even
though the winning call had just succeeded. This is a real bug the Android
home-screen widgets hit (see CHANGELOG.md's "Widget-triggered logout" entry)
by calling `RestoreSessionAsync()` on every 30-minute widget tick, same
process, same `Client` singleton as the app. Any other code path that might
touch this client from a background trigger (a future sync job, a
notification handler) needs the same guard, not a fresh
`LoadSession()+InitializeAsync()` call.

### Navigation

Uses **MAUI Shell**, routes declared as constants in `AppConstants.Routes`
(`Splash`, `Login`, `Groups`, `GroupDetails`, `Members`, `JoinGroup`,
`AddExpense`, `NewGroup`, `AddEvent`, `MemberProfile`) rather than hardcoded
strings — follow that pattern for any new screen. The Events list itself
has **no** separate route — it lives embedded in `GroupDetailPage`'s Events
tab (`GroupEventsView`), not as its own navigated page; only `AddEventPage`
needed a real route, same shape as `AddExpensePage`. Same for the Stats
tab (`GroupStatsView`, no route of its own). `Splash` is the first
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

### Web app: PWA install + push notifications

Added 2026-09-14, **not yet activated in production** — see the "not yet
activated" gap above for exactly what's missing. Two purposes at once: makes
`webapp/` installable as a standalone app, and (the reason iOS web push needs
that first) reuses the same Firebase project as Android for push.

- **`webapp/public/manifest.json`** + `index.html`'s `<link rel="manifest">`/
  `apple-mobile-web-app-*` tags make the app installable. Chrome/Edge/Android
  fire `beforeinstallprompt`, captured by `webapp/src/lib/useInstallPrompt.ts`
  to drive an in-app "Install" button (`ProfilePage`) — there's no equivalent
  event on iOS Safari, which only supports manual Share → "Add to Home
  Screen", so `useInstallPrompt` also exposes `showIosInstructions` to show
  text instead of a button there.
- **iOS web push only works after that manual install** — a bare Safari tab
  can never register for push, even with permission granted. This is the
  reason PWA installability had to ship before push, not just a nice-to-have
  alongside it.
- **`webapp/public/sw.js`** is one service worker doing both jobs — a second,
  competing SW was deliberately avoided. It's a static file Vite does not
  process, so its Firebase config is hardcoded (not read from `.env`) and
  must be filled in by hand from Firebase Console once a Web App is
  registered under the same `axisapp-ee018` project
  (`AxisApp/Platforms/Android/google-services.json`'s `project_id`) — see
  that file's own header comment.
- **`webapp/src/lib/firebase.ts`** initializes the Firebase JS SDK from
  `VITE_FIREBASE_*` env vars (see `webapp/.env.example`); a web push token is
  still an FCM registration token like Android's, so `supabase/functions/
  send-push/index.ts` sends to both through the identical
  `fcm.googleapis.com/v1/projects/.../messages:send` call — no separate Web
  Push/VAPID-only send path.
- **`webapp/src/lib/pushNotifications.ts`** upserts `device_tokens` rows with
  `platform = 'web'` (added to that column's check constraint by
  `supabase/web_push.sql` — run once against the live database; `schema.sql`
  is already updated for fresh installs). `getPushState()` deliberately
  cross-checks against the `device_tokens` row rather than trusting
  `Notification.permission` alone — permission can't be revoked
  programmatically once granted, so after unregistering, the browser would
  still report `'granted'` with no token actually registered.
- Event/expense push copy is still UTC-only (`send-push/index.ts`'s existing
  limitation, unchanged by this feature — see that file's own header
  comment).

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
CHANGELOG.md). `leave_group()` and `create_group()` are security definer too
since the 2026-09-17 RLS hardening (`supabase/rls_hardening.sql`): there are
no direct-DELETE policies on `group_members` anymore and its INSERT policy is
phantom-only, so both genuinely need to run as owner. `save_expense()`/
`save_recurring_expense()` are the ones that deliberately run as the caller
(atomicity only). Column locks on `members`/`groups`/`invites`/`events`/
`event_attendees` are `protect_*` BEFORE UPDATE triggers keyed on
`current_user in ('authenticated','anon')` — so security definer functions,
cron jobs and service-role Edge Functions can still change those columns;
keep that pattern for any new locked column.
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
| `SkiaSharp` | 3.119.0 | Client-side image resize/WebP encode for avatar uploads; PDF export (`SKDocument.CreatePdf`, see "Stats tab" below) |
| `LiveChartsCore.SkiaSharpView.Maui` | 2.0.5 | Charts on the Stats tab (see "Stats tab" below) |
| `Xamarin.AndroidX.Credentials` | 1.6.0.1 | Android-only: Credential Manager for native Google sign-in |
| `Xamarin.AndroidX.Credentials.PlayServicesAuth` | 1.6.0.1 | Android-only, paired with the above |
| `Xamarin.Google.Android.Libraries.Identity.GoogleId` | 1.1.0.15 | Android-only: Google ID token credential type |
| `Xamarin.Firebase.Messaging` | 125.1.1.1 | Android-only: FCM push notifications |

**`SkiaSharp` is pinned to 3.119.0, not the newer 4.151.1 it briefly was** —
`LiveChartsCore.SkiaSharpView.Maui` 2.0.5's own dependency chain
(`SkiaSharp.Views.Maui.Controls`/`.Core`, `SkiaSharp.HarfBuzz`, etc.) floors
at 3.119.0, and NuGet doesn't auto-unify a directly-pinned core package
against its own still-3.119.0 companion assemblies. That split compiles
clean (both sides expose stable public API) but breaks at *runtime*: a
`MethodAccessException` in `SkiaSharp.HarfBuzz.SKShaper.Shape` calling
`SKPaint.GetFont()`, silently swallowed by LiveCharts' own render-error
handling — every chart rendered as a blank canvas with no error anywhere in
the app. See CHANGELOG.md's "Stats tab" entry. Don't re-bump `SkiaSharp`
alone without bumping every `SkiaSharp.*` companion package to the exact
same version (and re-verifying `Microsoft.Maui.Controls`' own version floor
doesn't move as a result — `SkiaSharp.Views.Maui.Controls` 4.151.1 needs
`Microsoft.Maui.Controls >= 10.0.20`, this project pins 10.0.10).

Be skeptical of assuming an SDK's API surface without checking a real local
build. This bit the project repeatedly early on (wrong `Postgrest`
namespace, wrong `CreateSignedUrl` return shape, `CommunityToolkit.Maui
.Popup`'s API changing between versions) — see CHANGELOG.md for each case.
The reliable source of truth is still a real local build's compiler errors
or a reflection probe of the actually-installed package, not fetched docs
— when something new comes up that hasn't been build-verified, say so
explicitly rather than asserting an API shape with false confidence. A
package's own bundled `.xml` doc can itself be stale against the compiled
DLL (found live: `SkiaSharp.Views.Maui.Controls`' doc names a method
`UseSkiaSharpHandlers` that the actual DLL doesn't have — the real one is
`UseSkiaSharp`, confirmed by grepping the DLL's raw string heap) — treat
even the installed package's own doc file as unverified until a build or a
binary check confirms it.

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
