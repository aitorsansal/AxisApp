import { iconGlyphs, type IconName } from './icons'

// Group appearance keys and glyphs match the Android client's curated Lucide set.

export const GROUP_COLORS = [
  { key: 'Blue', hex: '#6177D8', textOnAccent: '#08101F' },
  { key: 'Green', hex: '#4C8F63', textOnAccent: '#08101F' },
  { key: 'Red', hex: '#C55D59', textOnAccent: '#08101F' },
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
  { key: 'home', glyph: iconGlyphs['home' as IconName] },
  { key: 'users', glyph: iconGlyphs['users' as IconName] },
  { key: 'heart', glyph: iconGlyphs['heart' as IconName] },
  { key: 'baby', glyph: iconGlyphs['baby' as IconName] },
  { key: 'dog', glyph: iconGlyphs['dog' as IconName] },
  { key: 'plane', glyph: iconGlyphs['plane' as IconName] },
  { key: 'car', glyph: iconGlyphs['car' as IconName] },
  { key: 'ship', glyph: iconGlyphs['ship' as IconName] },
  { key: 'bike', glyph: iconGlyphs['bike' as IconName] },
  { key: 'tent', glyph: iconGlyphs['tent' as IconName] },
  { key: 'map_pin', glyph: iconGlyphs['map_pin' as IconName] },
  { key: 'mountain', glyph: iconGlyphs['mountain' as IconName] },
  { key: 'utensils_crossed', glyph: iconGlyphs['utensils_crossed' as IconName] },
  { key: 'coffee', glyph: iconGlyphs['coffee' as IconName] },
  { key: 'beer', glyph: iconGlyphs['beer' as IconName] },
  { key: 'party_popper', glyph: iconGlyphs['party_popper' as IconName] },
  { key: 'gift', glyph: iconGlyphs['gift' as IconName] },
  { key: 'wallet', glyph: iconGlyphs['wallet' as IconName] },
  { key: 'piggy_bank', glyph: iconGlyphs['piggy_bank' as IconName] },
  { key: 'briefcase', glyph: iconGlyphs['briefcase' as IconName] },
  { key: 'dumbbell', glyph: iconGlyphs['dumbbell' as IconName] },
  { key: 'graduation_cap', glyph: iconGlyphs['graduation_cap' as IconName] },
  { key: 'gamepad', glyph: iconGlyphs['gamepad' as IconName] },
  { key: 'film', glyph: iconGlyphs['film' as IconName] },
  { key: 'music', glyph: iconGlyphs['music' as IconName] },
  { key: 'book', glyph: iconGlyphs['book' as IconName] },
  { key: 'shopping_cart', glyph: iconGlyphs['shopping_cart' as IconName] },
  { key: 'star', glyph: iconGlyphs['star' as IconName] },
] as const

export function iconGlyph(key: string | null | undefined): string | null {
  return GROUP_ICONS.find((i) => i.key === key)?.glyph ?? null
}
