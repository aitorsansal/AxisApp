import { minorUnits } from './currencyUnits.ts'

export function formatMoney(
  amount: number,
  currency: string,
  language: string,
  signed = false,
): string {
  return new Intl.NumberFormat(language === 'es' ? 'es-ES' : 'en-US', {
    style: 'currency',
    currency,
    minimumFractionDigits: minorUnits(currency),
    maximumFractionDigits: minorUnits(currency),
    signDisplay: signed ? 'exceptZero' : 'auto',
  }).format(amount)
}
