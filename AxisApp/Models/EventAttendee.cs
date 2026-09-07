using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace AxisApp.Models;

/// <summary>
/// One member's RSVP to an <see cref="Event"/>. <see cref="Response"/> is a 3-state
/// (going/maybe/not_going), not a plain yes/no — it drives both the transport headcount and
/// (Milestone 5) the reminder recipient list. <see cref="CarStatus"/> is one tri-state field
/// (none/offering/needs_ride) rather than two booleans, so "offering a ride" and "needs a ride"
/// can never both be true at once — see /EVENTS_PLAN.md's "Decisions locked". Composite key
/// (EventId, MemberId).
/// </summary>
[Table("event_attendees")]
public class EventAttendee : BaseModel
{
    /// <summary>shouldInsert must be true — see ExpenseShare.ExpenseId for why false is wrong here
    /// (same composite-key footgun, same class of Insert-time bug).</summary>
    [PrimaryKey("event_id", shouldInsert: true)]
    public Guid EventId { get; set; }

    [Column("member_id")]
    public Guid MemberId { get; set; }

    [Column("response")]
    public string Response { get; set; } = "going";

    [Column("car_status")]
    public string CarStatus { get; set; } = "none";

    /// <summary>Meaningful only when CarStatus is "offering" — will default from a per-profile
    /// seat count (Milestone 4, not yet on the Member model) but is editable per event.</summary>
    [Column("car_offered_seats")]
    public int? CarOfferedSeats { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
