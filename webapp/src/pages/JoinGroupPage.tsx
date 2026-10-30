import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { useLocale } from '../context/LocaleContext'
import { AppHeader } from '../components/AppHeader'
import {
  inviteErrorKey,
  isJoinable,
  looksLikeInviteCode,
  parseInvitePreview,
  redeemErrorStatus,
  type InvitePreview,
} from '../lib/invitePreview'

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
  const [searchParams] = useSearchParams()
  const { t } = useLocale()
  // Prefilled from an invite link (/join?code=, see MembersPage's buildInviteUrl).
  const [input, setInput] = useState(searchParams.get('code') ?? '')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [preview, setPreview] = useState<InvitePreview | null>(null)

  // Read-only look at what this invite does (preview_invite). A failed lookup just means no
  // card: joining still works and redeem_invite reports its own error.
  useEffect(() => {
    const code = extractCode(input)
    setPreview(null)
    if (!looksLikeInviteCode(code)) return
    let stale = false
    const handle = setTimeout(async () => {
      const { data, error } = await supabase.rpc('preview_invite', { p_token: code })
      if (stale || error) return
      setPreview(parseInvitePreview(data))
    }, 400)
    return () => {
      stale = true
      clearTimeout(handle)
    }
  }, [input])

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    if (preview && !isJoinable(preview.status)) return
    setError(null)
    setBusy(true)
    const { data, error } = await supabase.rpc('redeem_invite', {
      p_token: extractCode(input),
    })
    setBusy(false)
    if (error) {
      const status = redeemErrorStatus(error.message)
      setError(status ? t(inviteErrorKey(status)) : error.message)
      return
    }
    navigate(`/groups/${data}`, { replace: true })
  }

  const blocked = preview !== null && !isJoinable(preview.status)

  return (
    <div className="page">
      <AppHeader title={t('JoinGroup_JoinTitle')} backTo="/" />

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

        {preview && (
          <div className="card" role="status">
            {blocked ? (
              <p className="error-text">{t(inviteErrorKey(preview.status))}</p>
            ) : preview.status === 'already_member' ? (
              <p>{t('JoinGroup_PreviewAlready', preview.group_name ?? '')}</p>
            ) : (
              <>
                <h2 className="section-title">
                  {preview.target_name
                    ? t('JoinGroup_PreviewClaim', preview.target_name, preview.group_name ?? '')
                    : t('JoinGroup_PreviewJoin', preview.group_name ?? '')}
                </h2>
                <p className="page-context">
                  {t('JoinGroup_PreviewMeta', preview.member_count ?? 0, preview.group_currency ?? '')}
                </p>
                {preview.target_name && <p>{t('JoinGroup_PreviewClaimNote')}</p>}
              </>
            )}
          </div>
        )}

        {error && <p className="error-text">{error}</p>}

        <button type="submit" className="btn btn-primary" disabled={busy || blocked}>
          {busy
            ? t('JoinGroup_Joining')
            : preview?.status === 'already_member'
              ? t('JoinGroup_OpenGroup')
              : t('JoinGroup_JoinButton')}
        </button>
      </form>
    </div>
  )
}
