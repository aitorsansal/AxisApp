import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { useAuth } from '../context/AuthContext'
import { useAliases } from '../context/AliasesContext'
import { useLocale } from '../context/LocaleContext'
import type { CarStatus, EventAttendee, EventRow, ExpenseWithPayer, Group, MemberRow, RsvpResponse } from '../lib/types'
import { AppHeader } from '../components/AppHeader'
import { Avatar } from '../components/Avatar'
import './EventDetailPage.css'

export function EventDetailPage() {
  const { groupId, eventId } = useParams<{ groupId: string; eventId: string }>()
  const { session } = useAuth()
  const { displayName, initials, avatarUrl } = useAliases()
  const { t, language } = useLocale()

  const [group, setGroup] = useState<Group | null>(null)
  const [event, setEvent] = useState<EventRow | null>(null)
  const [members, setMembers] = useState<MemberRow[]>([])
  const [attendees, setAttendees] = useState<EventAttendee[]>([])
  const [expenses, setExpenses] = useState<ExpenseWithPayer[]>([])
  const [expenseSearch, setExpenseSearch] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loaded, setLoaded] = useState(false)

  const myMemberId = members.find((m) => m.members.account_id === session?.user.id)?.member_id

  const load = useCallback(async () => {
    if (!groupId || !eventId) return
    setError(null)
    const [groupRes, eventRes, membersRes, attendeesRes, expensesRes] = await Promise.all([
      supabase.from('groups').select('id, name, currency, created_by, color, icon').eq('id', groupId).single(),
      supabase.from('events').select('*').eq('id', eventId).single(),
      supabase.from('group_members').select('member_id, members(id, display_name, account_id, avatar_path)').eq('group_id', groupId),
      supabase.from('event_attendees').select('event_id, member_id, response, car_status, car_offered_seats').eq('event_id', eventId),
      supabase
        .from('expenses')
        .select('id, group_id, paid_by_member_id, amount, currency, description, category, occurred_at, created_at, is_settlement, receipt_path, event_id')
        .eq('event_id', eventId)
        .order('occurred_at', { ascending: false }),
    ])

    if (groupRes.error) return setError(groupRes.error.message)
    if (eventRes.error) return setError(eventRes.error.message)
    if (membersRes.error) return setError(membersRes.error.message)
    if (attendeesRes.error) return setError(attendeesRes.error.message)
    if (expensesRes.error) return setError(expensesRes.error.message)

    setGroup(groupRes.data as Group)
    setEvent(eventRes.data as EventRow)
    setMembers((membersRes.data as unknown as MemberRow[]) ?? [])
    setAttendees((attendeesRes.data as EventAttendee[]) ?? [])
    setExpenses((expensesRes.data as unknown as ExpenseWithPayer[]) ?? [])
    setLoaded(true)
  }, [groupId, eventId])

  useEffect(() => {
    load()
  }, [load])

  async function upsertRsvp(response: RsvpResponse, carStatus: CarStatus, carOfferedSeats: number | null) {
    if (!myMemberId || !eventId) return
    const { error } = await supabase.from('event_attendees').upsert(
      { event_id: eventId, member_id: myMemberId, response, car_status: carStatus, car_offered_seats: carStatus === 'offering' ? carOfferedSeats : null },
      { onConflict: 'event_id,member_id' },
    )
    if (error) return setError(error.message)
    await load()
  }

  const mine = attendees.find((a) => a.member_id === myMemberId)
  const myResponse = mine?.response ?? ''

  function setRsvp(response: RsvpResponse) {
    const carStatus: CarStatus =
      response === 'not_going' ? 'none' : response === 'maybe' && mine?.car_status === 'offering' ? 'none' : (mine?.car_status ?? 'none')
    const seats = carStatus === 'offering' ? (mine?.car_offered_seats ?? 0) : null
    upsertRsvp(response, carStatus, seats)
  }

  function toggleCarOffering() {
    if (!mine || mine.response !== 'going') return
    const newStatus: CarStatus = mine.car_status === 'offering' ? 'none' : 'offering'
    upsertRsvp(mine.response, newStatus, newStatus === 'offering' ? 0 : null)
  }

  function toggleCarNeedsRide() {
    if (!mine || (mine.response !== 'going' && mine.response !== 'maybe')) return
    const newStatus: CarStatus = mine.car_status === 'needs_ride' ? 'none' : 'needs_ride'
    upsertRsvp(mine.response, newStatus, null)
  }

  function adjustSeats(delta: number) {
    if (!mine || mine.car_status !== 'offering') return
    upsertRsvp(mine.response, 'offering', Math.max(0, (mine.car_offered_seats ?? 0) + delta))
  }

  if (!loaded || !event || !group) {
    return (
      <div className="page">
        <AppHeader title={t('EventDetail_GenericTitle')} backTo={`/groups/${groupId}`} />
        {error ? <p className="error-text">{error}</p> : <div className="spinner">{t('Common_Loading')}</div>}
      </div>
    )
  }

  const dateLocale = language === 'es' ? 'es-ES' : 'en-US'
  const subParts = [new Date(event.starts_at).toLocaleString(dateLocale, { weekday: 'short', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })]
  if (event.location) subParts.push(event.location)

  const seatsOffered = attendees.filter((a) => a.car_status === 'offering').reduce((sum, a) => sum + (a.car_offered_seats ?? 0), 0)
  const ridersNeeded = attendees.filter((a) => a.car_status === 'needs_ride').length
  const showTransportControls = myResponse === 'going' || myResponse === 'maybe'
  const canOfferCar = myResponse === 'going'

  function attendeeRow(a: EventAttendee) {
    const memberInfo = members.find((m) => m.member_id === a.member_id)?.members
    if (!memberInfo) return null
    const carCaption =
      a.car_status === 'offering'
        ? t('EventDetail_HasCarSeats', a.car_offered_seats ?? 0)
        : a.car_status === 'needs_ride'
          ? t('GroupEvents_NeedRide')
          : ''
    return (
      <div className="attendee-row" key={a.member_id}>
        <Avatar url={avatarUrl(memberInfo)} initials={initials(memberInfo)} size={30} />
        <div>
          <div className="attendee-name">
            {displayName(memberInfo)} {a.member_id === myMemberId && <span className="attendee-you">({t('Members_You')})</span>}
          </div>
          {carCaption && <div className="attendee-car">{carCaption}</div>}
        </div>
      </div>
    )
  }

  const goingAttendees = attendees.filter((a) => a.response === 'going')
  const maybeAttendees = attendees.filter((a) => a.response === 'maybe')
  const notGoingAttendees = attendees.filter((a) => a.response === 'not_going')

  const searchTrimmed = expenseSearch.trim().toLowerCase()
  const shownExpenses = searchTrimmed
    ? expenses.filter(
        (e) =>
          (e.description || '').toLowerCase().includes(searchTrimmed) ||
          displayName(members.find((m) => m.member_id === e.paid_by_member_id)?.members ?? { id: '', display_name: '' }).toLowerCase().includes(searchTrimmed),
      )
    : expenses

  return (
    <div className="page">
      <AppHeader title={event.title} backTo={`/groups/${groupId}`} />
      {error && <p className="error-text">{error}</p>}

      {!event.is_birthday && (
        <Link to={`/groups/${groupId}/events/${eventId}/edit`} className="btn btn-outline edit-event-btn">
          {t('EventDetail_EditEvent')}
        </Link>
      )}

      <p className="event-subcaption">{subParts.join(' · ')}</p>
      {event.description && <p className="event-description">{event.description}</p>}

      {!event.is_birthday && (
        <div className="rsvp-pill">
          <button type="button" className={myResponse === 'going' ? 'active' : ''} onClick={() => setRsvp('going')}>
            {t('GroupEvents_Going')}
          </button>
          <button type="button" className={myResponse === 'maybe' ? 'active' : ''} onClick={() => setRsvp('maybe')}>
            {t('GroupEvents_Maybe')}
          </button>
          <button type="button" className={myResponse === 'not_going' ? 'active' : ''} onClick={() => setRsvp('not_going')}>
            {t('GroupEvents_NotGoing')}
          </button>
        </div>
      )}

      {event.needs_transport && !event.is_birthday && (
        <div className="transport-section">
          <div className={`transport-summary ${seatsOffered < ridersNeeded ? 'shortfall' : ''}`}>
            {t('GroupEvents_TransportSummary', seatsOffered, ridersNeeded)}
          </div>
          {showTransportControls && (
            <div className="transport-controls">
              {canOfferCar && (
                <button type="button" className={mine?.car_status === 'offering' ? 'btn btn-outline active' : 'btn btn-outline'} onClick={toggleCarOffering}>
                  {t('GroupEvents_HaveCar')}
                </button>
              )}
              <button type="button" className={mine?.car_status === 'needs_ride' ? 'btn btn-outline active' : 'btn btn-outline'} onClick={toggleCarNeedsRide}>
                {t('GroupEvents_NeedRide')}
              </button>
              {mine?.car_status === 'offering' && (
                <div className="seat-stepper">
                  <button type="button" onClick={() => adjustSeats(-1)}>−</button>
                  <span>{mine.car_offered_seats ?? 0}</span>
                  <button type="button" onClick={() => adjustSeats(1)}>+</button>
                </div>
              )}
            </div>
          )}
        </div>
      )}

      <section>
        <h2 className="section-title">{t('EventDetail_Attendees')}</h2>
        {goingAttendees.length === 0 && maybeAttendees.length === 0 && notGoingAttendees.length === 0 ? (
          <p className="empty-state small">{t('EventDetail_NoAttendees')}</p>
        ) : (
          <>
            {goingAttendees.length > 0 && (
              <div className="attendee-group">
                <h3 className="attendee-group-header">{t('GroupEvents_Going')}</h3>
                {goingAttendees.map(attendeeRow)}
              </div>
            )}
            {maybeAttendees.length > 0 && (
              <div className="attendee-group">
                <h3 className="attendee-group-header">{t('GroupEvents_Maybe')}</h3>
                {maybeAttendees.map(attendeeRow)}
              </div>
            )}
            {notGoingAttendees.length > 0 && (
              <div className="attendee-group">
                <h3 className="attendee-group-header">{t('GroupEvents_NotGoing')}</h3>
                {notGoingAttendees.map(attendeeRow)}
              </div>
            )}
          </>
        )}
      </section>

      <section>
        <div className="event-expenses-header">
          <h2 className="section-title">{t('EventDetail_Expenses')}</h2>
          <Link to={`/groups/${groupId}/add-expense?eventId=${eventId}`} className="btn btn-outline add-event-expense-btn">
            {t('EventDetail_AddExpense')}
          </Link>
        </div>
        {expenses.length > 0 && (
          <input
            type="search"
            className="activity-search"
            placeholder={t('GroupDetail_SearchExpenses')}
            value={expenseSearch}
            onChange={(e) => setExpenseSearch(e.target.value)}
          />
        )}
        {expenses.length === 0 ? (
          <p className="empty-state small">{t('EventDetail_NoExpenses')}</p>
        ) : shownExpenses.length === 0 ? (
          <p className="empty-state small">{t('GroupDetail_NoSearchResults')}</p>
        ) : (
          <div className="activity-list">
            {shownExpenses.map((exp) => (
              <Link to={`/groups/${groupId}/expenses/${exp.id}`} className="card activity-row" key={exp.id}>
                <div>
                  <div className="activity-desc">
                    {exp.is_settlement ? t('GroupDetail_SettleUp') : exp.description || t('GroupDetail_ExpenseFallback')}
                    {exp.receipt_path && <span className="receipt-badge" title={t('AddExpense_ReceiptPhoto')}>📎</span>}
                  </div>
                  <div className="activity-meta">
                    {displayName(members.find((m) => m.member_id === exp.paid_by_member_id)?.members ?? { id: '', display_name: t('GroupDetail_SomeoneCapitalized') })} ·{' '}
                    {new Date(exp.occurred_at).toLocaleDateString(dateLocale)}
                  </div>
                </div>
                <div className="activity-amount">
                  {exp.amount.toFixed(2)} {exp.currency}
                </div>
              </Link>
            ))}
          </div>
        )}
      </section>
    </div>
  )
}
