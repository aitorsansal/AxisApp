import { useEffect, useState, type FocusEvent } from 'react'

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
  const [text, setText] = useState(() => isoToDisplay(value))

  useEffect(() => {
    setText(isoToDisplay(value))
  }, [value])

  function handleChange(raw: string) {
    const next = autoSlash(raw, text)
    setText(next)
    const iso = displayToIso(next)
    if (iso) onChange(iso)
  }

  function handleBlur(e: FocusEvent<HTMLInputElement>) {
    const iso = displayToIso(e.target.value)
    setText(iso ? isoToDisplay(iso) : isoToDisplay(value))
  }

  return (
    <input
      id={id}
      type="text"
      inputMode="numeric"
      placeholder="dd/mm/yyyy"
      required={required}
      value={text}
      onChange={(e) => handleChange(e.target.value)}
      onBlur={handleBlur}
    />
  )
}
