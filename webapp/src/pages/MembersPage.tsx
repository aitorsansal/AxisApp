import { useCallback, useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { useAuth } from '../context/AuthContext'
import { useAliases } from '../context/AliasesContext'
import { useLocale } from '../context/LocaleContext'
import type { Group, MemberInfo, MemberRow } from '../lib/types'
import { AppHeader } from '../components/AppHeader'
import { Avatar } from '../components/Avatar'
import './MembersPage.css'

function buildInviteUrl(code: string): string {
  return `https://axisapp.aitorsansal.com/invite?code=${encodeURIComponent(code)}`
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
  const [renamingId, setRenamingId] = useState<string | null>(null)
  const [renameInput, setRenameInput] = useState('')

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

  async function copyInvite(targetMemberId?: string) {
    setError(null)
    setNotice(null)
    const token = generateToken()
    const { error } = await supabase.from('invites').insert({
      group_id: groupId,
      target_member_id: targetMemberId ?? null,
      token,
      created_by: session?.user.id,
      expires_at: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(),
    })
    if (error) return setError(error.message)
    await navigator.clipboard.writeText(buildInviteUrl(token))
    setNotice(targetMemberId ? t('Members_NewLinkCopied') : t('Members_LinkCopied'))
  }

  async function handleAddPhantom() {
    const trimmed = nameInput.trim()
    if (!trimmed || !groupId) return
    setBusy(true)
    setError(null)
    setNotice(null)
    const { data: phantom, error: memberError } = await supabase
      .from('members')
      .insert({ display_name: trimmed, created_by: session?.user.id })
      .select('id')
      .single()
    if (memberError) {
      setError(memberError.message)
      setBusy(false)
      return
    }
    const { error: joinError } = await supabase
      .from('group_members')
      .insert({ group_id: groupId, member_id: phantom.id })
    if (joinError) {
      setError(joinError.message)
      setBusy(false)
      return
    }
    setBusy(false)
    setNameInput('')
    setMatches([])
    await copyInvite(phantom.id)
    await load()
  }

  async function handleLinkExisting(member: MemberInfo) {
    if (!groupId) return
    setBusy(true)
    setError(null)
    setNotice(null)
    const { error } = await supabase.from('group_members').insert({ group_id: groupId, member_id: member.id })
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

  function openRename(row: MemberRow) {
    setRenamingId(row.member_id)
    setRenameInput(displayName(row.members))
  }

  async function confirmRename() {
    if (!renamingId) return
    const member = members.find((m) => m.member_id === renamingId)?.members
    const trimmed = renameInput.trim()
    setRenamingId(null)
    if (!member) return

    if (!trimmed || trimmed === member.display_name) {
      await supabase.from('member_aliases').delete().eq('member_id', member.id)
    } else {
      await supabase
        .from('member_aliases')
        .upsert({ member_id: member.id, alias: trimmed, owner_id: session?.user.id }, { onConflict: 'owner_id,member_id' })
    }
    await refreshAliases()
  }

  if (!group) {
    return (
      <div className="page">
        <AppHeader title={t('Members_Title')} back />
        {error ? <p className="error-text">{error}</p> : <div className="spinner">{t('Common_Loading')}</div>}
      </div>
    )
  }

  const phantoms = members.filter((m) => !m.members.account_id)

  return (
    <div className="page">
      <AppHeader title={group.name} back />
      {error && <p className="error-text">{error}</p>}
      {notice && <p className="notice-text">{notice}</p>}

      <section className="card members-section">
        <h2 className="section-title">{t('Members_InvitePeople')}</h2>
        <button type="button" className="btn btn-outline" onClick={() => copyInvite()}>
          {t('Members_CopyLink')}
        </button>

        <div className="field name-search">
          <label htmlFor="memberName">{t('Members_AddByName')}</label>
          <input
            id="memberName"
            value={nameInput}
            onChange={(e) => setNameInput(e.target.value)}
            placeholder={t('Members_NamePlaceholder')}
            disabled={busy}
          />
        </div>

        {matches.length > 0 && (
          <ul className="match-list">
            {matches.map((m) => (
              <li key={m.id} className="match-row">
                <Avatar url={avatarUrl(m)} initials={initials(m)} size={32} />
                <span className="match-name">{displayName(m)}</span>
                {m.account_id ? (
                  <span className="match-hint">{t('Members_AlreadyOnAxis')}</span>
                ) : (
                  <button type="button" className="btn btn-outline match-btn" disabled={busy} onClick={() => handleLinkExisting(m)}>
                    {t('Members_Link')}
                  </button>
                )}
              </li>
            ))}
          </ul>
        )}

        {nameInput.trim().length >= 2 && matches.every((m) => m.account_id) && (
          <button type="button" className="btn btn-primary" disabled={busy} onClick={handleAddPhantom}>
            {busy ? t('Common_Saving') : t('Common_Add')}
          </button>
        )}

        {phantoms.length > 0 && (
          <>
            <h3 className="subsection-title">{t('Members_PendingInvites')}</h3>
            <ul className="pending-list">
              {phantoms.map((row) => (
                <li key={row.member_id} className="pending-row">
                  <Avatar url={null} initials={initials(row.members)} size={32} />
                  <span className="match-name">{displayName(row.members)}</span>
                  <button type="button" className="btn btn-outline match-btn" onClick={() => copyInvite(row.member_id)}>
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
                <Avatar url={avatarUrl(row.members)} initials={initials(row.members)} size={40} />
                <div className="roster-info">
                  {renamingId === row.member_id ? (
                    <div className="rename-row">
                      <input
                        autoFocus
                        value={renameInput}
                        onChange={(e) => setRenameInput(e.target.value)}
                        onKeyDown={(e) => e.key === 'Enter' && confirmRename()}
                      />
                      <button type="button" className="link-btn" onClick={confirmRename}>{t('Common_Save')}</button>
                      <button type="button" className="link-btn" onClick={() => setRenamingId(null)}>{t('Common_Cancel')}</button>
                    </div>
                  ) : (
                    <>
                      <div className="roster-name" onClick={() => openRename(row)} title={t('Members_RenamePrompt')}>
                        {displayName(row.members)}
                      </div>
                      <div className="roster-caption">
                        {isYou ? t('Members_You') : isPhantom ? t('Members_Phantom') : ''}
                      </div>
                    </>
                  )}
                </div>
                {isPhantom && (
                  <button type="button" className="btn btn-outline remove-btn" onClick={() => handleRemove(row)}>
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
