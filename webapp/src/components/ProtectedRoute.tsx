import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '../context/AuthContext'
import { useRebuildDeepEntry } from '../lib/navigation'

export function ProtectedRoute() {
  const { session, loading } = useAuth()
  useRebuildDeepEntry(!loading && !!session)

  if (loading) return null
  if (!session) return <Navigate to="/login" replace />

  return <Outlet />
}
