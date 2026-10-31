# Expense and repayment Together forms

Date: 2026-10-31. Deployed from source commit b2367da. See [deployment receipt](./WEB_DEPLOYMENT_2026-10-31.md).

## Changes

Expense creation/editing, recurring expense creation/editing and event-linked expense creation now share a Together layout: a lighter title with group context, neutral filled sections for expense details, people/split and date/extras, and matching rounded actions. Wide desktop layouts put the people/split section beside the details; smaller widths stack the sections in reading order.

Payer choices are named radio tiles with clear selection and keyboard focus. Participants, manual shares and remainder feedback retain their existing behaviour. Until the amount is valid, a localized hint explains why split controls are absent. The amount and currency have consistent filled inputs. Receipt images use contain rather than cover so a preview does not crop the document.

Expenses now have an explicit Cancel action using the existing unsaved-change guard. Failed or pending saves and receipt operations retain their previous protections. Deletion stays separate from the primary actions.

Repayment creation keeps its dedicated modal and payer-to-recipient review, now with avatars, lighter typography and a prominent amount. Repayment editing uses the same design in its standalone page, preserving separate payer/recipient selection and currency editing. Dates, optional notes, initial-debt context, error messages and actions remain visible.

No financial parser, split algorithm, payload builder, freshness check, permission, backend schema or Android changes were made.

## Verification

- Production TypeScript/Vite build passed after cleanup. Existing bundle-size advisory remains.
- Web domain/regression suite: 32 passed.
- Lint: no errors or new warnings; existing warnings remain.
- 28 browser fixture layout cases passed at 320/390/768/1024/1440: new expense, expense edit, repayment edit, recurring edit and event-linked creation; additional maximum-amount/long-name, JPY-group and Spanish cases at 320.
- The repayment creation modal fit at 320, 390, 768 and 1440. Its initial EUR30 amount, Marta-to-Irene direction and amount focus were correct.
- EUR12.01 split equally into 3.01/3.00/3.00/3.00. Increasing one manual share by one cent showed a negative remainder and disabled saving. Returning to Equal restored the valid split.
- Clicking a payer tile and using ArrowRight selected the next payer. Toggling Repeat showed the start-date/frequency controls and hid the receipt controls.
- Cancel on a dirty expense opened the discard dialog. Keep editing retained the amount/manual shares and restored focus to Cancel.
- A mocked recurring save emitted one save_recurring_expense request with the selected payer, monthly frequency, Equal mode and exact EUR12.01 total/shares.
- A mocked ordinary expense save emitted one save_expense request for EUR8 with four EUR2 shares. While the response was held, its fieldset, Save and Cancel were disabled; a second button click generated no additional request. Releasing the response returned to the group.
- A repayment above the initial EUR30 balance was rejected before any request. A mocked EUR12.50 partial repayment emitted the correct payer, one recipient share and empty automatic note, and closed the modal.
- An injected repayment-edit save failure displayed the localized error, retained the EUR15 amount/custom note and re-enabled editing. Selecting the current recipient as payer cleared the recipient and excluded that payer from recipient choices.
- In a new JPY expense, 100.5 was rejected with the no-decimals message, hidden split inputs and disabled Save. JPY1001 split into 251/250/250/250 and enabled saving.
- All browser save operations above used the isolated fixture, which captures requests in memory. No live record, receipt or account was changed.

Layout receipts: [forms-design-review/layout-checks.json](./forms-design-review/layout-checks.json). Captured partial-repayment request: [forms-design-review/repayment-save.json](./forms-design-review/repayment-save.json).

## Limits and local review

These are automated browser layout/interaction and mocked-save checks, not a physical-device, screen-reader or live financial-write retest. The normal screenshot path timed out; a separate headless preview succeeded. Desktop expense creation, 390px expense editing, 390px repayment editing and desktop repayment creation screenshots were inspected. That review led to clearer modal input fills and two-column payer tiles at typical phone widths. Screenshots are stored in forms-design-review/. Receipt uploading/deleting was not repeated; only the preview styling changed.

Normal application: http://localhost:5173/. Isolated fixture: npm.cmd run dev --prefix webapp -- --config tests/visual.vite.config.ts --port 5187.

Example fixture: /groups/00000000-0000-0000-0000-000000000100/add-expense?qaLanguage=en&qaForms=default.
qaForms=save-error injects a failed save. qaForms=pending holds a save until the fixture's __axisReleaseFormWrite function is called. These are development-only fixture controls; never deploy the fixture configuration.
