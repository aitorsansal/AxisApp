# Web unsaved-edit protection — 28 October 2026

## Scope and behaviour

The local web expense editor now protects new and existing ordinary expenses and recurring templates. Event and repayment forms, Android protection, draft recovery and production deployment are outside this stage.

Changes are compared with the fully loaded form baseline. The snapshot covers description, normalized amount, currency, category, payer, participant membership and shares, Equal/Manual intent, active date/schedule fields, linked event and receipt path. Equivalent dot/comma amounts and participant ordering do not trigger a false warning. Restoring the original values makes the form clean again.

Internal navigation opens Axis's existing styled Modal with Keep editing and Discard changes. Keep editing is focused by default; Escape keeps the form. Backdrop clicks do not discard. Browser Back/Forward use the same route blocker. Reload/tab-close protection uses the browser beforeunload mechanism and its standard message.

Saving disables the editor fields. If navigation is attempted while saving/uploading, Discard is disabled until the operation finishes. Successful saves clear the guard before returning to the expected parent; failed saves preserve fields and protection. Accepted successful deletion also bypasses a second discard prompt.

## Routing and loading

The route tree now uses createBrowserRouter/RouterProvider so React Router's supported [useBlocker](https://reactrouter.com/api/hooks/useBlocker) can handle internal and POP navigation. Routes, auth/layout providers and existing parent-navigation behaviour are retained. After building ancestor history entries for a cold deep link, NavigationTracker's companion helper synchronizes the router's history index with a popstate notification. This was tested through clean/dirty Back, Forward, cancellation and discard.

The baseline is captured only after data for the current account/route has loaded. Old asynchronous loads are ignored after navigation. Auth-token refreshes with the same account no longer reload the expense form and overwrite edits.

## Automated checks

- `npm --prefix webapp test`: **20 tests passed**, including snapshot comparisons for meaningful fields, normalized amounts, invalid input, participant changes, Manual shares and recurring schedules.
- `npm --prefix webapp run build`: passed. The bundle-size advisory remains; using the data-router API increases the included routing code.
- `npm --prefix webapp run lint`: passed with existing warnings; no new warnings in the changed code.
- `git diff --check`: passed.

## Browser checks

The actual app, components, routing and editors were exercised on a separate local origin at `http://127.0.0.1:5174` using `tests/unsaved.vite.config.ts`. All Supabase HTTP responses and writes were mocked; realtime connections were disabled. Simulated save success/failure and a five-second delayed save were controlled through visible test-only buttons. The fixture showed zero external requests and three simulated successful writes. No production database or storage writes were made.

| Scenario | Observed result |
| --- | --- |
| Clean existing expense → Profile | Navigated directly, no dialog; no reload warning registered. |
| Change description → Profile | Navigation blocked, custom dialog shown, Keep editing focused. |
| Keep editing / Escape | Stayed in the editor with entered description intact. |
| Dirty browser Back from a deep link | Current editor restored while the dialog appeared. Discard continued to the group. |
| Back/Forward after discard | History remained usable; re-entered editor loaded saved values. |
| Change and restore description | No warning; header Back returned to parent. |
| Failed expense save | Simulated API error displayed; data retained; Back still asked for confirmation. |
| Successful expense save | Returned to group without a discard warning and reflected the mocked saved description. |
| Navigate during delayed save | Wait message appeared; Discard disabled; successful completion returned to group without another prompt. |
| Recurring frequency change | Protected by the dialog; restoring frequency removed the warning. |
| Failed/successful recurring save | Failure retained protection; success returned to recurring list without warning, preserving paused state. |
| New expense with only a description | Protected even though amount was incomplete and Save disabled. |
| English/Spanish | Correct title, explanation and action labels; dialogue fit the default test viewport. |
| Reload warning | The cancelable beforeunload check was prevented only while dirty. An actual reload attempt retained the typed draft; the automation did not expose a native-dialog object, so native browser wording was not independently inspected. |
| Final loading-baseline change | Rechecked clean parent navigation, Forward into the editor, dirty Back and Discard. |

Unit tests additionally cover other editable fields. Receipt upload interruption, accepted deletion, a full screen-reader audit and every browser/OS combination were not separately exercised. This is a local implementation and browser verification, not a production deployment.

## Reproduce the isolated checks

From the repository root run:

```powershell
npm --prefix webapp run dev -- --config tests/unsaved.vite.config.ts
```

Open the synthetic expense at `/groups/00000000-0000-0000-0000-000000000100/expenses/00000000-0000-0000-0000-000000000200` or the recurring template at `/groups/00000000-0000-0000-0000-000000000100/recurring/00000000-0000-0000-0000-000000000300` on port 5174. Add `?qaLanguage=es` for Spanish. Fixture state persists only in that local origin's localStorage; all credentials are synthetic. The fixture is injected only by the explicit test Vite config and is absent from the normal production bundle.

The temporary QA server was stopped after verification. The normal development server on port 5173 was not stopped.

## Evidence

- [English discard dialog](unsaved-web-review/discard-dialog.png)
- [Spanish discard dialog](unsaved-web-review/spanish-discard-dialog.png)

## Android follow-up

Android expense/recurring protection was subsequently implemented and installed on the connected phone on 28 October. [Device verification](ANDROID_UNSAVED_REVIEW.md) records native Back/Cancel/Discard, reverted changes and successful saves. This does not deploy the web stage or extend either client to event/repayment forms.
