import { expenseEditorSnapshot } from '../src/lib/expenseEditorSnapshot.ts'
import { editableEqualSplitCents, restoredSplitMode } from '../src/lib/splitMode.ts'
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { amountCents, parseAmountInput } from '../src/lib/amountInput.ts'
import { equalSplitCents } from '../src/lib/expenseMath.ts'
import { SerializedUpdates } from '../src/lib/serializedUpdates.ts'
import { CoalescedWrites } from '../src/lib/coalescedWrites.ts'
import { nextDue } from '../src/lib/recurringSchedule.ts'
import { formatExpenseAmount } from '../src/lib/amountDisplay.ts'
import { pairBreakdown } from '../src/lib/pairBreakdown.ts'
import { isSettled, summarizeBalances } from '../src/lib/balanceSummary.ts'
import { OTHER_COLOR, categoryColor, categoryFrequency, categoryTrend, expensesCsv, filterByRange, rangeCutoff, settlementsByMonth, spendByCategory, spendByMember } from '../src/lib/stats.ts'
import { CATEGORY_KEYS } from '../src/lib/types.ts'
import { categoryIcon, iconGlyphs } from '../src/lib/icons.ts'
import { activityDate, expenseTimestamp, isCalendarDate, localDateInput, localDateTimeToIso, formatEventRange } from '../src/lib/dates.ts'
import { isSettlementDate, settlementCents, settlementParties, settlementPayload } from '../src/lib/settlements.ts'
import { strings } from '../src/lib/i18n/strings.ts'
import { assertSupabaseConfig } from '../src/lib/runtimeConfig.ts'
import { eventEditorSnapshot, settlementEditorSnapshot } from '../src/lib/formEditorSnapshots.ts'
import { RefreshRevision } from '../src/lib/refreshRevision.ts'
import { resolveConfig } from 'vite'
import { mkdtemp, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

test('late attendance responses cannot replace a newer read or a local seat/RSVP edit', () => {
  const revision = new RefreshRevision()
  const firstRead = revision.begin()
  const newerRead = revision.begin()
  assert.equal(firstRead(), false)
  assert.equal(newerRead(), true)
  const firstWrite = revision.invalidate()
  assert.equal(newerRead(), false)
  const firstWriteRead = revision.begin(firstWrite)
  const secondWrite = revision.invalidate()
  assert.equal(firstWriteRead(), false)
  const finalRead = revision.begin(secondWrite)
  assert.equal(revision.begin(firstWrite)(), false)
  assert.equal(finalRead(), true, 'an older write callback must not cancel the current read')
  revision.invalidate()
  assert.equal(finalRead(), false, 'unmount or a subsequent edit invalidates pending results')
})

test('event and repayment dirty snapshots protect editable fields and ignore inactive/display values', () => {
  const event = { title: 'Plan', description: '', location: '', startDate: '2026-10-28', startTime: '12:00', hasEndTime: false, endDate: '2026-10-28', endTime: '13:00', needsTransport: false }
  assert.equal(eventEditorSnapshot(event), eventEditorSnapshot({ ...event, endDate: '2026-11-01', endTime: '14:00' }))
  for (const change of [{ title: 'New plan' }, { description: ' ' }, { location: 'Park' }, { startDate: 'invalid' }, { startTime: '' }, { hasEndTime: true }, { needsTransport: true }])
    assert.notEqual(eventEditorSnapshot(event), eventEditorSnapshot({ ...event, ...change }))
  const payment = { payerId: 'a', recipientId: 'b', amount: '8.50', currency: 'EUR', date: '2026-10-28', note: '' }
  assert.equal(settlementEditorSnapshot(payment), settlementEditorSnapshot({ ...payment, amount: '8,50', id: 'saved-id' }))
  for (const change of [{ payerId: 'c' }, { recipientId: 'c' }, { amount: '8,' }, { currency: 'USD' }, { date: '' }, { note: 'Partial' }])
    assert.notEqual(settlementEditorSnapshot(payment), settlementEditorSnapshot({ ...payment, ...change }))
})

test('startup configuration rejects missing settings without exposing values', () => {
  const valid = { VITE_SUPABASE_URL: 'https://qa.supabase.co', VITE_SUPABASE_PUBLISHABLE_KEY: 'sb_publishable_test' }
  assert.doesNotThrow(() => assertSupabaseConfig(valid))
  for (const field of Object.keys(valid)) {
    for (const value of [undefined, '', '   '])
      assert.throws(() => assertSupabaseConfig({ ...valid, [field]: value }), new RegExp(`Missing ${field}`))
  }
  for (const url of ['broken', 'ftp://example.com', 'https://YOUR-PROJECT-REF.supabase.co', 'https://user:password@example.com'])
    assert.throws(() => assertSupabaseConfig({ ...valid, VITE_SUPABASE_URL: url }), /^Error: Invalid VITE_SUPABASE_URL\.$/)
  for (const key of ['sb_publishable_...', 'key with spaces', 'sb_secret_private-test-value']) {
    assert.throws(() => assertSupabaseConfig({ ...valid, VITE_SUPABASE_PUBLISHABLE_KEY: key }), (error) => {
      assert.ok(!error.message.includes(key))
      return /VITE_SUPABASE_PUBLISHABLE_KEY/.test(error.message)
    })
  }
})

test('the actual Vite build configuration blocks missing env and loads valid deployment env', async () => {
  const root = await mkdtemp(join(tmpdir(), 'axis-build-config-'))
  const configFile = fileURLToPath(new URL('../vite.config.ts', import.meta.url))
  const options = { configFile, root, mode: 'production', logLevel: 'silent' }
  const names = ['VITE_SUPABASE_URL', 'VITE_SUPABASE_PUBLISHABLE_KEY']
  const previous = Object.fromEntries(names.map((name) => [name, process.env[name]]))
  try {
    for (const name of names) delete process.env[name]
    await assert.rejects(resolveConfig(options, 'build'), /Missing VITE_SUPABASE_URL/)
    await writeFile(join(root, '.env.production'), 'VITE_SUPABASE_URL=https://qa.supabase.co\n')
    await assert.rejects(resolveConfig(options, 'build'), /Missing VITE_SUPABASE_PUBLISHABLE_KEY/)
    await writeFile(join(root, '.env.production'), 'VITE_SUPABASE_URL=https://qa.supabase.co\nVITE_SUPABASE_PUBLISHABLE_KEY=sb_publishable_test\n')
    await resolveConfig(options, 'build')
    process.env.VITE_SUPABASE_URL = ''
    await assert.rejects(resolveConfig(options, 'build'), /Missing VITE_SUPABASE_URL/)
  } finally {
    for (const name of names) {
      if (previous[name] === undefined) delete process.env[name]
      else process.env[name] = previous[name]
    }
    await rm(root, { recursive: true, force: true })
  }
})

test('expense, share and settlement amounts follow the same cross-client input contract', () => {
  const cases = JSON.parse(readFileSync(new URL('../../tests/amount-input-cases.json', import.meta.url), 'utf8'))
  for (const sample of cases) {
    const parsed = parseAmountInput(sample.text, sample.currency ?? 'EUR')
    assert.equal(parsed.cents, sample.cents, JSON.stringify(sample.text))
    assert.equal(parsed.error, sample.error ?? null, JSON.stringify(sample.text))
    assert.equal(amountCents(sample.text, sample.currency ?? 'EUR'), sample.cents)
    assert.equal(settlementCents(sample.text, sample.currency ?? 'EUR'), sample.cents)
  }
  const total = amountCents('8,50', 'EUR')
  assert.equal(amountCents('4.25', 'EUR') + amountCents('4,25', 'EUR'), total)
  assert.equal(amountCents('4.251', 'EUR'), null)
  assert.equal(amountCents('4,25.0', 'EUR'), null)
})

test('settlements accept exact positive cents and reject ambiguous or overflowing amounts', () => {
  for (const [input, cents] of [['8.50', 850], ['8,50', 850], [' 0,01 ', 1], ['12', 1200], ['9999999999.99', 999999999999]]) {
    assert.equal(settlementCents(input, 'EUR'), cents)
  }
  for (const invalid of ['', ' ', '0', '-1', '1.001', '1,234.56', '1.234,56', '1e3', 'Infinity', '10000000000']) {
    assert.equal(settlementCents(invalid, 'EUR'), null, invalid)
  }
})

test('settlement dates reject rollover and preserve leap days', () => {
  for (const date of ['2026-02-31', '2026-02-29', '2026-13-01', '2026-00-01', '', '0000-01-01'])
    assert.equal(isSettlementDate(date), false, date)
  assert.equal(isSettlementDate('2028-02-29'), true)
  assert.equal(isSettlementDate('2026-10-27'), true)
})

test('full and partial repayments reduce the correct pairwise debt in either direction', () => {
  for (const balance of [20, -20]) {
    const parties = settlementParties('me', 'alex', balance)
    for (const amount of ['20', '7,50']) {
      const payload = settlementPayload({ ...parties, amount, currency: 'EUR', note: '', date: '2026-10-27' }, 'group', '2026-10-27T00:00:00Z')
      assert.equal(payload.p_shares.length, 1)
      assert.equal(payload.p_shares[0].share_amount, payload.p_expense.amount)
      const basePayer = balance > 0 ? 'me' : 'alex'
      const baseRecipient = balance > 0 ? 'alex' : 'me'
      const result = pairBreakdown([
        { id: 'debt', paid_by_member_id: basePayer, is_settlement: false, occurred_at: '2026-10-26' },
        { ...payload.p_expense, id: 'repayment' },
      ], [
        { expense_id: 'debt', member_id: baseRecipient, share_amount_in_group_currency: 20 },
        { expense_id: 'repayment', member_id: payload.p_shares[0].member_id, share_amount_in_group_currency: payload.p_expense.amount },
      ], 'me', 'alex')
      assert.equal(result.balance, ((balance > 0 ? 1 : -1) * (20 - payload.p_expense.amount)) || 0)
    }
  }
})

test('settlements save an empty note as empty (never generated text) and retain edit metadata', () => {
  const draft = { id: 'saved', payerId: 'me', recipientId: 'alex', amount: '4.20', currency: 'EUR', date: '2026-10-27', note: '   ', receiptPath: 'receipt.webp', eventId: 'event' }
  const payload = settlementPayload(draft, 'group', 'original-timestamp')
  assert.equal(payload.p_expense.description, '')
  assert.equal(payload.p_expense.id, 'saved')
  assert.equal(payload.p_expense.receipt_path, 'receipt.webp')
  assert.equal(payload.p_expense.event_id, 'event')
  assert.equal(payload.p_expense.occurred_at, 'original-timestamp')
  assert.equal(payload.p_expense.is_settlement, true)
  assert.equal(payload.p_expense.created_at, undefined)
  assert.equal(payload.p_expense.created_by, undefined)
  assert.equal(settlementPayload({ ...draft, note: '  Bank transfer  ' }, 'group', 'timestamp').p_expense.description, 'Bank transfer')
  assert.throws(() => settlementPayload({ ...draft, recipientId: 'me' }, 'group', 'timestamp'))
})

test('equal splits conserve every cent and differ by at most one cent', () => {
  for (let count = 1; count <= 30; count++) {
    const ids = Array.from({ length: count }, (_, i) => String(i))
    for (let total = 0; total <= 1000; total++) {
      const values = Object.values(equalSplitCents(total, ids))
      assert.equal(values.reduce((sum, value) => sum + value, 0), total)
      assert.ok(Math.min(...values) >= 0)
      assert.ok(Math.max(...values) - Math.min(...values) <= 1)
      if (total >= count) assert.ok(values.every((value) => value > 0))
    }
  }
  assert.deepEqual(Object.values(equalSplitCents(6, ['a', 'b', 'c', 'd'])), [2, 2, 1, 1])
})

test('expense dates preserve original instants across time zones and DST', () => {
  for (const zone of ['Europe/Madrid', 'America/Los_Angeles', 'Asia/Tokyo']) {
    const result = execFileSync(process.execPath, ['--experimental-strip-types', '--input-type=module', '-e', `
      import { localDateInput, expenseTimestamp } from './src/lib/dates.ts';
      const originals = ['2026-10-25T23:00:00+00:00', '2026-03-28T23:00:00+00:00', '2026-07-10T18:23:45.123+00:00'];
      const preserved = originals.every(original => expenseTimestamp(localDateInput(original), original) === original);
      const today = new Date(2026, 9, 26, 0, 15);
      const changed = expenseTimestamp('2026-10-27', null);
      console.log(JSON.stringify({ preserved, today: localDateInput(today), changed: localDateInput(changed) }));
    `], { cwd: new URL('..', import.meta.url), env: { ...process.env, TZ: zone }, encoding: 'utf8' })
    assert.deepEqual(JSON.parse(result), { preserved: true, today: '2026-10-26', changed: '2026-10-27' })
  }
})

test('rapid updates compose using the last successful write, not stale render state', async () => {
  const queue = new SerializedUpdates()
  const writes = []
  const persist = async (value) => { await new Promise(resolve => setImmediate(resolve)); writes.push(value) }
  await Promise.all(Array.from({ length: 10 }, () => queue.run('event', 0, value => value + 1, persist)))
  assert.deepEqual(writes, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
  await queue.run('event', 20, value => value - 1, persist)
  assert.equal(writes.at(-1), 19)
})

test('failed updates abort dependent writes and allow a fresh retry', async () => {
  const queue = new SerializedUpdates()
  let writes = 0
  const first = queue.run('event', 0, value => value + 1, async () => { throw new Error('offline') })
  const second = queue.run('event', 0, value => value + 1, async () => { writes++ })
  const results = await Promise.allSettled([first, second])
  assert.ok(results.every(result => result.status === 'rejected'))
  assert.equal(writes, 0)
  assert.equal(await queue.run('event', 4, value => value + 1, async () => {}), 5)
})

test('event captions include both endpoints for multi-day events', () => {
  const caption = formatEventRange('2026-10-26T12:00:00Z', '2026-10-27T14:00:00Z', 'en')
  assert.match(caption, /26/)
  assert.match(caption, /27/)
  assert.match(caption, / – /)
})

test('rapid seat taps collapse into one write of the final value; flush sends immediately', async () => {
  const writes = []
  const coalesced = new CoalescedWrites(10, (key, value) => writes.push([key, value]))
  coalesced.set('event', 1)
  coalesced.set('event', 2)
  coalesced.set('event', 3)
  await new Promise((resolve) => setTimeout(resolve, 40))
  assert.deepEqual(writes, [['event', 3]])

  coalesced.set('a', 5)
  coalesced.set('b', 7)
  coalesced.flushAll()
  coalesced.flush('a')
  await new Promise((resolve) => setTimeout(resolve, 40))
  assert.deepEqual(writes.slice(1), [['a', 5], ['b', 7]])
})

test('recurring next-due matches the server materializer and RecurringSchedule.cs', () => {
  for (const frequency of ['daily', 'weekly', 'monthly', 'yearly']) {
    assert.equal(nextDue('2026-10-26', null, frequency), '2026-10-26')
  }
  assert.equal(nextDue('2026-10-26', '2026-10-26', 'daily'), '2026-10-27')
  assert.equal(nextDue('2026-12-28', '2026-12-28', 'weekly'), '2027-01-04')
  assert.equal(nextDue('2026-10-26', '2026-10-26', 'monthly'), '2026-11-26')
  assert.equal(nextDue('2026-01-31', '2026-01-31', 'monthly'), '2026-02-28')
  assert.equal(nextDue('2026-01-31', '2026-02-28', 'monthly'), '2026-03-31')
  assert.equal(nextDue('2026-01-31', '2026-03-28', 'monthly'), '2026-04-30')
  assert.equal(nextDue('2024-02-29', '2024-02-29', 'yearly'), '2025-02-28')
  assert.equal(nextDue('2024-02-29', '2027-02-28', 'yearly'), '2028-02-29')
})
test('stats aggregations mirror GroupStatsViewModel', () => {
  const e = (id, payer, amount, category, occurred, settlement = false) => ({
    id, paid_by_member_id: payer, amount, currency: 'EUR', amount_in_group_currency: amount,
    description: id, category, occurred_at: occurred, is_settlement: settlement,
  })
  const expenses = [
    e('a', 'm1', 9, 'food', '2026-09-10T12:00:00Z'),
    e('b', 'm1', 10, 'drinks', '2026-09-11T12:00:00Z'),
    e('c', 'm2', 4, 'food', '2026-10-01T12:00:00Z'),
    e('d', 'm2', 50, null, '2026-10-02T12:00:00Z', true),
  ]
  const shares = [
    { expense_id: 'a', member_id: 'm2', share_amount_in_group_currency: 9 },
    { expense_id: 'b', member_id: 'm1', share_amount_in_group_currency: 5 },
    { expense_id: 'b', member_id: 'm2', share_amount_in_group_currency: 5 },
    { expense_id: 'c', member_id: 'm1', share_amount_in_group_currency: 4 },
    { expense_id: 'd', member_id: 'm1', share_amount_in_group_currency: 50 },
  ]

  // Numeric sort (13 before 10), settlements excluded.
  assert.deepEqual(spendByCategory(expenses), [{ key: 'food', value: 13 }, { key: 'drinks', value: 10 }])
  assert.deepEqual(categoryFrequency(expenses), [{ key: 'food', value: 2 }, { key: 'drinks', value: 1 }])
  assert.deepEqual(spendByMember(expenses, shares, 'paid'), [{ key: 'm1', value: 19 }, { key: 'm2', value: 4 }])
  assert.deepEqual(spendByMember(expenses, shares, 'share'), [{ key: 'm2', value: 14 }, { key: 'm1', value: 9 }])
  assert.deepEqual(settlementsByMonth(expenses), [{ key: '2026-10', value: 1 }])

  const trend = categoryTrend(expenses)
  assert.deepEqual(trend.months, ['2026-09', '2026-10'])
  assert.deepEqual(trend.series, [{ key: 'food', values: [9, 4] }, { key: 'drinks', values: [10, 0] }])

  // Colour follows the category's fixed slot; null, unknown and past-the-palette keys are the
  // neutral "other".
  assert.equal(categoryColor('drinks'), '#D95926')
  assert.equal(categoryColor('rent'), '#E66767')
  assert.equal(categoryColor('utilities'), OTHER_COLOR)
  assert.equal(categoryColor(null), OTHER_COLOR)
  assert.equal(categoryColor('legacy'), OTHER_COLOR)

  // Utilities and unknown keys share the trend's "other" line, which sorts last.
  const folded = categoryTrend([
    e('u', 'm1', 30, 'utilities', '2026-09-10T12:00:00Z'),
    e('o', 'm1', 5, 'other', '2026-09-11T12:00:00Z'),
    e('r', 'm1', 400, 'rent', '2026-09-12T12:00:00Z'),
    e('f', 'm1', 2, 'food', '2026-09-13T12:00:00Z'),
  ])
  assert.deepEqual(folded.series, [{ key: 'food', values: [2] }, { key: 'rent', values: [400] }, { key: 'other', values: [35] }])

  // AddMonths(-n) clamps the day instead of overflowing.
  assert.equal(rangeCutoff('3m', new Date('2026-05-31T10:00:00Z')).toISOString(), '2026-02-28T10:00:00.000Z')
  assert.equal(rangeCutoff('all', new Date()), null)
  assert.deepEqual(filterByRange(expenses, '3m', new Date('2026-12-15T00:00:00Z')).map((x) => x.id), ['c', 'd'])

  const csv = expensesCsv(
    [e('x', 'm1', 1.5, 'food', '2026-09-10T12:00:00Z'), { ...e('y', 'm2', 2, null, '2026-09-11T12:00:00Z', true), description: 'Dinner, "late"' }],
    (id) => (id === 'm1' ? 'Ana' : 'Luis'),
    (key) => key.toUpperCase(),
  ).split('\n')
  assert.equal(csv[0], 'Date,Description,Category,Amount,Currency,Paid By,Settlement')
  assert.equal(csv[1], '2026-09-11,"Dinner, ""late""",OTHER,2.00,EUR,Luis,Yes')
  assert.equal(csv[2], '2026-09-10,x,FOOD,1.50,EUR,Ana,No')
})
test('balance summary keeps owed and owing apart, per currency, without netting', () => {
  const row = (group_id, other_member_id, balance) => ({ group_id, other_member_id, balance })
  const currency = { g1: 'EUR', g2: 'EUR', g3: 'USD' }
  const summary = summarizeBalances(
    [
      row('g1', 'marta', 30), row('g1', 'joan', 28), row('g1', 'lucia', -12),
      // Same person in another group stays on its own side: never netted against g1.
      row('g2', 'marta', -5.1), row('g2', 'pau', 0.004),
      row('g3', 'alex', 20.1), row('g3', 'alex', 0.2),
      row('unknown', 'zoe', 99),
    ],
    (id) => currency[id],
  )
  assert.deepEqual(summary.owedToMe.totals, [{ currency: 'EUR', cents: 5800 }, { currency: 'USD', cents: 2030 }])
  assert.deepEqual(summary.owedToMe.memberIds, ['marta', 'joan', 'alex'])
  assert.deepEqual(summary.owedToMe.groupIds, ['g1', 'g3'])
  assert.deepEqual(summary.iOwe.totals, [{ currency: 'EUR', cents: 1710 }])
  assert.deepEqual(summary.iOwe.memberIds, ['lucia', 'marta'])
  assert.equal(isSettled(summary), false)
  // Sub-cent residue and unknown groups are ignored; float noise doesn't leak into cents.
  assert.equal(isSettled(summarizeBalances([row('g1', 'pau', 0.004), row('g1', 'pau', -0.004)], (id) => currency[id])), true)
  assert.deepEqual(summarizeBalances([row('g1', 'a', 0.1), row('g1', 'b', 0.2)], (id) => currency[id]).owedToMe.totals, [{ currency: 'EUR', cents: 30 }])
})
test('expense amounts follow FormatExpenseAmount in both display modes', () => {
  const same = { amount: 20, currency: 'EUR', amount_in_group_currency: 20 }
  const foreign = { amount: 17.1, currency: 'GBP', amount_in_group_currency: 20 }
  assert.deepEqual(formatExpenseAmount(same, 'EUR', 'en', true), { primary: '€20.00', secondary: '' })
  assert.deepEqual(formatExpenseAmount(same, 'EUR', 'en', false), { primary: '€20.00', secondary: '' })
  assert.deepEqual(formatExpenseAmount(foreign, 'EUR', 'en', true), { primary: '€20.00', secondary: '(£17.10)' })
  assert.deepEqual(formatExpenseAmount(foreign, 'EUR', 'en', false), { primary: '£17.10', secondary: '(≈€20.00)' })
})
test('pair breakdown lines add up to the pairwise balance, like PairBreakdown.cs', () => {
  const exp = (id, paidBy, settlement, day) => ({
    id, paid_by_member_id: paidBy, is_settlement: settlement, description: id, category: 'food',
    occurred_at: `2026-09-${day}T12:00:00Z`,
  })
  const expenses = [exp('dinner', 'me', false, 19), exp('taxi', 'them', false, 20), exp('unrelated', 'other', false, 21), exp('payback', 'them', true, 22)]
  const shares = [
    { expense_id: 'dinner', member_id: 'me', share_amount_in_group_currency: 8 },
    { expense_id: 'dinner', member_id: 'them', share_amount_in_group_currency: 16 },
    { expense_id: 'dinner', member_id: 'other', share_amount_in_group_currency: 8 },
    { expense_id: 'taxi', member_id: 'me', share_amount_in_group_currency: 5 },
    { expense_id: 'taxi', member_id: 'them', share_amount_in_group_currency: 5 },
    { expense_id: 'unrelated', member_id: 'me', share_amount_in_group_currency: 7 },
    { expense_id: 'payback', member_id: 'me', share_amount_in_group_currency: 11 },
  ]
  const pair = pairBreakdown(expenses, shares, 'me', 'them', 'EUR')
  assert.equal(pair.paidForThem, 16)
  assert.equal(pair.paidForMe, 5)
  assert.equal(pair.settledToMe, 11)
  assert.equal(pair.settlementsNet, -11)
  assert.equal(pair.balance, 0)
  assert.deepEqual(pair.expenses.map((l) => [l.expenseId, l.iPaid, l.amount]), [['taxi', false, 5], ['dinner', true, 16]])

  // Cent arithmetic: 0.1 + 0.2 must not leave float drift.
  const drift = pairBreakdown(
    [exp('a', 'me', false, 1), exp('b', 'me', false, 2), exp('c', 'them', true, 3)],
    [{ expense_id: 'a', member_id: 'them', share_amount_in_group_currency: 0.1 }, { expense_id: 'b', member_id: 'them', share_amount_in_group_currency: 0.2 }, { expense_id: 'c', member_id: 'me', share_amount_in_group_currency: 0.3 }],
    'me', 'them', 'EUR')
  assert.equal(drift.balance, 0)
})


test('calendar dates reject impossible days, invalid years and incomplete input', () => {
  for (const value of ['', '2026-02-31', '2026-02-29', '1900-02-29', '2026-04-31',
    '2026-13-01', '2026-00-01', '2026-01-00', '2026-01-32', '0000-01-01',
    '2026-1-01', '2026-01-1', '2026-01-', '2026-01-01extra']) {
    assert.equal(isCalendarDate(value), false, value)
    assert.throws(() => expenseTimestamp(value, null), RangeError, value)
    assert.throws(() => localDateTimeToIso(value, '12:00'), RangeError, value)
  }
  for (const value of ['2028-02-29', '2000-02-29', '2026-04-30', '2026-12-31', '0001-01-01', '0099-01-01']) {
    assert.equal(isCalendarDate(value), true, value)
    assert.equal(localDateInput(expenseTimestamp(value, null)), value)
    assert.equal(localDateInput(localDateTimeToIso(value, '12:30')), value)
  }
  assert.throws(() => expenseTimestamp('2026-02-31', '2026-03-03T00:00:00Z'), RangeError)
})

test('event date-time conversion rejects invalid times and preserves valid local input', () => {
  for (const time of ['', '24:00', '12:60', '9:00', '12:00junk'])
    assert.throws(() => localDateTimeToIso('2026-10-28', time), RangeError, time)
  const result = new Date(localDateTimeToIso('2028-02-29', '23:15'))
  assert.equal(result.getFullYear(), 2028)
  assert.equal(result.getMonth(), 1)
  assert.equal(result.getDate(), 29)
  assert.equal(result.getHours(), 23)
  assert.equal(result.getMinutes(), 15)
})

test('calendar validation is stable across time zones and rejects skipped local event times', () => {
  for (const zone of ['Europe/Madrid', 'America/Los_Angeles', 'Asia/Tokyo']) {
    const result = execFileSync(process.execPath, ['--experimental-strip-types', '--input-type=module', '-e', `
      import { isCalendarDate, localDateTimeToIso } from './src/lib/dates.ts';
      let rejected = false;
      try { localDateTimeToIso('2026-03-29', '02:30'); } catch { rejected = true; }
      console.log(JSON.stringify({ leap: isCalendarDate('2028-02-29'), invalid: isCalendarDate('2026-02-31'), rejected }));
    `], { cwd: new URL('..', import.meta.url), env: { ...process.env, TZ: zone }, encoding: 'utf8' })
    assert.deepEqual(JSON.parse(result), { leap: true, invalid: false, rejected: zone === 'Europe/Madrid' })
  }
})


test('saved split intent is explicit and legacy/manual shares are never inferred as Equal', () => {
  assert.equal(restoredSplitMode('equal'), 'equal')
  for (const mode of ['manual', null, undefined, '', 'unknown'])
    assert.equal(restoredSplitMode(mode), 'manual')
  const saved = { a: 333, b: 334, c: 333 }
  assert.deepEqual(editableEqualSplitCents(1000, ['a', 'b', 'c'], saved), saved)
  assert.deepEqual(editableEqualSplitCents(1000, ['c', 'a', 'b'], saved), saved)
  assert.deepEqual(editableEqualSplitCents(1200, ['a', 'b', 'c'], saved), { a: 400, b: 400, c: 400 })
  assert.deepEqual(editableEqualSplitCents(1000, ['a', 'b'], saved), { a: 500, b: 500 })
  assert.deepEqual(editableEqualSplitCents(1000, ['a', 'b', 'd'], saved), { a: 334, b: 333, d: 333 })
  assert.deepEqual(editableEqualSplitCents(1000, ['a', 'b', 'c'], null), { a: 334, b: 333, c: 333 })
  assert.deepEqual(editableEqualSplitCents(1000, ['a', 'b'], { a: 900, b: 100 }), { a: 500, b: 500 })
})


test('unsaved expense snapshots compare persisted intent and ignore display-only changes', () => {
  const form = { amount: '12.00', description: 'Dinner', currency: 'EUR', category: 'food', paidBy: 'me',
    includedIds: ['me', 'alex'], manual: false, shareInputs: {}, shareCents: { me: 600, alex: 600 },
    recurring: false, occurredOn: '2026-10-28', startDate: '2026-10-28', frequency: 'monthly', receiptPath: null, eventId: null }
  const baseline = expenseEditorSnapshot(form)
  assert.equal(expenseEditorSnapshot({ ...form, amount: '12,00', includedIds: ['alex', 'me'], frequency: 'weekly' }), baseline)
  for (const change of [{ description: 'Changed' }, { amount: '13' }, { amount: '12,' }, { currency: 'USD' },
    { category: 'other' }, { paidBy: 'alex' }, { includedIds: ['me'] }, { manual: true },
    { occurredOn: '' }, { receiptPath: 'new.webp' }, { eventId: 'event' }, { recurring: true }])
    assert.notEqual(expenseEditorSnapshot({ ...form, ...change }), baseline, JSON.stringify(change))
  const manual = { ...form, manual: true, shareInputs: { me: '6.00', alex: '6,00' } }
  assert.equal(expenseEditorSnapshot({ ...manual, shareInputs: { me: '6,00', alex: '6.00' } }), expenseEditorSnapshot(manual))
  assert.notEqual(expenseEditorSnapshot({ ...manual, shareInputs: { me: '7', alex: '5' } }), expenseEditorSnapshot(manual))
  const recurring = { ...form, recurring: true }
  assert.notEqual(expenseEditorSnapshot({ ...recurring, frequency: 'weekly' }), expenseEditorSnapshot(recurring))
  assert.notEqual(expenseEditorSnapshot({ ...recurring, startDate: '' }), expenseEditorSnapshot(recurring))
  assert.equal(expenseEditorSnapshot({ ...form, description: 'Changed', amount: '13', ...form }), baseline)
})

test('invite reuse mirrors the Android rule and Enter never selects a phantom match', async () => {
  const { pickReusableInvite, canAddByName } = await import('../src/lib/inviteReuse.ts')
  const now = Date.parse('2026-10-30T12:00:00Z')
  const live = { token: 'a', expires_at: '2026-10-31T00:00:00Z', max_uses: 1, use_count: 0 }
  const used = { ...live, token: 'u', use_count: 1, expires_at: '2026-12-01T00:00:00Z' }
  const expired = { ...live, token: 'e', expires_at: '2026-10-30T11:59:59Z' }
  assert.equal(pickReusableInvite([live], now)?.token, 'a')
  assert.equal(pickReusableInvite([used, expired, live], now)?.token, 'a')
  assert.equal(pickReusableInvite([used, expired], now), undefined)
  assert.equal(pickReusableInvite(undefined, now), undefined)
  assert.equal(canAddByName('Ana', []), true)
  assert.equal(canAddByName(' A ', []), false)
  assert.equal(canAddByName('Ana', [{ account_id: 'x' }]), true)
  assert.equal(canAddByName('Ana', [{ account_id: null }]), false)
})

test('invite preview parsing and redeem error mapping cover every server status', async () => {
  const { parseInvitePreview, redeemErrorStatus, isJoinable, inviteErrorKey, looksLikeInviteCode } =
    await import('../src/lib/invitePreview.ts')
  assert.equal(parseInvitePreview({ status: 'ok', group_name: 'G' })?.status, 'ok')
  assert.equal(parseInvitePreview({ status: 'surprise' }), null)
  assert.equal(parseInvitePreview(null), null)
  assert.equal(isJoinable('ok'), true)
  assert.equal(isJoinable('already_member'), true)
  assert.equal(isJoinable('expired'), false)
  const cases = {
    'Invalid invite code': 'invalid',
    'Invite expired': 'expired',
    'Invite already used': 'used',
    'This invite is no longer valid — ask a current group member for a new one': 'inviter_gone',
    'This invite has already been claimed': 'claimed',
    "You're already in a group with this person, so this invite isn't for you": 'shared_group',
  }
  for (const [message, status] of Object.entries(cases)) {
    assert.equal(redeemErrorStatus(message), status)
    for (const lang of ['en', 'es']) assert.ok(strings[lang][inviteErrorKey(status)], `${lang} ${status}`)
  }
  assert.equal(redeemErrorStatus('Not signed in'), null)
  assert.equal(looksLikeInviteCode('XhLXnvGa9d5Y'), true)
  assert.equal(looksLikeInviteCode('abc'), false)
  assert.equal(looksLikeInviteCode('https://x'), false)
})

test('invite expiry picker snaps to the closest preset like Android', async () => {
  const { closestExpiryIndex, EXPIRY_HOURS } = await import('../src/lib/inviteReuse.ts')
  const hour = 3_600_000
  assert.deepEqual(EXPIRY_HOURS, [1, 24, 168, 720])
  assert.equal(closestExpiryIndex(30 * 60_000), 0)
  assert.equal(closestExpiryIndex(20 * hour), 1)
  assert.equal(closestExpiryIndex(7 * 24 * hour - 5 * 60_000), 2)
  assert.equal(closestExpiryIndex(25 * 24 * hour), 3)
  assert.equal(closestExpiryIndex(-hour), 0)
})

test('minor units table matches the SQL function and drives parsing, splits, balances and formatting', async () => {
  const { minorUnits, toMinor, fromMinor, minorText, amountText, ZERO_DECIMAL_CURRENCIES } = await import('../src/lib/currencyUnits.ts')
  const { formatMoney } = await import('../src/lib/formatting.ts')
  // Drift guard: the list in supabase/zero_decimal_currencies.sql is the source of truth.
  const sql = readFileSync(new URL('../../supabase/zero_decimal_currencies.sql', import.meta.url), 'utf8')
  const listed = /p_currency in \(([^)]*)\)/.exec(sql)[1].match(/'([A-Z]{3})'/g).map((c) => c.slice(1, 4)).sort()
  assert.deepEqual([...ZERO_DECIMAL_CURRENCIES].sort(), listed)
  for (const code of ZERO_DECIMAL_CURRENCIES) assert.equal(minorUnits(code), 0)
  for (const code of ['EUR', 'USD', 'HUF', 'IDR', 'GBP']) assert.equal(minorUnits(code), 2)
  assert.equal(toMinor('1500', 'JPY'), 1500)
  assert.equal(toMinor(0.1 + 0.2, 'EUR'), 30)
  assert.equal(fromMinor(1500, 'JPY'), 1500)
  assert.equal(minorText(1500, 'JPY'), '1500')
  assert.equal(minorText(1550, 'EUR'), '15.50')
  assert.equal(amountText('1500.00', 'JPY'), '1500')
  assert.equal(formatMoney(1500, 'JPY', 'en').replace(/\s/g, ''), '¥1,500')
  assert.equal(formatMoney(1500, 'EUR', 'en'), '€1,500.00')
  // Equal splits are whole units for yen: 1000 / 3 -> 334 + 333 + 333.
  assert.deepEqual(Object.values(equalSplitCents(1000, ['a', 'b', 'c'])), [334, 333, 333])
  // Pairwise balances are summed in the group currency's units.
  const rows = [{ group_id: 'g', other_member_id: 'x', balance: 1234 }, { group_id: 'g', other_member_id: 'y', balance: -500 }]
  const summary = summarizeBalances(rows, () => 'JPY')
  assert.deepEqual(summary.owedToMe.totals, [{ currency: 'JPY', cents: 1234 }])
  assert.deepEqual(summary.iOwe.totals, [{ currency: 'JPY', cents: 500 }])
  const yen = pairBreakdown([{ id: 'a', paid_by_member_id: 'me', is_settlement: false, description: '', category: '', occurred_at: '2026-10-01' }],
    [{ expense_id: 'a', member_id: 'them', share_amount_in_group_currency: 750 }], 'me', 'them', 'JPY')
  assert.equal(yen.balance, 750)
  const payload = settlementPayload({ payerId: 'me', recipientId: 'them', amount: '1500', currency: 'JPY', note: '', date: '2026-10-27' }, 'g', 'ts')
  assert.equal(payload.p_expense.amount, 1500)
  assert.equal(payload.p_shares[0].share_amount, 1500)
  assert.equal(settlementEditorSnapshot({ payerId: 'me', recipientId: 'them', amount: '1500', currency: 'JPY', note: '', date: 'd' }), settlementEditorSnapshot({ payerId: 'me', recipientId: 'them', amount: ' 1500 ', currency: 'JPY', note: '', date: 'd' }))
})

test('category keys and icons match Android AppConstants.Categories', () => {
  const cs = readFileSync(new URL('../../AxisApp/AppConstants.cs', import.meta.url), 'utf8')
  const keys = /class Categories[\s\S]*?Keys =\s*\[([^\]]*)\]/.exec(cs)[1].match(/"([a-z]+)"/g).map((k) => k.slice(1, -1))
  assert.deepEqual([...CATEGORY_KEYS], keys)
  assert.equal(keys.at(-1), 'other')

  // Resolve each Glyph() switch arm to its constant's codepoint (raw PUA character or \u escape).
  const glyphBody = /public static string Glyph\([\s\S]*?\n {8}\}/.exec(cs)[0]
  const codepoint = (constName) => {
    const value = new RegExp(`const string ${constName} = "([^"]+)"`).exec(cs)[1]
    return value.startsWith('\\u') ? parseInt(value.slice(2), 16) : value.codePointAt(0)
  }
  for (const key of keys) {
    const arm = new RegExp(`"${key}" => \\w+\\.(\\w+)`).exec(glyphBody)
    const constName = arm ? arm[1] : /_ => \w+\.(\w+)/.exec(glyphBody)[1]
    assert.equal(iconGlyphs[categoryIcon(key)].codePointAt(0), codepoint(constName), key)
  }
})

test('activity dates compare local calendar days, like Android FormatRelative', () => {
  const labels = { today: 'Today', yesterday: 'Yesterday' }
  const now = new Date(2026, 9, 30, 0, 30)
  // Stored as a midnight timestamp: an hour ago is still "Today", never "1h ago".
  assert.equal(activityDate(new Date(2026, 9, 30).toISOString(), 'en', labels, now), 'Today')
  assert.equal(activityDate(new Date(2026, 9, 29).toISOString(), 'en', labels, now), 'Yesterday')
  // Yesterday across a month and year boundary.
  assert.equal(activityDate(new Date(2025, 11, 31).toISOString(), 'en', labels, new Date(2026, 0, 1, 9)), 'Yesterday')
  assert.equal(activityDate(new Date(2026, 9, 28).toISOString(), 'en', labels, now), 'Oct 28')
  assert.equal(activityDate(new Date(2026, 9, 28).toISOString(), 'es', labels, now), '28 oct')
  assert.equal(activityDate(new Date(2025, 9, 28).toISOString(), 'en', labels, now), 'Oct 28, 2025')
  assert.equal(activityDate(new Date(2025, 9, 28).toISOString(), 'es', labels, now), '28 oct 2025')
})
