using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace AxisApp.Models;

/// <summary>
/// One account's read-only .ics calendar feed link — see schema.sql's "Calendar subscription
/// feed" remarks. One row per member (a claimed account has exactly one), covering every group
/// that member belongs to. Token is the actual credential the public calendar-feed Edge Function
/// checks; there is no separate revoke flag — regenerating overwrites Token in place, and the
/// previous link simply stops resolving.
/// </summary>
[Table("calendar_subscriptions")]
public class CalendarSubscription : BaseModel
{
    [PrimaryKey("id")]
    public Guid Id { get; set; }

    [Column("member_id")]
    public Guid MemberId { get; set; }

    [Column("token")]
    public string Token { get; set; } = "";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("last_accessed_at")]
    public DateTime? LastAccessedAt { get; set; }
}
