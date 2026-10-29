const backend = new URL(import.meta.env.VITE_SUPABASE_URL)
const groupId = '00000000-0000-0000-0000-000000000100'
const expenseId = '00000000-0000-0000-0000-000000000200'
const templateId = '00000000-0000-0000-0000-000000000300'
const eventId = '00000000-0000-0000-0000-000000000400'
const paymentId = '00000000-0000-0000-0000-000000000500'
const userId = '00000000-0000-0000-0000-000000000001'
const members = [
  { id: '00000000-0000-0000-0000-000000000010', display_name: 'QA User', account_id: userId, avatar_path: null },
  { id: '00000000-0000-0000-0000-000000000020', display_name: 'QA Alex', account_id: null, avatar_path: null },
]
const user = { id: userId, aud: 'authenticated', role: 'authenticated', email: 'qa@example.invalid', app_metadata: {}, user_metadata: {} }
const expiry = Math.floor(Date.now() / 1000) + 86400
const accessToken = btoa(JSON.stringify({ alg: 'HS256', typ: 'JWT' })) + '.' + btoa(JSON.stringify({ sub: userId, aud: 'authenticated', role: 'authenticated', exp: expiry })) + '.synthetic-test-signature'
localStorage.setItem(`sb-${backend.hostname.split('.')[0]}-auth-token`, JSON.stringify({ access_token: accessToken, refresh_token: 'synthetic-test-only', token_type: 'bearer', expires_at: expiry, expires_in: 86400, user }))
localStorage.setItem('axis_language_override', new URLSearchParams(location.search).get('qaLanguage') || 'en')
const group = { id: groupId, name: 'Unsaved editor QA (mocked)', currency: 'EUR', created_by: userId, color: '#3987E5', icon: null }
const initialExpense = { id: expenseId, group_id: groupId, paid_by_member_id: members[0].id, amount: 12, currency: 'EUR', amount_in_group_currency: 12, description: 'Original dinner', category: 'food', occurred_at: '2026-10-28T12:00:00Z', created_at: '2026-10-28T12:00:00Z', created_by: userId, is_settlement: false, split_mode: 'equal', event_id: null, receipt_path: null }
const initialTemplate = { ...initialExpense, id: templateId, description: 'Original monthly bill', frequency: 'monthly', start_date: '2026-10-28', last_processed_date: null, is_active: false }
const initialEvent = { id: eventId, group_id: groupId, title: 'Original outing', description: '', location: 'Park', starts_at: '2026-11-01T12:00:00Z', ends_at: null, needs_transport: false, created_by: userId, created_at: initialExpense.created_at }
const initialPayment = { ...initialExpense, id: paymentId, paid_by_member_id: members[1].id, is_settlement: true, split_mode: null, amount: 2, amount_in_group_currency: 2, description: 'Original repayment' }
function readState() { return { expense: initialExpense, template: initialTemplate, event: initialEvent, payment: initialPayment, writes: 0, ...JSON.parse(localStorage.getItem('axis.unsaved.qa') || 'null') } }
let failSave = false
let delaySave = false
let outsideAttempts = 0
function updateStatus() {
  const status = document.getElementById('qa-status')
  if (status) status.textContent = `Mocked API · writes ${readState().writes} · failures ${failSave ? 'ON' : 'OFF'} · delay ${delaySave ? 'ON' : 'OFF'} · external requests blocked ${outsideAttempts}`
}
function reply(data, request, status = 200) {
  const accept = new Headers(request.headers).get('accept') || ''
  if (Array.isArray(data) && accept.includes('vnd.pgrst.object')) data = data[0] ?? null
  return new Response(request.method === 'HEAD' ? null : JSON.stringify(data), { status, headers: { 'Content-Type': 'application/json', 'Content-Range': '0-0/1' } })
}
const realFetch = window.fetch.bind(window)
window.fetch = async (input, init) => {
  const request = new Request(input, init)
  const url = new URL(request.url)
  if (url.origin === location.origin) return realFetch(input, init)
  if (url.origin !== backend.origin) { outsideAttempts++; updateStatus(); return reply({ message: 'External requests disabled in QA fixture' }, request, 503) }
  const state = readState()
  const table = url.pathname.split('/').pop()
  if (url.pathname.includes('/auth/v1/')) return reply({ user, ...user }, request)
  if (url.pathname.includes('/rpc/save_')) {
    if (delaySave) await new Promise(resolve => setTimeout(resolve, 5000))
    if (failSave) return reply({ message: 'Simulated save failure', code: 'QA_FAILURE' }, request, 500)
    const body = await request.json()
    const recurring = table === 'save_recurring_expense'
    const payload = body.p_template || body.p_expense
    const slot = recurring ? 'template' : payload.is_settlement ? 'payment' : 'expense'
    const id = payload.id || (recurring ? templateId : payload.is_settlement ? paymentId : expenseId)
    state[slot] = { ...state[slot], ...payload, id }
    state.writes++
    localStorage.setItem('axis.unsaved.qa', JSON.stringify(state))
    updateStatus()
    return reply(id, request)
  }
  if (url.searchParams.get('limit') === '0') return reply([], request)
  if (table === 'groups') return reply([group], request)
  if (table === 'group_members') return reply(members.map(m => ({ group_id: groupId, member_id: m.id, members: m })), request)
  if (table === 'members') return reply([{ ...members[0], birth_date: null, car_extra_seats: 3 }], request)
  if (table === 'events') {
    if (request.method !== 'GET' && request.method !== 'HEAD') {
      if (delaySave) await new Promise(resolve => setTimeout(resolve, 5000))
      if (failSave) return reply({ message: 'Simulated save failure', code: 'QA_FAILURE' }, request, 500)
      const payload = request.method === 'DELETE' ? {} : await request.json()
      state.event = { ...state.event, ...payload }
      state.writes++
      localStorage.setItem('axis.unsaved.qa', JSON.stringify(state)); updateStatus()
    }
    return reply([state.event], request)
  }
  if (table === 'expenses') return reply(url.searchParams.get('id') === `eq.${paymentId}` ? [state.payment]
    : url.searchParams.has('id') ? [state.expense] : [state.expense, state.payment], request)
  if (table === 'recurring_expenses') return reply([state.template], request)
  if (table === 'expense_shares' || table === 'recurring_expense_shares') {
    if (url.searchParams.get('expense_id') === `eq.${paymentId}`)
      return reply([{ member_id: members[0].id, expense_id: paymentId, share_amount: state.payment.amount, share_amount_in_group_currency: state.payment.amount }], request)
    const amount = table === 'expense_shares' ? state.expense.amount : state.template.amount
    return reply(members.map(m => ({ member_id: m.id, expense_id: expenseId, recurring_expense_id: templateId, share_amount: amount / 2, share_amount_in_group_currency: amount / 2 })), request)
  }
  if (table === 'calendar_subscriptions') return reply([{ id: 'test-subscription', member_id: members[0].id, token: 'synthetic-not-a-real-token' }], request)
  if (table === 'my_group_balances') return reply([{ group_id: groupId, balance: 0 }], request)
  if (table === 'my_pairwise_balances') return reply([{ group_id: groupId, other_member_id: members[1].id, balance: 6 }], request)
  return reply([], request)
}
// The fixture never opens a real realtime connection.
window.WebSocket = class extends EventTarget {
  static CONNECTING = 0; static OPEN = 1; static CLOSING = 2; static CLOSED = 3
  readyState = 3
  send() {}
  close() {}
}
window.addEventListener('DOMContentLoaded', () => {
  const controls = document.createElement('aside')
  controls.style.cssText = 'position:fixed;z-index:5;bottom:4px;left:4px;padding:8px;background:white;color:black;border:2px solid black;font:12px sans-serif;max-width:95vw'
  controls.innerHTML = '<strong id="qa-status"></strong><button id="qa-failure">Toggle save failure</button><button id="qa-delay">Toggle save delay</button><button id="qa-unload">Check reload warning</button><output id="qa-unload-result"></output>'
  document.body.append(controls)
  document.getElementById('qa-failure').onclick = () => { failSave = !failSave; updateStatus() }
  document.getElementById('qa-delay').onclick = () => { delaySave = !delaySave; updateStatus() }
  document.getElementById('qa-unload').onclick = () => {
    const event = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(event)
    document.getElementById('qa-unload-result').textContent = event.defaultPrevented ? 'Reload warning active' : 'No reload warning'
  }
  updateStatus()
})
