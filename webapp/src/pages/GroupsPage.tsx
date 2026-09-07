import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { useLocale } from '../context/LocaleContext'
import type { Group, MyGroupBalance } from '../lib/types'
import { AppHeader } from '../components/AppHeader'
import './GroupsPage.css'

interface GroupRow extends Group {
  balance: number
}

export function GroupsPage() {
  const { t } = useLocale()
  const [groups, setGroups] = useState<GroupRow[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    load()
  }, [])

  async function load() {
    setError(null)
    const [groupsRes, balancesRes] = await Promise.all([
      supabase.from('groups').select('id, name, currency, created_by'),
      supabase.from('my_group_balances').select('group_id, balance'),
    ])

    if (groupsRes.error) {
      setError(groupsRes.error.message)
      return
    }

    const balanceByGroup = new Map<string, number>(
      (balancesRes.data as MyGroupBalance[] | null)?.map((b) => [b.group_id, b.balance]) ?? [],
    )

    setGroups(
      (groupsRes.data as Group[]).map((g) => ({ ...g, balance: balanceByGroup.get(g.id) ?? 0 })),
    )
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
        {groups?.map((g) => (
          <Link to={`/groups/${g.id}`} key={g.id} className="card group-card">
            <div>
              <div className="group-name">{g.name}</div>
              <div className="group-currency">{g.currency}</div>
            </div>
            <BalanceTag balance={g.balance} currency={g.currency} />
          </Link>
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
