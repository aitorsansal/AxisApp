// calendar-feed — public, unauthenticated .ics endpoint backing the calendar_subscriptions table
// (see schema.sql's "Calendar subscription feed" remarks). Deployed via the Supabase dashboard's
// browser editor, not the CLI — this file is the version-controlled source of truth; keep it in
// sync if the deployed function is ever edited directly in the dashboard.
//
// Unlike every other function in this folder, this one MUST be deployed with "Enforce JWT
// Verification" turned OFF in the dashboard — a calendar client (Google/Apple/Outlook) does a
// plain GET with no Authorization header at all when it polls a subscribed feed. The token in
// the URL path is the only credential; it's looked up with the service-role key, deliberately
// bypassing calendar_subscriptions' RLS, because the request was never authenticated as any
// Supabase account to begin with. Never log the full request URL anywhere for this function —
// the URL itself is the secret.
//
// SUPABASE_URL/SUPABASE_SERVICE_ROLE_KEY are injected automatically into every Edge Function's
// environment.

import { createClient } from "jsr:@supabase/supabase-js@2";

interface EventRow {
  id: string;
  title: string;
  description: string | null;
  location: string | null;
  starts_at: string;
  ends_at: string | null;
}

// RFC 5545 basic date-time form (UTC): YYYYMMDDTHHMMSSZ.
function toIcsDate(isoString: string): string {
  return isoString.replace(/[-:]/g, "").split(".")[0] + "Z";
}

// RFC 5545 §3.3.11 TEXT escaping — commas, semicolons and backslashes are structural, and folded
// lines must not contain a raw CRLF.
function escapeText(value: string): string {
  return value
    .replace(/\\/g, "\\\\")
    .replace(/,/g, "\\,")
    .replace(/;/g, "\\;")
    .replace(/\r?\n/g, "\\n");
}

function eventToVevent(event: EventRow): string {
  const dtStart = toIcsDate(event.starts_at);
  // Every event needs SOME end (DTEND or DURATION) — default to a 1-hour block when ends_at
  // wasn't set, since the column is nullable but ICS requires one or the other.
  const dtEnd = toIcsDate(
    event.ends_at ?? new Date(new Date(event.starts_at).getTime() + 60 * 60 * 1000).toISOString(),
  );
  const now = toIcsDate(new Date().toISOString());

  const lines = [
    "BEGIN:VEVENT",
    `UID:event-${event.id}@axisapp.aitorsansal.com`,
    `DTSTAMP:${now}`,
    `DTSTART:${dtStart}`,
    `DTEND:${dtEnd}`,
    `SUMMARY:${escapeText(event.title)}`,
  ];

  if (event.location) {
    lines.push(`LOCATION:${escapeText(event.location)}`);
  }
  if (event.description) {
    lines.push(`DESCRIPTION:${escapeText(event.description)}`);
  }

  // Best-effort only — Google Calendar largely ignores VALARM on subscribed (as opposed to
  // invited) feeds, and Outlook/Apple support is inconsistent across versions. Included because
  // it costs nothing and does work on some clients (notably Apple), not because it's reliable.
  lines.push(
    "BEGIN:VALARM",
    "ACTION:DISPLAY",
    "DESCRIPTION:Reminder",
    "TRIGGER:-P4D",
    "END:VALARM",
  );

  lines.push("END:VEVENT");
  return lines.join("\r\n");
}

Deno.serve(async (req) => {
  if (req.method !== "GET") {
    return new Response("Method not allowed", { status: 405 });
  }

  // Path is /calendar-feed/<token>.ics — strip the function name prefix and extension.
  const path = new URL(req.url).pathname;
  const match = path.match(/([^/]+?)(?:\.ics)?$/);
  const token = match?.[1];

  if (!token) {
    return new Response("Not found", { status: 404 });
  }

  const supabase = createClient(
    Deno.env.get("SUPABASE_URL")!,
    Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
  );

  const { data: subscription, error: subscriptionError } = await supabase
    .from("calendar_subscriptions")
    .select("member_id")
    .eq("token", token)
    .maybeSingle();

  if (subscriptionError || !subscription) {
    return new Response("Not found", { status: 404 });
  }

  // Fire-and-forget — a failure here must never block the feed itself.
  supabase
    .from("calendar_subscriptions")
    .update({ last_accessed_at: new Date().toISOString() })
    .eq("token", token)
    .then(() => {});

  const { data: groupRows, error: groupError } = await supabase
    .from("group_members")
    .select("group_id")
    .eq("member_id", subscription.member_id);

  if (groupError) {
    return new Response("Internal error", { status: 500 });
  }

  const groupIds = (groupRows ?? []).map((row) => row.group_id);
  let events: EventRow[] = [];

  if (groupIds.length > 0) {
    const { data: eventRows, error: eventsError } = await supabase
      .from("events")
      .select("id, title, description, location, starts_at, ends_at")
      .in("group_id", groupIds)
      .eq("is_birthday", false)
      .gt("starts_at", new Date(Date.now() - 7 * 24 * 60 * 60 * 1000).toISOString());

    if (eventsError) {
      return new Response("Internal error", { status: 500 });
    }
    events = eventRows ?? [];
  }

  const ics = [
    "BEGIN:VCALENDAR",
    "VERSION:2.0",
    "PRODID:-//Axis//Calendar Feed//EN",
    "CALSCALE:GREGORIAN",
    "METHOD:PUBLISH",
    ...events.map(eventToVevent),
    "END:VCALENDAR",
  ].join("\r\n");

  return new Response(ics, {
    headers: {
      "Content-Type": "text/calendar; charset=utf-8",
      "Content-Disposition": 'inline; filename="axis.ics"',
    },
  });
});
