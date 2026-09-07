export interface Group {
  id: string
  name: string
  currency: string
  created_by: string
}

export interface MyGroupBalance {
  group_id: string
  balance: number
}

export interface MemberRow {
  member_id: string
  members: {
    display_name: string
    account_id: string | null
  }
}

export interface PairwiseBalance {
  group_id: string
  other_member_id: string
  balance: number
}

export interface ExpenseWithPayer {
  id: string
  group_id: string
  paid_by_member_id: string
  amount: number
  currency: string
  description: string
  category: string
  occurred_at: string
  created_at: string
  is_settlement: boolean
  payer: { display_name: string } | null
}

// Keys must stay in sync with AxisApp/AppConstants.cs's Categories.Keys —
// these are stored as plain text in expenses.category with no DB check
// constraint, so a mismatched key here silently fails to resolve a label in
// the MAUI app. Labels are resolved per-viewer via Category_<key> in the i18n
// dictionary (lib/i18n/strings.ts), never stored as text — same reasoning
// AppConstants.Categories' own doc comment gives.
export const CATEGORY_KEYS = ['food', 'transport', 'rent', 'utilities', 'entertainment', 'other'] as const

export interface MyMember {
  id: string
  display_name: string
  birth_date: string | null
  avatar_path: string | null
}

export const CURRENCIES = [
  'EUR', 'USD', 'GBP', 'AUD', 'BRL', 'CAD', 'CHF', 'CNY', 'CZK', 'DKK',
  'HKD', 'HUF', 'IDR', 'ILS', 'INR', 'ISK', 'JPY', 'KRW', 'MXN', 'MYR',
  'NOK', 'NZD', 'PHP', 'PLN', 'RON', 'SEK', 'SGD', 'THB', 'TRY', 'ZAR',
]
