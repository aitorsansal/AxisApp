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

- The MAUI client builds and shows a Login page. Sign-in/sign-up are wired to
  `IAuthService`, but the registered implementation
  (`NotConfiguredAuthService`) always returns a "not configured yet" error —
  there is no real Supabase project behind it.
- `supabase/schema.sql` is designed and ready to run, but has not been run
  against a real project.
- `I*Repository` interfaces exist (`Services/`) describing the data layer's
  shape; there are **no concrete implementations yet**. Nothing reads or
  writes actual data.
- Beyond the login screen, there is no group/payment/invite UI yet.

Next real milestones, in order: (1) create the Supabase project and run the
schema — see `/supabase/README.md`; (2) implement `SupabaseAuthService` and
swap it in for `NotConfiguredAuthService`; (3) implement the Supabase-backed
repositories; (4) build the groups/payments/invites screens.

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

`Models/` are plain data classes decorated with `Supabase.Postgrest`
attributes (`[Table]`, `[PrimaryKey]`, `[Column]`, base class `BaseModel`) so
they double as both the app's domain model and the Postgrest ORM's row
mapping. See `packages/Postgrest/README.md` in the `supabase-csharp` repo if
the exact attribute shape is ever unclear — don't guess at it.

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

Always verify API compatibility with these exact versions before suggesting
usage of any package API — the Supabase C# SDK in particular has changed
shape across versions; check `packages/Postgrest/README.md` and
`packages/Gotrue/README.md` in the `supabase-community/supabase-csharp` repo
rather than assuming.

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
