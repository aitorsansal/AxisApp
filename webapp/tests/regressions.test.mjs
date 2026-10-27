import { test } from 'node:test'
import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { equalSplitCents } from '../src/lib/expenseMath.ts'
import { SerializedUpdates } from '../src/lib/serializedUpdates.ts'
import { CoalescedWrites } from '../src/lib/coalescedWrites.ts'
import { nextDue } from '../src/lib/recurringSchedule.ts'
import { categoryColor, categoryFrequency, categoryTrend, expensesCsv, filterByRange, rangeCutoff, settlementsByMonth, spendByCategory, spendByMember } from '../src/lib/stats.ts'
import { formatEventRange } from '../src/lib/dates.ts'

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
