import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import { useLocation, useParams, useSearchParams } from 'react-router-dom'
import { useGoBackTo } from '../lib/navigation'
import { supabase } from '../lib/supabaseClient'
import { amountDbErrorKey } from '../lib/dbErrors'
import { useAuth } from '../context/AuthContext'
import { useAliases } from '../context/AliasesContext'
import { useLocale } from '../context/LocaleContext'
import { resizeImageToWebp } from '../lib/imageResize'
import { editableEqualSplitCents, restoredSplitMode } from '../lib/splitMode'
import { expenseTimestamp, isCalendarDate, localDateInput } from '../lib/dates'
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
import { SettlementForm } from '../components/SettlementForm'
import type { SettlementDraft } from '../lib/settlements'
import { settlementEditorSnapshot } from '../lib/formEditorSnapshots'
import { amountErrorKey, amountHintKey, parseAmountInput } from '../lib/amountInput'
import { fromMinor, minorText, minorUnits, toMinor } from '../lib/currencyUnits'
import { expenseEditorSnapshot } from '../lib/expenseEditorSnapshot'
import { useUnsavedChanges } from '../lib/useUnsavedChanges'
import { UnsavedChangesDialog } from '../components/UnsavedChangesDialog'
import './AddExpensePage.css'

const RECEIPTS_BUCKET = 'receipts'

const formatMinor = (minor: number, currency: string) => minorText(minor, currency)

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
  const { t, language } = useLocale()
  const goBackTo = useGoBackTo()
  const editorKey = `${session?.user.id ?? ''}:${location.pathname}`

  const [group, setGroup] = useState<Group | null>(null)
  const [members, setMembers] = useState<MemberRow[]>([])
  const [description, setDescription] = useState('')
  const [amount, setAmount] = useState('')
  const [showAmountError, setShowAmountError] = useState(false)
  const [shareTouched, setShareTouched] = useState<Record<string, boolean>>({})
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
  // Only explicit saved intent restores Equal; legacy records remain Manual.
  const [isManualSplit, setIsManualSplit] = useState(false)
  const [savedEqualShares, setSavedEqualShares] = useState<Record<string, number> | null>(null)
  const [shareInputs, setShareInputs] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const [isEditMode, setIsEditMode] = useState(false)
  // Server rule (enforce_expense_delete_permission): creator, payer or group owner. Templates aren't restricted.
  const [canDelete, setCanDelete] = useState(false)
  const [isRecurringMode, setIsRecurringMode] = useState(isRecurringRoute)
  const [canToggleRecurring] = useState(!expenseId && !recurringId)
  const [isSettlement, setIsSettlement] = useState(false)
  const [settlementDraft, setSettlementDraft] = useState<SettlementDraft | null>(null)
  const [expenseLoaded, setExpenseLoaded] = useState(false)
  const [loadedEditorKey, setLoadedEditorKey] = useState<string | null>(null)

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
    let cancelled = false
    ;(async () => {
      setExpenseLoaded(false)
      setSavedEqualShares(null)
      originalOccurredAt.current = null;
      const [groupRes, membersRes] = await Promise.all([
        supabase.from('groups').select('id, name, currency, created_by').eq('id', groupId).single(),
        supabase
          .from('group_members')
          .select('member_id, members(id, display_name, account_id, avatar_path)')
          .eq('group_id', groupId),
      ])
      if (cancelled) return
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
        if (cancelled) return
        if (expenseRes.error) return setError(expenseRes.error.message)
        if (sharesRes.error) return setError(sharesRes.error.message)
        const expense = expenseRes.data
        if (expense.is_settlement) setSettlementDraft({
          id: expense.id,
          payerId: expense.paid_by_member_id,
          recipientId: sharesRes.data?.[0]?.member_id ?? '',
          amount: String(expense.amount),
          currency: expense.currency,
          note: expense.description ?? '',
          date: localDateInput(expense.occurred_at),
          originalOccurredAt: expense.occurred_at,
          receiptPath: expense.receipt_path,
          eventId: expense.event_id,
        })
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
        loadShares(sharesRes.data ?? [], expense.currency)
        // A settlement is always one share for the full amount — keep it derived.
        setIsManualSplit(!expense.is_settlement && restoredSplitMode(expense.split_mode) === 'manual')
        if (expense.receipt_path) {
          const { data: signed } = await supabase.storage
            .from(RECEIPTS_BUCKET)
            .createSignedUrl(expense.receipt_path, 3600)
          if (cancelled) return
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
        if (cancelled) return
        if (templateRes.error) return setError(templateRes.error.message)
        if (sharesRes.error) return setError(sharesRes.error.message)
        const tpl = templateRes.data
        setDescription(tpl.description ?? '')
        setAmount(String(tpl.amount))
        setCurrency(tpl.currency)
        setCategory((tpl.category || '') as typeof category)
        setStartDate(tpl.start_date)
        setFrequency(tpl.frequency)
        setPaidBy(tpl.paid_by_member_id)
        loadShares(sharesRes.data ?? [], tpl.currency)
        setIsManualSplit(restoredSplitMode(tpl.split_mode) === 'manual')
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
          if (cancelled) return
          setLinkedEventTitle(eventRow?.title ?? null)
          const goingIds = new Set((goingRows ?? []).map((r: { member_id: string }) => r.member_id))
          setParticipants(
            goingIds.size > 0 ? goingIds : new Set(memberRows.map((m) => m.member_id)),
          )
        } else {
          setParticipants(new Set(memberRows.map((m) => m.member_id)))
        }
      }
      if (cancelled) return
      setLoadedEditorKey(editorKey)
      setExpenseLoaded(true)
    })()
    return () => { cancelled = true }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [groupId, expenseId, recurringId, editorKey])

  function loadShares(rows: { member_id: string; share_amount: number }[], rowCurrency: string) {
    setSavedEqualShares(Object.fromEntries(rows.map((s) => [s.member_id, toMinor(s.share_amount, rowCurrency)])))
    setParticipants(new Set(rows.map((s) => s.member_id)))
    setShareInputs(
      Object.fromEntries(rows.map((s) => [s.member_id, Number(s.share_amount).toLocaleString(language === 'es' ? 'es-ES' : 'en-US', {
        useGrouping: false, minimumFractionDigits: minorUnits(rowCurrency), maximumFractionDigits: minorUnits(rowCurrency),
      })])),
    )
  }

  // Member-list order, so "last participant" (who absorbs the leftover cent) is stable.
  const includedIds = members.map((m) => m.member_id).filter((id) => participants.has(id))
  const parsedAmount = parseAmountInput(amount, currency)
  const totalCents = parsedAmount.cents ?? 0
  const useEqualSplit = !isManualSplit || isSettlement
  const parsedShares = Object.fromEntries(includedIds.map((id) => [id, parseAmountInput(shareInputs[id] ?? '', currency)]))
  const shareCents: Record<string, number> = useEqualSplit
    ? editableEqualSplitCents(totalCents, includedIds, savedEqualShares)
    : Object.fromEntries(includedIds.map((id) => [id, parsedShares[id].cents ?? 0]))
  const remainingCents = totalCents - includedIds.reduce((sum, id) => sum + shareCents[id], 0)
  const shareInputsValid = useEqualSplit || includedIds.every((id) => parsedShares[id].cents !== null)
  const isSplitValid = parsedAmount.cents !== null && shareInputsValid && remainingCents === 0
    && includedIds.every((id) => shareCents[id] > 0)
  const displayCents = (cents: number) => formatMinor(cents, currency).replace('.', language === 'es' ? ',' : '.')

  const unsaved = useUnsavedChanges(editorKey,
    expenseLoaded && loadedEditorKey === editorKey && !isSettlement ? expenseEditorSnapshot({
      amount, description, currency, category, paidBy, includedIds, manual: isManualSplit,
      shareInputs, shareCents, recurring: isRecurringMode, occurredOn, startDate, frequency,
      receiptPath, eventId: linkedEventId,
    }) : expenseLoaded && loadedEditorKey === editorKey && isSettlement && settlementDraft
      ? settlementEditorSnapshot(settlementDraft) : null, busy || receiptBusy)

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
    setError(null)
    setShareTouched((previous) => ({ ...previous, [memberId]: false }))
    // First manual edit snapshots the current equal split so the other
    // participants keep their amounts instead of dropping to empty.
    const base = isManualSplit
      ? shareInputs
      : Object.fromEntries(includedIds.map((id) => [id, displayCents(shareCents[id])]))
    setShareInputs({ ...base, [memberId]: value })
    setIsManualSplit(true)
  }

  function handleSplitEqually() {
    setError(null)
    setSavedEqualShares(null)
    setIsManualSplit(false)
    setShareInputs({})
  }

  function buildShares() {
    return includedIds.map((id) => ({ member_id: id, share_amount: fromMinor(shareCents[id], currency) }))
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

    if (parsedAmount.cents === null) {
      setShowAmountError(true)
      setError(t(amountErrorKey(parsedAmount.error, currency)))
      return
    }
    const total = fromMinor(parsedAmount.cents, currency)
    if (participants.size === 0) {
      setError(t('AddExpense_PickParticipant'))
      return
    }
    if (!paidBy) {
      setError(t('AddExpense_PickPayer'))
      return
    }
    if (!isSplitValid) {
      setShareTouched(Object.fromEntries(includedIds.map((id) => [id, true])))
      setError(t('AddExpense_SplitInvalid'))
      return
    }

    if (!isCalendarDate(isRecurringMode ? startDate : occurredOn)) {
      setError(t('DateInput_Invalid'))
      return
    }
    let occurredAt: string | null = null
    if (!isRecurringMode) {
      try {
        occurredAt = expenseTimestamp(occurredOn, originalOccurredAt.current)
      } catch {
        setError(t('DateInput_Invalid'))
        return
      }
    }
    setBusy(true)
    try {
      const { error: schemaError } = await supabase
        .from(isRecurringMode ? 'recurring_expenses' : 'expenses').select('split_mode').limit(0)
      if (schemaError) return setError(t('AddExpense_SaveUnavailable'))
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
            split_mode: isManualSplit ? 'manual' : 'equal',
            start_date: startDate,
          },
          p_shares: buildShares(),
        })
        if (saveError) return setError(amountDbErrorKey(saveError) ? t(amountDbErrorKey(saveError)!) : saveError.message)
        unsaved.markSaved()
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
          occurred_at: occurredAt,
          receipt_path: receiptPath,
          is_settlement: isSettlement,
          split_mode: isSettlement ? null : isManualSplit ? 'manual' : 'equal',
          event_id: linkedEventId,
        },
        p_shares: buildShares(),
      })
      if (saveError) return setError(amountDbErrorKey(saveError) ? t(amountDbErrorKey(saveError)!) : saveError.message)

      unsaved.markSaved()
      goBackTo(parentPath)
    } catch (error) {
      setError(t(amountDbErrorKey(error) ?? 'Common_SaveFailed'))
    } finally {
      setBusy(false)
    }
  }

  async function handleDelete() {
    if (!window.confirm(t('AddExpense_DeleteConfirm'))) return
    setBusy(true)
    const { error } = recurringId
      ? await supabase.from('recurring_expenses').delete().eq('id', recurringId)
      : await supabase.from('expenses').delete().eq('id', expenseId)
    setBusy(false)
    if (error) return setError(error.message)
    unsaved.markSaved()
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

  if (!group || !expenseLoaded || loadedEditorKey !== editorKey) {
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

  if (isSettlement && settlementDraft) {
    return (
      <div className="page expense-page repayment-page">
        <AppHeader title={t('AddExpense_EditSettlementTitle')} backTo={parentPath} />
        <p className="editor-context">{group.name} <span aria-hidden="true">·</span> {group.currency}</p>
        <UnsavedChangesDialog blocker={unsaved.blocker} keepEditing={unsaved.keepEditing}
          pending={busy} open={unsaved.confirmingClose} onDiscard={unsaved.discardChanges} />
        <div className="settlement-editor-card">
          <SettlementForm
            key={settlementDraft.id}
            initial={settlementDraft}
            groupId={group.id}
            members={members}
            canDelete={canDelete}
            onDraftChange={setSettlementDraft}
            onBusyChange={setBusy}
            onCommitted={unsaved.markSaved}
            onCancel={() => unsaved.requestClose(() => goBackTo(parentPath))}
            onSaved={() => goBackTo(parentPath)}
            onDeleted={() => goBackTo(parentPath)}
          />
        </div>
      </div>
    )
  }

  return (
    <div className="page page-wide expense-page">
      <AppHeader title={title} backTo={parentPath} />
      <p className="editor-context">{group.name} <span aria-hidden="true">·</span> {group.currency}</p>

      <UnsavedChangesDialog blocker={unsaved.blocker} keepEditing={unsaved.keepEditing} pending={busy || receiptBusy}
        open={unsaved.confirmingClose} onDiscard={unsaved.discardChanges} />
      <form onSubmit={handleSubmit} className="expense-form">
        <fieldset className="expense-fields expense-layout" disabled={busy}>
          <section className="expense-section expense-details" aria-labelledby="expense-details-heading">
            <h2 id="expense-details-heading">{t('AddExpense_DetailsTitle')}</h2>
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
                  type="text"
                  inputMode="decimal"
                  placeholder={minorUnits(currency) === 0 ? '0' : '0.00'}
                  required
                  value={amount}
                  aria-invalid={showAmountError && parsedAmount.cents === null}
                  aria-describedby="amount-format-hint amount-error"
                  onBlur={() => setShowAmountError(true)}
                  onChange={(e) => { setAmount(e.target.value); setShowAmountError(false); setError(null) }}
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

            <p id="amount-format-hint" className="amount-format-hint">{t(amountHintKey(currency))}</p>
            <p id="amount-error" className="error-text" role="status">
              {showAmountError && parsedAmount.cents === null ? t(amountErrorKey(parsedAmount.error, currency)) : ''}
            </p>

            <div className="field">
              <label htmlFor="description">{t('AddExpense_DescriptionLabel')}</label>
              <input
                id="description"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                placeholder={t('AddExpense_DescriptionPlaceholder')}
              />
            </div>

          </section>

          <section className="expense-section expense-people" aria-labelledby="expense-people-heading">
            <h2 id="expense-people-heading">{t('AddExpense_PeopleTitle')}</h2>
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
                    <Avatar url={avatarUrl(m.members)} initials={initials(m.members)} seed={m.member_id} size={32} />
                    <span>{displayName(m.members)}</span>
                  </label>
                ))}
              </div>
            </fieldset>

            {parsedAmount.cents === null && <p className="expense-split-hint">{t('AddExpense_SplitNeedsAmount')}</p>}
            {parsedAmount.cents !== null && <div className="field">
              <div className="split-header">
                <h3>{t('AddExpense_Split')}</h3>
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
                              includedIds.map((id) => [id, displayCents(shareCents[id])]),
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
                        <Avatar url={avatarUrl(m.members)} initials={initials(m.members)} seed={m.member_id} size={28} />
                        <span>{displayName(m.members)}</span>
                      </label>
                      {!isSettlement && included && (
                        <input
                          className="share-input"
                          type="text"
                          inputMode="decimal"
                          aria-label={t('AmountInput_ShareLabel', displayName(m.members))}
                          aria-invalid={!useEqualSplit && !!shareTouched[m.member_id] && parsedShares[m.member_id]?.cents === null}
                          aria-describedby={!useEqualSplit && shareTouched[m.member_id] && parsedShares[m.member_id]?.cents === null ? `share-error-${m.member_id}` : undefined}
                          placeholder={minorUnits(currency) === 0 ? '0' : '0.00'}
                          value={
                            useEqualSplit
                              ? displayCents(shareCents[m.member_id])
                              : (shareInputs[m.member_id] ?? '')
                          }
                          onChange={(e) => handleShareChange(m.member_id, e.target.value)}
                          onBlur={() => setShareTouched((previous) => ({ ...previous, [m.member_id]: true }))}
                        />
                      )}
                      {!useEqualSplit && included && shareTouched[m.member_id] && parsedShares[m.member_id]?.cents === null && (
                        <p id={`share-error-${m.member_id}`} className="error-text share-error" role="status">
                          {t(amountErrorKey(parsedShares[m.member_id].error, currency))}
                        </p>
                      )}
                    </div>
                  )
                })}
              </div>
              {!isSettlement && (
                <p className={`split-remaining${!shareInputsValid || remainingCents !== 0 ? ' split-remaining-off' : ''}`}>
                  {shareInputsValid ? t('AddExpense_Remaining', displayCents(remainingCents), currency) : t('AmountInput_IncompleteSplit')}
                </p>
              )}
            </div>}

          </section>

          <section className="expense-section expense-extras" aria-labelledby="expense-extras-heading">
            <h2 id="expense-extras-heading">{t('AddExpense_ExtrasTitle')}</h2>
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
                    <img src={receiptUrl} alt={t('AddExpense_ReceiptPhoto')} />
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

          </section>

          <footer className="expense-form-footer">
            {error && (
              <p className="error-text" role="alert">
                {error}
              </p>
            )}

            <div className="expense-form-actions">
            <button type="button" className="btn btn-outline" disabled={busy || receiptBusy}
              onClick={() => unsaved.requestClose(() => goBackTo(parentPath))}>
              {t('Common_Cancel')}
            </button>
            <button type="submit" className="btn btn-primary" disabled={busy || receiptBusy || !isSplitValid}>
              {busy ? t('Common_Saving') : t('AddExpense_SaveExpense')}
            </button>
            </div>

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
          </footer>
        </fieldset>
      </form>
    </div>
  )
}
