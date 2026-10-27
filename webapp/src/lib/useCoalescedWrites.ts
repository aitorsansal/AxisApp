import { useEffect, useState } from 'react'
import { CoalescedWrites } from './coalescedWrites'

// CoalescedWrites bound to the component's latest write callback, flushed on
// page hide, tab hide and unmount. A request started while the page is being
// torn down can still be cut off by the browser, so the worst case is losing
// the last settled value, never writing a wrong one.
export function useCoalescedWrites<T>(delayMs: number, write: (key: string, value: T) => void) {
  const [writes] = useState(() => new CoalescedWrites<T>(delayMs, write))
  useEffect(() => {
    writes.setWrite(write)
  })

  useEffect(() => {
    const flush = () => writes.flushAll()
    const onVisibility = () => {
      if (document.visibilityState === 'hidden') flush()
    }
    window.addEventListener('pagehide', flush)
    document.addEventListener('visibilitychange', onVisibility)
    return () => {
      window.removeEventListener('pagehide', flush)
      document.removeEventListener('visibilitychange', onVisibility)
      flush()
    }
  }, [writes])

  return writes
}
