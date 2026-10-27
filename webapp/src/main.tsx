import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { applyAccent, getAccent } from './lib/accent'

// Before the first render, so a non-default accent never flashes blue first.
applyAccent(getAccent())

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)

// Required for PWA installability (Chrome/Edge/Android need a registered SW at root scope) and,
// later, for Firebase Messaging's background push handling — see public/sw.js.
if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js').catch(() => {
      // Best-effort — a failed registration just means no install prompt / no push, not a broken app.
    })
  })
}
