# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

This file holds current-state facts and architecture rules. `CHANGELOG.md`
has the dated build history, design rationale and past bugs; check it before
assuming a decision here is undocumented.

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
- **Android minimum SDK 24** (`SupportedOSPlatformVersion`) — Play's
  automatic protection rejects bundles below 24, don't lower it
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
0. **Stabilisation pass (agreed 2026-10-29, before any new feature)**: Axis is
   a private, whitelisted friends app, so everyday reliability comes first.
   Done: (1) monetary safeguards (converted-amount limit, cross-client stale
   repayments refused), (2) dependable updates (Group Detail auto refresh,
   honest web push state), (3) event context on both clients, plus
   date-only activity labels on Android. Next: (4) invitations and everyday
   clarity — guest-add vs. clipboard, explicit copy, join preview, aligned
   categories/repayment descriptions/dates, translated errors, Enter to add a
   member, zero-decimal currencies (JPY/KRW/ISK; app assumes two decimals
   everywhere, design: one minor-units table in SQL mirrored by both
   clients); (5) accessibility and evidence gaps. The 2026-10-29 web changes
   are not deployed yet (`npm run deploy --prefix webapp`).
1. **Phase 2.5 — Google Calendar sync**, not started at all — per-user
   Google OAuth separate from Supabase Auth, encrypted token storage/
   refresh, a sync strategy, and reconciling Axis's recurrence model
   against Google's RRULE.
2. **iOS/MacCatalyst**, not in the active `TargetFrameworks` (no Mac to
   build/test against).
3. Small known gaps: a live RLS-enforcement pass on the `events`/
   `event_attendees` policies (structurally verified, not yet exercised
   under a real adversarial session). The web app has receipts, recurring expenses,
   the members roster/add-by-name flow, calendar feeds and group appearance;
   it has no remaining feature-parity gaps vs. Android (the list from
   `review-artifacts/APP_REVIEW.md` was closed 2026-10-27). PWA install + web push are implemented and live
   (see "Web app: PWA install + push notifications" below). The Android events widget
   (`EventsWidgetDataProvider`) still compares `StartsAt` against
   `DateTime.UtcNow` without normalizing Kind, the bug `EventTimes.IsPast`
   fixed for the events list — deferred pending a wider widget discussion.
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
  convention** (CHANGELOG.md's "One-account-one-member invariant fix" has
  the merge-on-claim logic). `create_group()`/`redeem_invite()`
  both look up and reuse an account's existing `members` row rather than
  minting a new one, and a trigger on `auth.users`
  (`handle_new_user_member()`) provisions the row at signup time so it
  always exists, even before the account joins any group.

Don't reintroduce a design where ledger rows reference `auth.users` or a
"Person" that assumes every participant has logged in. That collapses the
phantom-member case, which is the entire point of this schema.

## Balances: pairwise only

Group Detail's Balances section (Android and web) shows only pairwise debts:
the `pairwise_balances`/`my_pairwise_balances` views derive genuine two-party
debts directly from `expense_shares`/`expenses`, so "owes you"/"you owe" is
always literally true. "Settle" creates an `Expense` with
`IsSettlement = true` between the viewer and that one counterparty.

There used to be a "Simplified" toggle on Android (greedy debt
simplification over `group_balances` nets, `DebtSimplifier`). It was removed
2026-10-27, not ported to web: its suggested transfers could pair two people
who never shared an expense, and settling one left made-up debts in the
pairwise view. Don't reintroduce a settle-up plan that can record payments
between people with no pairwise debt. The reverse is safe: if a member's
pairwise balances are all zero, their `group_balances` net is zero too.
`group_balances`/`my_group_balances` stay — the Groups list total and
Group Detail still read them.

Web's "Te deben / Debes" summary (Groups page) and Group Detail's balance
hero are built from `my_pairwise_balances` via `lib/balanceSummary.ts`,
never from summed group nets: positive and negative pairwise rows are
totalled separately, per currency, with no conversion. A group net would
hide what you owe one person behind what another owes you. Android has
neither yet.

## Repayment review

Android repayments now use `SettlementPage`/`SettlementViewModel` at
`AppConstants.Routes.Settlement`, for both balance-action creation and
activity editing. Creation shows a payer/recipient summary, full debt by
default, editable partial amount/date and an optional note; editing has
one payer and one recipient instead of expense split controls. Empty notes
save "Settlement with {person}" / "Saldar deuda con {person}" in the
creator's language. These are deliberately stored descriptions, just like
custom notes; editing the payment direction does not regenerate existing
text. Web uses the same description policy and its own dedicated form.

`SettlementRules` preserves unchanged date timestamps and delegates money
validation to `Models/AmountInput.cs`. The same parser is used for ordinary
expense totals, manual shares, recurring forms and settlements. Input may
contain one dot or comma decimal separator, with at most two decimal
places; grouped, mixed-separator, non-positive and overflowing values are
rejected without stripping punctuation or rounding. Whitespace at the
edges is trimmed for parsing, while the field retains its raw text.

`Controls/DecimalInput` is the native attached behaviour for these entries:
it keeps a text key listener and requests the decimal IME through
`SetRawInputType`, so punctuation reaches validation unchanged. New expense
totals start empty instead of an editable zero. Generated editable amounts
use the app language without grouping. Invalid totals hide derived split
previews; invalid manual shares replace the numerical remainder with a
validation hint. Both the UI and save commands enforce validity.

Web uses `lib/amountInput.ts` with the same contract, text inputs with
`inputMode="decimal"`, and integer cents. Its expense, share and settlement
parsers no longer diverge. Both domain suites consume the same 40-case
fixture at `tests/amount-input-cases.json`. See
`reports/AMOUNT_INPUT_REVIEW.md` for browser and physical-phone checks.

The repayment screen checks balance freshness before creation, but does
not lock against concurrent clients. After a confirmed save it disables
further saves and shows the remaining debt. `SupabaseExpensesRepository`
raises `ExpenseSaveCommittedException` if the transaction succeeded but
its subsequent typed re-fetch failed; the repayment screen treats that
as saved and never invites a duplicate retry. See
`reports/ANDROID_SETTLEMENT_REVIEW.md` for physical-device verification.

## Stats tab

Attendance freshness follow-up (2026-10-28): web useLiveRefresh now refetches on
first subscription and window focus as well as reconnect/visibility/online/poll.
EventDetailPage and GroupEventsTab use RefreshRevision to reject late responses
after newer reads/local edits; loader dependencies use account ID, not session
object identity. Native EventDetailPage and GroupDetailPage check visible event
data every 30 seconds, fetching at age >=60 seconds and forcing refresh on
resume/reconnect (and Events tab selection). Timers stop on Window.Stopped and
page disappearance; refreshes skip pending writes and reject overtaken data.
Native uses polling, not Realtime. See reports/LIVE_ATTENDANCE_REVIEW.md:
production web checks pass; Android is built/installed but physical UI polling
and lifecycle checks remain unverified.

Group Detail's third tab (`GroupStatsView`/`GroupStatsViewModel`) — pure
read-only aggregation over a group's whole expense history, no new
schema/view (CHANGELOG.md's "Stats tab" entry has the build history).
Current-state facts worth knowing before touching
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
  NuGet Packages section below for the other real bug this shipped with
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
  the group's whole aggregate. Each group card explains its balance with
  three lines that add up to it ("Pagaste por X", "X pagó por ti",
  "Liquidaciones"), computed in `Models/PairBreakdown.cs` (pure, covered by
  the regression tests), then lists the expenses between the two of you with
  whose part each amount is. Web mirrors it at
  `/groups/:groupId/members/:memberId` (`pages/MemberProfilePage.tsx`,
  `lib/pairBreakdown.ts`), linked from each Members row. Redesigned 2026-10-27: the old card showed
  unlabeled per-category totals that mixed both directions and skipped
  settle-ups, so they never connected to the balance.
- **Export (`Services/StatsExporter.cs`)** — CSV is the group's whole raw
  expense history (general-purpose, not Stats-specific); PDF is a
  numbers-table snapshot of the current tab's aggregations, over whichever
  `StatsDateRange` is selected. PDF uses `SkiaSharp.SKDocument.CreatePdf`
  directly — no separate PDF library, confirmed present in the
  already-installed `SkiaSharp` package. Both hand off to the OS share
  sheet via `Share.Default.RequestAsync(ShareFileRequest)`, the same API
  `ProfileViewModel`/`InviteToGroupViewModel` already use for a text share
  — this was the app's first *file* share.
- **Charts show their values, not tooltips.** LiveCharts' default tooltip only
  showed the row label and rendered empty on the top row, so every chart has
  `TooltipPosition="Hidden"` and bars/columns print their value at the tip
  (`Bars`/`Columns` in `GroupStatsViewModel`). Value axes start at 0 with 30%
  headroom for the end label; count axes step by 1.
- **Web** (`components/GroupStatsTab.tsx`, aggregations in `lib/stats.ts`,
  covered by the node tests): same tab, filters and charts, hand-drawn in
  HTML/SVG — no chart library. Fetches the whole history in 1000-row pages.
  CSV downloads in the browser with Android's exact columns; "PDF" is the
  browser print dialog over a print-only layout of the same four tables.

## Architecture

### Backend abstraction — why it exists, and the one rule

Every data access interface lives in `Services/` (`IAuthService`,
`IMembersRepository`, `IGroupsRepository`,
`IExpensesRepository`, `IBalancesRepository`, `IRecurringExpensesRepository`,
`IInvitesRepository`, `IDeviceTokensRepository`, `IEventsRepository`,
`IAliasesRepository`, `IAvatarsRepository`, `IReceiptsRepository`,
`ICalendarSubscriptionsRepository`). The other `I*` services there
(`IGoogleAuthService`, `IPushRegistrationService`, `ISecretClipboardService`,
`IWidgetRefreshService`) wrap platform features, not data access.
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
(e.g. `Splash`, `Groups`, `AddExpense`) rather than hardcoded
strings — follow that pattern for any new screen. The Events list itself
has **no** separate route — it lives embedded in `GroupDetailPage`'s Events
tab (`GroupEventsView`), not as its own navigated page; only `AddEventPage`
needed a real route, same shape as `AddExpensePage`. Same for the Stats
tab (`GroupStatsView`, no route of its own). Group Detail accepts `tab=events`
(Android query attribute; web `?tab=events|stats` in the URL, replaced on tab
switch) so event save/delete/Back and reload land on Events. Android event
deep links and push taps navigate Groups → that group's GroupDetail (Events)
→ EventDetail via `App.NavigateAsync`, so Back reaches the event's own group;
`GroupDetailViewModel.LoadAsync` sets `GroupName` from the loaded group, since
those routes carry no `groupName` param. Event-linked expenses list every
member as participant/payer, with Going attendees preselected (everyone if
nobody is Going), on both clients. `Splash` is the first
`ShellContent` in `AppShell.xaml`; it decides between `//Login` and
`//Groups` before anything else renders (`SplashPage.OnAppearing` must
`await Task.Yield()` before touching `Shell.Current` — Shell's own initial
navigation to `//Splash` can still be mid-flight otherwise, a real crash
documented in CHANGELOG).

Avoid `Shell.Current.DisplayPromptAsync` entirely — a known WinUI fail-fast
crash on Windows (microsoft/microsoft-ui-xaml#10897). Use a dedicated page
or an inline overlay instead. `DisplayAlert`/`DisplayActionSheet` are
confirmed safe standard MAUI primitives; `Toast.Make(...).Show()` is
**not** — it throws `COMException` on this unpackaged Win32 build. The
existing callers (`ProfileViewModel`, `InviteToGroupViewModel`,
`JoinGroupViewModel`) wrap it in a try/catch `TryShowToast` helper so it
works on Android and is a silent no-op on Windows; never call it bare.

### Deep linking (invites and events) — one domain

There is exactly one Axis domain: **`app.axisapp.aitorsansal.com`**, the web
app (`AppConstants.Links.AppHost`). The old `axisapp.aitorsansal.com` static
site (`web/`) was retired 2026-10-27 with no redirects — old links are dead
by design. A shared link *is* the web app page itself, so it opens in the
Android app when installed and in the web app otherwise:

- Invites: `/join?code=...` (`BuildInviteUrl`; web `MembersPage.buildInviteUrl`).
- Events: `/groups/{groupId}/events/{eventId}` (`BuildEventUrl`).

How the pieces fit — easy to change one side and forget the other:

- **`webapp/public/`** holds every non-SPA file: `.well-known/assetlinks.json`
  (Android's Digital Asset Links proof, the app's signing-cert SHA-256
  fingerprints), `version.json` (`AppUpdateService`), the static `reset/`,
  `confirm/`, `privacy/`, `termsofservice/`, `delete-account/` pages and
  `email-assets/`. Cloudflare serves real files ahead of the SPA fallback
  (`webapp/wrangler.jsonc`), so none of them are behind login.
  `supabase/email-templates/` are pasted into the Supabase dashboard, not
  served.
- **`AxisApp/Platforms/Android/MainActivity.cs`** declares the App Links as
  `[IntentFilter(..., DataHost = "app.axisapp.aitorsansal.com", ...,
  AutoVerify = true)]` attributes — `DataPath = "/join"` and
  `DataPathPattern = "/groups/.*/events/.*"`. Only those two: claiming all of
  `/groups/` would make every copied web URL open the app. No manifest XML,
  it's all in the C# attribute. `AutoVerify` only succeeds once Android has
  fetched and matched `assetlinks.json` against the APK's actual signing
  certificate (verification runs at install time), so a cert mismatch makes
  links silently fall back to the browser with no error anywhere.
- `MainActivity.OnCreate`/`OnNewIntent` both funnel the incoming `Intent`
  into `App.HandleDeepLink(uri)`, queued in `App.pendingRoute` if the app
  isn't ready to navigate yet (see the `App.isReadyToNavigate` note below —
  this is shared with push-notification taps, not just invite links).
  `SplashPage` calls `App.ReplayPendingDeepLinkAsync()` once it's picked
  Login vs. Groups. A queued route stays pending while signed out (replay is
  a no-op without a session); `LoginViewModel`/`RegisterViewModel` call the
  same replay after landing on Groups, so a link opened signed out resumes
  after login instead of hitting RLS with no session ("Group not found").
  `AppConstants.Links.TryExtractCode`/`TryExtractEvent`
  are the only parsers for those two link shapes.
- A signed-out web visitor opening either link is sent through `/login` and
  back (`ProtectedRoute`'s `axis.returnTo`).
- **`App.isReadyToNavigate`, not `Shell.Current is null`, gates queued
  navigation** — a Firebase-triggered process start (see push notifications
  in CHANGELOG) can construct MAUI's whole `Application`/`Window`/`Shell`
  graph with no Activity ever appearing, so `Shell.Current` can be non-null
  well before `SplashPage.OnAppearing` has actually restored a session. Set
  `isReadyToNavigate = true` only once that restore has genuinely finished.
- iOS Universal Links would need the equivalent (`apple-app-site-association`
  under `webapp/public/.well-known/`, entitlements on the iOS target) but iOS isn't in
  the active `TargetFrameworks` yet, so this is Android-only today.

### Web app: PWA install + push notifications

Live in production. Two purposes at once: makes
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

### Web live refresh

Event screens refresh through `webapp/src/lib/useLiveRefresh.ts` (Supabase Realtime on `events`/`event_attendees`, which `supabase/realtime_event_attendees.sql` added to the `supabase_realtime` publication, plus focus/online/reconnect refetch and a 60s visible poll, skipped while the viewer's own writes are pending). Views can't be published: a live balance would subscribe to the underlying tables. Android has no Realtime.

Group Detail balances/activity (2026-10-29) refresh without Realtime on both clients: web uses `useLiveRefresh` with an empty table list (focus/online/visible 60s poll); Android's `GroupExpensesViewModel.RefreshIfStaleAsync` follows the events tab lifecycle (30s tick, 60s stale, forced on resume/reconnect/tab switch). Neither touches the activity list during a search or after load-more (balances only), neither interrupts an open repayment, and background failures keep stale data silently. Device-verified: web→phone ~76s untouched, phone→web within one poll. Web push state gains `'unavailable'` (check failed or service worker not ready in 10s) so Profile never silently hides the Notifications section.

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
- **Web visual style ("Bloque", 2026-10-28) diverges from Android on purpose**:
  neutral near-black surfaces, self-hosted Bricolage Grotesque
  (`@fontsource-variable/bricolage-grotesque`, imported in `main.tsx`), no
  borders (filled surfaces; `.card`/controls keep a transparent border so
  forced-colors still outlines them), large text tabs, flat balance rows.
  Tokens live in `webapp/src/index.css`; Android has not been ported. On
  desktop the home `AppHeader` is visually hidden (sidebar has brand,
  profile and logout) — any action added to that header needs a sidebar
  equivalent. Visual review fixture: `npx vite webapp --config
  webapp/tests/visual.vite.config.ts` (mocked read-only data, port 5175;
  `?qaTab=1|2`, `?qaAccent=`, `?qaSignedOut=1`,
  `/tests/fixtures/mobile-frame.html#<path>` for a true 390px viewport).
- **Web accent presets** (`webapp/src/lib/accent.ts`) copy the same 8 presets'
  values from `AccentPalettes.cs` into the accent CSS variables in
  `index.css`, applied in `main.tsx` before the first render. Every web accent
  colour must go through those variables. Never use `--primary` as text on the
  dark surface: it fails contrast for most presets (Navy 2:1). Use
  `--action-text`, the light tint, instead.
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
| `events` / `event_attendees` | Group events with 3-state RSVP (`going`/`maybe`/`not_going`) and transport/carpooling fields. `is_birthday` rows are auto-generated, non-RSVPable member birthdays. `events_end_after_start` check (`ends_at is null or ends_at > starts_at`) — applied to the live DB via `supabase/event_time_validation.sql` and validated against all historical rows. |
| `device_tokens` | Per-account push tokens for the notification feature. |
| `member_aliases` | Private, per-account nickname override for how a member is displayed. |
| `expense_history` | Previous version of an expense (row + shares as JSON) on every update/delete, trigger-written, read-only for group members. |
| `calendar_subscriptions` | One per member: the secret token behind that member's ICS calendar feed URL. Regenerating rotates the token. |
| `allowed_signup_emails` | **Signup is invite-only**: a trigger on `auth.users` aborts any signup (every auth path) whose email isn't in this table. RLS on with no policies, so the app can't read it; add people with a manual `insert`. |

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
(atomicity only). `record_expense_history()` (no insert policy on
`expense_history`) and `sync_expense_converted_total()` (a deferred constraint
trigger keeping `amount_in_group_currency` equal to the sum of the converted
shares, which `protect_expense_columns` would otherwise revert) are security
definer too — see `supabase/currency_integrity.sql`. Expense deletes are
limited to creator, payer or group owner by a raising trigger, not the RLS
policy, so a refused delete errors instead of silently affecting 0 rows. Column locks on `members`/`groups`/`invites`/`events`/
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
| `SkiaSharp` | 3.119.0 | Client-side image resize/WebP encode for avatar uploads; PDF export (`SKDocument.CreatePdf`, see "Stats tab" above) |
| `LiveChartsCore.SkiaSharpView.Maui` | 2.0.5 | Charts on the Stats tab (see "Stats tab" above) |
| `Xamarin.AndroidX.Credentials` | 1.6.0.1 | Android-only: Credential Manager for native Google sign-in |
| `Xamarin.AndroidX.Credentials.PlayServicesAuth` | 1.6.0.1 | Android-only, paired with the above |
| `Xamarin.Google.Android.Libraries.Identity.GoogleId` | 1.1.0.15 | Android-only: Google ID token credential type |
| `Xamarin.Firebase.Messaging` | 125.1.1.1 | Android-only: FCM push notifications |
| `Xamarin.AndroidX.Fragment.Ktx` | 1.9.0 | Android-only pin, not used directly: aligns Credential Manager's Fragment.Ktx with Firebase's transitive Fragment 1.9.0. Without it, R8/dex fails on a duplicate `FragmentKt.class`. Don't remove it as unused. |
| `QRCoder` | 1.8.0 | Invite QR codes (`InviteToGroupViewModel`) |

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

## Build Commands

```bash
# Build for Android
dotnet build AxisApp/AxisApp.csproj -f net10.0-android

# Build for Windows
dotnet build AxisApp/AxisApp.csproj -f net10.0-windows10.0.19041.0
```

```bash
# Deploy the web app, which also serves assetlinks.json, version.json and the
# static reset/confirm/privacy pages (see "Deep linking" above).
# Always use this script: it builds first, while a bare `npx wrangler deploy`
# re-uploads the stale dist/. Use npm, not pnpm (webapp/ is npm-only; its
# pnpm files were removed 2026-10-27). Build locally: VITE_* values live in
# the gitignored webapp/.env.local, so a Cloudflare Git-connected build ships
# a blank page unless they are set as Cloudflare *build* variables.
npm run deploy --prefix webapp
```

The deploy script now runs HTTP smoke checks after publishing: the home route,
`/profile` SPA fallback and the JavaScript entry must match the local build.
`npm run smoke --prefix webapp` repeats these checks; failure reports a bad
release without automatic rollback. Vite rejects missing/malformed Supabase
build settings before generating assets. Browser bootstrap validates the same
settings before importing App, with an English/Spanish retry screen on failure
and static HTML loading/reload copy if JavaScript cannot load. See
`reports/STARTUP_REVIEW.md` for deployed verification and limits.

```bash
# Regression tests (no MAUI dependency — the test project links the pure
# domain files Models/ExpenseMath.cs, EventTimes.cs, DateFormats.cs,
# PairBreakdown.cs, RecurringSchedule.cs directly via <Compile Include>, so
# it builds as plain net10.0)
dotnet run --project tests/AxisApp.RegressionTests

# Web app regression tests (node:test, mirrors the C# checks against
# webapp/src/lib/expenseMath.ts, dates.ts, serializedUpdates.ts,
# coalescedWrites.ts, recurringSchedule.ts, stats.ts, amountDisplay.ts,
# pairBreakdown.ts)
npm test --prefix webapp
```

These are plain assertion runners, not an xUnit/NUnit suite, and the test
project isn't in `AxisApp.slnx`. Any logic you want covered there has to stay
free of MAUI/Supabase types, since the test project compiles those files on
their own.

## Saved split intent (2026-10-28)

Expenses and recurring templates now have nullable `split_mode` (`equal`/`manual`). NULL means historical intent is unknown; both editors open it as Manual. Models, RPC payloads, schema and recurring materializer carry the field. Do not infer Equal from equal-looking shares. Android `ExpenseSplitMode` and web `splitMode.ts` preserve saved per-member cent allocation on unchanged Equal edits, recalculate for total/participant changes, and reset allocation when Equal is explicitly selected. The user confirmed `supabase/split_mode.sql` applied and web operation; Android 1.1.0 (10) device verification is in `reports/ANDROID_SPLIT_MODE_REVIEW.md`. The isolated QA group and its paused recurring template remain. No new database migration is needed for this Android update.

## Calendar feed timestamp formatting (2026-10-28)

`supabase/functions/calendar-feed/index.ts` converts timestamps with `new Date(value).toISOString()` before basic UTC formatting; never strip an offset and append Z without converting the instant. The deployed function imports `npm:@supabase/supabase-js@2.118.0` because JSR metadata downloads timed out. This is still a single-file dashboard deployment; preserve the existing token-based access and JWT-verification setting. `supabase/tests/calendar_feed.test.mjs` exercises its actual helpers and ICAL.js import. Live feed verification is recorded in `reports/CALENDAR_FEED_REVIEW.md`.

## Web expense unsaved changes (2026-10-28)

Web now uses createBrowserRouter/RouterProvider with the existing route tree/providers. `useUnsavedChanges` and `UnsavedChangesDialog` protect ordinary and recurring AddExpensePage edits; repayments and events are not wired yet. `expenseEditorSnapshot` normalizes valid amounts and participant order while preserving split intent and invalid input. The baseline is keyed to the loaded account/route; keep token refreshes from reloading the form. Call `markSaved()` only after a confirmed successful save/delete, immediately before navigation. Native beforeunload handles document exits; internal navigation uses useBlocker. Deep-entry ancestry reconstruction dispatches popstate to synchronize router history indices; retest cold-link Back/Forward if changing that code. Isolated browser fixtures live in `webapp/tests/unsaved.vite.config.ts`; never use the fixture config for production. See `reports/UNSAVED_WEB_REVIEW.md`. The web stage is local, not deployed. Android expense/recurring protection is now installed and device-verified; see the following section.

## Android expense unsaved changes (2026-10-28)

Later extension: AddEventPage and SettlementPage now inherit GuardedEditorPage
and implement IUnsavedChangesEditor through their view models. FormEditorSnapshots
captures event/repayment values; baselines are captured after loading and only
confirmed commits bypass navigation protection. Web AddEventPage and both
SettlementForm parents use useUnsavedChanges; local repayment Cancel/Escape/
backdrop closure uses requestClose with the same confirmation. Keep one route
blocker per mounted page. See reports/FORM_UNSAVED_REVIEW.md: browser checks pass,
Android is clean-built/installed, but native tap-through QA remains pending.
The earlier paragraphs below record expense/recurring implementation history.

AddExpensePage subscribes to Shell.Navigating while visible and uses GetDeferral/DisplayAlertAsync to protect ordinary/recurring edits. ExpenseEditorSnapshot normalizes actual editable values; UnsavedEditGuard captures only fully loaded data and compares on navigation. MarkCommitted is called only after confirmed repository success before leaving. Pending operations cancel attempted navigation and show a wait alert without holding a deferral. Keep or native-dialog dismissal cancels the original route; Discard completes it. Never start a second GoToAsync while a navigation deferral is pending. Native 1.1.0 (10) was installed and tested on the connected phone; details/data inventory are in `reports/ANDROID_UNSAVED_REVIEW.md`. Events, repayments and crash-recovery drafts remain separate work. No additional SQL is required.
