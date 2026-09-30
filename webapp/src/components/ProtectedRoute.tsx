import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../context/AuthContext'
import { useRebuildDeepEntry } from '../lib/navigation'

// Where a signed-out visitor was headed (e.g. a shared event link). Kept in sessionStorage rather
// than router state so it also survives Google sign-in, which leaves the SPA and comes back to
// the bare origin. Consumed the first time an authenticated session lands on "/".
const RETURN_TO_KEY = 'axis.returnTo'

function readReturnTo(): string | null {
  try {
    return sessionStorage.getItem(RETURN_TO_KEY)
  } catch {
    return null
  }
}

function writeReturnTo(value: string | null) {
  try {
    if (value) sessionStorage.setItem(RETURN_TO_KEY, value)
    else sessionStorage.removeItem(RETURN_TO_KEY)
  } catch {
    // Best-effort — without it the user just lands on the groups list after signing in.
  }
}

export function ProtectedRoute() {
  const { session, loading } = useAuth()
  const location = useLocation()
  useRebuildDeepEntry(!loading && !!session)

  if (loading) return null

  if (!session) {
    const target = location.pathname + location.search + location.hash
    if (target !== '/') writeReturnTo(target)
    return <Navigate to="/login" replace />
  }

  if (location.pathname === '/') {
    const returnTo = readReturnTo()
    if (returnTo) {
      writeReturnTo(null)
      return <Navigate to={returnTo} replace />
    }
  }

  return <Outlet />
}
