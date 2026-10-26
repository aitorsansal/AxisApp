import { GROUP_COLORS, GROUP_ICONS } from '../lib/groupAppearance'
import { useLocale } from '../context/LocaleContext'
import './GroupAppearancePicker.css'

interface Props {
  color: string
  icon: string | null
  onColorChange: (color: string) => void
  onIconChange: (icon: string | null) => void
}

export function GroupAppearancePicker({ color, icon, onColorChange, onIconChange }: Props) {
  const { t } = useLocale()
  return (
    <div className="appearance-picker">
      <div className="appearance-swatches">
        {GROUP_COLORS.map((c) => (
          <button
            key={c.key}
            type="button"
            className={`appearance-swatch${color === c.key ? ' selected' : ''}`}
            style={{ background: c.hex }}
            aria-label={t(`GroupColor_${c.key}`)}
            aria-pressed={color === c.key}
            onClick={() => onColorChange(c.key)}
          />
        ))}
      </div>
      <div className="appearance-icons">
        <button
          type="button"
          className={`appearance-icon${icon === null ? ' selected' : ''}`}
          aria-label={t('NewGroup_NoIcon')}
          aria-pressed={icon === null}
          onClick={() => onIconChange(null)}
        >
          ∅
        </button>
        {GROUP_ICONS.map((i) => (
          <button
            key={i.key}
            type="button"
            className={`appearance-icon${icon === i.key ? ' selected' : ''}`}
            aria-label={t(`GroupIcon_${i.key}`)}
            aria-pressed={icon === i.key}
            onClick={() => onIconChange(i.key)}
          >
            <span className="icon" aria-hidden="true">
              {i.glyph}
            </span>
          </button>
        ))}
      </div>
    </div>
  )
}
