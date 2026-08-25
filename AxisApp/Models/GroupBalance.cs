using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace AxisApp.Models;

/// <summary>
/// One row of the read-only <c>group_balances</c> view: a member's net balance within a group,
/// combining direct payments and N-way expense shares. Positive means the group owes them;
/// negative means they owe the group. Backed by a view, not a table — no primary key, and only
/// ever queried (Get/Where), never inserted/updated/deleted.
/// </summary>
[Table("group_balances")]
public class GroupBalance : BaseModel
{
    [Column("group_id")]
    public Guid GroupId { get; set; }

    [Column("member_id")]
    public Guid MemberId { get; set; }

    [Column("balance")]
    public decimal Balance { get; set; }
}
