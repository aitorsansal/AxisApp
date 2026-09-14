import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { useLocale } from '../context/LocaleContext'
import type { Group, MyGroupBalance } from '../lib/types'
import { AppHeader } from '../components/AppHeader'
import { GroupIconCircle } from '../components/GroupIconCircle'
import './GroupsPage.css'

interface GroupRow extends Group {
  balance: number
}

// Local-only, per-device manual ordering — same semantics as MAUI's
// AppConstants.Preferences.GroupOrder (Preferences, never synced): a comma-joined list of
// group ids, applied on load and re-persisted after every drag. Not present in the list yet
// (a new group, another device) falls through to the end via Number.MAX_SAFE_INTEGER, same
// as GroupsViewModel.ApplySavedOrder's int.MaxValue fallback.
const ORDER_STORAGE_KEY = 'axis_group_order'

function readSavedOrder(): string[] {
  try {
    const stored = localStorage.getItem(ORDER_STORAGE_KEY)
    return stored ? stored.split(',').filter(Boolean) : []
  } catch {
    return []
  }
}

function persistOrder(ids: string[]) {
  try {
    localStorage.setItem(ORDER_STORAGE_KEY, ids.join(','))
  } catch {
    // localStorage unavailable — ordering just won't survive a reload this session.
  }
}

function applySavedOrder(groups: GroupRow[]): GroupRow[] {
  const order = readSavedOrder()
  return [...groups].sort((a, b) => {
    const ia = order.indexOf(a.id)
    const ib = order.indexOf(b.id)
    return (ia < 0 ? Number.MAX_SAFE_INTEGER : ia) - (ib < 0 ? Number.MAX_SAFE_INTEGER : ib)
  })
}

export function GroupsPage() {
  const { t } = useLocale()
  const navigate = useNavigate()
  const [groups, setGroups] = useState<GroupRow[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  const [dragIndex, setDragIndex] = useState<number | null>(null)
  const rowRefs = useRef<Map<string, HTMLElement>>(new Map())
  const suppressClickRef = useRef(false)

  useEffect(() => {
    load()
  }, [])

  async function load() {
    setError(null)
    const [groupsRes, balancesRes] = await Promise.all([
      supabase.from('groups').select('id, name, currency, created_by, color, icon'),
      supabase.from('my_group_balances').select('group_id, balance'),
    ])

    if (groupsRes.error) {
      setError(groupsRes.error.message)
      return
    }

    const balanceByGroup = new Map<string, number>(
      (balancesRes.data as MyGroupBalance[] | null)?.map((b) => [b.group_id, b.balance]) ?? [],
    )

    const rows = (groupsRes.data as Group[]).map((g) => ({ ...g, balance: balanceByGroup.get(g.id) ?? 0 }))
    setGroups(applySavedOrder(rows))
  }

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
      const distance = Math.abs(e.clientY - midpoint)
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
    <div className="page">
      <AppHeader title={t('Groups_YourGroups')} />

      {error && <p className="error-text">{error}</p>}

      <div className="groups-actions">
        <Link to="/new-group" className="btn btn-outline">
          {t('Groups_NewGroup')}
        </Link>
        <Link to="/join" className="btn btn-outline">
          {t('Groups_JoinWithCode')}
        </Link>
      </div>

      {groups === null && <div className="spinner">{t('Common_Loading')}</div>}

      {groups?.length === 0 && <p className="empty-state">{t('Groups_EmptyState')}</p>}

      <div className="group-list">
        {groups?.map((g, index) => (
          <div
            key={g.id}
            ref={(el) => {
              if (el) rowRefs.current.set(g.id, el)
              else rowRefs.current.delete(g.id)
            }}
            className={`card group-card${dragIndex === index ? ' dragging' : ''}`}
            role="link"
            tabIndex={0}
            onClick={() => {
              if (!suppressClickRef.current) navigate(`/groups/${g.id}`)
            }}
            onKeyDown={(e) => {
              if (e.key === 'Enter') navigate(`/groups/${g.id}`)
            }}
          >
            <span
              className="group-drag-handle"
              onPointerDown={handlePointerDown(index)}
              onPointerMove={handlePointerMove}
              onPointerUp={handlePointerUp}
              onPointerCancel={handlePointerUp}
              aria-label={t('Groups_ReorderHandle')}
            >
              ⠿
            </span>
            <GroupIconCircle color={g.color} icon={g.icon} name={g.name} size={40} />
            <div className="group-card-body">
              <div className="group-name">{g.name}</div>
              <div className="group-currency">{g.currency}</div>
            </div>
            <BalanceTag balance={g.balance} currency={g.currency} />
          </div>
        ))}
      </div>
    </div>
  )
}

function BalanceTag({ balance, currency }: { balance: number; currency: string }) {
  const { t } = useLocale()
  if (Math.abs(balance) < 0.005) {
    return <span className="balance-tag balance-settled">{t('Common_SettledUp')}</span>
  }
  const owed = balance > 0
  return (
    <span className={`balance-tag ${owed ? 'balance-positive' : 'balance-negative'}`}>
      {t(owed ? 'Groups_YoureOwedAmount' : 'Groups_YouOweAmount', Math.abs(balance).toFixed(2), currency)}
    </span>
  )
}
