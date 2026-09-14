// Mirrors AxisApp/Services/AccentPalettes.cs's 8 named presets (color) and
// AppConstants.GroupIcons' fixed key list (icon) — groups.color/icon's DB check
// constraints (supabase/group_appearance.sql) are the source of truth for both lists;
// keep these in sync with it. Icons are rendered as emoji here rather than pulling in an
// icon library/Lucide font just for this — same stable, language-independent keys as the
// MAUI app, just a different glyph source.

export const GROUP_COLORS = [
  { key: 'Blue', hex: '#5B72D6', textOnAccent: '#08101F' },
  { key: 'Green', hex: '#4C8F63', textOnAccent: '#08101F' },
  { key: 'Red', hex: '#C25450', textOnAccent: '#08101F' },
  { key: 'Purple', hex: '#7C5FBF', textOnAccent: '#F2F5FA' },
  { key: 'Pink', hex: '#C15A87', textOnAccent: '#08101F' },
  { key: 'Amber', hex: '#A97A22', textOnAccent: '#08101F' },
  { key: 'Orange', hex: '#C36B3E', textOnAccent: '#08101F' },
  { key: 'Navy', hex: '#35507D', textOnAccent: '#F2F5FA' },
] as const

export type GroupColorKey = (typeof GROUP_COLORS)[number]['key']

export function colorHex(key: string | null | undefined): string {
  return GROUP_COLORS.find((c) => c.key === key)?.hex ?? GROUP_COLORS[0].hex
}

export function textOnAccent(key: string | null | undefined): string {
  return GROUP_COLORS.find((c) => c.key === key)?.textOnAccent ?? GROUP_COLORS[0].textOnAccent
}

export const GROUP_ICONS = [
  { key: 'home', glyph: '🏠' },
  { key: 'users', glyph: '👥' },
  { key: 'heart', glyph: '❤️' },
  { key: 'baby', glyph: '👶' },
  { key: 'dog', glyph: '🐶' },
  { key: 'plane', glyph: '✈️' },
  { key: 'car', glyph: '🚗' },
  { key: 'ship', glyph: '🚢' },
  { key: 'bike', glyph: '🚲' },
  { key: 'tent', glyph: '⛺' },
  { key: 'map_pin', glyph: '📍' },
  { key: 'mountain', glyph: '⛰️' },
  { key: 'utensils_crossed', glyph: '🍴' },
  { key: 'coffee', glyph: '☕' },
  { key: 'beer', glyph: '🍺' },
  { key: 'party_popper', glyph: '🎉' },
  { key: 'gift', glyph: '🎁' },
  { key: 'wallet', glyph: '👛' },
  { key: 'piggy_bank', glyph: '🐷' },
  { key: 'briefcase', glyph: '💼' },
  { key: 'dumbbell', glyph: '🏋️' },
  { key: 'graduation_cap', glyph: '🎓' },
  { key: 'gamepad', glyph: '🎮' },
  { key: 'film', glyph: '🎬' },
  { key: 'music', glyph: '🎵' },
  { key: 'book', glyph: '📖' },
  { key: 'shopping_cart', glyph: '🛒' },
  { key: 'star', glyph: '⭐' },
] as const

export function iconGlyph(key: string | null | undefined): string | null {
  return GROUP_ICONS.find((i) => i.key === key)?.glyph ?? null
}
