import { useEffect, useState, type ChangeEvent, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { FunctionsHttpError } from '@supabase/supabase-js'
import { supabase } from '../lib/supabaseClient'
import { useAuth } from '../context/AuthContext'
import { useLocale, type Language } from '../context/LocaleContext'
import { resizeImageToWebp } from '../lib/imageResize'
import type { MyMember } from '../lib/types'
import { AppHeader } from '../components/AppHeader'
import './ProfilePage.css'

const AVATAR_BUCKET = 'avatars'

export function ProfilePage() {
  const { session } = useAuth()
  const { t, override, setOverride } = useLocale()
  const navigate = useNavigate()

  const [member, setMember] = useState<MyMember | null>(null)
  const [displayName, setDisplayName] = useState('')
  const [birthDate, setBirthDate] = useState('')
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

  useEffect(() => {
    load()
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
      .select('id, display_name, birth_date, avatar_path')
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
    setAvatarUrl(row.avatar_path ? publicAvatarUrl(row.avatar_path) : null)
  }

  async function handleSaveProfile(e: FormEvent) {
    e.preventDefault()
    if (!member) return
    setError(null)
    setNotice(null)
    setSavingProfile(true)
    const { error } = await supabase
      .from('members')
      .update({ display_name: displayName, birth_date: birthDate || null })
      .eq('id', member.id)
    setSavingProfile(false)
    if (error) return setError(error.message)
    setMember({ ...member, display_name: displayName, birth_date: birthDate || null })
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
          <input id="birthDate" type="date" value={birthDate} onChange={(e) => setBirthDate(e.target.value)} />
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
