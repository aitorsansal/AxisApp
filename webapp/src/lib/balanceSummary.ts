import type { PairwiseBalance } from './types'
import { toMinor } from './currencyUnits.ts'

// "You're owed" / "You owe" totals built from pairwise balances, never from group nets: a group
// net already cancels what one person owes you against what you owe someone else, which would
// hide real debts. Positive and negative rows are summed separately, per currency (no
// conversion), in integer minor units (cents; whole yen for JPY, KRW, ISK).

export interface CurrencyTotal {
  currency: string
  cents: number
}

export interface BalanceSide {
  totals: CurrencyTotal[] // largest first; empty when nothing is owed on this side
  memberIds: string[] // distinct counterparties, in first-seen order
  groupIds: string[] // distinct groups, in first-seen order
}

export interface BalanceSummary {
  owedToMe: BalanceSide
  iOwe: BalanceSide
}

function emptySide(): BalanceSide {
  return { totals: [], memberIds: [], groupIds: [] }
}

function add(side: BalanceSide, row: PairwiseBalance, currency: string, cents: number) {
  const total = side.totals.find((t) => t.currency === currency)
  if (total) total.cents += cents
  else side.totals.push({ currency, cents })
  if (!side.memberIds.includes(row.other_member_id)) side.memberIds.push(row.other_member_id)
  if (!side.groupIds.includes(row.group_id)) side.groupIds.push(row.group_id)
}

// currencyOf resolves a group's currency; rows from groups it doesn't know are skipped rather
// than guessed into some currency.
export function summarizeBalances(
  rows: readonly PairwiseBalance[],
  currencyOf: (groupId: string) => string | undefined,
): BalanceSummary {
  const summary: BalanceSummary = { owedToMe: emptySide(), iOwe: emptySide() }
  for (const row of rows) {
    const currency = currencyOf(row.group_id)
    if (!currency) continue
    const cents = toMinor(row.balance, currency)
    if (cents > 0) add(summary.owedToMe, row, currency, cents)
    else if (cents < 0) add(summary.iOwe, row, currency, -cents)
  }
  for (const side of [summary.owedToMe, summary.iOwe]) side.totals.sort((a, b) => b.cents - a.cents)
  return summary
}

export function isSettled(summary: BalanceSummary): boolean {
  return summary.owedToMe.totals.length === 0 && summary.iOwe.totals.length === 0
}
