using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace AxisApp.Models;

/// <summary>Join row: which members belong to which groups. Composite key (GroupId, MemberId).</summary>
[Table("group_members")]
public class GroupMember : BaseModel
{
    /// <summary>shouldInsert must be true — group_id is a required FK with no default, unlike an
    /// auto-generated PK. false silently drops it from every insert payload, which fails NOT NULL
    /// / RLS with the generic "new row violates row-level security policy" (42501), not an
    /// obviously-missing-column error.</summary>
    [PrimaryKey("group_id", shouldInsert: true)]
    public Guid GroupId { get; set; }

    [Column("member_id")]
    public Guid MemberId { get; set; }

    [Column("added_at")]
    public DateTime AddedAt { get; set; }
}
