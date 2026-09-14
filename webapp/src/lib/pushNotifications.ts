import { deleteToken, getToken } from 'firebase/messaging'
import { getMessagingIfSupported } from './firebase'
import { supabase } from './supabaseClient'

export type PushState = 'unsupported' | 'default' | 'denied' | 'not_subscribed' | 'subscribed'

// 'subscribed' means more than "permission granted" — permission can't be revoked
// programmatically once granted, so after unregisterFromPush() the browser would still report
// 'granted' even though there's no device_tokens row left. Cross-checking against the row itself
// (rather than trusting a local flag) also means this reflects reality across devices/sessions —
// e.g. after signing into the same browser as a different account.
export async function getPushState(accountId: string): Promise<PushState> {
  if (!('Notification' in window)) return 'unsupported'
  const messaging = await getMessagingIfSupported()
  if (!messaging) return 'unsupported'
  if (Notification.permission === 'denied') return 'denied'
  if (Notification.permission === 'default') return 'default'

  const registration = await navigator.serviceWorker.ready
  const token = await getToken(messaging, {
    vapidKey: import.meta.env.VITE_FIREBASE_VAPID_KEY,
    serviceWorkerRegistration: registration,
  }).catch(() => null)
  if (!token) return 'not_subscribed'

  const { data } = await supabase
    .from('device_tokens')
    .select('id')
    .eq('push_token', token)
    .eq('account_id', accountId)
    .maybeSingle()
  return data ? 'subscribed' : 'not_subscribed'
}

// Mirrors IPushRegistrationService's Android flow (request permission, get an FCM token, upsert
// into device_tokens) — see CLAUDE.md's push-notifications remarks. `push_token` is unique across
// the whole table (schema.sql), so upserting on that column reassigns a reused token to the
// current account instead of erroring — the same browser signing into a different Axis account
// on the same device is the one realistic case this covers.
export async function registerForPush(accountId: string): Promise<PushState> {
  const messaging = await getMessagingIfSupported()
  if (!messaging) return 'unsupported'

  const permission = await Notification.requestPermission()
  if (permission !== 'granted') return permission

  const registration = await navigator.serviceWorker.ready
  const token = await getToken(messaging, {
    vapidKey: import.meta.env.VITE_FIREBASE_VAPID_KEY,
    serviceWorkerRegistration: registration,
  })

  const { error } = await supabase
    .from('device_tokens')
    .upsert({ account_id: accountId, push_token: token, platform: 'web' }, { onConflict: 'push_token' })
  if (error) throw error

  return 'subscribed'
}

export async function unregisterFromPush(): Promise<void> {
  const messaging = await getMessagingIfSupported()
  if (!messaging) return

  const registration = await navigator.serviceWorker.ready
  const token = await getToken(messaging, {
    vapidKey: import.meta.env.VITE_FIREBASE_VAPID_KEY,
    serviceWorkerRegistration: registration,
  }).catch(() => null)

  if (token) {
    await supabase.from('device_tokens').delete().eq('push_token', token)
    await deleteToken(messaging).catch(() => {})
  }
}
