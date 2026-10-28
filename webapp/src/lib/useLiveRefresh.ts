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
      void refreshRef.current()
    }
    const schedule = () => {
      clearTimeout(timer)
      timer = setTimeout(run, debounceMs)
    }

    const channel = supabase.channel(name)
    for (const { table, filter } of JSON.parse(tablesKey) as LiveTable[]) {
      channel.on('postgres_changes', { event: '*', schema: 'public', table, ...(filter ? { filter } : {}) }, schedule)
    }
    let subscribedBefore = false
    channel.subscribe((status) => {
      if (status !== 'SUBSCRIBED') return
      if (subscribedBefore) schedule()
      subscribedBefore = true
    })

    const onVisibility = () => {
      if (document.visibilityState === 'visible') schedule()
    }
    const poll = setInterval(() => {
      if (document.visibilityState === 'visible') schedule()
    }, pollMs)
    document.addEventListener('visibilitychange', onVisibility)
    window.addEventListener('online', schedule)

    return () => {
      disposed = true
      clearTimeout(timer)
      clearInterval(poll)
      document.removeEventListener('visibilitychange', onVisibility)
      window.removeEventListener('online', schedule)
      void supabase.removeChannel(channel)
    }
  }, [name, tablesKey])
}
