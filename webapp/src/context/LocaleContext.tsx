import { createContext, useContext, useState, type ReactNode } from 'react'
import { strings, SUPPORTED_LANGUAGES, type Language } from '../lib/i18n/strings'

export type { Language }

// Web equivalent of LocalizationResourceManager.cs — "" means "follow the
// browser's language", same as MAUI's null/empty override meaning "follow
// the device". Persisted via localStorage instead of Preferences.
const STORAGE_KEY = 'axis_language_override'

function isSupported(value: string): value is Language {
  return (SUPPORTED_LANGUAGES as readonly string[]).includes(value)
}

function deviceLanguage(): Language {
  const candidate = navigator.language?.slice(0, 2)
  return candidate && isSupported(candidate) ? candidate : 'en'
}

function readStoredOverride(): Language | '' {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    return stored && isSupported(stored) ? stored : ''
  } catch {
    return ''
  }
}

interface LocaleContextValue {
  language: Language
  override: Language | ''
  setOverride: (lang: Language | '') => void
  t: (key: string, ...args: Array<string | number>) => string
}

const LocaleContext = createContext<LocaleContextValue | null>(null)

export function LocaleProvider({ children }: { children: ReactNode }) {
  const [override, setOverrideState] = useState<Language | ''>(readStoredOverride)

  const language = override || deviceLanguage()

  function setOverride(lang: Language | '') {
    setOverrideState(lang)
    try {
      if (lang) localStorage.setItem(STORAGE_KEY, lang)
      else localStorage.removeItem(STORAGE_KEY)
    } catch {
      // localStorage unavailable (private browsing, blocked site data) — the
      // override still applies for the rest of this session via state.
    }
  }

  function t(key: string, ...args: Array<string | number>): string {
    const template = strings[language][key] ?? strings.en[key] ?? key
    if (args.length === 0) return template
    return template.replace(/\{(\d+)\}/g, (match, index: string) => {
      const value = args[Number(index)]
      return value === undefined ? match : String(value)
    })
  }

  return (
    <LocaleContext.Provider value={{ language, override, setOverride, t }}>{children}</LocaleContext.Provider>
  )
}

export function useLocale() {
  const ctx = useContext(LocaleContext)
  if (!ctx) throw new Error('useLocale must be used within a LocaleProvider')
  return ctx
}
