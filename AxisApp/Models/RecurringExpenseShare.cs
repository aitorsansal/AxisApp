using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace AxisApp.Models;

[Table("recurring_expense_shares")]
public class RecurringExpenseShare : BaseModel
{
    /// <summary>shouldInsert: true because this composite-PK column (paired with MemberId) has no
    /// DB default — Postgrest defaults a [PrimaryKey] column to shouldInsert: false since PKs are
    /// normally auto-generated, which would otherwise silently drop it from the insert payload.
    /// Same footgun already documented on ExpenseShare.ExpenseId/GroupMember.GroupId/
    /// MemberAlias.OwnerId.</summary>
    [PrimaryKey("recurring_expense_id", shouldInsert: true)]
    public Guid RecurringExpenseId { get; set; }

    [Column("member_id")]
    public Guid MemberId { get; set; }

    [Column("share_amount")]
    public decimal ShareAmount { get; set; }
}
