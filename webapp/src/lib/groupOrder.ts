const ORDER_STORAGE_KEY = 'axis_group_order'

export function applySavedOrder<T extends { id: string }>(groups: T[]): T[] {
  let order: string[] = []
  try {
    order = localStorage.getItem(ORDER_STORAGE_KEY)?.split(',').filter(Boolean) ?? []
  } catch {
    /* Ordering remains available for this session. */
  }
  const rank = (id: string) => {
    const index = order.indexOf(id)
    return index < 0 ? Number.MAX_SAFE_INTEGER : index
  }
  return [...groups].sort((a, b) => rank(a.id) - rank(b.id))
}

export function persistOrder(ids: string[]) {
  try {
    localStorage.setItem(ORDER_STORAGE_KEY, ids.join(','))
  } catch {
    /* Ordering remains available for this session. */
  }
}
