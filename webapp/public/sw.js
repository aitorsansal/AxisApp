// Service worker: satisfies PWA installability (Chrome/Edge/Android require a registered SW at
// root scope — see CLAUDE.md's "Phase A" web-push/PWA remarks) AND handles background push via
// Firebase Messaging. One SW instead of two, so there's no scope conflict between them.
//
// This file is served statically from public/ — Vite does NOT process it, so
// import.meta.env substitution doesn't happen here. The config below must be hardcoded, same as
// Firebase's own recommended pattern for firebase-messaging-sw.js. These values are not secrets
// (same reasoning as AxisApp/Platforms/Android/google-services.json's api_key, or
// VITE_SUPABASE_PUBLISHABLE_KEY) — every real permission check happens through Supabase RLS /
// Firebase project restrictions, not by keeping this config private.
//
// Values below are the "Axis Web" app registered under the same "axisapp-ee018" project the
// Android app uses (Firebase Console → Project settings → General → "Your apps" → Web app). If
// that app is ever deleted/re-created, refill from there (</> icon → no separate Firebase project
// needed) and update the matching VITE_FIREBASE_* vars in .env.local too.
importScripts('https://www.gstatic.com/firebasejs/12.19.0/firebase-app-compat.js')
importScripts('https://www.gstatic.com/firebasejs/12.19.0/firebase-messaging-compat.js')

firebase.initializeApp({
  apiKey: 'AIzaSyDKf8upjpqtj7RnPzP7RHMZkX2k-fAIAmk',
  authDomain: 'axisapp-ee018.firebaseapp.com',
  projectId: 'axisapp-ee018',
  storageBucket: 'axisapp-ee018.firebasestorage.app',
  messagingSenderId: '1080597749437',
  appId: '1:1080597749437:web:f2a41bb3a1d6a42586e1d4',
})

const messaging = firebase.messaging()

self.addEventListener('install', () => {
  self.skipWaiting()
})

self.addEventListener('activate', (event) => {
  event.waitUntil(self.clients.claim())
})

// send-push/index.ts sends data-only messages (deliberately, same reasoning as
// AxisFirebaseMessagingService on Android — a "notification" block would let the browser
// auto-display it with no control over the click action), so this is the only place a background
// push ever gets turned into something visible.
messaging.onBackgroundMessage((payload) => {
  const data = payload.data ?? {}
  self.registration.showNotification(data.title || 'Axis', {
    body: data.body || '',
    icon: '/icon-192.png',
    badge: '/icon-192.png',
    data,
  })
})

// Tapping the notification: focus an already-open tab and navigate it, or open a new one — same
// "deep link into the right group" behavior as MainActivity's PendingIntent on Android.
self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  const groupId = event.notification.data?.group_id
  const url = groupId ? `/groups/${groupId}` : '/'

  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clientList) => {
      for (const client of clientList) {
        if ('focus' in client) {
          if ('navigate' in client) client.navigate(url)
          return client.focus()
        }
      }
      if (self.clients.openWindow) return self.clients.openWindow(url)
    }),
  )
})
