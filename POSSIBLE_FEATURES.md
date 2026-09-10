# Possible features & changes

Shortlist kept from a 2026-09-10 review session, trimmed down to what's
actually worth it for a group of friends/family — not a full roadmap, just
ideas worth picking up when there's time. See `CLAUDE.md`/`CHANGELOG.md`/
`SCOPE.md` for what's already shipped.

## New features

### Spending insights
A "Stats" screen per group: spend by category over time, by member, a
settle-up cadence. Pure read — every number already lives in
`expenses`/`expense_shares`, converted via `amount_in_group_currency`. No
new schema, no new RLS — just an aggregation screen (and maybe one grouped
Postgrest query for efficiency). Cheapest item on this list to ship.

### Settle-up nudges (monthly)
A monthly `pg_cron` job scans `group_balances` for a nonzero balance and
sends one gentle push per debtor — a private reminder, not a public list.
Same shape as `materialize_recurring_expenses()`/`send_event_reminders()`:
scan-and-notify on a schedule. Monthly cadence (not biweekly) avoids nagging
anyone over normal household ebb and flow.

### Home-screen widgets (Android) — two, not one
1. **Balances widget** — each group's net balance at a glance ("You owe
   Marc €12"), fed by the already-computed `my_group_balances` view.
2. **Events widget** — next upcoming event per group (or across all
   groups), fed by `events`/`event_attendees`.

Both are periodic-refresh `RemoteViews` widgets, no new backend work —
Android-only, mirroring where push already landed.

## Upgrades / fixes to existing features

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

Explicitly dropped from the original review: multi-payer expenses, a
shared-lists third vertical, receipt OCR, debt-simplification counterparty
exclusion, the crash-safety synchronous-exception gap, a consolidated group
settings screen, and the duplicate-member cleanup (no longer relevant — the
two stray rows from before the 2026-09-07 invariant fix were already
cleaned up by hand; the merge-on-claim code path remains unexercised but
isn't an active problem).
