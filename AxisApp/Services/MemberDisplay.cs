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

    /// <summary>Always null for now — members.avatar_path is reserved but nothing populates or
    /// resolves it to a real URL yet. See schema.sql's "Member aliases + reserved avatar column"
    /// remarks. ProfileCircle already renders this correctly (falls back to Initials) so nothing
    /// else needs to change once Storage upload exists — just this one method.</summary>
    public static string? AvatarUrl(Member member) => null;
}
