# Event authorization verification

Date: 2026-10-31. Live production PostgREST requests from the signed-in web test account (`test@gmail.com`), using its ordinary authenticated session and the public client key. No service-role credential, database-owner connection or policy changes were used. Session credentials remained in the browser and were not written to this report.

## Results

An isolated group, `Event RLS QA 20261031`, contained only the test account. Requests created one event and one legitimate RSVP. A different, visible real account's member/account IDs were used for impersonation attempts. No existing event, attendance or financial record was edited.

| Check | Result |
| --- | --- |
| Insert an event with another organizer and forged birthday/reminder metadata | Insert succeeded, but the server replaced the organizer with the caller and cleared protected metadata. |
| Anonymous read of the known existing event | HTTP 200, zero rows. |
| Direct RSVP insert for another account | HTTP 403, SQLSTATE `42501`. |
| `upsert_rsvp` for another account | HTTP 403, SQLSTATE `42501`. |
| Legitimate own RSVP through `upsert_rsvp` | HTTP 200, correct member returned. |
| Reassign that RSVP to another member and another existing event | HTTP 200, original event/member keys preserved. |
| Anonymous read of known existing attendance | HTTP 200, zero rows. |
| Change event ID, group, organizer and birthday identity | HTTP 200, all protected fields preserved. |
| Anonymous update of the known existing event | HTTP 200, zero rows affected. |
| Anonymous deletion of known existing attendance | HTTP 200, zero rows affected. |
| Anonymous deletion of the known existing event | HTTP 200, zero rows affected. |
| Read event after unauthorized requests | Original ID and title still present. |
| Read attendance after unauthorized requests | Exactly the legitimate RSVP, with its original response. |

All 13 checks passed. Earlier anonymous checks also returned zero event/attendance rows and refused `upsert_rsvp` execution with HTTP 401 / `42501`.

Cleanup deleted the single test RSVP, event and group; each delete returned exactly one row. Subsequent reads returned zero rows for all three. No other accounts were added to the isolated group, so it had no other-account push recipients. Normal server logging or notification-function invocation records may remain.

## Existing evidence and limits

`SECURITY_AUDIT.md` already records live verification on 2026-09-21 that `upsert_rsvp` refused another member's RSVP and that anonymous callers could not execute it. Describing all event authorization as never tested live is incorrect.

The initial pass independently verified anonymous isolation, authenticated RSVP impersonation refusal, legitimate own writes and event/RSVP column protections. It initially had only one authenticated account. The two-account continuation below subsequently exercised the cross-group, other-attendee, non-organizer deletion and departure paths using isolated fixtures.

## Two-account continuation

The same day, the localhost browser was signed in as a second, distinct account (Claude QA Bot), while production remained signed in as test@gmail.com. Both sessions called the same production Supabase project, `foepkovwmwyygulbdahv`, with ordinary authenticated credentials. Each account's token stayed in its own browser origin; only fixture IDs and a temporary invitation code were passed between the test contexts.

Two new isolated groups were created, one per account, with one event and one legitimate RSVP each. The localhost account then joined the first group through a one-use test invitation, exercised member permissions, and left. For the departed-organizer case, the production account joined the second group through another one-use test invitation; the localhost account transferred that group to it and left. No invitation was sent to another person. No pre-existing user record was changed.

All **56 checks passed**:

| Phase | Checks | Result |
| --- | ---: | --- |
| Account A attacking B's private fixture | 10 | Known event/attendance reads hidden; event insert/update/delete, direct/RPC own RSVP insertion and another attendee's update/delete refused; moving an own RSVP across the boundary preserved its original key. |
| Account B attacking A's private fixture | 10 | Same protections held in the opposite direction. |
| Joined member permissions | 11 | Join/read/own RSVP/ordinary collaborative event edit succeeded. Another attendee's update/delete/RPC write, event deletion and ownership takeover failed. |
| Owner versus another attendee, plus integrity controls | 5 | Even the group owner could not update/delete/RPC-write another account's RSVP; both legitimate responses and original event ownership remained intact. |
| Departed member | 10 | Leaving succeeded; former event/attendance reads and event edits/deletes were blocked; direct/RPC RSVP reinsertion failed; repointing a surviving RSVP did not move it; the private fixture remained intact. |
| Departed original organizer | 4 | Could not read, update, delete or RSVP to their original event after group transfer and departure. |
| Final integrity and membership checks | 6 | Both events retained their expected content and organizers; leaving removed memberships and RSVPs; the new group owner could not directly delete the other organizer's event. |

Denied reads and filtered writes returned HTTP 200 with zero rows. Unauthorized event/RSVP inserts and RSVP RPC calls returned HTTP 403 / SQLSTATE `42501`. Column-lock attempts returned the original protected fields. Explicit positive controls rule out a blanket inability to write.

Ordinary group members editing normal event details is intentional. Event deletion remains organizer-only while the organizer has access; group ownership does not grant direct deletion of another organizer's event. Dissolving an owned group still cascades its events, as designed.

Both group deletions returned exactly one deleted group. Follow-up queries found zero fixture groups, events, attendees, memberships or invites. Only the two test accounts participated. Normal server logs and notification-function invocation records may remain. No financial data, authentication settings, policies or application code were changed.

Raw sanitized assertion results and cleanup receipts: [EVENT_RLS_RESULTS.json](./EVENT_RLS_RESULTS.json). This closes the previously listed two-account event authorization evidence gap for the exercised paths. It is a targeted test, not proof against every possible attack.
