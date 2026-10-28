import { useCallback, useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { SerializedUpdates } from '../lib/serializedUpdates'
import { useCoalescedWrites } from '../lib/useCoalescedWrites'
import { useLiveRefresh } from '../lib/useLiveRefresh'
import { formatEventRange } from '../lib/dates'
import { useAuth } from '../context/AuthContext'
import { useAliases } from '../context/AliasesContext'
import { useLocale } from '../context/LocaleContext'
import type { CarStatus, EventAttendee, EventRow, MemberRow, RsvpResponse } from '../lib/types'
import { Avatar } from './Avatar'
import './GroupEventsTab.css'

interface Props {
  groupId: string
  members: MemberRow[]
}

export function GroupEventsTab({ groupId, members }: Props) {
  const { session } = useAuth()
  const { initials, avatarUrl } = useAliases()
  const { t, language } = useLocale()

  const [events, setEvents] = useState<EventRow[]>([])
  const [attendeesByEvent, setAttendeesByEvent] = useState<Record<string, EventAttendee[]>>({})
  const [isPastSelected, setIsPastSelected] = useState(false)
  const [myCarExtraSeats, setMyCarExtraSeats] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loaded, setLoaded] = useState(false)
  const rsvpUpdates = useRef(new SerializedUpdates<EventAttendee>())

  const myMemberId = members.find((m) => m.members.account_id === session?.user.id)?.member_id

  const load = useCallback(async () => {
    setError(null)
    const [eventsRes, myMemberRes] = await Promise.all([
      supabase
        .from('events')
        .select('*')
        .eq('group_id', groupId)
        .order('starts_at', { ascending: true }),
      session
        ? supabase
            .from('members')
            .select('car_extra_seats')
            .eq('account_id', session.user.id)
            .limit(1)
            .maybeSingle()
        : Promise.resolve({ data: null, error: null }),
    ])
    if (eventsRes.error) return setError(eventsRes.error.message)
    const eventRows = (eventsRes.data as EventRow[]) ?? []
    setEvents(eventRows)
    setMyCarExtraSeats(
      (myMemberRes.data as { car_extra_seats: number | null } | null)?.car_extra_seats ?? null,
    )

    if (eventRows.length > 0) {
      const { data: attendees, error: attendeesError } = await supabase
        .from('event_attendees')
        .select('event_id, member_id, response, car_status, car_offered_seats')
        .in(
          'event_id',
          eventRows.map((e) => e.id),
        )
      if (attendeesError) return setError(attendeesError.message)
      const grouped: Record<string, EventAttendee[]> = {}
      for (const row of (attendees as EventAttendee[]) ?? []) {
        ;(grouped[row.event_id] ??= []).push(row)
      }
      setAttendeesByEvent(grouped)
    } else {
      setAttendeesByEvent({})
    }
    setLoaded(true)
  }, [groupId, session])

  useEffect(() => {
    load()
  }, [load])

  useLiveRefresh(
    `group-events-${groupId}`,
    [{ table: 'events', filter: `group_id=eq.${groupId}` }, { table: 'event_attendees' }],
    load,
    () => rsvpUpdates.current.hasPending || seatWrites.hasPending,
  )

  function mine(eventId: string): EventAttendee | undefined {
    return attendeesByEvent[eventId]?.find((a) => a.member_id === myMemberId)
  }

  const seatWrites = useCoalescedWrites<number>(400, (eventId, seats) =>
    queueRsvp(eventId, (current) => current.car_status !== 'offering' ? current : { ...current, car_offered_seats: seats }))

  function queueRsvp(eventId: string, update: (current: EventAttendee) => EventAttendee) {
    if (!myMemberId) return
    // Pending seat taps go first so a later toggle composes on top of them.
    seatWrites.flush(eventId)
    const initial = mine(eventId) ?? {
      event_id: eventId, member_id: myMemberId, response: 'not_going' as const,
      car_status: 'none' as const, car_offered_seats: null,
    }
    void rsvpUpdates.current.run(eventId, initial, update, async (next) => {
      const { error } = await supabase.from('event_attendees').upsert(next, { onConflict: 'event_id,member_id' })
      if (error) throw error
      await load()
    }).catch((err) => setError(err.message ?? t('Common_Error')))
  }

  function setRsvp(eventId: string, response: RsvpResponse) {
    queueRsvp(eventId, (current) => {
      const carStatus: CarStatus = response === 'not_going' || (response === 'maybe' && current.car_status === 'offering')
        ? 'none' : current.car_status
      return { ...current, response, car_status: carStatus, car_offered_seats: carStatus === 'offering' ? current.car_offered_seats : null }
    })
  }

  function toggleCarOffering(eventId: string) {
    queueRsvp(eventId, (current) => {
      if (current.response !== 'going') return current
      const offering = current.car_status !== 'offering'
      return { ...current, car_status: offering ? 'offering' : 'none', car_offered_seats: offering ? (myCarExtraSeats ?? 0) : null }
    })
  }

  function toggleCarNeedsRide(eventId: string) {
    queueRsvp(eventId, (current) => {
      if (current.response === 'not_going') return current
      return { ...current, car_status: current.car_status === 'needs_ride' ? 'none' : 'needs_ride', car_offered_seats: null }
    })
  }

  function adjustSeats(eventId: string, delta: number) {
    const current = mine(eventId)
    if (current?.car_status !== 'offering') return
    const seats = Math.max(0, (current.car_offered_seats ?? 0) + delta)
    setAttendeesByEvent((prev) => ({
      ...prev,
      [eventId]: (prev[eventId] ?? []).map((a) => a.member_id === myMemberId ? { ...a, car_offered_seats: seats } : a),
    }))
    seatWrites.set(eventId, seats)
  }

  if (!loaded) {
    return error ? (
      <div>
        <p className="error-text" role="alert">
          {error}
        </p>
        <button type="button" className="btn btn-outline" onClick={() => void load()}>
          {t('Common_Retry')}
        </button>
      </div>
    ) : (
      <div className="spinner" role="status">
        {t('Common_Loading')}
      </div>
    )
  }

  const now = new Date()
  const relevant = events
    .filter((e) => (isPastSelected ? new Date(e.starts_at) < now : new Date(e.starts_at) >= now))
    .sort((a, b) =>
      isPastSelected
        ? +new Date(b.starts_at) - +new Date(a.starts_at)
        : +new Date(a.starts_at) - +new Date(b.starts_at),
    )

  const dateLocale = language === 'es' ? 'es-ES' : 'en-US'
  const monthGroups: { header: string; events: EventRow[] }[] = []
  for (const ev of relevant) {
    const header = new Date(ev.starts_at).toLocaleDateString(dateLocale, {
      month: 'long',
      year: 'numeric',
    })
    const last = monthGroups[monthGroups.length - 1]
    if (last && last.header === header) last.events.push(ev)
    else monthGroups.push({ header, events: [ev] })
  }

  return (
    <div className="events-tab">
      {error && (
        <p className="error-text" role="alert">
          {error}
        </p>
      )}

      <div className="events-toggle">
        <button
          type="button"
          className={isPastSelected ? '' : 'active'}
          aria-pressed={!isPastSelected}
          onClick={() => setIsPastSelected(false)}
        >
          {t('GroupEvents_Upcoming')}
        </button>
        <button
          type="button"
          className={isPastSelected ? 'active' : ''}
          aria-pressed={isPastSelected}
          onClick={() => setIsPastSelected(true)}
        >
          {t('GroupEvents_Past')}
        </button>
      </div>

      {monthGroups.length === 0 && <p className="empty-state small">{t('GroupEvents_Empty')}</p>}

      {monthGroups.map((group) => (
        <div key={group.header} className="event-month-group">
          <h3 className="event-month-header">{group.header}</h3>
          <div className="event-list">
            {group.events.map((ev) => {
              const attendees = attendeesByEvent[ev.id] ?? []
              const myRow = attendees.find((a) => a.member_id === myMemberId)
              const myResponse = myRow?.response ?? ''
              const goingAttendees = attendees
                .filter((a) => a.response === 'going')
                .map((a) => members.find((m) => m.member_id === a.member_id)?.members)
                .filter((m): m is NonNullable<typeof m> => !!m)
              const seatsOffered = attendees
                .filter((a) => a.car_status === 'offering')
                .reduce((sum, a) => sum + (a.car_offered_seats ?? 0), 0)
              const ridersNeeded = attendees.filter((a) => a.car_status === 'needs_ride').length
              const showTransportControls = myResponse === 'going' || myResponse === 'maybe'
              const canOfferCar = myResponse === 'going'
              const subParts = [formatEventRange(ev.starts_at, ev.ends_at, language)]
              if (ev.location) subParts.push(ev.location)

              return (
                <div className="card event-row" key={ev.id}>
                  {ev.is_birthday ? (
                    <div className="event-header">
                      <div className="event-title">{ev.title}</div>
                      <div className="event-sub">{subParts.join(' · ')}</div>
                    </div>
                  ) : (
                    <Link to={`/groups/${groupId}/events/${ev.id}`} className="event-header">
                      <div className="event-title">{ev.title}</div>
                      <div className="event-sub">{subParts.join(' · ')}</div>
                    </Link>
                  )}

                  {goingAttendees.length > 0 && (
                    <div className="event-avatars">
                      {goingAttendees.map((m) => (
                        <Avatar key={m.id} url={avatarUrl(m)} initials={initials(m)} seed={m.id} size={26} />
                      ))}
                    </div>
                  )}

                  {!ev.is_birthday && (
                    <div className="rsvp-pill">
                      <button
                        type="button"
                        className={myResponse === 'going' ? 'active' : ''}
                        aria-pressed={myResponse === 'going'}
                        onClick={() => setRsvp(ev.id, 'going')}
                      >
                        {t('GroupEvents_Going')}
                      </button>
                      <button
                        type="button"
                        className={myResponse === 'maybe' ? 'active' : ''}
                        aria-pressed={myResponse === 'maybe'}
                        onClick={() => setRsvp(ev.id, 'maybe')}
                      >
                        {t('GroupEvents_Maybe')}
                      </button>
                      <button
                        type="button"
                        className={myResponse === 'not_going' ? 'active' : ''}
                        aria-pressed={myResponse === 'not_going'}
                        onClick={() => setRsvp(ev.id, 'not_going')}
                      >
                        {t('GroupEvents_NotGoing')}
                      </button>
                    </div>
                  )}

                  {ev.needs_transport && !ev.is_birthday && (
                    <div className="transport-section">
                      <div
                        className={`transport-summary ${seatsOffered < ridersNeeded ? 'shortfall' : ''}`}
                      >
                        {t('GroupEvents_TransportSummary', seatsOffered, ridersNeeded)}
                      </div>
                      {showTransportControls && (
                        <div className="transport-controls">
                          {canOfferCar && (
                            <button
                              type="button"
                              className={
                                myRow?.car_status === 'offering'
                                  ? 'btn btn-outline active'
                                  : 'btn btn-outline'
                              }
                              onClick={() => toggleCarOffering(ev.id)}
                            >
                              {t('GroupEvents_HaveCar')}
                            </button>
                          )}
                          <button
                            type="button"
                            className={
                              myRow?.car_status === 'needs_ride'
                                ? 'btn btn-outline active'
                                : 'btn btn-outline'
                            }
                            onClick={() => toggleCarNeedsRide(ev.id)}
                          >
                            {t('GroupEvents_NeedRide')}
                          </button>
                          {myRow?.car_status === 'offering' && (
                            <div className="seat-stepper">
                              <button
                                type="button"
                                aria-label={t('GroupEvents_FewerSeats')}
                                onClick={() => adjustSeats(ev.id, -1)}
                              >
                                −
                              </button>
                              <span>{myRow.car_offered_seats ?? 0}</span>
                              <button
                                type="button"
                                aria-label={t('GroupEvents_MoreSeats')}
                                onClick={() => adjustSeats(ev.id, 1)}
                              >
                                +
                              </button>
                            </div>
                          )}
                        </div>
                      )}
                    </div>
                  )}
                </div>
              )
            })}
          </div>
        </div>
      ))}
    </div>
  )
}
