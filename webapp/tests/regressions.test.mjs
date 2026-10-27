import { test } from 'node:test'
import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { equalSplitCents } from '../src/lib/expenseMath.ts'
import { SerializedUpdates } from '../src/lib/serializedUpdates.ts'
import { CoalescedWrites } from '../src/lib/coalescedWrites.ts'
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
