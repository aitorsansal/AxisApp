// send-push — fires on a new expense (see schema.sql's notify_new_expense trigger, called via
// pg_net the same way cleanup-receipts' cron job is), an event create/change/cancel/reminder
// (see schema.sql's notify_new_event/notify_event_changed/notify_event_cancelled triggers and the
// send_event_reminders cron job — /EVENTS_PLAN.md Milestone 5), or a birthday event's day-of push
// (send_birthday_notifications cron job — see schema.sql's "Birthday events" section; deliberately
// NOT notify_new_event, which is guarded to skip is_birthday rows since that fires on creation,
// months before the actual date). A settlement is just an expense
// with is_settlement = true (Payment/notify_new_payment/payment_notification_recipients were
// retired 2026-09-04 — see CLAUDE.md's "Merge payments into expenses" remarks), so there's only
// ever one expense branch. Deployed via the Supabase dashboard's browser editor, not the CLI —
// this file is the version-controlled source of truth; keep it in sync if the deployed function
// is ever edited directly in the dashboard.
//
// Recipient scoping happens in Postgres (expense_notification_recipients/
// event_notification_recipients/event_attendee_notification_recipients/event_reminder_recipients
// in schema.sql), not here — this function only turns that list into real FCM sends. The one
// exception is a cancelled event: its events row (and event_attendees) are already gone by the
// time this function's request actually arrives (pg_net delivery is asynchronous, and the row's
// own DELETE has already completed), so the cancellation trigger embeds the recipient list and
// message content directly in the request body instead of naming an id to look up — see this
// function's `event_type === "cancelled"` branch below and schema.sql's notify_event_cancelled
// remarks for why. SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY are injected automatically into
// every Edge Function's environment; FIREBASE_SERVICE_ACCOUNT_KEY is NOT — it must be set by hand
// under this function's own Secrets (the whole service-account JSON, as one string), separate
// from the database Vault (Vault secrets are for SQL-side callers like the triggers' own
// Authorization header; this one is only ever read here, in the function's own runtime).
//
// Android-only, per CLAUDE.md's push-notifications remarks — a recipient row with
// platform = 'windows' is silently skipped (IPushRegistrationService's Windows implementation is a
// deliberate no-op, so none should exist yet, but the filter is here regardless).
//
// Event push copy shows times in UTC, not converted to each recipient's own timezone — a known
// simplification (an Edge Function has no reliable way to know a given device's timezone), not
// something worth solving for a first pass.
//
// Not build-verified — no Deno runtime was available to type-check this against a real deploy the
// way every other piece of this feature was. Deploy it, trigger a real insert, and report back
// whatever the actual first-run error is, same as every other new Edge Function in this project.

import { createClient } from "jsr:@supabase/supabase-js@2";

interface Recipient {
  account_id: string;
  push_token: string;
  platform: string;
}

// FCM's HTTP v1 API is authenticated via a short-lived OAuth2 access token, exchanged for the
// service account's RS256-signed JWT — not a static bearer secret like service_role_key, so this
// can't reuse the Vault pattern even if the credential lived there. Built by hand with Deno's
// native Web Crypto API rather than pulling in a Google auth library, since RS256 signing is all
// this needs and Web Crypto already does it with no extra dependency.
async function getFcmAccessToken(serviceAccountJson: string): Promise<string> {
  const sa = JSON.parse(serviceAccountJson);
  const now = Math.floor(Date.now() / 1000);

  const encoder = new TextEncoder();
  const base64url = (bytes: Uint8Array) =>
    btoa(String.fromCharCode(...bytes)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");

  const header = { alg: "RS256", typ: "JWT" };
  const claims = {
    iss: sa.client_email,
    scope: "https://www.googleapis.com/auth/firebase.messaging",
    aud: "https://oauth2.googleapis.com/token",
    iat: now,
    exp: now + 3600,
  };

  const headerB64 = base64url(encoder.encode(JSON.stringify(header)));
  const claimsB64 = base64url(encoder.encode(JSON.stringify(claims)));
  const signingInput = `${headerB64}.${claimsB64}`;

  const pemBody = (sa.private_key as string)
    .replace("-----BEGIN PRIVATE KEY-----", "")
    .replace("-----END PRIVATE KEY-----", "")
    .replace(/\s/g, "");
  const keyBytes = Uint8Array.from(atob(pemBody), (c) => c.charCodeAt(0));

  const key = await crypto.subtle.importKey(
    "pkcs8",
    keyBytes,
    { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" },
    false,
    ["sign"],
  );

  const signature = await crypto.subtle.sign(
    "RSASSA-PKCS1-v1_5",
    key,
    encoder.encode(signingInput),
  );

  const jwt = `${signingInput}.${base64url(new Uint8Array(signature))}`;

  const tokenResponse = await fetch("https://oauth2.googleapis.com/token", {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      grant_type: "urn:ietf:params:oauth:grant-type:jwt-bearer",
      assertion: jwt,
    }),
  });

  if (!tokenResponse.ok) {
    throw new Error(`FCM OAuth2 token exchange failed: ${await tokenResponse.text()}`);
  }

  const { access_token } = await tokenResponse.json();
  return access_token as string;
}

// UTC, deliberately — see this file's header comment on why per-recipient timezone conversion
// isn't attempted here.
function formatEventTime(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleString("en-US", {
    weekday: "short", month: "short", day: "numeric", hour: "2-digit", minute: "2-digit",
    timeZone: "UTC",
  }) + " UTC";
}

Deno.serve(async (req) => {
  if (req.method !== "POST") {
    return new Response("Method not allowed", { status: 405 });
  }

  const payload = await req.json();
  const { expense_id, event_id, event_type, actor_account_id } = payload;
  if (!expense_id && !event_id && event_type !== "cancelled") {
    return new Response(JSON.stringify({ error: "expense_id or event_id required" }), {
      status: 400,
      headers: { "Content-Type": "application/json" },
    });
  }

  const supabase = createClient(
    Deno.env.get("SUPABASE_URL")!,
    Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
  );

  let recipients: Recipient[] = [];
  let title = "Axis";
  let body = "";
  let groupId = "";
  let isSettlement = false;
  let pushType = "expense";

  if (expense_id) {
    const { data: recipientRows, error: recError } = await supabase
      .rpc("expense_notification_recipients", { p_expense_id: expense_id })
      .returns<Recipient[]>();
    if (recError) {
      return new Response(JSON.stringify({ error: recError.message }), { status: 500 });
    }
    recipients = recipientRows ?? [];

    const { data: expense } = await supabase
      .from("expenses")
      .select("group_id, description, amount, currency, is_settlement, groups(name), members!expenses_paid_by_member_id_fkey(display_name)")
      .eq("id", expense_id)
      .single();

    if (expense) {
      // deno-lint-ignore no-explicit-any
      const e = expense as any;
      const groupName = e.groups?.name ?? "your group";
      const payerName = e.members?.display_name ?? "Someone";
      isSettlement = e.is_settlement === true;
      title = groupName;
      body = isSettlement
        ? `${payerName} paid you back — ${e.amount} ${e.currency}`
        : `${payerName} added ${e.description || "an expense"} — ${e.amount} ${e.currency}`;
      groupId = e.group_id ?? "";
    }
    pushType = isSettlement ? "settlement" : "expense";
  } else if (event_type === "cancelled") {
    // No live events/event_attendees row to query — the notify_event_cancelled trigger (BEFORE
    // DELETE) already embedded everything needed directly in the request body, since by the time
    // this async request actually arrives the row is long gone regardless of trigger timing. See
    // schema.sql's notify_event_cancelled remarks.
    const embedded = (payload.recipients ?? []) as Recipient[];
    recipients = embedded;
    title = payload.group_name ?? "your group";
    groupId = payload.group_id ?? "";
    body = `${payload.title ?? "An event"} was cancelled`;
    pushType = "event_cancelled";
  } else {
    const { data: eventRow } = await supabase
      .from("events")
      .select("group_id, title, starts_at, groups(name)")
      .eq("id", event_id)
      .single();

    // deno-lint-ignore no-explicit-any
    const ev = eventRow as any;
    const groupName = ev?.groups?.name ?? "your group";
    const eventTitle = ev?.title ?? "an event";
    const whenText = ev?.starts_at ? formatEventTime(ev.starts_at) : "";
    title = groupName;
    groupId = ev?.group_id ?? "";

    if (event_type === "birthday") {
      // eventTitle is already "🎂 {name}'s Birthday" (see materialize_birthday_events in
      // schema.sql) — used directly as the body, no "New event:"/whenText prefix, since this
      // isn't a generic event notification.
      body = eventTitle;
      pushType = "event_birthday";
      const { data: recipientRows } = await supabase
        .rpc("event_birthday_notification_recipients", { p_event_id: event_id })
        .returns<Recipient[]>();
      recipients = recipientRows ?? [];
    } else if (event_type === "changed") {
      body = `${eventTitle} was updated — new time or location`;
      pushType = "event_changed";
      const { data: recipientRows } = await supabase
        .rpc("event_attendee_notification_recipients", {
          p_event_id: event_id,
          p_actor_account_id: actor_account_id ?? null,
        })
        .returns<Recipient[]>();
      recipients = recipientRows ?? [];
    } else if (event_type === "reminder") {
      body = `Starting soon: ${eventTitle} — ${whenText}`;
      pushType = "event_reminder";
      const { data: recipientRows } = await supabase
        .rpc("event_reminder_recipients", { p_event_id: event_id })
        .returns<Recipient[]>();
      recipients = recipientRows ?? [];
    } else {
      body = `New event: ${eventTitle} — ${whenText}`;
      pushType = "event_created";
      const { data: recipientRows } = await supabase
        .rpc("event_notification_recipients", { p_event_id: event_id })
        .returns<Recipient[]>();
      recipients = recipientRows ?? [];
    }
  }

  const androidRecipients = recipients.filter((r) => r.platform === "android");
  if (androidRecipients.length === 0) {
    return new Response(JSON.stringify({ sent: 0, reason: "no android recipients" }), {
      headers: { "Content-Type": "application/json" },
    });
  }

  const serviceAccountJson = Deno.env.get("FIREBASE_SERVICE_ACCOUNT_KEY");
  if (!serviceAccountJson) {
    return new Response(JSON.stringify({ error: "FIREBASE_SERVICE_ACCOUNT_KEY not configured" }), {
      status: 500,
    });
  }

  let accessToken: string;
  try {
    accessToken = await getFcmAccessToken(serviceAccountJson);
  } catch (err) {
    return new Response(JSON.stringify({ error: (err as Error).message }), { status: 500 });
  }

  const projectId = JSON.parse(serviceAccountJson).project_id;

  let sent = 0;
  const failures: string[] = [];
  for (const recipient of androidRecipients) {
    const res = await fetch(`https://fcm.googleapis.com/v1/projects/${projectId}/messages:send`, {
      method: "POST",
      headers: {
        "Authorization": `Bearer ${accessToken}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        message: {
          token: recipient.push_token,
          // Data-only, deliberately no "notification" block — that would make Android
          // auto-display it using Firebase's own default handling, with no way to control the
          // tap action or the channel it lands in. Sending everything as data instead forces
          // AxisFirebaseMessagingService.OnMessageReceived to fire and build the notification
          // itself (channel, tap-to-open PendingIntent carrying group_id) — see CLAUDE.md's
          // push-notifications remarks. All values must be strings; FCM data payloads don't
          // support other JSON types.
          data: {
            type: pushType,
            expense_id: expense_id ?? "",
            event_id: event_id ?? "",
            group_id: groupId,
            group_name: title,
            title,
            body,
          },
        },
      }),
    });

    if (res.ok) {
      sent++;
    } else {
      failures.push(await res.text());
    }
  }

  return new Response(JSON.stringify({ sent, failed: failures.length, failures }), {
    headers: { "Content-Type": "application/json" },
  });
});
