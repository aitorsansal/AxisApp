import { parseAmountInput } from './amountInput.ts'

interface ExpenseEditorState {
  amount: string
  description: string
  currency: string
  category: string
  paidBy: string
  includedIds: string[]
  manual: boolean
  shareInputs: Record<string, string>
  shareCents: Record<string, number>
  recurring: boolean
  occurredOn: string
  startDate: string
  frequency: string
  receiptPath: string | null
  eventId: string | null
}

function amountValue(text: string, currency: string) {
  const parsed = parseAmountInput(text, currency)
  return parsed.cents ?? { invalid: text }
}

export function expenseEditorSnapshot(state: ExpenseEditorState): string {
  return JSON.stringify({
    amount: amountValue(state.amount, state.currency), description: state.description,
    currency: state.currency, category: state.category, paidBy: state.paidBy,
    manual: state.manual,
    shares: [...state.includedIds].sort().map(id => [id,
      state.manual ? amountValue(state.shareInputs[id] ?? '', state.currency) : state.shareCents[id]]),
    recurring: state.recurring,
    date: state.recurring ? state.startDate : state.occurredOn,
    frequency: state.recurring ? state.frequency : null,
    receiptPath: state.recurring ? null : state.receiptPath,
    eventId: state.eventId,
  })
}
