export function isCalendarDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false
  const [year, month, day] = value.split('-').map(Number)
  if (year < 1 || month < 1 || month > 12 || day < 1) return false
  const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0)
  const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
  return day <= days[month - 1]
}

export function localDateTimeToIso(date: string, time: string): string {
  if (!isCalendarDate(date) || !/^(?:[01]\d|2[0-3]):[0-5]\d$/.test(time))
    throw new RangeError('Invalid local date or time')
  const value = new Date(`${date}T${time}:00`)
  if (!Number.isFinite(value.getTime()) || localDateInput(value) !== date
    || value.getHours() !== Number(time.slice(0, 2)) || value.getMinutes() !== Number(time.slice(3)))
    throw new RangeError('Invalid local date or time')
  return value.toISOString()
}

export function localDateInput(value: Date | string = new Date()): string {
  const date = typeof value === 'string' ? new Date(value) : value
  return `${String(date.getFullYear()).padStart(4, '0')}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

export function expenseTimestamp(date: string, original: string | null): string {
  if (!isCalendarDate(date)) throw new RangeError('Invalid expense date')
  if (original && localDateInput(original) === date) return original
  const value = new Date(`${date}T00:00:00`)
  if (!Number.isFinite(value.getTime()) || localDateInput(value) !== date)
    throw new RangeError('Invalid expense date')
  return value.toISOString()
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

// Activity-row date, same rule as Android's GroupExpensesViewModel.FormatRelative: occurred_at
// is a calendar day, so compare local days (never elapsed hours) and show the year only when
// it isn't this year.
export function activityDate(
  iso: string,
  language: string,
  labels: { today: string; yesterday: string },
  now: Date = new Date(),
): string {
  const day = localDateInput(iso)
  if (day === localDateInput(now)) return labels.today
  const yesterday = new Date(now)
  yesterday.setDate(now.getDate() - 1)
  if (day === localDateInput(yesterday)) return labels.yesterday
  const date = new Date(iso)
  return date.toLocaleDateString(language === 'es' ? 'es-ES' : 'en-US', {
    day: 'numeric',
    month: 'short',
    year: date.getFullYear() === now.getFullYear() ? undefined : 'numeric',
  })
}
