import { Link, useNavigate } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { useAuth } from '../context/AuthContext'
import { useLocale } from '../context/LocaleContext'
import { useGoBackTo } from '../lib/navigation'
import './AppHeader.css'

// backTo is the page's logical parent, not the previous history entry — see lib/navigation.ts.
export function AppHeader({ title, backTo }: { title: string; backTo?: string }) {
  const { session } = useAuth()
  const { t } = useLocale()
  const navigate = useNavigate()
  const goBackTo = useGoBackTo()

  async function handleLogout() {
    // Local scope: the default ('global') revokes every session the account has, so logging out of the
    // web app would also log the phone out (see SupabaseAuthService.SignOutAsync).
    await supabase.auth.signOut({ scope: 'local' })
    navigate('/login')
  }

  return (
    <div className="app-header">
      {backTo ? (
        <button className="header-back" onClick={() => goBackTo(backTo)} aria-label={t('Common_Back')}>
          ←
        </button>
      ) : (
        <span className="header-spacer" />
      )}
      <h1>{title}</h1>
      {!backTo ? (
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
