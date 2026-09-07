import { useNavigate } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { useAuth } from '../context/AuthContext'
import './AppHeader.css'

export function AppHeader({ title, back }: { title: string; back?: boolean }) {
  const { session } = useAuth()
  const navigate = useNavigate()

  async function handleLogout() {
    await supabase.auth.signOut()
    navigate('/login')
  }

  return (
    <div className="app-header">
      {back ? (
        <button className="header-back" onClick={() => navigate(-1)} aria-label="Back">
          ←
        </button>
      ) : (
        <span className="header-spacer" />
      )}
      <h1>{title}</h1>
      {!back ? (
        <button className="header-logout" onClick={handleLogout} title={session?.user.email ?? ''}>
          Log out
        </button>
      ) : (
        <span className="header-spacer" />
      )}
    </div>
  )
}
