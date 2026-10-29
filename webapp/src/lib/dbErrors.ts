export function isConvertedAmountTooLarge(error: unknown): boolean {
  return typeof error === 'object' && error !== null &&
    'message' in error && error.message === 'converted_amount_too_large'
}
