using SkiaSharp;

namespace AxisApp.Services;

/// <summary>Resize-then-WebP-encode for avatar uploads — confirmed against a real build of the
/// installed SkiaSharp 4.151.1 package (a resize+encode round-trip actually ran), not assumed
/// from docs. AspectFill on ProfileCircle's Image already crops to a circle regardless of source
/// aspect ratio, so this only caps the long edge rather than forcing a square crop.</summary>
public static class ImageResizer
{
    /// <summary>maxDimension defaults to 256 — the largest an avatar ever actually renders in this
    /// app is AvatarSizeL (44px, see Tokens.xaml), so even at 3x display density 256px is already
    /// ~2x more resolution than needed. Dropped from an initial 512 once real uploads (7-18KB)
    /// showed there was no reason to keep that much headroom for something rendered this small and
    /// shown this often (every balance/activity/member row, unlike a receipt opened rarely).</summary>
    public static byte[] ToAvatarWebp(byte[] original, int maxDimension = 256, int quality = 80)
    {
        using var bitmap = SKBitmap.Decode(original)
            ?? throw new InvalidOperationException("Could not decode the selected image.");

        var scale = Math.Min(1.0, maxDimension / (double)Math.Max(bitmap.Width, bitmap.Height));
        var targetWidth = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

        using var resized = bitmap.Resize(new SKImageInfo(targetWidth, targetHeight), SKSamplingOptions.Default)
            ?? throw new InvalidOperationException("Could not resize the selected image.");

        using var image = SKImage.FromBitmap(resized);
        using var encoded = image.Encode(SKEncodedImageFormat.Webp, quality);
        return encoded.ToArray();
    }

    /// <summary>Targets SCOPE.md's ~100KB per receipt: starts at maxDimension and steps quality down
    /// through a fixed list before shrinking the dimension and retrying, down to a 480px floor. A
    /// receipt is viewed full-screen occasionally rather than as a 44px circle everywhere the way an
    /// avatar is, so it keeps real resolution (1280px) instead of avatars' aggressively small 256px.</summary>
    public static byte[] ToReceiptWebp(byte[] original, int maxDimension = 1280, long targetBytes = 100_000)
    {
        using var bitmap = SKBitmap.Decode(original)
            ?? throw new InvalidOperationException("Could not decode the selected image.");

        var dimension = maxDimension;
        byte[] encoded;
        do
        {
            var scale = Math.Min(1.0, dimension / (double)Math.Max(bitmap.Width, bitmap.Height));
            var targetWidth = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
            var targetHeight = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

            using var resized = bitmap.Resize(new SKImageInfo(targetWidth, targetHeight), SKSamplingOptions.Default)
                ?? throw new InvalidOperationException("Could not resize the selected image.");
            using var image = SKImage.FromBitmap(resized);

            encoded = EncodeSteppingQuality(image, targetBytes);
            dimension = dimension * 2 / 3;
        } while (encoded.Length > targetBytes && dimension >= 480);

        return encoded;
    }

    private static byte[] EncodeSteppingQuality(SKImage image, long targetBytes)
    {
        var best = image.Encode(SKEncodedImageFormat.Webp, 80).ToArray();
        foreach (var quality in new[] { 65, 50, 35 })
        {
            if (best.Length <= targetBytes) break;
            best = image.Encode(SKEncodedImageFormat.Webp, quality).ToArray();
        }
        return best;
    }
}
