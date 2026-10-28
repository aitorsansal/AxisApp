import { useEffect, useState, useCallback, useRef, type CSSProperties } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useGoBackTo } from '../lib/navigation'
import { supabase } from '../lib/supabaseClient'
import { useAuth } from '../context/AuthContext'
import { useAliases } from '../context/AliasesContext'
import { useLocale } from '../context/LocaleContext'
import type { ExpenseWithPayer, Group, MemberRow, PairwiseBalance } from '../lib/types'
import { AppHeader } from '../components/AppHeader'
import { GroupEventsTab } from '../components/GroupEventsTab'
import { GroupStatsTab } from '../components/GroupStatsTab'
import { GroupAppearancePicker } from '../components/GroupAppearancePicker'
import { Modal } from '../components/Modal'
import { SettlementForm } from '../components/SettlementForm'
import { GroupBalanceHero } from '../components/GroupBalanceHero'
import { localDateInput } from '../lib/dates'
import { settlementParties } from '../lib/settlements'
import { Avatar } from '../components/Avatar'
import { Icon } from '../components/Icon'
import { categoryIcon } from '../lib/icons'
import { categoryColor } from '../lib/stats'
import { formatMoney } from '../lib/formatting'
import { formatExpenseAmount, getShowConverted } from '../lib/amountDisplay'
import { useGroups } from '../context/useGroups'
import './GroupDetailPage.css'

const ACTIVITY_PAGE_SIZE = 30
const SEARCH_RESULT_LIMIT = 50

export function GroupDetailPage() {
  const { groupId } = useParams<{ groupId: string }>()
  const { session } = useAuth()
  const { displayName, initials, avatarUrl } = useAliases()
  const { refresh: refreshGroups } = useGroups()
  const { t, language } = useLocale()
  const goBackTo = useGoBackTo()

  const [group, setGroup] = useState<Group | null>(null)
  const [members, setMembers] = useState<MemberRow[]>([])
  const [balances, setBalances] = useState<PairwiseBalance[]>([])
  const [activity, setActivity] = useState<ExpenseWithPayer[]>([])
  const [hasMoreActivity, setHasMoreActivity] = useState(false)
  const [isLoadingMore, setIsLoadingMore] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [settlement, setSettlement] = useState<PairwiseBalance | null>(null)
  const [settlementBusy, setSettlementBusy] = useState(false)
  const [settlementNotice, setSettlementNotice] = useState<string | null>(null)
  const settlementTriggerRef = useRef<HTMLButtonElement | null>(null)

  const [searchQuery, setSearchQuery] = useState('')
  const [searchResults, setSearchResults] = useState<ExpenseWithPayer[] | null>(null)

  const [isMenuOpen, setIsMenuOpen] = useState(false)
  const [isTransferOpen, setIsTransferOpen] = useState(false)
  const [isAppearanceOpen, setIsAppearanceOpen] = useState(false)
  // Same three tabs as Android's GroupDetailTab enum. Stats mounts on its first selection and
  // then stays mounted (hidden), so it doesn't refetch the whole history on every switch.
  const [tab, setTab] = useState<'expenses' | 'events' | 'stats'>('expenses')
  const [hasOpenedStats, setHasOpenedStats] = useState(false)
  const isEventsTab = tab === 'events'
  const menuRef = useRef<HTMLDivElement>(null)
  const menuButtonRef = useRef<HTMLButtonElement>(null)

  const memberName = (id: string) => {
    const row = members.find((m) => m.member_id === id)
    return row ? displayName(row.members) : t('GroupDetail_SomeoneCapitalized')
  }

  const myMemberId = members.find((m) => m.members.account_id === session?.user.id)?.member_id
  const isCreator = group?.created_by === session?.user.id
  const transferCandidates = members.filter(
    (m) => m.members.account_id && m.member_id !== myMemberId,
  )

  const activitySelect =
    'id, group_id, paid_by_member_id, amount, currency, amount_in_group_currency, description, category, occurred_at, created_at, is_settlement, receipt_path, event_id'

  const load = useCallback(async () => {
    if (!groupId) return
    setError(null)

    const [groupRes, membersRes, balancesRes, activityRes] = await Promise.all([
      supabase
        .from('groups')
        .select('id, name, currency, created_by, color, icon')
        .eq('id', groupId)
        .single(),
      supabase
        .from('group_members')
        .select('member_id, members(id, display_name, account_id, avatar_path)')
        .eq('group_id', groupId),
      supabase
        .from('my_pairwise_balances')
        .select('group_id, other_member_id, balance')
        .eq('group_id', groupId),
      supabase
        .from('expenses')
        .select(activitySelect)
        .eq('group_id', groupId)
        .order('occurred_at', { ascending: false })
        .order('created_at', { ascending: false })
        .range(0, ACTIVITY_PAGE_SIZE - 1),
    ])

    if (groupRes.error) {
      setGroup(null)
      return setError(groupRes.error.code === 'PGRST116' || groupRes.error.code === '22P02'
        ? t('GroupDetail_Unavailable') : groupRes.error.message)
    }
    if (membersRes.error) return setError(membersRes.error.message)
    if (balancesRes.error) return setError(balancesRes.error.message)
    if (activityRes.error) return setError(activityRes.error.message)

    setGroup(groupRes.data as Group)
    setMembers((membersRes.data as unknown as MemberRow[]) ?? [])
    setBalances((balancesRes.data as PairwiseBalance[]) ?? [])
    const page = (activityRes.data as unknown as ExpenseWithPayer[]) ?? []
    setActivity(page)
    setHasMoreActivity(page.length === ACTIVITY_PAGE_SIZE)
  }, [groupId, t])

  useEffect(() => {
    load()
  }, [load])

  async function loadMoreActivity() {
    if (!groupId || isLoadingMore) return
    setIsLoadingMore(true)
    const { data, error } = await supabase
      .from('expenses')
      .select(activitySelect)
      .eq('group_id', groupId)
      .order('occurred_at', { ascending: false })
      .order('created_at', { ascending: false })
      .range(activity.length, activity.length + ACTIVITY_PAGE_SIZE - 1)
    setIsLoadingMore(false)
    if (error) return setError(error.message)
    const page = (data as unknown as ExpenseWithPayer[]) ?? []
    setActivity((prev) => [...prev, ...page])
    setHasMoreActivity(page.length === ACTIVITY_PAGE_SIZE)
  }

  // Server-side search on description only — same scope as GroupExpensesViewModel's
  // SearchForGroupAsync — debounced with a short timeout rather than a library, since this is
  // the only debounced input in the whole app.
  useEffect(() => {
    if (!groupId) return
    const query = searchQuery.trim()
    if (!query) {
      setSearchResults(null)
      return
    }
    const timeout = setTimeout(async () => {
      const { data, error } = await supabase
        .from('expenses')
        .select(activitySelect)
        .eq('group_id', groupId)
        .ilike('description', `%${query}%`)
        .order('occurred_at', { ascending: false })
        .order('created_at', { ascending: false })
        .limit(SEARCH_RESULT_LIMIT)
      if (error) return setError(error.message)
      setSearchResults((data as unknown as ExpenseWithPayer[]) ?? [])
    }, 300)
    return () => clearTimeout(timeout)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [groupId, searchQuery])

  useEffect(() => {
    function handleEscape(e: KeyboardEvent) {
      if (e.key === 'Escape' && menuRef.current?.contains(e.target as Node)) {
        setIsMenuOpen(false)
        menuButtonRef.current?.focus()
      }
    }
    function handleClickOutside(e: MouseEvent) {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setIsMenuOpen(false)
    }
    document.addEventListener('mousedown', handleClickOutside)
    document.addEventListener('keydown', handleEscape)
    return () => {
      document.removeEventListener('mousedown', handleClickOutside)
      document.removeEventListener('keydown', handleEscape)
    }
  }, [])

  const [appearanceColor, setAppearanceColor] = useState('Blue')
  const [appearanceIcon, setAppearanceIcon] = useState<string | null>(null)

  function openAppearance() {
    if (!group) return
    setIsMenuOpen(false)
    setAppearanceColor(group.color)
    setAppearanceIcon(group.icon)
    setIsAppearanceOpen(true)
  }

  async function handleSaveAppearance() {
    if (!group) return
    const { error } = await supabase
      .from('groups')
      .update({ color: appearanceColor, icon: appearanceIcon })
      .eq('id', group.id)
    if (error) return setError(error.message)
    setIsAppearanceOpen(false)
    setGroup({ ...group, color: appearanceColor, icon: appearanceIcon })
    void refreshGroups()
  }

  async function handleRename() {
    if (!group) return
    setIsMenuOpen(false)
    const next = window.prompt(t('GroupDetail_RenamePrompt'), group.name)
    if (!next || !next.trim() || next.trim() === group.name) return
    const { error } = await supabase.from('groups').update({ name: next.trim() }).eq('id', group.id)
    if (error) return setError(error.message)
    void refreshGroups()
    load()
  }

  async function handleLeave() {
    if (!groupId) return
    setIsMenuOpen(false)
    if (!window.confirm(t('GroupDetail_LeaveGroupConfirm'))) return
    const { error } = await supabase.rpc('leave_group', { p_group_id: groupId })
    if (error) return setError(error.message)
    goBackTo('/')
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
    goBackTo('/')
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
      <div className="page page-wide group-detail-page">
        <AppHeader title={t('GroupDetail_GenericTitle')} backTo="/" />
        {error ? (
          <p className="error-text">{error}</p>
        ) : (
          <div className="spinner">{t('Common_Loading')}</div>
        )}
      </div>
    )
  }

  const dateLocale = language === 'es' ? 'es-ES' : 'en-US'
  const showConverted = getShowConverted()

  return (
    <div className="page page-wide group-detail-page">
      <AppHeader
        title={group.name}
        backTo="/"
        actions={
          <>
            <Link
              to={isEventsTab ? `/groups/${groupId}/events/new` : `/groups/${groupId}/add-expense`}
              className="btn btn-primary add-expense-btn desktop-add"
            >
              {isEventsTab ? t('GroupEvents_AddEvent') : t('GroupDetail_AddExpenseButton')}
            </Link>
            <div className="group-menu" ref={menuRef}>
              <button
                type="button"
                className="btn btn-outline menu-trigger"
                ref={menuButtonRef}
                aria-label={t('GroupDetail_OptionsMenu')}
                aria-expanded={isMenuOpen}
                onClick={() => setIsMenuOpen((v) => !v)}
              >
                <Icon name="more" />
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
                  <button type="button" onClick={openAppearance}>
                    {t('GroupDetail_EditAppearance')}
                  </button>
                  {isCreator && (
                    <>
                      <button type="button" onClick={handleRename}>
                        {t('GroupDetail_RenameGroup')}
                      </button>
                      <button type="button" onClick={openTransfer}>
                        {t('GroupDetail_TransferOwnership')}
                      </button>
                      <button type="button" className="danger" onClick={handleDissolve}>
                        {t('GroupDetail_DissolveGroup')}
                      </button>
                    </>
                  )}
                  {!isCreator && (
                    <button type="button" className="danger" onClick={handleLeave}>
                      {t('GroupDetail_LeaveGroup')}
                    </button>
                  )}
                </div>
              )}
            </div>
          </>
        }
      />
      {error && <p className="error-text">{error}</p>}
      {settlementNotice && <p role="status">{settlementNotice}</p>}

      {settlement && myMemberId && (
        <Modal title={t('Settlement_Record')} returnFocusRef={settlementTriggerRef} className="settlement-modal"
          onClose={() => {
            if (settlementBusy) return
            setSettlement(null)
            void load()
          }}>
          <SettlementForm
            initial={{
              ...settlementParties(myMemberId, settlement.other_member_id, settlement.balance),
              amount: Math.abs(settlement.balance).toFixed(2),
              currency: group.currency,
              date: localDateInput(),
              note: '',
            }}
            groupId={group.id}
            members={members}
            myMemberId={myMemberId}
            otherMemberId={settlement.other_member_id}
            expectedBalance={settlement.balance}
            onBusyChange={setSettlementBusy}
            onCancel={() => { setSettlement(null); void load() }}
            onSaved={(remaining) => {
              setSettlementNotice(remaining === null ? t('Settlement_Recorded')
                : t('Settlement_Remaining', memberName(settlement.other_member_id), formatMoney(Math.abs(remaining), group.currency, language)))
              setSettlement(null)
              setSettlementBusy(false)
              setSearchQuery('')
              setSearchResults(null)
              void load()
              void refreshGroups()
            }}
          />
        </Modal>
      )}

      <div className="tab-pill group-tabs" aria-label={t('GroupDetail_GenericTitle')}>
        {([
          ['expenses', 'GroupDetail_ExpensesTab'],
          ['events', 'GroupDetail_EventsTab'],
          ['stats', 'GroupDetail_StatsTab'],
        ] as const).map(([key, label]) => (
          <button
            key={key}
            type="button"
            className={tab === key ? 'active' : ''}
            aria-pressed={tab === key}
            onClick={() => {
              setTab(key)
              if (key === 'stats') setHasOpenedStats(true)
            }}
          >
            {t(label)}
          </button>
        ))}
      </div>

      {isTransferOpen && (
        <Modal
          returnFocusRef={menuButtonRef}
          title={t('GroupDetail_TransferOwnershipTitle')}
          onClose={() => setIsTransferOpen(false)}
        >
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
          <button
            type="button"
            className="btn btn-outline"
            onClick={() => setIsTransferOpen(false)}
          >
            {t('Common_Cancel')}
          </button>
        </Modal>
      )}

      {isAppearanceOpen && (
        <Modal
          returnFocusRef={menuButtonRef}
          title={t('GroupDetail_EditAppearance')}
          onClose={() => setIsAppearanceOpen(false)}
        >
          <GroupAppearancePicker
            color={appearanceColor}
            icon={appearanceIcon}
            onColorChange={setAppearanceColor}
            onIconChange={setAppearanceIcon}
          />
          <div className="appearance-actions">
            <button
              type="button"
              className="btn btn-outline"
              onClick={() => setIsAppearanceOpen(false)}
            >
              {t('Common_Cancel')}
            </button>
            <button type="button" className="btn btn-primary" onClick={handleSaveAppearance}>
              {t('Common_Save')}
            </button>
          </div>
        </Modal>
      )}

      {hasOpenedStats && (
        <div hidden={tab !== 'stats'}>
          <GroupStatsTab group={group} members={members} />
        </div>
      )}

      {tab === 'stats' ? null : isEventsTab ? (
        <GroupEventsTab groupId={groupId!} members={members} />
      ) : (
        <>
        {(balances.length > 0 || activity.length > 0) && (
          <GroupBalanceHero balances={balances} currency={group.currency} memberName={memberName} />
        )}
        <div className="expense-overview">
          <section className="balances-panel">
            <h2 className="section-title">{t('GroupDetail_Balances')}</h2>
            {balances.length === 0 && (
              <p className="empty-state small">{t('GroupDetail_BalancesEmpty')}</p>
            )}
            <div className="balance-list">
              {balances.map((b) => {
                const member = members.find((row) => row.member_id === b.other_member_id)?.members
                return (
                  <div
                    className={`card balance-row ${b.balance > 0 ? 'balance-receive' : b.balance < 0 ? 'balance-pay' : 'balance-neutral'}`}
                    key={b.other_member_id}
                  >
                    <Avatar
                      url={member ? avatarUrl(member) : null}
                      initials={member ? initials(member) : '?'}
                      seed={b.other_member_id}
                      size={36}
                    />
                    <div className="balance-name" title={memberName(b.other_member_id)}>
                      {memberName(b.other_member_id)}
                    </div>
                    <div className="balance-value">
                      <strong>{formatMoney(b.balance, group.currency, language, true)}</strong>
                      <span>
                        {t(
                          b.balance > 0
                            ? 'GroupDetail_OwesYou'
                            : b.balance < 0
                              ? 'GroupDetail_YouOwe'
                              : 'Common_SettledUp',
                        )}
                      </span>
                    </div>
                    {Math.abs(b.balance) >= 0.005 && (
                      <button
                        className="btn btn-outline settle-btn"
                        disabled={!myMemberId}
                        onClick={(event) => {
                          settlementTriggerRef.current = event.currentTarget
                          setSettlementNotice(null)
                          setSettlementBusy(false)
                          setSettlement(b)
                        }}
                      >
                        {t('Settlement_Record')}
                      </button>
                    )}
                  </div>
                )
              })}
            </div>
            <p className="currency-note">{t('GroupDetail_CurrencyNote', group.currency)}</p>
          </section>

          <section className="activity-panel">
            <h2 className="section-title">{t('GroupDetail_RecentActivity')}</h2>
            <input
              type="search"
              className="activity-search"
              placeholder={t('GroupDetail_SearchExpenses')}
              aria-label={t('GroupDetail_SearchExpenses')}
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
            />
            {(() => {
              const shownActivity = searchResults ?? activity
              if (shownActivity.length === 0) {
                return (
                  <p className="empty-state small">
                    {searchResults !== null
                      ? t('GroupDetail_NoSearchResults')
                      : t('GroupDetail_ActivityEmpty')}
                  </p>
                )
              }
              return (
                <>
                  <div className="activity-list">
                    {shownActivity.map((exp) => (
                      <Link
                        to={`/groups/${groupId}/expenses/${exp.id}`}
                        className="card activity-row"
                        key={exp.id}
                      >
                        <span
                          className="activity-icon"
                          // Same per-category colour as the Stats tab; settle-ups stay neutral.
                          style={exp.is_settlement ? undefined : ({ '--category': categoryColor(exp.category) } as CSSProperties)}
                        >
                          <Icon name={categoryIcon(exp.category, exp.is_settlement)} size={22} />
                        </span>
                        <div className="activity-body">
                          <div className="activity-desc">
                            {exp.description || t(exp.is_settlement ? 'GroupDetail_SettleUp' : 'GroupDetail_ExpenseFallback')}
                            {exp.receipt_path && (
                              <span className="receipt-badge" title={t('AddExpense_ReceiptPhoto')}>
                                📎
                              </span>
                            )}
                            {exp.event_id && (
                              <span
                                className="receipt-badge"
                                title={t('GroupDetail_LinkedToEvent')}
                              >
                                <Icon name="calendar" size={14} />
                              </span>
                            )}
                          </div>
                          <div className="activity-meta">
                            {t('GroupDetail_PaidBy', memberName(exp.paid_by_member_id))}
                          </div>
                        </div>
                        <div className="activity-value">
                          {(() => {
                            const shown = formatExpenseAmount(exp, group.currency, language, showConverted)
                            return (
                              <>
                                <strong className="activity-amount">{shown.primary}</strong>
                                {shown.secondary && <span className="activity-secondary">{shown.secondary}</span>}
                              </>
                            )
                          })()}
                          <span className="activity-date">
                            {new Date(exp.occurred_at).toLocaleDateString(dateLocale, {
                              day: 'numeric',
                              month: 'short',
                            })}
                          </span>
                        </div>
                      </Link>
                    ))}
                  </div>
                  {searchResults === null && hasMoreActivity && (
                    <button
                      type="button"
                      className="btn btn-outline load-more-btn"
                      onClick={loadMoreActivity}
                      disabled={isLoadingMore}
                    >
                      {isLoadingMore ? t('Common_Loading') : t('GroupDetail_LoadMore')}
                    </button>
                  )}
                </>
              )
            })()}
          </section>
        </div>
        </>
      )}
      <Link
        to={isEventsTab ? `/groups/${groupId}/events/new` : `/groups/${groupId}/add-expense`}
        className="floating-add"
        aria-label={isEventsTab ? t('GroupEvents_AddEvent') : t('GroupDetail_AddExpenseButton')}
      >
        <Icon name="plus" size={26} />
      </Link>
    </div>
  )
}
