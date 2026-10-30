import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useGroups } from '../context/useGroups'
import { useAliases } from '../context/AliasesContext'
import { useLocale } from '../context/LocaleContext'
import { useLiveRefresh } from '../lib/useLiveRefresh'
import { toMinor } from '../lib/currencyUnits'
import { formatMoney } from '../lib/formatting'
import { AppHeader } from '../components/AppHeader'
import { BalanceSummary } from '../components/BalanceSummary'
import { UpcomingEvents } from '../components/UpcomingEvents'
import { OverviewFeed } from '../components/OverviewFeed'
import { GroupIconCircle } from '../components/GroupIconCircle'
import { Modal } from '../components/Modal'
import { Icon } from '../components/Icon'
import './OverviewPage.css'

export function OverviewPage({ agenda = false }: { agenda?: boolean }) {
  const { groups, pairwise, error, refresh } = useGroups()
  const { displayName } = useAliases()
  const { t, language } = useLocale()
  const navigate = useNavigate()
  const [showBalances, setShowBalances] = useState(false)
  const [chooseGroup, setChooseGroup] = useState(false)
  useLiveRefresh('overview-balances', [], refresh)

  const addExpense = () => {
    if (groups?.length === 1) navigate(`/groups/${groups[0].id}/add-expense`)
    else setChooseGroup(true)
  }
  return (
    <div className={`page page-wide overview-page${agenda ? ' agenda-page' : ''}`}>
      <AppHeader title="Axis" />
      <div className="overview-heading">
        <div>
          <h2>{t(agenda ? 'Overview_Agenda' : 'Overview_Title')}</h2>
          <p>
            {new Date().toLocaleDateString(language === 'es' ? 'es-ES' : 'en-GB', {
              weekday: 'long',
              day: 'numeric',
              month: 'long',
            })}{' '}
            · {t('Overview_AllGroups')}
          </p>
        </div>
        {!agenda && !!groups?.length && (
          <button type="button" className="btn btn-primary" onClick={addExpense}>
            <Icon name="plus" size={18} />
            {t('GroupDetail_AddExpenseButton').replace(/^\+\s*/, '')}
          </button>
        )}
      </div>
      {error && (
        <div className="overview-section-error" role="alert">
          <p>{t('Overview_BalancesFailed')}</p>
          <button type="button" className="link-btn" onClick={() => void refresh()}>
            {t('Common_Retry')}
          </button>
        </div>
      )}
      {groups === null && !error && (
        <p role="status" className="overview-hint">
          {t('Common_Loading')}
        </p>
      )}
      {groups?.length === 0 && (
        <section className="card overview-empty">
          <h3>{t('Overview_FirstGroup')}</h3>
          <p>{t('Groups_EmptyState')}</p>
          <div className="overview-inline-actions">
            <Link className="btn btn-primary" to="/new-group">
              {t('Groups_NewGroup').replace(/^\+\s*/, '')}
            </Link>
            <Link className="btn btn-outline" to="/join">
              {t('Groups_JoinWithCode')}
            </Link>
          </div>
        </section>
      )}
      {!!groups?.length && (
        <>
          {!agenda && pairwise && (
            <>
              <BalanceSummary groups={groups} pairwise={pairwise} />
              {pairwise.some((row) => {
                const group = groups.find((g) => g.id === row.group_id)
                return group && toMinor(row.balance, group.currency) !== 0
              }) && (
                <div className="overview-balance-review">
                  <button
                    type="button"
                    className="link-btn"
                    aria-expanded={showBalances}
                    aria-controls="overview-pairwise"
                    onClick={() => setShowBalances((v) => !v)}
                  >
                    {t(showBalances ? 'Overview_HideBalances' : 'Overview_ReviewBalances')}
                  </button>
                  {showBalances && (
                    <div id="overview-pairwise">
                      {pairwise.map((row) => {
                        const group = groups.find((g) => g.id === row.group_id)
                        if (!group || toMinor(row.balance, group.currency) === 0) return null
                        const person = group.members.find((m) => m.id === row.other_member_id)
                        return (
                          <Link
                            className="overview-debt-row"
                            key={`${row.group_id}:${row.other_member_id}`}
                            to={`/groups/${group.id}`}
                          >
                            <span>
                              <strong>
                                {person ? displayName(person) : t('GroupDetail_SomeoneCapitalized')}
                              </strong>
                              <small>{group.name}</small>
                            </span>
                            <span className={row.balance > 0 ? 'overview-positive' : 'overview-negative'}>
                              {formatMoney(Math.abs(row.balance), group.currency, language)}
                              <small>{t(row.balance > 0 ? 'Summary_OwedToYou' : 'Summary_YouOwe')}</small>
                            </span>
                            <Icon name="arrow_right" size={16} />
                          </Link>
                        )
                      })}
                    </div>
                  )}
                </div>
              )}
            </>
          )}
          <div className={agenda ? '' : 'overview-columns'}>
            <UpcomingEvents agenda={agenda} />
            {!agenda && <OverviewFeed />}
          </div>
          {!agenda && (
            <section className="overview-mobile-groups" aria-labelledby="overview-groups-heading">
              <div className="overview-section-heading">
                <h2 id="overview-groups-heading">{t('Groups_YourGroups')}</h2>
                <Link to="/groups">{t('Overview_ManageGroups')}</Link>
              </div>
              {groups.map((group) => (
                <Link key={group.id} className="card overview-group-tile" to={`/groups/${group.id}`}>
                  <GroupIconCircle name={group.name} color={group.color} icon={group.icon} size={36} />
                  <span>
                    <strong>{group.name}</strong>
                    <small>
                      {t('Groups_MemberCount', group.members.length)} · {group.currency}
                    </small>
                  </span>
                  <Icon name="arrow_right" size={16} />
                </Link>
              ))}
              <div className="overview-inline-actions">
                <Link to="/new-group">{t('Groups_NewGroup').replace(/^\+\s*/, '')}</Link>
                <Link to="/join">{t('Groups_JoinWithCode')}</Link>
              </div>
            </section>
          )}
        </>
      )}
      {chooseGroup && (
        <Modal title={t('Overview_ChooseGroup')} onClose={() => setChooseGroup(false)}>
          <div className="overview-group-choices">
            {groups?.map((group) => (
              <Link key={group.id} to={`/groups/${group.id}/add-expense`}>
                <GroupIconCircle name={group.name} color={group.color} icon={group.icon} size={32} />
                <span>{group.name}</span>
                <Icon name="arrow_right" size={16} />
              </Link>
            ))}
          </div>
        </Modal>
      )}
    </div>
  )
}
