# Together overview implementation

Date: 2026-10-30. Implemented on `main` and deployed to production.

## Delivered

- `/` is the approved Together overview. Owed and owing remain separate, per currency and based on actual pairwise debts. An empty direction is compact; fully settled balances collapse to one line.
- Upcoming plans appear chronologically without a day picker. Each normal event has inline response controls, a missing-response indicator and, when applicable, drive/ride controls. Offering seats reveals a stepper. Birthdays do not request attendance.
- `/agenda` uses the same event component with 24 initial results and a load-more action. Overview requests five upcoming events, plus one to determine whether more exist. Realtime, focus, reconnect and visible polling keep attendance current.
- Recent activity contains the six latest expenses/repayments. It retains original/conversion preferences and date-only activity labels. Automatic entries show the next three active schedules, sorted with the existing recurring schedule rules.
- Desktop navigation keeps group links available. Mobile has labelled Overview/Agenda/Profile navigation and a group section on the overview. The reorderable directory is retained at `/groups` under Manage groups.
- The global Add expense action chooses the group before entering the existing editor. Review balances reveals real per-person debts and links to the owning group, preserving the existing repayment workflow and safeguards.
- Profile retains all eight accent presets and adds Controls only / Subtle surfaces. Preferences persist per browser. Neutral main backgrounds and financial/status meanings remain consistent.
- Shared tokens, self-hosted Manrope and an accent-aware linked-spaces header mark establish the design foundation. Remaining page layouts retain their existing structure pending the next design pass. Launcher/PWA bitmap icons have not been redesigned.
- New copy is available in English and Castilian Spanish.

## Data and write behaviour

No schema, permission or live-data change was made. Queries use the existing signed-in Supabase client and RLS. Activity is bounded; attendees and active recurring templates use the existing pagination helper. Financial sections fail independently of events and retain previously displayed data with an explicit error when refresh fails. Missing data is not presented as zero.

`eventAttendance.ts` holds the existing response/transport transitions and is shared by Overview, Group Events and Event Detail. `useUpcomingEvents` serializes per-event updates, coalesces rapid seat taps, rejects overtaken reads with `RefreshRevision`, disables controls during writes and keeps failed-save feedback visible. A Maybe reply clears a car offer while retaining a ride request; Not going clears transport. A seat offer is not a passenger assignment.

## Verification

- `npm run build --prefix webapp`: passed. Existing bundle-size advisory remains.
- `npm test --prefix webapp`: 32 passed, including new response/transport transition coverage and existing financial, date, recurrence and freshness regressions.
- `npm run lint --prefix webapp`: no errors; existing warnings remain. No warnings in the new components/hooks.
- Headless Chrome against the isolated fixture: widths 320, 390, 768, 1024 and 1440; no inspected-control or document horizontal overflow.
- States exercised: all settled, one empty balance direction, mixed EUR/GBP balances, no events, new account, event query failure, activity query failure and failed attendance write.
- Inline Going/Maybe/Not going, offer/request ride, seat stepper, coalesced writes, transport cleanup, and consistency after navigating to Agenda passed. Failed writes retain an error and do not falsely select a saved response.
- A deliberately delayed attendance read was released after a newer local reply; it did not overwrite that reply. Accent and neutral-surface preferences survived reload.
- axe WCAG 2 A/AA and 2.1 AA checks: zero violations across 32 Profile combinations (eight accents, two surface treatments, desktop/phone) and ten mobile routes: Overview, Agenda, directory, Group Detail, expense editor, Event Detail, Members, recurring list, new group and join. These are automated checks, not a complete accessibility audit.
- Desktop/phone screenshots were visually inspected. The checks used invented fixture data and in-memory attendance writes only; no live account or group was modified.

## Local review

Production configuration: `npm run dev --prefix webapp`.

Isolated fixture: `npm run dev --prefix webapp -- --config tests/visual.vite.config.ts`.
Use `?qaLanguage=en&qaOverview=interactive` for English and in-memory attendance changes.
Other `qaOverview` values: `settled`, `owe-zero`, `mixed`, `no-events`, `empty`,
`events-error`, `activity-error`, `save-error`.

During this review a separate fixture server was started on port 5186 because 5175 was already occupied. Never deploy with the fixture configuration.

## Remaining evidence

Authenticated operation against live data, real multi-client Realtime/reconnect delivery and physical phone interaction are not verified in this pass. Existing financial save code was not rewritten. Global typography/surfaces now apply to existing pages; their structural redesign remains a later slice.

Deployed with `npm run deploy --prefix webapp` on 2026-10-30. Cloudflare Worker `axisapp-webapp`, version `f27da25f-a7d7-4a63-8cdd-9cbebc15e8c8`. Post-deploy smoke checks passed for `/`, `/profile` and the expected JavaScript entry. This confirms delivery of the built assets; authenticated live-data verification remains outstanding.
