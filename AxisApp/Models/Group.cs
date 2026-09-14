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

    /// <summary>An AccentPreset name (see Services/AccentPalettes.cs), not a hex value — same
    /// convention as the per-device accent preference. Member-editable (unlike Name/Currency
    /// above): see schema.sql's enforce_group_owner_only_columns() trigger for how name/currency
    /// stay creator-only despite the table's UPDATE policy now covering any member.</summary>
    [Column("color")]
    public string Color { get; set; } = "Blue";

    /// <summary>A key into AppConstants.GroupIcons, or null (falls back to an initials circle —
    /// see GroupIconCircle). Member-editable, same as Color.</summary>
    [Column("icon")]
    public string? Icon { get; set; }
}
