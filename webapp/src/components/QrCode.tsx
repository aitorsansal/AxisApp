import { useMemo } from 'react'
import qrcode from 'qrcode-generator'

const MARGIN = 4 // quiet zone, in modules; scanners need it even on a dark page

// Dark-on-light like the Android invite card (InviteToGroupViewModel.BuildQrImageSource): a
// light quiet zone is what lets phone cameras read it on the dark theme.
export function QrCode({ value, label, size = 144 }: { value: string; label: string; size?: number }) {
  const { path, extent } = useMemo(() => {
    const qr = qrcode(0, 'Q')
    qr.addData(value)
    qr.make()
    const count = qr.getModuleCount()
    let d = ''
    for (let row = 0; row < count; row++)
      for (let col = 0; col < count; col++)
        if (qr.isDark(row, col)) d += `M${col + MARGIN} ${row + MARGIN}h1v1h-1z`
    return { path: d, extent: count + MARGIN * 2 }
  }, [value])

  return (
    <svg
      viewBox={`0 0 ${extent} ${extent}`}
      width={size}
      height={size}
      role="img"
      aria-label={label}
      shapeRendering="crispEdges"
    >
      <rect width={extent} height={extent} fill="#F5F5F7" />
      <path d={path} fill="#0B1220" />
    </svg>
  )
}
