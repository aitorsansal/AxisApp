-- Multi-currency, Milestone 2 (2026-09-05) — see /MULTI_CURRENCY_PLAN.md
-- ============================================================
-- Adds the daily fetch-exchange-rates pg_cron job. Depends on Milestone 1's
-- exchange_rates table already existing live, and on the
-- fetch-exchange-rates Edge Function having been deployed first via the
-- dashboard's "Via Editor" flow (supabase/functions/fetch-exchange-rates/
-- index.ts is the version-controlled source — paste its contents in before
-- running this) — the cron job below is useless until that function exists
-- to call.
--
-- Same net.http_post + Vault service_role_key pattern the existing
-- cleanup-receipts cron already uses (schema.sql), so no new extension is
-- needed here — pg_net/pg_cron and the service_role_key Vault secret are
-- already live from that earlier work.
--
-- No begin/commit wrapper — this is a single statement, and cron.schedule
-- is itself already idempotent (re-running with the same job name updates
-- the existing job rather than duplicating it).
-- ============================================================

select cron.schedule(
  'fetch-exchange-rates',
  '0 6 * * *',
  $$
  select net.http_post(
    url := 'https://foepkovwmwyygulbdahv.supabase.co/functions/v1/fetch-exchange-rates',
    headers := jsonb_build_object(
      'Content-Type', 'application/json',
      'Authorization', 'Bearer ' || (select decrypted_secret from vault.decrypted_secrets where name = 'service_role_key' limit 1)
    ),
    body := '{}'::jsonb
  ) as request_id;
  $$
);

-- After running this, manually invoke the function once to confirm a real
-- row lands in exchange_rates, e.g. from the SQL editor:
--   select net.http_post(
--     url := 'https://foepkovwmwyygulbdahv.supabase.co/functions/v1/fetch-exchange-rates',
--     headers := jsonb_build_object(
--       'Content-Type', 'application/json',
--       'Authorization', 'Bearer ' || (select decrypted_secret from vault.decrypted_secrets where name = 'service_role_key' limit 1)
--     ),
--     body := '{}'::jsonb
--   );
-- then check net._http_response for a 200 and select * from exchange_rates;
