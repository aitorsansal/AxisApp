# Split-mode rollout — 28 October 2026

## Apply

1. Open `split_mode.sql` and execute the entire file in the production Supabase SQL Editor. Do not rerun `schema.sql` against the existing database.
2. The final result should contain `expenses.split_mode` and `recurring_expenses.split_mode`, both nullable text columns. The transaction also replaces the two hardened save functions and recurring materializer, then requests a PostgREST schema-cache reload.
3. Refresh the local web app and verify a new Equal expense through save/reopen, then change its total. Verify Manual and recurring editing too. Deploy the web update after this migration. Android support is a separate follow-up.

The migration has been prepared and tested locally; it has not been applied to production by this task.

## Compatibility and preservation

- Existing rows retain NULL mode: their original intent is unknown. The web editor opens them as Manual and preserves saved shares until the user changes them. Saving then records the selected mode.
- New web expenses and templates save `equal` or `manual` explicitly. Manual amounts are never classified as Equal by inspecting their values.
- Reopening Equal preserves the saved per-person cent allocation, even if member ordering changes. Changing the total or participant set recalculates with integer cents. Clicking Equal explicitly recalculates too.
- An older client can omit the new key. New rows then have NULL mode. An unchanged existing split retains its known mode; changing an Equal split's total or shares clears its mode to NULL. Existing Manual mode remains Manual.
- An explicit Equal save must have positive shares summing to the amount, with a maximum difference of one cent. Existing sum/participant/authorization validation remains in place.
- Generated recurring expenses inherit the template mode. Migration does not generate expenses or change any schedule, amount, participant, share, or balance.
- Existing settlements retain their separate editor and save path. New settlement saves have no split mode.
- The web checks that the target table exposes the new column before sending an expense save, avoiding a silent loss of intent against the old API. A schema/configuration failure displays a generic saving-unavailable message and performs no save.

## Rollback

Revert the web release while retaining this additive database migration. Old clients remain compatible, and stored metadata is preserved. No destructive rollback is required.

## Validation

- Web regression suite: 19 passing tests, including explicit/unknown/manual restoration, preserving the original remainder recipient, participant/total changes, and existing cent-allocation cases.
- Web production build passed. Lint passed with existing warnings; `git diff --check` passed.
- `tests/split_mode.test.mjs` ran the actual migration twice in isolated PGlite PostgreSQL. It checks existing-row preservation, Equal/Manual saves, old-client writes, rejection rollback, authentication/group checks, recurrence inheritance, preserved paused/schedule metadata, execute grants and parity with fresh-schema function definitions.
- The SQL fixture uses a minimal schema and membership/authentication stubs. This verifies migration behavior without production writes; it is not a complete Supabase RLS/integration test. Real web save/reopen awaits the user's SQL execution. Android has not been updated or retested for split-mode persistence.

The SQL test imports `@electric-sql/pglite`. Install that package in an isolated tool directory and set `PGLITE_MODULE` to its `dist/index.js` file URL, or make it available through normal Node resolution, then run `node supabase/tests/split_mode.test.mjs` from the repository root.

## Rollout follow-up

The user confirmed the SQL completed with both nullable text columns and that web Equal save/reopen worked. Android was then implemented, installed as 1.1.0 (10), and verified on the connected phone. See [Android verification](../reports/ANDROID_SPLIT_MODE_REVIEW.md) for the live save/reopen and recurring checks, retained isolated test data and coverage limits. Earlier pending statements above describe the preparation stage.
