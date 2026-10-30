import { amountCents } from './amountInput.ts'
import type { SettlementDraft } from './settlements'

export function settlementEditorSnapshot(draft: SettlementDraft): string {
  return JSON.stringify({
    payer: draft.payerId, recipient: draft.recipientId, currency: draft.currency,
    amount: amountCents(draft.amount, draft.currency) ?? `invalid:${draft.amount}`,
    date: draft.date, note: draft.note,
  })
}

export function eventEditorSnapshot(draft: {
  title: string; description: string; location: string; startDate: string; startTime: string;
  hasEndTime: boolean; endDate: string; endTime: string; needsTransport: boolean;
}): string {
  return JSON.stringify({ ...draft, endDate: draft.hasEndTime ? draft.endDate : null,
    endTime: draft.hasEndTime ? draft.endTime : null })
}
