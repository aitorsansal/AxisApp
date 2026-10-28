import { createContext, useContext, type Dispatch, type SetStateAction } from 'react'
import type { Group, MemberInfo, PairwiseBalance } from '../lib/types'

export interface DirectoryGroup extends Group {
  balance: number
  members: MemberInfo[]
}

interface GroupsContextValue {
  groups: DirectoryGroup[] | null
  setGroups: Dispatch<SetStateAction<DirectoryGroup[] | null>>
  // my_pairwise_balances across all groups; null until the first load.
  pairwise: PairwiseBalance[] | null
  currentMember: MemberInfo | null
  error: string | null
  refresh: () => Promise<void>
}

export const GroupsContext = createContext<GroupsContextValue | null>(null)

export function useGroups() {
  const context = useContext(GroupsContext)
  if (!context) throw new Error('useGroups must be used within a GroupsProvider')
  return context
}
