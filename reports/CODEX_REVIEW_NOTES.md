# Codex user review notes — 2026-10-27

Account: codex@test.com. UI-only web test at localhost:5173. No new accounts. Android pending.

## Verified
- Login successful; both assigned groups visible.
- Codex QA: created phantom Codex Alex (cf73be74-4d67-403d-8e71-12e40c1ee1ad) and Codex Bea. Adding each also generates pending invitation and auto-copies invite link.
- Expense Codex rounding dinner (1ff93a96-f472-4696-9ff0-7d9ab5b0183f): EUR10 equal 3-way => Bea 3.34, Alex 3.33, self 3.33. Correct balances.
- Reopened expense loads as Manual rather than Equally. Change total to 12 leaves old shares and Save errors: Every participant needs a share above 0, and shares must add up to the total. Selecting Equally resolves; saved EUR12 => each 4.
- Bea Settle button immediately records full EUR4 with no preview or amount/date prompt. Settlement 3f480e78-a406-4176-b7f5-3bdf72bbe7ac editable. Edited to EUR2, remaining Bea debt EUR2 correct.
- Event Codex weekend hike 37483f6e-750c-4471-a23d-9a2a4b60d7b6: Oct31 2026 09:00–18:00, Codex meeting point, transport enabled. End equal to start correctly rejected. Event save returns to Expenses tab, event hidden until selecting Events again.
- Car offer begins with zero seats; +1 updates totals. Not going removes car and seat offer; Going again does not restore offer. Singular grammar: 1 seats offered / 1 free seats.
- Event expense preselects going users only. Can manually include phantoms. Codex hike snacks 59a7bcdd-a437-405a-8bd6-3f00af2bccb9 EUR30, Alex paid, equal 3-way. Saves back to event; appears linked.
- Event has no visible phantom RSVP management. Cannot include named guests as attendees via detail view; manually adding to expenses possible. Feature opportunity.

## Candidate findings
- Medium: equal split mode lost on edit, forces manual recalculation/reselection.
- Medium UX: immediate full settlement; partial repayment requires saving full then editing.
- Low UX: event creation return route loses Events context.
- Low UX: auto-copy invites when adding phantom surprises clipboard and conflates phantom with invite.
- Low: date formats inconsistent (group Oct27 vs event expense 10/27/2026; native date inputs DD/MM/YYYY; event 12-hour clock).

## Test data left in place
Codex QA group 5a14858a-3aae-4a8e-b44a-7851c315a078. All above records retained by user request.

## Web review completed — additional results
- Stats: Food spend EUR42 across 2 expenses, EUR14 share each; payer totals Alex30/self12. Settlement excluded from spend; settle-ups count 1. Accessible chart table works. CSV export clicked but downloaded file contents not verified.
- Repeating template baba221a-8bbd-4f58-86e3-e5819ae78363: Codex monthly subscription EUR15, equal 3-way, Subscriptions, monthly starting Oct27. Creation shows Next Oct27. Paused successfully, retained. Actual scheduled generation not verified.
- Mixed group: Codex USD shared lunch d9a0a79b-c506-4c82-b497-5a1ff5288dd4, USD20 converts to EUR17.88. Initial equal split debt EUR8.94. Changed manual shares to self USD8 / Claude USD12 => Claude owes EUR10.73. Invalid sum (8+10) correctly rejected. Reload preserves final values/session.
- No exchange rate, rate date/source or manual conversion adjustment visible in expense creation/edit form. Feature opportunity: explain conversion and allow actual bank rate where appropriate. Not claiming fetched rate is wrong.
- Negative amount -5 correctly rejected by native browser constraint (Spanish text under System language).
- Unsaved-change loss: appended UNSAVED to USD expense description, clicked Profile sidebar, navigated immediately with no warning. Reopened expense shows original description. Confirmed silently discarded. Also abandoned final reproduction EUR15 total on dinner without saving; dinner remains EUR12.
- Search: unmatched query displays No expenses match your search; lowercase usd matches uppercase USD description. Search is delayed/debounced, not immediate.
- Profile saved display name Codex QA User and car seats3. Name propagates to sidebar/payer. Shared event car offer picks up 3 seats correctly.
- Shared event 449f672a-cdb4-4f67-b9a8-980f58708ef3: Codex shared QA picnic, Nov1 2026 12:00, Codex park, transport enabled. Self Going and offers 3 seats. Retained for Android/cross-client testing.
- No Claude changes observed during this review; concurrent two-user synchronization and conflicts not proven.
- Reproduced equal-split issue again after reload: saved EUR12 equal expense opens Manual, total15 leaves 4+4+4, Save rejected with Remaining3. Evidence saved then navigated away without saving.

## Evidence
- codex-evidence/web-shared-expense.jpg: mixed group, USD20/EUR17.88, Claude owes10.73.
- codex-evidence/web-equal-split-edit.jpg: manual mode on originally equal expense, Remaining3 and validation.

## Report classification guidance
Confirmed behavior bugs/rough edges: equal split editing; silent unsaved loss; event save redirects Expenses. Product opportunities: preview/partial settlement, FX transparency, guest RSVP controls, percentages/weights and multiple payers (current form offers Equally/Manual and single payer), filtering beyond description/search and date-window stats, visible expense change history/comments (not found). Do not claim security bugs from absent UI or financial corruption; tested sums reconciled.

## Scope limitations
No new accounts; no source-code review or changes; no deletion/dissolution, ownership transfer, auth changes, notification permission, actual money transfer, receipt upload, offline tests, or real simultaneous conflict tests. CSV content and print output not verified. Desktop browser only so far; Android pending. Final REPORT_CODEX.html deliberately deferred until Android review completes per request. Notes and screenshots retained; test records retained. Browser tab marked for continuation.

## Android review — 2026-10-27
Device via ADB: model 2412DPC0AG (rodin); package com.aitorsansal.axisapp; Spanish system UI. Account codex@test.com. No new accounts.
- Login works and web-created records/balances/profile visible. Initial Codex QA Bea+2, Alex-6; mixed Claude+10.73.
- Same equal-split edit issue: EUR12 dinner opens Manual with4+4+4. Total15 leaves Remaining3, top Save does not save. Cancel discards with no warning; stored dinner remains12.
- Created Codex Android coffee EUR7.50 using comma input 7,50, self payer, equal 3-way2.50 each, Drinks category. New balances Bea+4.50 / Alex-3.50; net+1. Web reload confirms matching expense 6af036fa-1dcf-49d8-b634-4571a791df24 and balances.
- New coffee immediately displays hace14h in recent activity. Settlement displays ahora mismo although created earlier. Likely date-at-midnight used for relative expense age; report as confusing occurrence-date presentation, not proven wrong creation timestamp.
- Android detail/edit share and edit icons expose private-use glyphs with empty content-desc. Native UI hierarchy shows chart headings but omits visual category/value data. Full TalkBack audit not performed; likely accessibility gaps.
- Event hike edited location to Codex meeting point - Android checked; saved to detail view. Selected Necesito coche; total1 correct and attendee shows need ride.
- Event-linked Android add expense lists ONLY self as payer/participant (only going attendee). Web form permits all3 group members with attendees preselected. Cannot select Alex as payer through this Android form even though existing linked expense was paid by Alex. Cancelled blank form.
- Created Codex Android planning, Oct30 2026 15:29, no end/location/description. Native date picker works. Top Save did not navigate on first attempt after editing date; bottom Save worked. Do not present header-save failure as confirmed without retest. New event visible in same Events list (better than web return-to-Expenses).
- Android stats show Food42/Drinks7.50 visually; settlements excluded. Menu contains members/color/rename/transfer/dissolve, no Repeating expenses management entry found. Add expense has Repeat. Existing paused template management not found in tested navigation; feature-discoverability gap, not confirmed unsupported.
- Members and invitation page load; existing phantom invites present, resend controls and single-use invite expiry settings. No invitations sent externally, no new phantom added on Android.
- Profile reads Codex QA User and seats3 from web. Top groups avatar shows CT while profile/group member avatar CQ; minor inconsistent initials.
- Cross-client live test: web mixed Events opened Going with3-seat car. On Android selected Maybe. Android now shows0 offered and no car offer controls. Web initially remains Going and3 seats without reload; pending final refresh check.
Evidence: android-group.png, android-edit.png, android-equal-split-edit.png, android-created-expense.png, android-event.png, android-new-event.png, android-stats.png. Screenshots show device status bar; no login credentials included.

## Final cross-client result
Android16, app1.0.2 (versionCode9). Web mixed Events remained Going/3 offered seats after Android Maybe until manual reload (observed across subsequent note/evidence capture calls). Reload plus selecting Events shows Maybe/0 seats. Confirmed stale live attendance display; persistence works. Evidence web-stale-attendance.jpg versus android-shared-maybe.png.
Claude concurrently added Claude: cervezas compartidas EUR15; after web reload visible as paid by claude@test.com, mixed balance3.23 = prior10.73 -7.50. No changes made to Claude record.
Android final location mixed group Events, picnic Maybe/0 offered. Codex QA final Bea+4.50 / Alex-3.50. Dinner remainsEUR12. CoffeeEUR7.50 Drinks. Hike location Android checked, self needs ride. Monthly template paused. New planning event retained.

## Report delivered
REPORT_CODEX.html generated as a standalone file with8 embedded images,10 findings,20 exploratory coverage entries,6 feature opportunities, reconciliation and retained data inventory. Browser preview verified desktop layout, embedded evidence, Android/P2 filtering (3 results), and unmatched search state. HTML audit verified image MIME/signatures, unique IDs, no external resources and no supplied password. Report print control included; printed output not verified. No application source files changed.
