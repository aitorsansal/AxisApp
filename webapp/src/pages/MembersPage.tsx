import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { useAuth } from '../context/AuthContext'
import { useAliases } from '../context/AliasesContext'
import { useLocale } from '../context/LocaleContext'
import type { Group, MemberInfo, MemberRow } from '../lib/types'
import { canAddByName, pickReusableInvite } from '../lib/inviteReuse'
import { AppHeader } from '../components/AppHeader'
import { Avatar } from '../components/Avatar'
import { Icon } from '../components/Icon'
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

  // Only runs from an explicit copy click; adding a guest never mints an invite or writes the
  // clipboard. Mirrors SupabaseInvitesRepository.GetOrCreateAsync: reuse a still-usable invite.
  async function copyInvite(target?: MemberRow['members']) {
    setError(null)
    setNotice(null)
    setManualLink(null)
    const targetMemberId = target?.id
    let query = supabase
      .from('invites')
      .select('token, expires_at, max_uses, use_count')
      .eq('group_id', groupId)
    query = targetMemberId ? query.eq('target_member_id', targetMemberId) : query.is('target_member_id', null)
    const { data: existing, error: lookupError } = await query
      .order('expires_at', { ascending: false })
      .limit(20)
    if (lookupError) return setError(lookupError.message)

    let token: string
    const reusable = pickReusableInvite(existing, Date.now())
    if (reusable) {
      token = reusable.token
    } else {
      token = generateToken()
      const { error } = await supabase.from('invites').insert({
        group_id: groupId,
        target_member_id: targetMemberId ?? null,
        token,
        created_by: session?.user.id,
        expires_at: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(),
      })
      if (error) return setError(error.message)
    }

    const url = buildInviteUrl(token)
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
      <div className="page">
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
    <div className="page">
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

      <section className="card members-section">
        <h2 className="section-title">{t('Members_InvitePeople')}</h2>
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

      <section>
        <h2 className="section-title">{t('Members_Title')}</h2>
        <ul className="roster-list">
          {members.map((row) => {
            const isYou = row.members.account_id === session?.user.id
            const isPhantom = !row.members.account_id
            return (
              <li key={row.member_id} className="card roster-row">
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
                        {displayName(row.members)}
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
    </div>
  )
}
