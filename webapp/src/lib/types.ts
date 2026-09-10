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

export interface MemberInfo {
  id: string
  display_name: string
  account_id: string | null
  avatar_path: string | null
}

export interface MemberRow {
  member_id: string
  members: MemberInfo
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
  receipt_path: string | null
}

export interface Invite {
  id: string
  token: string
  group_id: string
  target_member_id: string | null
  expires_at: string
  created_at: string
}

export interface RecurringExpense {
  id: string
  group_id: string
  paid_by_member_id: string
  amount: number
  currency: string
  description: string
  category: string
  frequency: 'daily' | 'weekly' | 'monthly' | 'yearly'
  start_date: string
  last_processed_date: string | null
  is_active: boolean
  created_by: string | null
  created_at: string
}

export interface RecurringExpenseShare {
  recurring_expense_id: string
  member_id: string
  share_amount: number
}

export const RECURRING_FREQUENCIES = ['daily', 'weekly', 'monthly', 'yearly'] as const

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
  car_extra_seats: number | null
}

export interface EventRow {
  id: string
  group_id: string
  title: string
  description: string | null
  location: string | null
  starts_at: string
  ends_at: string | null
  needs_transport: boolean
  is_birthday: boolean
  member_id: string | null
  created_by: string | null
  created_at: string
}

export type RsvpResponse = 'going' | 'maybe' | 'not_going'
export type CarStatus = 'none' | 'offering' | 'needs_ride'

export interface EventAttendee {
  event_id: string
  member_id: string
  response: RsvpResponse
  car_status: CarStatus
  car_offered_seats: number | null
}

export const CURRENCIES = [
  'EUR', 'USD', 'GBP', 'AUD', 'BRL', 'CAD', 'CHF', 'CNY', 'CZK', 'DKK',
  'HKD', 'HUF', 'IDR', 'ILS', 'INR', 'ISK', 'JPY', 'KRW', 'MXN', 'MYR',
  'NOK', 'NZD', 'PHP', 'PLN', 'RON', 'SEK', 'SGD', 'THB', 'TRY', 'ZAR',
]
