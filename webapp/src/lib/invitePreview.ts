// Mirrors supabase/invite_preview.sql (preview_invite) and the raise-exception texts of
// redeem_invite(). AxisApp/Models/InvitePreview.cs is the Android twin; keep the two in step.
export type InviteStatus =
  | 'ok'
  | 'already_member'
  | 'invalid'
  | 'expired'
  | 'used'
  | 'inviter_gone'
  | 'claimed'
  | 'shared_group'

export interface InvitePreview {
  status: InviteStatus
  group_name?: string
  group_currency?: string
  member_count?: number
  target_name?: string | null
}

const STATUSES: InviteStatus[] = [
  'ok', 'already_member', 'invalid', 'expired', 'used', 'inviter_gone', 'claimed', 'shared_group',
]

export function parseInvitePreview(value: unknown): InvitePreview | null {
  if (typeof value !== 'object' || value === null) return null
  const status = (value as { status?: unknown }).status
  if (typeof status !== 'string' || !STATUSES.includes(status as InviteStatus)) return null
  return value as InvitePreview
}

export function isJoinable(status: InviteStatus): boolean {
  return status === 'ok' || status === 'already_member'
}

export function inviteErrorKey(status: InviteStatus): string {
  return `JoinGroup_Err_${status}`
}

// redeem_invite() reports the same conditions as free text; used when the preview was skipped or
// raced (e.g. the invite was redeemed by someone else between preview and Join).
export function redeemErrorStatus(message: string): InviteStatus | null {
  if (message.includes('Invalid invite code')) return 'invalid'
  if (message.includes('Invite expired')) return 'expired'
  if (message.includes('Invite already used')) return 'used'
  if (message.includes('no longer valid')) return 'inviter_gone'
  if (message.includes('already been claimed')) return 'claimed'
  if (message.includes('already in a group with this person')) return 'shared_group'
  return null
}

// A real invite token is 12 base64url characters; don't hit the server for obvious partials.
export function looksLikeInviteCode(code: string): boolean {
  return /^[A-Za-z0-9_-]{8,}$/.test(code)
}
