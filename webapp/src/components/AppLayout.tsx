import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { GroupsProvider } from '../context/GroupsContext'
import { useGroups } from '../context/useGroups'
import { useAliases } from '../context/AliasesContext'
import { useLocale } from '../context/LocaleContext'
import { BrandMark } from './BrandMark'
import { Avatar } from './Avatar'
import { GroupIconCircle } from './GroupIconCircle'
import { Icon } from './Icon'
import './AppLayout.css'

function Workspace() {
  const { groups, currentMember, error } = useGroups()
  const { initials, avatarUrl, displayName } = useAliases()
  const { t } = useLocale()
  const location = useLocation()
  const navigate = useNavigate()
  // Same as AppHeader's account menu: local scope, so other devices stay signed in.
  async function handleLogout() {
    await supabase.auth.signOut({ scope: 'local' })
    navigate('/login')
  }
  return (
    <div className="app-layout">
      <a className="skip-link" href="#main-content">
        {t('Common_SkipToContent')}
      </a>
      <aside className="app-sidebar">
        <NavLink to="/" className="sidebar-brand">
          <BrandMark />
          Axis
        </NavLink>
        <nav className="sidebar-navigation" aria-label={t('Overview_Navigation')}>
          <div className="sidebar-primary-navigation">
            <NavLink to="/" end><Icon name="home" />{t('Overview_NavOverview')}</NavLink>
            <NavLink to="/agenda"><Icon name="calendar" />{t('Overview_Agenda')}</NavLink>
            <NavLink to="/profile"><Icon name="user" />{t('Groups_Profile')}</NavLink>
          </div>
          <span className="sidebar-section-label">{t('Groups_YourGroups')}</span>
          {groups === null && !error && (
            <p className="sidebar-status" role="status">
              {t('Common_Loading')}
            </p>
          )}
          {error && (
            <p className="error-text" role="alert">
              {error}
            </p>
          )}
          <div className="sidebar-groups">
            {groups?.map((group) => (
              <NavLink
                key={group.id}
                to={`/groups/${group.id}`}
                className="sidebar-group"
                title={group.name}
              >
                <GroupIconCircle
                  color={group.color}
                  icon={group.icon}
                  name={group.name}
                  size={30}
                />
                <span>{group.name}<small>{group.currency} · {t('Groups_MemberCount', group.members.length)}</small></span>
              </NavLink>
            ))}
          </div>
          <div className="sidebar-actions">
            <NavLink to="/groups">{t('Overview_ManageGroups')}</NavLink>
            <NavLink to="/new-group">
              <Icon name="plus" />
              {t('Groups_NewGroup').replace(/^\+\s*/, '')}
            </NavLink>
            <NavLink to="/join">
              <Icon name="ticket" />
              {t('Groups_JoinWithCode')}
            </NavLink>
          </div>
        </nav>
        {/* On desktop the header's account menu is hidden, so logging out lives here. */}
        <div className="sidebar-footer">
          <NavLink to="/profile" className="sidebar-account">
            <Avatar
              url={currentMember ? avatarUrl(currentMember) : null}
              initials={currentMember ? initials(currentMember) : '?'}
              seed={currentMember?.id}
            />
            <span>
              <strong>{currentMember ? displayName(currentMember) : t('Groups_Profile')}</strong>
              <small>{t('Groups_Profile')}</small>
            </span>
          </NavLink>
          <button
            type="button"
            className="sidebar-logout"
            onClick={handleLogout}
            aria-label={t('Groups_LogOut')}
            title={t('Groups_LogOut')}
          >
            <Icon name="log_out" size={18} />
          </button>
        </div>
      </aside>
      <main id="main-content" className="app-main" tabIndex={-1}>
        <Outlet key={location.pathname} />
      </main>
      <nav className="mobile-navigation" aria-label={t('Overview_Navigation')}>
        <NavLink to="/" end><Icon name="home" size={20} />{t('Overview_NavOverview')}</NavLink>
        <NavLink to="/agenda"><Icon name="calendar" size={20} />{t('Overview_Agenda')}</NavLink>
        <NavLink to="/profile"><Icon name="user" size={20} />{t('Groups_Profile')}</NavLink>
      </nav>
    </div>
  )
}

export function AppLayout() {
  return (
    <GroupsProvider>
      <Workspace />
    </GroupsProvider>
  )
}
