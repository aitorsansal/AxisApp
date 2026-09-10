import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import { supabase } from '../lib/supabaseClient'
import { useAuth } from './AuthContext'
import type { MemberInfo } from '../lib/types'

// Web equivalent of AxisApp/Services/MemberDisplay.cs — private, per-account
// nickname overrides for how a member is displayed (member_aliases, RLS-
// scoped to owner_id = auth.uid()). Loaded once per session, no group_id
// filter needed, same reasoning MemberDisplay.cs's own remarks give.
interface AliasesContextValue {
  aliases: Record<string, string>
  displayName: (member: Pick<MemberInfo, 'id' | 'display_name'>) => string
  initials: (member: Pick<MemberInfo, 'id' | 'display_name'>) => string
  avatarUrl: (member: Pick<MemberInfo, 'account_id' | 'avatar_path'>) => string | null
  refresh: () => Promise<void>
}

const AliasesContext = createContext<AliasesContextValue | null>(null)

function computeInitials(name: string): string {
  const parts = name.split(' ').filter(Boolean)
  return parts.length === 0 ? '?' : parts.slice(0, 2).map((w) => w[0]!.toUpperCase()).join('')
}

export function AliasesProvider({ children }: { children: ReactNode }) {
  const { session } = useAuth()
  const [aliases, setAliases] = useState<Record<string, string>>({})

  const refresh = useCallback(async () => {
    if (!session) {
      setAliases({})
      return
    }
    const { data, error } = await supabase.from('member_aliases').select('member_id, alias')
    if (error) return
    const map: Record<string, string> = {}
    for (const row of data ?? []) map[row.member_id as string] = row.alias as string
    setAliases(map)
  }, [session])

  useEffect(() => {
    refresh()
  }, [refresh])

  function displayName(member: Pick<MemberInfo, 'id' | 'display_name'>): string {
    const alias = aliases[member.id]
    return alias && alias.trim() !== '' ? alias : member.display_name
  }

  function initials(member: Pick<MemberInfo, 'id' | 'display_name'>): string {
    return computeInitials(displayName(member))
  }

  // Phantoms never have an avatar (enforced at the DB level too — see
  // schema.sql's "Avatar photos" remarks). The `avatars` bucket is public,
  // so this is a plain deterministic URL, same as MemberDisplay.AvatarUrl.
  function avatarUrl(member: Pick<MemberInfo, 'account_id' | 'avatar_path'>): string | null {
    if (!member.account_id || !member.avatar_path) return null
    return supabase.storage.from('avatars').getPublicUrl(member.avatar_path).data.publicUrl
  }

  return (
    <AliasesContext.Provider value={{ aliases, displayName, initials, avatarUrl, refresh }}>
      {children}
    </AliasesContext.Provider>
  )
}

export function useAliases() {
  const ctx = useContext(AliasesContext)
  if (!ctx) throw new Error('useAliases must be used within an AliasesProvider')
  return ctx
}
