using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace AxisApp.Models;

/// <summary>
/// A bill one member fronted, split across participants via <see cref="ExpenseShare"/> rows.
/// A settle-up ("I paid you back $20") is just an Expense with <see cref="IsSettlement"/> true
/// and exactly one share — Payment was retired 2026-09-04 once the balance math was confirmed
/// identical (see CLAUDE.md's "Merge payments into expenses" remarks).
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

    /// <summary>Snapshotted by snapshot_expense_currency_conversion() (schema.sql) at write time —
    /// the trigger overwrites both fields unconditionally on every insert/update, so whatever the
    /// app sends here is ignored server-side, same "app stays ignorant of currency math" treatment
    /// MULTI_CURRENCY_PLAN.md's Milestone 4 describes.</summary>
    [Column("amount_in_group_currency")]
    public decimal AmountInGroupCurrency { get; set; }

    [Column("exchange_rate")]
    public decimal ExchangeRate { get; set; } = 1;

    [Column("description")]
    public string Description { get; set; } = "";

    [Column("category")]
    public string Category { get; set; } = "";

    [Column("occurred_at")]
    public DateTime OccurredAt { get; set; }

    [Column("receipt_path")]
    public string? ReceiptPath { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("is_settlement")]
    public bool IsSettlement { get; set; }

    /// <summary>Optional link back to the Event this expense was booked from — see
    /// event_expenses.sql. The participant set is snapshotted at add/edit time from that event's
    /// "going" attendees, not re-derived from this on every read.</summary>
    [Column("event_id")]
    public Guid? EventId { get; set; }
}
