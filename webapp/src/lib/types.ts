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

export const CATEGORIES = [
  { key: 'general', label: 'General' },
  { key: 'food', label: 'Food & drink' },
  { key: 'transport', label: 'Transport' },
  { key: 'housing', label: 'Housing' },
  { key: 'entertainment', label: 'Entertainment' },
  { key: 'other', label: 'Other' },
]

export const CURRENCIES = [
  'EUR', 'USD', 'GBP', 'AUD', 'BRL', 'CAD', 'CHF', 'CNY', 'CZK', 'DKK',
  'HKD', 'HUF', 'IDR', 'ILS', 'INR', 'ISK', 'JPY', 'KRW', 'MXN', 'MYR',
  'NOK', 'NZD', 'PHP', 'PLN', 'RON', 'SEK', 'SGD', 'THB', 'TRY', 'ZAR',
]
