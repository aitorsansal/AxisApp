# Axis

A shared-expense tracker for households — multiple people, each with their
own account, sharing a ledger inside groups they invite each other into. The
successor to a local-only personal debt tracker, rebuilt around a real
backend (Supabase) from the start instead of retrofitted onto local SQLite.

## Status

**Scaffold stage.** The app builds and shows a login screen; nothing is wired
to a real backend yet. See `CLAUDE.md` → "Current state" for the exact list
of what exists vs. what's next, and `supabase/README.md` for the setup steps
that unblock everything past the login screen.

## What makes this different from a typical splitting app

Adding someone you owe money to doesn't require them to have an account.
Members are either **phantom** (added by name only) or **claimed** (linked to
a real login) — see `CLAUDE.md`'s "core design decision" section for why, and
`supabase/schema.sql` for how invites let a phantom's real person "claim"
their existing history once they actually sign up.

## Getting started

1. **Backend**: follow `supabase/README.md` — create a Supabase project, run
   `supabase/schema.sql`, grab your project URL + anon key.
2. **Client**: open `AxisApp.slnx` in Visual Studio (Windows, per
   `CLAUDE.md`'s target frameworks) and build/run the `AxisApp` project. It'll
   show the login screen; sign-in will say "not configured" until step 1's
   credentials are wired into a real `IAuthService` implementation (see the
   TODO in `MauiProgram.cs`).

## Structure

```
AxisApp/            the MAUI client
  Models/            Postgrest-mapped data classes (Member, Group, Payment, ...)
  Services/          backend-abstraction interfaces + the auth placeholder
  ViewModels/, Pages/ MVVM screens
  Resources/Styles/   design tokens + Axis's color palette
supabase/
  schema.sql         the full Postgres schema, RLS policies, and invite-redemption function
  README.md          setup steps
```
