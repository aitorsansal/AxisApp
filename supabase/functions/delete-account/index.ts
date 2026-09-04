// delete-account — self-service account deletion, called directly by the app (not by a SQL
// trigger/cron via pg_net like send-push/cleanup-receipts, so unlike those this function has to
// authenticate its own caller). Deployed via the Supabase dashboard's browser editor, not the
// CLI — this file is the version-controlled source of truth; keep it in sync if the deployed
// function is ever edited directly in the dashboard.
//
// Two-step deletion: the app-level cleanup (unlink the caller's member row back to a phantom,
// delete groups they own with no other members, delete their device tokens — see schema.sql's
// delete_account() remarks) runs under the caller's own session/RLS via a caller-scoped client,
// so its "you still own a group with other members" guard behaves exactly like every other
// guarded RPC in this app. Only the final auth.users removal needs the service-role admin client
// — deleting via supabase.auth.admin.deleteUser() rather than a raw `delete from auth.users` is
// deliberate, since only the Admin API correctly cleans up GoTrue's own internal session/identity
// tables. SUPABASE_URL/SUPABASE_ANON_KEY/SUPABASE_SERVICE_ROLE_KEY are all injected automatically
// into every Edge Function's environment — no extra secret needed for either client here.

import { createClient } from "jsr:@supabase/supabase-js@2";

Deno.serve(async (req) => {
  if (req.method !== "POST") {
    return new Response(JSON.stringify({ error: "Method not allowed" }), {
      status: 405,
      headers: { "Content-Type": "application/json" },
    });
  }

  const authHeader = req.headers.get("Authorization");
  if (!authHeader) {
    return new Response(JSON.stringify({ error: "Missing Authorization header" }), {
      status: 401,
      headers: { "Content-Type": "application/json" },
    });
  }

  // Scoped to the caller's own session, so delete_account() runs under their own auth.uid()/RLS.
  const callerClient = createClient(
    Deno.env.get("SUPABASE_URL")!,
    Deno.env.get("SUPABASE_ANON_KEY")!,
    { global: { headers: { Authorization: authHeader } } },
  );

  const { data: userData, error: userError } = await callerClient.auth.getUser();
  if (userError || !userData?.user) {
    return new Response(JSON.stringify({ error: "Invalid session" }), {
      status: 401,
      headers: { "Content-Type": "application/json" },
    });
  }
  const userId = userData.user.id;

  const { data: avatarPath, error: cleanupError } = await callerClient.rpc("delete_account");
  if (cleanupError) {
    // Surfaces the raise exception message verbatim — e.g. the "transfer ownership or dissolve
    // groups with other members first" guard — so the app can show it as-is.
    return new Response(JSON.stringify({ error: cleanupError.message }), {
      status: 400,
      headers: { "Content-Type": "application/json" },
    });
  }

  const adminClient = createClient(
    Deno.env.get("SUPABASE_URL")!,
    Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
  );

  if (avatarPath) {
    await adminClient.storage.from("avatars").remove([avatarPath]);
  }

  const { error: deleteError } = await adminClient.auth.admin.deleteUser(userId);
  if (deleteError) {
    return new Response(JSON.stringify({ error: deleteError.message }), {
      status: 500,
      headers: { "Content-Type": "application/json" },
    });
  }

  return new Response(JSON.stringify({ success: true }), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
});
