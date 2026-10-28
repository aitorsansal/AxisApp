# Calendar timestamp fix — 28 October 2026

## Change

`supabase/functions/calendar-feed/index.ts` now parses each timestamp as an instant and normalizes it with `toISOString()` before converting it to basic UTC calendar form. Milliseconds are omitted and exactly one `Z` is emitted.

The previous string-only formatter could emit `20261028T103045ZZ` or retain an offset before `Z`. For inputs with fractional seconds and an offset, splitting at the decimal point discarded the offset without converting the clock, exporting the wrong instant.

Examples:

| Source | Exported UTC |
| --- | --- |
| `2026-10-28T12:30:45+02:00` | `20261028T103045Z` |
| `2026-10-28T12:30:45.123456+02:00` | `20261028T103045Z` |
| `2026-10-28T06:30:45-04:00` | `20261028T103045Z` |
| `2026-01-01T00:30:00+02:00` | `20251231T223000Z` |

Event selection, token lookup, JWT settings, text escaping, UIDs, reminders and the existing one-hour fallback were not changed. No SQL migration or client rebuild is required.

## Regression proof

`supabase/tests/calendar_feed.test.mjs` exercises helpers extracted from the actual single-file deployed source, without importing the remote client or starting Deno. It uses ICAL.js to import generated calendars and verify the resulting UTC instants and text fields.

Before the formatter fix, six of seven tests failed. After the fix, all seven pass:

- UTC inputs with/without fractions; exactly one `Z`.
- Positive, negative and half-hour offsets, including fractional PostgreSQL timestamps.
- Day/year rollover.
- Explicit start/end import and exact basic-UTC syntax for DTSTART/DTEND/DTSTAMP.
- One-hour fallback across midnight and spring/autumn DST transitions.
- Preserved event identity, escaped Unicode/text, location and reminder.
- Invalid timestamps throw rather than generating broken date text.

Run with `node --test supabase/tests/calendar_feed.test.mjs`. The test imports `ical.js`; install it in an isolated tool directory and set `ICAL_MODULE` to its `dist/ical.js` file URL, or provide it through normal Node module resolution. The test harness uses Node 24's TypeScript stripping and reports its experimental-API warning.

## Deployment

The authenticated Supabase dashboard editor's previous source matched the repository before the fix. Initial deployment attempts failed while downloading the existing floating JSR dependency: Supabase could not load the metadata for `@supabase/supabase-js@2.118.0` before its timeout. The import was changed to the same resolved release through `npm:@supabase/supabase-js@2.118.0`, avoiding JSR and pinning the dependency. The deployed editor content was checked against local source before submission.

Supabase subsequently confirmed a new deployment on **28 October 2026 at approximately 12:58 Europe/Madrid**. Existing JWT settings were not changed. See [deployment evidence](calendar-feed-review/deployed.png).

## Live verification

After deployment, the existing QA account's calendar feed returned **HTTP 200** with `text/calendar; charset=utf-8`. Its **three events** contained **nine DTSTART/DTEND/DTSTAMP lines**, all matching basic UTC format. The complete feed imported through ICAL.js, and all start/end values parsed as finite instants.

The private subscription URL and downloaded event content were kept out of repository artifacts; temporary files were removed after verification. Fetching the feed updates its existing `last_accessed_at` metadata. No event or ledger records were created or edited. Invalid-token GET and unsupported POST smoke checks returned the expected 404 and 405; these smoke checks were performed during rollout.

This verifies live output and parser import, plus synthetic offset/end/fallback cases. No Google, Apple or Outlook calendar subscription was added or refreshed manually, so external clients' polling/cache behavior was not tested.
