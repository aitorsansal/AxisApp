# Stabilisation step 5 — 30 October 2026

## Changes

Android: localized icon-only action names, named icon/colour choices with selection,
decorative glyph exclusion, section headings, activity categories/settlements,
empty Balances/Going text, accessible chart summaries and Focus text contrast.
ProfileCircle accepts an accessible name for the Groups account action. Group tabs,
Upcoming/Past and all four stats ranges include localized selected text, updating
when selection changes. These are spoken names, not a native selection-role claim.

Web: html language follows UI language, localized route titles, Login main landmark,
receipt alt text, calendar-feed field name, month h2 headings, conditional error
references, and readable member-profile balance cards. Blue #6177D8 and Red #C55D59
replace fills that narrowly failed 4.5:1 with dark on-accent text on both clients.

## Evidence supplied in the handoff

The previous session reported axe-core zero violations across 13 fixture routes ×
8 accents, plus Login; Android builds with zero errors; compressed physical-phone
trees for Groups, Group Detail, appearance, Events, event detail, Stats and Members.
Those results are prior-session evidence, not rerun results from this continuation.

## Continuation checks

- Web tests: 31/31 pass; TypeScript passes; lint has existing warnings, no errors.
- Android build with selection converter: zero errors (existing dependency/XamlC warnings).
- Synthetic join fixture: claim card names Preview Guest in Preview QA and explains
  history transfer. Used shows “Esta invitación ya se ha usado.” Inviter gone shows
  “Esta invitación ya no es válida. Pide una nueva a un miembro actual del grupo.”
  Both dead states disable Join. No invite created/redeemed; this proves rendering,
  not live SQL production of those statuses. Repeat with /join?code=synthetic-qa&qaInvite=claim|used|inviter_gone
  under tests/visual.vite.config.ts only.
- Green member profile: rendered #F6F5F1 text and #38D998 positive amount on #1B1B1F.
- Fresh production signed-out startup reaches Login with no captured warnings/errors.
  This does not exercise messaging registration/delivery. Existing authenticated
  Brave tabs could not be controlled (focus command timeout).
- An adb-only install of the default Debug APK aborted because it expected Fast
  Deployment assemblies. Rebuild with -p:EmbedAssembliesIntoApk=true for a standalone
  adb installation; no application data was cleared.

## Pending evidence

The user explicitly removed the manual TalkBack walkthrough from scope while retaining all accessibility fixes. It is not a release requirement. A real event push tap,
controlled Wi-Fi reconnect with a second-client change, physical Register password toggles and the Recurring pencil remain pending
unless later continuation notes below explicitly record them. U03 still needs
an authenticated registration/delivery check. Do not close the entire W21–32 bundle:
it contains unrelated findings (receipt lifecycle, identity defaults, dialogs, etc.).


## Continuation results

Standalone Android build: zero errors, 57 existing dependency/XamlC warnings,
installed successfully with streamed adb install on f54f4285. It opened signed out;
no app data was explicitly cleared. Login password toggle names were verified on
this build in both directions (Mostrar contraseña / Ocultar contraseña), with
compressed trees saved under accessibility-review/. The user signed back in and authenticated checks below then passed. The native version remains 1.1.1 (11), debug only, not a store release.

Web deployed with npm run deploy --prefix webapp, Worker version
5b250b41-27fb-4169-ade7-76335e470e07; HTTP smoke checks passed for /, /profile and
the expected JavaScript entry. Login now has its own localized title (including
signup mode), as does Not Found. The local Spanish Login DOM has lang=es,
Iniciar sesión · Axis and exactly one main landmark.

Calculated contrast: #08101F on Blue #6177D8 is 4.65:1, on Red #C55D59 is 4.60:1.
Member-card primary #F6F5F1 and positive #38D998 on #1B1B1F are 15.74:1 and 9.44:1.


## Final physical-device checks

After the user signed back into the test account:

- Groups account avatar exposes Perfil (not TG).
- Expenses starts Gastos, seleccionado. Stats switches to Estadísticas,
  seleccionado and Todo, seleccionado. Tapping 3 months changes that name to
  3 meses, seleccionado and removes selection from Todo.
- Events exposes Eventos, seleccionado; Upcoming/Past names switch correctly,
  with exactly the active choice carrying seleccionado.
- Event detail exposes Compartir / Editar evento. Offering a car exposes
  Menos plazas disponibles / Más plazas disponibles. The test account was briefly
  switched from needs_ride to offering (four seats), then restored to needs_ride.
  The displayed totals returned to six seats / one needing a ride, as before.
- Invite page exposes Menos usos / Más usos and Copiar enlace on the copy glyph.
  Opened the general invite page (GetOrCreate may reuse or create a token); no
  link was copied or shared, no invite redeemed and no settings changed.
  The stored invite tree redacts its token.
- The inspected TestGroupForEvents recurring list is empty; no template was
  created just to test its pencil. That control and Register toggles remain
  device-unverified. Login toggles were verified before sign-in.
- Wi-Fi was cycled off/on and restored enabled; later Members/invite reads
  succeeded. No controlled second-client change was available, so this does
  not prove refresh-on-reconnect of stale attendance.

Compressed hierarchy evidence is in accessibility-review/*.xml. It verifies
accessible names and their state transitions; it is not a spoken usability audit.
Manual TalkBack walkthrough is out of scope by explicit user decision.
