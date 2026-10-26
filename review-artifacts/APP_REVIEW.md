# Axis app review — 26 October 2026

Follow-up: fixes and current validation status are recorded in [FIX_VALIDATION.md](FIX_VALIDATION.md). The observations below describe the original review state.

Reviewed the running web app at http://localhost:5173 and a fresh Android build on the connected Xiaomi M2007J3SY (Android 12, arm64), using test@gmail.com. Findings below distinguish live reproductions from source-confirmed extensions and feature gaps. No application source was changed.

## Highest-priority findings

### 1. P1 — Editing an Android expense on the web silently changes its date

**Live reproduction:** Created `QA_android_expense` on Android for 26 October. The web activity list correctly displayed 26 October, but opening its editor displayed **25 October**. Clicking Save without changing any fields moved the activity entry to 25 October.

**Cause:** Android stores local midnight converted to UTC; the web editor takes the first ten characters of the stored timestamp rather than its local calendar date. Saving then writes that date as UTC midnight. This can move expenses across reporting periods.

**Locations:** `webapp/src/pages/AddExpensePage.tsx:129` and `:350`; `AxisApp/ViewModels/AddExpenseViewModel.cs:734`.

**Fix direction:** Define a consistent date-only convention, use local date extraction where appropriate, and preserve the original timestamp when the date has not changed. Test both directions across positive/negative UTC offsets and DST.

**Evidence:** [Web editor showing the shifted date](web-android-date-shift.png). The retained QA expense now has the shifted date, as a reproduction artifact.

### 2. P1 — Cancelling receipt edits can permanently destroy a saved receipt

**Live reproduction on web:** Uploaded a generated receipt to `QA linked expense` and saved it. Reopened the expense, pressed Remove photo, then left through Back without saving. The activity still showed an attachment, but reopening the editor had no preview while still offering Change photo/Remove photo.

**Cause:** Remove deletes the storage object immediately, while the expense's `receipt_path` is updated only on Save. Replacing an image also deletes the previous object before saving the expense. Cancellation or a failed expense save leaves a dangling reference. Android has the same eager deletion pattern in source; its cancellation case was not separately reproduced.

**Locations:** `webapp/src/pages/AddExpensePage.tsx:256–289`; `AxisApp/ViewModels/AddExpenseViewModel.cs:839–872`; `AxisApp/Services/SupabaseReceiptsRepository.cs:20–32`.

**Fix direction:** Keep the old object until the expense save succeeds. Defer removal/replacement cleanup until commit, and clean up abandoned new uploads separately.

**Evidence:** [Reopened expense with missing receipt preview](web-lost-receipt.png). Only the generated QA receipt was deleted.

## Other reproducible errors

### 3. P2 — Events can end before they start

Saved an event on the web with start **26 October 23:04** and end **26 October 22:00**. Reopening preserved both values. Android loaded the same event and accepted Save with the invalid interval too.

Neither editor checks chronological ordering; the schema also lacks an end-after-start constraint. Enforce this in both clients and the database.

**Locations:** `webapp/src/pages/AddEventPage.tsx:77`; `AxisApp/ViewModels/AddEventViewModel.cs:166`; `supabase/schema.sql:2203`.

**Evidence:** [Persisted invalid interval](web-inverted-event.png).

### 4. P2 — Equal splitting can produce a split the app refuses to save

With four participants, entered **€0.06** using Equally. The app generated **€0.02, €0.02, €0.02, €0.00**, then rejected Save because every share must be positive. A valid split is **€0.02, €0.02, €0.01, €0.01**.

The algorithm rounds one share and puts the entire remainder on the last person. It can also generate negative final shares with larger participant counts, and can distribute the rounding difference unfairly. Android uses the same algorithm in source.

Use integer-cent quotient/remainder distribution, assigning at most one extra cent per person. Explicitly handle totals smaller than the number of participants.

**Locations:** `webapp/src/pages/AddExpensePage.tsx:34`; `AxisApp/ViewModels/AddExpenseViewModel.cs:634`.

**Evidence:** [Automatic split and validation error](web-equal-split-error.png).

### 5. P2 — Android displays the wrong first due date for recurring expenses

Created a new monthly template starting **26 October 2026**, then paused it. Android's recurring list displayed **25 November 2026** as its next date. The server's materializer explicitly makes the first occurrence the start date, irrespective of frequency.

`FormatNextDue` calculates `(StartDate - 1 day) + one period` when nothing has been processed. Weekly/monthly/yearly templates therefore skip the first occurrence in the displayed estimate. Paused templates also retain a due-looking date without explaining that they will not run.

**Locations:** `AxisApp/ViewModels/RecurringExpensesViewModel.cs:104`; `supabase/schema.sql:1687–1696`.

**Fix direction:** Return StartDate directly when LastProcessedDate is null; preferably derive next due using the server's scheduling logic.

**Evidence:** [Android recurring list](android-recurring-date.png). The web list does not expose a next-due date at all.

### 6. P2 — Android and web disagree about whether a started event is upcoming

After 23:04, the QA event appeared under **Past** on web but under **Upcoming / Próximos** on Android. Both device and host were on Europe/Madrid with matching clocks. Re-entering the Android event list retained the mismatch during the review.

Both implementations intend to compare the start instant with now. Inspect DateTime deserialization/Kind and normalize timestamps before comparison; the precise runtime cause was not instrumented. This is an observed classification error, not a claim that the intended filtering rules differ.

**Locations:** `AxisApp/ViewModels/GroupEventsViewModel.cs:176–181`; `webapp/src/components/GroupEventsTab.tsx:155–160`.

**Evidence:** [Android showing the started event as upcoming](android-event-upcoming.png).

### 7. P2 — Rapid car-seat changes lose updates on web

Starting at zero, clicked More available seats three times in quick succession. After requests completed, the value was **1**, not 3. Each click writes an absolute value based on the same stale attendee state, with no optimistic increment, serialization or pending-state lock.

**Locations:** `webapp/src/pages/EventDetailPage.tsx:88–135`; the event-list controls use a similar pattern in `GroupEventsTab.tsx`.

**Fix direction:** Serialize updates or maintain optimistic local state; consider an atomic server increment. Test rapid RSVP changes for the same stale-response problem.

### 8. P2 — Web notifications fail silently in the current localhost configuration

Opening Profile logged `FirebaseError: Installations: Missing App configuration value: "projectId"`. The notification section remained absent. The local environment file has no VITE_FIREBASE configuration keys.

This is confirmed for **the reviewed localhost setup**, not for production. The missing configuration is also a robustness issue: `getPushState(...).then(setPushState)` has no rejection handler, leaving the initial `unsupported` state and hiding the feature.

**Locations:** `webapp/src/lib/firebase.ts:8–28`; `webapp/src/pages/ProfilePage.tsx:48`; `webapp/src/lib/pushNotifications.ts:12`.

**Fix direction:** Configure the local Firebase web app, validate configuration, catch initialization errors, and distinguish configuration failure from an unsupported browser.

### 9. P3 — Event end times are invisible outside the editor

Both clients save an optional end time but their normal event detail captions show only start time and location. An attendee cannot see when an event finishes without opening Edit, which may be unavailable to them.

**Locations:** `webapp/src/pages/EventDetailPage.tsx:152–161`; `AxisApp/ViewModels/EventDetailViewModel.cs:205–207`.

Show a start/end range, including both dates for multi-day events.

### 10. P3 — Invalid web URLs have poor recovery

`/qa-unknown-page` renders a blank page with no recovery link. A syntactically valid but nonexistent group ID displays `Cannot coerce the result to a single JSON object` rather than a useful missing/inaccessible-group message.

**Locations:** `webapp/src/App.tsx:27` (no catch-all route); group detail query/error handling.

Add a usable not-found page and map missing/inaccessible data to a localized message. Invalid invitation codes, in contrast, correctly displayed `Invalid invite code`.

**Evidence:** [Unknown route](web-unknown-route.png).

### 11. P3 — Web car offering ignores saved seat capacity

The account profile has **4** available car seats, visible on both platforms. Selecting I have a car on web initialized the offering to **0** seats. The web handler explicitly supplies zero rather than reading the saved default.

**Location:** `webapp/src/pages/EventDetailPage.tsx:119–123`; corresponding list handler in `GroupEventsTab.tsx`.

Use the profile default for the initial offering, while preserving explicit per-event overrides.

## Missing features / parity gaps

These are observed absences, not proof that every item belongs in the intended web scope.

| Feature | Android | Web | Assessment |
| --- | --- | --- | --- |
| Forgot-password entry from login | Present | Missing | Highest-priority functional gap: a signed-out web-only user cannot initiate recovery in the app. |
| Statistics and export | Present; charts rendered with QA data | No statistics tab/export UI | Useful parity gap, particularly for reviewing or moving expense history. |
| Detailed/net balance mode | Present | No corresponding control | Clarify whether reduced web scope is intentional. |
| Accent color and converted-amount display preference | Present in Profile | Missing | Lower-priority parity gap. |
| Event share-link action | Present in Android detail | No visible action in web detail | Browser address can be copied, but there is no equivalent discoverable control. |
| Recurring next-due information | Present but incorrect in case above | Missing | Expose authoritative schedule status and next run. |
| Password visibility toggle | Present | Missing | Small login usability improvement. |

Receipts, recurring expenses, roster/add-by-name, calendar feeds, and group appearance **are implemented on web**. Existing documentation describing several of these as missing is stale. Google Calendar two-way sync, iOS, recurring events and offline editing were not treated as bugs merely because they are absent.

## Additional source-confirmed concern

The web expense and recurring start-date defaults use `new Date().toISOString().slice(0,10)`, which takes the UTC date. Around local midnight this can default to yesterday/tomorrow. This boundary was not reproduced by changing the device clock; it follows directly from the implementation at `AddExpensePage.tsx:64–65` and belongs in the same date-handling fix as finding 1.

## Coverage and validation

| Area | What was exercised |
| --- | --- |
| Authentication | Successful email/password login on both; Android session survived force-stop/relaunch. Login/profile recovery UI inspected. |
| Groups | Existing group inspection; new group creation with purple/gamepad appearance; owner/non-owner menus inspected. |
| Members | Created QA Alice, QA Bob and QA Charlie as phantom members; pending-invite links generated. |
| Expenses | Equal thirds, manual 2/3/7 split, editing, invalid tiny split, Android creation with another payer, USD save in an EUR group. |
| Balances | Cross-platform balances agreed; settlement removed the corresponding debt. Final QA net balance matched on both clients (€18.38 owed to the test account). |
| Receipts | Generated image upload, saved attachment, removal without saving, reopening broken attachment. |
| Events | Create, invalid interval, reopen/edit/save on Android, past/upcoming lists, car offering/seats, linked expense. |
| Recurrence | Create monthly template, pause, edit while paused; Android showed the edited template and paused state. |
| Profile | Name/birthday/car-seat fields, language switch to Spanish and back to original System setting, calendar-feed/account controls inspected. |
| Presentation/errors | Android statistics charts visually inspected; desktop and initially narrower web layout inspected; missing-route/group and invalid-invite behavior tested; browser errors inspected. |

**Build checks:** Clean Android build succeeded, with embedded managed assemblies verified inside the APK before installation. Installed `AxisApp/bin/Debug/net10.0-android/android-arm64/com.aitorsansal.axisapp-Signed.apk`, package `com.aitorsansal.axisapp`, version 1.0.2 (9), update time 26 October 2026 23:02:56 device time. Process remained alive; no Axis crash was found in the inspected crash buffer. The dependency graph emits NU1608 Lifecycle version-range warnings, alongside compiler/XAML warnings. These are maintenance risks, not a reproduced crash.

`npm --prefix webapp run build` and `npm --prefix webapp run lint` succeeded. Build warned about the large main bundle; lint reported warnings. Build outputs are not evidence that behavioral findings above are fixed.

**Testing limits:** This was a functional/product review, not a penetration test or exhaustive device matrix. Did not execute account deletion, change credentials, transfer ownership, dissolve existing groups, Google OAuth with a separate Google identity, a second-account invite claim, scheduled cron/reminder delivery, actual push delivery, camera capture, widget interactions, CSV export completion, calendar subscription in an external app, offline failure recovery, or accessibility with a screen reader. The requested narrow viewport override did not take effect in this browser (measured width remained 1110), so no 360px responsive pass is claimed. Group rename raised `prompt() is not supported` in the in-app browser; this is a browser limitation and was not classified as a general app defect.

## Retained test state

The group **QA Review 2026-10-26** (`7033f87f-e645-48d6-8c66-f6f92f1678e9`) remains for reproduction. It contains three phantom members, four ordinary QA expenses, one settlement, one invalid-time QA event, and one **paused** recurring template. The generated receipt is deliberately left as the broken-reference reproduction. The Android-created expense has the shifted date from finding 1. Existing group data was inspected but not edited. No password or account email was changed. No code fixes were applied.

Build-folder removal was blocked by the command policy, so the compile skill's fallback moved the old bin/obj folders to `C:/Users/aitor/AppData/Local/Temp/AxisReviewBuild-cc7ac633b2b14af98ff84215b93e24e2` before building. Review artifacts and the report are the only new untracked workspace files.

Recommended order: fix date preservation and receipt commit behavior first; then split allocation, event validation and recurring due dates; then update races, notification initialization and missing recovery UI.
