import { equalSplitCents } from './expenseMath.ts'

export type SplitMode = 'equal' | 'manual'

export function restoredSplitMode(mode: unknown): SplitMode {
  return mode === 'equal' ? 'equal' : 'manual'
}

export function editableEqualSplitCents(
  totalCents: number,
  ids: string[],
  saved: Record<string, number> | null,
): Record<string, number> {
  if (saved && ids.length > 0 && Object.keys(saved).length === ids.length
    && ids.every((id) => Number.isInteger(saved[id]) && saved[id] > 0)
    && ids.reduce((sum, id) => sum + saved[id], 0) === totalCents
    && Math.max(...ids.map((id) => saved[id])) - Math.min(...ids.map((id) => saved[id])) <= 1)
    return saved
  return equalSplitCents(totalCents, ids)
}
