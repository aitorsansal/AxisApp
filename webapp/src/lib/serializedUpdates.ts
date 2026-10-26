export class SerializedUpdates<T> {
  private pending = new Map<string, Promise<T>>()

  run(key: string, initial: T, update: (current: T) => T, persist: (next: T) => Promise<void>): Promise<T> {
    const previous = this.pending.get(key) ?? Promise.resolve(initial)
    const next = previous.then(async (current) => {
      const value = update(current)
      await persist(value)
      return value
    })
    this.pending.set(key, next)
    const cleanup = () => {
      if (this.pending.get(key) === next) this.pending.delete(key)
    }
    void next.then(cleanup, cleanup)
    return next
  }
}
