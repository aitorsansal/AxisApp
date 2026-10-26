import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import { useLocation, useParams, useSearchParams } from 'react-router-dom'
import { useGoBackTo } from '../lib/navigation'
import { supabase } from '../lib/supabaseClient'
import { useAuth } from '../context/AuthContext'
import { useAliases } from '../context/AliasesContext'
import { useLocale } from '../context/LocaleContext'
import { resizeImageToWebp } from '../lib/imageResize'
import { equalSplitCents } from '../lib/expenseMath'
import { expenseTimestamp, localDateInput } from '../lib/dates'
import {
  CATEGORY_KEYS,
  CURRENCIES,
  RECURRING_FREQUENCIES,
  type Group,
  type MemberRow,
} from '../lib/types'
import { AppHeader } from '../components/AppHeader'
import { Avatar } from '../components/Avatar'
import { DateField } from '../components/DateField'
import './AddExpensePage.css'

const RECEIPTS_BUCKET = 'receipts'

// All split math runs in integer cents so shares always sum to exactly the
// total — float `+=` on a leftover cent produced values like 3.3400000000000003.
function parseCents(text: string) {
  const n = Number(text.replace(',', '.'))
  return Number.isFinite(n) ? Math.round(n * 100) : 0
}

const formatCents = (cents: number) => (cents / 100).toFixed(2)

export function AddExpensePage() {
  const { groupId, expenseId, recurringId } = useParams<{
    groupId: string
    expenseId?: string
    recurringId?: string
  }>()
  const location = useLocation()
  const [searchParams] = useSearchParams()
  const isRecurringRoute = location.pathname.includes('/recurring/')
  const { session } = useAuth()
  const { displayName, initials, avatarUrl } = useAliases()
  const { t } = useLocale()
  const goBackTo = useGoBackTo()

  const [group, setGroup] = useState<Group | null>(null)
  const [members, setMembers] = useState<MemberRow[]>([])
  const [description, setDescription] = useState('')
  const [amount, setAmount] = useState('')
  const [currency, setCurrency] = useState('EUR')
  const [category, setCategory] = useState<(typeof CATEGORY_KEYS)[number] | ''>('food')
  const [occurredOn, setOccurredOn] = useState(() => localDateInput())
  const [startDate, setStartDate] = useState(() => localDateInput())
  const originalOccurredAt = useRef<string | null>(null)
  const [frequency, setFrequency] = useState<(typeof RECURRING_FREQUENCIES)[number]>('monthly')
  const [paidBy, setPaidBy] = useState('')
  const [participants, setParticipants] = useState<Set<string>>(new Set())
  // Mirrors AddExpenseViewModel.IsManualSplit: off = shares derived equally from
  // amount + participants; on = shareInputs holds each participant's typed amount.
  // Editing starts in manual mode so a saved uneven split is never flattened.
  const [isManualSplit, setIsManualSplit] = useState(false)
  const [shareInputs, setShareInputs] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const [isEditMode, setIsEditMode] = useState(false)
  // Server rule (enforce_expense_delete_permission): creator, payer or group owner. Templates aren't restricted.
  const [canDelete, setCanDelete] = useState(false)
  const [isRecurringMode, setIsRecurringMode] = useState(isRecurringRoute)
  const [canToggleRecurring] = useState(!expenseId && !recurringId)
  const [isSettlement, setIsSettlement] = useState(false)

  const receiptInputRef = useRef<HTMLInputElement>(null)
  const [receiptPath, setReceiptPath] = useState<string | null>(null)
  const [receiptUrl, setReceiptUrl] = useState<string | null>(null)
  const [receiptBusy, setReceiptBusy] = useState(false)

  // Set from the ?eventId= query param on a brand-new expense (EventDetailPage's "+ Add
  // expense" link — see EventDetailViewModel.AddExpense's matching GoToAsync), or from the
  // expense's own row when editing one that's already linked. Carried through unchanged on
  // update, same "never re-derive a field the row already carries" rule as createdBy/createdAt.
  const [linkedEventId, setLinkedEventId] = useState<string | null>(searchParams.get('eventId'))
  const [linkedEventTitle, setLinkedEventTitle] = useState<string | null>(null)

  useEffect(() => {
    if (!groupId) return
    ;(async () => {
      originalOccurredAt.current = null;
      const [groupRes, membersRes] = await Promise.all([
        supabase.from('groups').select('id, name, currency, created_by').eq('id', groupId).single(),
        supabase
          .from('group_members')
          .select('member_id, members(id, display_name, account_id, avatar_path)')
          .eq('group_id', groupId),
      ])
      if (groupRes.error) return setError(groupRes.error.message)
      if (membersRes.error) return setError(membersRes.error.message)

      const groupData = groupRes.data as Group
      setGroup(groupData)
      const memberRows = (membersRes.data as unknown as MemberRow[]) ?? []
      setMembers(memberRows)

      if (expenseId) {
        setIsEditMode(true)
        const [expenseRes, sharesRes] = await Promise.all([
          supabase.from('expenses').select('*').eq('id', expenseId).single(),
          supabase
            .from('expense_shares')
            .select('member_id, share_amount')
            .eq('expense_id', expenseId),
        ])
        if (expenseRes.error) return setError(expenseRes.error.message)
        const expense = expenseRes.data
        setDescription(expense.description ?? '')
        setAmount(String(expense.amount))
        setCurrency(expense.currency)
        setCategory((expense.category || '') as typeof category)
        originalOccurredAt.current = expense.occurred_at
        setOccurredOn(localDateInput(expense.occurred_at))
        setPaidBy(expense.paid_by_member_id)
        setIsSettlement(expense.is_settlement)
        const myId = session?.user.id
        const payerAccount = memberRows.find((m) => m.member_id === expense.paid_by_member_id)
          ?.members.account_id
        setCanDelete(
          !!myId &&
            (expense.created_by === myId || payerAccount === myId || groupData.created_by === myId),
        )
        setReceiptPath(expense.receipt_path)
        setLinkedEventId(expense.event_id)
        loadShares(sharesRes.data ?? [])
        // A settlement is always one share for the full amount — keep it derived.
        setIsManualSplit(!expense.is_settlement)
        if (expense.receipt_path) {
          const { data: signed } = await supabase.storage
            .from(RECEIPTS_BUCKET)
            .createSignedUrl(expense.receipt_path, 3600)
          setReceiptUrl(signed?.signedUrl ?? null)
        }
      } else if (recurringId) {
        setIsEditMode(true)
        setCanDelete(true)
        setIsRecurringMode(true)
        const [templateRes, sharesRes] = await Promise.all([
          supabase.from('recurring_expenses').select('*').eq('id', recurringId).single(),
          supabase
            .from('recurring_expense_shares')
            .select('member_id, share_amount')
            .eq('recurring_expense_id', recurringId),
        ])
        if (templateRes.error) return setError(templateRes.error.message)
        const tpl = templateRes.data
        setDescription(tpl.description ?? '')
        setAmount(String(tpl.amount))
        setCurrency(tpl.currency)
        setCategory((tpl.category || '') as typeof category)
        setStartDate(tpl.start_date)
        setFrequency(tpl.frequency)
        setPaidBy(tpl.paid_by_member_id)
        loadShares(sharesRes.data ?? [])
        setIsManualSplit(true)
      } else {
        setCurrency(groupData.currency)
        const myRow = memberRows.find((m) => m.members.account_id === session?.user.id)
        if (myRow) setPaidBy(myRow.member_id)
        else if (memberRows[0]) setPaidBy(memberRows[0].member_id)

        // Pre-filter participants to whoever's currently "going" to the linked event, same as
        // AddExpenseViewModel.LoadAsync's forEventId handling — falls back to everyone if the
        // event has no "going" RSVPs yet (or isn't linked at all).
        if (linkedEventId) {
          const [{ data: eventRow }, { data: goingRows }] = await Promise.all([
            supabase.from('events').select('title').eq('id', linkedEventId).single(),
            supabase
              .from('event_attendees')
              .select('member_id')
              .eq('event_id', linkedEventId)
              .eq('response', 'going'),
          ])
          setLinkedEventTitle(eventRow?.title ?? null)
          const goingIds = new Set((goingRows ?? []).map((r: { member_id: string }) => r.member_id))
          setParticipants(
            goingIds.size > 0 ? goingIds : new Set(memberRows.map((m) => m.member_id)),
          )
        } else {
          setParticipants(new Set(memberRows.map((m) => m.member_id)))
        }
      }
    })()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [groupId, expenseId, recurringId, session])

  function loadShares(rows: { member_id: string; share_amount: number }[]) {
    setParticipants(new Set(rows.map((s) => s.member_id)))
    setShareInputs(
      Object.fromEntries(rows.map((s) => [s.member_id, Number(s.share_amount).toFixed(2)])),
    )
  }

  // Member-list order, so "last participant" (who absorbs the leftover cent) is stable.
  const includedIds = members.map((m) => m.member_id).filter((id) => participants.has(id))
  const totalCents = parseCents(amount)
  const useEqualSplit = !isManualSplit || isSettlement
  const shareCents: Record<string, number> = useEqualSplit
    ? equalSplitCents(totalCents, includedIds)
    : Object.fromEntries(includedIds.map((id) => [id, parseCents(shareInputs[id] ?? '')]))
  const remainingCents = totalCents - includedIds.reduce((sum, id) => sum + shareCents[id], 0)
  const isSplitValid = remainingCents === 0 && includedIds.every((id) => shareCents[id] > 0)

  function toggleParticipant(memberId: string) {
    setParticipants((prev) => {
      const next = new Set(prev)
      if (next.has(memberId)) next.delete(memberId)
      else next.add(memberId)
      return next
    })
    // An excluded participant owes nothing; re-including starts from empty, same as MAUI.
    if (isManualSplit) {
      setShareInputs((prev) => {
        const next = { ...prev }
        delete next[memberId]
        return next
      })
    }
  }

  function handleShareChange(memberId: string, value: string) {
    // First manual edit snapshots the current equal split so the other
    // participants keep their amounts instead of dropping to empty.
    const base = isManualSplit
      ? shareInputs
      : Object.fromEntries(includedIds.map((id) => [id, formatCents(shareCents[id])]))
    setShareInputs({ ...base, [memberId]: value })
    setIsManualSplit(true)
  }

  function handleSplitEqually() {
    setIsManualSplit(false)
    setShareInputs({})
  }

  function buildShares() {
    return includedIds.map((id) => ({ member_id: id, share_amount: shareCents[id] / 100 }))
  }

  async function handleReceiptChange(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file || !groupId) return
    setReceiptBusy(true)
    setError(null)
    try {
      const blob = await resizeImageToWebp(file, 1280, 0.75)
      const path = `${groupId}/${crypto.randomUUID()}.webp`
      const { error: uploadError } = await supabase.storage
        .from(RECEIPTS_BUCKET)
        .upload(path, blob, { contentType: 'image/webp' })
      if (uploadError) throw uploadError

      setReceiptPath(path)
      const { data: signed } = await supabase.storage
        .from(RECEIPTS_BUCKET)
        .createSignedUrl(path, 3600)
      setReceiptUrl(signed?.signedUrl ?? null)
    } catch (err) {
      setError(err instanceof Error ? err.message : t('Common_Error'))
    } finally {
      setReceiptBusy(false)
    }
  }

  async function handleRemoveReceipt() {
    if (!receiptPath) return
    setReceiptPath(null)
    setReceiptUrl(null)
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    if (!group || !groupId) return
    setError(null)

    const total = Number(amount)
    if (!total || total <= 0) {
      setError(t('AddExpense_InvalidAmount'))
      return
    }
    if (participants.size === 0) {
      setError(t('AddExpense_PickParticipant'))
      return
    }
    if (!paidBy) {
      setError(t('AddExpense_PickPayer'))
      return
    }
    if (!isSplitValid) {
      setError(t('AddExpense_SplitInvalid'))
      return
    }

    setBusy(true)

    // One transaction per save (supabase/atomic_expense_save.sql) — the row and
    // its full share list commit or roll back together. created_by/created_at
    // (and a template's schedule fields) are server-owned, never sent.
    if (isRecurringMode) {
      const { error: saveError } = await supabase.rpc('save_recurring_expense', {
        p_template: {
          id: recurringId ?? null,
          group_id: groupId,
          paid_by_member_id: paidBy,
          amount: total,
          currency,
          description,
          category,
          frequency,
          start_date: startDate,
        },
        p_shares: buildShares(),
      })
      setBusy(false)
      if (saveError) return setError(saveError.message)
      goBackTo(`/groups/${groupId}/recurring`)
      return
    }

    const { error: saveError } = await supabase.rpc('save_expense', {
      p_expense: {
        id: expenseId ?? null,
        group_id: groupId,
        paid_by_member_id: paidBy,
        amount: total,
        currency,
        description,
        category: isSettlement ? '' : category,
        occurred_at: expenseTimestamp(occurredOn, originalOccurredAt.current),
        receipt_path: receiptPath,
        is_settlement: isSettlement,
        event_id: linkedEventId,
      },
      p_shares: buildShares(),
    })
    setBusy(false)
    if (saveError) return setError(saveError.message)

    goBackTo(parentPath)
  }

  async function handleDelete() {
    if (!window.confirm(t('AddExpense_DeleteConfirm'))) return
    setBusy(true)
    const { error } = recurringId
      ? await supabase.from('recurring_expenses').delete().eq('id', recurringId)
      : await supabase.from('expenses').delete().eq('id', expenseId)
    setBusy(false)
    if (error) return setError(error.message)
    goBackTo(parentPath)
  }

  const parentPath = isRecurringRoute
    ? `/groups/${groupId}/recurring`
    : linkedEventId
      ? `/groups/${groupId}/events/${linkedEventId}`
      : `/groups/${groupId}`

  const title = recurringId
    ? t('AddExpense_EditRecurringTitle')
    : isSettlement
      ? t('AddExpense_EditSettlementTitle')
      : expenseId
        ? t('AddExpense_EditTitle')
        : isRecurringMode
          ? t('AddExpense_RecurringTitle')
          : t('AddExpense_Title')

  if (!group) {
    return (
      <div className="page expense-page">
        <AppHeader title={title} backTo={parentPath} />
        {error ? (
          <p className="error-text">{error}</p>
        ) : (
          <div className="spinner">{t('Common_Loading')}</div>
        )}
      </div>
    )
  }

  return (
    <div className="page expense-page">
      <AppHeader title={title} backTo={parentPath} />

      <form onSubmit={handleSubmit} className="expense-form">
        {linkedEventId && linkedEventTitle && (
          <p className="field-hint linked-event-hint">
            {t('AddExpense_LinkedToEvent', linkedEventTitle)}
          </p>
        )}

        <div className="amount-row">
          <div className="field amount-field">
            <label htmlFor="amount">{t('AddExpense_AmountLabel')}</label>
            <input
              id="amount"
              type="number"
              inputMode="decimal"
              placeholder="0.00"
              step="0.01"
              min="0.01"
              required
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
            />
          </div>
          <div className="field currency-field">
            <label htmlFor="currency">{t('AddExpense_Currency')}</label>
            <select id="currency" value={currency} onChange={(e) => setCurrency(e.target.value)}>
              {CURRENCIES.map((code) => (
                <option key={code} value={code}>
                  {code}
                </option>
              ))}
            </select>
          </div>
        </div>

        <div className="field">
          <label htmlFor="description">{t('AddExpense_DescriptionLabel')}</label>
          <input
            id="description"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder={t('AddExpense_DescriptionPlaceholder')}
          />
        </div>

        <fieldset className="payer-picker">
          <legend>{t('AddExpense_PaidBy')}</legend>
          <div className="payer-options">
            {members.map((m) => (
              <label
                className={`payer-option${paidBy === m.member_id ? ' selected' : ''}`}
                key={m.member_id}
                title={displayName(m.members)}
              >
                <input
                  type="radio"
                  name="paidBy"
                  value={m.member_id}
                  checked={paidBy === m.member_id}
                  onChange={() => setPaidBy(m.member_id)}
                />
                <Avatar url={avatarUrl(m.members)} initials={initials(m.members)} size={44} />
                <span>{displayName(m.members)}</span>
              </label>
            ))}
          </div>
        </fieldset>

        <div className="field">
          <div className="split-header">
            <label>{t('AddExpense_Split')}</label>
            {!isSettlement && (
              <div className="split-mode" role="group" aria-label={t('AddExpense_Split')}>
                <button
                  type="button"
                  className={!isManualSplit ? 'active' : ''}
                  aria-pressed={!isManualSplit}
                  onClick={handleSplitEqually}
                >
                  {t('AddExpense_Equally')}
                </button>
                <button
                  type="button"
                  className={isManualSplit ? 'active' : ''}
                  aria-pressed={isManualSplit}
                  onClick={() => {
                    if (!isManualSplit)
                      setShareInputs(
                        Object.fromEntries(
                          includedIds.map((id) => [id, formatCents(shareCents[id])]),
                        ),
                      )
                    setIsManualSplit(true)
                  }}
                >
                  {t('AddExpense_Manually')}
                </button>
              </div>
            )}
          </div>
          <div className="participant-list">
            {members.map((m) => {
              const included = participants.has(m.member_id)
              return (
                <div key={m.member_id} className="participant-item">
                  <label className="participant-name">
                    <input
                      type="checkbox"
                      checked={included}
                      onChange={() => toggleParticipant(m.member_id)}
                    />
                    <Avatar url={avatarUrl(m.members)} initials={initials(m.members)} size={28} />
                    <span>{displayName(m.members)}</span>
                  </label>
                  {!isSettlement && included && (
                    <input
                      className="share-input"
                      type="number"
                      inputMode="decimal"
                      step="0.01"
                      min="0"
                      aria-label={displayName(m.members)}
                      placeholder="0.00"
                      value={
                        useEqualSplit
                          ? formatCents(shareCents[m.member_id])
                          : (shareInputs[m.member_id] ?? '')
                      }
                      onChange={(e) => handleShareChange(m.member_id, e.target.value)}
                    />
                  )}
                </div>
              )
            })}
          </div>
          {!isSettlement && (
            <p className={`split-remaining${remainingCents !== 0 ? ' split-remaining-off' : ''}`}>
              {t('AddExpense_Remaining', formatCents(remainingCents), currency)}
            </p>
          )}
        </div>

        {!isSettlement && (
          <div className="field">
            <label htmlFor="category">{t('AddExpense_Category')}</label>
            <select
              id="category"
              value={category}
              onChange={(e) => setCategory(e.target.value as typeof category)}
            >
              {CATEGORY_KEYS.map((key) => (
                <option key={key} value={key}>
                  {t(`Category_${key}`)}
                </option>
              ))}
            </select>
          </div>
        )}

        {canToggleRecurring && (
          <label className="repeat-toggle">
            <input
              type="checkbox"
              checked={isRecurringMode}
              onChange={(e) => setIsRecurringMode(e.target.checked)}
            />
            {t('AddExpense_Repeat')}
          </label>
        )}

        {isRecurringMode ? (
          <>
            <div className="field">
              <label htmlFor="startDate">{t('AddExpense_StartDateLabel')}</label>
              <DateField id="startDate" required value={startDate} onChange={setStartDate} />
            </div>
            <div className="field">
              <label htmlFor="frequency">{t('AddExpense_Frequency')}</label>
              <select
                id="frequency"
                value={frequency}
                onChange={(e) => setFrequency(e.target.value as typeof frequency)}
              >
                {RECURRING_FREQUENCIES.map((f) => (
                  <option key={f} value={f}>
                    {t(`Recurring_Frequency_${f}`)}
                  </option>
                ))}
              </select>
            </div>
          </>
        ) : (
          <div className="field">
            <label htmlFor="occurred">{t('AddExpense_DateLabel')}</label>
            <DateField id="occurred" required value={occurredOn} onChange={setOccurredOn} />
          </div>
        )}

        {!isRecurringMode && !isSettlement && (
          <div className="field">
            <label>{t('AddExpense_ReceiptPhoto')}</label>
            {receiptUrl && (
              <a href={receiptUrl} target="_blank" rel="noreferrer" className="receipt-preview">
                <img src={receiptUrl} alt="" />
              </a>
            )}
            <div className="receipt-actions">
              <button
                type="button"
                className="btn btn-outline receipt-upload-btn"
                disabled={receiptBusy}
                onClick={() => receiptInputRef.current?.click()}
              >
                {receiptBusy
                  ? t('Common_Saving')
                  : receiptPath
                    ? t('AddExpense_ChangePhoto')
                    : t('AddExpense_AddPhoto')}
              </button>
              <input
                ref={receiptInputRef}
                type="file"
                accept="image/*"
                onChange={handleReceiptChange}
                disabled={receiptBusy}
                hidden
              />
              {receiptPath && (
                <button
                  type="button"
                  className="btn btn-outline"
                  onClick={handleRemoveReceipt}
                  disabled={receiptBusy}
                >
                  {t('AddExpense_RemovePhoto')}
                </button>
              )}
            </div>
          </div>
        )}

        {error && (
          <p className="error-text" role="alert">
            {error}
          </p>
        )}

        <button type="submit" className="btn btn-primary" disabled={busy || receiptBusy}>
          {busy ? t('Common_Saving') : t('AddExpense_SaveExpense')}
        </button>

        {isEditMode && canDelete && (
          <button
            type="button"
            className="btn btn-danger delete-btn"
            onClick={handleDelete}
            disabled={busy}
          >
            {t('AddExpense_DeleteExpense')}
          </button>
        )}
      </form>
    </div>
  )
}
