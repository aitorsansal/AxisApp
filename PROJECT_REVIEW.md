# Axis — Full Project Review

**Date:** 2026-09-09
**Reviewer:** Claude (Sonnet 5), reading the repository directly — no prior review content reused
**Scope note:** Per instructions, the only Markdown file read was `CLAUDE.md`; the only SQL file
read was `supabase/schema.sql` (not the one-off migration scripts under `supabase/*.sql`). Every
other file referenced below — `.cs`, `.xaml`, `.ts`, `.tsx`, `.json`, `.csproj`, `.html` — was read
directly from the working tree on branch `claude/project-review-features-kgdc3f` at commit
`6c3c0c0`. `EVENTS_PLAN.md`, `MULTI_CURRENCY_PLAN.md`, `REDESIGN_PLAN.md`, `README.md`, and
`SCOPE.md` exist and are sizeable (15–47 KB each) but were **not read**, per instructions — any
statement below about the *code's* current behavior is grounded in the code and `schema.sql`
themselves, not in those plan documents.

---

## 1. What Axis actually is, right now

Axis is a .NET MAUI (net10.0-android + net10.0-windows10.0.19041.0) shared-ledger app backed by a
single real Supabase project (Postgres + Auth + Storage + Edge Functions + pg_cron), plus two
separate web surfaces: a static Cloudflare-Worker site (`web/`) for invite links, legal pages,
password reset, and a lightweight React SPA (`webapp/`) for browser-only friends who can't install
the Android app or don't have a Windows machine.

It is **not** just an expense splitter anymore. As of this commit it's a two-vertical friends/family
app:

1. **Shared expenses** — N-way split bills, settle-up, recurring templates, receipts, multi-currency.
2. **Events & calendar** — group events with RSVP, tri-state car-pooling, auto-generated birthday
   events, and its own push-notification/reminder pipeline.

Both verticals share one `GroupDetailPage` with a tab switch (`GroupDetailViewModel.IsEventsTabSelected`
delegating to `GroupExpensesViewModel`/`GroupEventsViewModel`), one members/invite system, one
push-notification transport, and one localization/theming layer.

### 1.1 The documentation is out of date — and by a non-trivial amount

This is the first and most concrete finding, and worth stating plainly rather than softening: **`CLAUDE.md`
does not describe roughly the last two days of shipped work.** It was last touched at commit `de66f3b`
(2026-09-07). `HEAD` is `6c3c0c0` (2026-09-09). The diff between those two points is 36 files and
~1,370 inserted lines, and it is not small stuff — it includes two entire features that get zero
mention anywhere in `CLAUDE.md`:

- **Multi-currency** (`58a2426`, `2233d08`): per-expense currency, a `groups.currency` lock at
  creation, an `exchange_rates` singleton table, two currency-conversion triggers, a
  `fetch-exchange-rates` Edge Function on a daily cron, and a whole `MULTI_CURRENCY_PLAN.md` design
  doc — none of it in `CLAUDE.md`.
- **Events & Calendar / Phase 2** (`fb6a321`, `ffff5eb`): `events`/`event_attendees` tables, RSVP,
  tri-state carpooling with a seats-offered/needed aggregate, a whole second push-notification
  pipeline (created/changed/cancelled/reminder), and **auto-generated birthday events** materialized
  by their own daily cron — again, zero mention in `CLAUDE.md`.
- Also undocumented there: the "UI juice pass" (`8d8f688` — press/selection animations, animated
  numeric labels, skeleton loading states, a Debug/Release Android signing fix), the update-available
  banner (`6c3c0c0`), and `AppShell`/`GroupDetailViewModel` being split into a tabbed
  parent + two child ViewModels.

`CLAUDE.md` is written as a running engineering journal and is *extremely* good at what it does — the
existing entries are candid, dated, and cross-referenced, and several real bugs are documented with
enough forensic detail to be genuinely useful six months later. That makes the gap more costly, not
less: anyone (or any agent) reading `CLAUDE.md` today to understand "what exists" will conclude the
app is a debt tracker with no calendar and no currency support, which is wrong. The project's own
stated practice (`schema.sql`'s comments constantly say "see CLAUDE.md's remarks") depends on that
file staying current. **Recommendation: catch `CLAUDE.md` up before starting the next feature**, the
same way commit `8d992ce` ("Catch up CLAUDE.md/SCOPE.md with what's actually been built") already
did once before.

A smaller instance of the same pattern: two pre-existing HTML review reports already sit in the repo
root — `ui-review-report.html` and `live-ui-walkthrough-report.html`. Both date to commit `4eb9221`
(2026-08-25), **before** the redesign (`cb5fdd6`) and the UI juice pass (`8d8f688`). I spot-checked
two of their findings against current XAML: `LoginPage.xaml` now has the `ScrollView` +
`IsEnabled="{Binding IsBusy, ...}"` guards the report said were missing (both present, lines 13–14
and throughout), and `GroupsPage.xaml`'s "Join with code / + New group look ugly together" issue is
gone because that button pair no longer exists in that shape (`JoinGroupCommand` is now a plain text
link, not a competing outline button next to the FAB). Both old reports are stale artifacts from a
pre-redesign UI and should either be deleted or clearly marked superseded — as they stand they will
mislead the next person who finds them into re-litigating already-fixed issues.

---

## 2. Architecture — this part is genuinely solid

The backend-abstraction rule (`Services/I*Repository` interfaces, ViewModels never touch
`Supabase.Client` directly) is followed with no exceptions I found. Every repository takes the one
shared `Supabase.Client` singleton registered in `MauiProgram.cs`, so there's exactly one
authenticated session object in the whole app — a real, deliberate fix for the "every service opens
its own unauthenticated client" class of bug.

The MVVM layer is consistent CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`)
end to end, and — this is the standout piece of the whole codebase — **every single async command
runs through `BaseViewModel.RunSafeAsync`** (`ViewModels/BaseViewModel.cs:24`). That one method is
doing a lot of quiet work: it turns what would otherwise be process-fatal unhandled exceptions
(confirmed in `CLAUDE.md`'s own history — a bare `AsyncRelayCommand` exception fail-fasts WinUI with
`0xc000027b`) into a plain `ErrorMessage` string, and it also transparently retries exactly once on
the recurring Supabase clock-skew rejection (`PGRST303`) rather than surfacing it as a scary raw
error. This is a well-reasoned, minimal solution to a real crash class, not over-engineering.

The `GroupDetailViewModel` → `GroupExpensesViewModel` + `GroupEventsViewModel` split (done when
Events landed) is the right call architecturally — the alternative was one ballooning "god ViewModel"
covering two unrelated verticals. The tradeoff the code explicitly accepts (`GroupExpensesViewModel.cs:70-74`,
`GroupEventsViewModel.cs:79-83`) — both tabs independently re-fetch group/members/aliases rather than
sharing the parent's fetch — is a reasonable, self-aware simplification, not an oversight.

### 2.1 Real performance smell: N+1 query patterns, three separate places

This is a genuine finding, not a style nitpick, and it will get worse as data grows rather than
better:

- **`GroupExpensesViewModel.RefreshActivityAsync`** (`ViewModels/GroupExpensesViewModel.cs:182-227`)
  calls `expensesRepository.GetSharesAsync(expense.Id)` inside a `foreach` over every expense in the
  group — one round trip per expense, sequential, every time Group Detail's Expenses tab loads.
- **`GroupEventsViewModel.LoadAsync`** (`ViewModels/GroupEventsViewModel.cs:148-151`) does the
  identical thing for attendees: one `GetAttendeesAsync(ev.Id)` round trip per event, in a loop.
- **`GroupsViewModel.LoadAsync`** (`ViewModels/GroupsViewModel.cs:90-105`) does it a third time for
  the groups list: one `GetForGroupAsync(group.Id)` round trip per group just to build the avatar
  stack.

None of these are broken — they're correct — but a group with, say, 150 expenses in its history now
means 150 sequential HTTP round trips just to render "Recent activity," every single time that tab
loads. Postgrest supports filtering with an `in` operator and embedded/nested selects
(`expenses?select=*,expense_shares(*)`), either of which would collapse each of these into one
request. This is the single most concrete "will bite you later" finding in the whole review — it's
invisible today because nobody's test group has 150 expenses yet, and it will be a real, user-visible
slowdown the day one does.

### 2.2 `Update(model)` sends the whole row — a documented, load-bearing footgun

Several places explicitly carry forward fields a fresh model object would otherwise silently reset —
`AddExpenseViewModel` stashing `editingCreatedBy`/`editingCreatedAt`/`editingIsSettlement` before
calling `UpdateAsync` (`ViewModels/AddExpenseViewModel.cs:101-104, 630-633`), `ProfileViewModel.SaveProfile`
mutating the already-loaded `myMember` in place rather than building a new one
(`ViewModels/ProfileViewModel.cs:147-149`). This pattern is consistent and well-commented everywhere
I checked, but it is entirely convention-enforced — nothing in the type system stops the next new
ViewModel from constructing a fresh model and calling `UpdateAsync` on it, silently zeroing
`CreatedBy`/`CreatedAt` the way `CLAUDE.md`'s own history says already happened once for real
(the `expenses_created_by_fkey` violation bug). This is worth a one-line note the next time a new
edit flow is added, not a redesign — the current shape (whole-row `Update`, no server-side partial
patch) is a reasonable choice for a Postgrest-backed app of this size.

---

## 3. Backend (`supabase/schema.sql`) — 2,413 lines, read in full

This is a well-run Postgres/RLS schema for a project this size, with a level of self-documentation
(inline comments explaining *why*, including two genuine `42P17` RLS-recursion postmortems) well
above what most side projects bother with. Specific things worth calling out:

- **The core design decision (members vs. accounts, phantom vs. claimed) is applied consistently.**
  Every ledger table points at `members`, never `auth.users`, and the one-account-one-member
  invariant fix (`create_group()`/`redeem_invite()` both reuse an existing `members` row instead of
  minting a new one) is a real, previously-shipped bug fix, not a hypothetical concern — it was found
  because real friends using the app hit it (`display_name`/`birth_date` edits "not sticking").
- **`SECURITY DEFINER` is used narrowly and each instance is justified in a comment** — either a
  genuine permission gap (the `vault` schema reads in `notify_new_expense`/`notify_new_event`, which
  fixed a real `permission denied for schema vault` error in production) or RLS-recursion avoidance
  (`is_group_member`, `is_own_member_row`, `is_unscoped_expense_party`). I did not find a
  `SECURITY DEFINER` function without a stated reason, which is the discipline the file's own header
  comments call for.
- **The multi-currency conversion trigger fails closed, not open** (`snapshot_expense_currency_conversion`,
  `schema.sql:445-490`): if `exchange_rates` has no row yet, it `raise exception`s rather than silently
  treating the rate as 1:1. That's the right call for a ledger — a blocked save beats a silently wrong
  balance — but it does mean the *very first* foreign-currency expense on a brand-new deployment will
  hard-fail until `fetch-exchange-rates` has run at least once. Worth a note in setup docs, not a code
  fix.
- **Birthday events are a genuinely clever reuse of existing infrastructure** rather than a parallel
  system: `events.is_birthday` + `events.member_id`, materialized daily by `materialize_birthday_events()`,
  which correctly clamps Feb 29 birthdays to the target month's real last day (`least(v_day,
  v_last_day_of_month)`, `schema.sql:2313-2319`) instead of crashing on `make_date`, and deliberately
  leaves `created_by` null specifically so nobody gets a working delete button that the next day's
  cron run would just silently recreate anyway (`schema.sql:2213-2223` explains this reasoning inline
  — a genuinely subtle design call, correctly made).
- **One soft spot flagged in the schema's own comments and worth repeating here**: the
  `"update events in your groups"` RLS policy allows *any* group member to edit *any* event
  (`schema.sql:1890-1891`), including someone else's birthday-event row, because the app simply never
  exposes an edit entry point for `is_birthday` events (`GroupEventsViewModel.OpenEvent`'s guard,
  `ViewModels/GroupEventsViewModel.cs:248-251`) rather than the database enforcing it. That's an
  honest, explicitly-flagged soft protection ("a soft protection, not a hard one" — the schema's own
  words), fine for a closed friends-and-family app, but it would need a real DB-level guard before
  this app ever had a less-trusted membership model.
- **Every cron-only / trigger-only function is correctly revoked from `anon`/`authenticated`**
  (`materialize_recurring_expenses`, `find_expired_receipts`, `expense_notification_recipients`,
  every `event_*_notification_recipients`, `send_event_reminders`, `materialize_birthday_events`,
  `send_birthday_notifications`) — I checked every one, and none of them are reachable via
  PostgREST's `/rpc/` by an ordinary signed-in user. This is the kind of thing that's easy to forget
  once and hard to notice you forgot, and it wasn't forgotten anywhere.
- **`allowed_signup_emails`** is a sensible stopgap for a pre-launch app with a public web URL, and is
  self-aware about being temporary (its own comment: "not the app's long-term intended shape"). Worth
  flagging only because the table currently has exactly one seeded row (the project owner's own
  email) — every friend who's supposed to use the web app needs a manual `insert` first, which is
  easy to forget when handing someone a signup link.

### 3.1 Things that look like currently-live risk, not just design notes

- **The live Supabase project ref (`foepkovwmwyygulbdahv.supabase.co`) is hardcoded in ten separate
  `cron.schedule(...)`/trigger bodies across `schema.sql`.** The file's own comments correctly note
  this isn't a secret (it's protected by RLS + keys, same reasoning as the committed
  `google-services.json`), but it does mean **`schema.sql` is not actually a portable "run this
  against a fresh project" script anymore**, despite its header comment still saying exactly that
  ("Run this once against a fresh Supabase project's SQL editor"). Ten `net.http_post` URLs would need
  hand-editing before this file could stand up a second environment (staging, a fork, a fresh
  install). Worth either a `\set` -style placeholder convention or just an explicit note at the top
  that the cron blocks are project-specific and must be edited before a fresh deploy.
- **`schema.sql` itself is a single 2,413-line file that has been hand-edited in place for months**
  (the "Merge payments into expenses" section literally describes deleting a table's worth of
  create-statements after the live migration already ran). That's a defensible choice for a
  single-maintainer project — it keeps one canonical fresh-install script instead of an ever-growing
  migrations folder — but it means the file is no longer a reliable *history*, only a reliable
  *current state*; the one-off `.sql` files alongside it (`merge_payments_into_expenses.sql`,
  `one_account_one_member_fix.sql`, etc., which this review didn't read per instructions) are the only
  record of *how* it got there. Fine as-is, just worth knowing which of the two files is "the truth"
  before touching either.

---

## 4. Cross-client feature parity — a real, growing gap

There are now **three** Axis clients: the MAUI app (Android + Windows), the static `web/` site
(invite/legal/reset pages only, not a real client), and the React `webapp/` SPA. Comparing
`webapp/src/App.tsx`'s route table against the MAUI app's `AppConstants.Routes`:

| Capability | MAUI app | `webapp/` (React) |
|---|---|---|
| Sign in (email + Google) | ✅ | ✅ |
| Groups list, new group, join by code | ✅ | ✅ |
| Group detail — balances (simplified + pairwise) | ✅ | ✅ (pairwise via `my_pairwise_balances`) |
| Add expense — equal split | ✅ | ✅ |
| Add expense — **manual/custom split** | ✅ | ❌ (equal-split only) |
| **Edit or delete an existing expense** | ✅ | ❌ (no route at all) |
| Settle up | ✅ | ✅ |
| **Recurring expenses** | ✅ | ❌ (no route) |
| **Multi-currency** | ✅ (full picker, conversion, display toggle) | Partial — `currency` referenced in `NewGroupPage.tsx`/`AddExpensePage.tsx`, but no dedicated route/UI comparable to the native picker |
| **Receipts** | ✅ | ❌ |
| **Events & calendar, RSVP, carpooling, birthdays** | ✅ (entire second vertical) | ❌ (no route, no mention anywhere in `webapp/src`) |
| **Push notifications** | ✅ (Android) | ❌ (no web-push equivalent) |
| Members roster / add-by-name / alias | ✅ (`MembersPage`) | ❌ (no members route in `App.tsx`) |
| Profile (name, birthday, avatar, language, email/password, delete account) | ✅ | ✅ |

This isn't a criticism of the web app's original scope decision — `webapp/`'s own commit history and
the "lightweight browser client" framing make clear it was deliberately narrower from day one, built
so one iPhone-only friend could use *something*. But the gap has grown materially wider since that
decision was made: **Events & Calendar is now an entire second product vertical that doesn't exist on
web at all**, and recurring expenses / receipts / member management don't either. Anyone using the
web app is missing half of what the app now does, with no in-app indication of that (no "this feature
isn't available on web yet" messaging anywhere I found). Worth a deliberate decision — either commit
to closing the gap incrementally, or add explicit "not available on web" messaging so it reads as a
known limitation rather than a broken feature when a web user goes looking for it.

---

## 5. Setup, tooling, and process gaps

- **No automated tests anywhere in the repository.** `CLAUDE.md` says this outright and it's
  accurate — no `*.Tests.csproj`, no xunit/nunit/mstest package reference, nothing under `webapp/`
  beyond `oxlint` (a linter, not a test runner). Every "confirmed working end to end" note throughout
  `CLAUDE.md`'s history is a manual verification pass, not a regression test. For a solo/small-team
  project at this stage that's a defensible tradeoff, but it does mean every one of the real, subtle
  bugs already documented in `CLAUDE.md` (the RLS-recursion pair, the `payment_net` sign inversion,
  the invite-expiry-defaulting-to-`0001-01-01` bug) had exactly one thing standing between it and
  production: a human manually trying the exact right sequence of actions. None of them would be
  caught automatically if they regressed.
- **No CI/CD.** `.github/**` doesn't exist — I globbed for it and got zero results. There is no
  automated `dotnet build` gate on push/PR, no lint gate on `webapp/`, nothing. Combined with "no
  tests," this means the *only* verification any change gets before landing on `main` is whatever the
  person (or agent) making the change happens to run locally. Given how much of `CLAUDE.md`'s history
  is "confirmed via a real local build, not docs" — the project clearly already values real
  verification — codifying even a minimal `dotnet build` + `oxlint` GitHub Action would turn that
  existing discipline into something that can't be skipped under time pressure.
- **Secrets handling is correctly done** — `Config.Local.cs` (MAUI) and `webapp/.env` (React) are
  both gitignored with committed `.example` templates, `google-services.json`'s client config is
  genuinely not a secret (documented as such, correctly), and the Firebase *service-account* key and
  Supabase `service_role_key` are both kept out of the repo (Edge Function secrets / Vault
  respectively) rather than hardcoded. I didn't find a committed secret anywhere in the files I read.
- **`AppUpdateService` reads `latestVersion` from `web/version.json` but never reads `minVersion`**
  (`Services/AppUpdateService.cs:60-83` vs. `web/version.json`'s two fields). The field exists, is
  deployed, and is simply unused — it reads like a placeholder for a future hard-block-below-minimum-
  version flow that hasn't been wired up yet (today's banner is purely a dismissible soft nudge, per
  `AppConstants.Preferences.UpdateBannerDismissedDate`'s own "not permanently" framing). Small, but
  worth knowing before assuming `minVersion` does anything today.
- **The Windows push-notification story is a real, acknowledged gap, not an oversight** —
  `IPushRegistrationService`'s Windows implementation is a deliberate no-op (`MauiProgram.cs:62-64`
  and CLAUDE.md's own framing), so every Windows user of this app currently gets zero push
  notifications for anything — new expenses, settle-ups, new events, RSVP changes, or birthdays. For
  an app whose entire second vertical (Events) leans on push for reminders and carpooling
  coordination, that's a meaningfully-sized hole on one of only two supported platforms.

---

## 6. UI / styling system

The design-token discipline (`Resources/Styles/Tokens.xaml`, `Colors.xaml`, `Styles.xaml`) is
real and largely followed, not aspirational — spacing, radius, and type ramps are consistently
referenced via `{StaticResource ...}` in the styles I read, and the accent-color feature
(`Services/ThemeService.cs`, 8 curated presets) is implemented cleanly: colors that should react to
the live accent swap use `{DynamicResource}`, everything else uses `{StaticResource}`, and the
in-place-dictionary-mutation approach was arrived at only after confirming (per `CLAUDE.md`) that the
more obvious remove-and-readd approach under-updates real WinUI button chrome. That's careful,
verified engineering, not a guess that happened to work.

The two old HTML review reports (see §1) did catch a real, now-fixed high-severity bug (`LoginPage`
missing a `ScrollView`/busy-guard) and a batch of lower-severity, still-plausible issues — hardcoded
pixel values that happen to equal a token's value but aren't expressed as a token reference, touch
targets under the ~44dp guideline on several icon-only buttons (back chevrons, overflow menus), and a
contrast issue on the Receive/Pay balance tiles. I did not re-audit every page against every one of
those ~30 findings (most of the underlying XAML has since been rewritten by the redesign and the
juice pass, so line numbers in the old reports no longer line up), but the *category* of issue —
small icon-only tap targets, occasional raw pixel values sitting next to `StaticResource` ones — is
still visibly present in the newer XAML I did read (e.g. `SplitCheckboxStyle`'s 20×20 tap target in
`Styles.xaml:749-769` is unchanged from what the old report flagged). This is a reasonable thing for a
dedicated pass to re-run now that the redesign has settled, rather than something to re-litigate
finding-by-finding here.

---

## 7. Concrete strengths worth stating plainly

It's easy for a review to read as a list of problems, so to be direct about the other side: this is a
well-engineered solo/small-team project, above the bar of most side projects at this stage.

- The `members` vs. `accounts` (phantom vs. claimed) design is the right foundational call for a
  shared-ledger app and it was applied with real discipline everywhere — I did not find a place where
  it was silently violated.
- Bug-fixing in this codebase is unusually rigorous: root causes are chased down and explained (the
  `42P17` RLS-recursion pair, the `payment_net` sign inversion, the invite-`expires_at`-defaulting
  bug), not just patched around. The fixes are narrow and address the actual cause, not the symptom.
- `BaseViewModel.RunSafeAsync` is a small piece of code doing an unusually good job of containing a
  real, previously-fatal crash class app-wide, with no per-ViewModel opt-out to forget.
- The multi-currency and Events features are both non-trivial (currency conversion snapshotting,
  tri-state RSVP/carpooling with a coupled state-reset rule, auto-generated birthday events with
  correct leap-day handling) and both shipped with real end-to-end verification against the live
  project, not just a clean compile.
- Nothing in this codebase reaches for a bigger abstraction than the problem needs — no premature
  interfaces, no speculative configuration, no dead feature flags. `Grep`-ing the whole `AxisApp/`
  tree for `TODO|FIXME|HACK|XXX` returns zero matches, which either means unresolved things get
  tracked in `CLAUDE.md`'s narrative instead of code comments (true, and arguably better — they come
  with full context) or genuinely don't linger.

---

## 8. Summary punch list

**Worth doing soon:**
1. Catch `CLAUDE.md` up with the last ~36 files / two feature phases (multi-currency, Events &
   Calendar, the UI juice pass) — it is currently the most-referenced source of truth in the repo and
   is meaningfully out of date.
2. Delete or clearly mark `ui-review-report.html` / `live-ui-walkthrough-report.html` as superseded —
   both predate the redesign and will mislead a future reader.
3. Collapse the three N+1 query loops (`GroupExpensesViewModel.RefreshActivityAsync`,
   `GroupEventsViewModel.LoadAsync`, `GroupsViewModel.LoadAsync`) into single filtered/embedded
   queries before a real group accumulates enough history to make them visibly slow.
4. Decide, deliberately, what to do about the web app's growing feature gap (no Events, no recurring,
   no receipts, no manual split, no push) — either scope it in or message it clearly in-app.

**Worth a note, not urgent:**
5. `web/version.json`'s `minVersion` field is deployed but never read client-side.
6. `schema.sql`'s ten hardcoded `net.http_post` URLs mean it's no longer a portable fresh-install
   script despite its own header comment claiming it is.
7. Windows gets zero push notifications — worth an explicit decision (build it, or document the
   platform gap) now that Events leans on push for reminders/carpooling.

**No action needed, just flagged as known/accepted:**
8. No automated tests, no CI — consistent with the project's current stage, but worth naming as the
   reason every regression so far has been caught by hand.
9. The `"update events in your groups"` RLS policy is wider than the app's own UI exposes (any
   member can technically update any event, including a birthday row) — already self-documented as a
   deliberate soft protection in `schema.sql`, not a surprise.

---
