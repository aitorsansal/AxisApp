# Supabase setup

Axis's backend is a Supabase project: Postgres + Auth + Row Level Security, no
custom server. This folder holds the schema; there's no code to deploy.

## 1. Create the project

Go to [supabase.com](https://supabase.com), create a new project, and wait for
it to finish provisioning.

## 2. Run the schema

Open the project's **SQL Editor**, paste in the full contents of
[`schema.sql`](./schema.sql), and run it. It's safe to run once against a
fresh project; it is **not** idempotent (re-running it will fail on the
`create table` statements since the tables already exist) — if you need to
change something, write a new migration file rather than re-running this one.

## 3. Enable email auth

Under **Authentication → Providers**, confirm Email is enabled. For local
development you can disable "Confirm email" so sign-up doesn't require
clicking a verification link every time; re-enable it before anyone besides
you uses the app.

## 4. Get your credentials

Under **Project Settings → API**, copy:

- **Project URL**
- **anon / public key** (not the service_role key — that one must never ship
  inside the app)

## 5. Wire them into the app

`AxisApp/Services/NotConfiguredAuthService.cs` is a placeholder that lets the
app boot before this step is done. Once you have the URL and anon key:

1. Add a real `SupabaseAuthService : IAuthService` (and the
   `I*Repository` implementations) that construct a `Supabase.Client` with
   those two values.
2. Don't hardcode them into a file that gets committed — read them from a
   local config file that's gitignored (`appsettings.local.json` and
   `Config.Local.cs` are already excluded — see `.gitignore`), or from
   platform secure storage.
3. Swap the `NotConfiguredAuthService` registration in `MauiProgram.cs` for
   the real one.

## Why RLS instead of a custom API

Every table has Row Level Security enabled, so Postgres itself enforces "you
can only see/edit data in groups you belong to" — the client talks to
PostgREST directly (via the `supabase-csharp` SDK) with no server code in the
middle to keep in sync with the schema. The one exception is
`redeem_invite()`, a `SECURITY DEFINER` function: redeeming an invite has to
create a `group_members` row for an account that, by definition, isn't a
group member yet — normal RLS can't express "let me in because I have this
specific token," so that one operation runs with elevated privileges inside a
tightly-scoped function instead of loosening any table's RLS policy.

## Self-hosting later

Supabase ships as a self-hostable Docker Compose stack (Postgres + Auth +
Realtime + Storage). If you self-host it later — e.g. on your NAS — "migrating"
is just running this same `schema.sql` against the self-hosted instance and
pointing the app at its URL/anon key instead. No data migration, no auth
migration, because it's the same software either way.
