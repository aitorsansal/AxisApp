import { iconGlyphs, type IconName } from '../lib/icons'

export function Icon({ name, size = 20 }: { name: IconName; size?: number }) {
  return (
    <span className="icon" aria-hidden="true" style={{ fontSize: size }}>
      {iconGlyphs[name]}
    </span>
  )
}
