import { amountCents } from './amountInput.ts'

export interface SettlementDraft {
  id?: string
  payerId: string
  recipientId: string
  amount: string
  currency: string
  note: string
  date: string
  originalOccurredAt?: string | null
  receiptPath?: string | null
  eventId?: string | null
}

export const settlementCents = amountCents

export function isSettlementDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || value.startsWith('0000')) return false
  const date = new Date(`${value}T12:00:00Z`)
  return Number.isFinite(date.getTime()) && date.toISOString().slice(0, 10) === value
}

export function settlementParties(myId: string, otherId: string, balance: number) {
  return balance > 0
    ? { payerId: otherId, recipientId: myId }
    : { payerId: myId, recipientId: otherId }
}

export function settlementPayload(
  draft: SettlementDraft,
  groupId: string,
  fallbackDescription: string,
  occurredAt: string,
) {
  const cents = settlementCents(draft.amount)
  if (cents === null || !draft.payerId || !draft.recipientId || draft.payerId === draft.recipientId)
    throw new Error('Invalid settlement')
  const amount = cents / 100
  return {
    p_expense: {
      id: draft.id ?? null,
      group_id: groupId,
      paid_by_member_id: draft.payerId,
      amount,
      currency: draft.currency,
      description: draft.note.trim() || fallbackDescription,
      category: '',
      occurred_at: occurredAt,
      receipt_path: draft.receiptPath ?? null,
      event_id: draft.eventId ?? null,
      is_settlement: true,
    },
    p_shares: [{ member_id: draft.recipientId, share_amount: amount }],
  }
}
