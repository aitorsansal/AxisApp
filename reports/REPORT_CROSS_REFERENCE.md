# Axis QA: a guide to both reports

Prepared 27 October 2026 and extended with the later production web user review. Sections 1–8 compare the two original reports; section 9 records the later session and product direction. Original source-code claims have not all been independently verified. The combined HTML merges repeated observations and labels later evidence separately.

Read this guide first, then open the original finding when you need reproduction steps or screenshots:

- [Claude's report](REPORT_CLAUDE.html): broader exploratory coverage, with some source-code investigation.
- [Codex's report](REPORT_CODEX.html): narrower controlled scenarios, explicit balance reconciliation and confidence limits.
- [Codex's working notes](CODEX_REVIEW_NOTES.md): additional detail and the sequence of observations.
- [Production web user review](REPORT_WEB_USER.md): later user verdict, direct production observations, confidence limits and isolated test-data inventory.
- [Combined report](REPORT_COMBINED.html): all findings, user perspective and dashboard direction in one interface.

## Current implementation status — 28 October 2026

**30 October update:** W6, W12 (partly), W14, W15, W17 and W18 have new statuses, and the web invite card, Enter-to-add-member and zero-decimal currencies were added. See the first section of [the status review](REPORT_STATUS_REVIEW.md) and the combined report cards; the paragraphs below keep their original checkpoint wording.

**F02 update:** production web RSVP/seat propagation passed between open detail
and list views. Recovery/race hardening is deployed. Android polling was
confirmed on the physical phone on 29 October (about a minute delay; phone-to-web
instant; Wi-Fi reconnect refresh did not visibly trigger). See
[attendance verification](LIVE_ATTENDANCE_REVIEW.md); the isolated QA event was
restored to Going, no car offer and transport disabled.

**F03 update:** event and repayment editors now have unsaved-change guards on
both clients. Additional web browser checks pass; the Android guard was confirmed
on the physical phone on 29 October (native alert, not itemised per form).
See [form verification](FORM_UNSAVED_REVIEW.md) for evidence and release status.

**U01 update:** startup safeguards are deployed: missing configuration blocks
builds, browser startup failures show a retry screen, and the npm deployment
pipeline checks the expected home/profile shell and entry asset. Local failure
and production startup checks passed. See [verification](STARTUP_REVIEW.md).

**Later update:** the web fixes have now been deployed and the financial flows checked against production. See [production verification](RELEASE_MONEY_REVIEW.md) for save/reopen, split intent, repayment results and retained QA records. W14 still has a converted-amount boundary gap; fresh production sync/discard checks remain separate. The following paragraph describes the earlier status checkpoint.

[The status review](REPORT_STATUS_REVIEW.md) supersedes the historical open wording for A1, W4/A2/F07 and W7/A8: these are implemented with prior Android QA evidence. W8/A7, W13 and W14 have narrower fixes or remaining verification. Both regression suites pass. Live web deployment was not checked. W1/W2 now have local date-validation fixes and browser checks. Split-mode persistence is implemented on both clients; the user confirmed SQL/web operation and Android device checks passed. Calendar timestamp formatting is now deployed and verified against the live QA feed; earlier timezone and split-rounding fixes addressed different problems. Local web expense and recurring editors now have unsaved-change protection, checked with isolated browser fixtures; see [verification](UNSAVED_WEB_REVIEW.md). The web release was later deployed. Android expense/recurring protection has been device-verified, and event and repayment forms now have guards too (see F03 update above). F02 (stale attendance) now has a web fix using Supabase Realtime plus focus/poll refetches, confirmed by the user locally and since deployed. The combined HTML now shows these statuses alongside original evidence.

## 1. What the combined review tells us

**The tested arithmetic generally reconciles, but getting the right information into the ledger and keeping screens understandable need work.** Correct arithmetic cannot protect a user who enters 8.50 and gets 850, saves an impossible date, or accidentally records two repayments.

The reports describe four main problem areas:

| Area | What happens in everyday terms | Examples |
|---|---|---|
| Input and saved records | The app may save something different from what the user meant. | Android dot decimals; impossible expense dates; immediate repayments. |
| Editing and navigation | Editing requires extra work, or navigation hides/discards work. | Equal splits reopen as Manual; unsaved edits disappear; Events resets to Expenses. |
| Consistency between clients | Web and Android expose different choices or show different versions of the same information. | Event expense membership; legacy categories; settlement descriptions; stale attendance. |
| Clarity and accessibility | Information exists but is difficult to interpret or access. | Relative times; chart labels; unnamed icon actions; unexplained exchange rates. |

Neither report establishes release readiness. Codex's “no arithmetic discrepancy” applies to its exercised scenarios. Claude's additional input failures are compatible with that result.

## 2. Read these first

This is my proposed review order, based on consequences. It is not a mechanical conversion of either author's severity labels.

| Order | Topic and original references | Why it comes first | What would demonstrate a successful fix |
|---|---|---|---|
| 1 | **Android decimal entry** — Claude A1 | A reported 100× amount change can put the wrong debt in the ledger. | In the Spanish device locale, typing and pasting both `8.50` and `8,50` produces 8.50 with matching saved shares. Include the initial `0,00` state and an actual device keyboard. |
| 2 | **Invalid dates** — Claude W1, W2 | One path freezes; another reportedly saves an expense in the wrong month. | Impossible dates produce an inline error and save nothing. Valid dates survive save/reopen unchanged. A failed submission leaves Save usable. |
| 3 | **Accidental repayments** — Claude W4/A2; Codex F07 | Both saw immediate full repayment; Claude also reproduced a second person being settled by the next tap. | Users can review recipient and amount, enter a partial repayment, and cannot accidentally settle the next person through row movement. |
| 4 | **Calendar export timestamps** — Claude W3 | A broken feed can affect every exported event for strict calendar clients. | Generated timestamps validate, and real calendar import preserves start/end times, including explicit ends and fallback ends. |
| 5 | **Stale attendance and transport** — Codex F02; Claude's sync pass | A screen can promise seats that are no longer offered. The reports differ here. | An already-open web view updates after an Android RSVP change without reload, across repeated runs and relevant event screens. |
| 6 | **Unsaved edits** — Codex F03 | Navigation silently discards entered work. | Leaving a changed form offers an explicit discard choice or a recoverable draft. |
| 7 | **Equal-split editing** — Claude W10; Codex F01 | Both reproduced the same common editing failure. | A saved equal split reopens with its intent preserved, recalculates on total changes, and retains valid cent rounding. Explicit manual splits remain manual. |

After these, review client parity and accessibility, then validation, navigation and polish. Accessibility can be a blocker for an affected user even when a visual workflow works.

## 3. Where the reports reinforce each other

“Both observed” means overlapping behavior was reported in two sessions. It does not mean every platform or proposed explanation was independently checked twice.

| Combined topic | Claude | Codex | How to read them together |
|---|---|---|---|
| Equal split becomes Manual | W10 | F01 | Strong agreement on web and Android. The split amounts still reconcile; the editing intent is lost. |
| Immediate full repayment | W4 / A2 | F07 | Same underlying interaction. Claude adds Android coverage and the consecutive-tap failure, making this more consequential than Codex's P3 framing suggests. |
| Events context is lost | W11 | F06 | Both see creation return to Expenses. Claude extends it to reload/back navigation and reports that the tab lives only in component state. |
| Misleading relative times | A5 | F09 | Both see “14 hours ago” for recent entries and “right now” for older repayments. Claude proposes a timestamp explanation; Codex reports the observed symptom without source inspection. |
| Adding a phantom copies an invite | W17 | F10 | Same unexpected clipboard/invitation side effect. Claude additionally reports old resend tokens remaining valid; Codex does not corroborate that extension. |
| Missing visible change history | W16 | Feature opportunity | Both identify a trust gap. Claude reports history already exists in storage; Codex only establishes that no visible history control was found. Comments are a separate product idea. |
| Stale initials / identity | A10 | Secondary polish in limits | Both see CT versus CQ. Claude adds email display and identical initials for different people. |
| Inconsistent date presentation | Smaller polish items | Notes / limits | Both notice event-linked expense dates differing from surrounding dates. This is separate from invalid-date saving. |

## 4. Apparent contradictions and important limits

### Live sync: a pass and a failure

Claude reports Android expense and RSVP updates appearing instantly on web. Codex captures an open web event still showing Going / three seats after Android changes to Maybe / zero seats; reloading reveals the saved change.

**Conclusion:** live sync is not proven consistently reliable. A successful update does not invalidate a captured stale state. Different views, subscriptions, timing or navigation could explain the difference, but these are hypotheses. Reproduce with the exact screen, account, event, action time and update delay recorded.

### Decimal comma passes; decimal dot fails

Codex successfully saved `7,50` on Android. Claude reports `8.50` becoming `850`, while `8,50` works. These results agree that comma entry works; only Claude exercised the failing dot path. The suspected native input filter remains explicitly unverified in Claude's report.

### Profile updates: different refresh behavior

Codex reports the saved name propagating to the sidebar/payer; Claude reports a stale sidebar until reload. Both see stale Android initials. Check the exact save/navigation sequence before treating sidebar refresh as resolved or universally broken.

### Different labels do not mean different evidence

Claude assigns High / Medium / Low. Codex uses P2 / P3 and defines its own scope. Settlement is High in Claude and P3 in Codex; event navigation is Medium versus P3. Review the consequence and reproduction, not the label. Category-label gaps and blank group names also should not automatically inherit the same urgency as a 100× amount error just because Claude labels all of them High.

### Counts are not a backlog

Claude has 29 finding cards (7 High, 14 Medium, 8 Low), including paired web/Android IDs and a card containing twelve smaller observations. Codex has ten findings plus six opportunities. Adding 29 + 10 would double-count shared issues and still miss individual subitems. “20 verified flows” also represents different checks in each report, not one common test suite.

### A visible control is not the whole capability

Codex did not find Android recurring management or phantom RSVP controls in the tested navigation. That supports a discoverability investigation, not a claim that the features cannot exist. Claude's member-search result supports the tested access path, not a complete authorization/security audit.

## 5. Remaining findings, without duplication

These tables complete the finding cross-reference. “Claude only” or “Codex only” means only that report supplies the observation, not that the other tester disagrees.

### Claude-only findings

| Reference | Plain-language issue | Suggested treatment |
|---|---|---|
| A1 | Dot decimal can change the amount by 100×. | Review first; see section 2. |
| W1, W2 | Invalid event date freezes Save; invalid expense date rolls into another month. | Review together as related date validation failures; verify each path separately. |
| W3 | Calendar feed timestamps contain both an offset and `Z`. | Validate generated output and calendar import. Treat the reported source fix as a proposal. |
| W5 / A3 | Web permits rent/utilities that Android cannot label/style. | Agree on shared category keys and handle existing records. No category data loss was reported. |
| W6 | A group name containing only spaces is accepted. | Reject after trimming on the client and at the data boundary. Lower practical urgency than incorrect amounts. |
| W7 / A8 | Editing a repayment exposes expense-style multi-person controls. | Design with the repayment creation fix: one sender, one recipient, amount and date. Explain invalid states. |
| W8 / A7 | Custom repayment descriptions and automatic labels differ by client/language. | Define one display rule for custom text and localized automatic labels. |
| A4 | Android uses the shopping-cart icon for every expense category. | Correct category presentation alongside category parity. |
| A6 | Back from a deep-linked event goes to an unrelated group. | Verify navigation history and the expected destination. Correct event opening alone is insufficient. |
| W12 | Spanish screens expose raw English browser/server errors. | Translate known failures and provide useful fallback messages. |
| W13 | Mouse wheel changes a focused amount; decimal display conventions are mixed. | Separate unintended amount changes from formatting polish. Retest scrolling and locale input. |
| W14 | Oversized amounts reach a database overflow error. | Define supported limits, then validate amount/share fields consistently. |
| W15 | Desktop event sharing opens Windows share UI without an obvious copy-link alternative. | Product judgment: offer an explicit Copy link action; native sharing itself is not inherently defective. |
| W18 | Join screen lacks group/claimed-person preview. | Clarify what joining will do before confirmation. |
| A9 | Android chart labels collide or clip; the trend joins distant months. | Fix readability. Decide explicitly whether a missing month means zero spending or unavailable data before changing the chart. |
| A11 | Spanish money placement differs between clients. | Align locale formatting; separate from numeric correctness. |
| W19 | Profile save feedback can be off-screen and sidebar identity can stay stale. | Check visibility and refresh; see the mixed evidence in section 4. |
| W20 | Event time/end-date defaults and zero-seat car offers can confuse. | Review intended defaults and explain zero-seat states. Codex also observed zero initial seats, then successfully increased them. |
| W23 | Receipt preview crops content; abandoned picks can leave uploaded files. | Separate preview/layout work from the reported upload lifecycle. |
| W21–32 | Twelve smaller web observations are bundled together. | Split into individual items; inventory below. |
| A12 | Android settled/empty attendee sections lack explanatory empty states. | Add clear empty-state copy. |

### Codex-only findings

| Reference | Plain-language issue | Suggested treatment |
|---|---|---|
| F02 | An open web view can retain stale attendance and seats. | Investigate alongside Claude's sync pass; see sections 2 and 4. |
| F03 | Sidebar navigation discards an unsaved expense edit. | Add form protection or drafts; see section 2. |
| F04 | Android event-expense entry only offers attendees as payers/participants; web offers all members. | Agree whether non-attendees may pay or participate, then align the clients. Existing Alex-paid linked expenses demonstrate the relevant use case. |
| F05 | Android icon actions lack useful names and chart values are missing from the accessibility hierarchy. | Add meaningful semantics and accessible values, then verify with TalkBack. This was hierarchy inspection, not a full screen-reader audit. |
| F08 | The tested web expense form does not explain its currency conversion. | Show applied rate/date/source. Manual bank-rate entry is an additional product decision; neither report establishes that the conversion was wrong. |

### Unpack Claude's W21–32 bundle

The original card groups these observations; it does not assign a separate W number to each bullet. Keep the bundle reference until individual issue IDs are created.

| Observation | Why it matters |
|---|---|
| Enter does not submit the named-member input. | Keyboard convenience and form consistency. |
| Other appears before legacy rent/utilities categories. | Picker ordering; related to category parity. |
| Web checkboxes/date fields/share inputs have weak accessible names. | Separate web accessibility work from Codex's Android F05. |
| Event-linked expense date format differs. | Already cross-referenced in section 3. |
| Green balance gradient ends in mauve with weak text contrast. | Visual consistency and readability; measure contrast. |
| Default display name exposes an email to group members. | Decide the intended identity/privacy experience; no unauthorized access is established. |
| Payer line uses the user's name instead of “You paid.” | Copy preference. |
| Destructive actions use native confirms/prompts inconsistently. | Review action-specific safeguards; one confirmation is not automatically inadequate for every action. |
| Negative input is rejected but still produces negative share previews. | Invalid-state presentation, despite successful save validation. |
| Location has no map link; event expenses do not prefill the event title. | Two convenience improvements; track separately if implemented. |
| Settlement terminology varies. | Align repayment language across screens. |
| Clicking a member name to rename an alias is undiscoverable. | Make the action visible. |

## 6. Product ideas should have their own list

Some proposals improve existing defects; others add capabilities. Mixing them makes the app look more broken and the repair scope larger than the evidence supports.

| Product idea | Source | Relationship to current findings |
|---|---|---|
| Direct partial repayment | Codex opportunity; Claude W4/A2 and W7/A8 | Include in the repayment workflow review rather than counting as a second defect. |
| Organizer-managed guest/phantom attendance | Codex opportunity | Separate capability/discoverability decision; distinct from F04's expense member selection. |
| Exchange-rate explanation and optional actual bank charge | Codex opportunity / F08 | Explanation and override are separate scope decisions. |
| Weighted/percentage splits and multiple payers | Codex opportunity | New capabilities beyond the tested Equal/Manual, single-payer form. |
| Visible expense history and discussion | Codex opportunity; Claude W16 | History visibility is shared feedback. Comments are a separate feature. |
| Discoverable Android recurring management | Codex opportunity | Locate existing pause/resume/next-run controls before deciding whether implementation is missing. |

## 7. How I would make the reports easier to use

**Use one combined index, with the original reports available as evidence.** Reading both front to back repeats several issues and forces you to reconcile conflicting labels yourself.

For a more interactive version, I would use a single page with:

1. **A short overview:** what works, what can save wrong information, and the next decisions.
2. **A journey filter:** Enter expense, Edit expense, Repay, Events/transport, Members/invites, Stats, Profile, Accessibility.
3. **One card per problem:** a plain title, a concrete example, affected clients, consequence, evidence confidence, source references, and an expandable screenshot/reproduction section.
4. **Separate confidence and urgency:** “observed by both,” “observed by one,” “conflicting observations,” and “proposal” describe evidence. Priority describes what to handle first.
5. **A clear resolution status:** Needs reproduction → Confirmed → Decision needed / Ready to fix → Implemented → Retested. Agreement between reports does not by itself mean Fixed or independently verified now.
6. **A small product-ideas view:** hide feature expansion while reviewing defects.
7. **A glossary and acceptance condition:** explain terms and say how you will know each issue is resolved.

For your first pass, read sections 1–4 here, then inspect the seven priority topics in the originals. Use section 5 as the complete finding checklist. Keep record IDs, test-data inventories and implementation locations in the evidence detail rather than the main reading path.

### Useful terms

| Term | Meaning here |
|---|---|
| Ledger | Saved expenses, repayments and the balances calculated from them. |
| Reconciles | The checked totals and balances add up. This does not prove the entered amount/date was intended. |
| Settlement / settle-up | A recorded repayment between members; the reports do not describe actual bank transfers. |
| Phantom | A named group member without a connected user account. |
| Persistence | A change stays saved and can be read again later. |
| Live sync | Another already-open screen reflects that change without a manual reload. |
| Occurrence date / creation time | When the expense happened versus when someone entered it. They can legitimately differ. |
| ICS | The calendar-feed file format used to export events. |
| RLS | Database row-level access rules. A narrow passing UI test is not a full audit of those rules. |

## 8. What remains uncertain

No current-build reproduction or source verification was performed for this comparison. Claude's file/line references and suggested fixes are leads to inspect before implementation, especially the explicitly speculative Android input-filter cause. Inferring Equal mode solely because stored shares happen to be equal can also misclassify an intentional manual split; preserving split intent is a product/data-model decision.

Coverage remains incomplete: offline recovery, concurrent conflicting edits, broad authorization boundaries and recurring scheduled execution are not established by these reports. Codex did not verify CSV contents or printed output. Claude covers some areas Codex skipped, such as receipts and group dissolution; a limitation in one report does not erase coverage in the other.

Both sessions modified retained test data, including a shared group. Their screenshots and balances are checkpoints, not expected final balances for the app today. Claude's invalid-date test left an expense in March, and its accidental settlement test left repayments recorded. Account for those records when reproducing or comparing statistics.

## 9. Later production web user review

The later session reviewed the signed-in Brave app at `app.axisapp.aitorsansal.com`, read the earlier reports first, and created an isolated review group. A temporary deployment configuration failure produced a blank page; the user restored deployment and testing resumed successfully. Read-only supplementary checks on the already-open alternate deployment used a different account and are not production acceptance results.

### New evidence and scope decisions

| Reference | Result | Interpretation |
|---|---|---|
| U01 | Production became blank after reload; Supabase configuration was missing. Recovery was verified in a fresh tab. | Recovered incident, not a claim of an ongoing outage. Add release/startup checks. |
| U02 | Guest creation saved but did not refresh the current list in two automated attempts. | Manual confirmation remains necessary because a later tooling clipboard-bridge error complicates attribution. No data loss established. |
| U03 | Recovered session logged Firebase missing `projectId`. | Investigate configuration; notification delivery was not tested. |
| U04 | A longer group name caused horizontal overflow in the desktop sidebar. | Observed layout issue; phone cards wrapped readably. |
| U05 | Downloaded CSV records were correct, but lacked participants and individual shares. | Export scope limitation for independent debt reconciliation, not a broken download. |
| U06 | Home offers group cards and balances but little combined agenda/action context. | Product judgment. The user agrees that desktop centre space should provide an immediate overview. |

### Later reproductions merged into existing entries

| New reference | Existing reference | Later evidence |
|---|---|---|
| U07 | Claude W4/A2; Codex F07 | Settle immediately recorded the full EUR20 repayment. |
| U08 | Codex F02 | Going/Maybe attendance stayed inconsistent between two production web tabs until reload. |
| U09 | Claude W11; Codex F06 | Creating an event returned to Expenses. |
| U10 | Claude W12 | Invalid invitation displayed English error text within Spanish UI. |

These are four source additions, not four extra defects. Historical agreement labels retain their original meaning; the new source details and session-result badges identify later observations. Existing comparison IDs remain stable.

### Positive results and retained records

The person-to-person explanation reconciled EUR30 paid for Alex minus EUR10 Alex paid for self to EUR20 owed. Statistics excluded the EUR20 repayment from the EUR120 spending checkpoint. The CSV contents were inspected, extending the original Codex report's export coverage; the retained CSV predates the later EUR6 snacks expense.

The phone dashboard/event layout, keyboard group reordering, localised unknown-page recovery and downloaded CSV all worked in the exercised paths. Small-group saves and navigation felt responsive after recovery; no numerical performance benchmark was run.

The new group `cf7c5504-6a30-42e0-bcbb-d81058ae9dbc` retains three members, three expenses, one repayment and one event. Final observed balance: self is owed EUR34, comprising Bea EUR32 and Alex EUR2. The [full session inventory](REPORT_WEB_USER.md#retained-test-data-and-reconciliation) gives record IDs and the sequence. Earlier groups were not edited in this later session.

### Dashboard direction

Use the sidebar for desktop group navigation and put an actionable overview in the centre: explicit group/currency scope, separate amounts owed and owing, actions needing attention, upcoming plans, recent changes, and optional spending analysis below. Mobile still needs an obvious group switcher.

The Power BI analogy is useful for seeing the whole picture; a dense wall of charts would compete with the decisions users need to make. Proposed cards must not invent overdue/unread/confirmed statuses or imply that unreliable refresh is current. Some actions require new workflow state. This is product direction, not implemented behaviour.

See [User perspective](REPORT_COMBINED.html#user) and [Dashboard direction](REPORT_COMBINED.html#dashboard) in the combined report. Offline recovery, conflicting edits, notification delivery, recurring scheduled execution, print output and destructive lifecycle actions remain unverified by this later session.
