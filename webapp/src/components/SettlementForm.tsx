import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useLocale } from '../context/LocaleContext'
import { useAliases } from '../context/AliasesContext'
import { supabase } from '../lib/supabaseClient'
import { amountDbErrorKey } from '../lib/dbErrors'
import { expenseTimestamp } from '../lib/dates'
import { formatMoney } from '../lib/formatting'
import { CURRENCIES, type MemberRow } from '../lib/types'
import { isSettlementDate, settlementCents, settlementPayload, type SettlementDraft } from '../lib/settlements'
import { amountErrorKey, parseAmountInput } from '../lib/amountInput'
import { toMinor } from '../lib/currencyUnits'
import './SettlementForm.css'

export function SettlementForm({
  initial, groupId, members, myMemberId, expectedBalance, otherMemberId,
  canDelete = false, onCancel, onSaved, onDeleted, onBusyChange,
  onDraftChange, onCommitted,
}: {
  initial: SettlementDraft
  groupId: string
  members: MemberRow[]
  myMemberId?: string
  expectedBalance?: number
  otherMemberId?: string
  canDelete?: boolean
  onCancel: () => void
  onSaved: (remaining: number | null) => void
  onDeleted?: () => void
  onBusyChange?: (busy: boolean) => void
  onDraftChange: (draft: SettlementDraft) => void
  onCommitted: () => void
}) {
  const { t, language } = useLocale()
  const { displayName } = useAliases()
  const [draft, setDraft] = useState(initial)
  useEffect(() => { onDraftChange(draft) }, [draft, onDraftChange])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const inFlight = useRef(false)
  const isEdit = !!initial.id
  const name = (id: string) => {
    const member = members.find((m) => m.member_id === id)
    return member ? displayName(member.members) : t('GroupDetail_SomeoneCapitalized')
  }
  const counterparty = draft.payerId === myMemberId ? draft.recipientId
    : draft.recipientId === myMemberId ? draft.payerId : draft.recipientId
  const placeholder = t('Settlement_DefaultDescription', name(counterparty))
  const update = <K extends keyof SettlementDraft>(key: K, value: SettlementDraft[K]) =>
    setDraft((previous) => ({ ...previous, [key]: value }))

  function setSaving(value: boolean) {
    inFlight.current = value
    setBusy(value)
    onBusyChange?.(value)
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (inFlight.current) return
    setError(null)
    const cents = settlementCents(draft.amount, draft.currency)
    if (cents === null) return setError(t(amountErrorKey(parseAmountInput(draft.amount, draft.currency).error, draft.currency)))
    if (!isSettlementDate(draft.date)) return setError(t('Settlement_InvalidDate'))
    if (!draft.payerId || !draft.recipientId || draft.payerId === draft.recipientId ||
      !members.some((m) => m.member_id === draft.payerId) ||
      !members.some((m) => m.member_id === draft.recipientId))
      return setError(t('Settlement_InvalidParties'))
    if (expectedBalance !== undefined && cents > toMinor(Math.abs(expectedBalance), draft.currency))
      return setError(t('Settlement_ExceedsBalance'))

    setSaving(true)
    let saved = false
    try {
      if (!isEdit && otherMemberId && expectedBalance !== undefined) {
        const result = await supabase.from('my_pairwise_balances').select('balance')
          .eq('group_id', groupId).eq('other_member_id', otherMemberId).maybeSingle()
        if (result.error) throw result.error
        if (toMinor(result.data?.balance ?? 0, draft.currency) !== toMinor(expectedBalance, draft.currency)) {
          setError(t('Settlement_BalanceChanged'))
          return
        }
      }
      const result = await supabase.rpc('save_expense', settlementPayload(
        draft, groupId, placeholder, expenseTimestamp(draft.date, draft.originalOccurredAt ?? null),
      ))
      if (result.error) throw result.error
      saved = true
      onCommitted()
    } catch (error) {
      setError(t(amountDbErrorKey(error) ?? 'Settlement_SaveFailed'))
    } finally {
      if (!saved) setSaving(false)
    }
    if (saved) {
      let remaining: number | null = null
      try {
        if (otherMemberId) {
          const result = await supabase.from('my_pairwise_balances').select('balance')
            .eq('group_id', groupId).eq('other_member_id', otherMemberId).maybeSingle()
          if (!result.error) remaining = Number(result.data?.balance ?? 0)
        }
      } catch { /* The write succeeded; a refresh failure must not enable a second save. */ }
      onSaved(remaining)
    }
  }

  async function handleDelete() {
    if (inFlight.current || !initial.id || !canDelete || !window.confirm(t('Settlement_DeleteConfirm'))) return
    setError(null)
    setSaving(true)
    let deleted = false
    try {
      const result = await supabase.from('expenses').delete().eq('id', initial.id)
      if (result.error) throw result.error
      deleted = true
      onCommitted()
    } catch {
      setError(t('Settlement_DeleteFailed'))
    } finally {
      if (!deleted) setSaving(false)
    }
    if (deleted) onDeleted?.()
  }

  return (
    <form className="settlement-form" onSubmit={handleSubmit}>
      <p className="settlement-hint">{t('Settlement_Hint')}</p>
      <fieldset disabled={busy}>
        {isEdit ? (
          <div className="settlement-fields">
            <div className="field">
              <label htmlFor="settlement-payer">{t('Settlement_From')}</label>
              <select id="settlement-payer" value={draft.payerId} onChange={(e) => {
                const payerId = e.target.value
                setDraft((previous) => ({ ...previous, payerId,
                  recipientId: previous.recipientId === payerId ? '' : previous.recipientId }))
              }}>
                {members.map((m) => <option key={m.member_id} value={m.member_id}>{displayName(m.members)}</option>)}
              </select>
            </div>
            <div className="field">
              <label htmlFor="settlement-recipient">{t('Settlement_To')}</label>
              <select id="settlement-recipient" value={draft.recipientId} onChange={(e) => update('recipientId', e.target.value)}>
                <option value="" disabled>{t('Settlement_ChooseRecipient')}</option>
                {members.filter((m) => m.member_id !== draft.payerId).map((m) =>
                  <option key={m.member_id} value={m.member_id}>{displayName(m.members)}</option>)}
              </select>
            </div>
          </div>
        ) : (
          <div className="settlement-parties">
            <dl><dt>{t('Settlement_From')}</dt><dd>{name(draft.payerId)}</dd></dl>
            <span className="settlement-direction" aria-hidden="true">→</span>
            <dl><dt>{t('Settlement_To')}</dt><dd>{name(draft.recipientId)}</dd></dl>
          </div>
        )}
        <div className="settlement-fields">
          <div className="field">
            <label htmlFor="settlement-amount">{t('AddExpense_AmountLabel')} ({draft.currency})</label>
            <input id="settlement-amount" type="text" inputMode="decimal" required value={draft.amount}
              onChange={(e) => update('amount', e.target.value)} aria-describedby="settlement-amount-hint" />
            <p id="settlement-amount-hint" className="settlement-hint">{t('Settlement_PartialHint')}</p>
          </div>
          <div className="field">
            <label htmlFor="settlement-date">{t('AddExpense_DateLabel')}</label>
            <input id="settlement-date" type="date" required value={draft.date} onChange={(e) => update('date', e.target.value)} />
          </div>
        </div>
        {isEdit && <div className="field">
          <label htmlFor="settlement-currency">{t('AddExpense_Currency')}</label>
          <select id="settlement-currency" value={draft.currency} onChange={(e) => update('currency', e.target.value)}>
            {CURRENCIES.map((currency) => <option key={currency} value={currency}>{currency}</option>)}
          </select>
        </div>}
        <div className="field">
          <label htmlFor="settlement-note">{t('Settlement_Note')}</label>
          <input id="settlement-note" value={draft.note} placeholder={placeholder} onChange={(e) => update('note', e.target.value)} />
        </div>
        {error && <p className="error-text" role="alert">{error}</p>}
        <div className="settlement-footer">
          {expectedBalance !== undefined && <p className="settlement-hint settlement-outstanding">
            {t('Settlement_Outstanding', formatMoney(Math.abs(expectedBalance), initial.currency, language))}
          </p>}
          <div className="settlement-actions">
            <button type="button" className="btn btn-outline" onClick={onCancel}>{t('Common_Cancel')}</button>
            <button type="submit" className="btn btn-primary">{busy ? t('Common_Saving') : t(isEdit ? 'Settlement_Save' : 'Settlement_Record')}</button>
          </div>
        </div>
        {isEdit && canDelete && <button type="button" className="btn btn-danger settlement-delete" onClick={handleDelete}>{t('Settlement_Delete')}</button>}
      </fieldset>
    </form>
  )
}
