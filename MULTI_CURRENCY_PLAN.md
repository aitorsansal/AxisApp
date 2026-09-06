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
| 2 | Edge Function — daily rate refresh (pg_cron + Frankfurter) | **done & verified 2026-09-05** — deployed, cron job live, manual invoke confirmed a real row |
| 3 | App — group currency (`NewGroupPage`, `Group`/`IGroupsRepository`) | code done & DB migration verified 2026-09-05, **not yet manually tested end to end in the running app** |
| 4 | App — per-expense currency (`AddExpensePage`, `Expense`/`RecurringExpense` models) | **done 2026-09-06** — manually verified, plus a UI feedback pass (inline currency picker, correct symbols) |
| 5 | App — display preference (`ProfilePage` toggle + render logic) | **done & verified 2026-09-06** |
| 6 | `send-push` copy decision (optional, low priority) | **done 2026-09-06** — decided: leave as-is |

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

**Status: done and verified, 2026-09-05.** Deployed via the dashboard's "Via
Editor" flow (driven through the Claude in Chrome browser extension —
navigated to the Edge Functions page, set the Monaco editor's contents
directly via a `window.monaco` JS call rather than typing, since typing raw
code with braces/quotes into Monaco risks autoclose-bracket corruption; same
approach used for the SQL editor). `multi_currency_milestone2.sql` was then
run against the live project (`cron.schedule` returned job id 4), and a
manual invoke of the deployed function (the exact `net.http_post` call from
that file's trailing comment) returned `200` with
`{"as_of":"2026-09-04","currencies_cached":29}` — confirmed a real row landed
in `exchange_rates` (`id=true`, `as_of=2026-09-04`, real currency keys like
AUD/BRL/CAD/CHF/CNY). 29 cached currencies (not 30) is correct — the fixed
list includes EUR, which Frankfurter's own response never returns a key for
(see "Decisions locked" above).

- [x] `supabase/functions/fetch-exchange-rates/index.ts` — calls Frankfurter's
      `/v1/latest?base=EUR` endpoint (all symbols, no API key needed), writes the
      singleton `exchange_rates` row via delete-then-insert (not upsert — matches
      `SupabaseDeviceTokensRepository.RegisterAsync`'s existing idiom for
      enforcing uniqueness without trusting an `ON CONFLICT` target). Same
      response shape assumed by `snapshot_expense_currency_conversion()`: EUR-
      pivoted, no `"EUR"` key in `rates`.
- [x] `pg_cron` job (`fetch-exchange-rates`, `schema.sql`) — daily **6am UTC**,
      deliberately before `materialize-recurring-expenses`'s 8am run so a
      same-morning recurring materialization in a foreign currency has a
      same-day rate rather than falling back to yesterday's cache. Same
      `net.http_post` + Vault `service_role_key` pattern `cleanup-receipts`
      already uses — added directly into `schema.sql` (fresh-install source of
      truth) plus a standalone one-off `supabase/multi_currency_milestone2.sql`
      to run against the already-live project (mirrors
      `multi_currency_milestone1.sql`'s "one-off script + schema.sql both
      updated" shape).
- [x] Deployed `fetch-exchange-rates` via the dashboard's "Via Editor" flow
      (same as `send-push`/`cleanup-receipts`/`delete-account`).
- [x] Ran `multi_currency_milestone2.sql` against the live project — cron job
      `fetch-exchange-rates` created (job id 4, `0 6 * * *`).
- [x] Manually invoked once — `200`,
      `{"as_of":"2026-09-04","currencies_cached":29}`, confirmed a real row in
      `exchange_rates`.

## Milestone 3 — Group currency (app side)

**Status: code done and DB migration verified live, 2026-09-05 — not yet
manually tested end to end in the running app** (this project has no
automated UI test suite; verification is always manual, see CLAUDE.md).
Depends on Milestone 1 (needs `groups.currency` to exist and the insert-time
trigger to be in place), which is already live.

- [x] `AppConstants.Currencies.All` — the fixed 30-entry `(Code, Symbol)`
      list (from the verified Frankfurter set, see "Decisions locked" above),
      shared by this milestone and Milestone 4's per-expense picker. Symbol
      is a plain display convenience, deliberately not localized per-language
      (a currency code is already an international standard).
- [x] `Models/Group.cs` gains `Currency` (`[Column("currency")]`, default
      `"EUR"` client-side only — the DB has no default once explicitly
      required via `create_group`).
- [x] `IGroupsRepository.CreateAsync(string name, string currency)` /
      `SupabaseGroupsRepository` — passes `p_currency` to the `create_group`
      RPC.
- [x] **Schema addition beyond Milestone 1's original scope**: `create_group()`
      only ever took `p_name` — needed a `p_currency char(3)` parameter added
      (required, no default) to actually write a chosen currency atomically
      with the group/member/group_members inserts. Since Postgres treats a
      changed parameter list as a new overload rather than replacing the old
      one, the old 1-arg `create_group(text)` had to be dropped explicitly —
      done via `supabase/multi_currency_milestone3.sql`, run against the live
      project and confirmed via `pg_get_function_arguments`: exactly one
      `create_group` overload now exists, `(p_name text, p_currency
      character)`. `schema.sql` updated in place to match.
- [x] `NewGroupPage`/`NewGroupViewModel`: a `Picker` (not the chip-row pattern
      `AddExpensePage` uses for category/frequency/payer — 30 items would be
      unusable as horizontal chips) bound to `CurrencyOptions`
      (`"USD ($)"`-style display strings) / `SelectedCurrencyIndex`, which
      starts at `-1` (Picker's own default) so nothing is pre-selected —
      `Create` blocks with `NewGroup_SelectCurrency` if no pick was made,
      same shape as the existing empty-name guard. New loc keys
      (`NewGroup_Currency`/`NewGroup_CurrencyPlaceholder`/
      `NewGroup_CurrencyHint`/`NewGroup_SelectCurrency`, en/es).
- [x] Build-verified clean on both `net10.0-windows10.0.19041.0` and
      `net10.0-android` (0 errors, only pre-existing warnings).
- [ ] **Not done yet**: manually create a group with a non-EUR currency in the
      running app, confirm `groups.currency` lands correctly and no UI path
      exists to change it after.

## Milestone 4 — Per-expense currency (app side)

**Status: code done 2026-09-06, builds clean on both `net10.0-windows10.0.19041.0`
and `net10.0-android` (0 errors) — not yet manually tested end to end in the
running app.** Depends on Milestone 1 (already live). Both `Expense`/
`RecurringExpense.Currency` and `groups.currency` already existed before this
milestone (Milestone 1/3), so this was purely app-side wiring, no new SQL.

- [x] **Scope correction found while implementing**: only `Expense` actually has
      `amount_in_group_currency`/`exchange_rate` columns — `recurring_expenses`
      has neither (conversion only happens once a template *materializes* into a
      real `expenses` row via `materialize_recurring_expenses()`, which is a
      plain insert that fires the same trigger like any other caller). The
      original milestone text calling for these fields on `RecurringExpense` too
      was wrong; only `Models/Expense.cs` gained them.
- [x] `Models/Expense.cs` gained `AmountInGroupCurrency`/`ExchangeRate` — plain
      `[Column]` properties, not `[JsonIgnore]`-hidden, since
      `snapshot_expense_currency_conversion()` (schema.sql) overwrites both
      unconditionally in every branch (own-currency fast path, unscoped-group
      fallback, and the real conversion path) before the row's `not null`
      constraints are even checked — whatever value the app sends is ignored
      server-side, so there was no need to strip them from the insert payload
      the way `CreatedBy`/`CreatedAt` need explicit carry-through on edit.
- [x] `AddExpenseViewModel` gained `CurrencyOptions`/`SelectedCurrencyIndex`/
      `CurrencySymbol`, same `"USD ($)"`-display-string shape as
      `NewGroupViewModel`'s picker. `SetCurrencyByCode(code)` selects the
      matching index — called with the group's own currency (fetched via
      `IGroupsRepository.GetByIdAsync`, added to `LoadAsync`'s existing
      `Task.WhenAll` batch) on a fresh add, or with the loaded
      `Expense.Currency`/`RecurringExpense.Currency` when editing either. `Save()`
      reads the selected code back onto `Expense.Currency`/
      `RecurringExpense.Currency` (falls back to `"EUR"` if somehow nothing's
      selected, mirroring the picker's own unselected-state default).
- [x] `AddExpensePage.xaml`: the amount hero's hardcoded `"€"` prefix `Label` now
      binds to `CurrencySymbol` instead, and a new "Currency" `Picker` section
      (same bordered shape as `NewGroupPage`'s) sits right after the Description
      field — shown unconditionally (unlike Category/receipt, currency applies to
      settlements and recurring templates too, both of which have a real
      `currency` column). New `AddExpense_Currency` loc key (en/es).
- [x] **UI feedback from the first manual test, fixed same session (2026-09-06)**:
      the picker worked and saved correctly, but two real display bugs surfaced —
      - **Recent Activity showed `€180` for a real ¥180 expense.** Root cause:
        every amount-rendering ViewModel (`GroupDetailViewModel`'s activity feed
        *and* both balance modes, `GroupsViewModel`'s group-list balance,
        `RecurringExpensesViewModel`'s template list) hardcoded a literal `"€"`
        prefix — harmless while every group was EUR, a real bug the moment
        Milestone 3 let a group pick anything else. Fixed by adding
        `AppConstants.Currencies.SymbolFor(code)` and using it everywhere instead
        of the literal, plus switching `ActivityItem.AmountText` to read
        `expense.AmountInGroupCurrency` (the converted amount) rather than the
        raw `expense.Amount` (the original-currency amount) — this alone is
        Milestone 5's "On" default behavior (group-currency amount as primary),
        implemented now rather than waiting, since the bug made it clearly
        needed. Also added `ActivityItem.SecondaryAmountText` — the expense's own
        original amount+currency in parens (e.g. `(¥180.00)`), shown smaller
        under the primary amount only when `expense.Currency != groups.currency`.
        **The ProfilePage on/off toggle itself is still Milestone 5's own scope,
        not built here** — this fix hardcodes the "On" behavior with no way to
        switch to "Off" yet.
      - **UI feedback: move the currency picker inline next to the amount entry**
        instead of a separate full-width row — the standalone "Currency" section
        (added in the first Milestone 4 pass) felt disconnected from the amount
        it describes. Fixed: the amount hero's `Picker` now sits where a static
        `"€"` prefix label used to be, bound to a plain currency-code list
        (`CurrencyOptions` narrowed from `"USD ($)"` to just `"USD"` — short
        enough to fit inline). The standalone Currency row and its now-unused
        `AddExpense_Currency` loc keys were removed.
      - **Further UI feedback (still 2026-09-06)**: the inline picker only
        showed the bare code ("USD"); user asked for the symbol too. Changed
        `CurrencyOptions` to `"{Code} {Symbol}"` (e.g. "USD $", "JPY ¥") —
        still short enough to sit inline, unlike NewGroupViewModel's parenthesized
        "USD ($)" form.
- [x] **Manually confirmed end to end in the running app** — added a real
      non-EUR (yen) expense, the row saved and displayed correctly, and the
      currency picker/symbol UI feedback above was addressed in the same pass.

## Milestone 5 — Display preference (app side)

**Status: done & verified 2026-09-06.** The "On" (default) rendering behavior was already
implemented as part of Milestone 4's bugfix pass, since a real display bug
(Recent Activity showing the wrong currency symbol) forced the question early;
this milestone added the actual toggle plus the "Off" branch that was still
missing.

- [x] `ProfilePage`/`ProfileViewModel`: new "Amount display" section (a
      `Switch` row, "Show converted to group currency", plus a hint label
      clarifying the balance/Settle scope limit below), backed by
      `AppConstants.Preferences.AmountDisplayConverted` (bool, default true).
      Persisted immediately on toggle via a `partial void
      OnAmountDisplayConvertedChanged` — no explicit Save button, same
      "device setting, not part of the profile form" treatment
      `BalanceDisplayModePrefix`/`LanguageOverride`/`AccentPreset` already get.
      New `Profile_AmountDisplay`/`Profile_AmountDisplayConverted`/
      `Profile_AmountDisplayHint` loc keys (en/es).
- [x] `GroupDetailViewModel.LoadAsync` reads the preference once per load and
      passes it into a new `FormatExpenseAmount(expense, groupSymbol,
      groupCurrency, showConverted)` helper that replaced the inline
      always-"On" logic from Milestone 4's bugfix pass. Same-currency expenses
      short-circuit to a plain group-currency amount with no secondary line
      either way (nothing extra to show when both amounts are identical).
      Different-currency expenses: **On** shows the converted amount primary
      + `(¥1600.00)`-style original secondary (unchanged from the bugfix
      pass); **Off** flips it — original amount primary, `(≈€10.00)`-style
      converted secondary (the `≈` distinguishes "this is computed" from the
      literal amount actually paid). Balance/Settle code paths
      (`BuildPairwiseItemsAsync`/`BuildSimplifiedItems`/`GroupsViewModel
      .ApplyBalance`) are untouched by this milestone, per the plan's original
      scope limit — they already always render in the group's own currency
      symbol (fixed in Milestone 4's bugfix pass) regardless of this toggle.
- [x] Manually confirmed end to end — the toggle and both display modes work
      correctly in the running app.

## Milestone 6 — `send-push` copy (optional)

**Status: done 2026-09-06 — decided, no code change.** `send-push/index.ts`
keeps showing the expense's own entered amount/currency (e.g. "¥1600"), not a
group-currency conversion. Reasoning: a push notification is describing "what
did they spend" — the original currency is the more meaningful number there,
and cramming a second converted amount into the body would make it noisier
for the common (same-currency) case without adding much; anyone who wants the
converted total can open the app.

---

## Explicitly deferred (not in this pass)

- Per-viewer personal *currency* preference (e.g. view a EUR group's balances in
  USD) — different from the native/converted toggle above; this would need real
  currency-conversion math applied to already-converted balance totals, no schema
  impact, good Phase 2 candidate.
