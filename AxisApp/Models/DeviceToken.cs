using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace AxisApp.Models;

/// <summary>A push token (e.g. a OneSignal player id) registered for the current account's device.</summary>
[Table("device_tokens")]
public class DeviceToken : BaseModel
{
    [PrimaryKey("id")]
    public Guid Id { get; set; }

    [Column("account_id")]
    public Guid AccountId { get; set; }

    [Column("push_token")]
    public string PushToken { get; set; } = "";

    [Column("platform")]
    public string Platform { get; set; } = "";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
