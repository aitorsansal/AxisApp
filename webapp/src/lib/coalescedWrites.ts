// Collapses rapid absolute-value changes (e.g. seat stepper taps) into one
// write per key once input settles. flush/flushAll send pending values right
// away, for page hide and unmount, so taps don't wait on a timer that may
// never fire.
export class CoalescedWrites<T> {
  private pending = new Map<string, { value: T; timer: ReturnType<typeof setTimeout> }>()
  private delayMs: number
  private write: (key: string, value: T) => void

  constructor(delayMs: number, write: (key: string, value: T) => void) {
    this.delayMs = delayMs
    this.write = write
  }

  /** Swaps the write callback (a React component's latest closure) without losing pending values. */
  setWrite(write: (key: string, value: T) => void) {
    this.write = write
  }

  set(key: string, value: T) {
    const existing = this.pending.get(key)
    if (existing) clearTimeout(existing.timer)
    this.pending.set(key, { value, timer: setTimeout(() => this.flush(key), this.delayMs) })
  }

  flush(key: string) {
    const entry = this.pending.get(key)
    if (!entry) return
    clearTimeout(entry.timer)
    // Removed before writing so a write that re-enters flush(key) is a no-op.
    this.pending.delete(key)
    this.write(key, entry.value)
  }

  flushAll() {
    for (const key of [...this.pending.keys()]) this.flush(key)
  }
}
