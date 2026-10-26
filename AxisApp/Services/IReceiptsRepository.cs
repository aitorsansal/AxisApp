namespace AxisApp.Services;

/// <summary>Manages the private `receipts` Storage bucket. Unlike avatars' public bucket, viewing
/// needs a live signed URL rather than a deterministic string (see GetSignedUrlAsync) — a receipt is
/// a financial document, not self-presentation. Paths are scoped by group, not by the specific
/// expense a receipt attaches to, so a photo can be captured/uploaded before that expense has even
/// been saved — see schema.sql's "Receipts" remarks.</summary>
public interface IReceiptsRepository
{
    /// <summary>Uploads webpData under a new, never-reused path scoped to groupId, returning the
    /// storage path to set on Expense.ReceiptPath. Unreferenced files are removed by the scheduled
    /// orphan cleanup so cancelled edits and concurrent saves cannot destroy saved receipts.</summary>
    Task<string> UploadAsync(Guid groupId, byte[] webpData);

    /// <summary>Best-effort delete; a leftover file is a harmless orphan.</summary>
    Task RemoveAsync(string path);

    /// <summary>Signed URL valid for expiresInSeconds — the bucket is private, so display always
    /// needs a fresh one rather than a cached/stored URL.</summary>
    Task<string?> GetSignedUrlAsync(string path, int expiresInSeconds = 3600);
}
