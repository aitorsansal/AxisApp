# Multi-Currency — Implementation Plan

Written 2026-09-04, from a planning-only chat session (no code changes made yet).
Picks up from `SCOPE.md`'s "currency column reserved, no conversion logic" note —
this plan is what actually wires that up. If you're a fresh session reading this
cold: read the "Decisions locked" section below before touching schema.sql or any
Expense-related code, the design has real subtlety in the conversion-snapshot
timing. Then check the status line on each milestone and continue from the first
one not marked done.

## Progress

| # | Milestone | Status |
|---|---|---|
| 1 | Schema — currency columns, triggers, balance views | **done & verified 2026-09-04** — live migration + smoke test, all 5 checks passed |
| 2 | Edge Function — daily rate refresh (pg_cron + Frankfurter) | not started |
| 3 | App — group currency (`NewGroupPage`, `Group`/`IGroupsRepository`) | not started |
| 4 | App — per-expense currency (`AddExpensePage`, `Expense`/`RecurringExpense` models) | not started |
| 5 | App — display preference (`ProfilePage` toggle + render logic) | not started |
| 6 | `send-push` copy decision (optional, low priority) | not started |

**When you finish a milestone, update its Status cell in the table above** (e.g.
`done 2026-09-06`, or `done — see commit abc123`) before ending the session, so
the next session (possibly on a different machine) can tell at a glance what's
left. If you deviate from a milestone's plan below while implementing it, update
that milestone's text too — this file should stay a true reflection of what
exists, not just what was originally intended.

## Decisions locked in this session (don't re-litigate without new information)

- **Scope: Option B** — per-expense currency within a group, converted to the
  group's one settlement currency. (Option A, a plain per-group-only currency with
  no conversion, was considered and rejected — the actual need is mixed-currency
  expenses inside one group, e.g. a foreign-currency purchase in an otherwise-EUR
  group.)
- **`groups.currency`**: a **required selector on `NewGroupPage`**, set at the same
  insert that creates the group. **No edit path is ever exposed in the UI, ever** —
  not even before the first expense. This was a deliberate correction mid-session:
  an earlier draft of this plan said "lock after first expense," which the user
  rejected — picking the currency must be a deliberate, explicit choice at creation
  time, never something that could be perceived as falling out of whatever the
  first expense happened to use.
- **Currency list**: a single fixed list drives *both* the group-currency picker
  and the per-expense-currency picker, so nothing pickable is ever unconvertible.
  The list = whatever Frankfurter (frankfurter.dev, ECB reference rates, free, no
  API key) actually supports — **do not hand-roll a separate "curated" subset**.
  **Verified against the live `https://api.frankfurter.dev/v1/currencies`
  response on 2026-09-04** (an earlier draft of this plan had a recalled-from-
  memory list that wrongly included BGN — corrected):
  `AUD, BRL, CAD, CHF, CNY, CZK, DKK, EUR, GBP, HKD, HUF, IDR, ILS, INR, ISK,
  JPY, KRW, MXN, MYR, NOK, NZD, PHP, PLN, RON, SEK, SGD, THB, TRY, USD, ZAR`
  (30 total, EUR included). None of these are 3-decimal currencies, so the
  existing `numeric(12,2)` scale on `amount` needs no changes. Enforced as a DB
  `check` constraint on `groups.currency`/`expenses.currency`/
  `recurring_expenses.currency` (mirrors `device_tokens.platform`'s existing
  `check (platform in (...))` pattern) so a bug can never write a currency
  nothing can convert — keep `AppConstants.Currencies` (Milestone 3/4) in sync
  with this exact list if it's ever revisited.
- **Rate storage: EUR-pivot, a SINGLE-ROW table**, not one row per day. Corrected
  mid-session from an earlier draft that proposed `as_of date primary key` (one
  row per day, growing forever) — since every expense snapshots its own
  converted amount at write time, `exchange_rates` never needs history, only
  "the latest known rate" to compute a *new* snapshot. Implemented as a
  singleton (`id boolean primary key default true check (id)` — `id` can only
  ever be `true`, so at most one row can ever exist): `exchange_rates(id boolean
  primary key default true, as_of date not null, rates jsonb not null)`, `rates`
  keyed by currency code, value = units per 1 EUR (Frankfurter's native base;
  its response has no "EUR" key, so the conversion trigger treats EUR as
  implicitly 1 rather than requiring it to be injected). Converting X→Y goes
  through EUR: `amount / rates[X] * rates[Y]`. The daily Edge Function
  (Milestone 2) writes it via delete-then-insert, matching this project's
  existing "delete then insert, not upsert" idiom
  (`SupabaseDeviceTokensRepository.RegisterAsync`).
- **Missing rate → raise, don't silently assume 1:1.** If an expense's currency
  differs from its group's and `exchange_rates` has no row yet (realistic right
  after Milestone 1 ships, before Milestone 2's cron has ever run), the
  `BEFORE INSERT/UPDATE` trigger raises an exception and blocks the save, rather
  than treating it as an untracked 1:1 conversion. A blocked save is a better
  failure mode than a silently wrong number in a ledger. To test conversion
  before Milestone 2 exists, seed one row by hand (see
  `multi_currency_milestone1.sql`'s header comment for the exact insert).
- **Conversion happens server-side, via `BEFORE INSERT` triggers**, not in C#.
  Rationale: this codebase's existing convention is that any business rule which
  must produce the same result regardless of which code path inserts a row lives
  in Postgres (`leave_group`, `remove_group_member`,
  `materialize_recurring_expenses`, `notify_new_expense`) — never duplicated
  separately in `AddExpenseViewModel` vs. the recurring-expense cron job.
  Conversion is exactly that shape, so `AddExpenseViewModel` and
  `materialize_recurring_expenses()` both stay exactly as ignorant of currency
  math as they are today; they just insert `amount`/`currency` like now.
  - **Missing rate for today**: fall back to the most recent cached `as_of` row
    (`order by as_of desc limit 1`) rather than blocking the insert. A ledger app
    refusing to record an expense because the daily rate cache is a day stale
    would be worse than a very slightly stale conversion.
  - **Editing an expense's amount/currency later**: re-snapshot against *today's*
    rate — an edit is a deliberate correction, not an attempt to reconstruct
    historical accuracy.
  - When `currency == groups.currency` (the common case, and 100% of current
    real usage since everything so far has been EUR): rate = 1 exactly, no drift,
    no dependency on the rates table being fresh.
- **Balance views keep their exact current output shape.** `group_balances` /
  `pairwise_balances` / `my_group_balances` / `my_pairwise_balances` swap
  `sum(amount)` → `sum(amount_in_group_currency)` and `sum(share_amount)` →
  `sum(share_amount_in_group_currency)`, nothing else changes. This means
  `DebtSimplifier`, `GroupDetailViewModel.Settle`, and `leave_group()`'s
  balance-zero check **need zero changes** — the whole point of snapshotting at
  write time instead of converting at read time.
- **Display preference: per-device, global, not per-group.** A single toggle on
  `ProfilePage`: **"Show amounts converted to group currency"**, default **on**.
  This was a deliberate simplification mid-session — the original idea was a
  per-group preference (mirroring `BalanceDisplayModePrefix`'s shape), but the
  user preferred one blanket device setting (mirroring `LanguageOverride`/
  `AccentPreset` instead). Backed by a single `Preferences` bool key
  (`AppConstants.Preferences.AmountDisplayConverted`), no group-id in the key.
  - **On** (default): group-currency amount shown as primary on expense rows,
    original amount+currency shown secondary/smaller when it differs
    (e.g. `€10.00 (¥1600)`).
  - **Off**: original entered currency shown as primary
    (e.g. `¥1600 (≈€10.00)`), group-currency equivalent secondary.
  - **Scope limit, confirmed acceptable by the user**: this toggle only affects
    **individual expense line items** (activity feed rows, expense detail) — it
    does **not** apply to balance totals or the Settle amount. A pairwise balance
    or group net can be a sum across several expenses in *different* original
    currencies, so there's no single coherent "native" number for those; they are
    always shown/settled in the group's currency regardless of this toggle.

---

## Milestone 1 — Schema

**Status: done and verified, 2026-09-04.** `multi_currency_milestone1.sql` was
run against the live project with no errors, then smoke-tested
(`supabase/multi_currency_milestone1_smoketest.sql`, throwaway/rolled-back) —
all 5 checks passed: 100 USD → 92.59 EUR at rate 0.92592593 (1 EUR:1.08 USD),
its 50 USD share → 46.30, editing the amount to 200 USD re-snapshotted to
185.19, a same-currency EUR expense passed through exactly at rate 1, and a
mismatched-currency insert with zero cached rates correctly raised an
exception instead of silently assuming 1:1. Everything else
depends on this existing live (same "run it against Supabase before testing
anything that reads it" step every prior schema change in this repo has needed).
Two files:
- **`supabase/multi_currency_milestone1.sql`** — the one-off script to actually
  run against the live project (mirrors `merge_payments_into_expenses.sql`'s
  shape: header comment explaining the change, wrapped in one `begin;`/`commit;`).
- **`supabase/schema.sql`** — already hand-edited in place to reflect the same
  end state (original `create table` statements for `groups`/`expenses`/
  `expense_shares`/`recurring_expenses` now include the new columns/checks
  directly, `group_balances`/`pairwise_balances` already sum the
  `_in_group_currency` columns), so a fresh install matches the live project
  once the one-off script above has been run — same "keep it a clean
  fresh-install script" convention `merge_payments_into_expenses.sql` and the
  `recurring_payments` retirement both followed.

What's actually in it:
- [x] Verified the Frankfurter currency list live (see "Decisions locked" above
      — corrected from a wrong recalled-from-memory list).
- [x] `groups.currency char(3) not null default 'EUR'` + check constraint against
      the verified list.
- [x] `exchange_rates` table as a **singleton** (`id boolean primary key default
      true check (id)`, not `as_of date primary key` — corrected mid-session, see
      "Decisions locked" above), RLS enabled, `select`-to-`authenticated` policy,
      no write policy for `authenticated`/`anon`.
- [x] `expenses.amount_in_group_currency numeric(12,2) not null` +
      `expenses.exchange_rate numeric(18,8) not null default 1`, plus a check
      constraint on `expenses.currency` against the verified list. The one-off
      script adds these nullable first, backfills existing rows (1:1, since
      everything so far has been EUR), then locks `amount_in_group_currency` to
      `not null` — `schema.sql`'s version just declares them `not null` directly
      since a fresh install has no rows to backfill.
- [x] `expense_shares.share_amount_in_group_currency numeric(12,2) not null`,
      same nullable-then-backfill-then-lock treatment in the one-off script.
- [x] `snapshot_expense_currency_conversion()` — `BEFORE INSERT OR UPDATE OF
      amount, currency` trigger on `expenses`. Fast path when
      `NEW.currency = groups.currency` (rate=1, no `exchange_rates` touch at
      all — the common case, and 100% of current real usage). Otherwise reads
      the singleton `exchange_rates` row, EUR-pivots (treating EUR as implicitly
      1 rather than requiring it as an explicit key, since Frankfurter's own
      response never includes one), and **raises an exception** rather than
      assuming 1:1 if no rate exists yet or either currency is missing from the
      cached `rates` — see "Decisions locked" above.
- [x] `snapshot_expense_share_currency_conversion()` — `BEFORE INSERT OR UPDATE
      OF share_amount` trigger on `expense_shares`, reads the parent expense's
      already-computed `exchange_rate` (confirmed safe against
      `SupabaseExpensesRepository.AddAsync`/`UpdateAsync`, both of which await
      the expense insert/update before touching `expense_shares` — no ordering
      risk).
- [x] `group_balances`/`pairwise_balances` recreated (`create or replace view`)
      summing the `_in_group_currency` columns — output columns unchanged, so
      `my_group_balances`/`my_pairwise_balances`/`DebtSimplifier`/
      `GroupDetailViewModel.Settle`/`leave_group()`/`remove_group_member()`
      need no changes.
- [x] `recurring_expenses.currency` gained the same check constraint (column
      itself already existed, unused).
- [x] Ran `multi_currency_milestone1.sql` against the live project — no errors.
- [x] Smoke-tested (`multi_currency_milestone1_smoketest.sql`) — all 5 checks
      passed exactly. Along the way, learned/confirmed a real Postgres gotcha
      worth remembering for future multi-statement test scripts in this repo:
      sibling data-modifying CTEs in one `WITH` clause share a single snapshot
      and cannot see each other's writes to the same target table (an `UPDATE`
      CTE depending on a sibling `INSERT` CTE's just-created row silently
      matches zero rows) — sequential statements in a `DO` block don't have
      this problem.
- [ ] **Not done yet**: `materialize_recurring_expenses()`'s insert shape
      hasn't been re-confirmed against the new triggers directly (should be
      fine — plain sequential SQL inserts into `expenses`/`expense_shares`,
      same as any other caller, and the smoke test already proved the trigger
      mechanics work — but not yet exercised via that actual function).

## Milestone 2 — Daily rate refresh

**Status: not started.** Depends on Milestone 1's `exchange_rates` table existing.
Functionally non-blocking for Milestones 3/4 to be *coded* (the trigger's
stale-rate fallback means the app doesn't hard-depend on this running yet), but
needed before real conversion testing has any rates to work with.

- [ ] New `supabase/functions/fetch-exchange-rates/index.ts` — calls Frankfurter's
      latest-rates endpoint (base EUR, all symbols), writes the singleton
      `exchange_rates` row via delete-then-insert (not upsert — matches
      `SupabaseDeviceTokensRepository.RegisterAsync`'s existing idiom for
      enforcing uniqueness without trusting an `ON CONFLICT` target). No API key
      needed.
- [ ] `pg_cron` job, similar shape to `materialize-recurring-expenses`'s daily
      8am UTC job — pick a time (doesn't need to be early; nothing in the app
      blocks on today's rate being ready the instant the day starts, thanks to the
      stale-rate fallback in the trigger).
- [ ] Deploy via the dashboard's "Via Editor" flow, same as every other Edge
      Function in this project (`send-push`, `cleanup-receipts`,
      `delete-account`) — no Supabase CLI project link exists in this repo.
- [ ] Manually invoke once after creating the cron job (same verification style
      `materialize_recurring_expenses`/`cleanup-receipts` got) to confirm a real
      row lands in `exchange_rates` before relying on it.

## Milestone 3 — Group currency (app side)

**Status: not started.** Depends on Milestone 1 (needs `groups.currency` to exist
and the insert-time trigger to be in place before this is testable end to end).

- [ ] `AppConstants.Currencies` — the fixed 30-entry list (from the verified
      Frankfurter set, see "Decisions locked" above), each with a display
      label/symbol, shared by this milestone and Milestone 4's per-expense
      picker. Must match the DB check constraints exactly.
- [ ] `Models/Group.cs` gains `Currency`.
- [ ] `IGroupsRepository.CreateAsync` gains a currency parameter.
- [ ] `NewGroupPage`/`NewGroupViewModel`: required currency selector, no default
      pre-selection that could be mistaken for "just click through" — requiring a
      deliberate pick reinforces the "explicit, not accidental" design goal from
      the locked decisions above.
- [ ] Confirm end to end: create a group with a non-EUR currency, confirm
      `groups.currency` is set correctly and no UI path exists to change it after.

## Milestone 4 — Per-expense currency (app side)

**Status: not started.** Depends on Milestone 1. Independent of Milestone 3 except
for sharing `AppConstants.Currencies` — could be built in either order relative to
Milestone 3, but Milestone 3 is listed first since a group needs a currency before
a "does this expense's currency differ from the group's" picker means anything.

- [ ] `Models/Expense.cs` / `Models/RecurringExpense.cs`: add
      `AmountInGroupCurrency`/`ExchangeRate` as read-only-from-the-app fields
      (populated by the trigger, never sent on insert/update — same
      `[JsonIgnore]`-on-computed-property treatment `Member.IsPhantom` already
      needed, or simply omit them from the insert payload's column list the way
      `CreatedBy`/`CreatedAt` already are for a fresh insert).
- [ ] `AddExpensePage`/`AddExpenseViewModel`: per-expense currency picker,
      defaulting to the group's currency, only meaningfully different when the
      user actively changes it.
- [ ] Confirm end to end: add an expense in a currency different from its group's,
      confirm `amount_in_group_currency`/`exchange_rate` land correctly, and that
      editing that expense's amount later re-snapshots the rate.

## Milestone 5 — Display preference (app side)

**Status: not started.** Depends on Milestone 4 (needs `AmountInGroupCurrency` on
the `Expense` model to have something to branch on).

- [ ] `ProfilePage`/`ProfileViewModel`: new toggle row, backed by
      `AppConstants.Preferences.AmountDisplayConverted` (bool, default true).
- [ ] Wherever an expense amount is currently rendered (Group Detail's recent
      activity, expense detail/edit read state, anywhere else `AmountText`-style
      formatting happens) — branch on the new preference to decide which of
      original vs. group-currency is primary vs. secondary. Balance/Settle display
      code paths are explicitly **not** touched by this — they already render
      `group_balances`/`pairwise_balances` output, which is always in group
      currency.
- [ ] Confirm end to end: toggle on/off on a group with a mixed-currency expense,
      confirm the primary/secondary amounts swap correctly, and confirm balance/
      Settle screens are unaffected by the toggle either way.

## Milestone 6 — `send-push` copy (optional)

**Status: not started, low priority.**

- [ ] `send-push/index.ts` currently reads `expenses.amount`/`currency` for
      notification copy; decide whether to leave as-is (shows the expense's own
      currency, arguably always correct for "what did they spend") or also surface
      the group-currency equivalent. Not blocking anything else in this plan.

---

## Explicitly deferred (not in this pass)

- Per-viewer personal *currency* preference (e.g. view a EUR group's balances in
  USD) — different from the native/converted toggle above; this would need real
  currency-conversion math applied to already-converted balance totals, no schema
  impact, good Phase 2 candidate.
