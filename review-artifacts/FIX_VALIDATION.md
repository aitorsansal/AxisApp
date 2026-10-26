# Review fixes and validation

Scope: the errors from APP_REVIEW.md, excluding Firebase as requested. Missing features were not implemented. Changes are local source changes; the web production site has not been deployed.

## Implemented

| Finding | Change | Validation |
| --- | --- | --- |
| Expense date shifts | Both editors show the local calendar date and preserve the original timestamp when its date is unchanged. New web dates use local midnight; today's defaults use the local date. | Created `QA_Android_fixed_date` for 26 October on the phone. Web opened it as 26 October, saved it untouched, and reopened it as 26 October. Automated checks also cover Madrid, Los Angeles, Tokyo and DST dates. |
| Receipt loss on cancelled edits | Both clients stop deleting files from the expense editor. Existing scheduled orphan cleanup owns deletion. Saving is guarded during uploads. | Web removal + Back retained the original 500px test image. Replacing it with a different-size image + Back also retained the original. Removing it in the final Android build and cancelling also preserved the original image, verified by reopening it on web. |
| Invalid event interval | Both clients reject an end at or before the start with a localized error. | Reproduced rejection on web and phone. Then corrected the QA event to 23:04–23:34 and saved it successfully on web. |
| Equal-split allocation | Allocate integer cents using quotient/remainder, differing by at most one cent. | Both clients saved €0.06 across four participants as 2¢/2¢/1¢/1¢. Each language's regression suite exercises 30,030 total/count combinations. |
| Wrong first recurrence | Unprocessed schedules return StartDate directly. Later dates follow the server's period calculation. | Phone now shows 26 October instead of 25 November for the retained paused template. Month-end and leap-year cases pass. |
| Android event bucket | Normalize DateTime instances to UTC before comparing. | Phone Upcoming became empty and the started event appeared under Past, agreeing with web. Tests cover equivalent local and UTC instants. |
| Lost car-seat updates | Serialize writes per event and derive each action from the preceding successful result. Aborted dependent writes do not run after failure. | Three quick web clicks changed 1 to 4. Automated queue, failure and retry checks pass. Both list and detail controls use the queue. |
| End time hidden | Both event lists and details display start/end ranges, including dates for multi-day events. | Visible on phone and web; multi-day formatting test passes. |
| Invalid web routes/groups | Add localized not-found page and map missing/invalid group IDs to a useful unavailable-group message. | Unknown route offers a home link; zero UUID group shows the expected localized message. |
| Saved car capacity ignored | Web detail initializes a new offering from the saved profile capacity, matching the existing list behavior. | Set an event to 5 seats, switched offering off/on, and verified it returned to the profile's 4 seats. |

## Database enforcement

`supabase/event_time_validation.sql` is an idempotent migration adding an end-after-start CHECK constraint as NOT VALID. It enforces new writes without silently changing historical invalid events and lists legacy rows for review. The fresh schema includes the same constraint.

**Applied to the live AxisApp production database on 26 October 2026 through the user's authenticated Brave/Supabase SQL Editor session.** The constraint `events_end_after_start` was created, the historical-invalid-event count was 0, and the constraint was then fully validated (`convalidated = true`). A test update setting the QA event's end equal to its start was rejected specifically by this constraint; the failed update was rolled back within a PL/pgSQL exception block, leaving event data unchanged. The database now protects writes from older clients too. Do not rerun the entire schema against the existing project.

[Production verification screenshot](supabase-event-constraint-applied.png). [Supabase SQL result](https://supabase.com/dashboard/project/foepkovwmwyygulbdahv/sql/5a06c42f-2ad4-4172-a1e9-b1b41da853a6).

## Additional Android crash found during verification

Repeated UI automation/navigation exposed a fatal `TouchBehavior.AccessibilityListener` activation failure in the existing CommunityToolkit animation path, matching [upstream issue 3317](https://github.com/CommunityToolkit/Maui/issues/3317). Android press feedback now uses a native StateListAnimator, avoiding that listener. Other platforms retain their original animation implementation. A touch-handler prototype interfered with taps during verification and was replaced before final delivery.

## Automated checks

- `npm --prefix webapp test`: 5 passing regression tests, including 30,030 split scenarios and time-zone subprocess checks.
- `dotnet run --project tests/AxisApp.RegressionTests -v:q`: passes 30,030 split scenarios plus schedule, instant-comparison and range checks.
- `npm --prefix webapp run build`: passes.
- `npm --prefix webapp run lint`: passes with existing warnings.
- `git diff --check`: passes; Git reports existing line-ending conversion notices.
- Final Android clean build succeeded with 0 errors and 56 warnings (dependency/XAML/compiler warnings, including nullable Android animator factory annotations). Firebase was not changed.

Final signed APK: `AxisApp/bin/Debug/net10.0-android/android-arm64/com.aitorsansal.axisapp-Signed.apk`. Verified 382 embedded managed assembly entries before installing with `adb install -r` on device `f54f4285`. Package `com.aitorsansal.axisapp`, version 1.0.2 (9), lastUpdateTime **26 October 2026 23:40:04 Europe/Madrid**. The final process (PID 1850) remained alive through group navigation, expense editing, opening/removing a receipt, cancelling and repeated accessibility dumps. No new Axis crash appeared in the inspected crash buffer; the earlier 23:33 crash belongs to the superseded build. This is a smoke test, not a guarantee against every possible crash.

## Evidence

- [Receipt preserved](fixed-receipt-preserved.png)
- [Invalid event rejected](fixed-event-validation.png)
- [Correct date after round-trip](fixed-date-roundtrip.png)
- [Valid start/end range](fixed-event-range.png)
- [Android Past classification](fixed-android-past-event.png)
- [Correct Android recurrence date](fixed-android-recurring-date.png)

The QA group and its paused recurring template remain. Two additional €0.06 QA expenses were created for regression checks. The original review's damaged test expense date and broken test receipt are historical test artifacts, not repaired production data. No existing non-QA group data was edited, no credentials changed, and no notifications configuration changed.
