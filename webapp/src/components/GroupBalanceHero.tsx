import { useLocale } from '../context/LocaleContext'
import { isSettled, summarizeBalances, type BalanceSide } from '../lib/balanceSummary'
import { formatMoney } from '../lib/formatting'
import { fromMinor } from '../lib/currencyUnits'
import type { PairwiseBalance } from '../lib/types'
import './GroupBalanceHero.css'

export function GroupBalanceHero({
  balances,
  currency,
  memberName,
}: {
  balances: PairwiseBalance[]
  currency: string
  memberName: (memberId: string) => string
}) {
  const { t, language } = useLocale()
  const summary = summarizeBalances(balances, () => currency)

  function who(side: BalanceSide) {
    if (side.memberIds.length > 2) return t('GroupDetail_HeroPeople', side.memberIds.length)
    return new Intl.ListFormat(language === 'es' ? 'es' : 'en', { type: 'conjunction' }).format(
      side.memberIds.map(memberName),
    )
  }

  if (isSettled(summary)) {
    return (
      <section className="group-balance-hero settled" aria-label={t('GroupDetail_YourBalance')}>
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none"
          stroke="currentColor" strokeWidth="2" role="presentation" aria-hidden="true">
          <path d="m5 12 4 4L19 6" />
        </svg>
        <div>
          <strong>{t('Common_SettledUp')}</strong>
          <span>{t('GroupDetail_YouAreSettled')}</span>
        </div>
      </section>
    )
  }

  return (
    <section className="group-balance-hero" aria-label={t('GroupDetail_YourBalance')}>
      {([
        ['owed', 'Summary_OwedToYou', summary.owedToMe],
        ['owe', 'Summary_YouOwe', summary.iOwe],
      ] as const).map(([kind, label, side]) => (
        <div className={`group-balance-side group-balance-${kind}`} key={kind}>
          <span className="group-balance-label">{t(label)}</span>
          {side.totals.length === 0 ? (
            <span className="group-balance-nothing">{t('Summary_NothingPending')}</span>
          ) : (
            <>
              <strong className="group-balance-amount">
                {formatMoney(fromMinor(side.totals[0].cents, currency), currency, language)}
              </strong>
              <span className="group-balance-people">{who(side)}</span>
            </>
          )}
        </div>
      ))}
    </section>
  )
}
