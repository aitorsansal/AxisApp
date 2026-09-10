import { useEffect, useState, useCallback, useRef } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { useAuth } from '../context/AuthContext'
import { useAliases } from '../context/AliasesContext'
import { useLocale } from '../context/LocaleContext'
import type { ExpenseWithPayer, Group, MemberRow, PairwiseBalance } from '../lib/types'
import { AppHeader } from '../components/AppHeader'
import { GroupEventsTab } from '../components/GroupEventsTab'
import './GroupDetailPage.css'

export function GroupDetailPage() {
  const { groupId } = useParams<{ groupId: string }>()
  const { session } = useAuth()
  const { displayName } = useAliases()
  const { t, language } = useLocale()
  const navigate = useNavigate()

  const [group, setGroup] = useState<Group | null>(null)
  const [members, setMembers] = useState<MemberRow[]>([])
  const [balances, setBalances] = useState<PairwiseBalance[]>([])
  const [activity, setActivity] = useState<ExpenseWithPayer[]>([])
  const [error, setError] = useState<string | null>(null)
  const [settlingId, setSettlingId] = useState<string | null>(null)

  const [isMenuOpen, setIsMenuOpen] = useState(false)
  const [isTransferOpen, setIsTransferOpen] = useState(false)
  const [isEventsTab, setIsEventsTab] = useState(false)
  const menuRef = useRef<HTMLDivElement>(null)

  const memberName = (id: string) => {
    const row = members.find((m) => m.member_id === id)
    return row ? displayName(row.members) : t('GroupDetail_SomeoneCapitalized')
  }

  const myMemberId = members.find((m) => m.members.account_id === session?.user.id)?.member_id
  const isCreator = group?.created_by === session?.user.id
  const transferCandidates = members.filter((m) => m.members.account_id && m.member_id !== myMemberId)

  const load = useCallback(async () => {
    if (!groupId) return
    setError(null)

    const [groupRes, membersRes, balancesRes, activityRes] = await Promise.all([
      supabase.from('groups').select('id, name, currency, created_by').eq('id', groupId).single(),
      supabase.from('group_members').select('member_id, members(id, display_name, account_id, avatar_path)').eq('group_id', groupId),
      supabase.from('my_pairwise_balances').select('group_id, other_member_id, balance').eq('group_id', groupId),
      supabase
        .from('expenses')
        .select('id, group_id, paid_by_member_id, amount, currency, description, category, occurred_at, created_at, is_settlement, receipt_path')
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

  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setIsMenuOpen(false)
    }
    document.addEventListener('mousedown', handleClickOutside)
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [])

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

  async function handleRename() {
    if (!group) return
    setIsMenuOpen(false)
    const next = window.prompt(t('GroupDetail_RenamePrompt'), group.name)
    if (!next || !next.trim() || next.trim() === group.name) return
    const { error } = await supabase.from('groups').update({ name: next.trim() }).eq('id', group.id)
    if (error) return setError(error.message)
    load()
  }

  async function handleLeave() {
    if (!groupId) return
    setIsMenuOpen(false)
    if (!window.confirm(t('GroupDetail_LeaveGroupConfirm'))) return
    const { error } = await supabase.rpc('leave_group', { p_group_id: groupId })
    if (error) return setError(error.message)
    navigate('/')
  }

  async function handleDissolve() {
    if (!groupId) return
    setIsMenuOpen(false)
    const hasOutstanding = balances.some((b) => b.balance !== 0)
    const message = hasOutstanding
      ? t('GroupDetail_DissolveGroupConfirmWithBalances')
      : t('GroupDetail_DissolveGroupConfirm')
    if (!window.confirm(message)) return
    const { error } = await supabase.from('groups').delete().eq('id', groupId)
    if (error) return setError(error.message)
    navigate('/')
  }

  function openTransfer() {
    setIsMenuOpen(false)
    setIsTransferOpen(true)
  }

  async function handleTransfer(newOwnerMemberId: string) {
    if (!groupId) return
    setIsTransferOpen(false)
    const { error } = await supabase.rpc('transfer_group_ownership', {
      p_group_id: groupId,
      p_new_owner_member_id: newOwnerMemberId,
    })
    if (error) return setError(error.message)
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

      <div className="tab-pill">
        <button type="button" className={isEventsTab ? '' : 'active'} onClick={() => setIsEventsTab(false)}>
          {t('GroupDetail_ExpensesTab')}
        </button>
        <button type="button" className={isEventsTab ? 'active' : ''} onClick={() => setIsEventsTab(true)}>
          {t('GroupDetail_EventsTab')}
        </button>
      </div>

      <div className="group-toolbar">
        <Link
          to={isEventsTab ? `/groups/${groupId}/events/new` : `/groups/${groupId}/add-expense`}
          className="btn btn-primary add-expense-btn"
        >
          {isEventsTab ? t('GroupEvents_AddEvent') : t('GroupDetail_AddExpenseButton')}
        </Link>
        <div className="group-menu" ref={menuRef}>
          <button
            type="button"
            className="btn btn-outline menu-trigger"
            aria-label={t('GroupDetail_OptionsMenu')}
            onClick={() => setIsMenuOpen((v) => !v)}
          >
            ⋮
          </button>
          {isMenuOpen && (
            <div className="menu-dropdown">
              <Link to={`/groups/${groupId}/members`} onClick={() => setIsMenuOpen(false)}>
                {t('GroupDetail_ViewMembers')}
              </Link>
              {!isEventsTab && (
                <Link to={`/groups/${groupId}/recurring`} onClick={() => setIsMenuOpen(false)}>
                  {t('GroupDetail_ManageRecurring')}
                </Link>
              )}
              {isCreator && (
                <>
                  <button type="button" onClick={handleRename}>{t('GroupDetail_RenameGroup')}</button>
                  <button type="button" onClick={openTransfer}>{t('GroupDetail_TransferOwnership')}</button>
                  <button type="button" className="danger" onClick={handleDissolve}>{t('GroupDetail_DissolveGroup')}</button>
                </>
              )}
              {!isCreator && (
                <button type="button" className="danger" onClick={handleLeave}>{t('GroupDetail_LeaveGroup')}</button>
              )}
            </div>
          )}
        </div>
      </div>

      {isTransferOpen && (
        <div className="overlay-scrim" onClick={() => setIsTransferOpen(false)}>
          <div className="overlay-card card" onClick={(e) => e.stopPropagation()}>
            <h3>{t('GroupDetail_TransferOwnershipTitle')}</h3>
            {transferCandidates.length === 0 ? (
              <p className="empty-state small">{t('GroupDetail_NoTransferCandidates')}</p>
            ) : (
              <ul className="transfer-list">
                {transferCandidates.map((m) => (
                  <li key={m.member_id}>
                    <button type="button" onClick={() => handleTransfer(m.member_id)}>
                      {displayName(m.members)}
                    </button>
                  </li>
                ))}
              </ul>
            )}
            <button type="button" className="btn btn-outline" onClick={() => setIsTransferOpen(false)}>
              {t('Common_Cancel')}
            </button>
          </div>
        </div>
      )}

      {isEventsTab ? (
        <GroupEventsTab groupId={groupId!} members={members} />
      ) : (
        <>
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
                <Link
                  to={`/groups/${groupId}/expenses/${exp.id}`}
                  className="card activity-row"
                  key={exp.id}
                >
                  <div>
                    <div className="activity-desc">
                      {exp.is_settlement ? t('GroupDetail_SettleUp') : exp.description || t('GroupDetail_ExpenseFallback')}
                      {exp.receipt_path && <span className="receipt-badge" title={t('AddExpense_ReceiptPhoto')}>📎</span>}
                    </div>
                    <div className="activity-meta">
                      {memberName(exp.paid_by_member_id)} ·{' '}
                      {new Date(exp.occurred_at).toLocaleDateString(dateLocale)}
                    </div>
                  </div>
                  <div className="activity-amount">
                    {exp.amount.toFixed(2)} {exp.currency}
                  </div>
                </Link>
              ))}
            </div>
          </section>
        </>
      )}
    </div>
  )
}
