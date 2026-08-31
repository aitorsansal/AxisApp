using AxisApp.Models;

namespace AxisApp.Services;

/// <summary>Single place resolving how a member should actually be shown — folding in the
/// viewer's private alias overrides (member_aliases) and, once avatar upload exists, their
/// picture. Every screen that used to read Member.DisplayName directly, or compute initials from
/// it inline, should go through here instead, so alias/avatar support doesn't need touching each
/// screen twice.</summary>
public static class MemberDisplay
{
    public static string Name(Member member, IReadOnlyDictionary<Guid, string> aliases) =>
        aliases.TryGetValue(member.Id, out var alias) && !string.IsNullOrWhiteSpace(alias)
            ? alias
            : member.DisplayName;

    public static string Initials(Member member, IReadOnlyDictionary<Guid, string> aliases)
    {
        var parts = Name(member, aliases).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "?" : string.Concat(parts.Take(2).Select(w => char.ToUpperInvariant(w[0])));
    }

    /// <summary>Phantoms never have an avatar (see schema.sql's "Avatar photos" remarks — enforced
    /// at the database level too, not just here). For a claimed member with one set, the URL is a
    /// plain deterministic string: the `avatars` bucket is public, so this needs no live
    /// Supabase.Client/Storage call the way a private bucket's signed URL would — same reasoning
    /// AppConstants.Links.BuildInviteUrl already uses for a different fixed-host URL.</summary>
    public static string? AvatarUrl(Member member) =>
        member.IsPhantom || member.AvatarPath is null
            ? null
            : $"{AxisApp.SupabaseConfig.Url}/storage/v1/object/public/avatars/{member.AvatarPath}";
}
