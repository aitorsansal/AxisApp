# Combined report status review — 28 October 2026

The combined report retained historical failures after commit `f4a5d7f` implemented repayment and monetary-input changes. Its affected cards now carry current implementation statuses and this evidence alongside the original observations. The original reports remain historical records.

This review checked repository source and reran existing regression suites. It did not test the live web deployment or run a fresh physical-device session. Prior Android and browser QA evidence is explicitly identified below.

| References | Current status | Evidence and limits |
| --- | --- | --- |
| A1 | Implemented; prior device QA passed | `AxisApp/Controls/DecimalInput.cs`, `AxisApp/Models/AmountInput.cs` and `webapp/src/lib/amountInput.ts` preserve dot/comma values and reject ambiguous punctuation. [Amount-input QA](AMOUNT_INPUT_REVIEW.md) records actual Android typing, native paste, save/reopen/delete, and mocked browser checks. |
| W4 / A2; F07 | Implemented; prior device QA passed | `GroupExpensesViewModel` opens `SettlementPage`; web `GroupDetailPage` opens `SettlementForm`. Opening the review performs no repayment write. [Android repayment QA](ANDROID_SETTLEMENT_REVIEW.md) covers cancellation, full/partial repayments and editing. Current tests check debt direction and amounts. Earlier production-web reproduction does not describe the later implementation. |
| W7 / A8 | Implemented; prior device QA passed | `SettlementViewModel` and `SettlementForm` provide dedicated repayment editing with distinct parties, date, amount and note. Ordinary expense editing routes settlement records to these editors. Prior Android QA exercised reversed direction and invalid amounts. |
| W8 / A7 | Partially implemented | Web group activity now uses `exp.description`, matching Android. Web `EventDetailPage` still replaces settlement descriptions with `GroupDetail_SettleUp`. Default descriptions are saved as localized text, so changing the viewer language cannot relocalize existing defaults. |
| W13 | Partially implemented | Web total and share inputs now use `type="text"` and `inputMode="decimal"`; native number-input wheel increments no longer apply. Prior browser QA checked scrolling. The bundled cross-client display-format consistency claim remains unverified. |
| W14 | Implemented input limits; broader retest needed | Both parsers enforce positive plain amounts up to `9999999999.99`, with at most two decimals. Invalid manual shares retain their text and cannot save. Shared fixtures cover limits and precision. Cross-currency conversions and all downstream numeric constraints were not exhaustively audited. |

## Impossible-date follow-up — W1/W2

Implemented on 28 October 2026 in local web source:

- Shared Gregorian calendar validation rejects impossible days, year zero and incomplete values, including non-leap 29 February and 31 April.
- Date fields retain invalid/incomplete text, show localized inline errors and participate in native form validation. Clearing an optional date propagates an empty value.
- Ordinary and recurring expense save paths validate dates independently of the field. Timestamp conversion rejects rollover and preserves the original instant when a valid date is unchanged.
- Event start/end conversion validates dates and times before entering Saving and rejects nonexistent local times during the spring DST transition. End-after-start validation remains in place.
- Expense/event save handlers release Saving in finally, including thrown failures.
- Existing settlement validation reuses the shared calendar validator.

Local browser checks covered impossible event start/end dates, retaining incomplete input after blur, accepting a leap-day correction, preserving end-after-start rejection, blocking an impossible expense date, and rejecting a non-leap 29 February as a recurring start date. No valid record was submitted and no test records were created. No production deployment or fresh physical-device session was performed. Save-handler exception recovery was inspected in source, not fault-injected in the browser.

`npm --prefix webapp test`: **18 passed**, including leap-century rules, invalid/incomplete dates and times, three time zones, DST gaps and existing unchanged-timestamp checks. Production build passed. Lint passed with existing warnings. `git diff --check` passed.

## Findings that still need work

- **W10/F01, Equal reopening as Manual — implemented:** the user confirmed SQL/web operation. Android 1.1.0 (10) was installed and verified on the connected phone for Equal/Manual and paused recurring editing. [Device verification](ANDROID_SPLIT_MODE_REVIEW.md) records the evidence, retained test data and limits. No alternating-client or concurrent-edit test was performed.
- **W3, calendar timestamps — deployed and verified:** UTC normalization is deployed in Supabase. Seven regression tests pass, and the live QA feed imported successfully with three events and nine correctly formatted timestamps. [Verification](CALENDAR_FEED_REVIEW.md) records the registry workaround and limits; external calendar polling was not tested.
- **F02, live attendance:** remains open.
- **F03, unsaved edits — local web expense editors implemented:** ordinary/recurring editors now guard navigation and browser unload. [Browser verification](UNSAVED_WEB_REVIEW.md) covers pristine/reverted values, Back/Forward, failed/successful/pending saves and bilingual dialogs with mocked APIs. Android expense/recurring protection has since been implemented and verified on the phone; see [native verification](ANDROID_UNSAVED_REVIEW.md). Web production deployment and event/repayment forms remain pending.

Other findings retain their earlier evidence and status; they have not all been retested.

## Validation run today

- `npm --prefix webapp test`: all 15 tests passed in the initial status review; 18 passed after the date fix.
- `dotnet run --project tests/AxisApp.RegressionTests/AxisApp.RegressionTests.csproj -v:q`: passed the domain, settlement and 40 shared amount-input fixtures, including 30,030 split scenarios.

Passing these suites proves their covered rules. It does not establish deployment status, every UI interaction, or concurrent repayment safety.

[Browser evidence: impossible expense date blocked](date-validation-expense.png).
