// Mirrors materialize_recurring_expenses() and AxisApp/Models/RecurringSchedule.cs: the first
// occurrence is start_date; monthly/yearly ones are anchored on start_date (start + N periods,
// clamped to month end), so a day-31 template returns to the 31st after February. Dates are
// date-only 'YYYY-MM-DD' strings and are handled in UTC to avoid DST shifts.

function parse(date: string): [number, number, number] {
  const [y, m, d] = date.split('-').map(Number)
  return [y, m - 1, d]
}

function format(utc: Date): string {
  return utc.toISOString().slice(0, 10)
}

function addMonths(date: string, months: number): string {
  const [y, m, d] = parse(date)
  const lastDay = new Date(Date.UTC(y, m + months + 1, 0)).getUTCDate()
  return format(new Date(Date.UTC(y, m + months, Math.min(d, lastDay))))
}

function addDays(date: string, days: number): string {
  const [y, m, d] = parse(date)
  return format(new Date(Date.UTC(y, m, d + days)))
}

export function nextDue(startDate: string, lastProcessedDate: string | null, frequency: string): string {
  if (!lastProcessedDate) return startDate
  const [sy, sm] = parse(startDate)
  const [ly, lm] = parse(lastProcessedDate)
  const next =
    frequency === 'daily' ? addDays(lastProcessedDate, 1)
    : frequency === 'weekly' ? addDays(lastProcessedDate, 7)
    : frequency === 'monthly' ? addMonths(startDate, (ly - sy) * 12 + lm - sm + 1)
    : frequency === 'yearly' ? addMonths(startDate, (ly - sy + 1) * 12)
    : lastProcessedDate
  return next < startDate ? startDate : next
}
