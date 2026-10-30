import { useLocale } from '../context/LocaleContext'
import type { DirectoryGroup } from '../context/useGroups'
import { isSettled, summarizeBalances, type BalanceSide } from '../lib/balanceSummary'
import { formatMoney } from '../lib/formatting'
import { fromMinor } from '../lib/currencyUnits'
import type { PairwiseBalance } from '../lib/types'
import './BalanceSummary.css'

// Groups page header: what you're owed and what you owe across every group, as two separate
// totals (see lib/balanceSummary.ts for why they're never netted).
export function BalanceSummary({
  groups,
  pairwise,
}: {
  groups: DirectoryGroup[]
  pairwise: PairwiseBalance[]
}) {
  const { t } = useLocale()
  if (groups.length === 0) return null
  const currencyOf = new Map(groups.map((g) => [g.id, g.currency]))
  const summary = summarizeBalances(pairwise, (id) => currencyOf.get(id))

  if (isSettled(summary)) {
    return (
      <section className="balance-summary settled" aria-label={t('Summary_Label')}>
        <strong>{t('Common_SettledUp')}</strong>
        <span>{t('Summary_AllSettled')}</span>
      </section>
    )
  }
  return (
    <section className="balance-summary" aria-label={t('Summary_Label')}>
      <SummaryBlock kind="owed" label={t('Summary_OwedToYou')} side={summary.owedToMe} groups={groups} />
      <SummaryBlock kind="owe" label={t('Summary_YouOwe')} side={summary.iOwe} groups={groups} />
    </section>
  )
}

function SummaryBlock({
  kind,
  label,
  side,
  groups,
}: {
  kind: 'owed' | 'owe'
  label: string
  side: BalanceSide
  groups: DirectoryGroup[]
}) {
  const { t, language } = useLocale()
  const empty = side.totals.length === 0
  const people = side.memberIds.length === 1 ? t('Summary_OnePerson') : t('Summary_People', side.memberIds.length)
  // Name the groups while that stays short; past two, just count them.
  const where =
    side.groupIds.length <= 2
      ? new Intl.ListFormat(language === 'es' ? 'es' : 'en', { type: 'conjunction' }).format(
          side.groupIds.map((id) => groups.find((g) => g.id === id)?.name ?? ''),
        )
      : t('Summary_InGroups', side.groupIds.length)
  return (
    <div className={`summary-block summary-${kind}${empty ? ' empty' : ''}`}>
      <span className="summary-label">{label}</span>
      {empty ? (
        <span className="summary-nothing">{t('Summary_NothingPending')}</span>
      ) : (
        <>
          {/* One line per currency: amounts in different currencies are never added together. */}
          {side.totals.map((total, index) => (
            <span key={total.currency} className={index === 0 ? 'summary-amount' : 'summary-amount secondary'}>
              {formatMoney(fromMinor(total.cents, total.currency), total.currency, language)}
            </span>
          ))}
          <span className="summary-detail">
            {people} · {where}
          </span>
        </>
      )}
    </div>
  )
}
