# Axis — Scope & Roadmap

This is the living scope doc from the "what are we actually building" discussion.
Update it as decisions change — it's the source of truth for phase boundaries and
what's deliberately deferred, so nobody re-litigates a decision that's already
been made here.

## Product vision

Axis isn't "just" an expense splitter. The real goal is **one unified channel per
group** — a shared space for the people in a household/family/friend group that
eventually covers both money (who owes whom) and time (shared events/calendar),
instead of juggling a separate splitting app and a separate calendar app per group.

The debt-tracker (expenses/payments) is being built **first** specifically to prove
the Supabase-only backend (Postgres + Auth + RLS + Storage + Edge Functions +
pg_cron) can carry the whole app end to end — data, files, scheduled jobs, push —
before a second vertical (events/calendar) gets built on the same foundation.

## Phase 1 — Debt tracker (current focus)

The full vertical slice: auth → data → UI → storage → scheduled jobs → push,
all for money-tracking. This is what "done" looks like before Phase 2 starts.

### Data model (additive to `supabase/schema.sql`, no existing rows to migrate)

- **`expenses` + `expense_shares`** — N-way bill splitting. `expenses` has one
  `paid_by_member_id` who fronted the money; `expense_shares` records what each
  participating member owes toward it. `payments` (already in schema.sql) stays
  as-is for direct pairwise settle-ups ("I paid you back $20") — it's a different
  transaction shape, not a special case of an expense.
- **`group_balances`** (view) — net balance per member per group, computed from
  both `payments` and `expenses`/`expense_shares`, so the client queries one
  thing instead of aggregating two tables itself.
- **`currency`** column reserved (default e.g. `'EUR'`) on `expenses`/`payments`
  while the schema is still young — no conversion logic yet, just not retrofitting
  the column onto real rows later.
- **`device_tokens`** — account/member → push token, needed for the push feature
  below.

### Screens / flow

Login → Groups (list) → Group detail (balances + recent activity for that group)
→ Add expense / Add payment → (Invite/Join group flow, routes already stubbed
in `AppConstants.Routes`).

### Supporting infra

- **Receipts**: Supabase Storage bucket (`receipts`), private with policies
  scoped to group members. Client resizes + encodes to WebP before upload,
  targeting ~100KB (downscale to ~1280px long edge, step quality/dimension down
  if still over target).
- **Receipt cleanup**: weekly Edge Function on `pg_cron`. Orphaned receipts (no
  expense/payment references them anymore) purged after **3 months**. Receipts
  still attached to a real expense/payment get the *photo* purged after
  **6 months** — the expense/payment record itself is never deleted, only the
  image, with `receipt_path` nulled out.
- **Recurring payments**: `pg_cron` job scans `recurring_payments` for due
  templates and materializes them into real `payments`/`expenses` rows
  server-side — not dependent on the app being opened.
- **Push notifications**: Supabase Edge Function triggered by DB webhook/trigger
  on new expense/payment, calling **OneSignal** (wraps FCM for Android + WNS for
  Windows behind one API, matching Axis's current two active targets) rather than
  integrating each platform's push service directly.

### Explicitly deferred within Phase 1

- Multi-currency **logic** (conversion, display formatting) — column reserved,
  nothing else.
- Group-level settings/preferences — groups need to exist and be used first.
- Multi-payer expenses (one expense split among many payers, not just one payer
  + many owers) — not currently planned; single `paid_by_member_id` is enough
  for the target use case.

## Phase 2 — Events & Calendar

Builds on the same primitives Phase 1 establishes, not a new architecture:

- `events` (group_id, title, description, starts_at, ends_at, location,
  created_by) — same table shape pattern as `expenses`.
- `event_attendees` (event_id, member_id, response) — same shape as
  `expense_shares`.
- Reminders reuse the Phase 1 push infra (OneSignal + Edge Function) and the
  Phase 1 `pg_cron` pattern (scan upcoming events instead of due recurring
  payments).
- UI: calendar view per group, RSVP to events.

Not started until Phase 1 is fully working and has proven the backend pattern.

## Phase 2.5 — Google Calendar sync

Treated as its own scope, tackled only after native events (Phase 2) ship:

- Per-user Google OAuth (separate consent flow from Supabase Auth).
- Encrypted token storage + refresh.
- Sync strategy: Google webhook push vs. polling.
- Reconciling Axis's recurrence model against Google's RRULE.

This is real, independent scope — not a checkbox on the events feature.

## Non-goals (for now)

- iOS/macOS targets — no Mac to build/test against yet (see `CLAUDE.md`); the
  platform folders exist but aren't in the active `TargetFrameworks`.
- Anything backend-shaped that isn't Supabase — the `I*Repository` abstraction
  exists so this could change later, but there's no active plan to.
