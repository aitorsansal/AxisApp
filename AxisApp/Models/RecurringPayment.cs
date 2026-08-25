using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace AxisApp.Models;

[Table("recurring_payments")]
public class RecurringPayment : BaseModel
{
    [PrimaryKey("id")]
    public Guid Id { get; set; }

    [Column("group_id")]
    public Guid? GroupId { get; set; }

    [Column("payer_member_id")]
    public Guid PayerMemberId { get; set; }

    [Column("payee_member_id")]
    public Guid PayeeMemberId { get; set; }

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
