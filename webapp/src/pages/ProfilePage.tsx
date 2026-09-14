import { useEffect, useState, type ChangeEvent, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { FunctionsHttpError } from '@supabase/supabase-js'
import { supabase } from '../lib/supabaseClient'
import { useAuth } from '../context/AuthContext'
import { useLocale, type Language } from '../context/LocaleContext'
import { resizeImageToWebp } from '../lib/imageResize'
import { useInstallPrompt } from '../lib/useInstallPrompt'
import { getPushState, registerForPush, unregisterFromPush, type PushState } from '../lib/pushNotifications'
import type { CalendarSubscription, MyMember } from '../lib/types'
import { AppHeader } from '../components/AppHeader'
import { DateField } from '../components/DateField'
import './ProfilePage.css'

const AVATAR_BUCKET = 'avatars'

export function ProfilePage() {
  const { session } = useAuth()
  const { t, override, setOverride } = useLocale()
  const navigate = useNavigate()
  const { installed, canPrompt, promptInstall, showIosInstructions } = useInstallPrompt()

  const [member, setMember] = useState<MyMember | null>(null)
  const [calendarSubscription, setCalendarSubscription] = useState<CalendarSubscription | null>(null)
  const [copyNotice, setCopyNotice] = useState(false)
  const [displayName, setDisplayName] = useState('')
  const [birthDate, setBirthDate] = useState('')
  const [carExtraSeats, setCarExtraSeats] = useState('')
  const [avatarUrl, setAvatarUrl] = useState<string | null>(null)

  const [newEmail, setNewEmail] = useState('')
  const [newPassword, setNewPassword] = useState('')

  const [savingProfile, setSavingProfile] = useState(false)
  const [savingAvatar, setSavingAvatar] = useState(false)
  const [savingEmail, setSavingEmail] = useState(false)
  const [savingPassword, setSavingPassword] = useState(false)
  const [deleting, setDeleting] = useState(false)

  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const [pushState, setPushState] = useState<PushState>('unsupported')
  const [savingPush, setSavingPush] = useState(false)

  useEffect(() => {
    load()
    if (session) getPushState(session.user.id).then(setPushState)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  function publicAvatarUrl(path: string) {
    return supabase.storage.from(AVATAR_BUCKET).getPublicUrl(path).data.publicUrl
  }

  async function load() {
    if (!session) return
    setError(null)
    // Same "one account, one member row" lookup as MAUI's GetMyMemberAsync —
    // ordered by created_at so a stray duplicate row (documented, unresolved
    // edge case in the MAUI app) resolves the same way on both clients.
    const { data, error } = await supabase
      .from('members')
      .select('id, display_name, birth_date, avatar_path, car_extra_seats')
      .eq('account_id', session.user.id)
      .order('created_at', { ascending: true })
      .limit(1)
      .maybeSingle()

    if (error) return setError(error.message)
    if (!data) return

    const row = data as MyMember
    setMember(row)
    setDisplayName(row.display_name ?? '')
    setBirthDate(row.birth_date ?? '')
    setCarExtraSeats(row.car_extra_seats?.toString() ?? '')
    setAvatarUrl(row.avatar_path ? publicAvatarUrl(row.avatar_path) : null)

    const { data: existingSub } = await supabase
      .from('calendar_subscriptions')
      .select('id, member_id, token, created_at, last_accessed_at')
      .eq('member_id', row.id)
      .maybeSingle()
    if (existingSub) {
      setCalendarSubscription(existingSub as CalendarSubscription)
    } else {
      const { data: createdSub, error: subError } = await supabase
        .from('calendar_subscriptions')
        .insert({ member_id: row.id, token: generateFeedToken() })
        .select('id, member_id, token, created_at, last_accessed_at')
        .single()
      if (!subError) setCalendarSubscription(createdSub as CalendarSubscription)
    }
  }

  // Same random-token shape as SupabaseCalendarSubscriptionsRepository.GenerateToken — set
  // explicitly on insert rather than left to the DB's own `default encode(gen_random_bytes(24),
  // 'base64url')`, since Postgrest sends every plain column on insert regardless and would
  // otherwise silently override it (see that repository's own remarks on the exact bug this
  // avoids for invites.token).
  function generateFeedToken(): string {
    const bytes = crypto.getRandomValues(new Uint8Array(24))
    let binary = ''
    bytes.forEach((b) => (binary += String.fromCharCode(b)))
    return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
  }

  function calendarFeedUrl(token: string): string {
    return `${import.meta.env.VITE_SUPABASE_URL}/functions/v1/calendar-feed/${encodeURIComponent(token)}.ics`
  }

  async function handleCopyCalendarLink() {
    if (!calendarSubscription) return
    await navigator.clipboard.writeText(calendarFeedUrl(calendarSubscription.token))
    setCopyNotice(true)
    setTimeout(() => setCopyNotice(false), 2000)
  }

  async function handleShareCalendarLink() {
    if (!calendarSubscription) return
    const url = calendarFeedUrl(calendarSubscription.token)
    if (navigator.share) {
      try {
        await navigator.share({ url })
      } catch {
        // Share sheet dismissed — no error to surface.
      }
    } else {
      await handleCopyCalendarLink()
    }
  }

  async function handleRegenerateCalendarLink() {
    if (!calendarSubscription) return
    if (!window.confirm(t('Profile_RegenerateCalendarLinkConfirm'))) return
    const nextToken = generateFeedToken()
    const { error } = await supabase
      .from('calendar_subscriptions')
      .update({ token: nextToken })
      .eq('id', calendarSubscription.id)
    if (error) return setError(error.message)
    setCalendarSubscription({ ...calendarSubscription, token: nextToken })
  }

  async function handleSaveProfile(e: FormEvent) {
    e.preventDefault()
    if (!member) return
    setError(null)
    setNotice(null)
    setSavingProfile(true)
    const parsedSeats = carExtraSeats.trim() === '' ? null : Number.parseInt(carExtraSeats, 10)
    const seats = parsedSeats !== null && Number.isFinite(parsedSeats) ? parsedSeats : null
    const { error } = await supabase
      .from('members')
      .update({ display_name: displayName, birth_date: birthDate || null, car_extra_seats: seats })
      .eq('id', member.id)
    setSavingProfile(false)
    if (error) return setError(error.message)
    setMember({ ...member, display_name: displayName, birth_date: birthDate || null, car_extra_seats: seats })
    setNotice(t('Profile_ProfileSaved'))
  }

  async function handleAvatarChange(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file || !member) return
    setError(null)
    setNotice(null)
    setSavingAvatar(true)
    try {
      const blob = await resizeImageToWebp(file)
      const path = `${member.id}/${crypto.randomUUID()}.webp`
      const { error: uploadError } = await supabase.storage
        .from(AVATAR_BUCKET)
        .upload(path, blob, { contentType: 'image/webp' })
      if (uploadError) throw uploadError

      const previousPath = member.avatar_path
      const { error: updateError } = await supabase
        .from('members')
        .update({ avatar_path: path })
        .eq('id', member.id)
      if (updateError) throw updateError

      if (previousPath) await supabase.storage.from(AVATAR_BUCKET).remove([previousPath])

      setMember({ ...member, avatar_path: path })
      setAvatarUrl(publicAvatarUrl(path))
    } catch (err) {
      setError(err instanceof Error ? err.message : t('Common_Error'))
    } finally {
      setSavingAvatar(false)
    }
  }

  async function handleRemoveAvatar() {
    if (!member?.avatar_path) return
    setError(null)
    setNotice(null)
    setSavingAvatar(true)
    const previousPath = member.avatar_path
    const { error } = await supabase.from('members').update({ avatar_path: null }).eq('id', member.id)
    setSavingAvatar(false)
    if (error) return setError(error.message)
    // Best-effort cleanup — a leftover orphan file is harmless, same treatment the MAUI app gives it.
    await supabase.storage.from(AVATAR_BUCKET).remove([previousPath])
    setMember({ ...member, avatar_path: null })
    setAvatarUrl(null)
  }

  async function handleChangeEmail(e: FormEvent) {
    e.preventDefault()
    if (!newEmail) return
    setError(null)
    setNotice(null)
    setSavingEmail(true)
    const { error } = await supabase.auth.updateUser({ email: newEmail })
    setSavingEmail(false)
    if (error) return setError(error.message)
    setNewEmail('')
    setNotice(t('Profile_EmailUpdateSent'))
  }

  async function handleChangePassword(e: FormEvent) {
    e.preventDefault()
    if (!newPassword) return
    setError(null)
    setNotice(null)
    setSavingPassword(true)
    const { error } = await supabase.auth.updateUser({ password: newPassword })
    setSavingPassword(false)
    if (error) return setError(error.message)
    setNewPassword('')
    setNotice(t('Profile_PasswordUpdated'))
  }

  async function handleEnablePush() {
    if (!session) return
    setError(null)
    setNotice(null)
    setSavingPush(true)
    try {
      const result = await registerForPush(session.user.id)
      setPushState(result)
      if (result === 'denied') setError(t('Profile_PushDenied'))
    } catch (err) {
      setError(err instanceof Error ? err.message : t('Common_Error'))
    } finally {
      setSavingPush(false)
    }
  }

  async function handleDisablePush() {
    if (!session) return
    setError(null)
    setNotice(null)
    setSavingPush(true)
    try {
      await unregisterFromPush()
      setPushState(await getPushState(session.user.id))
    } catch (err) {
      setError(err instanceof Error ? err.message : t('Common_Error'))
    } finally {
      setSavingPush(false)
    }
  }

  async function handleDeleteAccount() {
    if (!window.confirm(t('Profile_DeleteAccountConfirm'))) return
    setError(null)
    setNotice(null)
    setDeleting(true)

    const { error } = await supabase.functions.invoke('delete-account')
    if (error) {
      let message = error.message
      if (error instanceof FunctionsHttpError) {
        try {
          const body = await error.context.json()
          if (body?.error) message = body.error
        } catch {
          // fall back to the generic FunctionsHttpError message
        }
      }
      setError(message)
      setDeleting(false)
      return
    }

    // A failed remote sign-out here is harmless — the account (and its
    // session) is already gone server-side; ProtectedRoute's next auth check
    // sends us to /login regardless. Same reasoning as SupabaseAuthService's
    // DeleteAccountAsync in the MAUI app.
    await supabase.auth.signOut().catch(() => {})
    navigate('/login')
  }

  if (!member) {
    return (
      <div className="page">
        <AppHeader title={t('Profile_Title')} back />
        {error ? <p className="error-text">{error}</p> : <div className="spinner">{t('Common_Loading')}</div>}
      </div>
    )
  }

  return (
    <div className="page">
      <AppHeader title={t('Profile_Title')} back />

      {error && <p className="error-text">{error}</p>}
      {notice && <p className="notice-text">{notice}</p>}

      <section className="card profile-section avatar-section">
        <div className="avatar-preview">
          {avatarUrl ? (
            <img src={avatarUrl} alt="" />
          ) : (
            <span className="avatar-fallback">{displayName.slice(0, 1).toUpperCase() || '?'}</span>
          )}
        </div>
        <div className="avatar-actions">
          <label className="btn btn-outline avatar-upload-btn">
            {savingAvatar ? t('Common_Saving') : t('Profile_ChangePhoto')}
            <input type="file" accept="image/*" onChange={handleAvatarChange} disabled={savingAvatar} hidden />
          </label>
          {member.avatar_path && (
            <button type="button" className="btn btn-outline" onClick={handleRemoveAvatar} disabled={savingAvatar}>
              {t('Profile_RemovePhoto')}
            </button>
          )}
        </div>
      </section>

      <form onSubmit={handleSaveProfile} className="card profile-section">
        <div className="field">
          <label htmlFor="displayName">{t('Profile_DisplayName')}</label>
          <input
            id="displayName"
            required
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            placeholder={t('Profile_DisplayNamePlaceholder')}
          />
        </div>
        <div className="field">
          <label htmlFor="birthDate">{t('Profile_Birthday')}</label>
          <DateField id="birthDate" value={birthDate} onChange={setBirthDate} />
        </div>
        <div className="field">
          <label htmlFor="carExtraSeats">{t('Profile_CarSeats')}</label>
          <input
            id="carExtraSeats"
            type="number"
            min="0"
            step="1"
            value={carExtraSeats}
            onChange={(e) => setCarExtraSeats(e.target.value)}
            placeholder={t('Profile_CarSeatsPlaceholder')}
          />
          <p className="field-hint">{t('Profile_CarSeatsHint')}</p>
        </div>
        <button type="submit" className="btn btn-primary" disabled={savingProfile}>
          {savingProfile ? t('Common_Saving') : t('Common_Save')}
        </button>
      </form>

      <section className="card profile-section">
        <h2 className="section-title">{t('Profile_Language')}</h2>
        <div className="language-options">
          {([['', 'Profile_LanguageSystem'], ['en', 'Profile_LanguageEnglish'], ['es', 'Profile_LanguageSpanish']] as const).map(
            ([value, key]) => (
              <label key={value || 'system'} className="language-option">
                <input
                  type="radio"
                  name="language"
                  checked={override === value}
                  onChange={() => setOverride(value as Language | '')}
                />
                {t(key)}
              </label>
            ),
          )}
        </div>
      </section>

      {!installed && (canPrompt || showIosInstructions) && (
        <section className="card profile-section">
          <h2 className="section-title">{t('Profile_InstallAppSection')}</h2>
          <p className="field-hint">
            {showIosInstructions ? t('Profile_InstallAppIosHint') : t('Profile_InstallAppHint')}
          </p>
          {canPrompt && (
            <button type="button" className="btn btn-outline" onClick={promptInstall}>
              {t('Profile_InstallAppButton')}
            </button>
          )}
        </section>
      )}

      {pushState !== 'unsupported' && (
        <section className="card profile-section">
          <h2 className="section-title">{t('Profile_NotificationsSection')}</h2>
          <p className="field-hint">
            {pushState === 'denied' ? t('Profile_PushDenied') : t('Profile_NotificationsHint')}
          </p>
          {pushState === 'subscribed' ? (
            <button type="button" className="btn btn-outline" onClick={handleDisablePush} disabled={savingPush}>
              {savingPush ? t('Common_Saving') : t('Profile_DisableNotifications')}
            </button>
          ) : (
            <button
              type="button"
              className="btn btn-outline"
              onClick={handleEnablePush}
              disabled={savingPush || pushState === 'denied'}
            >
              {savingPush ? t('Common_Saving') : t('Profile_EnableNotifications')}
            </button>
          )}
        </section>
      )}

      <section className="card profile-section">
        <h2 className="section-title">{t('Profile_CalendarFeedSection')}</h2>
        <p className="field-hint">{t('Profile_CalendarFeedDescription')}</p>
        {calendarSubscription && (
          <div className="field">
            <input readOnly value={calendarFeedUrl(calendarSubscription.token)} onFocus={(e) => e.target.select()} />
          </div>
        )}
        {copyNotice && <p className="notice-text">{t('Profile_CalendarLinkCopied')}</p>}
        <div className="calendar-feed-actions">
          <button type="button" className="btn btn-outline" onClick={handleCopyCalendarLink} disabled={!calendarSubscription}>
            {t('Profile_CopyCalendarLink')}
          </button>
          <button type="button" className="btn btn-outline" onClick={handleShareCalendarLink} disabled={!calendarSubscription}>
            {t('Profile_ShareCalendarLink')}
          </button>
        </div>
        <button
          type="button"
          className="btn btn-danger"
          onClick={handleRegenerateCalendarLink}
          disabled={!calendarSubscription}
        >
          {t('Profile_RegenerateCalendarLink')}
        </button>
      </section>

      <form onSubmit={handleChangeEmail} className="card profile-section">
        <h2 className="section-title">{t('Profile_Email')}</h2>
        <p className="field-hint">{session?.user.email}</p>
        <div className="field">
          <input
            type="email"
            required
            value={newEmail}
            onChange={(e) => setNewEmail(e.target.value)}
            placeholder={t('Profile_NewEmailPlaceholder')}
          />
        </div>
        <button type="submit" className="btn btn-outline" disabled={savingEmail}>
          {savingEmail ? t('Common_Saving') : t('Profile_ChangeEmail')}
        </button>
      </form>

      <form onSubmit={handleChangePassword} className="card profile-section">
        <h2 className="section-title">{t('Profile_Password')}</h2>
        <div className="field">
          <input
            type="password"
            required
            minLength={6}
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
            placeholder={t('Profile_NewPasswordPlaceholder')}
          />
        </div>
        <button type="submit" className="btn btn-outline" disabled={savingPassword}>
          {savingPassword ? t('Common_Saving') : t('Profile_ChangePassword')}
        </button>
      </form>

      <section className="card profile-section danger-zone">
        <h2 className="section-title">{t('Profile_DeleteAccountSection')}</h2>
        <p className="field-hint">{t('Profile_DeleteAccountDescription')}</p>
        <button type="button" className="btn btn-danger" onClick={handleDeleteAccount} disabled={deleting}>
          {deleting ? t('Common_Saving') : t('Profile_DeleteAccountButton')}
        </button>
      </section>
    </div>
  )
}
