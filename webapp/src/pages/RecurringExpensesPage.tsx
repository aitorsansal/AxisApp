import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { useAliases } from '../context/AliasesContext'
import { useLocale } from '../context/LocaleContext'
import type { Group, MemberRow, RecurringExpense } from '../lib/types'
import { AppHeader } from '../components/AppHeader'
import './RecurringExpensesPage.css'

export function RecurringExpensesPage() {
  const { groupId } = useParams<{ groupId: string }>()
  const { displayName } = useAliases()
  const { t } = useLocale()

  const [group, setGroup] = useState<Group | null>(null)
  const [members, setMembers] = useState<MemberRow[]>([])
  const [templates, setTemplates] = useState<RecurringExpense[]>([])
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    if (!groupId) return
    setError(null)
    const [groupRes, membersRes, templatesRes] = await Promise.all([
      supabase.from('groups').select('id, name, currency, created_by').eq('id', groupId).single(),
      supabase.from('group_members').select('member_id, members(id, display_name, account_id, avatar_path)').eq('group_id', groupId),
      supabase.from('recurring_expenses').select('*').eq('group_id', groupId).order('created_at', { ascending: false }),
    ])
    if (groupRes.error) return setError(groupRes.error.message)
    if (membersRes.error) return setError(membersRes.error.message)
    if (templatesRes.error) return setError(templatesRes.error.message)
    setGroup(groupRes.data as Group)
    setMembers((membersRes.data as unknown as MemberRow[]) ?? [])
    setTemplates((templatesRes.data as RecurringExpense[]) ?? [])
  }, [groupId])

  useEffect(() => {
    load()
  }, [load])

  function payerName(id: string) {
    const row = members.find((m) => m.member_id === id)
    return row ? displayName(row.members) : ''
  }

  async function toggleActive(template: RecurringExpense) {
    const { error } = await supabase
      .from('recurring_expenses')
      .update({ is_active: !template.is_active })
      .eq('id', template.id)
    if (error) return setError(error.message)
    await load()
  }

  async function handleDelete(template: RecurringExpense) {
    if (!window.confirm(t('RecurringExpenses_DeleteConfirm'))) return
    const { error } = await supabase.from('recurring_expenses').delete().eq('id', template.id)
    if (error) return setError(error.message)
    await load()
  }

  if (!group) {
    return (
      <div className="page">
        <AppHeader title={t('RecurringExpenses_Title')} back />
        {error ? <p className="error-text">{error}</p> : <div className="spinner">{t('Common_Loading')}</div>}
      </div>
    )
  }

  return (
    <div className="page">
      <AppHeader title={group.name} back />
      {error && <p className="error-text">{error}</p>}

      <Link to={`/groups/${groupId}/recurring/new`} className="btn btn-primary recurring-add-btn">
        {t('RecurringExpenses_Add')}
      </Link>

      {templates.length === 0 && <p className="empty-state small">{t('RecurringExpenses_Empty')}</p>}

      <ul className="recurring-list">
        {templates.map((tpl) => (
          <li key={tpl.id} className="card recurring-row">
            <Link to={`/groups/${groupId}/recurring/${tpl.id}`} className="recurring-info">
              <div className="recurring-desc">
                {tpl.description || (tpl.category ? t(`Category_${tpl.category}`) : t('GroupDetail_ExpenseFallback'))}
              </div>
              <div className="recurring-meta">
                {payerName(tpl.paid_by_member_id)} · {t(`Recurring_Frequency_${tpl.frequency}`)}
              </div>
            </Link>
            <div className="recurring-actions">
              <div className="recurring-amount">
                {tpl.amount.toFixed(2)} {tpl.currency}
              </div>
              <button type="button" className="link-btn" onClick={() => toggleActive(tpl)}>
                {tpl.is_active ? t('RecurringExpenses_Pause') : t('RecurringExpenses_Resume')}
              </button>
              <button type="button" className="link-btn danger" onClick={() => handleDelete(tpl)}>
                {t('RecurringExpenses_Delete')}
              </button>
            </div>
          </li>
        ))}
      </ul>
    </div>
  )
}
