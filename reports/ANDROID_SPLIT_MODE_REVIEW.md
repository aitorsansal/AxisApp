# Android split-mode verification — 28 October 2026

## Implementation

Expenses and recurring templates deserialize nullable `split_mode` and send it through their atomic save RPCs. AddExpenseViewModel loads saved shares while redistribution is disabled, then restores explicit Equal/Manual intent. NULL or unrecognized intent opens as Manual, preserving legacy amounts. Repayments retain NULL mode and their separate editor.

ExpenseSplitMode preserves saved Equal shares by member ID when the total and participants are unchanged, including who received the remainder cent. Total/participant changes recalculate using integer cents; selecting Equal explicitly starts a fresh allocation. Manual share inputs remain unchanged when the total changes.

## Installed build

- Device: 2412DPC0AG / rodin_eea, Android 16, ARM64, connected over wireless ADB.
- Package: `com.aitorsansal.axisapp`, version **1.1.0 (10)**.
- Installed at **28 October 2026, 12:31:54 Europe/Madrid** using `adb install -r`; existing login/data retained.
- Build: `dotnet build AxisApp/AxisApp.csproj -f net10.0-android -p:RuntimeIdentifier=android-arm64 -p:EmbedAssembliesIntoApk=true -v:q`.
- Result: **0 errors, 55 warnings**. Build log: `android-split-mode-build.log`.
- Signed APK: `AxisApp/bin/Debug/net10.0-android/android-arm64/com.aitorsansal.axisapp-Signed.apk`, 63,813,287 bytes.
- APK inspection: **382 embedded managed assemblies**, including AxisApp, with the app assembly timestamp from this build.
- The app process remained alive through verification. The inspected crash buffer contained no matching Axis/FATAL entries.

## Physical-device checks

A new isolated group, **Split mode QA 20261028**, was created using the existing test account (Claude QA Bot) and one new phantom, **SplitQA-Alex**. No messages or invitations were sent to another person. The add-member flow generated its usual invitation artifacts; no link was shared. Existing groups and their ledger records were not edited.

| Check | Observed result |
| --- | --- |
| Create Equal expense | `Android split mode QA` saved as EUR10.01 with shares EUR5.01 / EUR5.00. |
| Reopen Equal | Equal remained selected; the saved per-member cents were retained. |
| Change total | EUR12.01 recalculated automatically to EUR6.01 / EUR6.00, with Save enabled. Saved and reopened at EUR12.01. |
| Change participants | Temporarily excluding Alex assigned EUR12.01 to the only included member; re-including restored the two-person split. |
| Explicit Manual with equal amounts | Changed total to EUR12.00 while Equal, then chose Manual and saved EUR6.00 / EUR6.00. Reopened as Manual despite equal amounts. |
| Change total in Manual | Entering EUR14.00 retained EUR6.00 / EUR6.00; Remaining showed EUR2.00 and both Save actions were disabled. Cancelled this unsaved change. |
| Recurring Equal | Created `Android recurring split QA`, EUR6.01 monthly; paused immediately. Reopened with Equal and EUR3.01 / EUR3.00. |
| Edit paused recurring template | Changed to EUR8.01; shares recalculated to EUR4.01 / EUR4.00. Saved, remained paused, and reopened with the same total, shares and Equal mode. |

## Data retained

The isolated QA group remains with two members, one **EUR12.00 Manual expense** (EUR6.00 each), and one **paused EUR8.01 monthly template**. The unsaved EUR14.00 edit was cancelled. No test repayment was recorded. The template remains paused, so it will not generate future expenses. Test records and invitation artifacts were not deleted.

## Automated validation and limits

The C# domain suite passes existing 30,030 split scenarios, amount/date/settlement regressions and new split-mode checks for explicit intent, legacy fallback, saved remainder cents, reordered/replaced participants and total changes. `git diff --check` passes. Web regression/build results from the preceding stage remain applicable; no web source was changed for this Android implementation.

The user confirmed the SQL migration and web save/reopen worked before Android deployment. This session verified actual Android saves against that backend. It did not exercise a single expense alternating between both clients, a live cron run, offline conflicts, concurrent edits or a full accessibility audit. Legacy fallback and reordered-member allocation are covered by automated tests rather than a separate phone fixture.

## Evidence

- [Equal reopened](android-split-mode-review/01-equal-reopened.png)
- [Manual retained shares and blocked invalid total](android-split-mode-review/02-manual-preserved.png)
- [Recurring Equal recalculation](android-split-mode-review/03-recurring-equal.png)
- [Paused template retained after save](android-split-mode-review/04-paused-template.png)
