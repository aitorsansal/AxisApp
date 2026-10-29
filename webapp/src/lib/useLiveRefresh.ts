import { useEffect, useRef } from 'react'
import { supabase } from './supabaseClient'

export interface LiveTable {
  table: string
  filter?: string
}

const pollMs = 60_000
const debounceMs = 300

// Keeps a screen fresh without a manual reload: refetches on Realtime changes to the given
// tables, and also on tab focus, coming back online, a Realtime reconnect and a slow poll while
// visible, because a websocket silently misses whatever happened while it was down. Realtime
// applies the caller's RLS, so only rows they can read trigger it. `refresh` runs debounced and
// is skipped while `isBusy()` is true, so a refetch can't overwrite an optimistic local edit.
export function useLiveRefresh(
  name: string,
  tables: LiveTable[],
  refresh: () => void | Promise<void>,
  isBusy: () => boolean = () => false,
) {
  const refreshRef = useRef(refresh)
  const busyRef = useRef(isBusy)
  useEffect(() => {
    refreshRef.current = refresh
    busyRef.current = isBusy
  })

  const tablesKey = JSON.stringify(tables)

  useEffect(() => {
    let timer: ReturnType<typeof setTimeout> | undefined
    let disposed = false

    const run = () => {
      if (disposed) return
      if (busyRef.current()) {
        timer = setTimeout(run, debounceMs)
        return
      }
      void Promise.resolve().then(() => disposed ? undefined : refreshRef.current()).catch(() => {
        // A later reconnect/focus/poll retries a failed background refresh.
      })
    }
    const schedule = () => {
      clearTimeout(timer)
      timer = setTimeout(run, debounceMs)
    }

    const liveTables = JSON.parse(tablesKey) as LiveTable[]
    const channel = liveTables.length > 0 ? supabase.channel(name) : null
    if (channel) {
      for (const { table, filter } of liveTables) {
        channel.on('postgres_changes', { event: '*', schema: 'public', table, ...(filter ? { filter } : {}) }, schedule)
      }
      channel.subscribe((status) => {
        if (status === 'SUBSCRIBED') schedule()
      })
    }

    const onVisibility = () => {
      if (document.visibilityState === 'visible') schedule()
    }
    const poll = setInterval(() => {
      if (document.visibilityState === 'visible') schedule()
    }, pollMs)
    document.addEventListener('visibilitychange', onVisibility)
    window.addEventListener('online', schedule)
    window.addEventListener('focus', schedule)

    return () => {
      disposed = true
      clearTimeout(timer)
      clearInterval(poll)
      document.removeEventListener('visibilitychange', onVisibility)
      window.removeEventListener('online', schedule)
      window.removeEventListener('focus', schedule)
      if (channel) void supabase.removeChannel(channel)
    }
  }, [name, tablesKey])
}
