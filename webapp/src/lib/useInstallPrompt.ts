import { useEffect, useState } from 'react'

// Chrome/Edge/Android fire this instead of showing their own install UI automatically — capturing
// it is the only way to trigger installation from an in-app button on those platforms. iOS Safari
// never fires it (no programmatic install API exists there at all — that's a manual "Add to Home
// Screen" instructions screen instead, driven by isIos/isStandalone below).
interface BeforeInstallPromptEvent extends Event {
  prompt(): Promise<void>
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>
}

function isStandalone(): boolean {
  return (
    window.matchMedia('(display-mode: standalone)').matches ||
    // iOS Safari's own non-standard flag — matchMedia alone doesn't cover it there.
    (window.navigator as Navigator & { standalone?: boolean }).standalone === true
  )
}

export function useInstallPrompt() {
  const [deferredPrompt, setDeferredPrompt] = useState<BeforeInstallPromptEvent | null>(null)
  const [installed, setInstalled] = useState(isStandalone)

  useEffect(() => {
    function onBeforeInstallPrompt(e: Event) {
      e.preventDefault()
      setDeferredPrompt(e as BeforeInstallPromptEvent)
    }
    function onInstalled() {
      setInstalled(true)
      setDeferredPrompt(null)
    }
    window.addEventListener('beforeinstallprompt', onBeforeInstallPrompt)
    window.addEventListener('appinstalled', onInstalled)
    return () => {
      window.removeEventListener('beforeinstallprompt', onBeforeInstallPrompt)
      window.removeEventListener('appinstalled', onInstalled)
    }
  }, [])

  const isIos = /iphone|ipad|ipod/i.test(window.navigator.userAgent)

  async function promptInstall() {
    if (!deferredPrompt) return
    await deferredPrompt.prompt()
    await deferredPrompt.userChoice
    setDeferredPrompt(null)
  }

  return {
    installed,
    canPrompt: deferredPrompt !== null,
    promptInstall,
    // iOS never fires beforeinstallprompt — show manual instructions there instead of a button.
    showIosInstructions: isIos && !installed,
  }
}
