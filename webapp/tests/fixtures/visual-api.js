// Read-only mocked Supabase backend for visual review: several groups, members, expenses,
// balances and events so every main screen renders populated without a real account.
// Writes are acknowledged and discarded, except the opt-in qaOverview scenarios which keep
// attendance changes in this tab's in-memory fixture. Never loaded by the production config.
const backend = new URL(import.meta.env.VITE_SUPABASE_URL)
const userId = '00000000-0000-0000-0000-000000000001'
const id = (n) => `00000000-0000-0000-0000-${String(n).padStart(12, '0')}`
const members = [
  { id: id(10), display_name: 'Irene Soler', account_id: userId, avatar_path: null },
  { id: id(11), display_name: 'Marta Vidal', account_id: id(91), avatar_path: null },
  { id: id(12), display_name: 'Joan Puig', account_id: id(92), avatar_path: null },
  { id: id(13), display_name: 'Lucía Ortega', account_id: null, avatar_path: null },
  { id: id(14), display_name: 'Pau Ferrer', account_id: id(94), avatar_path: null },
  { id: id(15), display_name: 'Alex Romero', account_id: id(95), avatar_path: null },
]
const [me, marta, joan, lucia, pau, alex] = members
const user = { id: userId, aud: 'authenticated', role: 'authenticated', email: 'visual@example.invalid', app_metadata: {}, user_metadata: {} }
const expiry = Math.floor(Date.now() / 1000) + 86400
const accessToken = btoa(JSON.stringify({ alg: 'HS256', typ: 'JWT' })) + '.' + btoa(JSON.stringify({ sub: userId, aud: 'authenticated', role: 'authenticated', exp: expiry })) + '.synthetic-test-signature'
const params = new URLSearchParams(location.search)
const sessionKey = `sb-${backend.hostname.split('.')[0]}-auth-token`
// ?qaSignedOut=1 renders the signed-out screens (login).
if (params.get('qaSignedOut')) localStorage.removeItem(sessionKey)
else localStorage.setItem(sessionKey, JSON.stringify({ access_token: accessToken, refresh_token: 'synthetic-test-only', token_type: 'bearer', expires_at: expiry, expires_in: 86400, user }))
localStorage.setItem('axis_language_override', params.get('qaLanguage') || 'es')
if (params.get('qaAccent')) localStorage.setItem('axis.accent', params.get('qaAccent'))

const piso = id(100), familia = id(101), viaje = id(102)
const groups = [
  { id: piso, name: 'Piso Gràcia', currency: 'EUR', created_by: userId, color: 'Blue', icon: 'home' },
  { id: familia, name: 'Familia', currency: 'EUR', created_by: id(91), color: 'Pink', icon: 'heart' },
  { id: viaje, name: 'Asturias 2026', currency: 'EUR', created_by: userId, color: 'Green', icon: 'tent' },
]
const groupMembers = [
  ...[me, marta, joan, lucia].map((m) => ({ group_id: piso, member_id: m.id, members: m })),
  ...[me, marta, pau].map((m) => ({ group_id: familia, member_id: m.id, members: m })),
  ...[me, marta, joan, alex, pau].map((m) => ({ group_id: viaje, member_id: m.id, members: m })),
]
const day = (offset, hour = 12) => {
  const d = new Date()
  d.setDate(d.getDate() + offset)
  d.setHours(hour, 0, 0, 0)
  return d.toISOString()
}
let n = 200
const expense = (group_id, payer, amount, description, category, offset, extra = {}) => ({
  id: id(n++), group_id, paid_by_member_id: payer.id, amount, currency: 'EUR', amount_in_group_currency: amount,
  description, category, occurred_at: day(offset), created_at: day(offset), created_by: userId,
  is_settlement: false, split_mode: 'equal', event_id: null, receipt_path: null, ...extra,
})
const expenses = [
  expense(piso, joan, 64.3, 'Mercadona', 'food', 0),
  expense(piso, marta, 15, 'Marta → Irene', 'other', 0, { is_settlement: true, split_mode: null }),
  expense(piso, me, 88.12, 'Factura de la luz', 'utilities', -1, { receipt_path: 'x' }),
  expense(piso, marta, 30, 'Fibra Digi', 'subscriptions', -1),
  expense(piso, me, 42, 'Cena de pizzas', 'food', -4),
  expense(piso, lucia, 23.5, 'Productos de limpieza', 'shopping', -6),
  expense(piso, me, 650, 'Alquiler octubre', 'rent', -27),
  expense(familia, pau, 37.2, 'Regalo cumpleaños abuela', 'shopping', -2),
  expense(familia, marta, 18, 'Gasolina', 'transport', -9),
  expense(viaje, alex, 312, 'Casa rural Cangas', 'travel', -20),
  expense(viaje, me, 96.4, 'Sidrería', 'drinks', -18),
]
const shares = expenses.flatMap((e) => {
  const people = groupMembers.filter((gm) => gm.group_id === e.group_id).map((gm) => gm.member_id)
  if (e.is_settlement) return [{ expense_id: e.id, member_id: me.id, share_amount: e.amount, share_amount_in_group_currency: e.amount, expenses: { group_id: e.group_id } }]
  return people.map((member_id) => ({ expense_id: e.id, member_id, share_amount: +(e.amount / people.length).toFixed(2), share_amount_in_group_currency: +(e.amount / people.length).toFixed(2), expenses: { group_id: e.group_id } }))
})
const pairwise = [
  { group_id: piso, other_member_id: marta.id, balance: 30 },
  { group_id: piso, other_member_id: joan.id, balance: 28 },
  { group_id: piso, other_member_id: lucia.id, balance: -12 },
  { group_id: familia, other_member_id: pau.id, balance: -12.4 },
  { group_id: familia, other_member_id: marta.id, balance: 0 },
]
const groupBalances = [
  { group_id: piso, balance: 46 },
  { group_id: familia, balance: -12.4 },
  { group_id: viaje, balance: 0 },
]
const events = [
  { id: id(300), group_id: piso, title: 'Cena de piso', description: 'Traed postre', location: 'Casa', starts_at: day(3, 21), ends_at: day(3, 23), needs_transport: false, is_birthday: false, member_id: null, created_by: userId, created_at: day(-3) },
  { id: id(301), group_id: piso, title: 'Excursión a Montserrat', description: null, location: 'Montserrat', starts_at: day(10, 9), ends_at: null, needs_transport: true, is_birthday: false, member_id: null, created_by: id(91), created_at: day(-2) },
  { id: id(302), group_id: piso, title: 'Cumpleaños de Joan', description: null, location: null, starts_at: day(16, 0), ends_at: null, needs_transport: false, is_birthday: true, member_id: joan.id, created_by: null, created_at: day(-30) },
]
const attendees = [
  { event_id: id(300), member_id: me.id, response: 'going', car_status: 'none', car_offered_seats: null },
  { event_id: id(300), member_id: marta.id, response: 'going', car_status: 'none', car_offered_seats: null },
  { event_id: id(300), member_id: joan.id, response: 'maybe', car_status: 'none', car_offered_seats: null },
  { event_id: id(301), member_id: marta.id, response: 'going', car_status: 'offering', car_offered_seats: 3 },
  { event_id: id(301), member_id: joan.id, response: 'going', car_status: 'needs_ride', car_offered_seats: null },
]
const recurring = [
  { id: id(400), group_id: piso, paid_by_member_id: me.id, amount: 650, currency: 'EUR', description: 'Alquiler', category: 'rent', split_mode: 'equal', frequency: 'monthly', start_date: '2026-01-01', last_processed_date: '2026-10-01', is_active: true, created_by: userId, created_at: day(-300) },
]
const tables = {
  groups, group_members: groupMembers, expenses, expense_shares: shares, my_pairwise_balances: pairwise,
  my_group_balances: groupBalances, events, event_attendees: attendees, recurring_expenses: recurring,
  recurring_expense_shares: [], member_aliases: [], invites: [], device_tokens: [],
  members: members.map((m) => ({ ...m, birth_date: m === me ? '1994-03-12' : null, car_extra_seats: m === me ? 3 : null })),
  calendar_subscriptions: [{ id: 'visual-subscription', member_id: me.id, token: 'synthetic-not-a-real-token', created_at: day(-40), last_accessed_at: null }],
}

const overviewScenario = params.get('qaOverview')
if (overviewScenario === 'settled') tables.my_pairwise_balances = pairwise.map(row => ({ ...row, balance: 0 }))
if (overviewScenario === 'owe-zero') tables.my_pairwise_balances = pairwise.filter(row => row.balance >= 0)
if (overviewScenario === 'empty') {
  tables.groups = []; tables.group_members = []; tables.my_pairwise_balances = []; tables.my_group_balances = []
}
if (overviewScenario === 'no-events') tables.events = []
if (overviewScenario === 'mixed') {
  groups[2].currency = 'GBP'
  tables.my_pairwise_balances = [...pairwise, { group_id: viaje, other_member_id: marta.id, balance: 15 }, { group_id: viaje, other_member_id: alex.id, balance: -20 }]
}
if (overviewScenario) window.__axisOverviewWrites = []

// Applies PostgREST eq./in. filters on top-level columns; everything else (order, embedded
// filters, select lists) is ignored, which is enough for rendering.
function filterRows(rows, searchParams) {
  return rows.filter((row) => {
    for (const [key, value] of searchParams) {
      if (key.includes('.') || !(key in row)) continue
      if (value.startsWith('eq.') && String(row[key]) !== value.slice(3)) return false
      if (value.startsWith('gte.') && row[key] < value.slice(4)) return false
      if (value.startsWith('in.(')) {
        const set = value.slice(4, -1).split(',').map((v) => v.replace(/"/g, ''))
        if (!set.includes(String(row[key]))) return false
      }
    }
    return true
  })
}
function reply(data, request, status = 200) {
  const accept = new Headers(request.headers).get('accept') || ''
  if (Array.isArray(data) && accept.includes('vnd.pgrst.object')) data = data[0] ?? null
  const count = Array.isArray(data) ? data.length : 1
  return new Response(request.method === 'HEAD' ? null : JSON.stringify(data), { status, headers: { 'Content-Type': 'application/json', 'Content-Range': `0-${Math.max(0, count - 1)}/${count}` } })
}
const realFetch = window.fetch.bind(window)
window.fetch = async (input, init) => {
  const request = new Request(input, init)
  const url = new URL(request.url)
  if (url.origin === location.origin) return realFetch(input, init)
  if (url.origin !== backend.origin) return reply({ message: 'External requests disabled in visual fixture' }, request, 503)
  if (url.pathname.includes('/auth/v1/')) return reply({ user, ...user }, request)
  // Synthetic preview states exercise rendering without creating or redeeming real invites.
  if (url.pathname.endsWith('/rpc/preview_invite')) {
    const scenario = params.get('qaInvite')
    if (scenario === 'claim') return reply({ status: 'ok', group_name: 'Preview QA', group_currency: 'EUR', member_count: 3, target_name: 'Preview Guest' }, request)
    if (scenario === 'used' || scenario === 'inviter_gone') return reply({ status: scenario }, request)
  }
  if (url.pathname.includes('/rpc/')) return reply(null, request)
  if (overviewScenario && url.pathname.endsWith('/event_attendees') && request.method === 'POST') {
    const payload = await request.json()
    if (overviewScenario === 'save-error') return new Response(JSON.stringify({ message: 'Synthetic save failure' }), { status: 500, headers: { 'Content-Type': 'application/json' } })
    for (const row of (Array.isArray(payload) ? payload : [payload])) {
      const index = attendees.findIndex(a => a.event_id === row.event_id && a.member_id === row.member_id)
      if (index < 0) attendees.push(row)
      else attendees[index] = row
      window.__axisOverviewWrites.push({ ...row })
    }
    return reply([], request)
  }
  if (request.method !== 'GET' && request.method !== 'HEAD') return reply([], request)
  if (url.searchParams.get('limit') === '0') return reply([], request)
  const table = url.pathname.split('/').pop()
  if (overviewScenario === 'events-error' && table === 'events') return new Response(JSON.stringify({ message: 'Synthetic events failure' }), { status: 500, headers: { 'Content-Type': 'application/json' } })
  if (overviewScenario === 'activity-error' && table === 'expenses') return new Response(JSON.stringify({ message: 'Synthetic activity failure' }), { status: 500, headers: { 'Content-Type': 'application/json' } })
  const rows = filterRows(tables[table] ?? [], url.searchParams)
  const order = url.searchParams.get('order')
  const ordered = [...rows].sort((a, b) => {
    for (const part of (order || '').split(',')) {
      const [column, direction] = part.split('.')
      if (!column) continue
      const comparison = String(a[column] ?? '').localeCompare(String(b[column] ?? ''))
      if (comparison) return direction === 'desc' ? -comparison : comparison
    }
    return 0
  })
  const offset = Number(url.searchParams.get('offset') || 0)
  const limit = Number(url.searchParams.get('limit') || ordered.length)
  return reply(ordered.slice(offset, offset + limit), request)
}
// ?qaTab=1|2 opens Group Detail's Events/Stats tab, which has no route of its own.
// A MutationObserver rather than polling: timers would burn headless virtual time.
if (params.get('qaTab')) {
  const observer = new MutationObserver(() => {
    const tabs = document.querySelectorAll('.group-tabs button')
    if (!tabs.length) return
    observer.disconnect()
    tabs[Number(params.get('qaTab'))]?.click()
  })
  observer.observe(document, { childList: true, subtree: true })
}
window.WebSocket = class extends EventTarget {
  static CONNECTING = 0; static OPEN = 1; static CLOSING = 2; static CLOSED = 3
  readyState = 3
  send() {}
  close() {}
}
