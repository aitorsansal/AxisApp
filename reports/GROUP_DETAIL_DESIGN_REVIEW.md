# Group Detail Together design

Date: 2026-10-31. Deployed from source commit b2367da. See [deployment receipt](./WEB_DEPLOYMENT_2026-10-31.md).

## Changes

Group Detail now follows the Together dashboard's lighter typography, neutral surfaces and directional balance presentation. The header shows the group's appearance, a member-count link and its currency, with a visible recurring-expense shortcut. Expenses/Events/Stats remain URL-backed choices. Activity uses a filled list with restrained category icons, clearer amount/date hierarchy and viewer-localized "You paid" copy.

The previous headline subtracted owing from owed and displayed "Settled up" for a zero result, even when two real pairwise debts cancelled. The headline now shows separate owed/owing amounts from the existing currency-aware pairwise summary, never a net. A side with no debt is compact; the fully settled state appears only when both sides are empty. Its explanation refers to the viewer's balances, not every relationship in the group. Balance rows use absolute amounts with an explicit debt direction, omit zero amounts at the currency's displayed precision and retain the existing repayment review. Repayment buttons have person-specific accessible names that include their visible labels.

Desktop places activity beside balances when enough space is available (1200px and above). Smaller layouts stack the panels. At the narrowest widths, activity amounts wrap beneath their description. Event Detail's shared activity baseline remains intact; the new activity presentation is scoped to Group Detail.

No backend, financial write, Android or deployment changes were made.

## Verification

- Production TypeScript/Vite build passed after the final CSS cleanup. Existing bundle-size advisory remains.
- Web regression suite: 32 tests passed. Existing financial, currency, date, navigation snapshot and refresh rules remain covered.
- Lint passed with existing warnings; no new warnings in the changed files.
- Browser checks used the isolated visual fixture on port 5187. No real account or record was modified.
- 25 layout/state combinations passed document and inspected-element horizontal-overflow checks: default widths 320, 390, 768, 1024 and 1440; offsetting debts, settled, empty, one-sided, long-name/maximum-amount and JPY states at 320 and 1440; Spanish offsetting debts at 320 across all eight accents.
- The offsetting-debt case shows both EUR30 directions and never "Settled up". JPY shows whole units. Empty and settled cases omit zero balance rows. Long text and maximum amounts remain within the viewport.
- Search returned just the matching Mercadona record; an unmatched query showed the empty result message.
- Events/Stats switches selected the correct controls and updated the URL; Events retained the event-creation link. Both screens fit the narrow viewport.
- Opening Marta's repayment displayed Marta as payer, the viewer as recipient and EUR30.00 as the initial amount, with focus in that amount field. Escape closed the unchanged form and returned focus to its trigger. Nothing was saved.
- Desktop and 390px fixture screenshots were visually inspected. The browser's default screenshot path timed out; viewport capture through the same dedicated automation browser succeeded for the preview tab.

DOM measurements: [group-detail-review/layout-checks.json](./group-detail-review/layout-checks.json).
Screenshots: [desktop](./group-detail-review/desktop.png), [390px frame](./group-detail-review/mobile.png).

## Limits and review

The checks above are browser fixture checks, not a physical-device or complete screen-reader audit. A repayment save was not repeated because the financial save implementation was unchanged. Events/Stats content layouts, expense editors and the remaining standalone pages retain their existing structures.

Normal local app: `http://localhost:5173/`.
Isolated preview: `npm.cmd run dev --prefix webapp -- --config tests/visual.vite.config.ts --port 5187`.
Example: `/groups/00000000-0000-0000-0000-000000000100?qaLanguage=en&qaGroup=offset`.
Other `qaGroup` states: `settled`, `empty`, `owed-only`, `large`, `jpy`.
Never deploy using the fixture configuration.

## Follow-up: payer labels (2026-10-31, local, not deployed)

The activity payer line now names who an expense covered. One person other than the payer reads "Marc paid for Bea", "You paid for Marc" or "Marc paid for you". Two or more read "You paid for N people", without counting the payer. A payer-only expense keeps "You paid" / "Marc paid". Repayments read "You paid Marc" / "Marc paid you". Shares are embedded in the same activity query (`expense_shares(member_id)`), so initial load, load more and search all carry them. The rule is in `lib/activityLabel.ts`. 33 web regressions and the production build pass, and the user checked it in the local app. See CHANGELOG.md, "Activity payer labels (web)".
