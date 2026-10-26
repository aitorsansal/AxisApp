export function equalSplitCents(totalCents: number, ids: string[]): Record<string, number> {
  if (ids.length === 0) return {}
  const base = Math.floor(totalCents / ids.length)
  const remainder = totalCents % ids.length
  return Object.fromEntries(ids.map((id, i) => [id, base + (i < remainder ? 1 : 0)]))
}
