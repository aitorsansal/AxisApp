// cleanup-receipts — weekly Storage cleanup for the `receipts` bucket, triggered by pg_cron via
// pg_net (see schema.sql's "find_expired_receipts" / cron.schedule('cleanup-receipts', ...)
// remarks). Deployed via the Supabase dashboard's browser editor, not the CLI — this file is the
// version-controlled source of truth; keep it in sync if the deployed function is ever edited
// directly in the dashboard.
//
// Does the real deletion work that find_expired_receipts() (a pure SQL function) deliberately
// doesn't: a plain `DELETE FROM storage.objects` only removes the metadata row, not the actual
// stored file, so every delete here goes through the real Storage API. SUPABASE_URL and
// SUPABASE_SERVICE_ROLE_KEY are injected automatically into every Edge Function's environment —
// no extra secret needed for this client, only for the pg_net -> function call itself (see
// schema.sql's Vault remarks).

import { createClient } from "jsr:@supabase/supabase-js@2";

interface ExpiredReceipt {
  path: string;
  kind: "orphan" | "attached";
  expense_id: string | null;
}

Deno.serve(async (req) => {
  if (req.method !== "POST") {
    return new Response("Method not allowed", { status: 405 });
  }

  const supabase = createClient(
    Deno.env.get("SUPABASE_URL")!,
    Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
  );

  const { data: candidates, error: findError } = await supabase
    .rpc("find_expired_receipts")
    .returns<ExpiredReceipt[]>();

  if (findError) {
    return new Response(JSON.stringify({ error: findError.message }), {
      status: 500,
      headers: { "Content-Type": "application/json" },
    });
  }

  const orphans = (candidates ?? []).filter((c) => c.kind === "orphan");
  const attached = (candidates ?? []).filter((c) => c.kind === "attached");
  const allPaths = [...orphans, ...attached].map((c) => c.path);

  let filesRemoved = 0;
  if (allPaths.length > 0) {
    const { data: removed, error: removeError } = await supabase.storage
      .from("receipts")
      .remove(allPaths);

    if (removeError) {
      return new Response(JSON.stringify({ error: removeError.message }), {
        status: 500,
        headers: { "Content-Type": "application/json" },
      });
    }
    filesRemoved = removed?.length ?? 0;
  }

  // Only null out receipt_path once the file delete above actually succeeded — an expense should
  // never end up pointing at nothing while the file it pointed at still silently exists, or vice
  // versa never get its dangling reference cleared because this step was skipped.
  if (attached.length > 0) {
    const expenseIds = attached.map((c) => c.expense_id);
    const { error: updateError } = await supabase
      .from("expenses")
      .update({ receipt_path: null })
      .in("id", expenseIds);

    if (updateError) {
      return new Response(JSON.stringify({ error: updateError.message }), {
        status: 500,
        headers: { "Content-Type": "application/json" },
      });
    }
  }

  return new Response(
    JSON.stringify({
      orphans_deleted: orphans.length,
      attached_photos_purged: attached.length,
      files_removed: filesRemoved,
    }),
    { headers: { "Content-Type": "application/json" } },
  );
});
