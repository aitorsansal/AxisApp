using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace AxisApp.Models;

/// <summary>Join row: which members belong to which groups. Composite key (GroupId, MemberId).</summary>
[Table("group_members")]
public class GroupMember : BaseModel
{
    [PrimaryKey("group_id", false)]
    public Guid GroupId { get; set; }

    [Column("member_id")]
    public Guid MemberId { get; set; }

    [Column("added_at")]
    public DateTime AddedAt { get; set; }
}
