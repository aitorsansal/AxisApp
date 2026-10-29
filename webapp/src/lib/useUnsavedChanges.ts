import { useCallback, useEffect, useRef, useState } from 'react'
import { useBlocker } from 'react-router-dom'

export function useUnsavedChanges(recordKey: string, snapshot: string | null, pending: boolean) {
  const [initial, setInitial] = useState<{ key: string; snapshot: string } | null>(null)
  const bypass = useRef(false)
  const focusTarget = useRef<HTMLElement | null>(null)
  const [closeAction, setCloseAction] = useState<(() => void) | null>(null)

  if (snapshot !== null && initial?.key !== recordKey) {
    setInitial({ key: recordKey, snapshot })
  }
  useEffect(() => { bypass.current = false }, [recordKey])

  const dirty = snapshot !== null && initial?.key === recordKey && initial.snapshot !== snapshot
  const shouldWarn = dirty || pending
  const blocker = useBlocker(useCallback(({ currentLocation, nextLocation }) => {
    const leaving = currentLocation.pathname !== nextLocation.pathname
      || currentLocation.search !== nextLocation.search
    if (!bypass.current && shouldWarn && leaving) {
      focusTarget.current = document.activeElement instanceof HTMLElement ? document.activeElement : null
      return true
    }
    return false
  }, [shouldWarn]))

  useEffect(() => {
    if (!shouldWarn) return
    const beforeUnload = (event: BeforeUnloadEvent) => {
      if (bypass.current) return
      event.preventDefault()
      event.returnValue = ''
    }
    window.addEventListener('beforeunload', beforeUnload)
    return () => window.removeEventListener('beforeunload', beforeUnload)
  }, [shouldWarn])

  function keepEditing() {
    setCloseAction(null)
    if (blocker.state === 'blocked') blocker.reset()
    requestAnimationFrame(() => focusTarget.current?.isConnected && focusTarget.current.focus())
  }

  function markSaved() {
    bypass.current = true
    if (snapshot !== null) setInitial({ key: recordKey, snapshot })
    if (blocker.state === 'blocked') blocker.reset()
    setCloseAction(null)
  }

  function requestClose(action: () => void) {
    if (bypass.current || !shouldWarn) { action(); return }
    focusTarget.current = document.activeElement instanceof HTMLElement ? document.activeElement : null
    setCloseAction(() => action)
  }

  function discardChanges() {
    if (pending) return
    if (closeAction) { bypass.current = true; setCloseAction(null); closeAction() }
    else if (blocker.state === 'blocked') blocker.proceed()
  }

  return { blocker, keepEditing, markSaved, requestClose, discardChanges, confirmingClose: closeAction !== null }
}
