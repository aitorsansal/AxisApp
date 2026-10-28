import { useEffect, useRef, useState } from 'react'
import { useLocale } from '../context/LocaleContext'
import { isCalendarDate } from '../lib/dates'

// Native <input type="date"> renders its displayed format (not just the
// picker) from the browser's own locale (navigator.languages), not from an
// element's lang attribute — confirmed live, contrary to some outdated
// advice. That made it impossible to force dd/MM/yyyy display without
// building this. Value/onChange still speak plain ISO (yyyy-MM-dd), same
// contract every native date input elsewhere in this app already used, so
// callers don't need to change anything else.
function isoToDisplay(iso: string): string {
  if (!iso) return ''
  const [y, m, d] = iso.split('-')
  if (!y || !m || !d) return ''
  return `${d}/${m}/${y}`
}

function displayToIso(display: string): string | null {
  const match = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(display)
  if (!match) return null
  const [, d, m, y] = match
  return `${y}-${m}-${d}`
}

function autoSlash(raw: string, previous: string): string {
  const digitsOnly = raw.replace(/[^\d]/g, '')
  let out = ''
  for (let i = 0; i < digitsOnly.length && i < 8; i++) {
    if (i === 2 || i === 4) out += '/'
    out += digitsOnly[i]
  }
  // Deleting a trailing "/" should also drop the digit before it, not get re-inserted.
  if (raw.length < previous.length && previous.startsWith(out) && out.endsWith('/')) {
    out = out.slice(0, -1)
  }
  return out
}

export function DateField({
  id,
  value,
  onChange,
  required,
}: {
  id: string
  value: string
  onChange: (isoValue: string) => void
  required?: boolean
}) {
  const { t } = useLocale()
  const [text, setText] = useState(() => isoToDisplay(value))
  const [touched, setTouched] = useState(false)
  const emittedValue = useRef(value)
  const input = useRef<HTMLInputElement>(null)
  const iso = displayToIso(text)
  const valid = (!required && text === '') || (iso !== null && isCalendarDate(iso))
  const message = valid ? '' : t('DateInput_Invalid')

  useEffect(() => {
    if (value !== emittedValue.current) {
      emittedValue.current = value
      setText(isoToDisplay(value))
      setTouched(false)
    }
  }, [value])

  useEffect(() => {
    input.current?.setCustomValidity(message)
  }, [message])

  function handleChange(raw: string) {
    const next = autoSlash(raw, text)
    setText(next)
    const nextValue = displayToIso(next) ?? ''
    emittedValue.current = nextValue
    onChange(nextValue)
  }

  return (
    <>
      <input
        ref={input}
        id={id}
        type="text"
        inputMode="numeric"
        placeholder="dd/mm/yyyy"
        required={required}
        value={text}
        aria-invalid={touched && !valid}
        aria-describedby={touched && !valid ? `${id}-error` : undefined}
        onChange={(e) => handleChange(e.target.value)}
        onBlur={() => setTouched(true)}
        onInvalid={() => setTouched(true)}
      />
      {touched && !valid && <p id={`${id}-error`} className="error-text" role="alert">{message}</p>}
    </>
  )
}
