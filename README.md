# Axis

A shared-expense tracker for households — multiple people, each with their
own account, sharing a ledger inside groups they invite each other into. The
successor to a local-only personal debt tracker, rebuilt around a real
backend (Supabase) from the start instead of retrofitted onto local SQLite.

## Status

**Scaffold stage.** The Supabase project exists and its schema is live; the
app has a login screen wired to a real `SupabaseAuthService` — but that
service's exact Auth API calls haven't been verified against a local build
yet (see `CLAUDE.md` → "Current state" for exactly what that means and what
to do if it doesn't compile). No group/payment/invite screens exist yet.

## What makes this different from a typical splitting app

Adding someone you owe money to doesn't require them to have an account.
Members are either **phantom** (added by name only) or **claimed** (linked to
a real login) — see `CLAUDE.md`'s "core design decision" section for why, and
`supabase/schema.sql` for how invites let a phantom's real person "claim"
their existing history once they actually sign up.

## Getting started

1. **Backend**: already set up — see `supabase/README.md` if you need to
   recreate it. To point the client at your project, copy
   `AxisApp/Config.Local.cs.example` to `AxisApp/Config.Local.cs` (gitignored)
   and fill in your project URL + publishable key.
2. **Client**: open `AxisApp.slnx` in Visual Studio (Windows, per
   `CLAUDE.md`'s target frameworks) and build/run the `AxisApp` project. If
   `SupabaseAuthService.cs` fails to compile, that's expected — its exact
   `client.Auth.*` calls are unverified (see `CLAUDE.md`); paste the compiler
   errors back so they can get fixed for real instead of guessed at again.

## Structure

```
AxisApp/            the MAUI client
  Models/            Postgrest-mapped data classes (Member, Group, Payment, ...)
  Services/          backend-abstraction interfaces + SupabaseAuthService
  Config.Local.cs.example   template for your own Supabase credentials
  ViewModels/, Pages/ MVVM screens
  Resources/Styles/   design tokens + Axis's color palette
supabase/
  schema.sql         the full Postgres schema, RLS policies, and invite-redemption function
  README.md          setup steps
```
