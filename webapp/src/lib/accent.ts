// Accent colour presets — the same 8 as Android's Services/AccentPalettes.cs (values copied from
// there, not recomputed). Every accent colour on the web app goes through these CSS variables in
// index.css; backgrounds, surfaces and status colours stay fixed for every preset, as on Android.
// Per-browser choice, never synced (same "viewing preference, not account state" reasoning as
// AppConstants.Preferences.AccentPreset).

export const ACCENT_PRESETS = ['Blue', 'Green', 'Red', 'Purple', 'Pink', 'Amber', 'Orange', 'Navy'] as const
export type AccentPreset = (typeof ACCENT_PRESETS)[number]

interface AccentValues {
  primary: string
  primaryHover: string
  primaryPressed: string
  primarySoft: string // Android's BtnPrimaryDisabledBg
  focus: string // also --action-text: the light tint used for accent text on the dark surface
  secondary: string
  textOnAccent: string
}

const VALUES: Record<AccentPreset, AccentValues> = {
  Blue: { primary: '#6177D8', primaryHover: '#7387DC', primaryPressed: '#4A5EB4', primarySoft: '#232A48', focus: '#B1BCEC', secondary: '#CBA257', textOnAccent: '#08101F' },
  Green: { primary: '#4C8F63', primaryHover: '#57A371', primaryPressed: '#42664E', primarySoft: '#234830', focus: '#C0DDCA', secondary: '#814B77', textOnAccent: '#08101F' },
  Red: { primary: '#C55D59', primaryHover: '#CA6A67', primaryPressed: '#954C4A', primarySoft: '#482523', focus: '#E6B9B7', secondary: '#4CB6A8', textOnAccent: '#08101F' },
  Purple: { primary: '#7C5FBF', primaryHover: '#8E75C8', primaryPressed: '#685299', primarySoft: '#2E2348', focus: '#C6BAE3', secondary: '#A8B45A', textOnAccent: '#F2F5FA' },
  Pink: { primary: '#C15A87', primaryHover: '#C97097', primaryPressed: '#994F6F', primarySoft: '#482333', focus: '#E5B9CC', secondary: '#56B67C', textOnAccent: '#08101F' },
  Amber: { primary: '#A97A22', primaryHover: '#C28C27', primaryPressed: '#6F5729', primarySoft: '#483B23', focus: '#EFD8AE', secondary: '#275F94', textOnAccent: '#08101F' },
  Orange: { primary: '#C36B3E', primaryHover: '#CA7D55', primaryPressed: '#8D5B41', primarySoft: '#483023', focus: '#E8C6B5', secondary: '#419DB0', textOnAccent: '#08101F' },
  Navy: { primary: '#35507D', primaryHover: '#3E5E92', primaryPressed: '#2E3B51', primarySoft: '#233148', focus: '#BBCAE2', secondary: '#6E4F35', textOnAccent: '#F2F5FA' },
}

const STORAGE_KEY = 'axis.accent'

export function accentSwatch(preset: AccentPreset): { color: string; onColor: string } {
  return { color: VALUES[preset].primary, onColor: VALUES[preset].textOnAccent }
}

export function getAccent(): AccentPreset {
  const stored = localStorage.getItem(STORAGE_KEY)
  return (ACCENT_PRESETS as readonly string[]).includes(stored ?? '') ? (stored as AccentPreset) : 'Blue'
}

export function applyAccent(preset: AccentPreset) {
  const v = VALUES[preset]
  const style = document.documentElement.style
  style.setProperty('--primary', v.primary)
  style.setProperty('--primary-hover', v.primaryHover)
  style.setProperty('--primary-pressed', v.primaryPressed)
  style.setProperty('--primary-soft', v.primarySoft)
  style.setProperty('--action-text', v.focus)
  style.setProperty('--focus', v.focus)
  style.setProperty('--secondary', v.secondary)
  style.setProperty('--text-on-accent', v.textOnAccent)
}

export function setAccent(preset: AccentPreset) {
  localStorage.setItem(STORAGE_KEY, preset)
  applyAccent(preset)
}
