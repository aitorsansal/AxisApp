# Axis web UI

The responsive web client follows the Android client's visual language. The receipt attachment marker remains the original `📎` emoji (including its eyed rendering on Windows); the linked-event marker is the Lucide calendar glyph.

## Layout

- Below 1000px: single-column pages, balances above activity, account avatar on the groups screen, and a floating add action. Narrow screens keep readable names instead of forcing desktop columns.
- From 1000px: persistent group sidebar, activity and balances in separate columns, header add action. Group cards become a two-column grid at 1250px.
- Forms stay constrained to 700px including page padding; group views can use up to 1440px. Date/time fields stack on narrow phones.
- Statistics and the detailed-balance mode are not implemented by this visual update.

## Shared components and tokens

- `src/index.css`: Android-derived palette, surfaces, control/card/pill radii, input and button states, visible focus, safe-area spacing, and reduced-motion support.
- `AppLayout`: authenticated responsive shell. `GroupsProvider` supplies group names, balances, member previews and the current profile to both sidebar and group cards. Navigation refreshes the directory; request versions prevent older responses overwriting newer ones.
- `AppHeader`: 44px back/account targets, optional page actions, and the account menu.
- `Icon` / `lib/icons.ts`: existing Android Lucide codepoints. The font is subsetted to about 10KB; see `public/fonts/README.md` and the bundled license.
- `Modal`: native modal dialog semantics, keyboard focus containment and Escape dismissal.
- `formatMoney`: locale-aware display only. Editable values and split calculations retain their existing cent-based semantics.
- Expense categories are the same ordered list as Android's `AppConstants.Categories.Keys` (rent/utilities included since 2026-10-30, Other last), with the same icon per category; a regression test checks both against `AppConstants.cs`.

## Validation on 26 October 2026

- Production TypeScript/Vite build passed. Oxlint completed with no errors and warnings about effect-based loading and existing mixed component/hook exports; Vite retains its large-bundle advisory.
- Brave visual inspection at desktop, 1024px, 390px and 320px widths; narrow group view checked for horizontal overflow.
- Equal split of EUR 0.05 produced 0.02 / 0.02 / 0.01.
- Invalid manual shares were rejected before saving.
- Saved one expense named `UI review - responsive split` in `TestGroupForEvents` only, EUR 0.05 with shares 0.01 / 0.01 / 0.03. Reopening preserved the manual shares and payer.
- Appearance dialog opened with labelled swatches/icons and closed with Escape without saving.
- Payer selection worked with keyboard arrow keys. Enabling Repeat exposed the start-date and frequency controls without saving a template.
- Receipt search returned both matching attachment-bearing expenses. Mobile event fields fit without horizontal overflow.

No database schema, authentication, settlement RPC or Android implementation changes are required by this UI update.

## Final polish

- Shared text actions now use the brighter action-text token and 44px targets; outline, danger and disabled states are consistent. Primary buttons use light text while pressed to preserve contrast on the darker pressed background.
- Measured contrast: text actions on list surfaces 8.80:1, primary normal 4.52:1, primary pressed 5.41:1, danger hover 5.12:1.
- Compact balance tiles dedicate a row to the member name; longer overflow truncates cleanly with the complete name retained in the accessible text and title.
- Members and recurring screens use their task name as the heading and retain the group as context. Member rename is a native button; Enter opens the editor and Escape cancels and restores focus without saving.
- Recurring rows share activity icons, amount typography and a separate action row. The live test group had no recurring templates: its empty state was checked at 320px; populated rows were reviewed in source.
- RSVP and transport controls use consistent targets and labelled seat actions. Attendee names and descriptions wrap within their list rows.
- Group dialogs restore focus to the persistent options trigger after closing. Escape focus restoration was verified in Brave.
- Initial event-load errors now show an alert and retry action instead of remaining on the loading message. This recovery branch was reviewed in source, without forcing a live server outage.
- This polish pass created no additional records and changed no existing records.

## Together Group Detail — 31 October 2026 (deployed)

Group Detail now extends the Together dashboard's design: lighter header/tabs, visible member and recurring links, separate directional balance totals, quieter pairwise rows and a filled activity list. The summary never calls cancelling debts settled. Existing repayments, tab URLs, search and refresh behaviour are retained. New activity styling is scoped so Event Detail keeps its own layout.

Build, 32 regression tests and lint pass. Browser fixture checks cover 25 viewport/state/accent combinations plus search, tabs and repayment opening/cancellation. See [Group Detail review](../reports/GROUP_DETAIL_DESIGN_REVIEW.md). The remaining pages were extended later the same day (see below).

## Together expense and repayment forms — 31 October 2026 (deployed)

Expense forms now group details, people/split and date/extras into neutral sections, side by side on wide screens and stacked on phones. Payer radio tiles wrap; primary/secondary actions match Together. Cancel retains unsaved-change protection. Repayment creation and editing share the same amount, party-summary and field styling. Receipt previews contain rather than crop the image.

Build, 32 regression tests and lint pass. The browser fixture exercises layouts, split/currency/recurrence interaction, guarded cancellation, held/failed saves and partial repayments. See [form review](../reports/FORMS_DESIGN_REVIEW.md) for precise coverage and limits.

## Remaining Together pages — 31 October 2026 (deployed)

Event Detail/editors, Group Events, Members, recurring expenses and member profiles now use Together layouts. TogetherPage.css supplies common lighter headers, filled panels and rounded actions. The roster is prominent; event response/transport and attendee sections are separate; recurring status and pairwise explanations stay explicit. Existing save, permission, financial and refresh rules are retained.

Build, 32 regressions and lint pass. See [remaining page review](../reports/REMAINING_PAGES_DESIGN_REVIEW.md) for 62 layout cases, screenshots, mocked interaction checks and their limits.

Deployment: the Together page updates above are live as of 31 October 2026. See [deployment receipt](../reports/WEB_DEPLOYMENT_2026-10-31.md).

## Activity payer labels — 31 October 2026 (local, not deployed)

Activity rows on Group Detail, Overview and Event Detail say who was paid for: "Marc paid for Bea", "You paid for 4 people" (payer not counted), "Marc paid you" for repayments, and plain "You paid" when the payer is the only share. The rule is in `src/lib/activityLabel.ts`, and the shares come from the same query. Android copy is unchanged.
