// Browser-native equivalent of AxisApp/Services/ImageResizer.cs — canvas +
// createImageBitmap instead of SkiaSharp, no npm dependency. Same 256px
// target as the MAUI avatar path (the largest an avatar ever renders is
// AvatarSizeL, 44px — see Tokens.xaml — so 256px already has headroom).
export async function resizeImageToWebp(file: File, maxDimension = 256, quality = 0.85): Promise<Blob> {
  const bitmap = await createImageBitmap(file)
  try {
    const scale = Math.min(1, maxDimension / Math.max(bitmap.width, bitmap.height))
    const width = Math.max(1, Math.round(bitmap.width * scale))
    const height = Math.max(1, Math.round(bitmap.height * scale))

    const canvas = document.createElement('canvas')
    canvas.width = width
    canvas.height = height
    const ctx = canvas.getContext('2d')
    if (!ctx) throw new Error('Canvas 2D context unavailable')
    ctx.drawImage(bitmap, 0, 0, width, height)

    return await new Promise<Blob>((resolve, reject) => {
      canvas.toBlob(
        (blob) => (blob ? resolve(blob) : reject(new Error('WebP encoding failed'))),
        'image/webp',
        quality,
      )
    })
  } finally {
    bitmap.close()
  }
}
