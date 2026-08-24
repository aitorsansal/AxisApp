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
    public Guid CreatedBy { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    public bool IsPhantom => AccountId is null;
}
