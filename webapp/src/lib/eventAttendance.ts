import type { EventAttendee, RsvpResponse } from './types'

export function respondToEvent(current: EventAttendee, response: RsvpResponse): EventAttendee {
  const clearTransport =
    response === 'not_going' || (response === 'maybe' && current.car_status === 'offering')
  return {
    ...current,
    response,
    car_status: clearTransport ? 'none' : current.car_status,
    car_offered_seats:
      !clearTransport && current.car_status === 'offering' ? current.car_offered_seats : null,
  }
}

export function toggleEventTransport(
  current: EventAttendee,
  status: 'offering' | 'needs_ride',
  defaultSeats: number,
): EventAttendee {
  if (current.response === 'not_going' || (status === 'offering' && current.response !== 'going'))
    return current
  const next = current.car_status === status ? 'none' : status
  return {
    ...current,
    car_status: next,
    car_offered_seats: next === 'offering' ? Math.max(0, defaultSeats) : null,
  }
}
