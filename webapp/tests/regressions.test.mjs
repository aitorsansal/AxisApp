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
import { categoryColor, categoryFrequency, categoryTrend, expensesCsv, filterByRange, rangeCutoff, settlementsByMonth, spendByCategory, spendByMember } from '../src/lib/stats.ts'
import { expenseTimestamp, isCalendarDate, localDateInput, localDateTimeToIso, formatEventRange } from '../src/lib/dates.ts'
import { isSettlementDate, settlementCents, settlementParties, settlementPayload } from '../src/lib/settlements.ts'
import { strings } from '../src/lib/i18n/strings.ts'

test('expense, share and settlement amounts follow the same cross-client input contract', () => {
  const cases = JSON.parse(readFileSync(new URL('../../tests/amount-input-cases.json', import.meta.url), 'utf8'))
  for (const sample of cases) {
    const parsed = parseAmountInput(sample.text)
    assert.equal(parsed.cents, sample.cents, JSON.stringify(sample.text))
    assert.equal(parsed.error, sample.error ?? null, JSON.stringify(sample.text))
    assert.equal(amountCents(sample.text), sample.cents)
    assert.equal(settlementCents(sample.text), sample.cents)
  }
  const total = amountCents('8,50')
  assert.equal(amountCents('4.25') + amountCents('4,25'), total)
  assert.equal(amountCents('4.251'), null)
  assert.equal(amountCents('4,25.0'), null)
})

test('settlements accept exact positive cents and reject ambiguous or overflowing amounts', () => {
  for (const [input, cents] of [['8.50', 850], ['8,50', 850], [' 0,01 ', 1], ['12', 1200], ['9999999999.99', 999999999999]]) {
    assert.equal(settlementCents(input), cents)
  }
  for (const invalid of ['', ' ', '0', '-1', '1.001', '1,234.56', '1.234,56', '1e3', 'Infinity', '10000000000']) {
    assert.equal(settlementCents(invalid), null, invalid)
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
      const payload = settlementPayload({ ...parties, amount, currency: 'EUR', note: '', date: '2026-10-27' }, 'group', 'Settlement with Alex', '2026-10-27T00:00:00Z')
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

test('settlements persist the displayed default or a custom note and retain edit metadata', () => {
  const draft = { id: 'saved', payerId: 'me', recipientId: 'alex', amount: '4.20', currency: 'EUR', date: '2026-10-27', note: '   ', receiptPath: 'receipt.webp', eventId: 'event' }
  for (const language of ['en', 'es']) {
    const fallback = strings[language].Settlement_DefaultDescription.replace('{0}', 'Alex')
    const payload = settlementPayload(draft, 'group', fallback, 'original-timestamp')
    assert.equal(payload.p_expense.description, language === 'en' ? 'Settlement with Alex' : 'Saldar deuda con Alex')
    assert.equal(payload.p_expense.id, 'saved')
    assert.equal(payload.p_expense.receipt_path, 'receipt.webp')
    assert.equal(payload.p_expense.event_id, 'event')
    assert.equal(payload.p_expense.occurred_at, 'original-timestamp')
    assert.equal(payload.p_expense.is_settlement, true)
    assert.equal(payload.p_expense.created_at, undefined)
    assert.equal(payload.p_expense.created_by, undefined)
  }
  assert.equal(settlementPayload({ ...draft, note: '  Bank transfer  ' }, 'group', 'fallback', 'timestamp').p_expense.description, 'Bank transfer')
  assert.throws(() => settlementPayload({ ...draft, recipientId: 'me' }, 'group', 'fallback', 'timestamp'))
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

  // Colour follows the category's fixed slot; null category is "other"; legacy keys fall to slot 0.
  assert.equal(categoryColor('drinks'), '#D95926')
  assert.equal(categoryColor(null), '#E66767')
  assert.equal(categoryColor('rent'), '#3987E5')

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
  const pair = pairBreakdown(expenses, shares, 'me', 'them')
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
    'me', 'them')
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
