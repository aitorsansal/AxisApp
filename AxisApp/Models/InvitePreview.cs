using System.Text.Json;

namespace AxisApp.Models;

/// <summary>What supabase/invite_preview.sql's preview_invite() returns: a read-only look at an
/// invite before the person presses Join. Group fields are only present for Ok/AlreadyMember;
/// every dead status carries the status alone. Twin of webapp/src/lib/invitePreview.ts. Plain
/// data and parsing only, so the regression test project can link this file.</summary>
public sealed record InvitePreview(
    string Status,
    string? GroupName = null,
    string? GroupCurrency = null,
    int MemberCount = 0,
    string? TargetName = null)
{
    public const string Ok = "ok";
    public const string AlreadyMember = "already_member";

    private static readonly string[] KnownStatuses =
        [Ok, AlreadyMember, "invalid", "expired", "used", "inviter_gone", "claimed", "shared_group"];

    public bool IsJoinable => Status is Ok or AlreadyMember;

    /// <summary>Localization key for a dead status's message (JoinGroup_Err_{status}).</summary>
    public static string ErrorKey(string status) => $"JoinGroup_Err_{status}";

    /// <summary>Null when the payload is not a recognised preview, so a newer server status or a
    /// malformed body degrades to "no card" instead of an error.</summary>
    public static InvitePreview? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("status", out var statusElement)
                || statusElement.ValueKind != JsonValueKind.String)
                return null;
            var status = statusElement.GetString()!;
            if (!KnownStatuses.Contains(status)) return null;

            string? Text(string name) =>
                root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

            var count = root.TryGetProperty("member_count", out var c) && c.TryGetInt32(out var n) ? n : 0;
            return new InvitePreview(status, Text("group_name"), Text("group_currency"), count, Text("target_name"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>redeem_invite() reports the same conditions as free text; used when the preview
    /// was skipped or raced (another person redeemed the invite between preview and Join).</summary>
    public static string? StatusFromRedeemMessage(string message) => message switch
    {
        _ when message.Contains("Invalid invite code", StringComparison.Ordinal) => "invalid",
        _ when message.Contains("Invite expired", StringComparison.Ordinal) => "expired",
        _ when message.Contains("Invite already used", StringComparison.Ordinal) => "used",
        _ when message.Contains("no longer valid", StringComparison.Ordinal) => "inviter_gone",
        _ when message.Contains("already been claimed", StringComparison.Ordinal) => "claimed",
        _ when message.Contains("already in a group with this person", StringComparison.Ordinal) => "shared_group",
        _ => null
    };
}
