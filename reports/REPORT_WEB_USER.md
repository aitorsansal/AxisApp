# Axis: production web user review

Reviewed 27 October 2026 in the signed-in Brave session at https://app.axisapp.aitorsansal.com/. This supplements the earlier Claude and Codex reports. The combined HTML report merges repeated findings and retains the evidence boundaries below.

## User verdict

I would use Axis as a shared expense ledger with event planning for a trip or a small ongoing group. It is less complete as an everyday manager for several groups. Its strongest screens explain money within a group; its home screen gives too little help deciding what needs attention across groups.

## Worth keeping

- Clear owed/owing labels and group balances.
- The person-to-person explanation: EUR30 paid for Alex minus EUR10 Alex paid for me correctly yielded EUR20 owed.
- Named guests without accounts, so everyone need not register before tracking costs.
- Attendance, transport and linked expenses on one event page.
- Statistics that excluded the EUR20 repayment from EUR120 spending in the checked checkpoint.
- Readable phone dashboard and event layouts at 390 pixels wide, including a longer group name.
- Keyboard group reordering with arrow keys; the original order was restored.
- Working CSV downloads and a useful localised unknown-page recovery screen.

## Findings and reproduced issues

U01: Production became blank after reload and reported missing VITE_SUPABASE_URL / VITE_SUPABASE_PUBLISHABLE_KEY. The user restored deployment and a fresh production tab worked. This incident is recovered, not a current-outage claim.

U02: Adding each of QA Alex and QA Bea cleared the input but left the current member list unchanged. Reopening showed that the additions had saved. A later browser-tool clipboard-bridge error complicates attribution. Retest manually with the document focused and with clipboard failure; do not describe this as proven data loss.

U03: The recovered session logged Firebase Installations missing projectId. Ledger workflows worked. Notification permission and delivery were not tested.

U04: The longer review-group name caused horizontal scrolling in the desktop sidebar. Its phone dashboard card wrapped readably.

U05: The CSV contained the two expenses and repayment present at export time, but no participants or individual shares. It is an expense-list export, not enough to reconstruct all debts independently. The retained CSV predates the later EUR6 snacks expense.

U06: Home exposes group cards and net balances, without a combined agenda or action overview. This is a product gap and judgment, not a calculation defect.

U07 → Claude W4/A2 and Codex F07: Settle immediately recorded Alex's full EUR20 repayment without review or partial-amount entry. No actual money moved.

U08 → Codex F02: Two production web tabs using the same account disagreed about attendance after Going changed to Maybe. The second tab remained stale across subsequent checks; reload showed Maybe. This adds a web-to-web reproduction, not a separate defect or simultaneous conflict test.

U09 → Claude W11 and Codex F06: Creating an event returned to Expenses. A supplementary read-only test on the already-open alternate deployment also lost past-event context on Back. That deployment used a different account; its observations must remain separate from production acceptance results.

U10 → Claude W12: An invalid invitation displayed “Invalid invite code” within the Spanish interface. Submission recovered and no group was joined.

## Dashboard direction

The user wants useful information immediately and suggests removing central group cards on desktop because groups are already in the sidebar. I agree with that direction. A dense analytics wall would hide the actions that matter; an actionable overview should lead, with deeper analytics below or behind a separate view.

Suggested order:

1. Explicit scope: all groups or one group, with currency and timeframe where relevant.
2. Separate amounts owed to me and amounts I owe, with a person/group breakdown. Never cancel distinct obligations into one net number or add different currencies without an explicit conversion policy.
3. What needs my attention: unanswered plans, relevant invitations and repayments to review. Some of these require new workflow state; do not invent overdue, unread or confirmed statuses.
4. Upcoming plans across groups, including date, group, my RSVP and transport availability where enabled. Link directly to the event.
5. Recent meaningful changes with person, group and timestamp. Reliable history and live refresh must support any promise that this is current.
6. Optional spending trends below the actionable information. Clearly separate group spending, my paid amount, my share and repayments.

Desktop can move the group cards out of the main area. Mobile still needs a compact, obvious group switcher because the sidebar disappears. Preserve sensible empty/loading/error states and show data freshness. This is proposed direction only; the app was not redesigned in this review.

Other useful product opportunities: direct balance-to-person-detail navigation; archiving completed groups (not found in inspected controls); person/category/event/date/repayment filters; organiser-managed guest attendance (not found in the inspected event view).

## Acceptable as it is

The dark visual direction, equal/manual splits, a single payer per expense, simple event details and daily/weekly/monthly/yearly recurring choices are reasonable foundations. Complex split methods and broader collaboration features can wait. The report does not claim that previously reported editing or validation issues are fixed.

After recovery, the small-group navigation and saves felt responsive enough. No speed benchmark, large-ledger test, slow-network test or numerical performance score was produced.

## Retained test data and reconciliation

Only a new isolated production group was modified in this session. Earlier shared-group records were not edited. No application source was changed during the review.

Group: QA — Weekend group review, `cf7c5504-6a30-42e0-bcbb-d81058ae9dbc`, EUR, green with a camping icon.

Members: existing signed-in user; QA Alex (`e06b82ea-4ad7-4837-8010-2c441a7dc847`); QA Bea (`7af3eb9b-9014-4ba4-bce0-cf13fcf9bd22`). Named guest creation generated pending invitations; none was sent to an external recipient.

| Record | Amount and effect | ID |
|---|---|---|
| QA weekend accommodation | Self paid EUR90, three equal EUR30 shares | `a2eb2101-3954-43b1-9511-3626db3003bb` |
| QA shared fuel | Alex paid EUR30, three equal EUR10 shares | `df680c37-244b-41ed-86ca-4e33d5aa280f` |
| Saldar cuenta | Alex repaid self EUR20 | `a0d8b86b-f6ef-4a7b-8808-f177ea3a60bf` |
| QA hike snacks | Self paid EUR6, three equal EUR2 shares, linked to outing | `eab1cab8-5b9d-49f6-a9b3-622ac3abc29d` |

Final observed balances: Bea owes self EUR32; Alex owes self EUR2; the dashboard shows EUR34 owed to self. The arithmetic is consistent with this sequence. Final expected spending is EUR126; the inspected statistics checkpoint was EUR120 before snacks.

Event: QA Saturday hike (`6f7e75c9-d255-4434-a0fc-aeedafc88d7a`), 7 November 2026 at 10:00, QA trailhead, transport enabled, self Maybe. No repeating schedule was created. Test records remain for inspection.

## Evidence and limits

Evidence lives in [web-user-review-2026-10-27](web-user-review-2026-10-27/). Images 02 and 03 are supplementary alternate-deployment checks. Image 10-mobile-expense.png is a malformed automation capture and is excluded from conclusions and the combined report. Screenshots are checkpoints, not a claim that every pictured balance is final.

Browser inspection ended because computer use could not safely verify the current Brave URL. Offline recovery, concurrent conflicting edits, notification delivery, recurring scheduled execution, print output and destructive lifecycle actions were not established. The browser time-input fill attempt did not reliably change the field; accessible input subsequently saved 10:00 successfully. That automation problem was not classified as an application defect.
