using AxisApp.Models;
using Supabase.Postgrest;

namespace AxisApp.Services;

/// <summary>Supabase.Storage's Upload/GetPublicUrl/Remove shape is confirmed against a real build
/// of the installed Supabase 1.6.0 / Supabase.Storage 2.7.0 package (reflection probe, not docs) —
/// see schema.sql's "Avatar photos" remarks.</summary>
public class SupabaseAvatarsRepository : IAvatarsRepository
{
    private const string Bucket = "avatars";

    private readonly Supabase.Client client;

    public SupabaseAvatarsRepository(Supabase.Client client)
    {
        this.client = client;
    }

    public async Task<Member> SetAvatarAsync(Member member, byte[] webpData)
    {
        var previousPath = member.AvatarPath;
        var newPath = $"{member.Id}/{Guid.NewGuid()}.webp";

        await client.Storage.From(Bucket).Upload(webpData, newPath, new Supabase.Storage.FileOptions { ContentType = "image/webp" });

        member.AvatarPath = newPath;
        var result = await client.From<Member>().Update(member);

        await TryRemoveAsync(previousPath);

        return result.Model ?? member;
    }

    /// <summary>Relies on Update(member) actually sending an explicit `"avatar_path": null` for a
    /// null AvatarPath rather than omitting the property — Newtonsoft's default NullValueHandling
    /// is Include, and nothing in this codebase's existing Postgrest calls suggests the library
    /// overrides that, but this specific case (nulling out a previously-set nullable column) isn't
    /// independently confirmed the way Storage's API shape above is. If a removed avatar doesn't
    /// actually clear in the database, this is the first place to check.</summary>
    public async Task<Member> RemoveAvatarAsync(Member member)
    {
        var previousPath = member.AvatarPath;
        member.AvatarPath = null;

        var result = await client.From<Member>().Update(member);

        await TryRemoveAsync(previousPath);

        return result.Model ?? member;
    }

    private async Task TryRemoveAsync(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            await client.Storage.From(Bucket).Remove(path);
        }
        catch
        {
            // Best-effort cleanup — a leftover file is a harmless orphan, not a correctness bug.
        }
    }
}
