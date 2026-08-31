using AxisApp.Models;

namespace AxisApp.Services;

/// <summary>Manages the `avatars` Storage bucket. Both methods take the full Member (not just an
/// id) so the Update call sends real values for every column, not defaults — Postgrest's
/// .Update(model) sends the whole model, so a partial object here would clobber DisplayName/
/// CreatedBy/etc. with blanks, same footgun class already documented elsewhere in this codebase.
/// RLS restricts both to a claimed member's own account — see schema.sql's "Avatar photos" remarks
/// for why phantoms get no avatar support at all.</summary>
public interface IAvatarsRepository
{
    /// <summary>Uploads webpData under a new, never-reused path, points the member at it, then
    /// best-effort deletes whatever the member's previous avatar was (harmless orphan if that
    /// delete fails — same "cleanup isn't critical" class SCOPE.md already put receipts in).</summary>
    Task<Member> SetAvatarAsync(Member member, byte[] webpData);

    /// <summary>Clears the member back to initials-only and deletes the stored file.</summary>
    Task<Member> RemoveAvatarAsync(Member member);
}
