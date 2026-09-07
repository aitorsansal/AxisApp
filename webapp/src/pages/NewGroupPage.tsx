import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { CURRENCIES } from '../lib/types'
import { AppHeader } from '../components/AppHeader'

export function NewGroupPage() {
  const navigate = useNavigate()
  const [name, setName] = useState('')
  const [currency, setCurrency] = useState('EUR')
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
    setBusy(false)
    if (error) {
      setError(error.message)
      return
    }
    navigate(`/groups/${data}`)
  }

  return (
    <div className="page">
      <AppHeader title="New group" back />

      <form onSubmit={handleSubmit}>
        <div className="field">
          <label htmlFor="name">Name</label>
          <input id="name" required value={name} onChange={(e) => setName(e.target.value)} />
        </div>
        <div className="field">
          <label htmlFor="currency">Currency</label>
          <select id="currency" value={currency} onChange={(e) => setCurrency(e.target.value)}>
            {CURRENCIES.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </select>
        </div>

        {error && <p className="error-text">{error}</p>}

        <button type="submit" className="btn btn-primary" disabled={busy}>
          {busy ? 'Creating…' : 'Create group'}
        </button>
      </form>
    </div>
  )
}
