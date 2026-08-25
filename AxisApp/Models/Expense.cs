using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace AxisApp.Models;

/// <summary>
/// A bill one member fronted, split across participants via <see cref="ExpenseShare"/> rows.
/// Distinct from <see cref="Payment"/>, which is a direct pairwise settle-up with no splitting
/// concept — see SCOPE.md for why these stay as two separate transaction shapes.
/// </summary>
[Table("expenses")]
public class Expense : BaseModel
{
    [PrimaryKey("id")]
    public Guid Id { get; set; }

    [Column("group_id")]
    public Guid? GroupId { get; set; }

    [Column("paid_by_member_id")]
    public Guid PaidByMemberId { get; set; }

    [Column("amount")]
    public decimal Amount { get; set; }

    [Column("currency")]
    public string Currency { get; set; } = "EUR";

    [Column("description")]
    public string Description { get; set; } = "";

    [Column("category")]
    public string Category { get; set; } = "";

    [Column("occurred_at")]
    public DateTime OccurredAt { get; set; }

    [Column("receipt_path")]
    public string? ReceiptPath { get; set; }

    [Column("created_by")]
    public Guid CreatedBy { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
