import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { useLocation } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { applySavedOrder } from '../lib/groupOrder'
import type { Group, MemberInfo, MyGroupBalance, PairwiseBalance } from '../lib/types'
import { useAuth } from './AuthContext'
import { GroupsContext, type DirectoryGroup } from './useGroups'

export function GroupsProvider({ children }: { children: ReactNode }) {
  const { session } = useAuth()
  const { pathname } = useLocation()
  const [groups, setGroups] = useState<DirectoryGroup[] | null>(null)
  const [currentMember, setCurrentMember] = useState<MemberInfo | null>(null)
  const [pairwise, setPairwise] = useState<PairwiseBalance[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const requestVersion = useRef(0)
  const userId = session?.user.id

  const refresh = useCallback(async () => {
    if (!userId) return
    const version = ++requestVersion.current
    const [groupResult, balanceResult, pairwiseResult, memberResult, profileResult] = await Promise.all([
      supabase.from('groups').select('id, name, currency, created_by, color, icon'),
      supabase.from('my_group_balances').select('group_id, balance'),
      // Every group at once (RLS scopes it to this account): feeds the owed/owing summary.
      supabase.from('my_pairwise_balances').select('group_id, other_member_id, balance'),
      supabase
        .from('group_members')
        .select('group_id, members(id, display_name, account_id, avatar_path)'),
      supabase
        .from('members')
        .select('id, display_name, account_id, avatar_path')
        .eq('account_id', userId)
        .order('created_at')
        .limit(1)
        .maybeSingle(),
    ])
    if (version !== requestVersion.current) return
    const loadError = groupResult.error ?? balanceResult.error ?? pairwiseResult.error ?? memberResult.error
    if (loadError) {
      setError(loadError.message)
      return
    }
    const balances = new Map(
      (balanceResult.data as MyGroupBalance[]).map((row) => [row.group_id, row.balance]),
    )
    const memberships = (memberResult.data ?? []) as unknown as {
      group_id: string
      members: MemberInfo
    }[]
    setGroups(
      applySavedOrder(
        (groupResult.data as Group[]).map((group) => ({
          ...group,
          balance: balances.get(group.id) ?? 0,
          members: memberships
            .filter((row) => row.group_id === group.id)
            .map((row) => row.members)
            .filter(Boolean),
        })),
      ),
    )
    setPairwise(pairwiseResult.data as PairwiseBalance[])
    setCurrentMember(profileResult.data as MemberInfo | null)
    setError(null)
  }, [userId])

  useEffect(() => {
    void refresh()
  }, [refresh, pathname])

  return (
    <GroupsContext.Provider value={{ groups, setGroups, pairwise, currentMember, error, refresh }}>
      {children}
    </GroupsContext.Provider>
  )
}
