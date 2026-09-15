using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace AxisApp.Models;

/// <summary>What one member owes toward an <see cref="Expense"/>. Composite key (ExpenseId, MemberId).</summary>
[Table("expense_shares")]
public class ExpenseShare : BaseModel
{
    /// <summary>shouldInsert must be true — see GroupMember.GroupId for why false is wrong here
    /// (same composite-key footgun, same class of Insert-time bug).</summary>
    [PrimaryKey("expense_id", shouldInsert: true)]
    public Guid ExpenseId { get; set; }

    [Column("member_id")]
    public Guid MemberId { get; set; }

    [Column("share_amount")]
    public decimal ShareAmount { get; set; }

    /// <summary>Snapshotted server-side alongside Expense.AmountInGroupCurrency — see that
    /// property's remarks. Use this, not ShareAmount, for any cross-expense aggregation (stats),
    /// since ShareAmount is in the expense's own currency and isn't safe to sum across expenses
    /// that may be in different currencies.</summary>
    [Column("share_amount_in_group_currency")]
    public decimal ShareAmountInGroupCurrency { get; set; }
}
