import { test } from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { stripTypeScriptTypes } from 'node:module'

// Exercise the single-file deployed source without importing its JSR client or starting Deno.
const source = readFileSync(new URL('../functions/calendar-feed/index.ts', import.meta.url), 'utf8')
const helpers = source.slice(source.indexOf('interface EventRow'), source.indexOf('Deno.serve('))
const compiled = stripTypeScriptTypes(helpers) + '\nexport { toIcsDate, eventToVevent };'
const { toIcsDate, eventToVevent } = await import('data:text/javascript;base64,' + Buffer.from(compiled).toString('base64'))
const ICAL = (await import(process.env.ICAL_MODULE || 'ical.js')).default
const event = {
  id: 'calendar-timestamp-qa', title: 'Calendar QA, café; meeting',
  description: 'First line\nSecond line', location: 'Room 1, Madrid',
  starts_at: '2026-10-28T12:30:45+02:00', ends_at: null,
}
function imported(row) {
  const text = ['BEGIN:VCALENDAR', 'VERSION:2.0', 'PRODID:-//Axis//Calendar Feed//EN',
    eventToVevent(row), 'END:VCALENDAR', ''].join('\r\n')
  const calendar = new ICAL.Component(ICAL.parse(text))
  return { text, event: new ICAL.Event(calendar.getFirstSubcomponent('vevent')) }
}

test('UTC timestamps contain one Z, with and without fractional seconds', () => {
  for (const value of ['2026-10-28T10:30:45Z', '2026-10-28T10:30:45.123Z', '2026-10-28T10:30:45+00:00', '2026-10-28T10:30:45.123456+00:00'])
    assert.equal(toIcsDate(value), '20261028T103045Z', value)
})
test('positive and negative offsets preserve the instant, not the original wall clock', () => {
  for (const value of ['2026-10-28T12:30:45+02:00', '2026-10-28T12:30:45.123456+02:00', '2026-10-28T06:30:45-04:00', '2026-10-28T16:00:45+05:30'])
    assert.equal(toIcsDate(value), '20261028T103045Z', value)
})
test('offset conversion preserves day and year rollover', () => {
  assert.equal(toIcsDate('2026-01-01T00:30:00+02:00'), '20251231T223000Z')
  assert.equal(toIcsDate('2026-12-31T23:30:00-02:00'), '20270101T013000Z')
})
test('explicit start and end import with their correct UTC times', () => {
  const result = imported({ ...event, ends_at: '2026-10-28T13:45:00+02:00' })
  assert.equal(result.event.startDate.toJSDate().toISOString(), '2026-10-28T10:30:45.000Z')
  assert.equal(result.event.endDate.toJSDate().toISOString(), '2026-10-28T11:45:00.000Z')
  for (const line of result.text.split('\r\n').filter(s => /^(DTSTART|DTEND|DTSTAMP):/.test(s)))
    assert.match(line, /^DT(?:START|END|STAMP):\d{8}T\d{6}Z$/)
})
test('fallback end is one elapsed hour across midnight and DST transitions', () => {
  for (const starts_at of ['2026-12-31T23:30:00-02:00', '2026-03-29T01:30:00+01:00', '2026-10-25T02:30:00+02:00']) {
    const result = imported({ ...event, starts_at })
    assert.equal(result.event.startDate.toJSDate().getTime(), new Date(starts_at).getTime())
    assert.equal(result.event.endDate.toJSDate().getTime() - result.event.startDate.toJSDate().getTime(), 3600000)
  }
})
test('calendar import preserves event identity, escaped text and reminder', () => {
  const result = imported(event)
  assert.equal(result.event.uid, 'event-calendar-timestamp-qa@axisapp.aitorsansal.com')
  assert.equal(result.event.summary, event.title)
  assert.equal(result.event.description, event.description)
  assert.equal(result.event.location, event.location)
  assert.ok(result.text.includes('TRIGGER:-P4D'))
})
test('invalid timestamps fail explicitly instead of emitting broken calendar text', () => {
  assert.throws(() => toIcsDate('not-a-timestamp'), RangeError)
})
