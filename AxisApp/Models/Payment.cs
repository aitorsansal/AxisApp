using Postgrest.Attributes;
using Postgrest.Models;

namespace AxisApp.Models;

[Table("payments")]
public class Payment : BaseModel
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
