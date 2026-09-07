using AxisApp.Models;

namespace AxisApp.Services;

/// <summary>Group events (see /EVENTS_PLAN.md's Phase 2) — CRUD on events, RSVP read/write on
/// event_attendees.</summary>
public interface IEventsRepository
{
    Task<List<Event>> GetForGroupAsync(Guid groupId);
    Task<Event?> GetByIdAsync(Guid eventId);
    Task<Event> AddAsync(Event ev);
    Task<Event> UpdateAsync(Event ev);
    Task DeleteAsync(Guid eventId);

    Task<List<EventAttendee>> GetAttendeesAsync(Guid eventId);

    /// <summary>Insert-if-absent/update-if-present RSVP write for the given event+member — the one
    /// place enforcing the response/car_status coupling documented in schema.sql and
    /// /EVENTS_PLAN.md's "Decisions locked": whenever <paramref name="response"/> is "not_going",
    /// <paramref name="carStatus"/>/<paramref name="carOfferedSeats"/> are forced to "none"/null in
    /// the same write regardless of what the caller passed, so a declined attendee can never linger
    /// in the transport shortfall aggregate Milestone 4 builds. carStatus/carOfferedSeats aren't
    /// exercised by any UI until then, but this rule needs exactly one implementation rather than
    /// being redone when Milestone 4 lands.</summary>
    Task<EventAttendee> UpsertRsvpAsync(
        Guid eventId, Guid memberId, string response, string carStatus = "none", int? carOfferedSeats = null);
}
