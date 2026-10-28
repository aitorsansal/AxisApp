import { useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { useGroups } from '../context/useGroups'
import { useAliases } from '../context/AliasesContext'
import { persistOrder } from '../lib/groupOrder'
import { formatMoney } from '../lib/formatting'
import { Avatar } from '../components/Avatar'
import { Icon } from '../components/Icon'
import { useLocale } from '../context/LocaleContext'
import { AppHeader } from '../components/AppHeader'
import { GroupIconCircle } from '../components/GroupIconCircle'
import './GroupsPage.css'

export function GroupsPage() {
  const { t } = useLocale()
  const { initials, avatarUrl, displayName } = useAliases()
  const { groups, setGroups, error } = useGroups()

  const [dragIndex, setDragIndex] = useState<number | null>(null)
  const rowRefs = useRef<Map<string, HTMLElement>>(new Map())
  const suppressClickRef = useRef(false)

  // Pointer capture keeps every subsequent pointermove routed to the handle that started the
  // drag, regardless of what's visually under the cursor — without it, the browser dispatches
  // move events to whatever element the pointer is currently over, which breaks the moment the
  // cursor leaves the original handle.
  function handlePointerDown(index: number) {
    return (e: React.PointerEvent) => {
      e.currentTarget.setPointerCapture(e.pointerId)
      setDragIndex(index)
      suppressClickRef.current = false
    }
  }

  function handlePointerMove(e: React.PointerEvent) {
    if (dragIndex === null || !groups) return
    suppressClickRef.current = true

    let targetIndex = dragIndex
    let bestDistance = Infinity
    groups.forEach((g, i) => {
      const el = rowRefs.current.get(g.id)
      if (!el) return
      const rect = el.getBoundingClientRect()
      const midpoint = rect.top + rect.height / 2
      const distance = Math.hypot(e.clientY - midpoint, e.clientX - (rect.left + rect.width / 2))
      if (distance < bestDistance) {
        bestDistance = distance
        targetIndex = i
      }
    })

    if (targetIndex !== dragIndex) {
      setGroups((prev) => {
        if (!prev) return prev
        const next = [...prev]
        const [moved] = next.splice(dragIndex, 1)
        next.splice(targetIndex, 0, moved)
        return next
      })
      setDragIndex(targetIndex)
    }
  }

  function handlePointerUp() {
    if (dragIndex !== null && groups) persistOrder(groups.map((g) => g.id))
    setDragIndex(null)
    // Swallow the click that follows a drag's pointerup so it doesn't also navigate.
    setTimeout(() => {
      suppressClickRef.current = false
    }, 0)
  }

  return (
    <div className="page page-wide groups-page">
      <AppHeader title="Axis" />
      <div className="groups-heading">
        <h2>{t('Groups_YourGroups')}</h2>
        <Link to="/join">{t('Groups_JoinWithCode')}</Link>
        <div className="groups-actions">
          <Link to="/join" className="btn btn-outline">
            {t('Groups_JoinWithCode')}
          </Link>
          <Link to="/new-group" className="btn btn-primary">
            {t('Groups_NewGroup')}
          </Link>
        </div>
      </div>

      {error && <p className="error-text">{error}</p>}

      {groups === null && !error && <div className="spinner">{t('Common_Loading')}</div>}

      {groups?.length === 0 && <p className="empty-state">{t('Groups_EmptyState')}</p>}

      <div className="group-list">
        {groups?.map((g, index) => (
          <article
            key={g.id}
            ref={(el) => {
              if (el) rowRefs.current.set(g.id, el)
              else rowRefs.current.delete(g.id)
            }}
            className={`card group-card${dragIndex === index ? ' dragging' : ''}`}
          >
            <button
              type="button"
              className="group-drag-handle"
              onPointerDown={handlePointerDown(index)}
              onPointerMove={handlePointerMove}
              onPointerUp={handlePointerUp}
              onPointerCancel={handlePointerUp}
              aria-label={t('Groups_ReorderHandle')}
              onClick={(event) => event.stopPropagation()}
              onKeyDown={(event) => {
                if (event.key !== 'ArrowUp' && event.key !== 'ArrowDown') return
                event.preventDefault()
                event.stopPropagation()
                const target = index + (event.key === 'ArrowUp' ? -1 : 1)
                if (!groups || target < 0 || target >= groups.length) return
                const next = [...groups]
                ;[next[index], next[target]] = [next[target], next[index]]
                setGroups(next)
                persistOrder(next.map((row) => row.id))
              }}
            >
              ⠿
            </button>
            <Link
              to={`/groups/${g.id}`}
              className="group-card-link"
              onClick={(event) => {
                if (suppressClickRef.current) event.preventDefault()
              }}
            >
              <GroupIconCircle color={g.color} icon={g.icon} name={g.name} size={52} />
              <div className="group-card-body">
                <div className="group-name">{g.name}</div>
                <div className="group-avatars" aria-hidden="true">
                  {g.members.slice(0, 4).map((member) => (
                    <span key={member.id} title={displayName(member)}>
                      <Avatar url={avatarUrl(member)} initials={initials(member)} seed={member.id} size={26} />
                    </span>
                  ))}
                </div>
                <div className="group-member-count">
                  {t('Groups_MemberCount', g.members.length)}
                </div>
              </div>
              <BalanceTag balance={g.balance} currency={g.currency} />
            </Link>
          </article>
        ))}
      </div>
      <Link to="/new-group" className="floating-add" aria-label={t('Groups_NewGroup')}>
        <Icon name="plus" size={26} />
      </Link>
    </div>
  )
}

function BalanceTag({ balance, currency }: { balance: number; currency: string }) {
  const { t, language } = useLocale()
  if (Math.abs(balance) < 0.005) {
    return <span className="balance-tag balance-settled">{t('Common_SettledUp')}</span>
  }
  const owed = balance > 0
  return (
    <span className={`balance-tag ${owed ? 'balance-positive' : 'balance-negative'}`}>
      <strong>{formatMoney(balance, currency, language, true)}</strong>
      <span>{t(owed ? 'Groups_YoureOwed' : 'Groups_YouOwe')}</span>
    </span>
  )
}
