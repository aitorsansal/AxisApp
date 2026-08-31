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
  implementation (`Services/Supabase{Members,Groups,Payments,Expenses,
  Balances,Categories,RecurringPayments,Invites,DeviceTokens}Repository.cs`),
  registered as singletons in `MauiProgram.cs` alongside a single shared
  `Supabase.Client` (also registered there — `SupabaseAuthService` and every
  repository take that same instance rather than each opening its own).
  **Same caveat as `SupabaseAuthService`: the exact Postgrest query call
  shapes (`.Filter`/`.Insert`/`.Update`/`.Get`/`.Single`, the
  `Postgrest.Constants.Operator`/`Ordering` enum members, and especially
  `SupabaseInvitesRepository.RedeemAsync`'s use of `client.Rpc(...)`) are
  grounded against the public postgrest-csharp source, not a local build of
  this exact installed version — expect compiler errors, report them back
  the same way.** Includes `expenses`/`expense_shares` (N-way splitting,
  separate from the pairwise `payments` table), a `group_balances` view/
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
  in Group Detail's recent-activity list. Editing a settle-up `Payment` is
  not implemented, only `Expense`.
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

## Architecture

### Backend abstraction — why it exists, and the one rule

Every data access interface lives in `Services/` (`IAuthService`,
`IMembersRepository`, `IGroupsRepository`, `IPaymentsRepository`,
`IExpensesRepository`, `IBalancesRepository`, `IRecurringPaymentsRepository`,
`ICategoriesRepository`, `IInvitesRepository`, `IDeviceTokensRepository`).
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
| `payments` | A single payment between two members, optionally scoped to a group. |
| `recurring_payments` | Template for periodic auto-generated payments. |
| `categories` | User-defined payment categories. |
| `invites` | A redeemable token to join a group, or to claim a specific phantom member. |
| `expenses` / `expense_shares` | N-way split expenses, separate from the pairwise `payments` table. |
| `device_tokens` | Per-account push tokens for the notification feature. |

Read-only views (no primary key, `security_invoker = true` so they enforce
RLS as the querying user, never inserted/updated/deleted):

| View | Purpose |
|---|---|
| `group_balances` | Each member's net balance against a group's whole shared pot (combines `payments` + `expense_shares`). |
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
| `Supabase` | 1.6.0 | Supabase client (Auth + Postgrest + Realtime) |

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
cd web && npx wrangler deploy
```

There are no automated tests in this project yet.
