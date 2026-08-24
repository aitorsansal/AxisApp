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
- **Two real bugs found and fixed while wiring the screens, worth knowing
  about**: (1) `SupabaseGroupsRepository.CreateAsync` originally only
  inserted a `groups` row — it never made the creator an actual member, so
  a newly created group would've been invisible in the creator's own list
  (RLS's "select groups you belong to" requires a real `group_members`
  row). Fixed to also create a `Member` + `GroupMember` for the creator,
  mirroring `redeem_invite()`'s "fresh join" shape. (2) `GroupMember` and
  `ExpenseShare` both have a real composite primary key in Postgres
  (`(group_id, member_id)` / `(expense_id, member_id)`) but only mark ONE
  property `[PrimaryKey]` in the C# model — harmless for insert/delete
  (which never relied on it), but a real footgun for `.Update(model)`,
  which would match on that single column and silently update every row
  sharing it. `SupabaseExpensesRepository.UpdateAsync` works around this
  with an explicit `.Filter(...).Filter(...).Update(...)` rather than
  trusting the implicit PK match — do the same if `GroupMember` ever needs
  an update path.
- Multi-theme (preset color palettes the user picks from) and a
  fully-custom user-defined palette were discussed but are **not planned
  work** — see `/SCOPE.md`'s Theming section for the technical read on
  feasibility if it comes up again.

Next real milestones, in order: (1) run the new blocks at the bottom of
`supabase/schema.sql` against the live project (expenses/expense_shares/
balances views/device_tokens, plus the expense_shares update policy); (2)
get everything actually compiling and confirm auth + a repository call work
end to end (report back compiler errors as they come up — see the caveats
above); (3) the Supabase-side infra `/SCOPE.md` describes but that has no
code yet — receipt storage/upload, the cleanup Edge Function, recurring
payment materialization, push.

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

## Architecture

### Backend abstraction — why it exists, and the one rule

Every data access interface lives in `Services/` (`IAuthService`,
`IMembersRepository`, `IGroupsRepository`, `IPaymentsRepository`,
`IRecurringPaymentsRepository`, `ICategoriesRepository`,
`IInvitesRepository`). **ViewModels depend on these interfaces, never on the
`Supabase.Client` type or the `supabase-csharp` package directly.** The reason:
if this ever moves off Supabase (self-hosted Supabase on a NAS, or a fully
custom backend), that's a new implementation of these interfaces registered
in `MauiProgram.cs` — not a rewrite of every page and ViewModel. Keep it that
way as the app grows.

`Models/` are plain data classes decorated with `Postgrest` attributes
(`using Postgrest.Attributes;` / `using Postgrest.Models;` — **not**
`Supabase.Postgrest.*`, despite what the SDK's own README examples show) —
`[Table]`, `[PrimaryKey]`, `[Column]`, base class `BaseModel` — so they double
as both the app's domain model and the Postgrest ORM's row mapping.

### MVVM + Dependency Injection

Same conventions as the previous app: **CommunityToolkit.Mvvm**
(`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`), all
services/pages/ViewModels registered in `MauiProgram.cs`. Once real
repositories exist, register them the same way `IAuthService` is registered
now: interface → concrete Supabase-backed implementation, singleton.

### Navigation

Uses **MAUI Shell**. Currently a single route (`Login`). As real screens are
built, follow the route-name-as-constant pattern in `AppConstants.Routes`
rather than hardcoding route strings.

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

RLS is enabled on every table. The one function that intentionally bypasses
it is `redeem_invite()` (`SECURITY DEFINER`) — it exists specifically because
redeeming an invite requires adding a `group_members` row for someone who, by
definition, isn't a group member yet. Don't add other `SECURITY DEFINER`
functions without a similarly specific reason; prefer expressing access rules
as RLS policies so Postgres enforces them uniformly.

## NuGet Packages (key)

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Maui.Controls` | 10.0.10 | MAUI runtime |
| `CommunityToolkit.Maui` | 13.0.0 | UI controls & behaviors |
| `CommunityToolkit.Mvvm` | 8.4.0 | MVVM source generators |
| `supabase-csharp` | 0.16.2 | Supabase client (Auth + Postgrest + Realtime) |

Be skeptical of the Supabase C# SDK's own README examples — they've been
observed to not match what's actually installed (the Models' `Postgrest.*`
vs. documented `Supabase.Postgrest.*` namespace is a confirmed case, and
`SupabaseAuthService`'s `client.Auth.*` calls are unverified for the same
reason). The reliable source of truth is a real local build's compiler
errors/IntelliSense, not fetched docs — when in doubt, say so explicitly and
let the human confirm from their own build rather than asserting an API
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

There are no automated tests in this project yet.
