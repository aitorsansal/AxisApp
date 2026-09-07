using AxisApp.Models;
using Supabase.Postgrest;

namespace AxisApp.Services;

public class SupabaseEventsRepository : IEventsRepository
{
    private readonly Supabase.Client client;
    private readonly IAuthService authService;

    public SupabaseEventsRepository(Supabase.Client client, IAuthService authService)
    {
        this.client = client;
        this.authService = authService;
    }

    public async Task<List<Event>> GetForGroupAsync(Guid groupId)
    {
        var result = await client.From<Event>()
            .Filter("group_id", Constants.Operator.Equals, groupId.ToString())
            .Order("starts_at", Constants.Ordering.Ascending)
            .Get();

        return result.Models;
    }

    public async Task<Event?> GetByIdAsync(Guid eventId) =>
        await client.From<Event>()
            .Filter("id", Constants.Operator.Equals, eventId.ToString())
            .Single();

    public async Task<Event> AddAsync(Event ev)
    {
        ev.CreatedBy = authService.RequireAccountId();
        var inserted = await client.From<Event>().Insert(ev);
        return inserted.Model!;
    }

    public async Task<Event> UpdateAsync(Event ev)
    {
        var updated = await client.From<Event>().Update(ev);
        return updated.Model!;
    }

    public async Task DeleteAsync(Guid eventId) =>
        await client.From<Event>()
            .Filter("id", Constants.Operator.Equals, eventId.ToString())
            .Delete();

    public async Task<List<EventAttendee>> GetAttendeesAsync(Guid eventId)
    {
        var result = await client.From<EventAttendee>()
            .Filter("event_id", Constants.Operator.Equals, eventId.ToString())
            .Get();

        return result.Models;
    }

    /// <summary>Checks for an existing row via an explicit event_id+member_id Filter before
    /// deciding insert vs. update — never trusts Update(model)'s implicit primary-key match, same
    /// footgun SupabaseExpensesRepository.UpdateAsync already documents for the identical
    /// composite-key shape (EventAttendee only marks EventId with [PrimaryKey]).</summary>
    public async Task<EventAttendee> UpsertRsvpAsync(
        Guid eventId, Guid memberId, string response, string carStatus = "none", int? carOfferedSeats = null)
    {
        if (response == "not_going")
        {
            carStatus = "none";
            carOfferedSeats = null;
        }

        var existing = await client.From<EventAttendee>()
            .Filter("event_id", Constants.Operator.Equals, eventId.ToString())
            .Filter("member_id", Constants.Operator.Equals, memberId.ToString())
            .Single();

        if (existing is not null)
        {
            existing.Response = response;
            existing.CarStatus = carStatus;
            existing.CarOfferedSeats = carOfferedSeats;
            existing.UpdatedAt = DateTime.UtcNow;

            var updated = await client.From<EventAttendee>()
                .Filter("event_id", Constants.Operator.Equals, eventId.ToString())
                .Filter("member_id", Constants.Operator.Equals, memberId.ToString())
                .Update(existing);
            return updated.Model!;
        }

        var attendee = new EventAttendee
        {
            EventId = eventId,
            MemberId = memberId,
            Response = response,
            CarStatus = carStatus,
            CarOfferedSeats = carOfferedSeats
        };
        var inserted = await client.From<EventAttendee>().Insert(attendee);
        return inserted.Model!;
    }
}
