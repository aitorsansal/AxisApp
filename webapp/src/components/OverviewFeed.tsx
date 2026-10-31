import { useCallback, useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { useGroups } from '../context/useGroups'
import { useAliases } from '../context/AliasesContext'
import { useLocale } from '../context/LocaleContext'
import { supabase } from '../lib/supabaseClient'
import { fetchAll } from '../lib/fetchAll'
import { nextDue } from '../lib/recurringSchedule'
import { activityDate } from '../lib/dates'
import { formatMoney } from '../lib/formatting'
import { formatExpenseAmount, getShowConverted } from '../lib/amountDisplay'
import { categoryIcon } from '../lib/icons'
import { activityLabel } from '../lib/activityLabel'
import { RefreshRevision } from '../lib/refreshRevision'
import { useLiveRefresh } from '../lib/useLiveRefresh'
import type { ExpenseWithPayer, RecurringExpense } from '../lib/types'
import { Icon } from './Icon'

export function OverviewFeed() {
  const { groups, currentMember } = useGroups()
  const { displayName } = useAliases()
  const { t, language } = useLocale()
  const [expenses, setExpenses] = useState<ExpenseWithPayer[] | null>(null)
  const [recurring, setRecurring] = useState<RecurringExpense[] | null>(null)
  const [errors, setErrors] = useState({ expenses: false, recurring: false })
  const revision = useRef(new RefreshRevision())
  const groupKey = (groups ?? []).map((g) => g.id).join(',')
  const memberId = currentMember?.id
  const load = useCallback(async () => {
    const canApply = revision.current.begin()
    const ids = groupKey.split(',').filter(Boolean)
    if (!ids.length) {
      setExpenses([])
      setRecurring([])
      return
    }
    const [activity, schedules] = await Promise.allSettled([
      (async () => {
        const result = await supabase
          .from('expenses')
          .select(
            'id, group_id, paid_by_member_id, amount, currency, amount_in_group_currency, description, category, occurred_at, created_at, is_settlement, receipt_path, event_id, expense_shares(member_id)',
          )
          .in('group_id', ids)
          .order('occurred_at', { ascending: false })
          .order('created_at', { ascending: false })
          .order('id')
          .limit(6)
        if (result.error) throw result.error
        return result.data as ExpenseWithPayer[]
      })(),
      fetchAll<RecurringExpense>((from, to) =>
        supabase
          .from('recurring_expenses')
          .select('*')
          .in('group_id', ids)
          .eq('is_active', true)
          .order('id')
          .range(from, to),
      ),
    ])
    if (!canApply()) return
    if (activity.status === 'fulfilled') setExpenses(activity.value)
    if (schedules.status === 'fulfilled')
      setRecurring(
        schedules.value
          .sort((a, b) =>
            nextDue(a.start_date, a.last_processed_date, a.frequency).localeCompare(
              nextDue(b.start_date, b.last_processed_date, b.frequency),
            ),
          )
          .slice(0, 3),
      )
    setErrors({ expenses: activity.status === 'rejected', recurring: schedules.status === 'rejected' })
  }, [groupKey])

  useEffect(() => {
    const freshness = revision.current
    let active = true
    void Promise.resolve().then(() => (active ? load() : undefined))
    return () => {
      active = false
      freshness.invalidate()
    }
  }, [load])
  useLiveRefresh(`overview-finances-${memberId ?? 'loading'}`, [], load)
  const dateLocale = language === 'es' ? 'es-ES' : 'en-GB'
  const errorNotice = (key: string) => (
    <div className="overview-section-error" role="alert">
      <p>{t(key)}</p>
      <button type="button" className="link-btn" onClick={() => void load()}>
        {t('Common_Retry')}
      </button>
    </div>
  )

  return (
    <aside className="overview-feed">
      <section aria-labelledby="recent-heading">
        <div className="overview-section-heading">
          <h2 id="recent-heading">{t('Overview_Recent')}</h2>
        </div>
        {errors.expenses && errorNotice('Overview_ActivityFailed')}
        {expenses === null && !errors.expenses && (
          <p role="status" className="overview-hint">
            {t('Common_Loading')}
          </p>
        )}
        {expenses?.length === 0 && <p className="overview-hint">{t('Overview_NoActivity')}</p>}
        {expenses?.map((expense) => {
          const group = groups?.find((g) => g.id === expense.group_id)
          if (!group) return null
          const label = activityLabel(
            {
              isSettlement: expense.is_settlement,
              payerId: expense.paid_by_member_id,
              shareMemberIds: expense.expense_shares?.map((s) => s.member_id),
              viewerId: memberId,
            },
            (id) => {
              const member = group.members.find((m) => m.id === id)
              return member ? displayName(member) : t('GroupDetail_SomeoneCapitalized')
            },
          )
          const amount = formatExpenseAmount(expense, group.currency, language, getShowConverted())
          return (
            <Link
              className="overview-activity-row"
              key={expense.id}
              to={`/groups/${group.id}/expenses/${expense.id}`}
            >
              <Icon name={categoryIcon(expense.category, expense.is_settlement)} size={18} />
              <div>
                <h3>
                  {expense.description ||
                    t(expense.is_settlement ? 'GroupDetail_SettleUp' : 'GroupDetail_ExpenseFallback')}
                </h3>
                <p>
                  {t(label.key, ...label.args)}
                </p>
                <p>
                  {group.name} ·{' '}
                  {activityDate(expense.occurred_at, language, {
                    today: t('Common_Today'),
                    yesterday: t('Common_Yesterday'),
                  })}
                </p>
              </div>
              <span className="overview-activity-amount">
                {amount.primary}
                {amount.secondary && <small>{amount.secondary}</small>}
              </span>
            </Link>
          )
        })}
      </section>
      <section aria-labelledby="scheduled-heading">
        <div className="overview-section-heading">
          <h2 id="scheduled-heading">{t('Overview_Automatic')}</h2>
        </div>
        {errors.recurring && errorNotice('Overview_SchedulesFailed')}
        {recurring === null && !errors.recurring && (
          <p role="status" className="overview-hint">
            {t('Common_Loading')}
          </p>
        )}
        {recurring?.length === 0 && <p className="overview-hint">{t('Overview_NoSchedules')}</p>}
        {recurring?.map((template) => {
          const due = nextDue(template.start_date, template.last_processed_date, template.frequency)
          const group = groups?.find((g) => g.id === template.group_id)
          return (
            <Link
              key={template.id}
              className="overview-activity-row"
              to={`/groups/${template.group_id}/recurring/${template.id}`}
            >
              <Icon name="repeat" size={18} />
              <div>
                <h3>{template.description}</h3>
                <p>{group?.name}</p>
                <p>
                  {t(
                    'Overview_NextEntry',
                    new Date(due + 'T12:00:00').toLocaleDateString(dateLocale, {
                      day: 'numeric',
                      month: 'short',
                      year: 'numeric',
                    }),
                  )}
                </p>
              </div>
              <span className="overview-activity-amount">
                {formatMoney(template.amount, template.currency, language)}
              </span>
            </Link>
          )
        })}
        {!!recurring?.length && (
          <p className="overview-hint overview-schedule-hint">{t('Overview_AutomaticHint')}</p>
        )}
      </section>
    </aside>
  )
}
