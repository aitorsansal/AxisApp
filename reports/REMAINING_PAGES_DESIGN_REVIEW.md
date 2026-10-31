# Remaining Together pages

Date: 2026-10-31. Deployed from source commit b2367da. See [deployment receipt](./WEB_DEPLOYMENT_2026-10-31.md).

## Delivered

- Event Detail separates the date/location/description, your response and transport, attendees, and linked expenses into filled sections. Group context, lighter headings and compact actions follow Together. Desktop uses a plan/expenses column beside attendees; phones stack the sections. Birthday detail omits response and attendee sections because birthdays do not request attendance.
- Event creation/editing uses the same filled sections as expense forms, with event details and date/transport controls clearly grouped. Group context comes from the existing groups context. Explicit Cancel uses the existing unsaved-change guard.
- Group Events cards and Upcoming/Past controls use the same type, surfaces and response buttons. Transport controls expose selected state; the seat targets stay at least 44px.
- Members puts the roster first and invitation/guest controls alongside it on desktop, below it on phones. Name buttons have a visible pencil. QR, use/expiry editing, explicit copying, phantom linking and guest entry retain their existing handlers.
- Recurring expenses show a clear Active/Paused label, schedule context and the existing Pause/Resume/Delete actions. The existing explanation that automatic entries create expenses rather than transfer money is visible.
- Member profiles use one neutral card per shared group, with a readable directional balance, its signed explanation lines and shared expenses. Group names link to the group; names and amounts wrap. Headline amounts use absolute values plus the existing explicit direction; the explanation retains signs.

A shared TogetherPage.css holds the common page/panel/header/form styling for these pages. No financial aggregation, recurrence algorithm, event serialization/coalescing, repository, database policy or native Android code was changed.

## Verification

- Production TypeScript/Vite build passed after cleanup; existing bundle-size advisory remains.
- Web regression suite: 32 passed, including attendance transitions, update ordering, date handling and financial rules.
- Lint passed with existing warnings and no errors.
- 62 isolated browser layout cases passed without document or inspected-element horizontal overflow. Coverage: Event Detail, birthday detail, new/edit event, Members, recurring list, member profile and Group Events at 320/390/768/1024/1440; long names/content at 320/390 on four main pages; empty event/recurring/profile states at 320/1440; all eight routes in Spanish at 320.
- Desktop and 390px screenshots for Event Detail, event editing, Members, recurring expenses and member profiles were inspected. A headless browser with its own temporary profile captured fixture pages only. Full-page phone captures include fixed navigation at the captured viewport edge.
- Event Detail: Going saved; offering a car used the profile's three-seat default; increasing seats and changing to Maybe cleared the offer; requesting a ride and then declining cleared transport. The peer seat/rider totals returned to their starting values. Writes were captured in memory.
- Event copy passed the correct production-format event URL to the fixture clipboard; expense search filtered the supplied activity; Add expense retained the eventId query.
- Event editing rejected an end before the start before sending any request. Dirty Cancel opened the discard prompt; Keep editing retained the draft. After correction, a mocked edit sent one PATCH with valid start/end timestamps and transport enabled, then returned to Event Detail.
- Members: opening name editing and pressing Escape restored focus to its original button without a write. Enter added a guest through exactly one members insert and one group_members insert; no invitation was created and the clipboard stayed untouched. Explicit Copy link wrote only to the fixture clipboard. Increasing uses sent the expected max_uses patch.
- Recurring Pause sent is_active=false and displayed Paused without a next-run date; Resume sent is_active=true and restored the next date.
- Member profile Show all expanded 10 rows to 12. Each of the three group breakdowns reconciled with its directional headline amount.

Raw layout receipts and screenshots: [remaining-pages-review/](./remaining-pages-review/).
Selected interaction receipts: [event edit](./remaining-pages-review/event-edit-receipt.json), [guest add](./remaining-pages-review/guest-add-receipt.json), [profile reconciliation](./remaining-pages-review/profile-receipt.json).

## Fixture scope and limits

All interaction checks used qaPages fixtures, which capture writes and clipboard calls in memory. No live account, group, event, invite, alias, recurring schedule or clipboard was changed. Fixture monetary edges are derived from its invented ledger so profile examples reconcile; this is test data, not a backend authorization or money-calculation retest.

These checks do not replace a physical-phone, spoken screen-reader or real multi-client attendance test. Native sharing and destructive confirmations were not invoked. The unchanged permission enforcement has separate live evidence in EVENT_RLS_REVIEW.md.

Normal application: http://localhost:5173/.
Isolated fixture: npm.cmd run dev --prefix webapp -- --config tests/visual.vite.config.ts --port 5187.
Use qaPages=default, long, empty or many. qaPages=save-error injects a failed page write. These are development fixtures; never deploy with the fixture configuration.
