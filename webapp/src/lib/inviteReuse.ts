export interface ReusableInvite {
  token: string
  expires_at: string
  max_uses: number
  use_count: number
}

// Same rule as SupabaseInvitesRepository.GetOrCreateAsync: among the (group, target) invites,
// ordered newest-created first and then latest-expiring (callers must sort that way), reuse the
// first one that still has uses left and has not expired, so repeated copies do not leave a
// trail of live tokens behind. created_at alone is not enough: Android-written rows before the
// Invite.CreatedAt fix all hold 0001-01-01, so expires_at breaks those ties.
export function pickReusableInvite(invites: ReusableInvite[] | null | undefined, now: number): ReusableInvite | undefined {
  return invites?.find((i) => i.use_count < i.max_uses && Date.parse(i.expires_at) > now)
}

// Name search gating shared by the Add button and the Enter key: a phantom suggestion must be
// a deliberate "Link" click, never something Enter picks for the user.
export function canAddByName(name: string, matches: { account_id: string | null }[]): boolean {
  return name.trim().length >= 2 && matches.every((m) => m.account_id)
}

// Presets for the general invite's expiry picker; same four as Android's ExpiryOptions.
export const EXPIRY_HOURS = [1, 24, 24 * 7, 24 * 30]
// Same cap as InviteToGroupViewModel.MaxUsesCeiling.
export const MAX_USES_CEILING = 20

// Index of the preset closest to the time left, like InviteToGroupViewModel.ClosestExpiryIndex,
// so an invite created with the 7-day default shows "7 days" for most of its life.
export function closestExpiryIndex(remainingMs: number): number {
  let best = 0
  let bestDiff = Infinity
  EXPIRY_HOURS.forEach((hours, i) => {
    const diff = Math.abs(hours * 3_600_000 - remainingMs)
    if (diff < bestDiff) {
      best = i
      bestDiff = diff
    }
  })
  return best
}
