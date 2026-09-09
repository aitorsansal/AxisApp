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

# Part 2 — The Council

Everything above is one reviewer's read of the codebase. To pressure-test "what should Axis build
next" from more than one angle, the question went to a 5-advisor LLM council (adapted from Andrej
Karpathy's LLM Council method): five independent advisors, each answering from a deliberately
different thinking style, then peer-reviewing each other's anonymized answers, then a chairman
synthesizing everything into one verdict.

**A methodology note worth being upfront about**: several advisors and reviewers had tool access and
used it — some read `CLAUDE.md`, `schema.sql`, `EVENTS_PLAN.md`, `SCOPE.md`, and git history directly
rather than relying solely on the briefing they were given, and the chairman went further still,
cross-checking claims against the independent review in Part 1 of this same document (which had
already been committed to the repo by the time the chairman ran). That wasn't instructed, but it made
the output substantially stronger — several claims below are verified against the live schema, not
just argued. Where a response leans on that verification, it's because the claim held up, not because
it was assumed.

## 2.1 The question posed to the council

> Axis is a .NET MAUI shared-ledger app (Android + Windows, Supabase/Postgres backend) for a small,
> invite-only group of friends/family — pre-launch, no public signups yet. [Current feature set as
> described in §1 above: split expenses, settle-up, recurring templates, receipts, multi-currency,
> two balance display modes; events with RSVP, tri-state carpooling, auto-generated birthday events;
> push notifications (Android only), aliases, avatars, accent colors, en/es localization, phantom
> members; a narrower React web client for one browser-only friend.]
>
> **Given everything already built, what should be built next?** Features that would make someone
> using this app go "oh, I want this" — not bug fixes or polish, but genuine product additions that
> would meaningfully increase value for a small group of friends/family actively managing shared
> money and shared plans together. Consider what's plausible for a solo/small-team hobby project to
> actually build and maintain, not what a big company would do.

## 2.2 The five advisors — independent responses

### Diane — The Contrarian
*Looks for what's wrong, what's missing, what will fail.*

> CLAUDE.md itself confirms the gap I'd flag first: it explicitly frames "events/calendar" as a
> **future phase**, not-yet-started ("the debt-tracker vertical currently being built, plus the
> events/calendar... phases planned after it"). No RSVP, no carpooling, no birthday events anywhere in
> it. Currency shows up exactly once as a field marked "reserved but unused." Yet the prompt says
> Events shipped with full RSVP/carpooling/birthdays, and expenses now have live exchange-rate
> conversion. Two entire verticals are live with zero trace in the one file this whole workflow
> depends on to avoid re-litigating already-fixed bugs. That's not a paperwork gap — it's the
> project's memory silently falling behind its own shipped surface area.
>
> That's the real constraint on "what's next," not idea supply. This log shows roughly one severe,
> data-touching bug per shipped feature, caught only after real friends hit it live: balance math
> running backwards (would've doubled every debt), invites silently dead for the entire pre-launch
> life of the app, two separate RLS infinite-recursion bugs, a headless-process race that told an
> actual group member "Group not found." Whatever ships next inherits that tax, on top of a base
> that's now under-documented.
>
> So the filter matters more than the pick: nothing that widens blast radius on money-correctness or
> adds an unverifiable integration. Concretely — do not build bank/Plaid sync, the single most
> tempting "obvious next step" for an expense app and the one that turns a hobby ledger into a
> liability. Skip in-app chat too; the group already has one. Anything you add should sit beside the
> ledger, not inside its math.

### Marcus — The First Principles Thinker
*Strips away the surface question and rebuilds it from the ground up.*

> You're both asking "what's the next vertical" and answering it by extrapolation — expenses got
> built, then events got built, so what's #3? Shopping lists, chores, photos? That's the wrong axis
> entirely. Look at what actually generates friction for a friend group sharing rent, trips, and
> plans: money and time-together aren't separate resources, they're the same occasion viewed from two
> angles. A trip is an event AND a pile of expenses. Right now, by your own description, Vertical 1
> and Vertical 2 are parallel data models with no edge between them — an event doesn't know about its
> expenses, an expense doesn't know which event it belongs to, and the RSVP list (who's actually
> going) isn't the same list you split the bill against three weeks later when nobody remembers who
> came.
>
> So: don't build vertical 3. Build the join. Let an event optionally own a scoped ledger — expenses
> attached to it, default split participants pulled straight from "going" RSVPs, a running total shown
> on the event page, settle-up prompted once the event's date passes. This isn't new infrastructure —
> it reuses your split engine, your settle-up-as-expense model, your RSVP data, your push triggers.
> It's a foreign key and some UI, not a subsystem.
>
> And it kills the exact annoying moment this group actually hits: reconciling "who was on this trip"
> against "who paid for what" as two separate memories instead of one screen. That's more value than
> any standalone feature either vertical could grow next, and it's cheaper to build than either of
> them was the first time.

### Alexandra — The Expansionist
*Looks for the upside everyone else is missing.*

> The vision doc already says it: the goal is "one unified channel per group" instead of juggling a
> separate splitting app and a separate calendar app. Nobody's built the fusion yet. Events and
> Expenses are still two silos that happen to share a members table. The single biggest swing
> available is Trip Mode — let an Event become a container for expenses. RSVP to the cabin weekend,
> and every expense tagged to it rolls into a running trip total, split only among whoever's actually
> going, visible live during the event instead of reconstructed afterward from a date range. That's
> the real Splitwise-for-trips use case, and this app is uniquely positioned to nail it because it
> already has both halves built separately — nobody else has to build RSVP and splitting from scratch
> to get there.
>
> Second bet, nearly free given what already exists: a Recap. After a trip, month, or year,
> auto-generate a shareable summary — total spent, cost per person per day, who fronted the most,
> photos if the receipts-storage pipeline gets reused for an event album. Zero new infrastructure,
> pure aggregation over data already captured, and it's exactly the kind of thing that gets
> screenshotted into the group chat unprompted.
>
> Third, and criminally underused: birthdays already auto-generate as events, and splitting already
> exists, but nobody's wired group gift pooling off a birthday event — prompt "chip in for Dave's
> gift" the moment it fires, keep that one expense off Dave's own feed.
>
> Stop treating these as two apps that share a login.

### Sam — The Outsider
*Zero context, reacts purely to what's in front of them.*

> Zero context here, so I'm reacting only to the two vertical descriptions — and the split itself is
> the tell. "Vertical 1" and "Vertical 2" is an engineering frame, not how a friend group experiences
> a trip. Nobody planning "Lake Trip" thinks "let me open the events product, then separately open the
> money product." From what's described, an event has no expenses hanging off it and an expense has no
> event hanging off it. That disconnect is invisible from inside the codebase — two clean feature
> areas, built and shipped sequentially — but it's the first thing a real user would trip over. The
> obvious next build: let an expense optionally attach to an event, so opening "Lake Trip" shows who's
> coming, who's driving, and a running per-person cost, in one place. Multi-currency conversion is
> already solved, so a live "$340 spent, ~$85/head" total on the event screen is nearly free to add
> and is exactly the number people actually check mid-trip.
>
> Second, smaller but just as obvious from outside: you already solved photo upload/resize/storage for
> receipts. Point that same plumbing at events and give each trip a shared photo album. Of everything
> on this list, "pictures from the trip" is the most viscerally wanted thing by an actual friend group
> — more than any ledger feature.
>
> Third: confirm carpool and RSVP changes actually push a notification, not just event-level ones.
> Carpooling only works socially if the person offering a seat finds out the moment someone claims it
> — a silent seats-remaining counter nobody checks defeats the point of building it at all.

### Rosa — The Executor
*Only cares whether it can actually be done, and the fastest path there.*

> Two features ship this week because you already built 90% of their plumbing. Everything else on a
> wishlist can wait.
>
> **1. Recurring-expense due reminders.** You have `materialize_recurring_expenses` running daily at
> 8am UTC and a working `send-push` pipeline (recipient-lookup SQL function → pg_net → Edge Function →
> FCM, already proven end-to-end). Monday morning: write `upcoming_recurring_reminders()` mirroring
> `expense_notification_recipients`'s exact shape, querying `recurring_expenses` where the next
> occurrence is within 3 days, call it from the same cron job right after materialization runs. No new
> table, no new client screen, no new push plumbing — just one more SQL function and one more
> `net.http_post`. This is the single most "oh I want this" feature for the actual use case here: rent
> day.
>
> **2. Google Calendar sync, but as a read-only ICS subscription feed, not OAuth two-way sync.** You
> already have battle scars from Google OAuth on Windows — the PKCE `bad_oauth_state` mess that forced
> a hand-rolled implicit-flow loopback listener just for login. Do not reopen that fight for the
> Calendar API. Instead: one Edge Function, `GET /functions/v1/group-ics?group_id=X&token=Y`, emits a
> static `.ics` text feed of events the token's member can see. Every calendar app subscribes to a URL
> natively — zero OAuth, one "copy link" button. Half a day, and it's literally the next line in
> SCOPE.md's own roadmap.
>
> Skip anything needing a new UI paradigm (polls, chat threads, budgets) until these two are actually
> deployed — both are "extend a cron job," not "build a feature."

## 2.3 Peer review — what the advisors caught in each other's answers

All five responses were anonymized and cross-reviewed. The pattern was unusually consistent:

- **Strongest response**: split between Marcus ("build the join," 3 of 5 reviewers) and Alexandra
  (Trip Mode + Recap + gift-pooling, 2 of 5) — never a real contest, since both are the same core idea
  argued from different angles. One reviewer verified the load-bearing claim directly against
  `schema.sql`: **`expenses` genuinely has no `event_id` column**, so the "two silos" diagnosis both
  advisors independently reached is factually correct, not a plausible-sounding guess.
- **Weakest response, unanimously**: Diane. Every one of the five reviewers, independently, flagged
  the same thing — her documentation-drift catch is real and her risk framing is sound, but she
  proposed zero features against a question that explicitly asked for features. Being right about risk
  isn't an answer to "what's next."
- **What all five advisors missed, surfaced only in peer review**:
  - **The web app is locked out of whatever ships here.** Raised independently by three reviewers:
    `webapp/`'s router has no Events routes at all, so if event-expense linking ships native-only, the
    one person the web client exists for can't use the feature the rest of the group starts relying on
    for trip planning.
  - **Nobody proposed the highest-leverage fix directly**: one reviewer pointed out that updating
    `CLAUDE.md`/`SCOPE.md` to match what's actually shipped may itself be the single most valuable
    "build" available right now, given this project's own history of severe bugs surfacing specifically
    when documentation lags code — and no advisor said so outright.
  - **Offline resilience.** Every flagship pitch (a live running trip total, Trip Mode) targets exactly
    the low-connectivity moments — cabins, hikes, travel abroad — where a live Supabase round trip
    can't be assumed, and nobody addressed what the event screen should show when it can't reach the
    server.
  - **Notification fatigue.** Multiple advisors independently propose adding *more* auto-push triggers
    to a pipeline that's about a week old and has no mute/category-preference mechanism anywhere in the
    schema or client yet.
  - **The irony that the consensus pick is exactly the failure shape that already bit this project
    twice** — both real, production RLS bugs to date (`42P17` infinite recursion) came from two tables'
    policies referencing each other; wiring `expenses` to `events` is a new cross-table relationship in
    a codebase with zero automated tests to catch a repeat.

**One additional peer-review finding, verified independently after the chairman synthesis was already
under way** (so the verdict below doesn't reflect it, but it's real and worth recording): a fifth
review — cross-checking `schema.sql`'s actual trigger list — found that **`events` has exactly three
triggers (insert/update/before-delete), and none of them are on `event_attendees`.** In plain terms:
today, changing an RSVP or a carpool offer/need fires **no push notification at all** — only creating,
editing, or cancelling the event itself does. Sam's "confirm carpool/RSVP changes actually push a
notification" instinct was, per this specific check, not a redundant ask — it's a real, currently-true
gap. The same review also flagged a structural question nobody else raised: **a phantom member (no
`auth.users` row) cannot RSVP at all**, since every `event_attendees` write policy requires
`m.account_id = auth.uid()`. For an app whose founding design principle is that phantom members
participate in the ledger exactly like anyone else, RSVP is quietly the first feature where that
principle doesn't extend — worth a deliberate decision (a proxy-RSVP-by-whoever-added-them affordance,
or an explicit "phantom members can't be invited to events yet" scoping) rather than an accidental gap.

## 2.4 The chairman's verdict

*Synthesized from all five advisor responses and all five peer reviews — the chairman also cross-checked
claims directly against `schema.sql`, `EVENTS_PLAN.md`, `SCOPE.md`, `MULTI_CURRENCY_PLAN.md`, the
webapp router, and Part 1 of this document.*

### Where the Council Agrees

**The event-expense fusion is the standout idea, reached three independent ways.** Marcus
(first-principles: money and time-together are the same occasion viewed from two angles), Alexandra
(expansionist: "Trip Mode," the unified-channel vision the project's own scope doc already states),
and Sam (outsider: the Vertical 1/Vertical 2 split is an engineering frame no real user shares) all
landed on the same mechanism — let an event optionally own a scoped set of expenses, default the split
to whoever RSVP'd "going," show a running total on the event page, prompt settle-up once the date
passes. Four of five peer reviewers independently ranked this the strongest response, and the chairman
confirmed the load-bearing technical claim directly: **`expenses` has no `event_id` anywhere in
`schema.sql`** — every event-related foreign key in the file is scoped to `event_attendees` and the
notification pipeline, none of it touches `expenses`. The gap is real, not assumed.

**It's structurally cheap, not just conceptually appealing.** Both tables already gate access through
the identical `is_group_member(group_id)` RLS pattern, so a nullable `expenses.event_id` needs no new
policy and no cross-table recursion risk — the same "scope by group, not by the specific parent row"
trick the schema already uses for receipt paths to dodge a chicken-and-egg problem. "A foreign key and
some UI, not a subsystem" (Marcus) holds up against the actual code, not just as a pitch.

**Nobody wants a third vertical.** No advisor proposed a genuinely new domain — budgets, chores,
shopping lists, polls. Even Rosa's picks are depth-additions to verticals that already exist. The
council converges hard on *connect what's built* over *add what isn't*.

**Diane's documentation-drift finding is real, and every reviewer said so.** Confirmed precisely:
`CLAUDE.md` was last edited two days before `HEAD`, and the gap is 36 files / ~1,370 lines / two entire
features (multi-currency, Events & Calendar) with zero mention. This isn't a hunch — Part 1 of this
same document, written independently the same morning from a cold read of the code, reached the
identical conclusion by a completely different route and opens with it as finding #1.

### Where the Council Clashes

**Diane's guardrails vs. everyone else's appetite to ship.** Every reviewer penalized Diane for
answering a features question with prohibitions. That's fair against the letter of the brief — but her
evidence (one severe, data-touching bug per shipped feature, all caught live by real friends) is
exactly what independent verification still confirms: **zero automated tests, zero CI**. And both real
recursion bugs happened on cross-table policy references — structurally the same risk class as wiring
`expenses` to `events`. This isn't "build vs. don't build." It's "how much guardrail does a cross-table
feature need in a codebase with no regression net." Ship it, but ship it the way the schema already
knows how to dodge this exact failure mode (additive nullable FK, no new policy referencing back into
`events`) — not as a reason to skip it.

**Rosa's "ship this week" sequencing vs. the fusion feature.** Rosa explicitly frames her two picks as
substitutes — "skip anything needing a new UI paradigm... until these two are deployed." That's a real
disagreement, not a complementary pick: she's optimizing for cheapest-win-this-week, the other four for
highest-leverage-single-feature. Both individually reasonable; they can't both be first.

**Rosa's ICS-feed pitch is good, but isn't "the next line in SCOPE.md" as claimed.** `SCOPE.md` already
scopes a real "Phase 2.5 — Google Calendar sync" (OAuth, encrypted token storage, RRULE reconciliation),
deliberately deferred until Events shipped — which it now has. Rosa's read-only ICS subscription is a
genuinely smarter, cheaper substitute for that OAuth path (and rightly avoids reopening the exact PKCE
`bad_oauth_state` fight already documented as lost once) — but it's a different, lighter feature than
the roadmap's own plan, not a continuation of it. Worth building, worth being precise that it's a
deliberate downgrade of scope, not "next per the plan."

### Blind Spots the Council Caught

- **The web-only friend loses the exact feature the group would use together.** `webapp/src/App.tsx`
  has zero Events routes — not partial, not stubbed, absent. If event-expense linking ships native-only,
  the one person the web client exists for is locked out of the trip-planning feature the rest of the
  group starts using together. This needs a deliberate decision, not silence.
- **Notification fatigue isn't a fresh oversight — it's a debt the project already wrote down and
  deferred.** `EVENTS_PLAN.md`'s own "Explicitly deferred" list names "per-group notification muting,"
  citing a notification-design section in `SCOPE.md` that already worked out category-level muting
  conceptually. There is currently zero mute/preference mechanism anywhere in the schema or client.
  Stacking a third or fourth trigger type on top isn't discovering a gap — it's compounding one the
  project already knows about.
- **The "which currency does a mixed-currency trip show" concern, raised in peer review, is already
  solved, not open** — `groups.currency` is a required, locked-at-creation settlement currency, and
  every expense already snapshots `amount_in_group_currency` at write time for exactly this reason. An
  event running total is `sum(amount_in_group_currency) where event_id = X` — no new currency design
  needed.
- **Sam's ask to "confirm carpool/RSVP pushes actually work" is more right than the peer review gave it
  credit for** — see §2.3's supplementary finding above: `event_attendees` genuinely has no push
  trigger today.
- **Offline resilience, unaddressed by all five.** Every repository call in this app is a live Supabase
  round trip with no local cache or offline queue mentioned anywhere in the docs or the independent
  review. Trip Mode's actual usage moments — cabins, hikes, travel abroad — are precisely where
  connectivity is worst. Not a reason to build offline-first (too big for this project), but the event
  page needs to degrade to "last known total" rather than error, or the flagship feature fails exactly
  when it's needed most.
- **Windows gets zero push, and Events leans on push for its entire coordination value.** Not raised by
  any advisor. Every reminder/carpool/gift-pooling nudge the council proposes is an Android-only win.

### The Recommendation

**Build the event-expense link — Marcus's scoping, exactly as written**: nullable `expenses.event_id`,
default split pulled from "going" RSVPs, a running total on the event page, settle-up prompted once the
date passes. This is the one idea on the table that is genuinely a feature (not a prohibition, not a
"wait"), that makes both verticals better at once instead of deepening one, and that the codebase can
absorb safely — it rides existing RLS with no new recursion surface, exactly the kind of change this
project's history shows it does well, as opposed to the two bugs that came from policies
cross-referencing each other.

Ship it with three guardrails the evidence above makes non-optional, not nice-to-haves:

1. **Decide the web-client question before shipping, don't let it default.** Either scope a minimal
   event-linked view into `webapp/` alongside this, or ship an explicit "not available on web yet"
   indicator. Silent widening of an already-documented gap is the one outcome nobody should choose by
   accident.
2. **Do not add a fourth push trigger type before shipping the mute control `SCOPE.md` already
   designed.** Rosa's due-date reminders and any carpool/gift-pooling nudges are good ideas — after
   there's a way to turn a category off. The project already knows it needs this; build it now, not
   after the next trigger makes it more urgent.
3. **Additive-only migration.** Nullable FK, no new policy touching `events`' own RLS, manual
   re-verification of the two already-known 42P17-prone paths (`is_group_member`, `is_own_member_row`)
   afterward — given zero test coverage, this is the one place "move fast" needs a specific, not
   general, brake.

Do Rosa's two picks (recurring-due reminders, ICS calendar feed) next — they're real, cheap, and don't
compete for the same schema surface, so there's no reason to choose between them and the fusion
feature. Just don't let them substitute for it; they're depth on one vertical each, not the "oh I want
this" the brief is actually asking for.

Explicitly don't build: bank/Plaid sync or in-app chat (Diane's calls stand — nothing above overrides
them), and don't reopen two-way Google OAuth calendar sync (the PKCE fight is already documented as
lost once on this exact stack).

### The One Thing to Do First

**Catch `CLAUDE.md` and `SCOPE.md` up to what's actually shipped — before writing the `event_id`
migration, as its first step, not a detour from it.** This isn't a hedge dressed as an answer: Part 1
of this document already did the investigation (exactly which commits, exactly which two features,
exactly what's missing) — this is transcription, a few hours, not a project. The reason it has to come
first is concrete, not procedural: whoever builds the event-expense link next — human or agent — will
read `CLAUDE.md` first, per this project's own stated practice, and right now that file doesn't mention
`events`, `event_attendees`, the group-settlement-currency columns, or the RLS helper functions those
features depend on. Building a cross-table schema change on top of docs that don't know the other table
exists, in a codebase with no test suite to catch the mistake, is the one sequencing error that would
compound every risk this council surfaced. Fix the map, then build the join.

---

# Part 3 — Closing synthesis

Two independent processes — a cold read of the codebase (Part 1) and a five-advisor council debating
product direction from adversarial angles (Part 2) — converged on the same starting point without
either one steering the other: **the documentation is the bottleneck, not the idea supply.** Part 1
flagged `CLAUDE.md`'s two-day, 36-file, two-feature gap as finding #1 before any council question was
asked. The council, reasoning purely about "what to build next," independently rediscovered the same
gap through Diane's contrarian pass, had it confirmed by every peer reviewer, and the chairman named
fixing it "the one thing to do first" — ahead of the feature idea the rest of the council spent its
energy on. When two differently-motivated passes over the same project land on the same top priority
without coordinating, that's about as strong a signal as this kind of review can produce.

The product answer underneath that is a genuinely good one, and it's good for a specific, checkable
reason rather than because it sounded appealing: **event-expense linking is the only idea the council
proposed that is verifiably cheap in this exact codebase** — a nullable foreign key riding RLS
machinery that already exists, not a new subsystem — while also being the only idea that addresses a
friction real users (the friends and family actually running trips and rent through this app right
now) would recognize immediately. It also happens to sit exactly on top of this review's own §2.1
performance finding: the N+1 query patterns in `GroupExpensesViewModel`/`GroupEventsViewModel` will get
real exercise the moment expenses start rendering inline on event pages, so fixing those loops isn't
just hygiene anymore — it becomes a prerequisite the event-expense feature will expose immediately if
skipped.

Put together, the punch list in §8 and the council's verdict in §2.4 point at the same short sequence:
**update the docs, collapse the N+1 loops, then build the join** — in that order, because each step
makes the next one safer in a codebase that currently has no automated safety net of its own.

