import { useEffect, useState, useCallback } from 'react'
import { Link, useParams } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { useAuth } from '../context/AuthContext'
import { useLocale } from '../context/LocaleContext'
import type { ExpenseWithPayer, Group, MemberRow, PairwiseBalance } from '../lib/types'
import { AppHeader } from '../components/AppHeader'
import './GroupDetailPage.css'

export function GroupDetailPage() {
  const { groupId } = useParams<{ groupId: string }>()
  const { session } = useAuth()
  const { t, language } = useLocale()
  const [group, setGroup] = useState<Group | null>(null)
  const [members, setMembers] = useState<MemberRow[]>([])
  const [balances, setBalances] = useState<PairwiseBalance[]>([])
  const [activity, setActivity] = useState<ExpenseWithPayer[]>([])
  const [error, setError] = useState<string | null>(null)
  const [settlingId, setSettlingId] = useState<string | null>(null)

  const memberName = (id: string) =>
    members.find((m) => m.member_id === id)?.members.display_name ?? t('GroupDetail_SomeoneCapitalized')

  const myMemberId = members.find((m) => m.members.account_id === session?.user.id)?.member_id

  const load = useCallback(async () => {
    if (!groupId) return
    setError(null)

    const [groupRes, membersRes, balancesRes, activityRes] = await Promise.all([
      supabase.from('groups').select('id, name, currency, created_by').eq('id', groupId).single(),
      supabase.from('group_members').select('member_id, members(display_name, account_id)').eq('group_id', groupId),
      supabase.from('my_pairwise_balances').select('group_id, other_member_id, balance').eq('group_id', groupId),
      supabase
        .from('expenses')
        .select('id, group_id, paid_by_member_id, amount, currency, description, category, occurred_at, created_at, is_settlement, payer:members!paid_by_member_id(display_name)')
        .eq('group_id', groupId)
        .order('occurred_at', { ascending: false })
        .order('created_at', { ascending: false })
        .limit(30),
    ])

    if (groupRes.error) return setError(groupRes.error.message)
    if (membersRes.error) return setError(membersRes.error.message)
    if (balancesRes.error) return setError(balancesRes.error.message)
    if (activityRes.error) return setError(activityRes.error.message)

    setGroup(groupRes.data as Group)
    setMembers((membersRes.data as unknown as MemberRow[]) ?? [])
    setBalances((balancesRes.data as PairwiseBalance[]) ?? [])
    setActivity((activityRes.data as unknown as ExpenseWithPayer[]) ?? [])
  }, [groupId])

  useEffect(() => {
    load()
  }, [load])

  async function handleSettle(otherMemberId: string, balance: number) {
    if (!group || !myMemberId || !groupId) return
    setSettlingId(otherMemberId)
    setError(null)

    const discharging = balance > 0 ? otherMemberId : myMemberId
    const receiving = balance > 0 ? myMemberId : otherMemberId
    const amount = Math.abs(balance)

    const { data: expense, error: expenseError } = await supabase
      .from('expenses')
      .insert({
        group_id: groupId,
        paid_by_member_id: discharging,
        amount,
        currency: group.currency,
        description: t('GroupDetail_SettleUp'),
        is_settlement: true,
      })
      .select('id')
      .single()

    if (expenseError) {
      setError(expenseError.message)
      setSettlingId(null)
      return
    }

    const { error: shareError } = await supabase
      .from('expense_shares')
      .insert({ expense_id: expense.id, member_id: receiving, share_amount: amount })

    setSettlingId(null)
    if (shareError) return setError(shareError.message)
    load()
  }

  if (!group) {
    return (
      <div className="page">
        <AppHeader title={t('GroupDetail_GenericTitle')} back />
        {error ? <p className="error-text">{error}</p> : <div className="spinner">{t('Common_Loading')}</div>}
      </div>
    )
  }

  const dateLocale = language === 'es' ? 'es-ES' : 'en-US'

  return (
    <div className="page">
      <AppHeader title={group.name} back />
      {error && <p className="error-text">{error}</p>}

      <Link to={`/groups/${groupId}/add-expense`} className="btn btn-primary add-expense-btn">
        {t('GroupDetail_AddExpenseButton')}
      </Link>

      <section>
        <h2 className="section-title">{t('GroupDetail_Balances')}</h2>
        {balances.length === 0 && <p className="empty-state small">{t('GroupDetail_BalancesEmpty')}</p>}
        <div className="balance-list">
          {balances.map((b) => (
            <div className="card balance-row" key={b.other_member_id}>
              <div>
                <div className="balance-name">{memberName(b.other_member_id)}</div>
                <div className={b.balance > 0 ? 'balance-positive' : 'balance-negative'}>
                  {t(
                    b.balance > 0 ? 'GroupDetail_OwesYouAmount' : 'GroupDetail_YouOweAmount',
                    Math.abs(b.balance).toFixed(2),
                    group.currency,
                  )}
                </div>
              </div>
              <button
                className="btn btn-outline settle-btn"
                disabled={!myMemberId || settlingId === b.other_member_id}
                onClick={() => handleSettle(b.other_member_id, b.balance)}
              >
                {settlingId === b.other_member_id ? t('GroupDetail_Settling') : t('GroupDetail_Settle')}
              </button>
            </div>
          ))}
        </div>
      </section>

      <section>
        <h2 className="section-title">{t('GroupDetail_RecentActivity')}</h2>
        {activity.length === 0 && <p className="empty-state small">{t('GroupDetail_ActivityEmpty')}</p>}
        <div className="activity-list">
          {activity.map((exp) => (
            <div className="card activity-row" key={exp.id}>
              <div>
                <div className="activity-desc">
                  {exp.is_settlement ? t('GroupDetail_SettleUp') : exp.description || t('GroupDetail_ExpenseFallback')}
                </div>
                <div className="activity-meta">
                  {exp.payer?.display_name ?? t('GroupDetail_SomeoneCapitalized')} ·{' '}
                  {new Date(exp.occurred_at).toLocaleDateString(dateLocale)}
                </div>
              </div>
              <div className="activity-amount">
                {exp.amount.toFixed(2)} {exp.currency}
              </div>
            </div>
          ))}
        </div>
      </section>
    </div>
  )
}
