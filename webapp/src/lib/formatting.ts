export function formatMoney(
  amount: number,
  currency: string,
  language: string,
  signed = false,
): string {
  return new Intl.NumberFormat(language === 'es' ? 'es-ES' : 'en-US', {
    style: 'currency',
    currency,
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
    signDisplay: signed ? 'exceptZero' : 'auto',
  }).format(amount)
}
