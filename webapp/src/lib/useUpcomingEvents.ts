import { useCallback, useEffect, useRef, useState } from 'react'
import { supabase } from './supabaseClient'
import { fetchAll } from './fetchAll'
import { RefreshRevision } from './refreshRevision'
import { SerializedUpdates } from './serializedUpdates'
import { useCoalescedWrites } from './useCoalescedWrites'
import { useLiveRefresh } from './useLiveRefresh'
import { respondToEvent, toggleEventTransport } from './eventAttendance'
import type { EventAttendee, EventRow, RsvpResponse } from './types'

export function useUpcomingEvents(groupIds: string[], memberId: string | undefined, limit: number) {
  const [events, setEvents] = useState<EventRow[] | null>(null)
  const [attendees, setAttendees] = useState<EventAttendee[]>([])
  const [hasMore, setHasMore] = useState(false)
  const [error, setError] = useState<'load' | 'save' | null>(null)
  const [pending, setPending] = useState<Set<string>>(new Set())
  const counts = useRef(new Map<string, number>())
  const defaultSeats = useRef(0)
  const revision = useRef(new RefreshRevision())
  const updates = useRef(new SerializedUpdates<EventAttendee>())
  const active = useRef(false)
  const groupKey = groupIds.join(',')

  const load = useCallback(
    async (writeRevision?: number) => {
      const canApply = revision.current.begin(writeRevision)
      const ids = groupKey.split(',').filter(Boolean)
      if (!ids.length) {
        if (canApply()) {
          setEvents([])
          setAttendees([])
          setError(null)
        }
        return
      }
      try {
        const result = await supabase
          .from('events')
          .select('*')
          .in('group_id', ids)
          .gte('starts_at', new Date().toISOString())
          .order('starts_at')
          .order('id')
          .limit(limit + 1)
        if (result.error) throw result.error
        if (!canApply()) return
        const rows = (result.data as EventRow[]).slice(0, limit)
        const [responses, profile] = await Promise.all([
          rows.length
            ? fetchAll<EventAttendee>((from, to) =>
                supabase
                  .from('event_attendees')
                  .select('event_id, member_id, response, car_status, car_offered_seats')
                  .in(
                    'event_id',
                    rows.map((e) => e.id),
                  )
                  .order('event_id')
                  .order('member_id')
                  .range(from, to),
              )
            : [],
          memberId
            ? supabase.from('members').select('car_extra_seats').eq('id', memberId).maybeSingle()
            : null,
        ])
        if (!canApply()) return
        defaultSeats.current = profile?.data?.car_extra_seats ?? 0
        setEvents(rows)
        setHasMore(result.data.length > limit)
        setAttendees(responses)
        setError((previous) => (previous === 'save' ? previous : null))
      } catch {
        if (canApply() && active.current) setError('load')
      }
    },
    [groupKey, memberId, limit],
  )

  useEffect(() => {
    const freshness = revision.current
    active.current = true
    void Promise.resolve().then(() => (active.current ? load() : undefined))
    return () => {
      active.current = false
      freshness.invalidate()
    }
  }, [load])

  const seatWrites = useCoalescedWrites<number>(400, (id, seats) => {
    queue(id, (current) =>
      current.car_status === 'offering' ? { ...current, car_offered_seats: seats } : current,
    )
  })

  useLiveRefresh(
    `overview-events-${memberId ?? 'loading'}`,
    [{ table: 'events' }, { table: 'event_attendees' }],
    () => load(),
    () => updates.current.hasPending || seatWrites.hasPending,
  )

  function mine(id: string) {
    return attendees.find((a) => a.event_id === id && a.member_id === memberId)
  }

  function queue(id: string, update: (row: EventAttendee) => EventAttendee) {
    if (!memberId) return
    seatWrites.flush(id)
    const writeRevision = revision.current.invalidate()
    const initial = mine(id) ?? {
      event_id: id,
      member_id: memberId,
      response: 'not_going',
      car_status: 'none',
      car_offered_seats: null,
    }
    counts.current.set(id, (counts.current.get(id) ?? 0) + 1)
    if (active.current) {
      setPending(new Set(counts.current.keys()))
      setError(null)
    }
    void updates.current
      .run(id, initial, update, async (next) => {
        const result = await supabase
          .from('event_attendees')
          .upsert(next, { onConflict: 'event_id,member_id' })
        if (result.error) throw result.error
        if (!active.current) return
        setAttendees((prev) => [...prev.filter((a) => a.event_id !== id || a.member_id !== memberId), next])
        await load(writeRevision)
      })
      .catch(() => {
        if (active.current) {
          setError('save')
          void load()
        }
      })
      .finally(() => {
        const count = (counts.current.get(id) ?? 1) - 1
        if (count) counts.current.set(id, count)
        else counts.current.delete(id)
        if (active.current) setPending(new Set(counts.current.keys()))
      })
  }

  function adjustSeats(id: string, delta: number) {
    const current = mine(id)
    if (current?.car_status !== 'offering') return
    const seats = Math.max(0, Math.min(99, (current.car_offered_seats ?? 0) + delta))
    revision.current.invalidate()
    setAttendees((prev) => prev.map((a) => (a === current ? { ...a, car_offered_seats: seats } : a)))
    seatWrites.set(id, seats)
  }

  return {
    events,
    attendees,
    hasMore,
    error,
    pending,
    load: () => {
      setError(null)
      return load()
    },
    mine,
    adjustSeats,
    respond: (id: string, response: RsvpResponse) => queue(id, (row) => respondToEvent(row, response)),
    transport: (id: string, status: 'offering' | 'needs_ride') =>
      queue(id, (row) => toggleEventTransport(row, status, defaultSeats.current)),
  }
}
