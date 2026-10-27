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
- Expense categories include the current Android keys and legacy web rent/utilities keys so editing older records preserves their category.

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
