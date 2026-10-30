import { useLocale } from '../context/LocaleContext'
import { summarizeBalances, type BalanceSide } from '../lib/balanceSummary'
import { formatMoney } from '../lib/formatting'
import { fromMinor } from '../lib/currencyUnits'
import type { PairwiseBalance } from '../lib/types'
import './GroupBalanceHero.css'

// Group Detail's headline: your net in this group, explained by who owes you and whom you owe.
// The parts come from the same pairwise rows as the Balances list, so they always add up.
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
  const owedCents = summary.owedToMe.totals[0]?.cents ?? 0
  const oweCents = summary.iOwe.totals[0]?.cents ?? 0
  const net = fromMinor(owedCents - oweCents, currency)

  // Up to two people are named; more are counted.
  function who(side: BalanceSide) {
    if (side.memberIds.length > 2) return t('GroupDetail_HeroPeople', side.memberIds.length)
    return new Intl.ListFormat(language === 'es' ? 'es' : 'en', { type: 'conjunction' }).format(
      side.memberIds.map(memberName),
    )
  }
  const parts: string[] = []
  if (owedCents > 0) {
    const key = summary.owedToMe.memberIds.length === 1 ? 'GroupDetail_HeroOwedOne' : 'GroupDetail_HeroOwedMany'
    parts.push(t(key, who(summary.owedToMe), formatMoney(fromMinor(owedCents, currency), currency, language)))
  }
  if (oweCents > 0) {
    const key = summary.iOwe.memberIds.length === 1 ? 'GroupDetail_HeroOweOne' : 'GroupDetail_HeroOweMany'
    parts.push(t(key, who(summary.iOwe), formatMoney(fromMinor(oweCents, currency), currency, language)))
  }
  const state = net > 0 ? 'Groups_YoureOwed' : net < 0 ? 'Groups_YouOwe' : 'Common_SettledUp'

  return (
    <section className={`group-balance-hero${net === 0 ? ' settled' : ''}`} aria-label={t('GroupDetail_YourBalance')}>
      <span className="hero-label">{t('GroupDetail_YourBalance')}</span>
      <span className="hero-amount">
        {net === 0 ? t('Common_SettledUp') : formatMoney(net, currency, language, true)}
        {net !== 0 && <span className="hero-state">{t(state)}</span>}
      </span>
      <span className="hero-detail">{parts.length ? parts.join(' · ') : t('GroupDetail_HeroAllSettled')}</span>
    </section>
  )
}
