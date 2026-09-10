import { useEffect, useState, type ChangeEvent, type FormEvent } from 'react'
import { useLocation, useNavigate, useParams } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { useAuth } from '../context/AuthContext'
import { useAliases } from '../context/AliasesContext'
import { useLocale } from '../context/LocaleContext'
import { resizeImageToWebp } from '../lib/imageResize'
import { CATEGORY_KEYS, CURRENCIES, RECURRING_FREQUENCIES, type Group, type MemberRow } from '../lib/types'
import { AppHeader } from '../components/AppHeader'
import { DateField } from '../components/DateField'
import './AddExpensePage.css'

const RECEIPTS_BUCKET = 'receipts'

export function AddExpensePage() {
  const { groupId, expenseId, recurringId } = useParams<{ groupId: string; expenseId?: string; recurringId?: string }>()
  const location = useLocation()
  const isRecurringRoute = location.pathname.includes('/recurring/')
  const { session } = useAuth()
  const { displayName } = useAliases()
  const { t } = useLocale()
  const navigate = useNavigate()

  const [group, setGroup] = useState<Group | null>(null)
  const [members, setMembers] = useState<MemberRow[]>([])
  const [description, setDescription] = useState('')
  const [amount, setAmount] = useState('')
  const [currency, setCurrency] = useState('EUR')
  const [category, setCategory] = useState<(typeof CATEGORY_KEYS)[number] | ''>('food')
  const [occurredOn, setOccurredOn] = useState(() => new Date().toISOString().slice(0, 10))
  const [startDate, setStartDate] = useState(() => new Date().toISOString().slice(0, 10))
  const [frequency, setFrequency] = useState<(typeof RECURRING_FREQUENCIES)[number]>('monthly')
  const [paidBy, setPaidBy] = useState('')
  const [participants, setParticipants] = useState<Set<string>>(new Set())
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const [isEditMode, setIsEditMode] = useState(false)
  const [isRecurringMode, setIsRecurringMode] = useState(isRecurringRoute)
  const [canToggleRecurring] = useState(!expenseId && !recurringId)
  const [isSettlement, setIsSettlement] = useState(false)

  const [receiptPath, setReceiptPath] = useState<string | null>(null)
  const [receiptUrl, setReceiptUrl] = useState<string | null>(null)
  const [receiptBusy, setReceiptBusy] = useState(false)

  // Carried through unchanged on update — never re-derived — so editing a
  // template/expense's amount/split/category can't reset its schedule or
  // silently drop who created it. Same footgun class documented on the
  // MAUI side (AddExpenseViewModel's editingCreatedBy/editingLastProcessedDate).
  const [editingMeta, setEditingMeta] = useState<{
    createdBy: string | null
    createdAt: string | null
    lastProcessedDate: string | null
    isActive: boolean
  }>({ createdBy: null, createdAt: null, lastProcessedDate: null, isActive: true })

  useEffect(() => {
    if (!groupId) return
    ;(async () => {
      const [groupRes, membersRes] = await Promise.all([
        supabase.from('groups').select('id, name, currency, created_by').eq('id', groupId).single(),
        supabase.from('group_members').select('member_id, members(id, display_name, account_id, avatar_path)').eq('group_id', groupId),
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
          supabase.from('expense_shares').select('member_id').eq('expense_id', expenseId),
        ])
        if (expenseRes.error) return setError(expenseRes.error.message)
        const expense = expenseRes.data
        setDescription(expense.description ?? '')
        setAmount(String(expense.amount))
        setCurrency(expense.currency)
        setCategory((expense.category || '') as typeof category)
        setOccurredOn(expense.occurred_at.slice(0, 10))
        setPaidBy(expense.paid_by_member_id)
        setIsSettlement(expense.is_settlement)
        setReceiptPath(expense.receipt_path)
        setEditingMeta({
          createdBy: expense.created_by,
          createdAt: expense.created_at,
          lastProcessedDate: null,
          isActive: true,
        })
        const shareIds = new Set<string>((sharesRes.data ?? []).map((s: { member_id: string }) => s.member_id))
        setParticipants(shareIds)
        if (expense.receipt_path) {
          const { data: signed } = await supabase.storage
            .from(RECEIPTS_BUCKET)
            .createSignedUrl(expense.receipt_path, 3600)
          setReceiptUrl(signed?.signedUrl ?? null)
        }
      } else if (recurringId) {
        setIsEditMode(true)
        setIsRecurringMode(true)
        const [templateRes, sharesRes] = await Promise.all([
          supabase.from('recurring_expenses').select('*').eq('id', recurringId).single(),
          supabase.from('recurring_expense_shares').select('member_id').eq('recurring_expense_id', recurringId),
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
        setEditingMeta({
          createdBy: tpl.created_by,
          createdAt: tpl.created_at,
          lastProcessedDate: tpl.last_processed_date,
          isActive: tpl.is_active,
        })
        const shareIds = new Set<string>((sharesRes.data ?? []).map((s: { member_id: string }) => s.member_id))
        setParticipants(shareIds)
      } else {
        setParticipants(new Set(memberRows.map((m) => m.member_id)))
        setCurrency(groupData.currency)
        const myRow = memberRows.find((m) => m.members.account_id === session?.user.id)
        if (myRow) setPaidBy(myRow.member_id)
        else if (memberRows[0]) setPaidBy(memberRows[0].member_id)
      }
    })()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [groupId, expenseId, recurringId, session])

  function toggleParticipant(memberId: string) {
    setParticipants((prev) => {
      const next = new Set(prev)
      if (next.has(memberId)) next.delete(memberId)
      else next.add(memberId)
      return next
    })
  }

  function equalShares(total: number) {
    const ids = Array.from(participants)
    const share = Math.round((total / ids.length) * 100) / 100
    const shares = ids.map((memberId) => ({ member_id: memberId, share_amount: share }))
    const roundingError = Math.round((total - share * ids.length) * 100) / 100
    if (roundingError !== 0 && shares.length > 0) shares[shares.length - 1].share_amount += roundingError
    return shares
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

      const previousPath = receiptPath
      if (previousPath) await supabase.storage.from(RECEIPTS_BUCKET).remove([previousPath])

      setReceiptPath(path)
      const { data: signed } = await supabase.storage.from(RECEIPTS_BUCKET).createSignedUrl(path, 3600)
      setReceiptUrl(signed?.signedUrl ?? null)
    } catch (err) {
      setError(err instanceof Error ? err.message : t('Common_Error'))
    } finally {
      setReceiptBusy(false)
    }
  }

  async function handleRemoveReceipt() {
    if (!receiptPath) return
    await supabase.storage.from(RECEIPTS_BUCKET).remove([receiptPath])
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

    setBusy(true)

    if (isRecurringMode) {
      const templatePayload = {
        group_id: groupId,
        paid_by_member_id: paidBy,
        amount: total,
        currency,
        description,
        category,
        frequency,
        start_date: startDate,
        last_processed_date: editingMeta.lastProcessedDate,
        is_active: recurringId ? editingMeta.isActive : true,
        ...(recurringId ? { created_by: editingMeta.createdBy, created_at: editingMeta.createdAt } : {}),
      }

      const { data: template, error: templateError } = recurringId
        ? await supabase.from('recurring_expenses').update(templatePayload).eq('id', recurringId).select('id').single()
        : await supabase.from('recurring_expenses').insert(templatePayload).select('id').single()

      if (templateError) {
        setError(templateError.message)
        setBusy(false)
        return
      }

      if (recurringId) await supabase.from('recurring_expense_shares').delete().eq('recurring_expense_id', recurringId)
      const shares = equalShares(total).map((s) => ({ ...s, recurring_expense_id: template.id }))
      const { error: sharesError } = await supabase.from('recurring_expense_shares').insert(shares)
      setBusy(false)
      if (sharesError) return setError(sharesError.message)
      navigate(`/groups/${groupId}/recurring`)
      return
    }

    const expensePayload = {
      group_id: groupId,
      paid_by_member_id: paidBy,
      amount: total,
      currency,
      description,
      category: isSettlement ? '' : category,
      occurred_at: new Date(occurredOn).toISOString(),
      receipt_path: receiptPath,
      is_settlement: isSettlement,
      ...(expenseId ? { created_by: editingMeta.createdBy, created_at: editingMeta.createdAt } : {}),
    }

    const { data: expense, error: expenseError } = expenseId
      ? await supabase.from('expenses').update(expensePayload).eq('id', expenseId).select('id').single()
      : await supabase.from('expenses').insert(expensePayload).select('id').single()

    if (expenseError) {
      setError(expenseError.message)
      setBusy(false)
      return
    }

    if (expenseId) await supabase.from('expense_shares').delete().eq('expense_id', expenseId)
    const shares = equalShares(total).map((s) => ({ ...s, expense_id: expense.id }))
    const { error: sharesError } = await supabase.from('expense_shares').insert(shares)
    setBusy(false)
    if (sharesError) return setError(sharesError.message)

    navigate(`/groups/${groupId}`)
  }

  async function handleDelete() {
    if (!window.confirm(t('AddExpense_DeleteConfirm'))) return
    setBusy(true)
    const { error } = recurringId
      ? await supabase.from('recurring_expenses').delete().eq('id', recurringId)
      : await supabase.from('expenses').delete().eq('id', expenseId)
    setBusy(false)
    if (error) return setError(error.message)
    navigate(recurringId ? `/groups/${groupId}/recurring` : `/groups/${groupId}`)
  }

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
      <div className="page">
        <AppHeader title={title} back />
        {error ? <p className="error-text">{error}</p> : <div className="spinner">{t('Common_Loading')}</div>}
      </div>
    )
  }

  return (
    <div className="page">
      <AppHeader title={title} back />

      <form onSubmit={handleSubmit}>
        <div className="field">
          <label htmlFor="description">{t('AddExpense_DescriptionLabel')}</label>
          <input
            id="description"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder={t('AddExpense_DescriptionPlaceholder')}
          />
        </div>

        <div className="amount-row">
          <div className="field amount-field">
            <label htmlFor="amount">{t('AddExpense_AmountLabel')}</label>
            <input
              id="amount"
              type="number"
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

        {!isSettlement && (
          <div className="field">
            <label htmlFor="category">{t('AddExpense_Category')}</label>
            <select id="category" value={category} onChange={(e) => setCategory(e.target.value as typeof category)}>
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
              <select id="frequency" value={frequency} onChange={(e) => setFrequency(e.target.value as typeof frequency)}>
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

        <div className="field">
          <label htmlFor="paidBy">{t('AddExpense_PaidBy')}</label>
          <select id="paidBy" value={paidBy} onChange={(e) => setPaidBy(e.target.value)}>
            {members.map((m) => (
              <option key={m.member_id} value={m.member_id}>
                {displayName(m.members)}
              </option>
            ))}
          </select>
        </div>

        <div className="field">
          <label>{t('AddExpense_SplitEquallyBetween')}</label>
          <div className="participant-list">
            {members.map((m) => (
              <label key={m.member_id} className="participant-item">
                <input
                  type="checkbox"
                  checked={participants.has(m.member_id)}
                  onChange={() => toggleParticipant(m.member_id)}
                />
                {displayName(m.members)}
              </label>
            ))}
          </div>
        </div>

        {!isRecurringMode && !isSettlement && (
          <div className="field">
            <label>{t('AddExpense_ReceiptPhoto')}</label>
            {receiptUrl && (
              <a href={receiptUrl} target="_blank" rel="noreferrer" className="receipt-preview">
                <img src={receiptUrl} alt="" />
              </a>
            )}
            <div className="receipt-actions">
              <label className="btn btn-outline receipt-upload-btn">
                {receiptBusy ? t('Common_Saving') : receiptPath ? t('AddExpense_ChangePhoto') : t('AddExpense_AddPhoto')}
                <input type="file" accept="image/*" onChange={handleReceiptChange} disabled={receiptBusy} hidden />
              </label>
              {receiptPath && (
                <button type="button" className="btn btn-outline" onClick={handleRemoveReceipt} disabled={receiptBusy}>
                  {t('AddExpense_RemovePhoto')}
                </button>
              )}
            </div>
          </div>
        )}

        {error && <p className="error-text">{error}</p>}

        <button type="submit" className="btn btn-primary" disabled={busy}>
          {busy ? t('Common_Saving') : t('AddExpense_SaveExpense')}
        </button>

        {isEditMode && (
          <button type="button" className="btn btn-danger delete-btn" onClick={handleDelete} disabled={busy}>
            {t('AddExpense_DeleteExpense')}
          </button>
        )}
      </form>
    </div>
  )
}
