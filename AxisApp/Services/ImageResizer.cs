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
}
