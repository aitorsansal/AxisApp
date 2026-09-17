import { useCallback, useEffect } from 'react'
import { useLocation, useNavigate, useNavigationType } from 'react-router-dom'

// Keeps browser history shaped like the page hierarchy (groups → group → child),
// so the in-app back arrow, the browser Back button and the Android back gesture
// all land on the logical parent. Browsers don't allow cancelling Back, so rather
// than intercepting it we make sure the entry below the current one IS the parent:
//  - "going up" (back arrow, save, delete) pops history to the parent when it's
//    already below us, instead of pushing a new entry on top;
//  - a cold start on a deep URL (invite link, push tap, bookmark) rebuilds the
//    parent entries underneath it.
// Relies on React Router's BrowserRouter storing its position as history.state.idx
// (verified against react-router 7's getUrlBasedHistory). If that ever disappears,
// everything degrades to navigate(parent, { replace: true }).

const STORAGE_KEY = 'axis.navStack'

type Stack = Record<number, string>

function loadStack(): Stack {
  try {
    return JSON.parse(sessionStorage.getItem(STORAGE_KEY) ?? '{}') as Stack
  } catch {
    return {}
  }
}

// idx → pathname for each history entry this tab has visited. Persisted per tab so
// a reload doesn't forget what's underneath the current entry.
const stack: Stack = loadStack()

function saveStack() {
  try {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(stack))
  } catch {
    // Best-effort — without it, going up falls back to replace.
  }
}

function historyIndex(): number | null {
  const idx = (window.history.state as { idx?: unknown } | null)?.idx
  return typeof idx === 'number' ? idx : null
}

/** The page "up" from a URL, mirroring the routes in App.tsx. null for top-level pages. */
export function logicalParent(pathname: string, search = ''): string | null {
  if (pathname === '/' || pathname === '/login') return null
  const match = pathname.match(/^\/groups\/([^/]+)(?:\/(.+))?$/)
  if (!match) return '/'
  const [, groupId, rest] = match
  const group = `/groups/${groupId}`
  if (!rest) return '/'

  const eventId = new URLSearchParams(search).get('eventId')
  if (rest === 'add-expense' && eventId) return `${group}/events/${eventId}`
  if (/^recurring\/[^/]+$/.test(rest)) return `${group}/recurring`
  const editEvent = rest.match(/^events\/([^/]+)\/edit$/)
  if (editEvent) return `${group}/events/${editEvent[1]}`
  return group
}

/** Records which path lives at each history index. Render once inside the router. */
export function NavigationTracker() {
  const location = useLocation()
  const navigationType = useNavigationType()

  useEffect(() => {
    const idx = historyIndex()
    if (idx == null) return
    if (navigationType === 'PUSH') {
      // A push discards every forward entry.
      for (const key of Object.keys(stack)) {
        if (Number(key) > idx) delete stack[Number(key)]
      }
    }
    stack[idx] = location.pathname
    saveStack()
  }, [location.key, location.pathname, navigationType])

  return null
}

/**
 * Navigates to an ancestor page: pops history back to it if it's already below
 * the current entry, otherwise replaces the current entry with it.
 */
export function useGoBackTo() {
  const navigate = useNavigate()
  return useCallback(
    (target: string) => {
      const idx = historyIndex()
      const targetPath = target.split('?')[0]
      if (idx != null) {
        for (let i = idx - 1; i >= 0; i--) {
          if (stack[i] === targetPath) {
            navigate(i - idx)
            return
          }
        }
      }
      navigate(target, { replace: true })
    },
    [navigate],
  )
}

let deepEntryChecked = false

/**
 * On the first authenticated render of this document, if the tab was opened
 * directly on a deep URL (nothing of ours underneath it), inserts its ancestors
 * below it. Uses the raw History API so nothing re-renders or refetches — the
 * router only reads location/idx back on the next navigation or popstate.
 */
export function useRebuildDeepEntry(ready: boolean) {
  useEffect(() => {
    if (!ready || deepEntryChecked) return
    deepEntryChecked = true
    if (historyIndex() !== 0) return

    const { pathname, search, hash } = window.location
    const ancestors: string[] = []
    for (let p = logicalParent(pathname, search); p; p = logicalParent(p)) {
      ancestors.unshift(p)
    }
    if (ancestors.length === 0) return

    const currentState = window.history.state as Record<string, unknown> | null
    const entryKey = () => Math.random().toString(36).slice(2, 10)

    window.history.replaceState({ usr: null, key: entryKey(), idx: 0 }, '', ancestors[0])
    stack[0] = ancestors[0]
    for (let i = 1; i < ancestors.length; i++) {
      window.history.pushState({ usr: null, key: entryKey(), idx: i }, '', ancestors[i])
      stack[i] = ancestors[i]
    }
    const idx = ancestors.length
    window.history.pushState({ ...currentState, idx }, '', pathname + search + hash)
    stack[idx] = pathname
    for (const key of Object.keys(stack)) {
      if (Number(key) > idx) delete stack[Number(key)]
    }
    saveStack()
  }, [ready])
}
