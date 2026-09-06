// fetch-exchange-rates — daily refresh of the exchange_rates singleton (see schema.sql's
// "exchange_rates" remarks / MULTI_CURRENCY_PLAN.md Milestone 2), triggered by pg_cron via
// pg_net. Deployed via the Supabase dashboard's browser editor, not the CLI — this file is the
// version-controlled source of truth; keep it in sync if the deployed function is ever edited
// directly in the dashboard.
//
// Calls Frankfurter (frankfurter.dev, free, no API key) for EUR-pivoted latest rates and writes
// them as the single exchange_rates row via delete-then-insert — not upsert — matching this
// project's existing idiom for enforcing uniqueness without trusting an ON CONFLICT target
// (SupabaseDeviceTokensRepository.RegisterAsync). SUPABASE_URL/SUPABASE_SERVICE_ROLE_KEY are
// injected automatically into every Edge Function's environment.

import { createClient } from "jsr:@supabase/supabase-js@2";

interface FrankfurterResponse {
  amount: number;
  base: string;
  date: string;
  rates: Record<string, number>;
}

Deno.serve(async (req) => {
  if (req.method !== "POST") {
    return new Response("Method not allowed", { status: 405 });
  }

  const frankfurterResponse = await fetch("https://api.frankfurter.dev/v1/latest?base=EUR");
  if (!frankfurterResponse.ok) {
    return new Response(
      JSON.stringify({ error: `Frankfurter request failed: ${frankfurterResponse.status}` }),
      { status: 502, headers: { "Content-Type": "application/json" } },
    );
  }

  const { rates, date }: FrankfurterResponse = await frankfurterResponse.json();

  const supabase = createClient(
    Deno.env.get("SUPABASE_URL")!,
    Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
  );

  // Delete-then-insert rather than upsert, matching RegisterAsync's own reasoning — the
  // singleton's `id boolean primary key default true` already guarantees at most one row can
  // exist, so this just clears whatever's there (if anything) before writing the fresh one.
  const { error: deleteError } = await supabase
    .from("exchange_rates")
    .delete()
    .eq("id", true);

  if (deleteError) {
    return new Response(JSON.stringify({ error: deleteError.message }), {
      status: 500,
      headers: { "Content-Type": "application/json" },
    });
  }

  const { error: insertError } = await supabase
    .from("exchange_rates")
    .insert({ id: true, as_of: date, rates });

  if (insertError) {
    return new Response(JSON.stringify({ error: insertError.message }), {
      status: 500,
      headers: { "Content-Type": "application/json" },
    });
  }

  return new Response(
    JSON.stringify({ as_of: date, currencies_cached: Object.keys(rates).length }),
    { headers: { "Content-Type": "application/json" } },
  );
});
