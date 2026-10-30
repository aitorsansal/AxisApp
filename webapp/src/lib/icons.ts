// Codepoints match the Lucide font already used by the Android client.
export const iconGlyphs = {
  arrow_left: '\uE048',
  arrow_right: '\uE049',
  more: '\uE0B7',
  plus: '\uE13D',
  log_out: '\uE10E',
  user: '\uE19F',
  users: '\uE1A4',
  shopping_cart: '\uE15C',
  ticket: '\uE20F',
  car: '\uE1D5',
  plane: '\uE1DE',
  home: '\uE0F5',
  heart: '\uE0F2',
  baby: '\uE2CE',
  dog: '\uE38D',
  ship: '\uE3BA',
  bike: '\uE1D2',
  tent: '\uE227',
  map_pin: '\uE111',
  mountain: '\uE231',
  utensils_crossed: '\uE2F7',
  coffee: '\uE096',
  beer: '\uE2CF',
  party_popper: '\uE343',
  gift: '\uE0E1',
  wallet: '\uE204',
  piggy_bank: '\uE13A',
  briefcase: '\uE062',
  dumbbell: '\uE3A1',
  graduation_cap: '\uE234',
  gamepad: '\uE0DF',
  film: '\uE0D0',
  music: '\uE122',
  book: '\uE05E',
  star: '\uE176',
  eye: '\uE0BA',
  eye_off: '\uE0BB',
  calendar: '\uE063',
  repeat: '\uE146',
  zap: '\uE1B4',
} as const

export type IconName = keyof typeof iconGlyphs

// Same mapping as AppConstants.Categories.Glyph on Android.
export function categoryIcon(category: string | null, settlement = false): IconName {
  if (settlement) return 'arrow_right'
  const icons: Record<string, IconName> = {
    food: 'utensils_crossed',
    drinks: 'beer',
    transport: 'car',
    travel: 'plane',
    entertainment: 'ticket',
    shopping: 'shopping_cart',
    subscriptions: 'repeat',
    rent: 'home',
    utilities: 'zap',
  }
  return icons[category ?? ''] ?? 'wallet'
}
