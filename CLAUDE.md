# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

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

## Current state (read this before assuming something exists)

This repo is a scaffold, not a finished app. As of this commit:

- A real Supabase project exists, `supabase/schema.sql` has been run against
  it, and the project's URL + publishable key live in `Config.Local.cs`
  (gitignored — see `Config.Local.cs.example` for the template).
- `IAuthService` is registered as `SupabaseAuthService`
  (`Services/SupabaseAuthService.cs`), which constructs a real
  `Supabase.Client` and calls `SignUp`/`SignIn`/`SignOut` on its `Auth`
  client. **This file's exact API surface (method/property names on
  `client.Auth`) was written from fetched SDK docs, not verified against a
  local build** — the `Postgrest` namespace on the Models was already wrong
  once this same way (docs said `Supabase.Postgrest.*`, the installed package
  actually exposes `Postgrest.*`). If this file doesn't compile, that's
  expected until someone reports the actual compiler errors back.
  `NotConfiguredAuthService` still exists as an unregistered fallback/example
  of the "boots without a backend" pattern.
- Every `I*Repository` now has a concrete `Supabase*Repository`
  implementation (`Services/Supabase{Members,Groups,Expenses,
  Balances,RecurringExpenses,Invites,DeviceTokens}Repository.cs`),
  registered as singletons in `MauiProgram.cs` alongside a single shared
  `Supabase.Client` (also registered there — `SupabaseAuthService` and every
  repository take that same instance rather than each opening its own).
  **Same caveat as `SupabaseAuthService`: the exact Postgrest query call
  shapes (`.Filter`/`.Insert`/`.Update`/`.Get`/`.Single`, the
  `Postgrest.Constants.Operator`/`Ordering` enum members, and especially
  `SupabaseInvitesRepository.RedeemAsync`'s use of `client.Rpc(...)`) are
  grounded against the public postgrest-csharp source, not a local build of
  this exact installed version — expect compiler errors, report them back
  the same way.** Includes `expenses`/`expense_shares` (N-way splitting;
  `payments` was retired 2026-09-04, see "Merge payments into expenses"
  below — a settle-up is just an Expense now), a `group_balances` view/
  repository, and a `device_tokens` table/repository for the push feature —
  see `/SCOPE.md`. **The corresponding schema additions at the bottom of
  `supabase/schema.sql` haven't been run against the live project yet** —
  run that new block before testing any of this against real data.
- The four core screens exist: `Pages/{Groups,GroupDetail,AddExpense,
  JoinGroup}Page.xaml` + matching `ViewModels/`, wired to the real
  repositories and navigated via `AppShell`/`AppConstants.Routes`. Built
  from a high-fidelity design handout (Claude Design output, not checked
  into this repo — ask if you need it re-generated) that locked colors,
  spacing, and component shapes; `Resources/Styles/Styles.xaml` gained new
  FAB/avatar/segmented-pill/split-checkbox/balance-tile styles to cover what
  the existing token set didn't have. `AddExpensePage` doubles as edit: a
  `?expenseId=` query param switches it into edit mode (loads the existing
  expense + shares, `Save` calls `IExpensesRepository.UpdateAsync` instead
  of `AddAsync`, adds a Delete button) — reached by tapping an expense row
  in Group Detail's recent-activity list. **Editing a settle-up is done too,
  as of 2026-09-04** — a settlement is just an Expense with `IsSettlement`
  true, so it reuses this same edit flow (category/receipt hidden for that
  case) rather than a separate screen — see "Merge payments into expenses"
  below.
- **Auth + a real repository call are now confirmed working end to end**:
  fresh sign-up followed by creating a group succeeds and the group shows up
  in the creator's list. Reaching that point surfaced four real bugs, all
  fixed and worth knowing about:
  1. `SupabaseGroupsRepository.CreateAsync` originally only inserted a
     `groups` row — it never made the creator an actual member, so a newly
     created group would've been invisible in the creator's own list (RLS's
     "select groups you belong to" requires a real `group_members` row).
     Fixed to also create a `Member` + `GroupMember` for the creator,
     mirroring `redeem_invite()`'s "fresh join" shape.
  2. `GroupMember` and `ExpenseShare` both have a real composite primary key
     in Postgres (`(group_id, member_id)` / `(expense_id, member_id)`) but
     only mark ONE property `[PrimaryKey]` in the C# model — harmless for
     insert/delete (which never relied on it), but a real footgun for
     `.Update(model)`, which would match on that single column and silently
     update every row sharing it. `SupabaseExpensesRepository.UpdateAsync`
     works around this with an explicit `.Filter(...).Filter(...).Update(...)`
     rather than trusting the implicit PK match — do the same if
     `GroupMember` ever needs an update path.
  3. **Creating a group threw `new row violates row-level security policy
     for table "groups"` even with an unquestionably-correct session** —
     root cause was Postgres RLS, not auth: a Postgrest `.Insert(...)` does
     `INSERT ... RETURNING`, and Postgres requires the returned row to also
     satisfy the table's `SELECT` policy, not just `INSERT`'s `WITH CHECK`.
     `CreateAsync` inserts the `groups` row *before* the creator has a
     `group_members` row, so `is_group_member(id)` was still `false` at the
     exact moment Postgres tried to hand the new row back — confirmed by the
     fact that setting `WITH CHECK (true)` didn't fix it either. Fixed by
     widening `groups`' `SELECT` policy to `is_group_member(id) or
     created_by = auth.uid()` (see `schema.sql`), mirroring how `update`/
     `delete` on `groups` already trust `created_by = auth.uid()`. **Watch
     for the same shape of bug anywhere else an insert happens before the
     inserted row would otherwise satisfy its own table's `SELECT` policy** —
     `SupabaseMembersRepository.AddPhantomAsync` (inserts a `Member` with
     `account_id` null, into no group yet) is a plausible next place to hit
     this, not yet confirmed either way.
  4. `Member.IsPhantom` (`AccountId is null`) is a computed, get-only
     convenience property with no `[Column]` attribute. Postgrest's
     Newtonsoft-based serializer included it in the INSERT body anyway
     (`PGRST204: Could not find the 'IsPhantom' column of 'members'`) since
     nothing told it to skip a property that isn't a real column. Fixed with
     `[JsonIgnore]`. Check any future computed/derived properties on a model
     get the same attribute.
- Multi-theme (preset color palettes the user picks from) and a
  fully-custom user-defined palette were discussed but are **not planned
  work** — see `/SCOPE.md`'s Theming section for the technical read on
  feasibility if it comes up again.

Next real milestones, in order: (1) ~~run the new blocks at the bottom of
`supabase/schema.sql` against the live project~~ **done** — expenses/
expense_shares/balances views/device_tokens, the expense_shares update
policy, and everything from the 2026-08-25 sessions (widened RLS policies,
`payment_net` fix, `pairwise_balances`/`my_pairwise_balances`) are all live;
(2) ~~get everything actually compiling and confirm auth + a repository call
work end to end~~ **done** — sign-up, create-group, adding/linking phantoms,
and full invite redemption (claim + fresh-join) are all confirmed working
against the live project; (3) build the actual "Settle up" UI — the
`payments` write path and both balance-display modes are ready for it (see
"Balances: simplified vs. pairwise" above), just no button calls
`IPaymentsRepository`'s create path yet; (4) the Supabase-side infra
`/SCOPE.md` describes but that has no code yet — receipt storage/upload, the
cleanup Edge Function, recurring payment materialization, push.

See **`SCOPE.md`** for the full product scope and phased roadmap (the
debt-tracker vertical currently being built, plus the events/calendar and
Google Calendar sync phases planned after it) — it's the source of truth for
what's in/out of scope and why, so check it before assuming a feature is or
isn't planned.

## The core design decision: members vs. accounts

This is the one thing to understand before changing the schema or the data
layer. See `supabase/schema.sql`'s header comment for the full reasoning; in
short:

- A **member** (`members` table) is a ledger participant — the thing
  `payments.payer_member_id`/`payee_member_id` point at.
- An **account** (`auth.users`, Supabase Auth) is a real login.
- A member is either **phantom** (`account_id is null` — added by name only,
  like adding a person in the old local app) or **claimed** (`account_id`
  set — a real person who signed in).
- Payments never reference `auth.users` directly, only `members`. This means
  adding "a debt with my dad" works identically whether or not he has an
  account yet.
- **Claiming**: when a phantom's real person signs up, redeeming an invite
  that targets that specific phantom (`invites.target_member_id`) links their
  new account to the *existing* member row — their prior payment history
  stays attached, nothing is recreated.

Don't reintroduce a design where `payments` reference `auth.users` or a
"Person" that assumes every participant has logged in. That collapses the
phantom-member case, which is the entire point of this schema.

**Cross-group phantom duplication — fixed 2026-08-25.** Phantom members used
to be scoped per-group: `AddPhantomAsync` always inserted a brand-new
`members` row, with no lookup against phantoms the same account already
created elsewhere, so adding "Maria Lopez" in two different groups created
two unrelated phantom rows. Since claiming links an account to exactly one
`members` row (`invites.target_member_id`), a duplicated phantom meant one of
the two histories would end up permanently orphaned. Fixed with an explicit
"is this someone who already exists?" step rather than silent name-matching
(silent auto-merge was considered and rejected — two different real people
sharing a name would wrongly merge their debts onto a stranger):
- `IMembersRepository.SearchVisibleByNameAsync` (backed by
  `SupabaseMembersRepository`) searches members by name, scoped automatically
  by the existing `members` `SELECT` RLS policy — never an app-wide directory,
  only people the account can already see (shared group, created themselves,
  or their own claimed row).
- `JoinGroupViewModel`'s "Add a member by name" field now shows live
  suggestions as you type (`NameMatches`/`MemberMatchItem`). A **phantom**
  match gets a "Link" action (`LinkExistingMemberCommand` →
  `AddToGroupAsync` on the *existing* row — no new phantom, no new invite
  needed for it). A **claimed** (real-account) match gets no linking action
  at all, only an informational "already on Axis, send them the invite code
  instead" — a real account must always join by redeeming an invite itself,
  never be added to a group by someone else's action. See
  `Pages/JoinGroupPage.xaml` for the UI.
- Required widening the `group_members` insert RLS policy (`schema.sql`,
  `"group members can add members"`), previously restricted to a group's
  creator only — the Link action (and plain phantom-adding) needs to work for
  any existing member, not just whoever created the group.

One structural gap this **doesn't** fix, found while testing the above: a
brand-new account with zero groups had **no UI path at all** to redeem an
invite code — `JoinGroupPage` was only ever reached from an existing group's
overflow menu. Fixed by adding a `JoinGroupCommand` + "Join with code" button
directly on `GroupsPage` (navigates to `JoinGroup` with no `groupId`, a case
`JoinGroupViewModel` already handled correctly).

**Invite redemption was completely broken, project-wide, until 2026-08-25 —
found only once someone actually tried to redeem one.** `Invite.ExpiresAt`
was never set in `SupabaseInvitesRepository.CreateAsync`, so it defaulted to
C#'s `DateTime.MinValue` (`0001-01-01`) and Postgrest sent that literal value
on every insert — silently overriding the table's real
`now() + interval '7 days'` default, the exact same shape of bug as the
`Token`-defaulting-to-`""` issue already documented below. Since
`redeem_invite()` checks `expires_at < now()`, **every invite ever created,
from every test session, was "expired" the instant it was made** — nobody
could ever successfully join a group or claim a phantom via invite code.
Fixed by setting `ExpiresAt = DateTime.UtcNow.AddDays(7)` explicitly, same
pattern as `Token`. Pre-fix rows already in the live table were left alone
(a bulk `UPDATE` backfill was attempted but blocked by this environment's
safety guardrails as a broad data mutation) — new invites are fine, anything
minted before this fix needs re-issuing via "Resend".

Redemption success/failure reporting had its own bug on top of that:
`JoinGroupViewModel.JoinByCode`/`Resend`/`CopyLink` all called
`Toast.Make(...).Show(...)` directly, which throws
`COMException 0x80070490` on this unpackaged Win32 build — `Microsoft.Windows
.AppNotifications.AppNotificationManager` isn't registered for a Win32 app
that isn't MSIX-packaged, and any unhandled exception from an async
`[RelayCommand]` fail-fasts the whole WinUI process (a systemic gap, not
specific to these three commands — worth a global fix later, e.g. wrapping
`AsyncRelayCommand` execution or hooking its `ExecutionTask`, but not
attempted here). `Resend` crashed the whole app outright; `JoinByCode` was
worse — it wrapped both the real `RedeemAsync` call *and* the toast in one
`try/catch`, so a **successful** claim (confirmed via the DB — `account_id`
was correctly set) still showed the user a scary
`"No se ha encontrado el elemento."` failure message and never navigated
anywhere. Fixed with a shared `TryShowToast` helper that swallows the toast
exception specifically, used by all three commands — a surgical fix for
these three call sites, not the broader systemic issue described above.

## Balances: simplified vs. pairwise, and the payment_net sign bug

**Found and fixed 2026-08-25**, during design discussion about an eventual
"Settle up" feature (not built yet — see below): `group_balances`'s
`payment_net` CTE (`schema.sql`) had `payer_member_id`/`payee_member_id`
deltas backwards. A `Payment` is a settle-up ("I paid you back $20" —
SCOPE.md), so the payer's balance should move *toward* zero (debt reduced)
and the payee's should too (credit reduced) — the view did the opposite,
which would have doubled every debt instead of clearing it, the first time
anyone actually used a create-payment flow. Never caught before because no
such flow existed yet to exercise it. Fixed in `schema.sql` and the live
view; swap payer/payee if this class of view is ever touched again.

Separately, `GroupDetailViewModel`'s Balances section used to show a
completely uninvolved member fake personal debts — e.g. an account that
wasn't party to any expense in a group still saw "X owes you $16.67". Root
cause: `group_balances` computes each member's net position against the
*whole group's shared pot*, not a pairwise debt with whoever's looking at
the screen — that only happens to coincide in a 2-person group, which is why
it went undetected until a genuine third party looked at a multi-member
group. Fixed with two selectable display modes, chosen via a per-device,
per-account, per-group **local-only preference**
(`AppConstants.Preferences.BalanceDisplayModePrefix` +
`Microsoft.Maui.Storage.Preferences` — deliberately never synced; it's a
viewing preference, not group state, so there's no reason every member has
to see the same one):
- **Simplified** (default): `Services/DebtSimplifier.cs` runs the standard
  greedy debt-simplification algorithm (match biggest creditor against
  biggest debtor, repeat) over every member's `group_balances` net —
  Tricount/Splitwise's "settle up" behavior, where offsetting/cyclic debts
  net out to fewer, smaller transfers than the raw history. Only labeled
  "you owe"/"owes you" when the viewer is actually a party to that specific
  transfer; otherwise shown neutrally ("X pays Y").
- **Pairwise** ("Detailed" toggle on Group Detail): new `pairwise_balances`/
  `my_pairwise_balances` views (`schema.sql`) derive genuine two-party debts
  directly from `expense_shares`/`expenses`/`payments` — no new tables, just
  a different aggregation of data already there (each non-payer share-holder
  owes the payer their share, same convention `group_balances` already uses,
  just kept broken out per counterparty instead of collapsed to one total).
  Always literally true "owes you"/"you owe" language, may show more/smaller
  line items than Simplified for the same underlying numbers.

**Not built yet, discussed but explicitly deferred:** a "Settle" button that
creates a real `payments` row from either mode — both modes would feed the
same write primitive (`payer`, `payee`, `amount`), just sourced differently
(real counterparty in Pairwise, the simplification algorithm's suggested
transfer in Simplified), so this doesn't block on picking one mode over the
other. Also deferred: letting someone exclude a specific counterparty from
simplification ("I don't want to settle with X, route my debt through
someone else instead") — genuinely possible, but needs pairwise data as
simplification's *input* (a constrained matching/flow problem), not just as
an alternate display mode, so it's a materially bigger feature than either of
the above. **Update:** the "Settle" button described here now exists — see
below.

## Session persistence, sign-out, Settle, and crash-safety (2026-08-25)

Ported from a sibling MAUI project (`PokeCards`, same `Supabase` 1.6.0
package) rather than designed from scratch:

- **Session persistence was silently dead on arrival.** `App.xaml.cs`'s
  `window.Created` hook and `SupabaseAuthService.RestoreSessionAsync` were
  both already wired up, and `AppConstants.Preferences.SupabaseSession` was
  already declared — but `Supabase.Client` was never given a `SessionHandler`
  (`SupabaseOptions`), so there was nothing to persist to or restore from,
  and every launch fell through to Login even right after signing in. Fixed
  with `Services/SupabaseSessionPersistence.cs` (`IGotrueSessionPersistence
  <Session>` backed by `SecureStorage`, ported verbatim from PokeCards) wired
  into the `Supabase.Client` registration in `MauiProgram.cs`. That alone
  still didn't fix it: `RestoreSessionAsync` only called
  `client.InitializeAsync()`, never `client.Auth.LoadSession()` first —
  confirmed via instrumented logging that `SaveSession` fired correctly on
  every sign-in but `LoadSession` was never once called on a fresh launch.
  Fixed by adding that call (PokeCards' `SupabaseService.InitializeAsync`
  does both, in that order). Verified end-to-end: sign in → kill process →
  relaunch → lands on Groups, no login screen.
- **Sign-out**: tapping the avatar on `GroupsPage` opens a small dropdown
  (`IsAccountMenuOpen`, a plain `Border` overlay + scrim — not a native
  dialog) showing the account email, a disabled "Profile" placeholder (no
  Profile screen designed yet), and "Log out" (`GroupsViewModel.Logout` →
  `IAuthService.SignOutAsync()` → `Routes.Login`). Confirmed logout also
  destroys the persisted session (`DestroySession()` fires automatically via
  the same `SessionHandler`), so a relaunch after logout shows Login again,
  not an auto-restore.
- **Crash-safety net**: every `[RelayCommand]` async body in every ViewModel
  now runs through `BaseViewModel.RunSafeAsync` (ported from PokeCards'
  `BaseViewModel`/`IErrorPresenter`, adapted — see below) instead of
  executing bare. Without this, an unhandled exception from
  `CommunityToolkit.Mvvm`'s `AsyncRelayCommand` posts back to the WinUI
  dispatcher outside any try/catch and fail-fasts the whole process
  (0xc000027b) — confirmed repeatedly this session by a transient Supabase
  "JWT issued at future" clock-skew rejection crashing the app on nearly
  every sign-in. **Adaptation from PokeCards, not a verbatim port**:
  PokeCards shows errors via a `CommunityToolkit.Maui` `Popup`
  (`IErrorPresenter`/`ErrorPresenter`/`ErrorPopup`), but that package's API
  changed incompatibly between the version PokeCards pins (9.1.1) and this
  app's (13.0.0) — `Popup.Close()` and `Page.ShowPopupAsync()` no longer
  exist in 13.0.0 (confirmed by an actual failed build, not assumption; the
  replacement API wasn't tracked down — see the NuGet caveat below). Rather
  than chase that, `BaseViewModel` just sets an `ErrorMessage` string
  instead, reusing the plain red-`Label` pattern `JoinGroupViewModel`/
  `NewGroupViewModel` already had individually — simpler, zero third-party
  dependency, and every page needs the same one-line `Label` binding
  (`Text="{Binding ErrorMessage}"`, `IsVisible` via
  `StringNotEmptyConverter`) that already existed on some pages. This
  doesn't fix the *systemic* class of bug for every possible failure mode —
  a *synchronous* exception thrown directly in a command handler (not from
  an awaited `Task`) or one thrown outside any `[RelayCommand]` context
  entirely isn't covered — but it covers the actual crashes observed.
- **Settle button**: `GroupDetailViewModel.Settle(MemberBalanceItem)` creates
  a real `Payment` row for one balance row's amount, working identically
  regardless of display mode — `MemberBalanceItem` now carries a raw
  `Amount` (decimal, so `Settle` never re-parses the formatted `AmountText`
  string) and, for a Simplified-mode neutral (third-party) row, the
  counterparty's `ToMemberId` alongside the existing `ToName`. Pairwise rows
  settle the real counterparty debt directly; Simplified rows settle
  whichever transfer `DebtSimplifier` suggested for that row. This is the
  first thing in the app that actually calls `IPaymentsRepository.AddAsync`
  — untested beyond a successful build, since verifying it was left to
  manual testing rather than another automated pass.

**NuGet caveat to add to the existing one below**: `CommunityToolkit.Maui`'s
`Popup` API is confirmed to have changed between 9.1.1 and 13.0.0 in a way
that breaks a straightforward port (`Close()`/`Page.ShowPopupAsync()` both
gone) — if a popup-based UI is wanted later, treat the current 13.0.0 API as
unknown and verify against a real build rather than copying older
CommunityToolkit.Maui code (PokeCards' included) as-is.

## Splash screen (2026-08-26)

Previously Shell always opened on its first `ShellContent` (`Login`), and
`App.xaml.cs`'s `window.Created` handler ran `RestoreSessionAsync()`
afterward, redirecting to `Groups` if it succeeded — so an already-signed-in
launch visibly flashed the login form before bouncing to Groups a moment
later. Fixed by adding `Pages/SplashPage.xaml` (centered "Axis" title +
`ActivityIndicator`, `Shell.NavBarIsVisible="False"` so the Shell top bar
doesn't show over it) as the new first `ShellContent` in `AppShell.xaml`
(`Routes.Splash = "//Splash"`, registered transient in `MauiProgram.cs`, no
ViewModel — nothing bindable beyond "spinning").

The restore-and-redirect logic moved from `App.xaml.cs`'s `window.Created`
into `SplashPage.OnAppearing` (wrapped in try/catch, falling back to Login
on any exception — same crash-safety reasoning as `BaseViewModel
.RunSafeAsync`, so a transient Supabase error can't fail-fast the process
before the user sees anything), which then does an absolute `GoToAsync`
to `//Login` or `//Groups` so Splash doesn't linger in the back stack.
`App.xaml.cs`'s `pendingDeepLink` cold-start queuing (a deep link can arrive
before Shell exists) still lives on `App`, but is now replayed by
`SplashPage` calling the new `App.ReplayPendingDeepLinkAsync()` after it
decides where to land, instead of unconditionally inside `window.Created`.
`CreateWindow` is back to just `new Window(new AppShell())` — `App` no
longer takes `IAuthService` at all.

## Splash-launch crash — "Pending Navigations still processing" (2026-08-31)

Found while trying to actually run the built `.exe` for the first time in a
while — a real, pre-existing bug, unrelated to whatever else was being worked
on that day: `%TEMP%\axisapp-crash.log` (an `AppDomain.CurrentDomain
.UnhandledException` logger already wired up in `App.xaml.cs`) had identical
crash stacks dated back to **2026-08-26**, meaning the app had been silently
uninstallable-by-launch on Windows for days.

Root cause: `SplashPage.OnAppearing` called `Shell.Current.GoToAsync
(destination)` immediately after `authService.RestoreSessionAsync()`. When
there's no persisted session to restore, that call can complete without ever
truly yielding — so the whole `async void OnAppearing` method ran straight
through to `GoToAsync` on the **same call stack** as Shell's own initial
navigation to `//Splash`, which was still mid-flight at that point (still
inside `MauiWinUIApplication.OnLaunched`). Shell's navigation code has a
reentrancy guard for exactly this and throws `InvalidOperationException:
Pending Navigations still processing` — an unhandled exception outside any
`[RelayCommand]`/`BaseViewModel.RunSafeAsync` path (it's thrown from deep
inside MAUI's own Shell internals during window creation, not from app code),
so it fail-fasts the whole WinUI process (`0xc000027b`) before any window
ever shows. Confirmed via Windows Event Viewer's Application log
(`APPCRASH`/`Application Error` entries, module `Microsoft.UI.Xaml.dll`,
exception code `0xc000027b`) matching the crash-log timestamps exactly.

Fixed with `await Task.Yield();` as the very first line of `OnAppearing`,
forcing the method onto a fresh dispatcher tick before touching
`Shell.Current` at all — guarantees the outer "navigate to Splash" call has
fully unwound before Splash's own redirect logic runs. Verified with several
consecutive launches (`AxisApp.exe` directly, not `dotnet build`'s own
run) — all clean, no new `axisapp-crash.log` entries.

## Leaving, transferring ownership, and dissolving a group (2026-08-31)

There was previously no way to leave a group, hand off ownership, or destroy
a group outright. The schema had already half-anticipated this: `groups` has
a `delete own groups` policy (`created_by = auth.uid()`), and the FK cascade
shape is deliberately split — `group_members`/`invites` are `ON DELETE
CASCADE` (membership and pending invites vanish with the group), while
`payments`/`expenses`/`recurring_payments` are `ON DELETE SET NULL` on
`group_id` (the ledger rows **survive**, just losing their group
association). What was actually missing:

- **`group_members` had no self-leave policy** — only `"group creator can
  remove members"` existed, so leaving a group was RLS-impossible, not just
  missing UI. Fixed with an additive `"members can remove themselves"`
  delete policy (multiple permissive policies for the same command are OR'd
  together in Postgres, so this didn't touch the existing one).
- **Dissolving a group silently orphaned other people's history.** The
  `group_id is null` branch of `payments`/`expenses`/`expense_shares`/
  `recurring_payments`'s `SELECT` policies only granted access to
  `created_by` — meaning once a group dissolved, only whoever *recorded*
  each transaction kept visibility into it, not the actual payer/payee/
  share-holders (who may not be the same account). Fixed with additive
  `SELECT` policies extending that branch to any real party to the row.
  Unscoped rows stay update/delete-restricted to `created_by` only
  (deliberately read-only for everyone else once unscoped).
- **Two genuine RLS infinite-recursion bugs (`42P17`), both hit live and
  both fixed the same way — routing the self-referential check through a
  `SECURITY DEFINER` helper function, same technique `is_group_member()`
  already used:**
  1. The new `expense_shares` policy queried `expense_shares` from *within
     its own* `USING` clause (checking "is there another share row for me on
     this expense") — Postgres re-evaluates the same policy on the
     sub-query and recurses infinitely. Fixed with
     `is_unscoped_expense_party()`.
  2. The new `group_members` self-leave policy queried `members` directly,
     and `members`' own policy queries `group_members` directly right back —
     a two-table mutual reference. Postgres's RLS rewriter inlines each
     policy at the table reference it's currently expanding, and a cycle
     back to the relation already being expanded trips the same guard, even
     though naively tracing the call graph suggests it should terminate.
     Fixed with `is_own_member_row()`.
- **`leave_group(p_group_id)`** (plain, not security definer — no
  permission gap, only the guards below): rejects the group's creator (they
  must transfer or dissolve instead — **RLS alone is actually more
  permissive** than this business rule, since `"group creator can remove
  members"` would otherwise let a creator delete their own row too) and
  rejects a nonzero balance in that group (checked against `group_balances`).
- **`transfer_group_ownership(p_group_id, p_new_owner_member_id)`** —
  creator-only, target must be a current, claimed (real-account) member.
  `SECURITY DEFINER`: the plain `"update own groups"` policy has no explicit
  `WITH CHECK`, so Postgres reuses its `USING` clause for the check too,
  which would reject the very act of transferring `created_by` away from
  the caller.
- **Dissolve needed no new function at all** — a plain `DELETE FROM groups`
  already works via the existing policy + FK cascade shape described above.
  The client shows a confirm dialog warning about outstanding balances
  first, but doesn't hard-block on them (unlike `leave_group`) — forcing an
  entire group to fully settle before its creator can walk away is a much
  bigger ask than the one-person case.

App side: `GroupDetailPage`'s `⋮` menu gained Leave/Transfer ownership/
Dissolve (shown per role via `GroupDetailViewModel.IsGroupCreator`/
`HasOtherMembers`), `IGroupsRepository` gained `LeaveAsync`/
`TransferOwnershipAsync`/`DeleteAsync`. Confirmed working end to end
against the live project, including both recursion bugs above (found via
the exact Postgrest error text, `{"code":"42P17",...}`, surfacing through
`BaseViewModel.RunSafeAsync` into `ErrorMessage`) and a non-owner
successfully leaving a group afterward. This is also the first place in the
app using `Shell.Current.DisplayAlert` for a confirm dialog — new to this
codebase (everywhere else uses the plain `ErrorMessage` label or the custom
dropdown-menu pattern), but it's a standard MAUI primitive, not the kind of
package-version-specific API (`CommunityToolkit.Maui`'s `Popup`, `Toast`)
that's bitten this project before.

## Group members page and phantom removal (2026-08-31)

Previously the only way to see who was actually in a group was indirectly,
via Add Expense's participant picker — there was no members list. Added:

- **`Pages/MembersPage.xaml` + `MembersViewModel`** (route `Members`,
  reached from `GroupDetailPage`'s `⋮` menu as "View members"): roster of
  the group's members (avatar, name, "You"/"Phantom member" caption, sorted
  alphabetically), reusing the existing `IMembersRepository
  .GetForGroupAsync` call `GroupDetailViewModel` already made for balances/
  activity.
- **"Invite people" moved here** from `GroupDetailPage` (previously a
  dedicated always-visible button there) — it's a low-frequency action, not
  worth a permanent top-level button competing with "+ Add expense".
- **`remove_group_member(p_group_id, p_member_id)`** RPC, backing a
  "Remove" action shown only on phantom rows: callable by **any current
  group member**, not just the creator — deliberately mirrors `"group
  members can add members"` (already widened past creator-only for the
  Link flow in `JoinGroupPage`; leaving removal creator-only while adding is
  open to everyone would be an odd asymmetry). Rejects removing a claimed
  member (a real account can only ever remove itself, via Leave — same "a
  real account joins by its own action, never someone else's" principle
  documented above for adding members) and rejects a nonzero balance, same
  shape as `leave_group()`. `SECURITY DEFINER` for the same recursion-
  avoidance reason as `leave_group`/`transfer_group_ownership` above, not a
  genuine permission gap.
- **Fixed a duplicated `⋮`**: `PageHeaderBar`'s built-in overflow and a
  separately-added inline "+ Invite people"/"⋮" row had both ended up bound
  to the same group-options-menu toggle, showing two identical "⋮" glyphs on
  screen. Collapsed to a single custom `⋮` drawn directly in
  `GroupDetailPage`'s own header row (not `PageHeaderBar`'s `ShowOverflow`)
  — confirmed clickable in a non-maximized window on Windows, the same
  corner `GroupsPage`'s avatar-menu trigger already uses reliably (the
  original concern about Windows' native caption buttons overlapping a
  header overflow turned out to be specific to `PageHeaderBar`'s own
  positioning, not a fundamental "top-right is dead" limitation).

Confirmed working end to end, including a non-owner account successfully
removing a phantom — validating the "any member" permission choice above.

## Member aliases and the ProfileCircle control (2026-08-31)

A private, per-account nickname override — e.g. seeing "Dave" instead of
"David Kim" — with a reusable avatar control built alongside it in
anticipation of profile photos (see "Avatar photos" below, added the same
day once this landed).

- **`member_aliases(owner_id, member_id, alias)`**, RLS scoped to
  `owner_id = auth.uid()` only (same `for all using/with check` shape as
  `device_tokens`) — fully private, no other account ever sees your
  aliases. Keyed off `member_id`, not an account id, specifically so a
  phantom (no account at all) can be aliased exactly like a claimed member.
- **`Services/MemberDisplay.cs`** is the one place resolving a member's
  displayed `Name`/`Initials`/`AvatarUrl` — alias-if-set-else-DisplayName,
  initials derived from that resolved name, image-if-set-else-null. Every
  screen that used to read `Member.DisplayName` directly (balances, recent
  activity, the members list, the expense split/payer pickers) goes through
  this instead, replacing several near-identical private `Initials(string)`
  helpers that existed per-ViewModel before.
- **`Controls/ProfileCircle`** (mirrors `PageHeaderBar`'s plain-bindable-
  property pattern): `ImageUrl`/`Initials`/`Kind` (Default/Primary/Phantom,
  picks the `AvatarCircle*` style variant internally)/`Diameter`. Image is a
  later sibling than the initials `Label` in the same `Grid` cell, so when
  it's visible it simply paints over the fallback rather than needing a
  second "hide the label" binding.
- **Rename UI**: a pencil icon on `MembersPage` rows opens an inline
  overlay (`Entry` + Save/Cancel in a scrim'd `Border` card, same shape as
  `GroupDetailPage`'s transfer-ownership picker) — **not**
  `Shell.Current.DisplayPromptAsync`, which was tried first and is a known
  WinUI crash on Windows (fail-fast in `Microsoft.UI.Xaml.dll`,
  microsoft/microsoft-ui-xaml#10897 — the exact reason `GroupsViewModel
  .NewGroup` already routes to a dedicated page instead of a prompt, a
  precedent missed when this was first built). `DisplayAlert` (used
  elsewhere in both this ViewModel and `GroupDetailViewModel`) is a
  different ContentDialog configuration and has been confirmed safe
  repeatedly — only the text-input prompt variant is the problem.
- **Real bug hit live**: creating an alias threw `new row violates row-level
  security policy for table "member_aliases"` even though `OwnerId` was set
  correctly in code. Root cause: `MemberAlias.OwnerId`'s `[PrimaryKey]`
  attribute defaulted to `shouldInsert: false` (Postgrest's default for a
  primary key, since PKs are normally auto-generated) — `owner_id` has no
  DB default, so it was silently dropped from the insert payload, landing as
  `null` and failing the `owner_id = auth.uid()` check. Exact same footgun
  already documented on `GroupMember.GroupId`; fixed the same way
  (`shouldInsert: true`).

## Avatar photos (2026-08-31)

Built the same day, once `MemberDisplay`/`ProfileCircle` above existed to
receive it — a profile picture per **claimed** member, deliberately **not**
available to phantoms at all (not just creator-restricted): a picture is
self-presentation, and a phantom has no way to see, object to, or remove
whatever anyone else uploads "for" it.

- **`avatars` Storage bucket, public** (unlike the private `receipts`
  bucket SCOPE.md already plans) — an avatar is low-sensitivity, and public
  means `MemberDisplay.AvatarUrl` stays the plain synchronous string-builder
  it already was, instead of needing signed-URL expiry/refresh plumbing
  each of the many places it's rendered. `client.Storage`'s API shape
  (`.From(bucket).Upload/GetPublicUrl/Remove`) was confirmed against a real
  build via a reflection probe of the installed `Supabase.Storage 2.7.0`
  package (not docs) before writing any of this — see `IAvatarsRepository`.
- **Path is `{member_id}/{new guid}.webp` per upload, never overwritten in
  place** — an overwritten same-path file would leave stale copies in any
  client-side image cache showing the old photo forever; a new path per
  upload gets a genuinely new URL instead. `SetAvatarAsync` uploads the new
  file, points `members.avatar_path` at it, then best-effort deletes
  whatever the previous file was (a leftover orphan if that delete fails is
  harmless, same "cleanup isn't critical" bucket SCOPE.md already put
  receipts in).
- **Phantom exclusion enforced twice**: the storage insert/delete policies
  only match a claimed member's own account (`account_id is null` for every
  phantom, so they're excluded automatically with no extra check), and a
  `members` check constraint (`avatar_path is null or account_id is not
  null`) makes it a real database invariant — needed because `members`'
  existing update policy (`created_by = auth.uid() or account_id =
  auth.uid()`) would otherwise let a phantom's *creator* set `avatar_path`
  directly, bypassing Storage entirely.
- **`Services/ImageResizer.cs`** (SkiaSharp `4.151.1` — a major-version
  jump from the old well-known 2.88.x line, confirmed compatible with
  `net10.0` and WebP encoding confirmed via an actual resize+encode round
  trip before adding it) resizes to `maxDimension` (256px — see below) and
  encodes WebP client-side before every upload, so output size is bounded
  by these settings regardless of the original photo's size; no separate
  upload-size validation needed.
- **Sizing tuned from real uploads**: started at 512px, but the largest an
  avatar ever actually renders in this app is `AvatarSizeL` (44px, see
  `Tokens.xaml`) — even at 3x display density that's ~132px of real pixels,
  so 512 was roughly 4x more resolution than anything would ever show.
  Dropped to 256px once real uploads (7-18KB at 512px) confirmed there was
  no reason to keep that headroom for something rendered this small and
  shown this often (every balance/activity/member row, unlike a receipt
  opened rarely).
- **"Change photo"/"Remove photo"** replace `GroupsPage`'s account-menu
  "Profile" placeholder (previously a disabled no-op — the first real thing
  that menu does beyond language/logout) — `MediaPicker.Default
  .PickPhotoAsync()` (ships with the MAUI SDK, no new package needed) picks
  the photo, `ImageResizer` processes it, `IAvatarsRepository` uploads it.
  New `IMembersRepository.GetMyMemberAsync()` finds the current account's
  one member row with no group context needed (a claimed account has
  exactly one, reused across every group — see "members vs. accounts"
  above).
- **Real bug hit live**: right after uploading, the new photo showed
  correctly everywhere; after a full logout/login, `GroupsPage`'s own
  avatar reverted to initials while `MembersPage` still showed it correctly
  for the same account. Root cause: `GetMyMemberAsync()` filtered by
  `account_id` with no `ORDER BY` before `.FirstOrDefault()` — Postgres
  gives no row-order guarantee at all without one, so if the account ever
  ended up with more than one `members` row (plausible residue from this
  project's own testing churn, not something this feature introduced),
  which row came back could vary across sessions/query plans.
  `GetForGroupAsync` never had this ambiguity, since it scopes by
  `group_members.group_id` to the specific row actually in that group.
  Fixed by ordering `GetMyMemberAsync()` by `created_at` — makes the
  symptom deterministic, but doesn't itself resolve a duplicate row if one
  actually exists; worth checking the live `members` table for that
  specifically if this account's avatar ever looks wrong again.

Confirmed working end to end after these fixes: upload, removal, and
cross-account visibility (a second account viewing the same group's
members list) all verified against the live project.

## Receipt photos (2026-08-31)

Built the same day, once avatars proved out the Storage/resize plumbing —
see SCOPE.md's "Supporting infra" note. Scoped down deliberately from
SCOPE.md's original description: **expenses only** (`payments.receipt_path`
stays reserved-but-unused, same "reserve now, wire up later" treatment
`currency` already got — a settle-up rarely has a receipt the way a bill
does), and the cleanup Edge Function (`pg_cron`, orphan/attached-photo
purge) is **not built yet**, left for later infra work.

- **`receipts` Storage bucket, private** — unlike `avatars`' public bucket,
  a receipt is a financial document, not self-presentation, so it needs
  real access control rather than just an unguessable path. Viewing goes
  through a live signed URL (`IReceiptsRepository.GetSignedUrlAsync`,
  `client.Storage.From(bucket).CreateSignedUrl(path, expiresIn)`) instead
  of `MemberDisplay.AvatarUrl`'s plain deterministic string.
  `CreateSignedUrl`'s shape was reflection-probed against the installed
  `Supabase.Storage 2.7.0` package the same way the avatars work was — and
  the first pass at that probe got it wrong (assumed a
  `CreateSignedUrlResponse` wrapper with a `SignedUrl` property, going by
  a same-named response type that exists in the assembly for a *different*
  method); the actual signature returns `Task<string>` directly. Caught
  immediately by a real `dotnet build` (`CS1061`), not by re-reading
  reflection output more carefully — worth remembering that a reflection
  probe still needs a real compile to confirm, the same "docs vs. real
  build" caution this file already gives NuGet API surfaces generally.
- **Path is `{group_id}/{guid}.webp`, not `{expense_id}/{guid}.webp`** —
  deliberately scoped by group rather than by the specific expense a
  receipt will attach to. A brand-new expense doesn't have an id yet until
  `Save` actually inserts it, so a policy joined against `expenses` would
  reject a photo captured while still filling out Add Expense (the common
  case — a receipt is naturally photographed while entering the expense,
  not after). Scoping storage RLS by `is_group_member(group_id)` directly
  (no join to `expenses`, so no recursion risk either) sidesteps that
  chicken-and-egg. `ReceiptPath` just rides along as a plain field on the
  `Expense` being inserted/updated, same as `Description`/`Category` — no
  separate "attach" step. An upload that's never attached (Add Expense is
  cancelled after a photo was taken) becomes exactly the "orphaned
  receipt" case SCOPE.md's (still unbuilt) cleanup function already
  expects to purge after 3 months, not a new failure mode.
- **`ImageResizer.ToReceiptWebp`** targets SCOPE.md's ~100KB (vs. avatars'
  256px/~7-18KB) — starts at 1280px long edge (a receipt is viewed
  full-screen occasionally, unlike an avatar rendered at 44px everywhere),
  steps quality down through a fixed list, then shrinks dimension and
  retries down to a 480px floor if still over target.
- **Capture UI**: tapping `AddExpensePage`'s existing (previously
  unwired) drop zone opens `Shell.Current.DisplayActionSheet` with Take
  photo / Choose from gallery / Remove photo (last one only offered once a
  receipt exists) — `DisplayActionSheet` is the same class of standard MAUI
  primitive as `DisplayAlert`, not the `DisplayPromptAsync`/`Toast`/`Popup`
  shapes that have bitten this project before. `MediaPicker.Default
  .CapturePhotoAsync()` needed a new `CAMERA` `<uses-permission>` (plus a
  `required="false"` camera `<uses-feature>` so a camera-less device still
  installs) added to `Platforms/Android/AndroidManifest.xml` — confirmed
  merged into the built manifest (`obj/.../android/AndroidManifest.xml`)
  after a real `dotnet build`, but the actual runtime permission-prompt
  flow on a real device is **not yet manually verified** the same way
  avatars' upload path was.
- **Real bug hit live, pre-existing and unrelated to the receipt work
  itself, only surfaced by it**: saving an edited expense (any field, not
  just the receipt) threw `23503 insert or update on table "expenses"
  violates foreign key constraint "expenses_created_by_fkey"`. Root cause:
  `AddExpenseViewModel.Save()` builds a brand-new `Expense` object for both
  add and edit mode, and edit mode never carried `CreatedBy`/`CreatedAt`
  over from the original row — `SupabaseExpensesRepository.UpdateAsync`'s
  `Update(model)` sends the *whole* model, so `CreatedBy` went out as
  `Guid.Empty` (FK violation) and `CreatedAt` would have silently reset to
  `0001-01-01` even without one. Same class of bug as `Token`/`ExpiresAt`
  defaulting silently (see "Invite redemption was completely broken"
  above) — a pattern worth checking anywhere else a fresh model object gets
  reused for an update. Fixed by having `LoadExistingExpense` capture the
  original `CreatedBy`/`CreatedAt` into two ViewModel fields
  (`editingCreatedBy`/`editingCreatedAt`) and `Save()` re-applying them onto
  the update payload before calling `UpdateAsync`. Editing an expense had
  apparently never actually been save-tested before this session — the edit
  *screen* worked (loads existing data correctly), just never a real save.

**Camera capture confirmed working on a real device (2026-09-04)** —
`CapturePhotoAsync` needed no fixes beyond what shipped here. Not yet done
for this feature specifically: the cleanup Edge Function described above
(see "Receipt cleanup" below — that part is done as of 2026-08-31, this
note predates it).

## Recurring expenses, and retiring recurring_payments (2026-08-31)

Built the same day, prompted by wanting real cron-driven infra next and
realizing the only recurring-template table that existed (`recurring_payments`,
pairwise payer/payee) didn't match what was actually wanted: a periodically
repeating **split** bill (e.g. "Crunchyroll $60/month between me + 3 others"),
not a pairwise transfer. Design discussion surfaced that this isn't a
coincidence — a `Payment(payer, payee, amount)` has **identical balance
math** to `Expense(paid_by=payer, participants=[payee], share=amount)`
(both move the fronting/paying party's balance up by the amount and the
other party's down by the same amount), so a 1-way recurring expense fully
subsumes what `recurring_payments` was for. Since `recurring_payments` had
been built schema-first with **no UI ever written against it** (confirmed
via a repo-wide grep — the only non-declaration references were two
doc-comment sentences), it was retired outright rather than kept alongside
the new table.

- **`recurring_expenses` / `recurring_expense_shares`** (schema.sql) mirror
  `expenses`/`expense_shares` exactly — same 4+4 RLS policy shape via
  `is_group_member(group_id)` — plus the schedule columns
  `frequency`/`start_date`/`last_processed_date`/`is_active` that
  `recurring_payments` already proved out. Deliberately **no**
  `is_unscoped_expense_party()`-style visibility widening for a dissolved
  group's templates (unlike `expenses`) — a template surviving group
  dissolution just becomes creator-only-visible, since nothing materializes
  from an unscoped template anyway. Flagged as a possible future gap, not a
  blocker.
- **Editing a template only affects future materializations** — this falls
  out for free from the template/instance split, no extra design needed:
  the (not-yet-built, see below) `pg_cron` job would insert independent
  `expenses`/`expense_shares` snapshots each run, so a later edit to
  `recurring_expenses.amount` never touches rows already materialized.
  `SupabaseRecurringExpensesRepository.UpdateAsync`/`AddExpenseViewModel`
  are careful to never let `LastProcessedDate` get touched by an unrelated
  field edit (carried through via `editingLastProcessedDate`, same pattern
  already used for `CreatedBy`/`CreatedAt` on one-off `Expense` edits) and
  to never silently reactivate a paused template on an unrelated save
  (`editingRecurringIsActive`).
- **`AddExpensePage`/`AddExpenseViewModel` extended in place, not a new
  page** — roughly 90% of the form (participant split, payer picker,
  category chips, description, amount) is identical for a recurring
  template, and the page already branched cleanly on add-vs-edit via query
  params, so a third "recurring" mode was a natural extension rather than
  new architecture. Four entry shapes into the same page: plain add,
  `?expenseId=` edit (unchanged), `?recurring=true` (fresh recurring add),
  `?recurringExpenseId=` (edit an existing template). A "Repeat" toggle is
  only shown in pure-add mode (`CanToggleRecurring`) — converting an
  existing one-off `Expense` into a template, or vice versa, isn't
  supported, an intentional simplification rather than a gap. Recurring
  mode swaps the "Occurred on" date row for "Start date" + a Frequency chip
  row (reusing `RecurringFrequency`, an enum that already existed in the
  codebase completely unused until now), and hides the receipt drop-zone
  entirely — a receipt belongs to one materialized instance, not an
  indefinitely-repeating template.
- **`Pages/RecurringExpensesPage.xaml` + `RecurringExpensesViewModel`**
  (route `RecurringExpenses`, reached from `GroupDetailPage`'s `⋮` menu as
  "Repeating expenses", same pattern `MembersPage` already established):
  lists each group's templates (description, payer, frequency, a
  client-computed "next due" estimate — purely informational, since nothing
  consumes it yet), with inline pause/resume (`SetActiveAsync`), edit
  (navigates back into `AddExpensePage` via `?recurringExpenseId=`), and
  delete (`DisplayAlert` confirm, same shape `GroupDetailViewModel.LeaveGroup`/
  `DissolveGroup` already use).
- **Retired**: `Models/RecurringPayment.cs`, `Services/IRecurringPaymentsRepository.cs`,
  `Services/SupabaseRecurringPaymentsRepository.cs` deleted outright; the
  `recurring_payments` table, its RLS policies, and its unscoped-visibility
  widening policy removed from `schema.sql`. The live Supabase table was
  confirmed empty and dropped (`drop table if exists
  public.recurring_payments cascade;`) the same day, after the additive
  `recurring_expenses`/`recurring_expense_shares` migration was applied.

## pg_cron materialization for recurring expenses (2026-08-31)

Built the same day as recurring expenses themselves, once the design
questions flagged when that feature landed (catch-up semantics, run time,
month-overflow behavior) were actually settled. `public
.materialize_recurring_expenses()` (schema.sql) scans `recurring_expenses`
for active, due templates and inserts real `expenses`/`expense_shares` rows
for every missed occurrence since `last_processed_date` — not just the
next one — capped at **24 occurrences per template per run** so a
long-stale template can't flood a group's ledger in one go. The very first
occurrence for a brand-new template is always exactly `start_date`
regardless of frequency (an early draft of this function stepped "anchor +
one period" from a null `last_processed_date`, which would have landed a
weekly template's first occurrence 6 days late — caught before it ran
against real data).

- **Runs daily at 8am UTC** (`cron.schedule('materialize-recurring-expenses',
  '0 8 * * *', ...)`) — deliberately not middle-of-the-night, so a future
  push notification on a newly-materialized expense doesn't wake anyone up
  at 3am. Daily is already fine-grained enough for every supported
  frequency (`daily`/`weekly`/`monthly`/`yearly`); the job just checks
  "is anything due yet," it doesn't need to run more often than that.
- **Monthly/yearly stepping does not clamp to end-of-month** — Postgres's
  native `date + interval '1 month'` overflows rather than clamping
  (`date '2026-01-31' + interval '1 month'` = `2026-03-03`, not
  `2026-02-28`), so a template anchored on day 29-31 will drift forward a
  few days whenever it crosses a shorter month and never land back on day
  31. Deliberately left unfixed — building real end-of-month clamping
  (`LEAST(...)` against the month's last day) is real complexity for an
  edge case that's just as easy to correct by editing the materialized
  expense afterward, same as any other expense field.
- **Never `security definer`** — pg_cron runs a scheduled job as whichever
  role called `cron.schedule()` (the SQL editor's role, effectively
  `postgres`), which already bypasses RLS entirely, so there's no real
  permission gap to elevate here unlike `leave_group()`/
  `transfer_group_ownership()`/etc. What *does* matter, and is easy to miss:
  every function in `public` is reachable via PostgREST's `/rpc/` by any
  authenticated user by default, and this one has zero caller-scoping (it
  processes every group's templates, not just the caller's) — fixed with an
  explicit `revoke execute ... from public, anon, authenticated;` right
  after creating it. Confirmed via `information_schema.routine_privileges`
  that only `postgres`/`service_role` can execute it, not `anon`/
  `authenticated`.
- **`pg_cron` extension**: was not enabled on the project (Free plan, Nano
  compute — confirmed via the dashboard that pg_cron doesn't require an
  upgrade). Enabled via Database → Extensions (must install into the
  `pg_catalog` schema, the only option offered).
- **Confirmed working end to end against the live project**: manually
  invoked once after creating the job (rather than waiting for the next
  8am UTC run) — a real overdue "Crunchy" template (yearly, started
  2026-07-28, never processed) materialized into a real `expenses` row
  (`$60.00`, `occurred_at = 2026-07-28`) with all 5 `expense_shares` rows,
  and `last_processed_date` advanced to `2026-07-28` correctly (only one
  occurrence, since the next yearly occurrence isn't due until 2027-07-28).

## Receipt cleanup (2026-08-31)

Built the same day as the recurring-expense cron, closing out the last
piece SCOPE.md's "Supporting infra" described. Unlike
`materialize_recurring_expenses()`, this one genuinely needs a Supabase
**Edge Function**, not a pure SQL function — a plain `DELETE FROM
storage.objects` only removes the metadata row, not the actual stored
file, so real deletion has to go through the Storage API
(`.storage.from(bucket).remove(...)`), which only runs in application
code, not plain Postgres.

- **`public.find_expired_receipts()`** (schema.sql) is the read-only half —
  pure SQL, no deletion, just answers "what qualifies" as a table of
  `(path, kind, expense_id)`. Two categories, both measured off
  `storage.objects.created_at` (the file's own upload time — neither
  `expenses` nor `recurring_expenses` stores a separate "receipt attached
  at" timestamp): `'orphan'` (no `expenses.receipt_path` points at it,
  uploaded >3 months ago) and `'attached'` (an expense's `receipt_path`
  does point at it, uploaded >6 months ago — the expense survives, only
  its `receipt_path` gets nulled and the file deleted). Same
  revoke-from-anon/authenticated treatment as every other cron-only
  function in this file.
- **`supabase/functions/cleanup-receipts/index.ts`** (Deno/TypeScript,
  version-controlled in-repo) is the Edge Function that does the actual
  work: calls `find_expired_receipts()` via `.rpc()`, deletes every
  candidate path in one `.storage.from('receipts').remove(...)` call, then
  nulls `receipt_path` on the attached-but-old expenses — deliberately
  *after* the delete succeeds, so an expense never ends up pointing at a
  file that's already gone, or keeps a dangling reference because the
  update step was skipped. `SUPABASE_URL`/`SUPABASE_SERVICE_ROLE_KEY` come
  from the environment every Edge Function gets automatically — no extra
  secret needed for this client. **Deployed via the Supabase dashboard's
  browser code editor** ("Via Editor"), not the CLI — no `supabase/
  config.toml` or CLI project link exists in this repo, and the dashboard
  editor needed no local auth setup at all. If the deployed function is
  ever hand-edited in the dashboard, this file is the one that needs
  updating to match, not the other way around.
- **`pg_net` extension** enabled the same way `pg_cron` was (Database ->
  Extensions, into `pg_catalog`) — needed because a `pg_cron` job can only
  run SQL, and calling the deployed Edge Function's URL from SQL is
  exactly what `pg_net`'s `net.http_post` is for.
- **The service-role key never appears in this repo.** It's the
  `Authorization: Bearer` credential the cron job's HTTP call needs to pass
  the function's default JWT verification (confirmed on by default at
  deploy time — "Verify JWT with legacy secret" — and the legacy
  `service_role` key, being a real JWT signed with the project's JWT
  secret, satisfies it). Stored in Supabase Vault instead (Integrations ->
  Vault -> Secrets, name `service_role_key`) via the dashboard's own "Add
  new secret" UI — copied from Settings -> API Keys -> Legacy using its
  **Copy button** and pasted straight into Vault's field, so the raw key
  value never had to be typed or transcribed anywhere along the way. The
  cron job's SQL (schema.sql) reads it at *call* time via `select
  decrypted_secret from vault.decrypted_secrets where name =
  'service_role_key'` — never hardcoded.
- **Weekly, Sunday 4am UTC** — same off-peak reasoning as the recurring-
  expense job's 8am choice, just weekly since a 3/6-month retention window
  doesn't need daily attention.
- **Confirmed working end to end against the live project**: manually ran
  the exact `net.http_post` call the cron job will run, checked
  `net._http_response` for the result — `200`, body
  `{"orphans_deleted":0,"attached_photos_purged":0,"files_removed":0}`.
  Zero is the *correct* answer here (the only receipt in the system at the
  time was uploaded that same day, nowhere near either threshold) — this
  confirms the full chain works (pg_net → Vault secret → Authorization
  header → Edge Function → JWT verification → the RPC call), not that
  nothing happened.
- **One thing worth double-checking later, not verified this session**:
  whether `.storage.from(bucket).remove(paths)` on an empty `paths` array
  is safe to skip (the function already guards this with an `if (allPaths
  .length > 0)` check, so it's moot in practice) — flagged only because it
  wasn't exercised by this session's test run (zero candidates existed),
  so the actual deletion path is unverified against real data. Worth a
  real test once a receipt legitimately ages past 3 months.

## Localization — English/Spanish (2026-08-28)

`AxisApp/Localization/` (`AppStrings.cs`, `LocalizationResourceManager.cs`,
`TranslateExtension.cs`) — every user-facing string in the app has an en/es
pair, switchable live without restarting. `LocalizationResourceManager` is a
singleton exposing a plain, ordinarily-notified `CurrentLanguage` property;
`TranslateExtension` (`{loc:Translate Key}` in XAML) binds to it through a
converter rather than to the manager's `this[string]` indexer directly —
the indexer approach (`PropertyChanged("Item[]")`, the WPF convention for
refreshing indexer bindings) was tried first and confirmed via a real
Windows build **not** to work in MAUI: newly-navigated pages picked up a
language change, already-open ones never refreshed. Binding to an ordinary
property uses the same mechanism every other binding in the app already
relies on.

`SetLanguage(overrideLanguage)` takes `null`/`""` to mean "follow the
device's OS language" (resolved once at first access, via
`CultureInfo.CurrentUICulture`, into a `DeviceLanguage` captured before
`Bootstrap` ever overrides it) or an explicit `"en"`/`"es"` override,
persisted per-device via `Preferences` (`AppConstants.Preferences
.LanguageOverride`) and reapplied by `Bootstrap()` before the first page
renders. `Format(key, args)` always formats numeric/date placeholders with
`InvariantCulture`, not the active UI culture — same reasoning as
`AmountText`/`OwesText` elsewhere: a `decimal.TryParse` round-trip
downstream must never see `"1,50"` just because the UI is in Spanish.
`AppConstants.Categories` predates this and was already designed around it
(a stable, language-independent key, resolved to a label per-viewer at
render time, never stored as text — see its own remarks).

The language picker itself now lives on `ProfilePage` (see below) — it
originally lived directly in `GroupsPage`'s account-menu dropdown, moved
when Profile was split out.

## Profile page (2026-08-31)

Previously "Profile" was a disabled placeholder in `GroupsPage`'s
account-menu dropdown, and everything else account-related (avatar
change/remove, language) lived directly in that same dropdown alongside
email/logout. `Pages/ProfilePage.xaml` + `ProfileViewModel` (route
`Profile`, reached via `GroupsViewModel.OpenProfileCommand`) consolidates
all of it onto its own page — the dropdown is trimmed back down to just
email, a "Profile" link, and "Log out". Adds:

- **Own display name + birthday**, backed by a new `members.birth_date`
  column (nullable, no RLS change needed — the existing "update members you
  created or claim yourself" policy already covers a claimed member editing
  their own row). `SaveProfile` mutates the already-loaded `myMember` in
  place before calling `IMembersRepository.UpdateAsync` rather than
  building a fresh `Member`, avoiding the same "fresh object silently
  drops `CreatedBy`/`CreatedAt`" footgun documented for `Expense` edits
  above. Nothing yet *uses* `birth_date` beyond storing/displaying it — a
  reserved field, same treatment `currency` and `payments.receipt_path`
  already got.
- **Avatar change/remove** — moved verbatim from the old dropdown
  (`IAvatarsRepository.SetAvatarAsync`/`RemoveAvatarAsync`, unchanged).
- **Language picker** — moved verbatim from the old dropdown (see
  "Localization" above).
- **Email + password change**, the first thing in the app calling
  `IAuthService.UpdateEmailAsync`/`UpdatePasswordAsync`
  (`client.Auth.Update(UserAttributes)`, confirmed against a reflection
  probe of the installed `Supabase.Gotrue` 6.3.0 package — same "reflection,
  not docs" caution as everywhere else in this file). Changing email sends
  a confirmation link to the new address and only takes effect once it's
  clicked; `IAuthService.CurrentEmail` keeps showing the old address until
  then, which is correct behavior, not a bug to chase.

`IMembersRepository` gained `UpdateAsync` (saving a member's own row) to
support this.

## Google sign-in, password reset, and avatar backfill (2026-09-01 – 09-02)

- **Google sign-in** (`IGoogleAuthService`, one implementation per platform
  under `Platforms/{Android,Windows}/GoogleAuthService.cs` — the
  `Platforms/<X>` folder convention means only the matching TargetFramework
  compiles each one, so `MauiProgram` registers `IGoogleAuthService,
  GoogleAuthService` with no platform branching needed) reaches a signed-in
  Supabase session by genuinely different means per platform:
  - **Android**: native Jetpack Credential Manager account picker (ported
    from the sibling `PokeCards` project, same `Xamarin.AndroidX
    .Credentials`/`Xamarin.AndroidX.Credentials.PlayServicesAuth`/
    `Xamarin.Google.Android.Libraries.Identity.GoogleId` packages), handing
    a native ID token to `client.Auth.SignInWithIdToken(Provider.Google,
    idToken)`. Needs `SupabaseConfig.GoogleWebClientId` (the **Web** OAuth
    client id from Supabase's Google provider settings, not a separate
    Android one — Credential Manager wants a client id Supabase's backend
    can verify) plus a registered SHA-1 signing-key fingerprint, or every
    request is rejected before any UI shows. Required bumping Android's
    `SupportedOSPlatformVersion` from 21.0 to 23.0 in the `.csproj`.
    **Real bug hit on a real device (MIUI)**: Play Services' own account-
    picker process can crash during init before ever showing UI or invoking
    either callback (confirmed via logcat), which hung the awaited
    `TaskCompletionSource` forever with no exception — the "stuck on the
    spinner" symptom. Fixed with a 2-minute timeout (`fdac580`,
    2026-09-02) racing the callback via `Task.WhenAny`.
  - **Windows**: no Credential Manager equivalent and no working deep-link
    path back into this unpackaged Win32 app, so this can't mirror Android.
    Originally built on `client.Auth.SignIn(Provider, SignInOptions)` with
    PKCE, gotrue-csharp's own documented native-app pattern — but hit a
    reproducible `bad_oauth_state` rejection every time (root-caused via
    Supabase's own Auth Logs: state rejected ~6-9s into a clean
    `/authorize` → `/callback` round trip, ruling out expiry; a manual
    browser test of the identical round trip with no PKCE/explicit state
    completed cleanly), matching an open upstream issue
    (`supabase-community/supabase-csharp#222`) about that SDK's PKCE state
    handling. Worked around by hand-building the `/authorize` URL and using
    the plain implicit flow instead — which returns the token in the URL
    **fragment**, never sent to a server, so a small local `HttpListener`
    (`http://localhost:48291/`, deliberately `localhost` not `127.0.0.1` —
    Supabase's ecosystem has documented sensitivity to that exact
    distinction) serves a page whose JS reads `location.hash` and
    re-submits it as a real query string to a second local request, which
    the listener can actually read and hand to `client.Auth.SetSession
    (accessToken, refreshToken)`.
  - Both implementations return `AuthResult(false, null)` — a **null**, not
    empty, `ErrorMessage` — for a user-cancelled sign-in (dismissed picker /
    closed tab), a signal `LoginViewModel.LoginWithGoogle` uses to stay
    silent instead of showing a "sign-in failed" message; every genuine
    failure elsewhere carries a real message.
  - **Google-branding-compliant button** on `LoginPage` (official
    colors/logo, `Resources/Fonts/GoogleSansMedium.ttf` registered in
    `MauiProgram` as font alias `GoogleSansMedium`) — required by Google's
    sign-in-button branding guidelines for app verification, not a design
    choice.
- **Forgot-password**: `LoginPage` reuses its existing Email field (no
  separate prompt — `Shell.Current.DisplayPromptAsync` is the same known
  WinUI fail-fast crash already avoided elsewhere, see `MembersPage`'s
  rename overlay) plus `IAuthService.ForgotPasswordAsync` →
  `client.Auth.ResetPasswordForEmail(...)` with `RedirectTo =
  AppConstants.Links.PasswordResetUrl`. The reset itself completes on a new
  standalone page, `web/reset/index.html`, using the Supabase JS SDK
  directly in the browser — deliberately **not** a deep link back into the
  app, since Windows has no deep-link support at all and this has to work
  in whatever browser opens the link regardless of platform. Deliberately
  left on the SDK's default **Implicit** flow rather than PKCE: PKCE's
  `code_verifier` is generated and stored on the client that started the
  flow, but the recovery link is clicked in a completely different
  browser/client that never had it.
- **New `web/privacy/` and `web/termsofservice/` pages**, plus
  `web/email-templates/{reset-password,change-email}.html` and
  `web/email-assets/icon.svg` — Axis-branded HTML email templates (for
  Supabase's auth emails) and the legal pages Google's app-verification
  process requires a public privacy policy/ToS URL for.
- **Avatar backfill from Google**: `IAuthService.ProviderAvatarUrl` reads
  `client.Auth.CurrentUser.UserMetadata["avatar_url"]` (falling back to
  `"picture"` — Google populates both with the same value; `UserMetadata`
  is a plain `Dictionary<string, object>`, confirmed via reflection against
  the installed `Supabase.Gotrue` 6.3.0 package, not a typed property) and
  returns `null` for a plain email/password account.
  `ProfileViewModel.LoadAsync` calls this once per Profile visit
  (`BackfillGoogleAvatarAsync`) and only ever *fills in* a photo when the
  member doesn't already have one (`AvatarPath is null`) — it never
  overwrites a deliberately-removed photo, since it re-checks fresh on
  every visit rather than tracking a one-time "already tried" flag. Reuses
  the same `ImageResizer`/`IAvatarsRepository` upload path `ChangePhoto`
  already used.

## Per-device accent color picker (2026-09-01)

**Directly supersedes SCOPE.md's old "Theming: discussed, not planned"
section** — the "presets first" recommendation written there got built.
Profile's new "Accent color" row (`AccentSwatches`,
`ChangeAccentColorCommand`) lets each device pick from **8 fixed presets**
(`AccentPreset`: Blue/Green/Red/Purple/Pink/Amber/Orange/Navy — see
`Services/AccentPalettes.cs`), independent of dark-theme-vs-light (the app
is dark-only regardless) and independent per group member (a per-device
`Preferences` value, `AppConstants.Preferences.AccentPreset`, same "personal
viewing preference, not group state" treatment as
`BalanceDisplayModePrefix`/`LanguageOverride`).

- **Scoped to ~35 accent-*derived* keys, not the full ~111-key
  `Colors.xaml`** — backgrounds/surfaces/status colors (Success/Warning/
  Danger/Info) stay fixed for every preset; only Primary/Secondary and
  everything visually derived from them (button states, switches,
  checkboxes, focus rings, sliders, nav/tab indicators, badges) change.
  Values are **precomputed per preset**, not derived at runtime (HSL
  lighten/darken off each base hue for Hover/Pressed, on-accent text picked
  per-color by WCAG contrast against the app's dark/light extremes,
  Secondary as a fixed hue rotation off Primary — the same relationship the
  original hardcoded blue/amber pair already had) — `ThemeService` itself
  has no color-math dependency, just a lookup plus in-place dictionary
  writes.
- **`ThemeService.Instance`**, a plain singleton (not DI-registered — reads
  directly from `Preferences` and mutates a `ResourceDictionary` merged
  once into `Application.Current.Resources` at `Bootstrap()`, called from
  `App.xaml.cs` after `InitializeComponent`, since `MergedDictionaries`
  doesn't exist before that). `SetPreset` **mutates the existing
  dictionary's keys in place** rather than removing and re-adding a whole
  dictionary object — tried remove-then-add first (the pattern MAUI's own
  light/dark theming samples use) and confirmed via a real Windows
  build/run that it **under-updates**: an idle-state `Button
  .BackgroundColor` set via a bare style-level `Setter` never picked up the
  new color until a hover/press VisualState transition forced it to
  re-apply, even though `DynamicResource` resolution itself was clearly
  working. In-place key mutation raises a per-key resource-changed
  notification instead of a coarse "a dictionary changed" one, which native
  WinUI button chrome actually listens to. One further gap even with that
  fix: an **already-rendered** WinUI `Button`'s native background brush
  still doesn't repaint itself from the now-correct `DynamicResource` value
  without a push — `RefreshVisibleButtons()` walks the current page's
  visual tree and calls `Handler?.UpdateValue(nameof(Button
  .BackgroundColor))` on every `Button` found, deliberately scoped to only
  the page currently on screen (every other page already reads the correct
  value the next time it's navigated to).
- Every color consumed from `Styles.xaml`/pages had to move from
  `{StaticResource ...}` (resolved once at load) to `{DynamicResource
  ...}` for the ~35 accent keys specifically — the bulk of this commit's
  `Styles.xaml`/page-XAML diff.
- **Not built, and explicitly out of scope for this pass**: a fully
  custom user-picked color (SCOPE.md's harder tier — deriving
  hover/pressed/disabled siblings and a contrast guard programmatically,
  plus a color-picker UI MAUI has no built-in control for). 8 curated
  presets fully satisfies what SCOPE.md called the "easy" tier; the
  "custom palette as a follow-on" recommendation there still stands as the
  next step if ever picked up.

## Android Google sign-in timeout, and retrying transient clock-skew errors (2026-09-02)

Two independent hardening fixes, bundled in one commit:

- The MIUI account-picker-crash timeout described under "Google sign-in"
  above (`GoogleAuthService.SignInTimeout`, 2 minutes, same duration as
  Windows' loopback-listener timeout).
- **`BaseViewModel.RunSafeAsync` now retries once on a transient Supabase
  clock-skew rejection** (`PGRST303`/"JWT issued at future" — a brief
  device/server clock drift at the exact moment a token was issued, not
  something the app or user can fix). PostgREST rejects this at the
  auth-check step before the query itself ever runs, so a bare retry after
  a 750ms delay is safe — nothing partially executed. A second consecutive
  failure is no longer treated as "just a blip" and surfaces normally
  instead of retrying again. This is the same crash class `RunSafeAsync`
  was originally built to contain (see "Session persistence, sign-out,
  Settle, and crash-safety" above) — this commit specifically targeted the
  clock-skew case that kept recurring, per that section's own note that it
  was "confirmed repeatedly this session."

## Push notifications — client-side registration (2026-09-03)

First real code against the last unbuilt piece of Phase 1's infra list
(`SCOPE.md`). Decided **against** the OneSignal wrapper `SCOPE.md`
originally named: this app is an unpackaged Win32 build on Windows, and
native Windows notification APIs already required MSIX packaging once
before on this exact codebase (`AppNotificationManager`, see the
crash-safety notes above) — OneSignal's actual Windows/WNS support for an
unpackaged MAUI app was an unverified assumption, not worth building
against. Went with **raw Firebase Cloud Messaging, Android-only** instead —
Android's delivery is FCM either way, so skipping OneSignal removes a
vendor dependency without losing anything Android-side; Windows push is a
separate, not-yet-started investigation.

- **Firebase project** (`axisapp-ee018`) created, `google-services.json`
  added under `Platforms/Android/` and wired via a `<GoogleServicesJson>`
  item in the `.csproj` (dropping the file in the folder alone doesn't
  register it — confirmed against Microsoft's own MAUI docs, not guessed).
  `Xamarin.Firebase.Messaging` (125.1.1.1) added Android-only. Hit a real
  transitive-dependency conflict getting there: Firebase pulls in
  `Xamarin.AndroidX.Fragment` 1.9.0, but Credential Manager's own
  `Fragment.Ktx` dependency (pinned to the 1.8.8.1 series) expected an
  older `Fragment` — the mismatch merged two different-generation copies of
  the same AAR and duplicated `FragmentKt.class` at the R8/dex step
  (`Compilación correcta` only after explicitly pinning `Fragment.Ktx` up
  to `1.9.0` to match). Xamarin as a product line is EOL — the binding
  still installs and works (confirmed, see below), same as Google
  sign-in's `Xamarin.AndroidX.Credentials` already proved for this
  codebase, but treat its exact API surface as unverified-until-built, same
  rule as every other SDK here.
- **`IPushRegistrationService`** (`RegisterAsync`/`UnregisterAsync`), same
  one-interface-per-platform shape as `IGoogleAuthService`.
  **Android** (`Platforms/Android/PushRegistrationService.cs`): requests
  `Permissions.PostNotifications` (MAUI's built-in Android 13+ permission
  class — no custom permission type needed in .NET 9/10), then retrieves an
  FCM token via the raw `Firebase.Messaging.FirebaseMessaging` binding.
  `GetToken()`/`DeleteToken()` return a Java `Android.Gms.Tasks.Task`, not a
  C# `Task` — wrapped in a `TaskCompletionSource` via
  `AddOnSuccessListener`/`AddOnFailureListener`, the exact same shape
  `GoogleAuthService`'s `ICredentialManagerCallback` wrapping already uses
  for the identical "Java callback API" problem. Both methods swallow their
  own exceptions internally — a push-registration hiccup (permission
  denied, no network) should never block sign-in, Groups loading, or
  sign-out, so callers invoke them with no try/catch of their own.
  **Windows** (`Platforms/Windows/PushRegistrationService.cs`): deliberate
  no-op, per the Windows-deferred decision above.
- **`SupabaseDeviceTokensRepository.RegisterAsync` fixed to be idempotent**
  — it previously did a plain `Insert`, but `push_token` carries a real
  unique constraint and this is called on every Groups-page load (see
  below), not just first sign-in; a repeat call with the same token would
  have thrown. Fixed by deleting any existing row for that exact token
  first, rather than guessing at Postgrest's `Upsert`/`OnConflict` API
  surface — zero new unverified API risk, reuses only the `.Filter(...)
  .Delete()` shape `UnregisterAsync` already had.
- **`AndroidManifest.xml`** gained `POST_NOTIFICATIONS` — required as an
  actual manifest entry before `Permissions.RequestAsync` will even
  attempt the runtime prompt on Android 13+, same requirement `CAMERA`
  already needed for receipt photos.
- **Wiring**: `GroupsViewModel.LoadAsync` fires `RegisterAsync()`
  fire-and-forget (not awaited — it may show a permission prompt, and this
  page's own `IsBusy` spinner shouldn't sit waiting on the user answering
  it) at the end of every load. `LoadAsync` runs on every path that lands
  on Groups — sign-in, sign-up, Google sign-in, and a session restored on
  relaunch — so this one choke point covers all of them without duplicating
  the call into `LoginViewModel`/`SplashPage`. `GroupsViewModel.Logout`
  awaits `UnregisterAsync()` **before** `SignOutAsync()` — it has to run
  while the session that authorizes deleting this device's own
  `device_tokens` row is still valid, or a device later reused by a second
  account would keep receiving the first account's notifications.
- **Confirmed working end to end against a real emulator run**: signed in,
  `POST_NOTIFICATIONS` granted, a row appeared in `device_tokens` with the
  correct `account_id`/`platform`, and a test campaign sent from the
  Firebase console arrived and displayed on the emulator. Deploying to a
  device from the CLI needs `dotnet build -f net10.0-android -t:Run` (not
  a plain `adb install` of the Debug APK — that installs a "Fast
  Deployment" stub whose assemblies get synced separately, and aborts with
  `No assemblies found ... Assuming this is part of Fast Deployment` if
  installed directly); with more than one device attached, pin the target
  explicitly with `-p:AdbTarget="-s <serial>"` — an unpinned run silently
  picked whichever device the tooling defaulted to once, not necessarily
  the intended one.
- **One known gap, not yet built**: no `FirebaseMessagingService` for
  custom foreground handling or tap-to-open deep linking (the test
  notification displayed using Firebase's own default fallback channel —
  logcat warned `Missing Default Notification Channel metadata`, worth a
  real "Axis" channel later). The server-side send half described as
  missing here is now done — see below.

## Push notifications — server-side send (2026-09-03)

The other half of the same day's work, once client-side registration
proved out. Scoped to the actual parties to each transaction, not the
whole group, per real design pushback during this session: a naive
"everyone in the group" send, or a "balance ≠ 0" heuristic, are both
wrong — the former spams uninvolved members of a large group, the latter
reflects the group's overall net position, not who's actually in *this*
expense.

- **`expense_notification_recipients(expense_id)` /
  `payment_notification_recipients(payment_id)`** (`schema.sql`) — plain
  SQL, mirroring `find_expired_receipts`'s "pure read, no side effects"
  role. For an expense: `paid_by_member_id` unioned with every
  `expense_shares.member_id` on it. For a payment: the two parties. Both
  exclude whoever recorded the row (`created_by` — no one needs telling
  about their own action) and join to `device_tokens` via `members
  .account_id`, which naturally excludes phantom members (no account, no
  token) with no special-casing needed. Verified directly in the SQL
  editor against a real expense before trusting the trigger, same
  discipline `find_expired_receipts`/`materialize_recurring_expenses` got.
- **`notify_new_expense()`/`notify_new_payment()`** — `AFTER INSERT`
  triggers on `expenses`/`payments`, calling the new `send-push` Edge
  Function via `net.http_post`, same Vault-`service_role_key`-as-bearer
  pattern `cleanup-receipts`' cron job already used. `pg_net` queues the
  call asynchronously, so this never blocks or can fail the actual
  insert. **Real bug, hit live on the first save after deploying**:
  `permission denied for schema vault` (`42501`). Root cause: unlike
  `cleanup-receipts`'s cron call (which runs as `postgres`, the role that
  called `cron.schedule()`, which already has Vault access),
  these triggers fire on a plain app-level `INSERT` from an ordinary
  signed-in user via Postgrest — the trigger function ran as
  `authenticated` by default, which has no grant on the `vault` schema at
  all. Fixed with `security definer` — a genuine permission gap this
  time, not the usual RLS-recursion-avoidance reason every other
  `SECURITY DEFINER` function in this file exists for. Safe the same way
  those are: the caller never sees the decrypted secret, only the
  function body does.
- **`supabase/functions/send-push/index.ts`** — reads the recipient
  function, looks up the expense/payment's description/amount/group
  name/payer name via embedded Postgrest selects, exchanges the Firebase
  service-account credential for an FCM OAuth2 access token (RS256 JWT
  hand-signed via Deno's native Web Crypto API — no external auth library
  dependency needed for just that), then POSTs to FCM's HTTP v1 API once
  per Android recipient token. **Correction to earlier guidance in this
  same session**: the service-account JSON belongs in this function's own
  **Edge Function Secrets** (`FIREBASE_SERVICE_ACCOUNT_KEY`, read via
  `Deno.env.get`), not Supabase Vault — Vault is for secrets a *SQL*
  caller needs (like `service_role_key` above, read by the trigger), this
  one is only ever read inside the Deno runtime itself.
- **Confirmed working end to end against the live project, including the
  real trigger firing on its own** (not just a manual invoke): saved a
  real expense in the Windows build (`AxisApp.exe` run directly, not via
  `dotnet build`), the trigger fired, `send-push` ran, and the correctly
  scoped recipient (one of 4 real accounts on the expense, minus the
  actor) received a real push instantly — title = group name, body =
  `"{payer name} added {description} — {amount} {currency}"`. Tapping the
  notification currently just opens the app to whatever Shell's default
  route is (Groups), not the specific group — the same
  `FirebaseMessagingService`/tap-to-open gap noted above, unbuilt.

## Push notifications — tap-to-open, a real channel, and a real cold-start bug (2026-09-03)

Closes the two gaps the sections above left open. Discussed with the user
first (see the design-pushback exchange in that session — a per-group
channel was considered and rejected once server-side recipient dedup made
it unnecessary; see `SCOPE.md`'s Phase 2 notification-design notes) before
any of this was written.

- **`send-push` now sends a data-only FCM message** (no `notification`
  block) — deliberately, since a `notification` block makes Android
  auto-display it via Firebase's own default handling, with no way to
  control the tap action or which channel it lands in. `data` now carries
  `group_id`/`group_name` alongside the existing `type`/`expense_id`/
  `payment_id`/`title`/`body` (the expense/payment selects gained
  `group_id`) — everything the client needs to build its own notification
  and route a tap.
- **`Platforms/Android/AxisFirebaseMessagingService.cs`** (new) — receives
  every message, creates a real `axis_default` channel ("Axis
  notifications") via `NotificationChannel`/`NotificationManager`
  (idempotent — Android dedupes by channel id, so this runs on every
  message rather than once at startup), and builds the shown notification
  by hand with a `PendingIntent` back to `MainActivity` carrying
  `group_id`/`group_name` as Intent extras.
- **`MainActivity.HandleIntent`** now also reads those extras (alongside
  the existing App Link URI check) and calls the new
  `App.HandleNotificationTap`. **`App.xaml.cs`**'s old invite-link-only
  `pendingDeepLink` queue was generalized into `pendingRoute`, shared by
  both `HandleDeepLink` (invite links) and `HandleNotificationTap` (push
  taps) — both resolve to an already-built route string before queuing,
  rather than duplicating the "queue until ready" logic per source.
- **Real bug, found via a live repro session on a physical device, not
  guessed**: tapping a notification while the app had been fully killed
  landed on the right group's page — title and layout rendered — but
  showed **"Group not found."**, even though the signed-in account
  genuinely was a member of that exact group (confirmed by hand: pulled
  the group's real member list via SQL and cross-checked against the
  account signed in on the device). Diagnostic `Log.Debug` calls added at
  every handoff (`OnMessageReceived` → `HandleIntent` → the route string)
  proved `group_id` was byte-for-byte correct the entire way through —
  ruling out a data bug. What actually happened, confirmed by one more
  diagnostic round: **`AxisFirebaseMessagingService` can start the app's
  process with no Activity ever appearing** — Android starts the process
  purely to run the messaging service, and MAUI's `Application`/`Window`/
  `Shell` object graph gets constructed as soon as *that* process starts
  (`CreateWindow()` runs unconditionally), well before `SplashPage` —
  the page that actually calls `RestoreSessionAsync` — has ever rendered.
  The old queue logic used `Shell.Current is null` as its "is it safe to
  navigate yet" signal; in this state `Shell.Current` was already
  non-null (from that headless construction), so a notification tapped
  shortly after saw it as "ready" and navigated immediately — straight
  into an RLS-gated query with no session ever restored. The result
  ("Group not found") is indistinguishable from a real permission
  failure from the client's point of view: PostgREST returns the same
  empty result set whether `auth.uid()` is a real account that isn't a
  member, or genuinely null. This exact race was never observable before
  this feature — every prior entry point (invite links, normal launches)
  always went through a real `MainActivity.OnCreate` first, and this is
  the first thing in the app that can start the process without one.
  **Fixed** with an explicit `App.isReadyToNavigate` flag, set true only
  once `SplashPage.OnAppearing` has actually finished `RestoreSessionAsync`
  and landed on Login/Groups — `Shell.Current`'s nullness is no longer
  consulted at all for this decision. Confirmed fixed against the same
  live repro (force-stop the app, send a push, tap it) after the fix
  deployed; the temporary diagnostic logging used to find this was removed
  once confirmed.

## Owner-only group rename (2026-09-03)

Small addition, no new SQL: `IGroupsRepository.RenameAsync` rides the
existing `"update own groups"` RLS policy (`created_by = auth.uid()`)
as-is — a rename is just another `groups.name` update, the same policy
that already gates `transfer_group_ownership()`'s target column. `⋮` on
`GroupDetailPage` gained "Rename group" (gated to `IsGroupCreator`, same
role check as Leave/Transfer/Dissolve), opening an inline overlay
(`IsRenameGroupOverlayOpen`/`RenameGroupInput`) rather than
`Shell.Current.DisplayPromptAsync` — same known WinUI crash `MembersPage`'s
member-rename overlay and `LoginPage`'s forgot-password flow already avoid
the same way. Confirmed working end to end against the live project on a
physical device.

## Self-service account deletion (2026-09-04)

`web/delete-account/index.html` (added the same day, before this) documented
a manual, email-triggered deletion process — created only because Play
Console's app-verification flow flagged the *absence* of any account-
deletion info, not because a real deletion flow existed. Built the real
thing instead: a "Delete account" section on `ProfilePage`, **blocking**
(not auto-transferring) if the account still owns a group with other
members — deliberately mirroring `leave_group()`'s existing "creator must
transfer or dissolve first" rule rather than silently reassigning ownership
without asking anyone.

- **Schema migration required first**: `auth.users(id)` was referenced with
  no `ON DELETE` action (`RESTRICT`) by `members.created_by`,
  `payments.created_by`, `invites.created_by`, `expenses.created_by`, and
  `recurring_expenses.created_by` (all `not null`) — meaning
  `auth.admin.deleteUser()` would fail with a foreign-key violation for
  almost any real account, since nearly everyone has created at least one
  expense or their own member row. Relaxed all five to `on delete set
  null` (and dropped their `not null`) — the same "row survives, its
  reference nulls out" treatment already used for `payments`/`expenses`/
  `recurring_expenses.group_id` when a group dissolves, not a new design
  idea. `groups.created_by` deliberately keeps its original `RESTRICT`
  behavior: `delete_account()` explicitly deletes every group the account
  still owns before the account row itself goes, so no `groups` row should
  ever survive with a dangling `created_by`.
- **`public.delete_account()`** (schema.sql) — `security definer`, needed
  because unlinking `members.account_id` for a member the caller *claimed*
  (rather than created themselves) would otherwise fail the plain "update
  members you created or claim yourself" policy's implicit `WITH CHECK`
  (reuses the `USING` clause — `created_by = auth.uid() or account_id =
  auth.uid()` — no longer true once `account_id` is nulled), the exact same
  footgun `transfer_group_ownership()` already exists to work around. Blocks
  with the same `raise exception '<message>'` style as `leave_group()` if
  the account owns a group with other members; otherwise deletes the
  account's `device_tokens`, deletes every group it owns (guard above
  ensures none have other members left), and nulls the claimed
  `members.account_id`/`avatar_path`, returning the pre-nulled `avatar_path`
  so the Edge Function below knows what Storage file to remove. Does **not**
  delete the `auth.users` row itself — that has to go through the Admin API.
  Known minor limitation, not worth guarding against: if the account ever
  had more than one `members` row (the duplicate-row scenario already
  flagged as unconfirmed-but-plausible under `GetMyMemberAsync`), only one
  row's `avatar_path` is returned, so a second stale avatar file could
  survive — same harmless-orphan bucket the receipt-cleanup function
  already tolerates.
- **`supabase/functions/delete-account/index.ts`** — the **first Edge
  Function in this repo called directly from the app**, rather than from a
  SQL trigger/cron via `pg_net` like `send-push`/`cleanup-receipts` (both of
  which trust their caller implicitly, since only `pg_net` holding the
  Vault service-role secret can reach them). This one authenticates its own
  caller: a caller-scoped client (built from the forwarded `Authorization`
  bearer token) calls `delete_account()` so it runs under the caller's own
  `auth.uid()`/RLS — a blocked guard surfaces as a 400 with the raw
  exception message. Only the final step — deleting the Storage avatar file
  and calling `supabase.auth.admin.deleteUser(userId)` — uses a
  service-role admin client. `auth.admin.deleteUser` is used deliberately
  instead of a raw `delete from auth.users`, since only the Admin API
  correctly cleans up GoTrue's own internal session/identity tables — no
  existing precedent either way in this repo, so this follows Supabase's
  documented-correct approach rather than the shortcut.
- **`IAuthService.DeleteAccountAsync()`** (`SupabaseAuthService.cs`) is the
  **first direct Edge-Function call from C#** — a plain `HttpClient` POST to
  `{SupabaseConfig.Url}/functions/v1/delete-account` with the current
  session's `AccessToken` as the bearer token and `SupabaseConfig
  .PublishableKey` as `apikey`, since no HTTP wrapper existed for this
  before. `NotConfiguredAuthService` also got a matching stub — the
  interface addition broke its build too, easy to miss since it's an
  unregistered fallback with no direct caller in normal operation.
- **`ProfileViewModel.DeleteAccountCommand`** follows
  `GroupDetailViewModel.LeaveGroup`'s exact confirm-then-act-then-navigate
  shape (`RunSafeAsync` → `Shell.Current.DisplayAlert` → early return on
  cancel → call the service → `Shell.Current.GoToAsync(AppConstants.Routes
  .Login)`, the same ending `GroupsViewModel.Logout` already uses). No
  explicit device-token unregister beforehand the way `Logout` does one —
  `delete_account()` already deletes every `device_tokens` row for the
  account server-side, not just this device's.
- Confirmed building clean on both `net10.0-windows10.0.19041.0` and
  `net10.0-android` after this change. **Not yet manually tested against
  the live project** — the schema migration block and `delete_account()`
  still need to be run in the SQL editor, and `delete-account` deployed via
  the dashboard's "Via Editor" flow (same as `send-push`/`cleanup-receipts`),
  before either the blocked-path or success-path can be verified for real.
- `delete_account()`'s guard names the specific blocked groups
  (`select string_agg(g.name, ', ' order by g.name) into v_blocked_names ...`
  then `raise exception 'Transfer ownership or dissolve before deleting your
  account: %', v_blocked_names`) rather than a generic "you still own
  groups" message — found worth doing once the first real test showed the
  blocked message with no indication of *which* group.
- **Building the real popup surfaced that the blocked error was effectively
  invisible**: every ViewModel's `RunSafeAsync` sets a plain `ErrorMessage`
  string, and every page had its own independently-placed `<Label
  Text="{Binding ErrorMessage}">` — on `ProfilePage` that label sat up near
  the Birthday/Save section, nowhere near the Delete Account button at the
  bottom that actually triggered it. Fixed app-wide (all 9 pages that bound
  `ErrorMessage`: `GroupsPage`, `GroupDetailPage`, `MembersPage`,
  `RecurringExpensesPage`, `NewGroupPage`, `JoinGroupPage`, `AddExpensePage`,
  `LoginPage`, `ProfilePage`) with a new **`Controls/ErrorPopup`** — a
  hand-built scrim (`BoxView`, `Black`/`Opacity="0.55"`) + centered
  `ElevatedCard` with an OK button, mirroring `AddExpensePage`'s existing
  receipt-preview overlay construction (scrim + full-bleed `Border`) rather
  than introducing anything new. Deliberately **not**
  `CommunityToolkit.Maui.Popup` — same reason `BaseViewModel`'s own remarks
  already give for avoiding it (its `Close()`/`Page.ShowPopupAsync()` don't
  exist in the 13.0.0 version this app is pinned to). `ErrorPopup.Message`
  is a `BindableProperty` with `BindingMode.TwoWay` as its *default* mode
  (`ProfileCircle`'s plain-bindable-property pattern, extended) — every page
  just writes `Message="{Binding ErrorMessage}"`, and dismissing (scrim tap
  or OK) writes `""` straight back onto the ViewModel, the same value
  `RunSafeAsync` already resets to at the start of every command. Added as
  the last child of each page's outermost `Grid` (a plain child on the
  pages already double-Grid-wrapped for exactly this "cover everything"
  behavior — `GroupDetailPage`, `MembersPage`, `GroupsPage`, `LoginPage`; an
  explicit `Grid.RowSpan` matching each page's own `ActivityIndicator`
  overlay everywhere else — `ProfilePage`/`NewGroupPage`/`JoinGroupPage`/
  `RecurringExpensesPage` at 2, `AddExpensePage` at 3, matching its own
  receipt-preview overlay's `RowSpan="3"` exactly).
- Two new `Common_*` localization keys (`Common_OK`, `Common_Error`) back
  the popup's button/heading, en/es.
- **Confirmed working end to end against the live project** — real
  deletion, real signed-in account, real success. Getting there surfaced
  two more real bugs, both in `SupabaseAuthService.DeleteAccountAsync`/
  `ProfileViewModel.DeleteAccount`, not the SQL/Edge Function side:
  1. `DeleteAccountCommand` had no re-entrancy guard —
     `CommunityToolkit.Mvvm`'s generated `AsyncRelayCommand` allows
     concurrent executions by default, so a double-tap (or just an
     impatient second tap during the confirm dialog/network round trip)
     could fire `DeleteAccountAsync` twice on the same cached access
     token. Fixed with `[RelayCommand(AllowConcurrentExecutions = false)]`.
  2. **The real bug**, and the one actually hit live: even a single,
     successful call showed a raw GoTrue error
     (`{"code":403,"error_code":"user_not_found","msg":"User from sub
     claim in JWT does not exist"}`) in the new `ErrorPopup` instead of
     navigating to Login — even though the account genuinely had been
     deleted. Root cause: `DeleteAccountAsync` called `client.Auth
     .SignOut()` *after* the Edge Function's success response, but by
     then `auth.users` no longer had that row, so `SignOut()`'s own
     server-side `/logout` call got the identical "sub claim doesn't
     exist" rejection — that exception was caught by the method's outer
     `catch` and reported as an overall failure, even though deletion had
     already fully succeeded. Fixed by wrapping just the `SignOut()` call
     in its own try/catch and always returning success once the Edge
     Function itself has confirmed deletion — a lingering local session
     from a failed remote sign-out is harmless, since `SplashPage
     .OnAppearing`'s own try/catch around `RestoreSessionAsync` already
     falls back to Login on any failure there.
- `web/delete-account/index.html` updated to lead with the in-app path
  (Profile → Danger zone → Delete account, immediate) now that it exists,
  keeping the email flow as the documented fallback for someone who no
  longer has the app installed — the "30 days" turnaround note now
  explicitly scopes to that email path only.

## Same-day activity ordering, and the merge payments into expenses (2026-09-04)

Found while testing: creating several settle-ups on the same day, they
sometimes appeared out of creation order in Group Detail's Recent Activity
feed — the newest one could land at the bottom instead of the top. Root
cause: `Expense.OccurredAt` comes from a plain date picker with no time
component (`AddExpenseViewModel.OccurredOn`), so several same-day entries
land on an identical midnight timestamp — sorting by `OccurredAt` alone then
has no real tiebreaker, so ties fall back to whatever arbitrary order
Postgres/Postgrest happens to return. Fixed by sorting
`OccurredAt desc, CreatedAt desc` instead — `CreatedAt` is server-set via
`now()` on every insert, so it always has full timestamp precision and
reflects real creation order even when `OccurredAt` can't (`ActivityItem`
gained a `CreatedAt` field; `GroupDetailViewModel.LoadAsync`'s final sort
uses both).

That bug, plus about to build a dedicated `EditPaymentPage` nearly identical
to `AddExpensePage`'s existing edit flow, prompted revisiting a question
this project had already half-answered once before: is a separate
`payments` table still justified? `recurring_payments` was retired
2026-08-31 on exactly this reasoning — `Payment(payer, payee, amount)` has
identical balance math to `Expense(paid_by=payer, shares=[{payee, amount}])`
(confirmed again here: the sign conventions line up — the fronting/paying
party's balance moves up, the other party's moves down, in both shapes).
Decided to finish what that retirement started and merge the base
`payments` table into `expenses` too, rather than build a second near-
duplicate edit page. The project was still pre-launch with no real user
data, so this shipped as a clean cutover (existing `payments` rows dropped,
not migrated) rather than a backfill.

- **`supabase/merge_payments_into_expenses.sql`** (run directly against the
  live project, not part of `schema.sql`'s own linear script since it was
  applied as a one-off against data that already existed) — `drop table
  public.payments cascade` took `group_balances`/`my_group_balances`/
  `pairwise_balances`/`my_pairwise_balances`/`payment_notification_
  recipients()` down with it automatically (all had real dependency links
  on the table — the balance views because they're plain SQL, the recipient
  function because `language sql` functions get dependency-tracked at
  CREATE time, unlike `plpgsql`). `notify_new_payment()` needed an explicit
  drop — no table reference in its body, so cascade didn't catch it. Added
  `expenses.is_settlement boolean not null default false` and recreated all
  four balance views without the payment-side CTEs (output columns
  unchanged, so `leave_group()`/`remove_group_member()`, which read
  `group_balances` by name, needed no changes). `schema.sql` itself was
  then edited directly to match this end state (table, indexes, RLS
  policies, the `payment_net` CTE, `payment_notification_recipients`/
  `notify_new_payment`/its trigger, the account-deletion FK relaxation —
  all physically removed, not left as a dead create-then-drop pair), same
  "keep it a clean fresh-install script" treatment `recurring_payments` got
  when it was retired.
- **`Models/Payment.cs`, `Services/IPaymentsRepository.cs`,
  `Services/SupabasePaymentsRepository.cs`** deleted outright.
  `Expense.cs` gained `IsSettlement` (`[Column("is_settlement")]`).
- **`GroupDetailViewModel.Settle`** now inserts an `Expense` (`IsSettlement
  = true`, `PaidByMemberId` = the discharging party) + one `ExpenseShare`
  (the party being paid back, `ShareAmount` = the full amount) via
  `IExpensesRepository.AddAsync`, instead of a `Payment`. `LoadAsync`'s
  Recent Activity feed collapsed back to a single loop over `expenses` —
  no more separate payments fetch/merge.
- **Settlement editing "fell out for free"** rather than needing the
  planned dedicated `EditPaymentPage`: tapping any activity row (settlement
  or not) already opens `AddExpensePage` in edit mode, since every row is
  now an `Expense`. `AddExpenseViewModel` gained `IsSettlement` (set once
  from the loaded expense, never user-toggleable — same "fixed at creation"
  treatment `CanToggleRecurring` already gives Recurring vs. one-off) and
  `ShowMoneyExtras`, which hides the Category chips and receipt drop-zone
  for that case (neither applies to a settle-up). The existing multi-select
  split UI needed no changes at all — a settlement is simply the case where
  exactly one participant is checked with the full amount owed, which the
  general split logic already produces correctly. `Save()` carries
  `IsSettlement` through on an edit the same way it already had to for
  `CreatedBy`/`CreatedAt` (a fresh `Expense` object defaults it back to
  `false` otherwise — same footgun class as the `Expense` edit bug
  documented above). New `AddExpense_EditSettlementTitle` loc key (en/es)
  for the page title in this mode. No creation path was added for a new
  settlement via this page — only Settle produces one.
- **`send-push/index.ts`** lost its `payment_id` branch entirely — the
  function now always looks up an `expenses` row (selecting `is_settlement`
  too) and branches its push copy on that instead of on which id arrived
  (`"{payer} paid you back — {amount} {currency}"` vs. `"{payer} added
  {description} — {amount} {currency}"`). The FCM `data` payload's `type`
  field is now `"settlement"` or `"expense"` instead of the old
  `"expense"`/`"payment"` split; `payment_id` dropped from the payload
  (nothing on the client ever read it — only `group_id`/`group_name` drive
  tap routing, see the tap-to-open section above). Needs redeploying via
  the dashboard's "Via Editor" flow, same as every other Edge Function in
  this project — not yet done as of this writing.
- **Build-verified only** (`dotnet build` clean on both `net10.0-android`
  and `net10.0-windows10.0.19041.0`) — the SQL migration was run and
  confirmed applying cleanly, but a real create-settlement-then-edit-it
  pass against the live project hasn't happened yet, and neither has
  redeploying `send-push`.

## Architecture

### Backend abstraction — why it exists, and the one rule

Every data access interface lives in `Services/` (`IAuthService`,
`IMembersRepository`, `IGroupsRepository`,
`IExpensesRepository`, `IBalancesRepository`, `IRecurringExpensesRepository`,
`IInvitesRepository`, `IDeviceTokensRepository`). `IPaymentsRepository` and
`ICategoriesRepository` existed early on and were both retired once their
tables were (payments 2026-09-04, categories 2026-08-28) — see "Merge
payments into expenses" and the "Categories removed" schema.sql remarks.
**ViewModels depend on these interfaces, never on the
`Supabase.Client` type or the `supabase-csharp` package directly.** The reason:
if this ever moves off Supabase (self-hosted Supabase on a NAS, or a fully
custom backend), that's a new implementation of these interfaces registered
in `MauiProgram.cs` — not a rewrite of every page and ViewModel. Keep it that
way as the app grows.

`Models/` are plain data classes decorated with `Postgrest` attributes
(`using Supabase.Postgrest.Attributes;` / `using Supabase.Postgrest.Models;`
— this **is** the correct namespace for the installed `Supabase` 1.6.0
package; an earlier note here said the opposite based on a stale/pre-1.6.0
install, see the NuGet section below) — `[Table]`, `[PrimaryKey]`,
`[Column]`, base class `BaseModel` — so they double as both the app's domain
model and the Postgrest ORM's row mapping. Any get-only computed property on
a model (no `[Column]`) needs `[Newtonsoft.Json.JsonIgnore]`, or Postgrest's
serializer will try to send it as a column on insert/update and PostgREST
will reject it (`PGRST204`) — see `Member.IsPhantom`.

### MVVM + Dependency Injection

Same conventions as the previous app: **CommunityToolkit.Mvvm**
(`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`), all
services/pages/ViewModels registered in `MauiProgram.cs`. Once real
repositories exist, register them the same way `IAuthService` is registered
now: interface → concrete Supabase-backed implementation, singleton.

### Navigation

Uses **MAUI Shell**, routes declared as constants in `AppConstants.Routes`
(`Splash`, `Login`, `Groups`, `GroupDetails`, `Members`, `JoinGroup`,
`AddExpense`, `NewGroup`) rather than hardcoded strings — follow that pattern
for any new screen. `Splash` is the first `ShellContent` in `AppShell.xaml`
(see "Splash screen" above); it decides between `//Login` and `//Groups`
before anything else renders.

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
  into `App.HandleDeepLink(uri)`, which queues the URI in the static
  `pendingDeepLink` field if `Shell.Current` is still null (cold start —
  the Intent arrives before Shell exists) or navigates immediately
  otherwise. `SplashPage` calls `App.ReplayPendingDeepLinkAsync()` once it's
  picked Login vs. Groups, so a queued cold-start link always replays after
  landing, never before. `AppConstants.Links.TryExtractCode` is the one
  place that parses a `?code=` query param back out of either a full invite
  URL or a raw platform URI.
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
  Consume these from XAML rather than hardcoding values, same rule as before.
- Colors (`Resources/Styles/Colors.xaml`) are **not** the same — Axis has its
  own palette (blue/amber) rather than reusing DebtTracker's purple/teal,
  since it's a distinct app. Same color *keys*, different values, so
  `Styles.xaml` didn't need to change at all.
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
| `expenses` / `expense_shares` | N-way split expenses. `is_settlement` marks a settle-up (exactly one share) — see "Merge payments into expenses" below. |
| `recurring_expenses` / `recurring_expense_shares` | Templates for periodically auto-generated N-way split expenses. Replaces the retired `recurring_payments` (pairwise, never got UI). |
| `device_tokens` | Per-account push tokens for the notification feature. |
| `member_aliases` | Private, per-account nickname override for how a member is displayed. |

(No `categories` table — removed 2026-08-28; see schema.sql's remarks. Categories
are now a small fixed list of keys in `AppConstants.Categories`, localized
client-side, not stored data. No `payments` table either — removed
2026-09-04, see "Merge payments into expenses" below.)

Also a public `avatars` Storage bucket and a private `receipts` Storage
bucket (`storage.buckets`/`storage.objects`, not `public.*`) — see "Avatar
photos" and "Receipt photos" above for the bucket/policy shapes.

Read-only views (no primary key, `security_invoker = true` so they enforce
RLS as the querying user, never inserted/updated/deleted):

| View | Purpose |
|---|---|
| `group_balances` | Each member's net balance against a group's whole shared pot, from `expenses`/`expense_shares` alone (settlements included). |
| `my_group_balances` | The current account's own row from `group_balances`, one per group — feeds the Groups list. |
| `pairwise_balances` | Real two-party net balance between every pair of members who've actually shared money in a group. |
| `my_pairwise_balances` | `pairwise_balances` reoriented around the current account, sign-normalized to "positive = they owe me". |

RLS is enabled on every table. Several functions intentionally bypass it via
`SECURITY DEFINER` — `redeem_invite()` (adding a `group_members` row for
someone who, by definition, isn't a group member yet),
`transfer_group_ownership()` (the plain `update own groups` policy would
reject the very act of transferring `created_by` away from the caller),
`remove_group_member()` (business rules — phantom-only, balance-zero — not
expressible as a plain RLS policy), and the small helper functions
`is_group_member()`/`is_own_member_row()`/`is_unscoped_expense_party()` (used
*inside* other policies specifically to avoid Postgres RLS recursion when two
tables' policies would otherwise reference each other — see "Leaving,
transferring ownership, and dissolving a group" above for two real 42P17
recursion bugs this caused). `leave_group()` and `create_group()` are
notably **not** security definer — every operation they perform is already
permitted under existing RLS, so there's no permission gap to bypass, only
atomicity (`create_group`) or explicit business-rule guards (`leave_group`).
Don't add a new `SECURITY DEFINER` function without a similarly specific
reason (a real permission gap, or breaking an RLS recursion cycle); prefer
expressing access rules as plain RLS policies so Postgres enforces them
uniformly.

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

Be skeptical of assuming an SDK's API surface without checking a real local
build — the Models' namespace was briefly `Postgrest.*` instead of the
correct `Supabase.Postgrest.*` early on (pinned to a stale 0.16.2 install at
the time), which is now fixed across every model. `SupabaseAuthService`'s
`client.Auth.*` calls and every `Supabase*Repository`'s query call shapes
(`.Filter`/`.Insert`/`.Update`/`.Get`/`.Single`) have since been confirmed
against a real local build — see the four bugs in "Current state" above,
found via an actual compile + a real sign-up/create-group run against the
live project, not docs. The reliable source of truth is still a real local
build's compiler errors, not fetched docs — when something new comes up that
hasn't been build-verified, say so explicitly rather than asserting an API
shape with false confidence.

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
# — Cloudflare's GitHub integration auto-deploys web/ on every push to main
# (confirmed 2026-09-04), so this manual command is now only needed for
# previewing before a push, or redeploying without a new commit.
cd web && npx wrangler deploy
```

There are no automated tests in this project yet.
