using Newtonsoft.Json;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace AxisApp.Models;

/// <summary>
/// A ledger participant. When <see cref="AccountId"/> is null, this is a "phantom" member —
/// added by name only, not yet linked to a real login (e.g. a relative who hasn't installed
/// the app). Payments always reference members, never auth accounts directly, so the ledger
/// logic doesn't care whether a member is phantom or claimed.
/// </summary>
[Table("members")]
public class Member : BaseModel
{
    [PrimaryKey("id")]
    public Guid Id { get; set; }

    [Column("account_id")]
    public Guid? AccountId { get; set; }

    [Column("display_name")]
    public string DisplayName { get; set; } = "";

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>Reserved, not yet populated by anything — see schema.sql's "Member aliases +
    /// reserved avatar column" remarks. Services/MemberDisplay.cs.AvatarUrl always resolves to
    /// null until Storage upload exists.</summary>
    [Column("avatar_path")]
    public string? AvatarPath { get; set; }

    /// <summary>Reserved for future birthday-related features — see schema.sql's "Profile page"
    /// remarks. Self-only: only the claimed account this row belongs to can set it.</summary>
    [Column("birth_date")]
    public DateTime? BirthDate { get; set; }

    /// <summary>Per-profile default seat count for the transport/carpooling feature — always
    /// means "extra seats beyond the driver", never a total including the driver (see
    /// /EVENTS_PLAN.md's "Decisions locked" on the naming footgun the original idea's phrasing
    /// had). A per-event offer (EventAttendee.CarOfferedSeats) defaults from this but can be
    /// edited independently for a specific event.</summary>
    [Column("car_extra_seats")]
    public int? CarExtraSeats { get; set; }

    [JsonIgnore]
    public bool IsPhantom => AccountId is null;
}
