# Amount input verification — 27 October 2026

## Implemented contract

Totals, manual shares, recurring forms and settlements use one parser per client: `AxisApp/Models/AmountInput.cs` and `webapp/src/lib/amountInput.ts`.

- Accept a positive plain amount with one optional dot or comma decimal separator and up to two decimal places.
- Preserve existing shorthand such as `.50` and `,50`.
- Reject grouping, mixed/repeated punctuation, internal spaces, excess precision, signs and exponent notation.
- Trim outer whitespace for parsing while retaining the user's original field text.
- Reject values above `9999999999.99`, matching the original-amount column's numeric precision.
- Never remove separators or round invalid input into a different amount.

Examples: `1234.30` and `1234,30` are accepted; `1.234,30`, `1,234.30`, `1,234`, `12..30` and `12,` cannot be saved. An incomplete edit may remain in the field. Normal displayed amounts may still use locale formatting; generated editable values omit grouping.

## Automated and browser validation

- `npm test`: **15 tests passed**.
- `dotnet run --project tests/AxisApp.RegressionTests/AxisApp.RegressionTests.csproj`: domain suite passed, including existing 30,030 split scenarios and the amount-input contract.
- Both suites consume the same **40 cases** in `tests/amount-input-cases.json`, covering valid dot/comma amounts, exact cents, limits, whitespace, repeated/mixed separators, grouping, negatives, zero, incomplete input and excessive precision.
- Web production build passed. Changed web files passed lint; existing full-source warnings and the bundle-size advisory remain.

Isolated Brave tests used mocked API responses and wrote no real web records. They verified:

1. `8.50` produces equal shares of `4.25` / `4.25`.
2. Scrolling over the total cannot change its value.
3. Invalid totals retain their exact text, hide derived shares and block both the button and a programmatic form submission.
4. An actual clipboard paste of `1.234,56` retains that text and shows the format error.
5. A manual share of `4,251` is rejected rather than rounded; correcting to `4,25` and combining with `4.25` saves an EUR8.50 payload with EUR4.25 shares.
6. Recurring creation sends `12,30` as numerical `12.3`, with `6.15` / `6.15` shares through the recurring RPC.
7. Spanish amounts/errors fit 390px and 320px viewports without horizontal overflow.

## Android deployment

- Device: **M2007J3SY**, serial `f54f4285`, Android 12, `arm64-v8a`.
- Package: `com.aitorsansal.axisapp`, version `1.1.0`, code `10`.
- Installed: **27 October 2026, 22:06:52 Europe/Madrid**.
- APK: `AxisApp/bin/Debug/net10.0-android/android-arm64/com.aitorsansal.axisapp-Signed.apk`.
- Build: `dotnet build AxisApp/AxisApp.csproj -f net10.0-android -p:RuntimeIdentifier=android-arm64 -p:EmbedAssembliesIntoApk=true -v:q`.
- Result: zero errors, 55 warnings.
- ZIP verification: **382 embedded managed assemblies**, including AxisApp, plus ARM64 `libSkiaSharp.so`, `libmonodroid.so` and `libmonosgen-2.0.so`.
- Installed with `adb install -r`; account and app data retained. Launcher, visible UI and sustained process operation were verified.

Before building, the verified app `bin` and `obj` folders were moved to `C:/Users/aitor/AppData/Local/Temp/AxisApp-settlement-build-backup-20261027/amount-input-build/`, using the previously established cleanup fallback. The source-tree outputs therefore came from a fresh build.

## Physical-device checks

The existing test account and `TestGroupForEvents` were used.

| Check | Observed result |
| --- | --- |
| Fresh expense | Empty amount rather than a prefilled editable zero; Save disabled. |
| Typed dot decimal | `8.50` remained `8.50`; shares were EUR4.25 each. |
| Typed grouped text | `1.234,56` remained intact; Save disabled and localized error shown on blur. |
| Native clipboard paste | Copied the selected grouped value, cleared the field, then pasted it using the native paste key. `1.234,56` was retained and rejected. |
| Typed comma decimal | `8,50` remained intact and produced the same EUR8.50 total. |
| Invalid manual share | `4,251` remained visible, disabled Save and showed a share error. The remainder changed to an instruction to complete valid amounts. |
| Corrected manual share | `4,25` restored a zero remainder and enabled Save. |
| Actual save | `Amount input QA - 20261027` saved as EUR8.50, paid by the test account, split EUR4.25 / EUR4.25. Debt changed from EUR32.51 to EUR28.26. |
| Reopen | The saved editor showed `8,50` and two `4,25` shares. |
| Cleanup | Deleted only that temporary test expense; debt returned to EUR32.51. The user's EUR5 repayment remained. |
| Settlement regression check | Grouped `1,234.56` was retained and rejected by the settlement save guard with the new localized message. No repayment was written. |

The phone's system-language setting was retained. No live recurring template was created. Deleted test records may remain in backend audit history.

## Coverage limits

Browser writes were mocked; the real save/reopen/delete check was performed on Android. Recurring input is covered by shared parsing and the mocked web recurring payload, rather than a live schedule. No new cross-currency, offline-conflict or assistive-technology audit was performed. Date validation and split-mode persistence are separate report items and were not changed here.

## Evidence

- [Web grouped clipboard paste](amount-input-review/web-invalid-paste.png)
- [Web Spanish manual-share validation](amount-input-review/web-mobile-validation.png)
- [Android dot amount](amount-input-review/android-dot-amount.png)
- [Android grouped input error](amount-input-review/android-invalid-grouping.png)
- [Android paste error](amount-input-review/android-invalid-paste.png)
- [Android manual-share error](amount-input-review/android-invalid-share.png)
- [Saved native expense](amount-input-review/android-saved-expense.png)
- [Restored ledger](amount-input-review/android-cleanup.png)
- [Android build log](android-amount-input-build.log)
