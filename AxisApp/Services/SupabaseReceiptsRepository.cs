namespace AxisApp.Services;

/// <summary>Supabase.Storage's CreateSignedUrl shape is confirmed against a real build of the
/// installed Supabase.Storage 2.7.0 package (reflection probe of StorageFileApi, not docs) —
/// (path, expiresIn, TransformOptions?, DownloadOptions?), the last two defaulting, returning
/// Task&lt;string&gt; directly (not a wrapper response type, unlike what a first pass at this probe
/// assumed). Same discipline as the avatars reflection probe, applied here because this bucket
/// needs signed rather than public URLs.</summary>
public class SupabaseReceiptsRepository : IReceiptsRepository
{
    private const string Bucket = "receipts";

    private readonly Supabase.Client client;

    public SupabaseReceiptsRepository(Supabase.Client client)
    {
        this.client = client;
    }

    public async Task<string> UploadAsync(Guid groupId, byte[] webpData, string? previousPath = null)
    {
        var path = $"{groupId}/{Guid.NewGuid()}.webp";

        await client.Storage.From(Bucket).Upload(webpData, path, new Supabase.Storage.FileOptions { ContentType = "image/webp" });

        if (!string.IsNullOrEmpty(previousPath))
            await TryRemoveAsync(previousPath);

        return path;
    }

    public Task RemoveAsync(string path) => TryRemoveAsync(path);

    public async Task<string?> GetSignedUrlAsync(string path, int expiresInSeconds = 3600) =>
        await client.Storage.From(Bucket).CreateSignedUrl(path, expiresInSeconds);

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
