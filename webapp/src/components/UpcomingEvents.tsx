import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useGroups } from '../context/useGroups'
import { useLocale } from '../context/LocaleContext'
import { useUpcomingEvents } from '../lib/useUpcomingEvents'
import { formatEventRange } from '../lib/dates'
import { Icon } from './Icon'

export function UpcomingEvents({ agenda = false }: { agenda?: boolean }) {
  const { groups, currentMember } = useGroups()
  const { t, language } = useLocale()
  const [limit, setLimit] = useState(agenda ? 24 : 5)
  const feed = useUpcomingEvents(
    (groups ?? []).map((g) => g.id),
    currentMember?.id,
    limit,
  )
  const dateLocale = language === 'es' ? 'es-ES' : 'en-GB'
  return (
    <section className="overview-events" aria-labelledby="upcoming-heading">
      <div className="overview-section-heading">
        <h2 id="upcoming-heading">{t('Overview_Upcoming')}</h2>
        {!agenda && <Link to="/agenda">{t('Overview_AllEvents')}</Link>}
      </div>
      {feed.error && (
        <div className="overview-section-error" role="alert">
          <p>{t(feed.error === 'save' ? 'Overview_ReplyFailed' : 'Overview_EventsFailed')}</p>
          <button type="button" className="link-btn" onClick={() => void feed.load()}>
            {t('Common_Retry')}
          </button>
        </div>
      )}
      {feed.events === null && !feed.error && (
        <p role="status" className="overview-hint">
          {t('Common_Loading')}
        </p>
      )}
      {feed.events?.length === 0 && (
        <div className="card overview-empty">
          <h3>{t('Overview_NoEvents')}</h3>
          <p>{t('Overview_NoEventsHint')}</p>
        </div>
      )}
      {!!feed.events?.length && (
        <div className="card overview-event-list">
          {feed.events.map((event) => {
            const group = groups?.find((g) => g.id === event.group_id)
            const rows = feed.attendees.filter((a) => a.event_id === event.id)
            const mine = feed.mine(event.id)
            const disabled = feed.pending.has(event.id) || !currentMember
            const seats = rows
              .filter((a) => a.car_status === 'offering')
              .reduce((n, a) => n + (a.car_offered_seats ?? 0), 0)
            const riders = rows.filter((a) => a.car_status === 'needs_ride').length
            const start = new Date(event.starts_at)
            return (
              <article
                key={event.id}
                className="overview-event"
                aria-label={`${event.title}, ${group?.name ?? ''}`}
                aria-busy={feed.pending.has(event.id)}
              >
                <time className="overview-event-date" dateTime={event.starts_at}>
                  <span>{start.toLocaleDateString(dateLocale, { month: 'short' })}</span>
                  <strong>{start.getDate()}</strong>
                </time>
                <div className="overview-event-body">
                  <div className="overview-event-title">
                    <h3>
                      {event.is_birthday ? (
                        event.title
                      ) : (
                        <Link to={`/groups/${event.group_id}/events/${event.id}`}>{event.title}</Link>
                      )}
                    </h3>
                    {!event.is_birthday && !mine && (
                      <span className="overview-reply-needed">{t('Overview_ReplyNeeded')}</span>
                    )}
                    {event.is_birthday && (
                      <span className="overview-event-tag">{t('Overview_Birthday')}</span>
                    )}
                  </div>
                  <p className="overview-event-context">
                    <Link to={`/groups/${event.group_id}?tab=events`}>{group?.name}</Link> ·{' '}
                    {event.is_birthday
                      ? start.toLocaleDateString(dateLocale, {
                          weekday: 'short',
                          day: 'numeric',
                          month: 'short',
                        })
                      : formatEventRange(event.starts_at, event.ends_at, language)}
                  </p>
                  {event.location && <p className="overview-event-location">{event.location}</p>}
                  {!event.is_birthday && (
                    <>
                      <div
                        className="overview-event-responses"
                        role="group"
                        aria-label={t('Overview_ResponseTo', event.title)}
                      >
                        {(['going', 'maybe', 'not_going'] as const).map((response, i) => (
                          <button
                            key={response}
                            type="button"
                            disabled={disabled}
                            aria-pressed={mine?.response === response}
                            onClick={() => feed.respond(event.id, response)}
                          >
                            {t(['GroupEvents_Going', 'GroupEvents_Maybe', 'GroupEvents_NotGoing'][i])}
                          </button>
                        ))}
                      </div>
                      {event.needs_transport && (
                        <div className="overview-event-transport">
                          <div
                            className="overview-event-responses"
                            role="group"
                            aria-label={t('Overview_TransportTo', event.title)}
                          >
                            <button
                              type="button"
                              disabled={disabled || mine?.response !== 'going'}
                              aria-pressed={mine?.car_status === 'offering'}
                              onClick={() => feed.transport(event.id, 'offering')}
                            >
                              <Icon name="car" size={16} />
                              {t('Overview_CanDrive')}
                            </button>
                            <button
                              type="button"
                              disabled={disabled || !mine || mine.response === 'not_going'}
                              aria-pressed={mine?.car_status === 'needs_ride'}
                              onClick={() => feed.transport(event.id, 'needs_ride')}
                            >
                              {t('GroupEvents_NeedRide')}
                            </button>
                          </div>
                          {mine?.car_status === 'offering' && (
                            <div className="overview-seat-stepper">
                              <span>{t('Overview_SpareSeats')}</span>
                              <button
                                type="button"
                                aria-label={t('GroupEvents_FewerSeats')}
                                disabled={disabled || !mine.car_offered_seats}
                                onClick={() => feed.adjustSeats(event.id, -1)}
                              >
                                −
                              </button>
                              <output aria-live="polite">{mine.car_offered_seats ?? 0}</output>
                              <button
                                type="button"
                                aria-label={t('GroupEvents_MoreSeats')}
                                disabled={disabled || (mine.car_offered_seats ?? 0) >= 99}
                                onClick={() => feed.adjustSeats(event.id, 1)}
                              >
                                +
                              </button>
                            </div>
                          )}
                          <p className="overview-transport-summary">
                            {t('GroupEvents_TransportSummary', seats, riders)}
                          </p>
                          {!mine && (
                            <p className="overview-transport-summary">{t('Overview_TransportReplyHint')}</p>
                          )}
                          {mine?.response === 'maybe' && (
                            <p className="overview-transport-summary">{t('Overview_TransportGoingHint')}</p>
                          )}
                        </div>
                      )}
                      {feed.pending.has(event.id) && (
                        <span className="overview-hint" role="status">
                          {t('Common_Saving')}
                        </span>
                      )}
                    </>
                  )}
                </div>
              </article>
            )
          })}
        </div>
      )}
      {agenda && feed.hasMore && (
        <button
          type="button"
          className="btn btn-outline overview-load-more"
          onClick={() => setLimit((n) => n + 24)}
        >
          {t('GroupDetail_LoadMore')}
        </button>
      )}
    </section>
  )
}
