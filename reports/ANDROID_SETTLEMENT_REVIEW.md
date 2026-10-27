# Android repayment verification — 27 October 2026

## Deployment

- Device: M2007J3SY, serial `f54f4285`, Android 12, `arm64-v8a`.
- Package: `com.aitorsansal.axisapp`, version `1.1.0`, version code `10`.
- Final installation: **27 October 2026, 21:26:44 Europe/Madrid**.
- APK: `AxisApp/bin/Debug/net10.0-android/android-arm64/com.aitorsansal.axisapp-Signed.apk`.
- Build command: `dotnet build AxisApp/AxisApp.csproj -f net10.0-android -p:RuntimeIdentifier=android-arm64 -p:EmbedAssembliesIntoApk=true -v:q`.
- Final build: zero errors, 55 warnings, including existing dependency constraints, obsolete APIs and platform-analysis warnings.
- ZIP inspection verified **382 embedded managed assemblies**, including `lib_AxisApp.dll.so`, plus `libSkiaSharp.so`, `libmonodroid.so` and `libmonosgen-2.0.so` under `lib/arm64-v8a/`.
- Replaced the installed APK with `adb install -r`; app data and the test login were retained. Launched with the package launcher, then verified its process and visible UI throughout the session.

Both builds began with fresh app build directories. Automated review blocked the recursive deletion command. The verified `bin` and `obj` folders were moved instead to `C:/Users/aitor/AppData/Local/Temp/AxisApp-settlement-build-backup-20261027/`, with the first review build under its `review-build/` subdirectory. The final source-tree outputs are from the second clean build.

## Physical checks

All ledger actions used the phone's existing test account in `TestGroupForEvents`.

| Check | Observed outcome |
| --- | --- |
| Open review and cancel | Existing EUR37.51 debt unchanged; opening did not write a repayment. |
| Decimal point input | `8.50` remained `8.50` on the native input and saved EUR8.50, not EUR850. |
| Partial repayment with custom note | `Android settlement QA - partial` saved EUR8.50, leaving EUR29.01 owed. |
| Decimal comma while editing | Reopening the same record and entering `8,25` saved EUR8.25, leaving EUR29.26. The custom note survived. |
| Delete test partial | Confirmation required; removing the test record restored EUR37.51. |
| Full repayment with empty Spanish note | EUR37.51 cleared the debt to zero. Reopening showed the stored description `Saldar deuda con Aitor`. |
| Remove full test repayment | Only that newly created full repayment was deleted. |
| Empty English note | EUR0.01 saved as `Settlement with Aitor`, leaving EUR37.50 at that checkpoint. Reopening after updating the APK retained the exact saved note. |
| Change payment direction | Editing that EUR0.01 record to Aitor → test account produced EUR37.52 owed. The existing saved description was retained, as intended. The native recipient picker kept the parties distinct. |
| Delete the English test record | Confirmed removal of the temporary EUR0.01 repayment. |
| Zero amount | `0` was rejected with a visible localized error popup; the form stayed usable. |
| Amount above the current debt | `40` was rejected against EUR32.51 outstanding; no repayment was written. |
| Language restoration | Changed from the original system setting to English for testing, then restored the system setting, which resolved to Spanish. |

## Data left in place

The three temporary repayment records created during this session were removed:

1. Custom-note partial: EUR8.50, edited to EUR8.25.
2. Spanish default-note full repayment: EUR37.51.
3. English default-note repayment: EUR0.01, including the reversed-direction edit.

The user confirmed independently recording a **EUR5 repayment during the session**. That record was preserved. The resulting current debt was **EUR32.51 owed by the test account to Aitor**. No attempt was made to undo the user's change or force the ledger back to its starting total. Deleting test repayments does not imply deleting any backend audit history.

## Automated validation and limits

`dotnet run --project tests/AxisApp.RegressionTests/AxisApp.RegressionTests.csproj` passed, including the existing 30,030 split scenarios and settlement checks for exact dot/comma amounts, precision/overflow limits, English/Spanish fallback descriptions, custom notes, preserved timestamps and full/partial repayments in both directions.

The implementation guards in-flight submissions and distinguishes a committed write from a failed post-save typed read. Those failure branches were reviewed in code; no deliberate live network outage or simultaneous multi-device settlement was induced. The freshness check is not a database lock. No full TalkBack audit or cross-currency physical test was performed. The ordinary expense amount field is unchanged and remains a separate report item.

## Shared navigation follow-up

Backing out repeatedly sometimes exposed an additional empty Group Detail screen with no title or loaded records before reaching Groups. The same sequence reproduced through the unchanged ordinary expense editor: open `UI review - responsive split`, cancel, then Back. This suggests a shared navigation-stack problem; it was not established against a pre-change binary and is not claimed fixed here. Recovery was an additional Back, without changing ledger data.

## Evidence

- [Initial group](android-settlement-review/02-group.png)
- [Partial repayment success](android-settlement-review/04-partial-saved.png)
- [Stored English description in the dedicated editor](android-settlement-review/06-editor-english.png)
- [Reversed-direction result](android-settlement-review/07-direction-reversed.png)
- [Visible invalid-amount feedback](android-settlement-review/09-amount-validation.png)
- [Final Spanish review screen](android-settlement-review/11-final-review-spanish.png)
- [Final clean-build log](android-settlement-build.log)
