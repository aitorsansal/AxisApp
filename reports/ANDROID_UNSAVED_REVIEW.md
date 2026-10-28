# Android unsaved-edit protection — 28 October 2026

## Scope

Native discard confirmation is implemented for the Android ordinary and recurring expense editors, including new unfinished entries. Event and repayment editors, draft recovery after process termination and other forms remain outside this stage. The web implementation remains local and has not been deployed.

## Implementation

AddExpensePage subscribes to Shell.Navigating while visible and defers cancellable navigation while asking with DisplayAlertAsync. Keep editing, dismissing the native dialog with Back, or a dialog error cancels the original navigation; Discard allows its original target to proceed. System Back and Cancel both pass through this event. A confirmation-in-progress guard avoids duplicate prompts. Event subscriptions are removed when the page disappears.

ExpenseEditorSnapshot compares actual editable values with the loaded baseline, including normalized total/share amounts, description, currency, category, payer, included members, Equal/Manual intent, active date/schedule fields, receipt path and event link. Returning values to the baseline removes the warning. The baseline is not captured during initial loading. Confirmed successful saves/deletes mark navigation as committed immediately before leaving. Exceptions before that point preserve the dirty state.

Pending save/upload navigation is cancelled with a localized wait message. This path does not hold a navigation deferral while the write is running, so a successful write can still navigate afterward. These pending/failure branches were covered by policy tests and source inspection, not live network fault injection.

The implementation follows MAUI's [Shell navigation deferral API](https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/shell/navigation?view=net-maui-10.0#navigation-deferral). No XAML restyling, database migration or backend deployment was required.

## Build and installation

- Device: **2412DPC0AG / rodin_eea**, Android **16**, ARM64; wireless ADB. An emulator was also connected but was not used for installation/testing.
- Package/version: `com.aitorsansal.axisapp`, **1.1.0 (10)**.
- Installed with `adb install -r` at **28 October 2026, 15:05:28 Europe/Madrid**, retaining login and data.
- Build command: `dotnet build AxisApp/AxisApp.csproj -f net10.0-android -p:RuntimeIdentifier=android-arm64 -p:EmbedAssembliesIntoApk=true -v:q`.
- Build result: **0 errors, 55 warnings**, elapsed 3:11. Log: `android-unsaved-edit-build.log`.
- Signed APK: `AxisApp/bin/Debug/net10.0-android/android-arm64/com.aitorsansal.axisapp-Signed.apk`, **63,208,955 bytes**.
- APK inspection verified **382 embedded managed assemblies**, including the app assembly from this build.
- The process remained alive through all checks. The inspected crash buffer contained no matching Axis/FATAL entries.

## Physical-device checks

All ledger interactions used the already-existing isolated **Split mode QA 20261028** group, its EUR12.00 Manual expense and paused EUR8.01 monthly template.

| Scenario | Observed result |
| --- | --- |
| Open existing expense, Cancel unchanged | Returned directly to the group without a warning. |
| Edit description, Cancel | Native Spanish discard dialog appeared. |
| Keep editing | Stayed in the editor with `Android unsaved QA draft` intact. |
| System Back after editing | The same native dialog appeared. |
| Back while dialog open | Dismissed the dialog, cancelled navigation and retained the draft. |
| Restore original description, Cancel | Returned directly without a stale warning. |
| New entry with description but no amount | Cancel prompted even though Save was disabled. Discard returned to the group; no expense was saved. |
| Save a changed existing expense | Saved `Android saved guard QA` and returned normally without a discard dialog. |
| Restore that saved description | Saved the original `Android split mode QA` description and returned normally; EUR12.00 total and EUR6.00 shares remained unchanged. |
| Edit recurring frequency | Changing Monthly to Weekly prompted on Cancel. Keep editing retained the editor. |
| Revert recurring frequency | Restoring Monthly allowed Cancel without a warning. |
| Save recurring template | An unchanged save returned directly to the recurring list; template remained EUR8.01, Monthly and Paused. |

The keyboard was dismissed before system-Back navigation checks; Back closing the keyboard alone is not treated as leaving the form. Physical checks used Spanish; English strings are included but no phone-language setting was changed.

## Data and limits

No new saved expenses, templates, members or invitations were created in this stage, and no records were deleted. Two successful QA expense description writes changed then restored its text; the existing template received an unchanged save. Backend audit history may record those actions. No existing non-QA groups or ledger records were edited.

The original QA group remains with its EUR12.00 Manual expense and paused EUR8.01 monthly template, as before this stage. A deliberately unfinished new expense was discarded without saving.

The C# regression executable passes the existing 30,030 split scenarios and new snapshot/guard assertions for meaningful fields, invalid/normalized money, order-independent members, recurring date/frequency, reverted edits, pending writes, failed-save retention and successful committed navigation. `git diff --check` passes. No live failing request, upload interruption, concurrent edit, OS process termination, full TalkBack audit or emulator run was performed. These results do not imply coverage of event or repayment forms.

## Evidence

- [Native expense confirmation](android-unsaved-review/01-discard-dialog.png)
- [Native recurring confirmation](android-unsaved-review/02-recurring-dialog.png)
- [Template unchanged and paused](android-unsaved-review/03-template-unchanged.png)
- [Original expense restored](android-unsaved-review/04-restored-expense.png)
