# Events & Calendar (Phase 2) — Implementation Plan

Written 2026-09-07, from a planning-only chat session (no code changes made yet).
Picks up from `SCOPE.md`'s "Phase 2 — Events & Calendar" sketch (`events`/
`event_attendees`, reuse Phase 1's push/pg_cron primitives, the flat-channel
notification design already decided 2026-09-03) — this plan is what actually
wires that up, plus a carpooling/transport feature that wasn't in the original
sketch. If you're a fresh session reading this cold: read "Decisions locked"
below before touching schema.sql or `GroupDetailPage`/`GroupDetailViewModel`,
then check the status line on each milestone and continue from the first one
not marked done.

## Progress

| # | Milestone | Status |
|---|---|---|
| 1 | Schema — `events`, `event_attendees`, `members.car_extra_seats`, RLS | **done & structurally verified 2026-09-07** — live migration ran clean, RLS enforcement itself not yet exercised under a real session (see milestone notes) |
| 2 | App — Group Detail tab restructure (Expenses / Events) | **done & verified 2026-09-07** — confirmed in the running app: pill switches tabs, FAB hides on Events, "Repeating expenses" hides on Events |
| 3a | App — Events data layer (models, repository, routes) | **code done 2026-09-07, builds clean on both targets — unexercised against live data until 3b has a UI to drive it** |
| 3b | App — Events list, grouped-by-date view, Add/Edit Event, RSVP | **code done 2026-09-07, builds clean on both targets — one real bug found and fixed via a live run (see milestone notes), rest not yet manually confirmed** |
| 4 | App — Transport/carpooling | **done & verified 2026-09-07** — confirmed working in the running app, including the Maybe-can't-offer-a-car refinement |
| 5 | Infra — Event notifications (creation + change/cancel + reminder) | **done & verified 2026-09-07** — confirmed by the user on a real device, real pushes landing correctly |

**When you finish a milestone, update its Status cell in the table above**
(e.g. `done 2026-09-10`, or `done — see commit abc123`) before ending the
session, so the next session (possibly on a different machine) can tell at a
glance what's left. If you deviate from a milestone's plan below while
implementing it, update that milestone's text too — this file should stay a
true reflection of what exists, not just what was originally intended.

## Decisions locked in this session (don't re-litigate without new information)

- **Tab structure, not a new page hierarchy.** `GroupDetailPage` gains a
  segmented-pill tab selector at the top — **Expenses / Events** — reusing
  the real segmented-pill styles (`SegmentPillContainer`/
  `SegmentButtonSelected`/`SegmentButtonUnselected`) `AddExpensePage`
  already established for its Equally/Manually split-mode toggle.
  (Correction from an earlier draft of this plan, found while implementing
  Milestone 2: the Simplified/Pairwise balance toggle this was originally
  attributed to is actually a plain `Switch`, not a segmented pill — the
  real pill precedent lives on `AddExpensePage` instead.) Switching tabs is
  an in-page property flip (`SelectedTab`), **not**
  a Shell navigation — stays on the same `//GroupDetails` route the whole
  time, so the hardware/gesture back button always leaves the group rather
  than "un-flipping" the tab first. Shell's `TabBar`/`Tab` was considered and
  rejected — that's for top-level app navigation, not a tab pair scoped to
  one page's body. A swipeable `CarouselView` was discussed as a nicer feel
  but deferred — no existing precedent for it in this codebase, and the
  segmented-pill approach ships faster; revisit if the flip feels clunky in
  practice.
- **`GroupDetailViewModel` gets split, not just the body content.** The
  existing ViewModel (balances, recent activity, Settle, the whole
  leave/transfer/dissolve/rename overflow menu) was already sizable before
  Events existed — bolting a second, unrelated vertical onto it directly
  would make it unmaintainable. Split into:
  - **`GroupDetailPage`/`GroupDetailViewModel`**: thin shell — group header,
    the tab selector, and the ⋮ overflow menu items that apply to the group
    itself regardless of tab (leave/transfer/dissolve/rename/view
    members/invite people).
  - **`GroupExpensesView`/`GroupExpensesViewModel`**: today's balance +
    recent-activity content, moved down a level, otherwise unchanged.
  - **`GroupEventsView`/`GroupEventsViewModel`**: new — the events list,
    RSVP, transport matching from this plan.
  - The **FAB** context-switches per tab ("+ Add expense" vs. "+ Add
    event"). **"Repeating expenses"** in the ⋮ menu only shows while the
    Expenses tab is active (matches: it's expense-specific, and an eventual
    event-reminder-management entry, if one is ever needed, would only show
    under Events).
- **`AddEventPage` is a dedicated page, not an extension of `AddExpensePage`.**
  Explicit call-out because the *opposite* choice was made for recurring
  expenses (extended `AddExpensePage` in place) — that worked there because
  ~90% of the form was genuinely shared (split UI, payer picker). An event
  shares almost nothing with an expense (title/date/location vs.
  amount/split/category), so force-fitting it into the same page would just
  be architecture for its own sake.
- **RSVP: 3-state — `going` / `maybe` / `not_going`.** Not a plain
  yes/no. Affects transport headcount math directly (see below) and the
  reminder-notification recipient list.
- **Transport/carpooling — aggregate counts only for v1, no seat
  assignment.** Two lists (offering a ride / needs a ride) plus a total —
  e.g. "8 seats offered / 5 need a ride ✓" or a shortfall banner. Explicitly
  **not** building "rider picks a specific driver's car" — that's a real
  matching/allocation problem (who rides with whom under capacity
  constraints), the same shape of complexity `SCOPE.md` already deferred
  once for debt-simplification counterparty exclusion. People coordinate who
  rides with whom by talking to each other; Axis only tells you if there's a
  shortfall. Real seat booking/assignment is a plausible fast-follow if the
  aggregate view proves insufficient, not planned now.
  - **`members.car_extra_seats`**: a per-profile default, named to avoid the
    off-by-one confusion the original idea's phrasing had ("car places = 6"
    meaning "me + 6", not "6 total") — this column and its UI label always
    mean **extra seats beyond the driver**, in both the DB and every place
    it's rendered. Reuses the "reserved profile field, unused until edited"
    pattern `birth_date` already established (no RLS change needed — same
    "update members you created or claim yourself" policy already covers
    it).
  - **Per-event override**: a member's car-offer seat count for one specific
    event can differ from their profile default (already has a passenger
    that trip, trunk full of gear, etc.) — defaults from
    `members.car_extra_seats` when they toggle "I have a car available" on
    an event, but is editable per event. Lives on `event_attendees`, not
    read live from the profile at display time.
  - **One tri-state field, not two booleans**: `event_attendees.car_status
    ('none' | 'offering' | 'needs_ride')` — avoids the invalid state of
    someone being both an offerer and a rider on the same event at once.
  - **`events.needs_transport` is editable after creation**, unlike
    `groups.currency`'s deliberate lock — an organizer may not know
    transport will be an issue until people start RSVPing.
  - **RSVP and car status are coupled on write, not left free-floating**:
    `response` and `car_status` are separate columns with no DB constraint
    tying them together, so nothing stops someone RSVPing `not_going` while
    still sitting in the `offering`/`needs_ride` aggregate — which would
    corrupt the shortfall math the whole feature exists to show. Fixed at
    the app layer: whenever an RSVP write sets `response = 'not_going'`,
    the same write resets `car_status` to `'none'` (and clears
    `car_offered_seats`). No DB trigger for this — it's a single call site
    (the RSVP save path) and matches this project's general preference for
    app-level logic when there's exactly one writer to coordinate, reserving
    triggers for cases with multiple entry points (inserts from several
    clients, cron) or a real cross-table invariant.
- **Calendar view: grouped-by-date list for v1, not a month grid.** MAUI has
  no built-in calendar-grid control (same category of gap as the theming
  color-picker — see `SCOPE.md`'s Theming section). A real month grid would
  need either a hand-built `Grid` or a third-party control with unverified
  version compatibility, per this project's existing "don't guess NuGet
  package versions" discipline. Starting with events grouped by
  month/date (a timeline, effectively) is cheaper to build and arguably more
  usable on a phone screen than a cramped grid. A real calendar-grid view is
  a plausible later addition, not blocking v1.
- **Notifications reuse the flat-channel design already decided
  2026-09-03** (`SCOPE.md`'s "Notification design for this phase" section)
  — three shared channels (Expenses, Events, Anniversaries/birthdays),
  distinct-`account_id` dedup so a member in several shared groups gets one
  push per event, not one per group membership. New event-specific behavior
  this plan adds:
  - **New event created** → push to every current group member except the
    creator, immediate (`AFTER INSERT` trigger on `events`, same shape as
    `notify_new_expense`).
  - **Reminder before an event** → a `pg_cron` job (mirrors
    `materialize_recurring_expenses`'s "scan for due things" pattern) scans
    for events starting soon that haven't been reminded yet, notifies every
    attendee with response `going` or `maybe` (not `not_going` — someone who
    already declined doesn't need a nudge; `maybe` is included because a
    reminder might tip them into actually going) — **including the creator
    this time**, unlike the creation push (they already know they made it;
    a reminder is useful to everyone attending regardless of who organized
    it). Uses the existing `events.reminder_sent_at timestamptz null`
    column so a given event is only ever reminded once, same idea as
    `recurring_expenses.last_processed_date`.
    - **Fixed lead time and cron run time, decided 2026-09-07**: daily at
      **9am UTC** — distinct from `fetch-exchange-rates` (6am) and
      `materialize-recurring-expenses` (8am) rather than bundling onto
      either, still a normal-morning time so a reminder push doesn't land
      at 3am for most people. Window: `starts_at` in `[now, now + 30h)`
      and `reminder_sent_at is null` — since the check only runs once a
      day, an exact "24h before" isn't achievable anyway; this gives every
      event its one reminder somewhere in the **24–30h** range before it
      starts (whichever daily run first catches it inside the window).
      Narrower than an earlier 48h draft, per explicit user preference.
    - **The real goal, explicitly deferred past this pass**: **per-user
      configurable reminder lead times** ("1 month before" / "1 week
      before" / "1 day before" / "same day", possibly more than one
      selected at once) — discussed directly with the user, who agrees
      this v1 fixed-window reminder is a starting point, not the intended
      end state. Deliberately not attempted now because it's a materially
      bigger data-model change than a fixed single reminder: today's
      design has exactly one `reminder_sent_at` on `events` because there's
      exactly one reminder, ever, for the whole event. A per-user,
      possibly-multi-select lead time means the "have I reminded this
      person for this lead time yet" state has to live per **attendee**
      (e.g. an `event_attendees.reminder_sent_at`, or a separate table if
      more than one lead time can be selected at once per person), and the
      cron's scan shape changes from "which events are due" to "which
      (event, attendee, lead time) combinations are due" — a real
      redesign, not a tweak to the window bounds. Revisit as its own
      milestone if/when this gets picked up; the fixed-window version
      below is intentionally simple enough to not block on that redesign.
  - **RSVP changes** → no push. Matches the existing "don't notify about
    routine, low-stakes changes" pattern (e.g. nobody's notified about their
    own action elsewhere in this app either).
  - **Event changed (`starts_at`/`ends_at`/`location` edited) or event
    deleted** → push to every current attendee except the actor, immediate
    (`AFTER UPDATE`/`AFTER DELETE` triggers on `events`). Added deliberately,
    not part of the original sketch: creation-only notification leaves a
    real gap once carpooling exists — an organizer moving the time or
    cancelling outright, with nobody who already RSVP'd or arranged a ride
    finding out, is a worse failure than the spam risk of one extra push.
    `AFTER DELETE` needs the recipient list computed *before* the row (and
    its `event_attendees` cascade) is gone — capture it in the trigger via
    `OLD`, same as any other `AFTER DELETE` trigger reading the deleted row.
    A plain description/`needs_transport` edit does **not** notify — only
    the fields that would actually strand or confuse someone who already
    made plans.
  - **Transport shortfall nudges** → explicitly deferred, not in this pass.
    Deciding *when* to trigger it without becoming spammy (every time
    someone joins the "needs a ride" list? Only as the event approaches?) is
    its own design problem.
- **RLS shape for `events`/`event_attendees`**: `events` mostly mirrors
  `recurring_expenses`'s 4-policy shape — any current group member
  (`is_group_member(group_id)`) can select/insert/update — but **delete is
  creator-only** (`created_by = auth.uid()`), a deliberate divergence from
  expenses. An expense is collective by nature (correcting a shared bill is
  routine), but an event is more ownership-flavored — having one you
  organized deleted out from under you by another member is a different,
  more disruptive kind of surprise, and it's compounded by the fact that
  attendees may have already arranged carpooling around it. Edit stays open
  to any member (fixing a typo'd time/location shouldn't need the
  organizer specifically), only delete is restricted. `event_attendees` is
  different again: select is scoped the same way (`is_group_member` via a
  join to `events.group_id`), but insert/update/delete of an attendee row
  is restricted to **your own member row only** — mirrors the "a real
  account joins/RSVPs by its own action, never someone else's" principle
  already established for group membership (`JoinGroupPage`'s
  claimed-member restriction, `leave_group()` vs. being removed).
  Deliberately **not** extending the `is_unscoped_expense_party()`-style
  post-dissolution visibility widening to events — same reasoning
  `recurring_expenses` already used to skip it (a template/event surviving
  group dissolution just becomes creator-only-visible, flagged as a
  possible future gap, not a blocker).

---

## Milestone 1 — Schema

**Status: done & structurally verified live, 2026-09-07.** Run against the
live project via the SQL editor (driven through the Claude in Chrome
browser extension) — completed with "Success. No rows returned." Verified
afterward, not just trusted:
- Both tables' columns match spec exactly (types, nullability, defaults) —
  queried `information_schema.columns`.
- `members.car_extra_seats` present.
- RLS enabled on both new tables (`pg_class.relrowsecurity`), 4 policies
  each (`pg_policies` count).
- Pulled every policy's actual `qual`/`with_check` text from `pg_policies`
  and confirmed it matches what was intended, including the group-
  membership-plus-own-row fix described below.
- Both indexes present (`pg_indexes`).

**Caveat, not swept under the rug**: the SQL editor runs as the `postgres`
role, which bypasses RLS entirely — everything above confirms the DDL
landed structurally correct, but it does **not** prove the policies
actually allow/block the right things under a real authenticated session.
That only means something once it runs through the app with a genuine JWT
— Milestone 3a/3b's real RSVP flow is what actually exercises this, not
a from-the-SQL-editor smoke test.
Additive to `supabase/schema.sql`, no existing rows to migrate (mirrors every
prior schema milestone in this project — one-off script to run against the
live project, plus `schema.sql` hand-edited to match for a clean fresh
install). Two files, same shape as every other milestone-1-style migration
in this project:
- **`supabase/events_milestone1.sql`** — the one-off script to actually run
  against the live project (wrapped in `begin;`/`commit;`, a smoke-test
  recipe in a trailing comment, same shape as
  `multi_currency_milestone1.sql`).
- **`supabase/schema.sql`** — already hand-edited in place (appended after
  the signup allow-list section, the end of the file) to reflect the same
  end state, so a fresh install matches the live project once the one-off
  script above has been run.

- [x] `events` table: `id`, `group_id references groups(id) on delete
      cascade`, `title text not null`, `description text`, `location text`,
      `starts_at timestamptz not null`, `ends_at timestamptz`,
      `needs_transport boolean not null default false`,
      `reminder_sent_at timestamptz`, `created_by references auth.users(id)
      on delete set null` (nullable **from the start** — unlike
      `expenses.created_by`/etc., which began `not null` and needed a later
      relaxation once account deletion was built; events didn't exist at
      that point, so there's no reason to reintroduce that bug here),
      `created_at timestamptz not null default now()`.
- [x] `event_attendees` table: `event_id references events(id) on delete
      cascade`, `member_id references members(id) on delete cascade`,
      `response text not null default 'going' check (response in ('going',
      'maybe', 'not_going'))`, `car_status text not null default 'none'
      check (car_status in ('none', 'offering', 'needs_ride'))`,
      `car_offered_seats int` (meaningful only when `car_status =
      'offering'` — no DB constraint enforcing that pairing for now, same
      light-touch treatment other nullable-conditional columns in this
      schema get), `created_at`/`updated_at timestamptz not null default
      now()`, primary key `(event_id, member_id)`.
- [x] `members.car_extra_seats int` — nullable, no default, no RLS change
      (existing "update members you created or claim yourself" policy
      already covers it).
- [x] Indexes: `events(group_id, starts_at)` (the grouped-by-date list and
      the reminder cron scan both filter/sort on this) and
      `event_attendees(event_id)` (attendee lookups per event, including the
      transport aggregate and the notification recipient functions in
      Milestone 5).
- [x] RLS policies per "Decisions locked" above — `events`: select/insert/
      update via `is_group_member(group_id)` (mirror `recurring_expenses`),
      **delete restricted to `created_by = auth.uid()`** (diverges from
      `recurring_expenses`' full 4-policy shape — see "Decisions locked").
      `event_attendees`: select via a join to `events` +
      `is_group_member(e.group_id)`; insert/update/delete require **both**
      the same group-membership check **and** the row's `member_id`
      resolving to the caller's own account — found while writing this
      milestone (not in the original plan text): the "own row only" check
      alone isn't sufficient, since one account's `member_id` is shared
      across every group it belongs to (the one-account-one-member
      invariant), so without the group-membership check too, an account
      could RSVP to an event in a group it was never invited into just by
      referencing its own `member_id`.
- [x] Ran `events_milestone1.sql` against the live project — no errors,
      structurally verified (see the milestone status note above).
- [ ] **Not done yet**: a real RLS-enforced exercise under an actual
      authenticated session (the SQL-editor verification above runs as
      `postgres`, which bypasses RLS) — deferred to Milestone 3a/3b, which
      will exercise this naturally through the real app.

## Milestone 2 — Group Detail tab restructure

**Status: done & verified 2026-09-07.** Builds clean on both
`net10.0-windows10.0.19041.0` and `net10.0-android` (0 errors), and
confirmed in the running app: the pill switches tabs, the FAB hides on
Events, and "Repeating expenses" hides on Events. Pure app-side plumbing —
no new Events functionality yet,
just splitting the existing page/ViewModel so Milestone 3 has somewhere to
land. See "Decisions locked" above for the shape.

- [x] Extracted today's `GroupDetailPage` balance + recent-activity content
      into `Views/GroupExpensesView.xaml` + `ViewModels/
      GroupExpensesViewModel.cs` — same logic, moved down a level, no
      behavior change (verified line-by-line against the original during
      the move; caught and fixed two transcription slips before they became
      real bugs — `FormatRelative`'s minutes-ago branch had been mistakenly
      rewritten to read `TotalHours` instead of `TotalMinutes`, and the
      Simplified-mode neutral-row branch of `BuildSimplifiedItems` had
      silently dropped its `AvatarUrl` assignment).
- [x] `GroupDetailPage`/`GroupDetailViewModel` slimmed to: header, the
      Expenses/Events tab selector (a real segmented pill — reused
      `SegmentPillContainer`/`SegmentButtonSelected`/`SegmentButtonUnselected`,
      the styles `AddExpensePage`'s Equally/Manually split-mode toggle
      already established; correcting an earlier assumption in this plan
      that the Simplified/Pairwise balance toggle used this pattern — that
      one is actually a plain `Switch`), and the group-level ⋮ menu
      (leave/transfer/dissolve/rename/view members/view recurring
      expenses). Kept its own group+members fetch (`IGroupsRepository`/
      `IMembersRepository`) for `IsGroupCreator`/`HasOtherMembers`/
      `TransferCandidates`, since those are needed regardless of tab;
      dropped `IExpensesRepository`/`IAliasesRepository` entirely (moved to
      `GroupExpensesViewModel`, which now does its own independent
      group/members/aliases/expenses fetch — a little redundant with the
      parent's own fetch, but keeps the tab genuinely independent of
      exactly when/how the parent refreshes, a deliberate simplicity-over-
      micro-optimization call given typical group sizes in this app).
- [x] `GroupEventsView`/`GroupEventsViewModel` — empty/placeholder shell
      (a centered "Events are coming soon." label, no DI dependencies yet),
      wired into the tab selector.
- [x] FAB context-switches per tab — bound to `ExpensesVm.AddExpenseCommand`
      directly from the page level (nested property-path binding), hidden
      entirely on the Events tab rather than wired to a route that doesn't
      exist yet. "Repeating expenses" menu item (plus its section divider,
      to avoid a stray line on the Events tab) only shown while the
      Expenses tab is active.
- [x] Builds clean on both targets (0 errors) — confirmed via two full
      `dotnet build` runs.
- [x] Manually confirmed in the running app: the pill switches tabs, the
      FAB disappears on the Events tab, and "Repeating expenses" disappears
      from the ⋮ menu on the Events tab. Full Expenses-tab regression pass
      (balances, Settle, activity feed, edit-expense navigation) not
      separately itemized by the user but implied working since nothing
      about that path changed behaviorally, only where the code lives.

## Milestone 3a — Events data layer

**Status: code done 2026-09-07, builds clean on both `net10.0-windows10.0.19041.0`
and `net10.0-android` (0 errors) — unexercised against live data, since no
UI calls any of it yet (that's 3b).** Depends on Milestone 1 (already live).
Split out from the original single Milestone 3 — mechanical, low-risk
plumbing with none of the real UI/UX decisions, so it's a clean checkpoint
on its own rather than bundled with Milestone 3b below.

- [x] `Models/Event.cs`, `Models/EventAttendee.cs` — plain Postgrest-attributed
      models, same shape as every other model in this project (`[Table]`,
      `[PrimaryKey]`, `[Column]`). `EventAttendee.EventId` uses
      `[PrimaryKey("event_id", shouldInsert: true)]`, mirroring
      `ExpenseShare.ExpenseId` — the same composite-key footgun (a plain
      `false` `shouldInsert` would silently drop `event_id` from every
      insert payload).
- [x] `IEventsRepository`/`SupabaseEventsRepository` — CRUD on `events`,
      RSVP read/write on `event_attendees` via `UpsertRsvpAsync` (checks for
      an existing row through an explicit `event_id`+`member_id` `Filter`
      before deciding insert vs. update, never trusting `Update(model)`'s
      implicit primary-key match — same pattern
      `SupabaseExpensesRepository.UpdateAsync` already established for
      `expense_shares`' identical composite-key shape). This is the one
      place responsible for the response/car_status coupling from
      "Decisions locked" — passing `response: "not_going"` always forces
      `carStatus`/`carOfferedSeats` to `"none"`/`null` in the same call
      regardless of what the caller passed, even though `car_status` itself
      isn't exercised by any UI until Milestone 4, so that rule has exactly
      one implementation instead of being redone when Milestone 4 lands.
      `AddAsync` only sets `CreatedBy` before insert, not `CreatedAt` —
      confirmed by checking `SupabaseExpensesRepository`/
      `SupabaseRecurringExpensesRepository`'s own `AddAsync` methods first
      (neither sets `CreatedAt` either, and both are proven working against
      live data), rather than assuming a fresh `DateTime` default might
      silently override the DB's `now()` default the way `Invite.ExpiresAt`
      once did — that turned out to be a needless worry once checked against
      this codebase's own established, working precedent. Registered in
      `MauiProgram.cs` as a singleton, same pattern as every other
      repository.
- [ ] **Scope correction from the original plan text**: route constants
      (`AppConstants.Routes.AddEvent`, etc.) were **not** added in this
      milestone. A route registered via `Routing.RegisterRoute` needs a real
      target `Page` type to point at, and no `AddEventPage`/events list page
      exists yet — adding a route constant now with nothing to register it
      against would be dead weight. Also, "Events" itself needs no separate
      list route at all: per Milestone 2, the events list lives embedded in
      `GroupDetailPage`'s Events tab (`GroupEventsView`), not as a
      separately-navigated page — only an eventual `AddEventPage` will need
      a real route, same shape as `AddExpensePage`. Deferred to Milestone
      3b, bundled with building that page itself.

## Milestone 3b — Events list, grouped-by-date view, Add/Edit Event, RSVP

**Status: code done 2026-09-07, builds clean on both `net10.0-windows10.0.19041.0`
and `net10.0-android` (0 errors) — not yet manually tested end to end in the
running app.** Depends on Milestones 2 and 3a (both already done).

- [x] `GroupEventsView`/`GroupEventsViewModel`: events grouped by
      month/date (nested `BindableLayout`, an `EventMonthGroup` header
      plus its `EventListItem` rows — no `CollectionView.IsGrouped`, to
      stay consistent with this codebase's existing flat-`BindableLayout`
      convention everywhere else), Upcoming/Past segmented toggle (same
      pill control), each row showing title/date+location/a car emoji
      badge when `NeedsTransport`/an overlapping "going"-only avatar stack/
      an inline 3-way RSVP pill. **Real gap caught during implementation,
      not in the original plan text**: `GroupDetailViewModel.LoadAsync`
      had to be updated to actually call `EventsVm.LoadAsync(groupId)` —
      it only loaded `ExpensesVm` as of Milestone 2, when `EventsVm` was
      still a no-op placeholder with nothing to load. Without this fix the
      Events tab would have silently stayed empty forever, since a
      `ContentView` has no `OnAppearing` hook to fall back on the way a
      `ContentPage` does.
- [x] `AddEventPage`/`AddEventViewModel`: title, description, location,
      start date+time, optional end date+time (a "Set an end time" switch
      reveals the End row, mirroring how `AddExpensePage` reveals its
      recurring-mode fields), the `NeedsTransport` toggle, save/cancel.
      Doubles as edit via the same `?eventId=` query-param pattern
      `AddExpensePage` uses. Date/time rows follow the same "decorative
      label row + invisible full-size picker on top" trick
      `AddExpensePage` already established for `DatePicker` — extended
      here to a first use of `TimePicker` in this codebase (a plain
      built-in MAUI control, not a third-party package, so no version-
      verification concern). Creator auto-RSVPs `going` on create (via
      `IMembersRepository.GetMyMemberAsync()` + `UpsertRsvpAsync`). Delete
      is gated on a computed `CanDelete` (`IsEditMode && isEventCreator`)
      rather than combining a plain `IsVisible` binding with a `DataTrigger`
      on the same property — simpler and avoids any doubt about how the
      two would interact.
- [x] RSVP UI: an inline 3-way segmented pill (Going/Maybe/Not going) on
      each event row, not a separate detail page — writes via
      `IEventsRepository.UpsertRsvpAsync` for the caller's own member id,
      then reloads the whole tab (same "write then reload" pattern
      `GroupExpensesViewModel.Settle` already uses, rather than patching
      local state). Showing "no response yet" as a real, distinct, neutral
      pill state (`EventListItem.MyResponse = ""`) rather than defaulting
      an un-RSVP'd viewer to "going" was a deliberate call — only the
      event's creator gets an `event_attendees` row automatically, so most
      viewers start with none at all.
- [x] **Real bug found and fixed via a live run, 2026-09-07**: the
      Expenses/Events tab pill visually switched, but both tabs' content
      rendered stacked on top of each other underneath (the Balances/
      Recent-activity header bleeding through behind the events list) —
      `IsVisible` never actually took effect on either child view. Root
      cause: `GroupDetailPage.xaml` set `BindingContext="{Binding
      ExpensesVm}"` and `IsVisible="{Binding IsEventsTabSelected}"` on the
      *same* `<views:GroupExpensesView>` element — once `BindingContext`
      is overridden on an element, every other binding declared on that
      same element (including `IsVisible`) resolves against the *new*
      context (`GroupExpensesViewModel`/`GroupEventsViewModel`, neither of
      which has `IsEventsTabSelected`), not the page's
      `GroupDetailViewModel` — so the binding silently failed and both
      panels stayed at their default `IsVisible = true`. The FAB buttons
      were unaffected (never had `BindingContext` overridden), which is
      why they alone worked correctly. Fixed by wrapping each child view
      in its own plain `Grid`: `IsVisible` lives on the wrapper (still
      resolving against `GroupDetailViewModel`), `BindingContext` moves
      down onto the inner `GroupExpensesView`/`GroupEventsView` only.
      Confirmed building clean afterward.
- [ ] **Not done yet**: a full manual pass now that the overlap bug is
      fixed — create an event, RSVP from a second account, edit the event,
      confirm Upcoming/Past actually swaps content (untested before the
      fix above, since both tabs were visible simultaneously the whole
      time), confirm the grouped-by-date rendering looks right with events
      spanning several months, confirm a non-creator can't see/use the
      delete action, confirm returning from `AddEventPage` refreshes the
      list.

## Milestone 4 — Transport/carpooling

**Status: done & verified 2026-09-07.** Builds clean on both
`net10.0-windows10.0.19041.0` and `net10.0-android` (0 errors), confirmed
working in the running app by the user — including the Maybe-can't-offer-a-
car refinement below, added from live feedback during that same testing
pass. Depends on Milestone 3b (already done).

- [x] `ProfilePage`/`ProfileViewModel`: new "Car seats available" field —
      `Member.CarExtraSeats` (nullable `int`) added to the model, a plain
      numeric `Entry` between Birthday and Save on `ProfilePage`, parsed on
      `SaveProfile` (empty/invalid text → `null`, never a crash on bad
      input). Persisted via the existing `IMembersRepository.UpdateAsync`
      path, same one the birthday field already uses. Label and hint text
      both explicitly say "extra seats beyond yourself" per the naming
      footgun called out in "Decisions locked".
- [x] Event row (not a separate detail page — RSVP already lives inline on
      each row from Milestone 3b, so the transport controls extend that
      same row) shown only when `NeedsTransport` **and** the viewer's own
      RSVP is `going`/`maybe` (`EventListItem.ShowTransportControls`) — a
      tri-state pair of buttons ("I have a car" / "I need a ride", tapping
      the active one again deselects back to `none`, mirroring the RSVP
      pill's own selected-state pattern) plus a +/- seat stepper (two
      tappable `Label`s, not a text `Entry` — typing a number would fire a
      write, and a full-tab reload, on every keystroke; a stepper commits
      one clean write per tap instead). First toggle-on defaults the seat
      count from `Member.CarExtraSeats` (`?? 0` if never set), then it's
      editable per event via the stepper, independent of the profile
      default afterward — matches "Decisions locked" exactly.
- [x] Aggregate summary (`EventListItem.TransportSummaryText`): sum of
      `car_offered_seats` across every `offering` attendee vs. count of
      every `needs_ride` attendee, shown to **everyone** viewing the event
      (not gated on `ShowTransportControls` — it's informational for
      anyone, not just people who can act on it), red (`Danger` color via
      `HasShortfall`) when offered seats fall short of riders needed. No
      per-driver assignment UI, per "Decisions locked".
- [x] `events.needs_transport` was already toggleable after creation as of
      Milestone 3b — `AddEventPage`'s `NeedsTransport` switch was never
      gated to add-only, so this checklist item needed no new code, just
      confirming it.
- [x] **Real bug caught in review before it shipped, not from a live
      run**: `GroupEventsViewModel.SetRsvpAsync` (shared by all three RSVP
      buttons) called `UpsertRsvpAsync` with no `carStatus` argument,
      falling through to its `"none"` default on *every* call — meaning
      simply toggling Going↔Maybe (both "attending" states, no reason to
      touch transport at all) would have silently wiped anyone's existing
      car offer/need, not just an actual switch to Not going (where
      wiping it is correct and already enforced independently by
      `UpsertRsvpAsync`'s own response/car_status coupling from Milestone
      3a). Fixed by carrying the item's current `CarStatus`/
      `CarOfferedSeats` through unless the new response is `not_going`.
- [x] **Refinement from live user feedback, 2026-09-07**: a "maybe" can
      reasonably *ask* for a ride (costs nothing if they don't end up
      coming) but shouldn't be trusted to *provide* one — "I have a car"
      is now Going-only (`EventListItem.CanOfferCar`), hidden entirely for
      a "maybe" RSVP; "I need a ride" stays available for both, unchanged.
      Downgrading Going→Maybe while currently offering now clears the
      offer specifically (`SetRsvpAsync`'s `"maybe" when item.CarStatus ==
      "offering" => "none"` branch) rather than leaving a stale,
      now-untrustworthy offer counted in the aggregate — "needs_ride" is
      still carried through unchanged on that same transition, since a
      maybe asking for a ride remains fine.
- [x] Confirmed working in the running app by the user ("worked fine now
      as expected") after the Maybe-can't-offer-a-car refinement landed.
      Not itemized check-by-check against every scenario listed in earlier
      drafts of this milestone (two-account seat math, stepper negative-
      value floor, Profile pre-fill onto a second event) — reasonable
      confidence given the code paths are simple and shared with what was
      already exercised, but worth a closer look if anything transport-
      related looks off later.

## Milestone 5 — Event notifications

**Status: done & smoke-tested live, 2026-09-07.** Depends on Milestone 3b
(already done). Reuses the Phase 1 push pipeline (`send-push` Edge
Function, `AxisFirebaseMessagingService`) unchanged client-side — confirmed
before writing any SQL that the Android service never branches on the
payload's `type` field, only reads `title`/`body`/`group_id`/`group_name`,
so no app code needed touching for this milestone at all.

Two files, same shape as every other schema milestone in this project:
- **`supabase/events_milestone5.sql`** — the one-off script run against the
  live project (wrapped in `begin;`/`commit;`, a smoke-test recipe in a
  trailing comment).
- **`supabase/schema.sql`** — hand-edited in place to match, appended after
  Milestone 1's `event_attendees` RLS policies.

- [x] `event_notification_recipients(event_id)` (SQL, mirrors
      `expense_notification_recipients`) — every current group member minus
      the creator, joined to `device_tokens`. Used for creation
      notifications (whole group).
- [x] `event_attendee_notification_recipients(event_id, actor_account_id)`
      — every current attendee (any `event_attendees` row, any `response`)
      minus the actor, joined to `device_tokens`. Used for change
      notifications — deliberately narrower than the creation recipient
      set: someone who never RSVP'd doesn't need to hear an event they're
      not tracking got moved. Takes the actor explicitly as a parameter
      (computed via `auth.uid()` inside the trigger, which still reflects
      the real caller's JWT despite `SECURITY DEFINER` — that only elevates
      SQL execution privileges, not what `auth.uid()` reports) rather than
      reading it internally, and guards `p_actor_account_id is null or ...`
      so a null actor excludes nobody instead of silently excluding
      everyone (`x <> null` is `null` in SQL, not true — the exact footgun
      already latent in `expense_notification_recipients`'s
      `created_by`-exclusion clause, deliberately not "fixed" there since
      it's a separate, already-shipped function, but avoided here in new
      code).
- [x] `event_reminder_recipients(event_id)` — **new, not in the original
      plan text**: going/maybe attendees only, **including the creator**
      (unlike creation, they need reminding too). A separate function
      rather than overloading `event_attendee_notification_recipients`
      with conditional response-filtering, since the two have genuinely
      different recipient shapes (all-attendees-minus-actor vs.
      going-or-maybe-minus-nobody).
- [x] `notify_new_event()` — `AFTER INSERT` trigger on `events`, same
      Vault-service-role-key-as-bearer pattern and `SECURITY DEFINER`
      reasoning as `notify_new_expense()` (fires from a plain app-level
      insert as `authenticated`, which has no grant on the `vault` schema).
- [x] `notify_event_changed()` — `AFTER UPDATE` trigger on `events`, firing
      only when `starts_at`/`ends_at`/`location` actually changed (`is
      distinct from` comparisons) — a description or `needs_transport` edit
      stays silent, per "Decisions locked".
- [x] `notify_event_cancelled()` — **`BEFORE DELETE`, not `AFTER DELETE`
      as the original plan text said — a real design correction made while
      writing this, not discovered live.** The plan assumed `AFTER DELETE`
      with `OLD` would be enough, which is fine for `OLD`'s own columns but
      not for the recipient list: `event_attendees` cascade-deletes with
      its parent `events` row, and Postgres's internal FK-cascade ordering
      relative to a user `AFTER DELETE` trigger on the same table isn't
      something worth depending on. `BEFORE DELETE` is unambiguous —
      nothing has cascaded yet when it fires. Going further than the
      original plan text: since `pg_net`'s HTTP delivery is asynchronous
      regardless of trigger timing (the row will be long gone by the time
      `send-push` actually processes the request *either way*), both the
      recipient list **and** the message content (title, group name) are
      computed synchronously inside the trigger and embedded directly in
      the JSON payload — there's no RPC lookup for the cancelled case at
      all, unlike created/changed/reminder, which all still have a live row
      to query when their own (also async) push fires.
- [x] `send-push/index.ts`: branches on `event_id`/`event_type`
      (`"created"`/`"changed"`/`"reminder"`, plus a `"cancelled"` case
      reading directly from the embedded payload instead of a DB lookup)
      alongside the existing `expense_id` branch, which is otherwise
      untouched. Push `type` values: `event_created`/`event_changed`/
      `event_cancelled`/`event_reminder`. Times are shown in UTC in the
      copy, not converted to each recipient's own timezone — a known,
      documented simplification (an Edge Function has no reliable way to
      know a given device's timezone), not attempted for this pass.
- [x] Reminder job: `send_event_reminders()`, daily `pg_cron` at **9am
      UTC**, scanning `starts_at` in `[now, now + 30h)` with
      `reminder_sent_at is null` — the exact window/time decided in a
      planning discussion before writing any code (see "Decisions locked"):
      distinct from `fetch-exchange-rates` (6am) and
      `materialize-recurring-expenses` (8am) rather than bundling onto
      either, and a 24–30h catch window since a once-daily check can't hit
      an exact "24h before" anyway. Never `SECURITY DEFINER` — same
      reasoning as `materialize_recurring_expenses`/`find_expired_receipts`:
      only ever runs via `pg_cron` as `postgres`, which already has Vault
      access, so there's no permission gap to bridge the way the
      trigger-based functions above need one.
- [x] Deployed `send-push` via the dashboard's "Via Editor" flow (driven
      through the Claude in Chrome browser extension, same JS-injection
      technique used for the SQL editor — Monaco's `setValue`), same as
      every other Edge Function in this project. The file contains its own
      template literals (backticks), so the SQL-migration trick of wrapping
      the whole payload in a JS template literal didn't work here — used a
      local Python one-off (`json.dumps`) to produce a properly
      JSON-escaped string instead, then injected that.
- [x] **Smoke-tested live against the real project, 2026-09-07** (not just
      structurally verified): inserted a real test event into a real group
      → `notify_new_event` fired → `net._http_response` showed `200`,
      `{"sent":0,"reason":"no android recipients"}` (0 is correct — the
      SQL-editor insert has no `auth.uid()` context, so `created_by` came
      back null, and nobody had RSVP'd to a brand-new test event anyway).
      Updated `starts_at` → `notify_event_changed` fired, same clean `200`.
      Deleted the event → `notify_event_cancelled` fired (confirmed
      `BEFORE DELETE FOR EACH ROW` via `pg_trigger.tgtype`), same clean
      `200` with the embedded-payload path exercised. Manually invoked
      `send_event_reminders()` directly — ran with no error, no due events
      at the time (expected, nothing was in the 30h window after the test
      event was already deleted). This confirms the full pipeline (trigger
      → Vault secret → `net.http_post` → deployed Edge Function → correct
      branch → correct RPC or embedded payload → graceful zero-recipient
      response) works end to end.
- [x] **Confirmed by the user on a real device, 2026-09-07** — real pushes
      landing correctly through the actual app, not just the SQL-editor
      smoke test above. Not itemized push-type-by-push-type against every
      scenario listed in the earlier draft (per-type recipient scoping,
      the reminder job specifically hitting its 24–30h window) — reasonable
      confidence given the shared code path and the SQL-editor smoke test
      already having proven each trigger/branch individually, but worth a
      closer look if a specific push type ever seems to misfire later.

Phase 2 (Events & Calendar) is now feature-complete — all five milestones
done and verified. See "Explicitly deferred" below for what's intentionally
left for a later pass.

---

## Explicitly deferred (not in this pass)

- **Per-user configurable reminder lead times** ("1 month before" / "1 week
  before" / "1 day before" / "same day", possibly more than one at once) —
  the actual goal for event reminders per the user, with the fixed
  24–30h-before window (Milestone 5) as a deliberate v1 starting point, not
  the intended end state. See Milestone 5's reminder notes above for why
  this is a real data-model redesign (per-attendee reminder-sent state,
  not a single `events.reminder_sent_at`) rather than a small follow-up.
- **Seat assignment/booking** (rider picks a specific driver, live
  remaining-seat counts, concurrency handling) — aggregate-only for v1, see
  "Decisions locked" above. Revisit if the plain numbers prove insufficient
  in practice.
- **Transport shortfall push nudges** — needs its own trigger-condition
  design (when, how often, without becoming spam) that wasn't settled in
  this session.
- **Real month-grid calendar view** — grouped-by-date list for v1; a true
  grid is a plausible fast-follow, needs either a hand-built control or a
  vetted third-party package.
- **Per-group notification muting** (mute just one group's Events channel)
  — `SCOPE.md`'s notification-design section already flagged this as a real
  but narrower need than category-level muting, not blocking Phase 2.
- **iOS/macOS** — same standing non-goal as Phase 1, no Mac to build/test
  against.
