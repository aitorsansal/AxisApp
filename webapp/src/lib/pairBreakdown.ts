// "You and X" within one group — mirrors AxisApp/Models/PairBreakdown.cs. The same edges
// my_pairwise_balances sums (payer vs. share holder, settlements included), split into the lines
// the balance is made of, so they always add up to it. Positive balance = they owe me.

export interface PairExpense {
  id: string
  paid_by_member_id: string
  is_settlement: boolean
  description: string | null
  category: string | null
  occurred_at: string
}

export interface PairShare {
  expense_id: string
  member_id: string
  share_amount_in_group_currency: number
}

export interface PairExpenseLine {
  expenseId: string
  description: string
  category: string
  occurredAt: string
  iPaid: boolean // the amount is their part if I paid, mine if they paid
  amount: number
}

export interface PairBreakdown {
  paidForThem: number
  paidForMe: number
  settledToThem: number
  settledToMe: number
  settlementsNet: number // positive = I paid them more in settle-ups than they paid me
  balance: number
  expenses: PairExpenseLine[]
}

// Sums in cents so 0.1 + 0.2 style float drift can't leave a "−0,00" line.
const cents = (value: number) => Math.round(Number(value) * 100)

export function pairBreakdown(expenses: PairExpense[], shares: PairShare[], me: string, them: string): PairBreakdown {
  const sharesByExpense = new Map<string, PairShare[]>()
  for (const share of shares) {
    const list = sharesByExpense.get(share.expense_id) ?? []
    list.push(share)
    sharesByExpense.set(share.expense_id, list)
  }

  let paidForThem = 0
  let paidForMe = 0
  let settledToThem = 0
  let settledToMe = 0
  const lines: PairExpenseLine[] = []

  for (const e of expenses) {
    const iPaid = e.paid_by_member_id === me
    if (!iPaid && e.paid_by_member_id !== them) continue

    const otherParty = iPaid ? them : me
    const amount = (sharesByExpense.get(e.id) ?? [])
      .filter((s) => s.member_id === otherParty)
      .reduce((sum, s) => sum + cents(s.share_amount_in_group_currency), 0)
    if (amount === 0) continue

    if (e.is_settlement) {
      if (iPaid) settledToThem += amount
      else settledToMe += amount
      continue
    }

    if (iPaid) paidForThem += amount
    else paidForMe += amount
    lines.push({
      expenseId: e.id,
      description: e.description ?? '',
      category: e.category ?? '',
      occurredAt: e.occurred_at,
      iPaid,
      amount: amount / 100,
    })
  }

  lines.sort((a, b) => b.occurredAt.localeCompare(a.occurredAt))
  return {
    paidForThem: paidForThem / 100,
    paidForMe: paidForMe / 100,
    settledToThem: settledToThem / 100,
    settledToMe: settledToMe / 100,
    settlementsNet: (settledToThem - settledToMe) / 100,
    balance: (paidForThem - paidForMe + settledToThem - settledToMe) / 100,
    expenses: lines,
  }
}
