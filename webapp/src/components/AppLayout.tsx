import { NavLink, Outlet, useLocation } from 'react-router-dom'
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
        <nav className="sidebar-navigation" aria-label={t('Groups_YourGroups')}>
          <NavLink to="/" end className="sidebar-section-link">
            {t('Groups_YourGroups')}
          </NavLink>
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
                <span>{group.name}</span>
              </NavLink>
            ))}
          </div>
          <div className="sidebar-actions">
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
        <NavLink to="/profile" className="sidebar-account">
          <Avatar
            url={currentMember ? avatarUrl(currentMember) : null}
            initials={currentMember ? initials(currentMember) : '?'}
          />
          <span>
            <strong>{currentMember ? displayName(currentMember) : t('Groups_Profile')}</strong>
            <small>{t('Groups_Profile')}</small>
          </span>
          <Icon name="arrow_right" size={16} />
        </NavLink>
      </aside>
      <main id="main-content" className="app-main" tabIndex={-1}>
        <Outlet key={location.pathname} />
      </main>
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
