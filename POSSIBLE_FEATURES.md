# Possible features & changes

Shortlist kept from a 2026-09-10 review session, trimmed down to what's
actually worth it for a group of friends/family — not a full roadmap, just
ideas worth picking up when there's time. See `CLAUDE.md`/`CHANGELOG.md`/
`SCOPE.md` for what's already shipped.

## New features

### Settle-up nudges (monthly) — the "balances digest" push
A monthly `pg_cron` job scans `group_balances` for a nonzero balance and
sends one gentle push per debtor — a private reminder, not a public list.
Same shape as `materialize_recurring_expenses()`/`send_event_reminders()`:
scan-and-notify on a schedule. Monthly cadence (not biweekly) avoids nagging
anyone over normal household ebb and flow. This is also the natural home for
the `InboxStyle` "You have 2 open balances" digest card from the 2026-09-16
notification-design discussion (see CHANGELOG.md's "Richer push
notifications" entry) — same scan, richer Android rendering on top.

### Weekly events digest (new, from the 2026-09-16 notification discussion)
A Monday-9am `pg_cron` job (own schedule, not piggybacked on the monthly
settle-up nudge above) listing each group's events for the coming week, or
skipping the push entirely if a group has none — mirrors
`send_event_reminders()`'s per-event scan-and-notify shape, just weekly and
aggregated across a group's whole week instead of one event at a time.
Pairs with an `InboxStyle` "3 events this week" card on the Android side,
same as the balances digest above.

**Both digests, when built: `InboxStyle`'s plain `addLine()` can't do the
two-column "label — right-aligned amount/time" layout from the original
mockup** — that needs either accepting a left-aligned single-line format
per row, padding with ` ` (figure space) as an approximation, or a
custom `RemoteViews` layout for just this one card type if pixel-accurate
columns actually matter. See CHANGELOG's notification entry for why a plain
`\t` doesn't work (proportional fonts have no fixed tab-stop metric).

### Home-screen widgets (Android) — two, not one
1. **Balances widget** — each group's net balance at a glance ("You owe
   Marc €12"), fed by the already-computed `my_group_balances` view.
2. **Events widget** — next upcoming event per group (or across all
   groups), fed by `events`/`event_attendees`.

Both are periodic-refresh `RemoteViews` widgets, no new backend work —
Android-only, mirroring where push already landed.

## Upgrades / fixes to existing features

### Notification tray actions give no feedback — reported live 2026-09-16
`NotificationActionReceiver`'s SETTLE/GOING/MAYBE/CAN'T GO actions (see
CHANGELOG's "Richer push notifications" entry) work correctly — confirmed,
the RSVP write really lands — but there's no visible confirmation when you
tap one. The notification *is* updated in place on success ("You're going" /
"Settled"), but that update can be easy to miss if the tray auto-collapses
or you've already moved on, so it doesn't feel like the tap "did" anything.
Worth a haptic tick or a brief toast-equivalent (careful: `Toast.Make(...)`
is confirmed broken on this unpackaged Win32 build per CLAUDE.md, but that's
a Windows-only finding — needs its own check on Android) at the moment the
background write completes, not just relying on the tray card's own text
changing.

### `device_tokens` re-registration can't recover a token from a different account
Real bug, found live 2026-09-16 (see CHANGELOG's "Richer push
notifications" entry) — not yet fixed, currently worked around by manually
deleting the stale row. `SupabaseDeviceTokensRepository.RegisterAsync`
deletes any existing row for the exact FCM token before inserting fresh,
but `device_tokens` RLS (`account_id = auth.uid()`) means that delete only
removes rows the *current* account already owns — a token previously
registered under a different account (a reinstall, a re-login, dev/test
account switching on the same physical device) can never be reclaimed by
the client; the insert just throws `23505` on the unique constraint and the
account silently never gets a working token. Needs a `SECURITY DEFINER`
RPC — same class of genuine permission gap as `redeem_invite()`/
`transfer_group_ownership()` — that deletes any existing row for a given
`push_token` regardless of current owner, then lets the caller insert their
own; `RegisterAsync` would call that instead of a plain `.Delete()`.

### `created_at`/`created_by` likely wrong across most models — sweep needed
Confirmed live 2026-09-16 while debugging the same push-notification issue
(see CHANGELOG's "Richer push notifications" entry): `DeviceToken.CreatedAt`
had no `[Column("created_at", ignoreOnInsert: true)]`, so the C# Postgrest
client sent the CLR default `0001-01-01` on every insert, silently
overriding the DB's `default now()` — fixed for `DeviceToken` only. The
live `expenses` table shows the exact same `0001-01-01` value on almost
every row, meaning `Expense.CreatedAt` has the identical bug — and by the
same reasoning, so almost certainly does `Member.CreatedAt`, `Group.CreatedAt`,
`Invite.CreatedAt`, `EventAttendee.CreatedAt`/`UpdatedAt`,
`RecurringExpense.CreatedAt`, and `CalendarSubscription.CreatedAt` — every
model with a plain, unset-before-insert `created_at`/`updated_at` property.
The fix is mechanical (add `ignoreOnInsert: true` to each) but touches
every model file, so it's its own pass rather than folding into an
unrelated change. Low urgency (nothing in the UI currently displays a raw
`created_at`) but a real data-correctness gap — anything that ever sorts or
displays by creation time (an audit trail, "member since", a support
request) would be silently wrong today.

### Clean up dead push tokens on send — reframed as a bug fix, not a feature
A redeployed app invalidates its old FCM token; `send-push` currently just
logs the failed send and leaves the dead row in `device_tokens` until the
account happens to reopen the app. This should just be **correct
behavior**, not an optional upgrade: FCM's HTTP v1 API returns
`UNREGISTERED` for a dead token — `send-push` should delete that row the
moment it sees that response, the same "cleanup on a specific detected
failure" shape `cleanup-receipts` already uses on a schedule.

### Recurring expenses: real end condition
`recurring_expenses` only pauses (`is_active`) or runs forever — a
12-month gym membership or a 6-payment installment plan has no way to stop
itself. Add an `end_date` (or `max_occurrences`) column, checked inside
`materialize_recurring_expenses()`'s existing due-scan. No new job, no new
page — one more guard in a function already proven live, plus the two new
fields on `AddExpensePage`'s recurring-mode form.

### Per-group notification muting
Today's three flat channels (Expenses, Events, Anniversaries) let someone
mute a whole category everywhere, but not "just this one noisy group." The
Phase 2 notification design left this door open on purpose — a per-account,
per-group `Preferences` flag checked before each recipient computation, on
top of infra that already exists.

---

**Spending insights — shipped 2026-09-15.** Went further than this file's
original one-liner: a "Stats" tab per group (spend by category, category
usage frequency, spend by member, settle-up cadence, category trend over
time), a page-level date-range filter, a cross-group member profile screen,
and CSV/PDF export. See CHANGELOG.md's "Stats tab" entry and CLAUDE.md's
"Stats tab" section. Picked over home-screen widgets (below) specifically
because it needed zero new schema/RLS and zero Android-only infrastructure.

Explicitly dropped from the original review: multi-payer expenses, a
shared-lists third vertical, receipt OCR, debt-simplification counterparty
exclusion, the crash-safety synchronous-exception gap, a consolidated group
settings screen, and the duplicate-member cleanup (no longer relevant — the
two stray rows from before the 2026-09-07 invariant fix were already
cleaned up by hand; the merge-on-claim code path remains unexercised but
isn't an active problem).
