using Postgrest.Attributes;
using Postgrest.Models;

namespace AxisApp.Models;

/// <summary>What one member owes toward an <see cref="Expense"/>. Composite key (ExpenseId, MemberId).</summary>
[Table("expense_shares")]
public class ExpenseShare : BaseModel
{
    [PrimaryKey("expense_id", false)]
    public Guid ExpenseId { get; set; }

    [Column("member_id")]
    public Guid MemberId { get; set; }

    [Column("share_amount")]
    public decimal ShareAmount { get; set; }
}
