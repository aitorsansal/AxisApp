// Stats tab aggregations — mirrors AxisApp/ViewModels/GroupStatsViewModel.cs and the CSV/PDF
// tables in AxisApp/Services/StatsExporter.cs. Pure (no React/Supabase) so
// tests/regressions.test.mjs can run it directly.

import { amountText } from './currencyUnits.ts'

export type StatsRange = 'all' | '3m' | '6m' | '12m'
export type MemberMode = 'paid' | 'share'

export interface StatsExpense {
  id: string
  paid_by_member_id: string
  amount: number
  currency: string
  amount_in_group_currency: number
  description: string | null
  category: string | null
  occurred_at: string
  is_settlement: boolean
}

export interface StatsShare {
  expense_id: string
  member_id: string
  share_amount_in_group_currency: number
}

export interface Bar {
  key: string
  value: number
}

// Same order as AppConstants.Categories.Keys: a category's palette slot is its position here,
// never its rank in one chart. Legacy keys outside the list fall back to slot 0, as on Android.
export const CATEGORY_ORDER = ['food', 'drinks', 'transport', 'travel', 'entertainment', 'shopping', 'subscriptions', 'other']

// Validated against the dark surface #0B1220 (dataviz validate_palette.js), same as Android's
// GroupStatsViewModel.CategoricalPalette.
export const CATEGORY_PALETTE = ['#3987E5', '#D95926', '#199E70', '#C98500', '#D55181', '#008300', '#9085E9', '#E66767']

export function categoryKey(category: string | null): string {
  return category ? category : 'other'
}

export function categoryColor(category: string | null): string {
  const index = CATEGORY_ORDER.indexOf(categoryKey(category))
  return CATEGORY_PALETTE[Math.max(0, index) % CATEGORY_PALETTE.length]
}

const RANGE_MONTHS: Record<StatsRange, number> = { all: 0, '3m': 3, '6m': 6, '12m': 12 }

// UtcNow.AddMonths(-n): the day clamps to the target month's length instead of overflowing.
export function rangeCutoff(range: StatsRange, now: Date): Date | null {
  const months = RANGE_MONTHS[range]
  if (!months) return null
  const year = now.getUTCFullYear()
  const month = now.getUTCMonth() - months
  const lastDay = new Date(Date.UTC(year, month + 1, 0)).getUTCDate()
  return new Date(Date.UTC(year, month, Math.min(now.getUTCDate(), lastDay),
    now.getUTCHours(), now.getUTCMinutes(), now.getUTCSeconds(), now.getUTCMilliseconds()))
}

export function filterByRange(expenses: StatsExpense[], range: StatsRange, now: Date): StatsExpense[] {
  const cutoff = rangeCutoff(range, now)
  return cutoff ? expenses.filter((e) => new Date(e.occurred_at) >= cutoff) : expenses
}

// A settle-up is a transfer between members, not spending — excluded from every spend chart.
function spending(expenses: StatsExpense[]): StatsExpense[] {
  return expenses.filter((e) => !e.is_settlement)
}

function sumBy<T>(items: T[], key: (item: T) => string, value: (item: T) => number): Bar[] {
  const totals = new Map<string, number>()
  for (const item of items) totals.set(key(item), (totals.get(key(item)) ?? 0) + value(item))
  return [...totals].map(([k, v]) => ({ key: k, value: v })).sort((a, b) => b.value - a.value)
}

export function spendByCategory(expenses: StatsExpense[]): Bar[] {
  return sumBy(spending(expenses), (e) => categoryKey(e.category), (e) => Number(e.amount_in_group_currency))
}

export function categoryFrequency(expenses: StatsExpense[]): Bar[] {
  return sumBy(spending(expenses), (e) => categoryKey(e.category), () => 1)
}

// Paid = who fronted the money; Share = whose shares it was, regardless of who paid.
export function spendByMember(expenses: StatsExpense[], shares: StatsShare[], mode: MemberMode): Bar[] {
  const spend = spending(expenses)
  if (mode === 'paid') return sumBy(spend, (e) => e.paid_by_member_id, (e) => Number(e.amount_in_group_currency))
  const ids = new Set(spend.map((e) => e.id))
  return sumBy(shares.filter((s) => ids.has(s.expense_id)), (s) => s.member_id, (s) => Number(s.share_amount_in_group_currency))
}

// Month key in the viewer's local time, like Android's OccurredAt.ToLocalTime().
export function monthKey(iso: string): string {
  const date = new Date(iso)
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`
}

export function settlementsByMonth(expenses: StatsExpense[]): Bar[] {
  return sumBy(expenses.filter((e) => e.is_settlement), (e) => monthKey(e.occurred_at), () => 1)
    .sort((a, b) => a.key.localeCompare(b.key))
}

export interface Trend {
  months: string[]
  series: { key: string; values: number[] }[]
}

export function categoryTrend(expenses: StatsExpense[]): Trend {
  const spend = spending(expenses)
  const months = [...new Set(spend.map((e) => monthKey(e.occurred_at)))].sort()
  const byCategory = new Map<string, number[]>()
  for (const e of spend) {
    const key = categoryKey(e.category)
    const values = byCategory.get(key) ?? months.map(() => 0)
    values[months.indexOf(monthKey(e.occurred_at))] += Number(e.amount_in_group_currency)
    byCategory.set(key, values)
  }
  const series = [...byCategory]
    .map(([key, values]) => ({ key, values }))
    .sort((a, b) => Math.max(0, CATEGORY_ORDER.indexOf(a.key)) - Math.max(0, CATEGORY_ORDER.indexOf(b.key)))
  return { months, series }
}

function csvField(value: string): string {
  return /[",\n]/.test(value) ? `"${value.replaceAll('"', '""')}"` : value
}

function localDate(iso: string): string {
  const date = new Date(iso)
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

// Same columns and English header/Yes-No as StatsExporter.ExportExpensesCsvAsync: the group's
// whole history, newest first, amounts in each expense's own currency.
export function expensesCsv(
  expenses: StatsExpense[],
  memberName: (memberId: string) => string,
  categoryLabel: (key: string) => string,
): string {
  const rows = [...expenses]
    .sort((a, b) => b.occurred_at.localeCompare(a.occurred_at))
    .map((e) => [
      localDate(e.occurred_at),
      e.description ?? '',
      categoryLabel(categoryKey(e.category)),
      amountText(e.amount, e.currency),
      e.currency,
      memberName(e.paid_by_member_id),
      e.is_settlement ? 'Yes' : 'No',
    ].map(csvField).join(','))
  return ['Date,Description,Category,Amount,Currency,Paid By,Settlement', ...rows].join('\n') + '\n'
}
