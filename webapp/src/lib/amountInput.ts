import { minorFactor, minorUnits } from './currencyUnits.ts'

// The stored cap is 9999999999.99 in every currency, so a zero-decimal currency tops out at
// 9999999999 whole units.
export function maxAmountMinor(currency: string): number {
  return minorUnits(currency) === 0 ? 9999999999 : 999999999999
}

export type AmountInputError = 'invalid' | 'tooLarge' | 'noDecimals' | null

// Parses an amount typed in `currency` into integer minor units (cents; whole units for JPY, KRW,
// ISK). Same contract as AxisApp/Models/AmountInput.cs; the shared cases are in
// tests/amount-input-cases.json. A currency without decimals rejects a decimal part with its own
// error rather than rounding it, and "1.000" is rejected the same way (no thousands separators).
export function parseAmountInput(text: string, currency: string): { cents: number | null; error: AmountInputError } {
  const normalized = text.trim().replace(',', '.')
  const units = minorUnits(currency)
  let whole: string
  let fraction = ''
  if (units === 0) {
    if (/^(?:[0-9]+\.[0-9]+|\.[0-9]+)$/.test(normalized)) return { cents: null, error: 'noDecimals' }
    if (!/^[0-9]+$/.test(normalized)) return { cents: null, error: 'invalid' }
    whole = normalized
  } else {
    if (!/^(?:[0-9]+(?:\.[0-9]{1,2})?|\.[0-9]{1,2})$/.test(normalized)) return { cents: null, error: 'invalid' }
    ;[whole, fraction = ''] = normalized.split('.')
  }
  const cents = Number(whole) * minorFactor(currency) + (units === 0 ? 0 : Number(fraction.padEnd(2, '0')))
  if (!Number.isSafeInteger(cents) || cents > maxAmountMinor(currency)) return { cents: null, error: 'tooLarge' }
  return cents > 0 ? { cents, error: null } : { cents: null, error: 'invalid' }
}

export function amountCents(text: string, currency: string): number | null {
  return parseAmountInput(text, currency).cents
}

export function amountErrorKey(error: AmountInputError, currency: string): string {
  if (error === 'tooLarge') return 'AmountInput_TooLarge'
  if (error === 'noDecimals') return 'AmountInput_NoDecimals'
  return minorUnits(currency) === 0 ? 'AmountInput_InvalidWhole' : 'AmountInput_Invalid'
}

export function amountHintKey(currency: string): string {
  return minorUnits(currency) === 0 ? 'AmountInput_FormatHintWhole' : 'AmountInput_FormatHint'
}
