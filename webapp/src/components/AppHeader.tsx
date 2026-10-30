import { useEffect, useRef, useState, type ReactNode } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { useGroups } from '../context/useGroups'
import { useAliases } from '../context/AliasesContext'
import { useLocale } from '../context/LocaleContext'
import { useGoBackTo } from '../lib/navigation'
import { useDocumentTitle } from '../lib/useDocumentTitle'
import { BrandMark } from './BrandMark'
import { Avatar } from './Avatar'
import { Icon } from './Icon'
import './AppHeader.css'

export function AppHeader({
  title,
  backTo,
  actions,
}: {
  title: string
  backTo?: string
  actions?: ReactNode
}) {
  const { currentMember } = useGroups()
  const { initials, avatarUrl } = useAliases()
  const { t } = useLocale()
  useDocumentTitle(title)
  const navigate = useNavigate()
  const goBackTo = useGoBackTo()
  const [accountOpen, setAccountOpen] = useState(false)
  const accountButtonRef = useRef<HTMLButtonElement>(null)
  const accountRef = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const close = (event: MouseEvent) => {
      if (!accountRef.current?.contains(event.target as Node)) setAccountOpen(false)
    }
    const escape = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && accountRef.current?.contains(event.target as Node)) {
        setAccountOpen(false)
        accountButtonRef.current?.focus()
      }
    }
    document.addEventListener('mousedown', close)
    document.addEventListener('keydown', escape)
    return () => {
      document.removeEventListener('mousedown', close)
      document.removeEventListener('keydown', escape)
    }
  }, [])
  async function handleLogout() {
    await supabase.auth.signOut({ scope: 'local' })
    navigate('/login')
  }
  return (
    <header className={`app-header${backTo ? '' : ' app-header-home'}`}>
      {backTo && (
        <button
          type="button"
          className="header-back"
          onClick={() => goBackTo(backTo)}
          aria-label={t('Common_Back')}
        >
          <Icon name="arrow_left" />
        </button>
      )}
      <h1 className={!backTo ? 'header-brand' : undefined}>
        {!backTo && <BrandMark />}
        {title}
      </h1>
      <div className="header-actions">
        {actions}
        {!backTo && (
          <div className="header-account" ref={accountRef}>
            <button
              type="button"
              className="header-account-trigger"
              ref={accountButtonRef}
              aria-label={t('Groups_Profile')}
              aria-expanded={accountOpen}
              onClick={() => setAccountOpen((open) => !open)}
            >
              <Avatar
                url={currentMember ? avatarUrl(currentMember) : null}
                initials={currentMember ? initials(currentMember) : '?'}
            seed={currentMember?.id}
              />
            </button>
            {accountOpen && (
              <div className="account-dropdown">
                <Link to="/profile">{t('Groups_Profile')}</Link>
                <button type="button" onClick={handleLogout}>
                  <Icon name="log_out" size={16} />
                  {t('Groups_LogOut')}
                </button>
              </div>
            )}
          </div>
        )}
      </div>
    </header>
  )
}
