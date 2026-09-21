import { useState, type FormEvent } from 'react'
import { Navigate } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { useAuth } from '../context/AuthContext'
import { useLocale } from '../context/LocaleContext'
import './LoginPage.css'

// Static "email confirmed" page under web/confirm/ — same page the MAUI app's sign-up points at
// (AppConstants.Links.EmailConfirmedUrl). Must be in Supabase Auth's redirect allow-list.
const EMAIL_CONFIRMED_URL = 'https://axisapp.aitorsansal.com/confirm/'

export function LoginPage() {
  const { session } = useAuth()
  const { t } = useLocale()
  const [mode, setMode] = useState<'signin' | 'signup'>('signin')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [unconfirmedEmail, setUnconfirmedEmail] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  if (session) return <Navigate to="/" replace />

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setNotice(null)
    setUnconfirmedEmail(null)
    setBusy(true)
    try {
      if (mode === 'signin') {
        const { error } = await supabase.auth.signInWithPassword({ email, password })
        if (error?.code === 'email_not_confirmed') {
          setUnconfirmedEmail(email)
          setError(t('Login_EmailNotConfirmed'))
          return
        }
        if (error?.code === 'invalid_credentials') {
          setError(t('Login_InvalidCredentials'))
          return
        }
        if (error) throw error
      } else {
        const { data, error } = await supabase.auth.signUp({
          email,
          password,
          options: { emailRedirectTo: EMAIL_CONFIRMED_URL },
        })
        if (error) throw error
        // With "Confirm email" on, the account exists but has no session until the link is
        // clicked. With it off, the session is set and AuthContext redirects on its own.
        if (!data.session) {
          setPassword('')
          setMode('signin')
          setNotice(t('Login_CheckInbox', email))
        }
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : t('Common_Error'))
    } finally {
      setBusy(false)
    }
  }

  async function handleResend() {
    if (!unconfirmedEmail) return
    setError(null)
    setBusy(true)
    const { error } = await supabase.auth.resend({
      type: 'signup',
      email: unconfirmedEmail,
      options: { emailRedirectTo: EMAIL_CONFIRMED_URL },
    })
    setBusy(false)
    // Supabase allows one auth email per address per 60s, counted from the last one sent —
    // usually the sign-up email itself, so resending right after signing up always hits this.
    if (error?.code === 'over_email_send_rate_limit') return setError(t('Login_ResendRateLimited'))
    if (error) return setError(error.message)
    setUnconfirmedEmail(null)
    setNotice(t('Login_CheckInbox', unconfirmedEmail))
  }

  async function handleGoogle() {
    setError(null)
    const { error } = await supabase.auth.signInWithOAuth({
      provider: 'google',
      options: { redirectTo: window.location.origin },
    })
    if (error) setError(error.message)
  }

  return (
    <div className="page login-page">
      <h1 className="login-title">Axis</h1>
      <p className="login-subtitle">
        {mode === 'signin' ? t('Login_SigninSubtitle') : t('Login_SignupSubtitle')}
      </p>

      <button type="button" className="btn google-btn" onClick={handleGoogle}>
        <GoogleIcon />
        {t('Login_ContinueWithGoogle')}
      </button>

      <div className="divider">
        <span>{t('Login_Or')}</span>
      </div>

      <form onSubmit={handleSubmit}>
        <div className="field">
          <label htmlFor="email">{t('Login_Email')}</label>
          <input
            id="email"
            type="email"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            autoComplete="email"
          />
        </div>
        <div className="field">
          <label htmlFor="password">{t('Login_Password')}</label>
          <input
            id="password"
            type="password"
            required
            minLength={mode === 'signup' ? 8 : undefined}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete={mode === 'signin' ? 'current-password' : 'new-password'}
          />
        </div>

        {error && <p className="error-text">{error}</p>}
        {notice && <p className="notice-text">{notice}</p>}
        {unconfirmedEmail && (
          <button type="button" className="mode-toggle resend-link" onClick={handleResend} disabled={busy}>
            {t('Login_ResendConfirmation')}
          </button>
        )}

        <button type="submit" className="btn btn-primary" disabled={busy}>
          {busy ? t('Common_PleaseWait') : mode === 'signin' ? t('Login_SignIn') : t('Login_SignUp')}
        </button>
      </form>

      <button
        type="button"
        className="mode-toggle"
        onClick={() => setMode(mode === 'signin' ? 'signup' : 'signin')}
      >
        {mode === 'signin' ? t('Login_ToggleToSignup') : t('Login_ToggleToSignin')}
      </button>
    </div>
  )
}

function GoogleIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 18 18" aria-hidden="true">
      <path
        fill="#4285F4"
        d="M17.64 9.2c0-.64-.06-1.25-.16-1.84H9v3.48h4.84a4.14 4.14 0 0 1-1.8 2.72v2.26h2.9c1.7-1.57 2.7-3.88 2.7-6.62z"
      />
      <path
        fill="#34A853"
        d="M9 18c2.43 0 4.47-.8 5.96-2.18l-2.9-2.26c-.8.54-1.84.86-3.06.86-2.35 0-4.34-1.59-5.05-3.72H.96v2.33A9 9 0 0 0 9 18z"
      />
      <path
        fill="#FBBC05"
        d="M3.95 10.7A5.4 5.4 0 0 1 3.67 9c0-.59.1-1.17.28-1.7V4.97H.96A9 9 0 0 0 0 9c0 1.45.35 2.83.96 4.03l2.99-2.33z"
      />
      <path
        fill="#EA4335"
        d="M9 3.58c1.32 0 2.5.45 3.44 1.35l2.58-2.58C13.46.89 11.43 0 9 0A9 9 0 0 0 .96 4.97l2.99 2.33C4.66 5.17 6.65 3.58 9 3.58z"
      />
    </svg>
  )
}
