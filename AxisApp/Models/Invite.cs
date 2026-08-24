using Postgrest.Attributes;
using Postgrest.Models;

namespace AxisApp.Models;

/// <summary>
/// A shareable code to join a group. When <see cref="TargetMemberId"/> is set, redeeming the
/// invite links the redeemer's account to that existing phantom member (a "claim") instead of
/// creating a brand-new member — this is how someone with existing payment history against a
/// phantom (e.g. your dad) takes over that identity once they actually sign up.
/// </summary>
[Table("invites")]
public class Invite : BaseModel
{
    [PrimaryKey("id")]
    public Guid Id { get; set; }

    [Column("token")]
    public string Token { get; set; } = "";

    [Column("group_id")]
    public Guid GroupId { get; set; }

    [Column("target_member_id")]
    public Guid? TargetMemberId { get; set; }

    [Column("created_by")]
    public Guid CreatedBy { get; set; }

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("max_uses")]
    public int MaxUses { get; set; } = 1;

    [Column("use_count")]
    public int UseCount { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
