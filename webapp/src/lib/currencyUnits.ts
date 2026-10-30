// Decimals per currency. Mirrors public.currency_minor_units() in supabase/zero_decimal_currencies.sql
// and AxisApp/Models/CurrencyUnits.cs; a regression test fails if the lists drift. HUF and IDR stay
// at two decimals on purpose. "Minor" amounts are integers in the currency's smallest unit:
// cents for EUR, whole yen for JPY.
export const ZERO_DECIMAL_CURRENCIES = ['ISK', 'JPY', 'KRW'] as const

export function minorUnits(currency: string): 0 | 2 {
  return (ZERO_DECIMAL_CURRENCIES as readonly string[]).includes(currency) ? 0 : 2
}

export function minorFactor(currency: string): 1 | 100 {
  return minorUnits(currency) === 0 ? 1 : 100
}

// Integer minor units of a decimal amount; rounds away float noise (0.1 + 0.2 style drift).
export function toMinor(amount: number | string, currency: string): number {
  return Math.round(Number(amount) * minorFactor(currency))
}

export function fromMinor(minor: number, currency: string): number {
  return minor / minorFactor(currency)
}

// Plain "1234.50" / "1500" text (dot decimal, no grouping) for editable fields and CSV.
export function amountText(amount: number | string, currency: string): string {
  return Number(amount).toFixed(minorUnits(currency))
}

export function minorText(minor: number, currency: string): string {
  return amountText(fromMinor(minor, currency), currency)
}
