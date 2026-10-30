export interface ReusableInvite {
  token: string
  expires_at: string
  max_uses: number
  use_count: number
}

// Same rule as SupabaseInvitesRepository.GetOrCreateAsync: among the (group, target) invites,
// ordered latest-expiring first, reuse the first one that still has uses left and has not
// expired, so repeated copies do not leave a trail of live tokens behind. Never order by
// created_at: Android-written rows before the Invite.CreatedAt fix all hold 0001-01-01.
export function pickReusableInvite(invites: ReusableInvite[] | null | undefined, now: number): ReusableInvite | undefined {
  return invites?.find((i) => i.use_count < i.max_uses && Date.parse(i.expires_at) > now)
}

// Name search gating shared by the Add button and the Enter key: a phantom suggestion must be
// a deliberate "Link" click, never something Enter picks for the user.
export function canAddByName(name: string, matches: { account_id: string | null }[]): boolean {
  return name.trim().length >= 2 && matches.every((m) => m.account_id)
}
