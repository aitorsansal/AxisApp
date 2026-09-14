import { GROUP_COLORS, GROUP_ICONS } from '../lib/groupAppearance'
import './GroupAppearancePicker.css'

interface Props {
  color: string
  icon: string | null
  onColorChange: (color: string) => void
  onIconChange: (icon: string | null) => void
}

export function GroupAppearancePicker({ color, icon, onColorChange, onIconChange }: Props) {
  return (
    <div className="appearance-picker">
      <div className="appearance-swatches">
        {GROUP_COLORS.map((c) => (
          <button
            key={c.key}
            type="button"
            className={`appearance-swatch${color === c.key ? ' selected' : ''}`}
            style={{ background: c.hex }}
            aria-label={c.key}
            onClick={() => onColorChange(c.key)}
          />
        ))}
      </div>
      <div className="appearance-icons">
        <button
          type="button"
          className={`appearance-icon${icon === null ? ' selected' : ''}`}
          onClick={() => onIconChange(null)}
        >
          ∅
        </button>
        {GROUP_ICONS.map((i) => (
          <button
            key={i.key}
            type="button"
            className={`appearance-icon${icon === i.key ? ' selected' : ''}`}
            onClick={() => onIconChange(i.key)}
          >
            {i.glyph}
          </button>
        ))}
      </div>
    </div>
  )
}
