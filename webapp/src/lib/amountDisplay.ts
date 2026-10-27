import { formatMoney } from './formatting.ts'

// "Show converted to group currency" — mirrors AppConstants.Preferences.AmountDisplayConverted
// and GroupExpensesViewModel.FormatExpenseAmount. Per-browser, on by default like Android. Only
// individual expense rows use it; balances and Settle amounts are always in the group currency.

const STORAGE_KEY = 'axis.amountDisplayConverted'

export function getShowConverted(): boolean {
  return localStorage.getItem(STORAGE_KEY) !== 'false'
}

export function setShowConverted(value: boolean) {
  localStorage.setItem(STORAGE_KEY, String(value))
}

export interface AmountExpense {
  amount: number
  currency: string
  amount_in_group_currency: number
}

// Same currency as the group (the common case): one amount, no secondary. Otherwise "on" leads
// with the converted amount and shows the original in brackets; "off" leads with the original and
// shows the approximate converted amount.
export function formatExpenseAmount(
  expense: AmountExpense,
  groupCurrency: string,
  language: string,
  showConverted: boolean,
): { primary: string; secondary: string } {
  const converted = formatMoney(Number(expense.amount_in_group_currency), groupCurrency, language)
  if (expense.currency === groupCurrency) return { primary: converted, secondary: '' }
  const original = formatMoney(Number(expense.amount), expense.currency, language)
  return showConverted
    ? { primary: converted, secondary: `(${original})` }
    : { primary: original, secondary: `(≈${converted})` }
}
