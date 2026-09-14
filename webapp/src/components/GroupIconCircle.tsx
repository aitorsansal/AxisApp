import { colorHex, iconGlyph } from '../lib/groupAppearance'
import './GroupIconCircle.css'

interface Props {
  color: string | null | undefined
  icon: string | null | undefined
  name: string
  size?: number
}

export function GroupIconCircle({ color, icon, name, size = 40 }: Props) {
  const glyph = iconGlyph(icon)
  return (
    <div
      className="group-icon-circle"
      style={{
        width: size,
        height: size,
        fontSize: size * 0.5,
        background: colorHex(color),
      }}
    >
      {glyph ?? (name.slice(0, 1).toUpperCase() || '?')}
    </div>
  )
}
