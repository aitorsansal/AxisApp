import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// Self-hosted variable font, bundled by Vite so typography does not depend on a remote font host.
import '@fontsource-variable/manrope'
import './index.css'
import { applyAccent, getAccent } from './lib/accent'
import { assertSupabaseConfig } from './lib/runtimeConfig'
import { showStartupFailure } from './lib/startupFailure'

// Before the first render, so a non-default accent never flashes blue first.
async function start() {
  try {
    assertSupabaseConfig(import.meta.env)
    try { applyAccent(getAccent()) } catch { /* A blocked preference store must not prevent startup. */ }
    const { default: App } = await import('./App.tsx')
    createRoot(document.getElementById('root')!, {
      onUncaughtError: () => showStartupFailure(),
    }).render(<StrictMode><App /></StrictMode>)
  } catch {
    showStartupFailure()
  }
}

void start()

// Required for PWA installability (Chrome/Edge/Android need a registered SW at root scope) and,
// later, for Firebase Messaging's background push handling — see public/sw.js.
if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js').catch(() => {
      // Best-effort — a failed registration just means no install prompt / no push, not a broken app.
    })
  })
}
