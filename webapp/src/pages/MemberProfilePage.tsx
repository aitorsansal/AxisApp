import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { fetchAll } from '../lib/fetchAll'
import { formatMoney } from '../lib/formatting'
import { pairBreakdown, type PairBreakdown, type PairExpense, type PairShare } from '../lib/pairBreakdown'
import { useGroups } from '../context/useGroups'
import { useAliases } from '../context/AliasesContext'
import { useLocale } from '../context/LocaleContext'
import { AppHeader } from '../components/AppHeader'
import { Avatar } from '../components/Avatar'
import './MemberProfilePage.css'

// Collapsed list length per group; "Show all" reveals the rest.
const COLLAPSED_EXPENSE_COUNT = 10

interface GroupPair {
  groupId: string
  balance: number // my_pairwise_balances, the same number Group Detail shows
  breakdown: PairBreakdown
}

// "You and X" — mirrors Android's MemberProfilePage. For every group both of you are in, the
// pairwise balance, the three lines it's made of (lib/pairBreakdown.ts) and the expenses between
// the two of you. Shared groups and members come from GroupsContext; each group then fetches only
// the rows involving one of you two.
export function MemberProfilePage() {
  const { groupId, memberId } = useParams<{ groupId: string; memberId: string }>()
  const { groups, currentMember } = useGroups()
  const { displayName, initials, avatarUrl } = useAliases()
  const { t, language } = useLocale()
  const [pairs, setPairs] = useState<GroupPair[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [expanded, setExpanded] = useState<Set<string>>(new Set())

  const sharedGroups = (groups ?? []).filter((g) => g.members.some((m) => m.id === memberId))
  const target = sharedGroups.flatMap((g) => g.members).find((m) => m.id === memberId)
  const me = currentMember?.id
  const sharedKey = sharedGroups.map((g) => g.id).join(',')

  useEffect(() => {
    if (!me || !memberId || !sharedKey) return
    let cancelled = false
    const parties = [me, memberId]
    Promise.all(sharedKey.split(',').map(async (id): Promise<GroupPair> => {
      const [balanceRes, expenses, shares] = await Promise.all([
        supabase
          .from('my_pairwise_balances')
          .select('balance')
          .eq('group_id', id)
          .eq('other_member_id', memberId)
          .maybeSingle(),
        fetchAll<PairExpense>((from, to) => supabase
          .from('expenses')
          .select('id, paid_by_member_id, is_settlement, description, category, occurred_at')
          .eq('group_id', id)
          .in('paid_by_member_id', parties)
          .order('occurred_at', { ascending: true })
          .order('id', { ascending: true })
          .range(from, to)),
        fetchAll<PairShare>((from, to) => supabase
          .from('expense_shares')
          .select('expense_id, member_id, share_amount_in_group_currency, expenses!inner(group_id)')
          .eq('expenses.group_id', id)
          .in('member_id', parties)
          .order('expense_id', { ascending: true })
          .order('member_id', { ascending: true })
          .range(from, to)),
      ])
      if (balanceRes.error) throw new Error(balanceRes.error.message)
      return {
        groupId: id,
        balance: Number(balanceRes.data?.balance ?? 0),
        breakdown: pairBreakdown(expenses, shares, me, memberId),
      }
    }))
      .then((result) => !cancelled && setPairs(result))
      .catch((err: Error) => !cancelled && setError(err.message))
    return () => {
      cancelled = true
    }
  }, [me, memberId, sharedKey])

  const name = target ? displayName(target) : ''
  const title = target ? t('MemberProfile_Title', name) : t('Members_Title')
  const backTo = `/groups/${groupId}/members`
  const dateLocale = language === 'es' ? 'es-ES' : 'en-US'
  const thisYear = new Date().getFullYear()

  if (groups && !target) {
    return (
      <div className="page">
        <AppHeader title={title} backTo={backTo} />
        <p className="empty-state">{t('MemberProfile_Empty')}</p>
      </div>
    )
  }

  return (
    <div className="page member-profile-page">
      <AppHeader title={title} backTo={backTo} />
      {error && <p className="error-text" role="alert">{error}</p>}
      {target && (
        <div className="member-profile-avatar">
          <Avatar url={avatarUrl(target)} initials={initials(target)} size={64} />
        </div>
      )}
      {!pairs ? (
        !error && <div className="spinner">{t('Common_Loading')}</div>
      ) : (
        <div className="pair-groups">
          {pairs.map(({ groupId: id, balance, breakdown }) => {
            const group = sharedGroups.find((g) => g.id === id)
            if (!group) return null
            const money = (value: number, signed = false) => formatMoney(value, group.currency, language, signed)
            const isExpanded = expanded.has(id)
            const shown = isExpanded ? breakdown.expenses : breakdown.expenses.slice(0, COLLAPSED_EXPENSE_COUNT)
            const settlementsLabel = breakdown.settlementsNet === 0
              ? t('MemberProfile_Settlements')
              : `${t('MemberProfile_Settlements')} (${t(breakdown.settlementsNet < 0 ? 'MemberProfile_TheyPaidYou' : 'MemberProfile_YouPaidThem', name)})`
            return (
              <section key={id} className="pair-group">
                <div className={`card pair-balance ${balance > 0 ? 'balance-receive' : balance < 0 ? 'balance-pay' : 'balance-neutral'}`}>
                  <span className="pair-group-name">{group.name}</span>
                  <span className="pair-balance-value">
                    <strong>{money(balance, true)}</strong>
                    <span>{t(balance > 0 ? 'GroupDetail_OwesYou' : balance < 0 ? 'GroupDetail_YouOwe' : 'Common_SettledUp')}</span>
                  </span>
                </div>

                <div className="card pair-details">
                  <dl className="pair-lines">
                    <dt>{t('MemberProfile_PaidForThem', name)}</dt>
                    <dd>{money(breakdown.paidForThem, true)}</dd>
                    <dt>{t('MemberProfile_PaidForYou', name)}</dt>
                    <dd>{money(-breakdown.paidForMe, true)}</dd>
                    <dt>{settlementsLabel}</dt>
                    <dd>{money(breakdown.settlementsNet, true)}</dd>
                  </dl>

                  <h2 className="section-title">{t('MemberProfile_SharedExpenses')}</h2>
                  {breakdown.expenses.length === 0 ? (
                    <p className="empty-state small">{t('MemberProfile_NoSharedExpenses')}</p>
                  ) : (
                    <ul className="pair-expenses">
                      {shown.map((line) => (
                        <li key={line.expenseId}>
                          <Link to={`/groups/${id}/expenses/${line.expenseId}`}>
                            <span className="pair-expense-main">
                              <span className="pair-expense-desc">{line.description || t(`Category_${line.category || 'other'}`)}</span>
                              <span className="pair-expense-date">
                                {new Date(line.occurredAt).toLocaleDateString(dateLocale, {
                                  day: 'numeric',
                                  month: 'short',
                                  ...(new Date(line.occurredAt).getFullYear() !== thisYear ? { year: 'numeric' } : {}),
                                })}
                              </span>
                            </span>
                            <span className="pair-expense-part">
                              {t(line.iPaid ? 'MemberProfile_TheirPart' : 'MemberProfile_YourPart', money(line.amount))}
                            </span>
                          </Link>
                        </li>
                      ))}
                    </ul>
                  )}
                  {!isExpanded && breakdown.expenses.length > COLLAPSED_EXPENSE_COUNT && (
                    <button
                      type="button"
                      className="btn btn-outline pair-show-all"
                      onClick={() => setExpanded((prev) => new Set(prev).add(id))}
                    >
                      {t('MemberProfile_ShowAll', breakdown.expenses.length)}
                    </button>
                  )}
                </div>
              </section>
            )
          })}
        </div>
      )}
    </div>
  )
}
