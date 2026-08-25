using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace AxisApp.Models;

/// <summary>
/// One row of the read-only <c>my_pairwise_balances</c> view: the current account's real,
/// two-party net balance with one other member of a group — positive means that member owes
/// the current account, negative means the current account owes them. Unlike
/// <see cref="GroupBalance"/> (each member's net position against the group's whole shared
/// pot), this is a genuine pairwise debt derived from the specific expenses/payments the two
/// of them actually share, so "owes you"/"you owe" phrasing is always literally true here.
/// Backed by a view, not a table — no primary key, only ever queried.
/// </summary>
[Table("my_pairwise_balances")]
public class MyPairwiseBalance : BaseModel
{
    [Column("group_id")]
    public Guid GroupId { get; set; }

    [Column("other_member_id")]
    public Guid OtherMemberId { get; set; }

    [Column("balance")]
    public decimal Balance { get; set; }
}
