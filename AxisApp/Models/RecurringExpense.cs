using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace AxisApp.Models;

/// <summary>
/// A template for a periodically auto-generated <see cref="Expense"/>, split N ways via
/// <see cref="RecurringExpenseShare"/>. Mirrors Expense's shape plus the schedule columns
/// (Frequency/StartDate/LastProcessedDate/IsActive) recurring_payments proved out before being
/// retired — see schema.sql's "recurring_expenses" remarks. Editing a template only affects
/// expenses materialized after the edit; past materialized rows are independent snapshots.
/// </summary>
[Table("recurring_expenses")]
public class RecurringExpense : BaseModel
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

    [Column("frequency")]
    public string Frequency { get; set; } = "monthly";

    [Column("start_date")]
    public DateTime StartDate { get; set; }

    [Column("last_processed_date")]
    public DateTime? LastProcessedDate { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_by")]
    public Guid CreatedBy { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
