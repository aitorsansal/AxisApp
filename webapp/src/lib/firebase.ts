import { initializeApp, type FirebaseOptions } from 'firebase/app'
import { getMessaging, isSupported, type Messaging } from 'firebase/messaging'

// Same Firebase project as the Android app (see AxisApp/Platforms/Android/google-services.json's
// project_id "axisapp-ee018") — a separate Web App registered under that project in the Firebase
// Console, so web push tokens land in the same FCM project send-push/index.ts already talks to.
// This config is not a secret — Firebase's web API key only identifies the project, every real
// permission check happens through Supabase RLS, same as VITE_SUPABASE_PUBLISHABLE_KEY.
const firebaseConfig: FirebaseOptions = {
  apiKey: import.meta.env.VITE_FIREBASE_API_KEY,
  authDomain: import.meta.env.VITE_FIREBASE_AUTH_DOMAIN,
  projectId: import.meta.env.VITE_FIREBASE_PROJECT_ID,
  storageBucket: import.meta.env.VITE_FIREBASE_STORAGE_BUCKET,
  messagingSenderId: import.meta.env.VITE_FIREBASE_MESSAGING_SENDER_ID,
  appId: import.meta.env.VITE_FIREBASE_APP_ID,
}

export const firebaseApp = initializeApp(firebaseConfig)

// Messaging isn't available in every browser (no Push API support, private-browsing restrictions,
// etc.) — isSupported() is the documented way to check before calling getMessaging(), which
// otherwise throws synchronously in an unsupported context.
let messagingPromise: Promise<Messaging | null> | null = null
export function getMessagingIfSupported(): Promise<Messaging | null> {
  if (!messagingPromise) {
    messagingPromise = isSupported().then((supported) => (supported ? getMessaging(firebaseApp) : null))
  }
  return messagingPromise
}
