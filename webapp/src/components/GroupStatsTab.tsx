import { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { supabase } from '../lib/supabaseClient'
import { fetchAll } from '../lib/fetchAll'
import { useAliases } from '../context/AliasesContext'
import { useLocale } from '../context/LocaleContext'
import { formatMoney } from '../lib/formatting'
import type { Group, MemberRow } from '../lib/types'
import {
  categoryColor,
  categoryFrequency,
  categoryTrend,
  expensesCsv,
  filterByRange,
  settlementsByMonth,
  spendByCategory,
  spendByMember,
  type Bar,
  type MemberMode,
  type StatsExpense,
  type StatsRange,
  type StatsShare,
  type Trend,
} from '../lib/stats'
import './GroupStatsTab.css'

const RANGES: { range: StatsRange; label: string }[] = [
  { range: 'all', label: 'Stats_RangeAllTime' },
  { range: '3m', label: 'Stats_Range3Months' },
  { range: '6m', label: 'Stats_Range6Months' },
  { range: '12m', label: 'Stats_Range12Months' },
]

// Stats tab of the group page — mirrors Android's GroupStatsView/GroupStatsViewModel. Loads the
// group's whole history once (the parent only mounts this on the tab's first selection) and
// aggregates client-side in lib/stats.ts. Charts are plain HTML/SVG, no chart library.
export function GroupStatsTab({ group, members }: { group: Group; members: MemberRow[] }) {
  const { displayName } = useAliases()
  const { t, language } = useLocale()
  const [expenses, setExpenses] = useState<StatsExpense[] | null>(null)
  const [shares, setShares] = useState<StatsShare[]>([])
  const [error, setError] = useState<string | null>(null)
  const [range, setRange] = useState<StatsRange>('all')
  const [mode, setMode] = useState<MemberMode>('paid')
  const [isPrinting, setIsPrinting] = useState(false)

  useEffect(() => {
    let cancelled = false
    Promise.all([
      fetchAll<StatsExpense>((from, to) => supabase
        .from('expenses')
        .select('id, paid_by_member_id, amount, currency, amount_in_group_currency, description, category, occurred_at, is_settlement')
        .eq('group_id', group.id)
        .order('occurred_at', { ascending: true })
        .order('id', { ascending: true })
        .range(from, to)),
      fetchAll<StatsShare>((from, to) => supabase
        .from('expense_shares')
        .select('expense_id, member_id, share_amount_in_group_currency, expenses!inner(group_id)')
        .eq('expenses.group_id', group.id)
        .order('expense_id', { ascending: true })
        .order('member_id', { ascending: true })
        .range(from, to)),
    ])
      .then(([expenseRows, shareRows]) => {
        if (cancelled) return
        setExpenses(expenseRows)
        setShares(shareRows)
      })
      .catch((err: Error) => !cancelled && setError(err.message))
    return () => {
      cancelled = true
    }
  }, [group.id])

  useEffect(() => {
    if (!isPrinting) return
    document.body.classList.add('printing-stats')
    const done = () => setIsPrinting(false)
    window.addEventListener('afterprint', done)
    window.print()
    return () => {
      window.removeEventListener('afterprint', done)
      document.body.classList.remove('printing-stats')
    }
  }, [isPrinting])

  if (error) return <p className="error-text" role="alert">{error}</p>
  if (!expenses) return <div className="spinner">{t('Common_Loading')}</div>
  if (expenses.length === 0) return <p className="empty-state">{t('Stats_Empty')}</p>

  const memberName = (id: string) => {
    const row = members.find((m) => m.member_id === id)
    return row ? displayName(row.members) : '?'
  }
  const categoryLabel = (key: string) => t(`Category_${key}`)
  const money = (value: number) => formatMoney(value, group.currency, language)
  const count = (value: number) => String(value)
  const monthLabel = (key: string) => {
    const [year, month] = key.split('-').map(Number)
    return new Intl.DateTimeFormat(language === 'es' ? 'es-ES' : 'en-US', { month: 'short', year: 'numeric' })
      .format(new Date(year, month - 1, 1))
  }

  const filtered = filterByRange(expenses, range, new Date())
  const byCategory = spendByCategory(filtered)
  const frequency = categoryFrequency(filtered)
  const byMember = spendByMember(filtered, shares, mode)
  const settlements = settlementsByMonth(filtered)
  const trend = categoryTrend(filtered)
  const rangeLabel = t(RANGES.find((r) => r.range === range)!.label)

  function downloadCsv() {
    const csv = expensesCsv(expenses!, memberName, categoryLabel)
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }))
    const link = document.createElement('a')
    link.href = url
    link.download = `${group.name.replace(/[\\/:*?"<>|]/g, '').trim() || 'group'}-expenses.csv`
    link.click()
    URL.revokeObjectURL(url)
  }

  return (
    <div className="stats-tab">
      <div className="stats-toolbar">
        <div className="stats-ranges" role="group" aria-label={t('Stats_DateRange')}>
          {RANGES.map((r) => (
            <button
              key={r.range}
              type="button"
              className={range === r.range ? 'active' : ''}
              aria-pressed={range === r.range}
              onClick={() => setRange(r.range)}
            >
              {t(r.label)}
            </button>
          ))}
        </div>
        <div className="stats-export">
          <button type="button" className="btn btn-outline" onClick={downloadCsv}>{t('Stats_ExportCsv')}</button>
          <button type="button" className="btn btn-outline" onClick={() => setIsPrinting(true)}>{t('Stats_PrintPdf')}</button>
        </div>
      </div>

      <div className="stats-grid">
        <section className="card stats-card">
          <h2 className="section-title">{t('Stats_SpendByCategory')}</h2>
          <BarChart bars={byCategory} label={categoryLabel} format={money} />
        </section>

        <section className="card stats-card">
          <h2 className="section-title">{t('Stats_CategoryFrequency')}</h2>
          <BarChart bars={frequency} label={categoryLabel} format={count} />
        </section>

        <section className="card stats-card stats-wide">
          <h2 className="section-title">{t('Stats_CategoryTrend')}</h2>
          <TrendChart trend={trend} label={categoryLabel} monthLabel={monthLabel} format={money} />
        </section>

        <section className="card stats-card">
          <div className="stats-card-header">
            <h2 className="section-title">{t('Stats_SpendByMember')}</h2>
            <div className="stats-mode" role="group" aria-label={t('Stats_SpendByMember')}>
              <button type="button" className={mode === 'paid' ? 'active' : ''} aria-pressed={mode === 'paid'} onClick={() => setMode('paid')}>
                {t('Stats_PaidMode')}
              </button>
              <button type="button" className={mode === 'share' ? 'active' : ''} aria-pressed={mode === 'share'} onClick={() => setMode('share')}>
                {t('Stats_ShareMode')}
              </button>
            </div>
          </div>
          <BarChart bars={byMember} label={memberName} format={money} />
        </section>

        <section className="card stats-card">
          <h2 className="section-title">{t('Stats_SettleUpCadence')}</h2>
          <ColumnChart bars={settlements} label={monthLabel} format={count} />
        </section>
      </div>

      {createPortal(
        <div className="stats-print">
          <h1>{group.name} — {t('GroupDetail_StatsTab')}</h1>
          <p>{rangeLabel}</p>
          <PrintTable title={t('Stats_SpendByCategory')} bars={byCategory} label={categoryLabel} format={money} />
          <PrintTable title={t('Stats_CategoryFrequency')} bars={frequency} label={categoryLabel} format={count} />
          <PrintTable title={t('Stats_SpendByMember')} bars={spendByMember(filtered, shares, 'paid')} label={memberName} format={money} />
          <PrintTable title={t('Stats_SettleUpCadence')} bars={settlements} label={monthLabel} format={count} />
        </div>,
        document.body,
      )}
    </div>
  )
}

interface ChartProps {
  bars: Bar[]
  label: (key: string) => string
  format: (value: number) => string
}

function BarChart({ bars, label, format }: ChartProps) {
  if (bars.length === 0) return <p className="empty-state small">—</p>
  const max = Math.max(...bars.map((b) => b.value))
  return (
    <ul className="bar-chart">
      {bars.map((bar) => (
        <li key={bar.key} title={`${label(bar.key)}: ${format(bar.value)}`}>
          <span className="bar-label">{label(bar.key)}</span>
          <span className="bar-track">
            <span className="bar" style={{ width: `${max > 0 ? (bar.value / max) * 100 : 0}%` }} />
            <span className="bar-value">{format(bar.value)}</span>
          </span>
        </li>
      ))}
    </ul>
  )
}

function ColumnChart({ bars, label, format }: ChartProps) {
  if (bars.length === 0) return <p className="empty-state small">—</p>
  const max = Math.max(...bars.map((b) => b.value))
  return (
    <ul className="column-chart">
      {bars.map((bar) => (
        <li key={bar.key} title={`${label(bar.key)}: ${format(bar.value)}`}>
          <span className="column-area">
            <span className="column-value">{format(bar.value)}</span>
            <span className="column" style={{ height: `${max > 0 ? (bar.value / max) * 100 : 0}%` }} />
          </span>
          <span className="column-label">{label(bar.key)}</span>
        </li>
      ))}
    </ul>
  )
}

// Gridline values on a clean 1/2/5 × 10^n step, about four intervals, starting at 0.
function niceTicks(max: number): number[] {
  if (max <= 0) return [0, 1]
  const rough = max / 4
  const magnitude = 10 ** Math.floor(Math.log10(rough))
  const step = [1, 2, 5, 10].find((s) => s * magnitude >= rough)! * magnitude
  return Array.from({ length: Math.ceil(max / step) + 1 }, (_, i) => i * step)
}

// The SVG's coordinate width follows its rendered width, so text stays at its CSS size
// instead of scaling with the viewBox.
function useWidth<T extends HTMLElement>(fallback: number) {
  const [element, setElement] = useState<T | null>(null)
  const [width, setWidth] = useState(fallback)
  useEffect(() => {
    if (!element) return
    const observer = new ResizeObserver(([entry]) => setWidth(Math.round(entry.contentRect.width) || fallback))
    observer.observe(element)
    return () => observer.disconnect()
  }, [element, fallback])
  return [setElement, width] as const
}

function TrendChart({ trend, label, monthLabel, format }: {
  trend: Trend
  label: (key: string) => string
  monthLabel: (key: string) => string
  format: (value: number) => string
}) {
  const { t } = useLocale()
  const [hover, setHover] = useState<number | null>(null)
  const [plotRef, width] = useWidth<HTMLDivElement>(640)
  if (trend.months.length === 0) return <p className="empty-state small">—</p>

  const height = 220
  const pad = { top: 12, right: 24, bottom: 28, left: 48 }
  const plotW = width - pad.left - pad.right
  const plotH = height - pad.top - pad.bottom
  const ticks = niceTicks(Math.max(...trend.series.flatMap((s) => s.values)))
  const top = ticks[ticks.length - 1]
  const x = (i: number) => pad.left + (trend.months.length === 1 ? plotW / 2 : (i / (trend.months.length - 1)) * plotW)
  const y = (v: number) => pad.top + plotH - (v / top) * plotH
  const slot = trend.months.length === 1 ? plotW : plotW / (trend.months.length - 1)

  return (
    <div className="trend">
      <ul className="trend-legend">
        {trend.series.map((s) => (
          <li key={s.key}>
            <span className="legend-line" style={{ background: categoryColor(s.key) }} />
            {label(s.key)}
          </li>
        ))}
      </ul>
      <div className="trend-plot" ref={plotRef} onPointerLeave={() => setHover(null)}>
        <svg viewBox={`0 0 ${width} ${height}`} width={width} height={height} role="img" aria-label={t('Stats_CategoryTrend')}>
          {ticks.map((v) => (
            <g key={v}>
              <line className="grid" x1={pad.left} x2={width - pad.right} y1={y(v)} y2={y(v)} />
              <text className="tick" x={pad.left - 8} y={y(v) + 4} textAnchor="end">{Math.round(v)}</text>
            </g>
          ))}
          {trend.months.map((m, i) => (
            <text key={m} className="tick" x={x(i)} y={height - 8} textAnchor="middle">{monthLabel(m)}</text>
          ))}
          {hover !== null && <line className="crosshair" x1={x(hover)} x2={x(hover)} y1={pad.top} y2={pad.top + plotH} />}
          {trend.series.map((s) => (
            <g key={s.key} style={{ color: categoryColor(s.key) }}>
              {s.values.length > 1 && (
                <polyline className="trend-line" points={s.values.map((v, i) => `${x(i)},${y(v)}`).join(' ')} />
              )}
              {s.values.map((v, i) => <circle key={i} className="trend-dot" cx={x(i)} cy={y(v)} r={4} />)}
            </g>
          ))}
          {trend.months.map((m, i) => (
            <rect
              key={m}
              className="hit"
              x={x(i) - slot / 2}
              y={pad.top}
              width={slot}
              height={plotH}
              onPointerEnter={() => setHover(i)}
            />
          ))}
        </svg>
        {hover !== null && (
          <div className="trend-tooltip" style={{ left: `${(x(hover) / width) * 100}%` }}>
            <div className="tooltip-title">{monthLabel(trend.months[hover])}</div>
            {trend.series.map((s) => (
              <div key={s.key} className="tooltip-row">
                <span className="legend-line" style={{ background: categoryColor(s.key) }} />
                <strong>{format(s.values[hover])}</strong>
                <span>{label(s.key)}</span>
              </div>
            ))}
          </div>
        )}
      </div>
      <details className="trend-table">
        <summary>{t('Stats_ShowTable')}</summary>
        <table>
          <thead>
            <tr>
              <th>{t('Stats_Month')}</th>
              {trend.series.map((s) => <th key={s.key}>{label(s.key)}</th>)}
            </tr>
          </thead>
          <tbody>
            {trend.months.map((m, i) => (
              <tr key={m}>
                <td>{monthLabel(m)}</td>
                {trend.series.map((s) => <td key={s.key}>{format(s.values[i])}</td>)}
              </tr>
            ))}
          </tbody>
        </table>
      </details>
    </div>
  )
}

function PrintTable({ title, bars, label, format }: ChartProps & { title: string }) {
  return (
    <section>
      <h2>{title}</h2>
      <table>
        <tbody>
          {bars.length === 0 ? (
            <tr><td>—</td></tr>
          ) : (
            bars.map((bar) => (
              <tr key={bar.key}>
                <td>{label(bar.key)}</td>
                <td>{format(bar.value)}</td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </section>
  )
}
