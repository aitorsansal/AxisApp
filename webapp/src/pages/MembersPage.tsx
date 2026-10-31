import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { useAuth } from '../context/AuthContext'
import { useAliases } from '../context/AliasesContext'
import { useLocale } from '../context/LocaleContext'
import type { Group, MemberInfo, MemberRow } from '../lib/types'
import {
  canAddByName,
  closestExpiryIndex,
  EXPIRY_HOURS,
  MAX_USES_CEILING,
  pickReusableInvite,
  type ReusableInvite,
} from '../lib/inviteReuse'
import { AppHeader } from '../components/AppHeader'
import { Avatar } from '../components/Avatar'
import { Icon } from '../components/Icon'
import { QrCode } from '../components/QrCode'
import '../components/TogetherPage.css'
import './MembersPage.css'

// Same URL as AppConstants.Links.BuildInviteUrl: this app's own /join page, which the Android app
// also claims as an App Link. Absolute so links copied from a localhost dev build still work.
function buildInviteUrl(code: string): string {
  return `https://app.axisapp.aitorsansal.com/join?code=${encodeURIComponent(code)}`
}

// Same token shape as AxisApp/Services/SupabaseInvitesRepository.cs's
// GenerateToken — a plain [Column] property with no DB default trust (the
// table's own gen_random_bytes default was silently overridden once before,
// see that file's remarks), generated client-side instead.
function generateToken(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(9))
  let binary = ''
  for (const b of bytes) binary += String.fromCharCode(b)
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

const INVITE_COLUMNS = 'id, token, expires_at, max_uses, use_count'
const EXPIRY_KEYS = ['Members_Expires1Hour', 'Members_Expires1Day', 'Members_Expires7Days', 'Members_Expires30Days']

type InviteRow = ReusableInvite & { id: string }

export function MembersPage() {
  const { groupId } = useParams<{ groupId: string }>()
  const { session } = useAuth()
  const { displayName, initials, avatarUrl, refresh: refreshAliases } = useAliases()
  const { t } = useLocale()

  const [group, setGroup] = useState<Group | null>(null)
  const [members, setMembers] = useState<MemberRow[]>([])
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const [nameInput, setNameInput] = useState('')
  const [matches, setMatches] = useState<MemberInfo[]>([])
  const [busy, setBusy] = useState(false)
  const busyRef = useRef(false)
  const [manualLink, setManualLink] = useState<string | null>(null)
  const [generalInvite, setGeneralInvite] = useState<InviteRow | null>(null)
  const [inviteSaving, setInviteSaving] = useState(false)
  const [renamingId, setRenamingId] = useState<string | null>(null)
  const [renameInput, setRenameInput] = useState('')
  const renameButtonsRef = useRef(new Map<string, HTMLButtonElement>())

  const load = useCallback(async () => {
    if (!groupId) return
    setError(null)
    const [groupRes, membersRes] = await Promise.all([
      supabase.from('groups').select('id, name, currency, created_by').eq('id', groupId).single(),
      supabase
        .from('group_members')
        .select('member_id, members(id, display_name, account_id, avatar_path)')
        .eq('group_id', groupId),
    ])
    if (groupRes.error) return setError(groupRes.error.message)
    if (membersRes.error) return setError(membersRes.error.message)
    setGroup(groupRes.data as Group)
    setMembers((membersRes.data as unknown as MemberRow[]) ?? [])
  }, [groupId])

  useEffect(() => {
    load()
  }, [load])

  useEffect(() => {
    const query = nameInput.trim()
    if (query.length < 2) {
      setMatches([])
      return
    }
    const handle = setTimeout(async () => {
      // RLS on `members` already scopes this to whoever the current account
      // can see (shared group, created by them, or their own claimed row) —
      // same SearchVisibleByNameAsync behavior IMembersRepository has.
      const { data, error } = await supabase
        .from('members')
        .select('id, display_name, account_id, avatar_path')
        .ilike('display_name', `%${query}%`)
        .limit(10)
      if (error || !data) return
      const currentIds = new Set(members.map((m) => m.member_id))
      setMatches((data as MemberInfo[]).filter((m) => !currentIds.has(m.id)))
    }, 300)
    return () => clearTimeout(handle)
  }, [nameInput, members])

  // Mirrors SupabaseInvitesRepository.GetOrCreateAsync: reuse a still-usable invite for this
  // (group, target) pair, otherwise mint one. Null (with `error` set) on failure.
  async function fetchOrCreateInvite(targetMemberId?: string): Promise<InviteRow | null> {
    let query = supabase.from('invites').select(INVITE_COLUMNS).eq('group_id', groupId)
    query = targetMemberId ? query.eq('target_member_id', targetMemberId) : query.is('target_member_id', null)
    // created_at first so an invite keeps being the one shown after its own expiry is edited;
    // rows written before the Invite.CreatedAt fix all tie at 0001-01-01, so expires_at breaks ties.
    const { data: existing, error: lookupError } = await query
      .order('created_at', { ascending: false })
      .order('expires_at', { ascending: false })
      .limit(20)
    if (lookupError) {
      setError(lookupError.message)
      return null
    }
    const reusable = pickReusableInvite(existing as InviteRow[] | null, Date.now())
    if (reusable) return reusable as InviteRow

    const { data: created, error: insertError } = await supabase
      .from('invites')
      .insert({
        group_id: groupId,
        target_member_id: targetMemberId ?? null,
        token: generateToken(),
        created_by: session?.user.id,
        expires_at: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(),
      })
      .select(INVITE_COLUMNS)
      .single()
    if (insertError) {
      setError(insertError.message)
      return null
    }
    return created as InviteRow
  }

  // Like the Android invite page, opening this screen shows (and if needed creates) the group's
  // general invite; reuse keeps repeated visits from minting new ones.
  useEffect(() => {
    if (!groupId || !session) return
    let cancelled = false
    fetchOrCreateInvite().then((invite) => {
      if (!cancelled && invite) setGeneralInvite(invite)
    })
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [groupId, session?.user.id])

  // max_uses/expires_at are the only invite columns a member may change (protect_invite_columns).
  async function updateGeneralInvite(patch: { max_uses?: number; expires_at?: string }) {
    if (!generalInvite) return
    setError(null)
    setInviteSaving(true)
    const { data, error } = await supabase
      .from('invites')
      .update(patch)
      .eq('id', generalInvite.id)
      .select(INVITE_COLUMNS)
      .single()
    setInviteSaving(false)
    if (error) return setError(error.message)
    setGeneralInvite(data as InviteRow)
  }

  // Only runs from an explicit copy click; adding a guest never mints an invite or writes the
  // clipboard.
  async function copyInvite(target?: MemberRow['members']) {
    setError(null)
    setNotice(null)
    setManualLink(null)
    const invite =
      !target && generalInvite && pickReusableInvite([generalInvite], Date.now())
        ? generalInvite
        : await fetchOrCreateInvite(target?.id)
    if (!invite) return
    if (!target) setGeneralInvite(invite)

    const url = buildInviteUrl(invite.token)
    try {
      await navigator.clipboard.writeText(url)
    } catch {
      setManualLink(url)
      return
    }
    setNotice(target ? t('Members_NewLinkCopied', displayName(target)) : t('Members_LinkCopied'))
  }

  async function handleAddPhantom() {
    const trimmed = nameInput.trim()
    if (!trimmed || !groupId || busyRef.current) return
    busyRef.current = true
    setBusy(true)
    setError(null)
    setNotice(null)
    setManualLink(null)
    try {
      const { data: phantom, error: memberError } = await supabase
        .from('members')
        .insert({ display_name: trimmed, created_by: session?.user.id })
        .select('id')
        .single()
      if (memberError) return setError(memberError.message)
      const { error: joinError } = await supabase
        .from('group_members')
        .insert({ group_id: groupId, member_id: phantom.id })
      if (joinError) return setError(joinError.message)
      setNameInput('')
      setMatches([])
      setNotice(t('Members_GuestAdded', trimmed))
      await load()
    } finally {
      busyRef.current = false
      setBusy(false)
    }
  }

  async function handleLinkExisting(member: MemberInfo) {
    if (!groupId) return
    setBusy(true)
    setError(null)
    setNotice(null)
    const { error } = await supabase
      .from('group_members')
      .insert({ group_id: groupId, member_id: member.id })
    setBusy(false)
    if (error) return setError(error.message)
    setNameInput('')
    setMatches([])
    await load()
  }

  async function handleRemove(row: MemberRow) {
    if (!groupId) return
    if (!window.confirm(t('Members_RemoveConfirm'))) return
    setError(null)
    const { error } = await supabase.rpc('remove_group_member', {
      p_group_id: groupId,
      p_member_id: row.member_id,
    })
    if (error) return setError(error.message)
    await load()
  }

  function closeRename() {
    const targetId = renamingId
    setRenamingId(null)
    requestAnimationFrame(() => {
      if (targetId) renameButtonsRef.current.get(targetId)?.focus({ preventScroll: true })
    })
  }

  function openRename(row: MemberRow) {
    setRenamingId(row.member_id)
    setRenameInput(displayName(row.members))
  }

  async function confirmRename() {
    if (!renamingId) return
    const member = members.find((m) => m.member_id === renamingId)?.members
    const trimmed = renameInput.trim()
    closeRename()
    if (!member) return

    if (!trimmed || trimmed === member.display_name) {
      await supabase.from('member_aliases').delete().eq('member_id', member.id)
    } else {
      await supabase
        .from('member_aliases')
        .upsert(
          { member_id: member.id, alias: trimmed, owner_id: session?.user.id },
          { onConflict: 'owner_id,member_id' },
        )
    }
    await refreshAliases()
  }

  if (!group) {
    return (
      <div className="page page-wide together-page members-page">
        <AppHeader title={t('Members_Title')} backTo={`/groups/${groupId}`} />
        {error ? (
          <p className="error-text" role="alert">
            {error}
          </p>
        ) : (
          <div className="spinner" role="status">
            {t('Common_Loading')}
          </div>
        )}
      </div>
    )
  }

  const phantoms = members.filter((m) => !m.members.account_id)

  return (
    <div className="page page-wide together-page members-page">
      <AppHeader title={t('Members_Title')} backTo={`/groups/${groupId}`} />
      <p className="page-context">{group.name}</p>
      {error && (
        <p className="error-text" role="alert">
          {error}
        </p>
      )}
      {notice && (
        <p className="notice-text" role="status">
          {notice}
        </p>
      )}

      <div className="members-layout">
        <section className="together-panel members-roster">
          <h2 className="section-title">{t('Members_Title')}</h2>
          <ul className="roster-list">
            {members.map((row) => {
              const isYou = row.members.account_id === session?.user.id
              const isPhantom = !row.members.account_id
              return (
                <li key={row.member_id} className="roster-row">
                  <Avatar url={avatarUrl(row.members)} initials={initials(row.members)} seed={row.member_id} size={40} />
                  <div className="roster-info">
                    {renamingId === row.member_id ? (
                      <div className="rename-row">
                        <input
                          autoFocus
                          aria-label={t('Members_RenamePrompt')}
                          value={renameInput}
                          onChange={(e) => setRenameInput(e.target.value)}
                          onKeyDown={(e) => {
                            if (e.key === 'Enter') confirmRename()
                            if (e.key === 'Escape') closeRename()
                          }}
                        />
                        <button type="button" className="link-btn" onClick={confirmRename}>
                          {t('Common_Save')}
                        </button>
                        <button type="button" className="link-btn" onClick={() => closeRename()}>
                          {t('Common_Cancel')}
                        </button>
                      </div>
                    ) : (
                      <>
                        <button
                          type="button"
                          className="roster-name"
                          ref={(element) => {
                            if (element) renameButtonsRef.current.set(row.member_id, element)
                            else renameButtonsRef.current.delete(row.member_id)
                          }}
                          onClick={() => openRename(row)}
                          aria-label={`${t('Members_RenamePrompt')}: ${displayName(row.members)}`}
                        >
                          <span>{displayName(row.members)}</span>
                          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true">
                            <path d="m16 3 5 5L9 20l-6 1 1-6L16 3Z M13 6l5 5" />
                          </svg>
                        </button>
                        <div className="roster-caption">
                          {isYou ? t('Members_You') : isPhantom ? t('Members_Phantom') : ''}
                        </div>
                      </>
                    )}
                  </div>
                  {!isYou && (
                    <Link
                      to={`/groups/${groupId}/members/${row.member_id}`}
                      className="btn btn-outline profile-link"
                      aria-label={t('Members_ViewProfile', displayName(row.members))}
                      title={t('Members_ViewProfile', displayName(row.members))}
                    >
                      <Icon name="arrow_right" size={18} />
                    </Link>
                  )}
                  {isPhantom && (
                    <button
                      type="button"
                      className="btn btn-outline remove-btn"
                      onClick={() => handleRemove(row)}
                    >
                      {t('Members_Remove')}
                    </button>
                  )}
                </li>
              )
            })}
          </ul>
        </section>

        <section className="together-panel members-section">
          <h2 className="section-title">{t('Members_InvitePeople')}</h2>
          {generalInvite && (
            <div className="invite-card">
              <div className="invite-qr">
                <QrCode value={buildInviteUrl(generalInvite.token)} label={t('Members_QrLabel')} />
              </div>
              <div className="invite-editor">
                <span className="invite-label" id="inviteUsesLabel">
                  {t('Members_UsesLabel')}
                </span>
                <div className="invite-stepper" role="group" aria-labelledby="inviteUsesLabel">
                  <button
                    type="button"
                    className="btn btn-outline stepper-btn"
                    aria-label={t('Members_FewerUses')}
                    disabled={inviteSaving || generalInvite.max_uses <= Math.max(1, generalInvite.use_count)}
                    onClick={() => updateGeneralInvite({ max_uses: generalInvite.max_uses - 1 })}
                  >
                    −
                  </button>
                  <strong aria-live="polite">{generalInvite.max_uses}</strong>
                  <button
                    type="button"
                    className="btn btn-outline stepper-btn"
                    aria-label={t('Members_MoreUses')}
                    disabled={inviteSaving || generalInvite.max_uses >= MAX_USES_CEILING}
                    onClick={() => updateGeneralInvite({ max_uses: generalInvite.max_uses + 1 })}
                  >
                    +
                  </button>
                </div>
                <span className="invite-hint">
                  {t('Members_UsedCount', generalInvite.use_count, generalInvite.max_uses)}
                </span>

                <label className="invite-label" htmlFor="inviteExpiry">
                  {t('Members_ExpiresLabel')}
                </label>
                <div className="field">
                  <select
                    id="inviteExpiry"
                    disabled={inviteSaving}
                    value={closestExpiryIndex(Date.parse(generalInvite.expires_at) - Date.now())}
                    onChange={(e) =>
                      updateGeneralInvite({
                        expires_at: new Date(Date.now() + EXPIRY_HOURS[Number(e.target.value)] * 3_600_000).toISOString(),
                      })
                    }
                  >
                    {EXPIRY_KEYS.map((key, i) => (
                      <option key={key} value={i}>
                        {t(key)}
                      </option>
                    ))}
                  </select>
                </div>
              </div>
            </div>
          )}
          {generalInvite && <div className="invite-code">{generalInvite.token}</div>}
          <button type="button" className="btn btn-outline" onClick={() => copyInvite()}>
            {t('Members_CopyLink')}
          </button>

          {manualLink && (
            <div className="field">
              <label htmlFor="manualInviteLink">{t('Members_CopyManually')}</label>
              <input
                id="manualInviteLink"
                readOnly
                value={manualLink}
                onFocus={(e) => e.currentTarget.select()}
              />
            </div>
          )}

          <form
            className="add-by-name"
            onSubmit={(e) => {
              e.preventDefault()
              if (canAddByName(nameInput, matches)) handleAddPhantom()
            }}
          >
            <div className="field name-search">
              <label htmlFor="memberName">{t('Members_AddByName')}</label>
              <input
                id="memberName"
                value={nameInput}
                onChange={(e) => setNameInput(e.target.value)}
                onKeyDown={(e) => {
                  // Enter that confirms an IME composition must not add the member.
                  if (e.key === 'Enter' && (e.nativeEvent.isComposing || e.keyCode === 229)) e.preventDefault()
                }}
                placeholder={t('Members_NamePlaceholder')}
                disabled={busy}
              />
            </div>

          {matches.length > 0 && (
            <ul className="match-list">
              {matches.map((m) => (
                <li key={m.id} className="match-row">
                  <Avatar url={avatarUrl(m)} initials={initials(m)} seed={m.id} size={32} />
                  <span className="match-name">{displayName(m)}</span>
                  {m.account_id ? (
                    <span className="match-hint">{t('Members_AlreadyOnAxis')}</span>
                  ) : (
                    <button
                      type="button"
                      className="btn btn-outline match-btn"
                      disabled={busy}
                      onClick={() => handleLinkExisting(m)}
                    >
                      {t('Members_Link')}
                    </button>
                  )}
                </li>
              ))}
            </ul>
          )}

          {canAddByName(nameInput, matches) && (
            <button type="submit" className="btn btn-primary" disabled={busy}>
              {busy ? t('Common_Saving') : t('Common_Add')}
            </button>
          )}
          </form>

          {phantoms.length > 0 && (
            <>
              <h3 className="subsection-title">{t('Members_PendingInvites')}</h3>
              <ul className="pending-list">
                {phantoms.map((row) => (
                  <li key={row.member_id} className="pending-row">
                    <Avatar url={null} initials={initials(row.members)} seed={row.member_id} size={32} />
                    <span className="match-name">{displayName(row.members)}</span>
                    <button
                      type="button"
                      className="btn btn-outline match-btn"
                      onClick={() => copyInvite(row.members)}
                    >
                      {t('Members_Resend')}
                    </button>
                  </li>
                ))}
              </ul>
            </>
          )}
        </section>

      </div>
    </div>
  )
}
