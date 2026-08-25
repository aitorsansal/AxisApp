using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace AxisApp.Models;

/// <summary>
/// One row of the read-only <c>my_group_balances</c> view: the current account's own net balance
/// in one group. Positive means the group owes them; negative means they owe the group. Backed
/// by a view, not a table — no primary key, only ever queried.
/// </summary>
[Table("my_group_balances")]
public class MyGroupBalance : BaseModel
{
    [Column("group_id")]
    public Guid GroupId { get; set; }

    [Column("balance")]
    public decimal Balance { get; set; }
}
