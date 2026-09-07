import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { useLocale } from '../context/LocaleContext'
import { AppHeader } from '../components/AppHeader'

function extractCode(input: string): string {
  const trimmed = input.trim()
  try {
    const url = new URL(trimmed)
    const code = url.searchParams.get('code')
    if (code) return code
  } catch {
    // not a URL — treat the whole input as the code
  }
  return trimmed
}

export function JoinGroupPage() {
  const navigate = useNavigate()
  const { t } = useLocale()
  const [input, setInput] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setBusy(true)
    const { data, error } = await supabase.rpc('redeem_invite', {
      p_token: extractCode(input),
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
      <AppHeader title={t('JoinGroup_JoinTitle')} back />

      <form onSubmit={handleSubmit}>
        <div className="field">
          <label htmlFor="code">{t('JoinGroup_CodeOrLinkLabel')}</label>
          <input
            id="code"
            required
            value={input}
            onChange={(e) => setInput(e.target.value)}
            placeholder={t('JoinGroup_CodePlaceholder')}
          />
        </div>

        {error && <p className="error-text">{error}</p>}

        <button type="submit" className="btn btn-primary" disabled={busy}>
          {busy ? t('JoinGroup_Joining') : t('JoinGroup_JoinButton')}
        </button>
      </form>
    </div>
  )
}
