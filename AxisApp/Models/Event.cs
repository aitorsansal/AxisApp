using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace AxisApp.Models;

/// <summary>
/// A group event (see /EVENTS_PLAN.md's Phase 2) — title/description/location/StartsAt/EndsAt,
/// plus <see cref="NeedsTransport"/> (editable after creation, unlike Group.Currency's deliberate
/// lock — an organizer may not know transport will be an issue until people start RSVPing) and
/// <see cref="ReminderSentAt"/> (a mark-processed column for the reminder cron, still unbuilt as
/// of Milestone 3a — see Milestone 5). GroupId is not nullable, unlike Expense's — events cascade-
/// delete with their group rather than surviving unscoped, per schema.sql.
/// </summary>
[Table("events")]
public class Event : BaseModel
{
    [PrimaryKey("id")]
    public Guid Id { get; set; }

    [Column("group_id")]
    public Guid GroupId { get; set; }

    [Column("title")]
    public string Title { get; set; } = "";

    [Column("description")]
    public string? Description { get; set; }

    [Column("location")]
    public string? Location { get; set; }

    [Column("starts_at")]
    public DateTime StartsAt { get; set; }

    [Column("ends_at")]
    public DateTime? EndsAt { get; set; }

    [Column("needs_transport")]
    public bool NeedsTransport { get; set; }

    [Column("reminder_sent_at")]
    public DateTime? ReminderSentAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
