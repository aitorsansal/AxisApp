export function isConvertedAmountTooLarge(error: unknown): boolean {
  return typeof error === 'object' && error !== null &&
    'message' in error && error.message === 'converted_amount_too_large'
}

export function isAmountPrecision(error: unknown): boolean {
  return typeof error === 'object' && error !== null &&
    'message' in error && error.message === 'amount_precision'
}

// Translation key for the stable amount errors raised by the currency triggers, or null when the
// error is something else.
export function amountDbErrorKey(error: unknown): string | null {
  if (isConvertedAmountTooLarge(error)) return 'AmountInput_ConvertedTooLarge'
  if (isAmountPrecision(error)) return 'AmountInput_NoDecimals'
  return null
}
