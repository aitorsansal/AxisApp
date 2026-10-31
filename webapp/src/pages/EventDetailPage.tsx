import { Icon } from '../components/Icon'
import { activityLabel } from '../lib/activityLabel'
import { categoryIcon } from '../lib/icons'
import { formatExpenseAmount, getShowConverted } from '../lib/amountDisplay'
import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { SerializedUpdates } from '../lib/serializedUpdates'
import { useCoalescedWrites } from '../lib/useCoalescedWrites'
import { useLiveRefresh } from '../lib/useLiveRefresh'
import { RefreshRevision } from '../lib/refreshRevision'
import { respondToEvent, toggleEventTransport } from '../lib/eventAttendance'
import { activityDate, formatEventRange } from '../lib/dates'
import { useAuth } from '../context/AuthContext'
import { useAliases } from '../context/AliasesContext'
import { useLocale } from '../context/LocaleContext'
import type {
  EventAttendee,
  EventRow,
  ExpenseWithPayer,
  Group,
  MemberRow,
  RsvpResponse,
} from '../lib/types'
import { AppHeader } from '../components/AppHeader'
import { Avatar } from '../components/Avatar'
import '../components/TogetherPage.css'
import './EventDetailPage.css'

// Same URL as AppConstants.Links.BuildEventUrl, which the Android app claims as an App Link.
// Absolute so links shared from a localhost dev build still work.
function buildEventUrl(groupId: string, eventId: string): string {
  return `https://app.axisapp.aitorsansal.com/groups/${groupId}/events/${eventId}`
}

export function EventDetailPage() {
  const { groupId, eventId } = useParams<{ groupId: string; eventId: string }>()
  const { session } = useAuth()
  const accountId = session?.user.id
  const { displayName, initials, avatarUrl } = useAliases()
  const { t, language } = useLocale()

  const [group, setGroup] = useState<Group | null>(null)
  const [event, setEvent] = useState<EventRow | null>(null)
  const [members, setMembers] = useState<MemberRow[]>([])
  const [attendees, setAttendees] = useState<EventAttendee[]>([])
  const [expenses, setExpenses] = useState<ExpenseWithPayer[]>([])
  const [expenseSearch, setExpenseSearch] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [loaded, setLoaded] = useState(false)
  const [myCarExtraSeats, setMyCarExtraSeats] = useState<number | null>(null)
  const rsvpUpdates = useRef(new SerializedUpdates<EventAttendee>())
  const refreshRevision = useRef(new RefreshRevision())

  const myMemberId = members.find((m) => m.members.account_id === session?.user.id)?.member_id

  const load = useCallback(async (writeRevision?: number) => {
    if (!groupId || !eventId) return
    const canApply = refreshRevision.current.begin(writeRevision)
    setError(null)
    const [groupRes, eventRes, membersRes, attendeesRes, expensesRes, myMemberRes] = await Promise.all([
      supabase
        .from('groups')
        .select('id, name, currency, created_by, color, icon')
        .eq('id', groupId)
        .single(),
      supabase.from('events').select('*').eq('id', eventId).single(),
      supabase
        .from('group_members')
        .select('member_id, members(id, display_name, account_id, avatar_path)')
        .eq('group_id', groupId),
      supabase
        .from('event_attendees')
        .select('event_id, member_id, response, car_status, car_offered_seats')
        .eq('event_id', eventId),
      supabase
        .from('expenses')
        .select(
          'id, group_id, paid_by_member_id, amount, currency, amount_in_group_currency, description, category, occurred_at, created_at, is_settlement, receipt_path, event_id, expense_shares(member_id)',
        )
        .eq('event_id', eventId)
        .order('occurred_at', { ascending: false }),
      accountId ? supabase.from('members').select('car_extra_seats')
        .eq('account_id', accountId).limit(1).maybeSingle()
        : Promise.resolve({ data: null, error: null }),
    ])

    if (!canApply()) return
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
    setMyCarExtraSeats(myMemberRes.data?.car_extra_seats ?? null)
    setLoaded(true)
  }, [groupId, eventId, accountId])

  useEffect(() => {
    const revision = refreshRevision.current
    load()
    return () => { revision.invalidate() }
  }, [load])

  useLiveRefresh(
    `event-${eventId}`,
    [{ table: 'events', filter: `id=eq.${eventId}` }, { table: 'event_attendees', filter: `event_id=eq.${eventId}` }],
    load,
    () => rsvpUpdates.current.hasPending || seatWrites.hasPending,
  )

  const mine = attendees.find((a) => a.member_id === myMemberId)
  const myResponse = mine?.response ?? ''

  const seatWrites = useCoalescedWrites<number>(400, (_key, seats) =>
    queueRsvp((current) => current.car_status !== 'offering' ? current : { ...current, car_offered_seats: seats }))

  function queueRsvp(update: (current: EventAttendee) => EventAttendee) {
    if (!myMemberId || !eventId) return
    // Pending seat taps go first so a later toggle composes on top of them.
    seatWrites.flush(eventId)
    const writeRevision = refreshRevision.current.invalidate()
    const initial = mine ?? {
      event_id: eventId, member_id: myMemberId, response: 'not_going' as const,
      car_status: 'none' as const, car_offered_seats: null,
    }
    void rsvpUpdates.current.run(eventId, initial, update, async (next) => {
      const { error } = await supabase.from('event_attendees').upsert(next, { onConflict: 'event_id,member_id' })
      if (error) throw error
      await load(writeRevision)
    }).catch((err) => setError(err.message ?? t('Common_Error')))
  }

  function setRsvp(response: RsvpResponse) {
    queueRsvp(current => respondToEvent(current, response))
  }

  function toggleCarOffering() {
    queueRsvp(current => toggleEventTransport(current, 'offering', myCarExtraSeats ?? 0))
  }

  function toggleCarNeedsRide() {
    queueRsvp(current => toggleEventTransport(current, 'needs_ride', myCarExtraSeats ?? 0))
  }

  function adjustSeats(delta: number) {
    if (!eventId || mine?.car_status !== 'offering') return
    const seats = Math.max(0, (mine.car_offered_seats ?? 0) + delta)
    refreshRevision.current.invalidate()
    setAttendees((prev) => prev.map((a) => a.member_id === myMemberId ? { ...a, car_offered_seats: seats } : a))
    seatWrites.set(eventId, seats)
  }

  // Desktop browsers expose navigator.share too (the Windows share sheet), which has no plain
  // "copy" path, so copying the link is its own button.
  async function handleCopyLink() {
    if (!groupId || !eventId) return
    try {
      await navigator.clipboard.writeText(buildEventUrl(groupId, eventId))
    } catch {
      return
    }
    setNotice(t('EventDetail_LinkCopied'))
    setTimeout(() => setNotice(null), 2000)
  }

  async function handleShare() {
    if (!event || !groupId || !eventId) return
    const text = t('EventDetail_ShareText', event.title,
      formatEventRange(event.starts_at, event.ends_at, language), buildEventUrl(groupId, eventId))
    if (navigator.share) {
      try {
        await navigator.share({ text })
      } catch {
        // Share sheet dismissed — no error to surface.
      }
      return
    }
    await navigator.clipboard.writeText(text)
    setNotice(t('EventDetail_LinkCopied'))
    setTimeout(() => setNotice(null), 2000)
  }

  if (!loaded || !event || !group) {
    return (
      <div className="page page-wide together-page event-detail-page">
        <AppHeader title={t('EventDetail_GenericTitle')} backTo={`/groups/${groupId}?tab=events`} />
        {error ? (
          <p className="error-text" role="alert">
            {error}
          </p>
        ) : (
          <div className="spinner">{t('Common_Loading')}</div>
        )}
      </div>
    )
  }

  const showConverted = getShowConverted()

  const seatsOffered = attendees
    .filter((a) => a.car_status === 'offering')
    .reduce((sum, a) => sum + (a.car_offered_seats ?? 0), 0)
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
        <Avatar url={avatarUrl(memberInfo)} initials={initials(memberInfo)} seed={memberInfo.id} size={30} />
        <div>
          <div className="attendee-name">
            {displayName(memberInfo)}{' '}
            {a.member_id === myMemberId && (
              <span className="attendee-you">({t('Members_You')})</span>
            )}
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
          displayName(
            members.find((m) => m.member_id === e.paid_by_member_id)?.members ?? {
              id: '',
              display_name: '',
            },
          )
            .toLowerCase()
            .includes(searchTrimmed),
      )
    : expenses

  return (
    <div className="page page-wide together-page event-detail-page">
      <AppHeader title={event.title} backTo={`/groups/${groupId}?tab=events`} />
      <p className="page-context"><Link to={`/groups/${groupId}?tab=events`}>{group.name}</Link></p>
      {error && (
        <p className="error-text" role="alert">
          {error}
        </p>
      )}

      {notice && (
        <p className="notice-text" role="status">
          {notice}
        </p>
      )}

      <div className="event-actions">
        {!event.is_birthday && (
          <Link
            to={`/groups/${groupId}/events/${eventId}/edit`}
            className="btn btn-outline"
          >
            {t('EventDetail_EditEvent')}
          </Link>
        )}
        <button type="button" className="btn btn-outline" onClick={handleShare}>
          {t('EventDetail_Share')}
        </button>
        <button type="button" className="btn btn-outline" onClick={handleCopyLink}>
          {t('EventDetail_CopyLink')}
        </button>
      </div>

      <div className={`event-detail-layout${event.is_birthday ? ' birthday-plan' : ''}`}>
      <section className="together-panel event-plan" aria-label={t('EventDetail_GenericTitle')}>
        <div className="event-plan-line">
          <Icon name="calendar" size={20} />
          <p className="event-subcaption">{formatEventRange(event.starts_at, event.ends_at, language)}</p>
        </div>
        {event.location && <div className="event-plan-line">
          <Icon name="map_pin" size={20} />
          <p className="event-subcaption">{event.location}</p>
        </div>}
      {event.description && <p className="event-description">{event.description}</p>}

      {!event.is_birthday && (
        <div className="event-response">
          <h2>{t('EventDetail_YourResponse')}</h2>
        <div className="rsvp-pill" role="group" aria-label={t('EventDetail_YourResponse')}>
          <button
            type="button"
            className={myResponse === 'going' ? 'active' : ''}
            aria-pressed={myResponse === 'going'}
            onClick={() => setRsvp('going')}
          >
            {t('GroupEvents_Going')}
          </button>
          <button
            type="button"
            className={myResponse === 'maybe' ? 'active' : ''}
            aria-pressed={myResponse === 'maybe'}
            onClick={() => setRsvp('maybe')}
          >
            {t('GroupEvents_Maybe')}
          </button>
          <button
            type="button"
            className={myResponse === 'not_going' ? 'active' : ''}
            aria-pressed={myResponse === 'not_going'}
            onClick={() => setRsvp('not_going')}
          >
            {t('GroupEvents_NotGoing')}
          </button>
        </div>
        </div>
      )}

      {event.needs_transport && !event.is_birthday && (
        <div className="transport-section">
          <h3>{t('EventDetail_Transport')}</h3>
          <div className={`transport-summary ${seatsOffered < ridersNeeded ? 'shortfall' : ''}`}>
            {t('GroupEvents_TransportSummary', seatsOffered, ridersNeeded)}
          </div>
          {showTransportControls && (
            <div className="transport-controls">
              {canOfferCar && (
                <button
                  type="button"
                  className={
                    mine?.car_status === 'offering' ? 'btn btn-outline active' : 'btn btn-outline'
                  }
                  aria-pressed={mine?.car_status === 'offering'}
                  onClick={toggleCarOffering}
                >
                  {t('GroupEvents_HaveCar')}
                </button>
              )}
              <button
                type="button"
                className={
                  mine?.car_status === 'needs_ride' ? 'btn btn-outline active' : 'btn btn-outline'
                }
                aria-pressed={mine?.car_status === 'needs_ride'}
                onClick={toggleCarNeedsRide}
              >
                {t('GroupEvents_NeedRide')}
              </button>
              {mine?.car_status === 'offering' && (
                <div className="seat-stepper">
                  <button
                    type="button"
                    aria-label={t('GroupEvents_FewerSeats')}
                    onClick={() => adjustSeats(-1)}
                  >
                    −
                  </button>
                  <span>{mine.car_offered_seats ?? 0}</span>
                  <button
                    type="button"
                    aria-label={t('GroupEvents_MoreSeats')}
                    onClick={() => adjustSeats(1)}
                  >
                    +
                  </button>
                </div>
              )}
            </div>
          )}
        </div>
      )}

      </section>

      {!event.is_birthday && <section className="together-panel event-attendees" aria-labelledby="event-attendees-heading">
        <h2 id="event-attendees-heading" className="section-title">{t('EventDetail_Attendees')}</h2>
        {goingAttendees.length === 0 &&
        maybeAttendees.length === 0 &&
        notGoingAttendees.length === 0 ? (
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
      </section>}

      <section className="together-panel event-expenses" aria-labelledby="event-expenses-heading">
        <div className="event-expenses-header">
          <h2 id="event-expenses-heading" className="section-title">{t('EventDetail_Expenses')}</h2>
          <Link
            to={`/groups/${groupId}/add-expense?eventId=${eventId}`}
            className="btn btn-outline add-event-expense-btn"
          >
            {t('EventDetail_AddExpense')}
          </Link>
        </div>
        {expenses.length > 0 && (
          <input
            type="search"
            className="activity-search"
            placeholder={t('GroupDetail_SearchExpenses')}
            aria-label={t('GroupDetail_SearchExpenses')}
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
              <Link
                to={`/groups/${groupId}/expenses/${exp.id}`}
                className="card activity-row"
                key={exp.id}
              >
                <span className="activity-icon">
                  <Icon name={categoryIcon(exp.category, exp.is_settlement)} size={22} />
                </span>
                <div className="activity-body">
                  <div className="activity-desc">
                    {exp.description || t(exp.is_settlement ? 'GroupDetail_SettleUp' : 'GroupDetail_ExpenseFallback')}
                    {exp.receipt_path && (
                      <span className="receipt-badge" title={t('AddExpense_ReceiptPhoto')}>
                        📎
                      </span>
                    )}
                  </div>
                  <div className="activity-meta">
                    {(() => {
                      const label = activityLabel(
                        {
                          isSettlement: exp.is_settlement,
                          payerId: exp.paid_by_member_id,
                          shareMemberIds: exp.expense_shares?.map((s) => s.member_id),
                          viewerId: myMemberId,
                        },
                        (id) => {
                          const row = members.find((m) => m.member_id === id)
                          return row ? displayName(row.members) : t('GroupDetail_SomeoneCapitalized')
                        },
                      )
                      return t(label.key, ...label.args)
                    })()}{' '}
                    · {activityDate(exp.occurred_at, language, { today: t('Common_Today'), yesterday: t('Common_Yesterday') })}
                  </div>
                </div>
                <div className="activity-value">
                  {(() => {
                    const shown = formatExpenseAmount(exp, group.currency, language, showConverted)
                    return (
                      <>
                        <strong className="activity-amount">{shown.primary}</strong>
                        {shown.secondary && <span className="activity-secondary">{shown.secondary}</span>}
                      </>
                    )
                  })()}
                </div>
              </Link>
            ))}
          </div>
        )}
      </section>
      </div>
    </div>
  )
}
