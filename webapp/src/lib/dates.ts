export function localDateInput(value: Date | string = new Date()): string {
  const date = typeof value === 'string' ? new Date(value) : value
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

export function expenseTimestamp(date: string, original: string | null): string {
  if (original && localDateInput(original) === date) return original
  return new Date(`${date}T00:00:00`).toISOString()
}

export function formatEventRange(start: string, end: string | null, language: string): string {
  const locale = language === 'es' ? 'es-ES' : 'en-US'
  const options: Intl.DateTimeFormatOptions = {
    weekday: 'short', year: 'numeric', month: 'short', day: 'numeric',
    hour: '2-digit', minute: '2-digit',
  }
  const result = new Date(start).toLocaleString(locale, options)
  if (!end) return result
  const endOptions = localDateInput(start) === localDateInput(end)
    ? { hour: '2-digit', minute: '2-digit' } as const
    : options
  return `${result} – ${new Date(end).toLocaleString(locale, endOptions)}`
}
