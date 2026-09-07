import { Link, useNavigate } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { useAuth } from '../context/AuthContext'
import { useLocale } from '../context/LocaleContext'
import './AppHeader.css'

export function AppHeader({ title, back }: { title: string; back?: boolean }) {
  const { session } = useAuth()
  const { t } = useLocale()
  const navigate = useNavigate()

  async function handleLogout() {
    await supabase.auth.signOut()
    navigate('/login')
  }

  return (
    <div className="app-header">
      {back ? (
        <button className="header-back" onClick={() => navigate(-1)} aria-label={t('Common_Back')}>
          ←
        </button>
      ) : (
        <span className="header-spacer" />
      )}
      <h1>{title}</h1>
      {!back ? (
        <div className="header-actions">
          <Link to="/profile" className="header-profile" title={session?.user.email ?? ''}>
            {t('Groups_Profile')}
          </Link>
          <button className="header-logout" onClick={handleLogout}>
            {t('Groups_LogOut')}
          </button>
        </div>
      ) : (
        <span className="header-spacer" />
      )}
    </div>
  )
}
