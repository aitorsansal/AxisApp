using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace AxisApp.Models;

[Table("groups")]
public class Group : BaseModel
{
    [PrimaryKey("id")]
    public Guid Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = "";

    [Column("created_by")]
    public Guid CreatedBy { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>Picked once at creation via create_group()'s p_currency, never editable
    /// afterward — see /MULTI_CURRENCY_PLAN.md's "Decisions locked" section.</summary>
    [Column("currency")]
    public string Currency { get; set; } = "EUR";
}
