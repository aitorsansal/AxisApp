import { useState, type InputHTMLAttributes } from 'react'
import { useLocale } from '../context/LocaleContext'
import { Icon } from './Icon'
import './PasswordInput.css'

// Password field with a show/hide toggle, same glyphs as Android's PasswordEyeConverter:
// the crossed-out eye means "visible, tap to hide".
export function PasswordInput(props: Omit<InputHTMLAttributes<HTMLInputElement>, 'type'>) {
  const { t } = useLocale()
  const [visible, setVisible] = useState(false)
  return (
    <div className="password-input">
      <input {...props} type={visible ? 'text' : 'password'} />
      <button
        type="button"
        className="password-toggle"
        onClick={() => setVisible((v) => !v)}
        aria-label={visible ? t('Common_HidePassword') : t('Common_ShowPassword')}
        aria-pressed={visible}
      >
        <Icon name={visible ? 'eye_off' : 'eye'} size={18} />
      </button>
    </div>
  )
}
