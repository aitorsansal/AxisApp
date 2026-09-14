import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { CURRENCIES } from '../lib/types'
import { useLocale } from '../context/LocaleContext'
import { AppHeader } from '../components/AppHeader'
import { GroupAppearancePicker } from '../components/GroupAppearancePicker'

export function NewGroupPage() {
  const navigate = useNavigate()
  const { t } = useLocale()
  const [name, setName] = useState('')
  const [currency, setCurrency] = useState('EUR')
  const [color, setColor] = useState('Blue')
  const [icon, setIcon] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setBusy(true)
    const { data, error } = await supabase.rpc('create_group', {
      p_name: name,
      p_currency: currency,
    })
    if (error) {
      setBusy(false)
      setError(error.message)
      return
    }
    // create_group() only takes name/currency — color/icon are set with a follow-up update,
    // same two-step flow the "Edit color & icon" overlay on an existing group uses.
    await supabase.from('groups').update({ color, icon }).eq('id', data)
    setBusy(false)
    navigate(`/groups/${data}`)
  }

  return (
    <div className="page">
      <AppHeader title={t('NewGroup_Title')} back />

      <form onSubmit={handleSubmit}>
        <div className="field">
          <label htmlFor="name">{t('NewGroup_GroupName')}</label>
          <input id="name" required value={name} onChange={(e) => setName(e.target.value)} />
        </div>
        <div className="field">
          <label htmlFor="currency">{t('NewGroup_Currency')}</label>
          <select id="currency" value={currency} onChange={(e) => setCurrency(e.target.value)}>
            {CURRENCIES.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </select>
        </div>

        <div className="field">
          <label>{t('NewGroup_Appearance')}</label>
          <GroupAppearancePicker color={color} icon={icon} onColorChange={setColor} onIconChange={setIcon} />
        </div>

        {error && <p className="error-text">{error}</p>}

        <button type="submit" className="btn btn-primary" disabled={busy}>
          {busy ? t('Common_Creating') : t('NewGroup_CreateButton')}
        </button>
      </form>
    </div>
  )
}
