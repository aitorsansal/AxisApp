using AxisApp.Models;
using Supabase.Postgrest;

namespace AxisApp.Services;

public class SupabaseEventsRepository : IEventsRepository
{
    private readonly Supabase.Client client;
    private readonly IAuthService authService;
    private readonly IWidgetRefreshService widgetRefresh;

    public SupabaseEventsRepository(Supabase.Client client, IAuthService authService, IWidgetRefreshService widgetRefresh)
    {
        this.client = client;
        this.authService = authService;
        this.widgetRefresh = widgetRefresh;
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
        widgetRefresh.RequestEventsRefresh();
        return inserted.Model!;
    }

    public async Task<Event> UpdateAsync(Event ev)
    {
        var updated = await client.From<Event>().Update(ev);
        widgetRefresh.RequestEventsRefresh();
        return updated.Model!;
    }

    public async Task DeleteAsync(Guid eventId)
    {
        await client.From<Event>()
            .Filter("id", Constants.Operator.Equals, eventId.ToString())
            .Delete();

        widgetRefresh.RequestEventsRefresh();
    }

    public async Task<List<EventAttendee>> GetAttendeesAsync(Guid eventId)
    {
        var result = await client.From<EventAttendee>()
            .Filter("event_id", Constants.Operator.Equals, eventId.ToString())
            .Get();

        return result.Models;
    }

    /// <summary>Single atomic write through the upsert_rsvp() RPC (see supabase/upsert_rsvp.sql). This
    /// used to SELECT the (event, member) row and then INSERT or UPDATE it, so two concurrent RSVPs
    /// from one member could both see "no row" and the second INSERT hit the primary-key violation.
    /// The function runs as the caller, so the RSVP RLS policies apply exactly as before, and it
    /// applies the "not_going clears the car" coupling itself.
    ///
    /// Not a client-side Upsert(model): that would send created_at/updated_at from a model whose
    /// timestamps default to year 1 and overwrite the stored ones on conflict. The RPC returns the
    /// stored row as JSON, parsed by hand rather than through the Postgrest serializer.</summary>
    public async Task<EventAttendee> UpsertRsvpAsync(
        Guid eventId, Guid memberId, string response, string carStatus = "none", int? carOfferedSeats = null)
    {
        var result = await client.Rpc("upsert_rsvp", new Dictionary<string, object?>
        {
            ["p_event_id"] = eventId.ToString(),
            ["p_member_id"] = memberId.ToString(),
            ["p_response"] = response,
            ["p_car_status"] = carStatus,
            ["p_car_offered_seats"] = carOfferedSeats
        });

        using var doc = System.Text.Json.JsonDocument.Parse(
            result.Content ?? throw new InvalidOperationException("upsert_rsvp returned no row."));
        var row = doc.RootElement;
        if (row.ValueKind == System.Text.Json.JsonValueKind.Array) row = row[0];

        var seats = row.GetProperty("car_offered_seats");
        return new EventAttendee
        {
            EventId = row.GetProperty("event_id").GetGuid(),
            MemberId = row.GetProperty("member_id").GetGuid(),
            Response = row.GetProperty("response").GetString()!,
            CarStatus = row.GetProperty("car_status").GetString()!,
            CarOfferedSeats = seats.ValueKind == System.Text.Json.JsonValueKind.Null ? null : seats.GetInt32(),
            CreatedAt = row.GetProperty("created_at").GetDateTimeOffset().UtcDateTime,
            UpdatedAt = row.GetProperty("updated_at").GetDateTimeOffset().UtcDateTime
        };
    }
}
