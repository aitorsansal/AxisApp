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
→ Add expense / Add payment → Invite/Join group flow.

**Built.** All core screens (`GroupsPage`, `GroupDetailPage`, `MembersPage`,
`AddExpensePage`, `JoinGroupPage`, `NewGroupPage`) exist and are wired to
the real repositories — see `CLAUDE.md`'s "Current state" for the specifics
(edit-expense flow, the two bugs found while wiring it up, and the
2026-08-31 additions: leave/transfer-ownership/dissolve a group, viewing a
group's members, removing a phantom, per-account member aliases, and avatar
photos). Built from a locked design handout
generated via Claude Design; that handout isn't checked into the repo, so if
it's needed again (new screens, revisiting a layout decision) it'll need
regenerating — the prompt used is reconstructable from this doc's token
values plus the screen descriptions above.

Since then, `ProfilePage` (own name/birthday/avatar/language/email/password,
consolidated off `GroupsPage`'s account menu) and Google sign-in +
forgot-password (`LoginPage`) were added — see CLAUDE.md's "Profile page"
and "Google sign-in, password reset, and avatar backfill" sections
(2026-08-31 / 2026-09-01–02).

Not yet built: editing a settle-up `Payment` (only `Expense` editing
exists).

### Supporting infra

- **Receipts**: Supabase Storage bucket (`receipts`), private with policies
  scoped to group members. Client resizes + encodes to WebP before upload,
  targeting ~100KB (downscale to ~1280px long edge, step quality/dimension down
  if still over target). **Done 2026-08-31** for expenses (capture/pick, upload,
  view via signed URL — see CLAUDE.md's "Receipt photos"); `payments` still has
  `receipt_path` reserved but unused.
- **Receipt cleanup**: weekly Edge Function on `pg_cron`, **done 2026-08-31**
  (see CLAUDE.md's "Receipt cleanup" remarks), confirmed working against the
  live project. Orphaned receipts (no expense references them anymore) get
  purged after **3 months**; receipts still attached to a real expense get
  the *photo* purged after **6 months** — the expense record itself is never
  deleted, only the image, with `receipt_path` nulled out. Both windows are
  measured off the file's own upload time (`storage.objects.created_at`),
  not the expense's date. `payments.receipt_path` is out of scope here since
  it's still unused (see above).
- **Recurring expenses**: `recurring_expenses`/`recurring_expense_shares`
  (N-way split templates, mirroring `expenses`/`expense_shares`), the
  add/edit/view/pause/delete UI, and the `pg_cron` job that materializes
  due templates into real `expenses`/`expense_shares` rows server-side
  (daily at 8am UTC, not dependent on the app being opened) are all
  **done 2026-08-31** (see CLAUDE.md's "Recurring expenses" and "pg_cron
  materialization" remarks) and confirmed working against the live
  project. Replaces the old pairwise `recurring_payments` table, retired
  the same day: a `Payment(payer, payee, amount)` has identical balance
  math to `Expense(paid_by=payer, participants=[payee], share=amount)`, so
  a 1-way recurring expense fully covers what `recurring_payments` was
  for, and nothing had ever built UI for it.
- **Push notifications**: **plan revised 2026-09-03** — dropped the OneSignal
  wrapper originally described here in favor of raw Firebase Cloud Messaging,
  **Android-only for now**. Windows is an unpackaged Win32 build, and this
  codebase already hit exactly this class of wall once before (native
  Windows notification APIs requiring MSIX packaging — see CLAUDE.md's
  crash-safety notes); OneSignal's actual Windows/WNS support for an
  unpackaged MAUI app was never a verified fact, and Android's delivery is
  FCM either way, so the wrapper wasn't buying real cross-platform coverage.
  Windows push is deferred to its own separate investigation, not attempted
  here.
  **Client-side registration and server-side send: both done 2026-09-03**
  (`IPushRegistrationService` on the client; `expense_notification_recipients`/
  `payment_notification_recipients` + `notify_new_expense`/`notify_new_payment`
  triggers + the `send-push` Edge Function on the server — see CLAUDE.md's
  "Push notifications — client-side registration" and "— server-side send"
  for the full detail), confirmed working end to end against the live
  project with the real `AFTER INSERT` trigger firing on its own, correctly
  scoped to only the expense/payment's actual participants (not the whole
  group, and not a "balance ≠ 0" heuristic — see CLAUDE.md for why that
  would have been wrong). **Still not built**: a `FirebaseMessagingService`
  for foreground handling and tap-to-open deep linking (tapping a
  notification currently just opens the app to Groups, not the specific
  group) and a proper "Axis" notification channel (currently falls back to
  Firebase's own default).

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
  expenses).
- UI: calendar view per group, RSVP to events.

Not started until Phase 1 is fully working and has proven the backend pattern.

## Phase 2.5 — Google Calendar sync

Treated as its own scope, tackled only after native events (Phase 2) ship:

- Per-user Google OAuth (separate consent flow from Supabase Auth).
- Encrypted token storage + refresh.
- Sync strategy: Google webhook push vs. polling.
- Reconciling Axis's recurrence model against Google's RRULE.

This is real, independent scope — not a checkbox on the events feature.

## Theming

**Presets: done 2026-09-01.** What used to be a "how hard would it be"
discussion (recorded below, kept for the reasoning) turned into
`Services/ThemeService.cs` + `Services/AccentPalettes.cs` — a per-device
accent-color picker on `ProfilePage` with **8 fixed presets** (Blue, Green,
Red, Purple, Pink, Amber, Orange, Navy), covering exactly the "presets
first" plumbing described below almost exactly as speculated: `Colors.xaml`
references switched from `{StaticResource ...}` to `{DynamicResource ...}`
for the ~35 accent-derived keys, `ThemeService` swaps values into a merged
`ResourceDictionary` and persists the choice via `Preferences`, applied at
startup. See CLAUDE.md's "Per-device accent color picker" section for the
real WinUI-specific snags hit along the way (in-place key mutation vs.
remove-and-readd, and a live `Button`'s native chrome needing an explicit
handler refresh even after the resource value is correct). Scoped
deliberately to just Primary/Secondary and what derives from them —
backgrounds/surfaces/status colors stay fixed across every preset.

**Fully custom user-defined palette: still not planned**, harder tier,
unchanged from the original discussion below.

- Reuses the same runtime-swap mechanism, but a user-picked color doesn't
  come with its hover/pressed/disabled siblings (~15+ tokens per accent) —
  those need deriving programmatically (lighten/darken, compute a readable
  text color against it) rather than hand-picked, unlike the 8 presets
  above, which were precomputed by hand. Also needs a color-picker UI (MAUI
  has none built in) and a contrast guard so someone can't pick a primary
  color that's unreadable against the dark background.
- **Recommendation if this gets picked up**: the `DynamicResource` plumbing
  the presets needed already exists now, so this is a smaller lift than it
  was when first discussed — but the palette-derivation math and
  color-picker UI are still new work.

## Non-goals (for now)

- iOS/macOS targets — no Mac to build/test against yet (see `CLAUDE.md`); the
  platform folders exist but aren't in the active `TargetFrameworks`.
- Anything backend-shaped that isn't Supabase — the `I*Repository` abstraction
  exists so this could change later, but there's no active plan to.
