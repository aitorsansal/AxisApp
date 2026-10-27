export const MAX_AMOUNT_CENTS = 999999999999

export type AmountInputError = 'invalid' | 'tooLarge' | null

export function parseAmountInput(text: string): { cents: number | null; error: AmountInputError } {
  const normalized = text.trim().replace(',', '.')
  if (!/^(?:[0-9]+(?:\.[0-9]{1,2})?|\.[0-9]{1,2})$/.test(normalized)) return { cents: null, error: 'invalid' }
  const [whole, fraction = ''] = normalized.split('.')
  const cents = Number(whole) * 100 + Number(fraction.padEnd(2, '0'))
  if (!Number.isSafeInteger(cents) || cents > MAX_AMOUNT_CENTS) return { cents: null, error: 'tooLarge' }
  return cents > 0 ? { cents, error: null } : { cents: null, error: 'invalid' }
}

export function amountCents(text: string): number | null {
  return parseAmountInput(text).cents
}

export function amountErrorKey(error: AmountInputError): string {
  return error === 'tooLarge' ? 'AmountInput_TooLarge' : 'AmountInput_Invalid'
}
