import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useParams } from 'react-router-dom'
import { useGoBackTo } from '../lib/navigation'
import { supabase } from '../lib/supabaseClient'
import { useAuth } from '../context/AuthContext'
import { useLocale } from '../context/LocaleContext'
import { AppHeader } from '../components/AppHeader'
import { DateField } from '../components/DateField'
import { isCalendarDate, localDateTimeToIso } from '../lib/dates'
import { eventEditorSnapshot } from '../lib/formEditorSnapshots'
import { useUnsavedChanges } from '../lib/useUnsavedChanges'
import { UnsavedChangesDialog } from '../components/UnsavedChangesDialog'
import { useGroups } from '../context/useGroups'
import '../components/TogetherPage.css'
import './AddEventPage.css'

function toLocalDateInput(iso: string): string {
  const d = new Date(iso)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function toLocalTimeInput(iso: string): string {
  const d = new Date(iso)
  return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
}

export function AddEventPage() {
  const { groupId, eventId } = useParams<{ groupId: string; eventId?: string }>()
  const { session } = useAuth()
  const { t } = useLocale()
  const { groups } = useGroups()
  const groupName = groups?.find((group) => group.id === groupId)?.name
  const goBackTo = useGoBackTo()
  const parentPath = eventId ? `/groups/${groupId}/events/${eventId}` : `/groups/${groupId}?tab=events`

  const now = new Date()
  const defaultDate = toLocalDateInput(now.toISOString())
  const defaultTime = toLocalTimeInput(now.toISOString())

  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [location, setLocation] = useState('')
  const [startDate, setStartDate] = useState(defaultDate)
  const [startTime, setStartTime] = useState(defaultTime)
  const [hasEndTime, setHasEndTime] = useState(false)
  const [endDate, setEndDate] = useState(defaultDate)
  const [endTime, setEndTime] = useState(defaultTime)
  const [needsTransport, setNeedsTransport] = useState(false)
  const [isEditMode, setIsEditMode] = useState(false)
  const [canDelete, setCanDelete] = useState(false)
  const [loaded, setLoaded] = useState(!eventId)
  const editorKey = `${session?.user.id}:${groupId}:${eventId ?? 'new'}`
  const [loadedKey, setLoadedKey] = useState<string | null>(null)
  const inFlight = useRef(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const loadErrorText = useRef(t('Common_Error'))
  useEffect(() => { loadErrorText.current = t('Common_Error') }, [t])
  const unsaved = useUnsavedChanges(editorKey, loaded && loadedKey === editorKey ? eventEditorSnapshot({
    title, description, location, startDate, startTime, hasEndTime, endDate, endTime, needsTransport,
  }) : null, busy)

  useEffect(() => {
    let active = true
    setLoadedKey(null)
    setLoaded(false)
    setError(null)
    ;(async () => {
      if (!eventId) {
        const initial = new Date().toISOString()
        setTitle(''); setDescription(''); setLocation(''); setNeedsTransport(false)
        setStartDate(toLocalDateInput(initial)); setStartTime(toLocalTimeInput(initial))
        setEndDate(toLocalDateInput(initial)); setEndTime(toLocalTimeInput(initial))
        setHasEndTime(false); setIsEditMode(false); setCanDelete(false)
        setLoaded(true); setLoadedKey(editorKey)
        return
      }
      const { data, error } = await supabase.from('events').select('*').eq('id', eventId).single()
      if (!active) return
      if (error) {
        setError(error.message)
        setLoaded(true)
        return
      }
      setIsEditMode(true)
      setTitle(data.title)
      setDescription(data.description ?? '')
      setLocation(data.location ?? '')
      setStartDate(toLocalDateInput(data.starts_at))
      setStartTime(toLocalTimeInput(data.starts_at))
      if (data.ends_at) {
        setHasEndTime(true)
        setEndDate(toLocalDateInput(data.ends_at))
        setEndTime(toLocalTimeInput(data.ends_at))
      } else setHasEndTime(false)
      setNeedsTransport(data.needs_transport)
      setCanDelete(data.created_by === session?.user.id)
      setLoaded(true)
      setLoadedKey(editorKey)
    })().catch(() => { if (active) { setError(loadErrorText.current); setLoaded(false) } })
    return () => { active = false }
  }, [eventId, editorKey, session?.user.id])

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    if (inFlight.current) return
    if (!title.trim() || !groupId) {
      setError(t('AddEvent_TitleRequired'))
      return
    }
    setError(null)
    if (!isCalendarDate(startDate) || (hasEndTime && !isCalendarDate(endDate))) {
      setError(t('DateInput_Invalid'))
      return
    }
    let startsAt: string
    let endsAt: string | null
    try {
      startsAt = localDateTimeToIso(startDate, startTime)
      endsAt = hasEndTime ? localDateTimeToIso(endDate, endTime) : null
    } catch {
      setError(t('DateInput_InvalidDateTime'))
      return
    }
    if (endsAt && new Date(endsAt) <= new Date(startsAt)) {
      setError(t('AddEvent_EndAfterStart'))
      return
    }
    inFlight.current = true
    setBusy(true)
    try {
      const payload = {
        group_id: groupId,
        title: title.trim(),
        description: description.trim() || null,
        location: location.trim() || null,
        starts_at: startsAt,
        ends_at: endsAt,
        needs_transport: needsTransport,
      }

      if (eventId) {
        const { error } = await supabase.from('events').update(payload).eq('id', eventId)
        if (error) return setError(error.message)
        unsaved.markSaved()
      } else {
        const { data: inserted, error } = await supabase
          .from('events')
          .insert({ ...payload, created_by: session?.user.id })
          .select('id')
          .single()
        if (error) return setError(error.message)
        unsaved.markSaved()
        // Creator auto-RSVPs "going" — same as AddEventViewModel.Save.
        try {
          const { data: myMember } = await supabase
            .from('members')
            .select('id')
            .eq('account_id', session?.user.id)
            .order('created_at', { ascending: true })
            .limit(1)
            .maybeSingle()
          if (myMember) {
            await supabase.from('event_attendees').insert({ event_id: inserted.id, member_id: myMember.id, response: 'going' })
          }
        } catch { /* The event is committed; RSVP failure must not invite a duplicate save. */ }
      }
      goBackTo(parentPath)
    } catch {
      setError(t('Common_SaveFailed'))
    } finally {
      setBusy(false)
      inFlight.current = false
    }
  }

  async function handleDelete() {
    if (inFlight.current) return
    if (!window.confirm(t('AddEvent_DeleteConfirm'))) return
    inFlight.current = true
    setBusy(true)
    try {
      const { error } = await supabase.from('events').delete().eq('id', eventId)
      if (error) return setError(error.message)
      unsaved.markSaved()
      goBackTo(`/groups/${groupId}?tab=events`)
    } catch { setError(t('Common_SaveFailed')) }
    finally { setBusy(false); inFlight.current = false }
  }

  const title_ = isEditMode ? t('AddEvent_EditTitle') : t('AddEvent_Title')

  if (!loaded || loadedKey !== editorKey) {
    return (
      <div className="page together-page event-editor-page">
        <AppHeader title={title_} backTo={parentPath} />
        {error ? <p className="error-text">{error}</p> : <div className="spinner">{t('Common_Loading')}</div>}
      </div>
    )
  }

  return (
    <div className="page together-page event-editor-page">
      <AppHeader title={title_} backTo={parentPath} />
      {groupName && <p className="page-context">{groupName}</p>}
      <UnsavedChangesDialog blocker={unsaved.blocker} keepEditing={unsaved.keepEditing} pending={busy}
        open={unsaved.confirmingClose} onDiscard={unsaved.discardChanges} />
      <form className="event-editor-form" onSubmit={handleSubmit}>
        <fieldset className="event-editor-fields" disabled={busy}>
          <section className="together-panel" aria-labelledby="event-details-heading">
            <h2 id="event-details-heading">{t('AddEvent_DetailsTitle')}</h2>
            <div className="field">
              <label htmlFor="title">{t('AddEvent_TitleLabel')}</label>
              <input id="title" required value={title} onChange={(e) => setTitle(e.target.value)} placeholder={t('AddEvent_TitlePlaceholder')} />
            </div>

            <div className="field">
              <label htmlFor="description">{t('AddEvent_DescriptionLabel')}</label>
              <input id="description" value={description} onChange={(e) => setDescription(e.target.value)} placeholder={t('AddEvent_DescriptionPlaceholder')} />
            </div>

            <div className="field">
              <label htmlFor="location">{t('AddEvent_LocationLabel')}</label>
              <input id="location" value={location} onChange={(e) => setLocation(e.target.value)} placeholder={t('AddEvent_LocationPlaceholder')} />
            </div>

          </section>
          <section className="together-panel" aria-labelledby="event-time-heading">
            <h2 id="event-time-heading">{t('AddEvent_WhenTitle')}</h2>
            <div className="datetime-row">
              <div className="field">
                <label htmlFor="startDate">{t('AddEvent_StartDate')}</label>
                <DateField id="startDate" required value={startDate} onChange={setStartDate} />
              </div>
              <div className="field">
                <label htmlFor="startTime">{t('AddEvent_StartTime')}</label>
                <input id="startTime" type="time" required value={startTime} onChange={(e) => setStartTime(e.target.value)} />
              </div>
            </div>

            <label className="repeat-toggle">
              <input type="checkbox" checked={hasEndTime} onChange={(e) => setHasEndTime(e.target.checked)} />
              {t('AddEvent_HasEndTime')}
            </label>

            {hasEndTime && (
              <div className="datetime-row">
                <div className="field">
                  <label htmlFor="endDate">{t('AddEvent_EndDate')}</label>
                  <DateField id="endDate" required value={endDate} onChange={setEndDate} />
                </div>
                <div className="field">
                  <label htmlFor="endTime">{t('AddEvent_EndTime')}</label>
                  <input id="endTime" type="time" required value={endTime} onChange={(e) => setEndTime(e.target.value)} />
                </div>
              </div>
            )}

            <label className="repeat-toggle">
              <input type="checkbox" checked={needsTransport} onChange={(e) => setNeedsTransport(e.target.checked)} />
              {t('AddEvent_NeedsTransport')}
            </label>

          </section>
          {error && <p className="error-text" role="alert">{error}</p>}

          <div className="together-form-actions">
          <button type="button" className="btn btn-outline" disabled={busy}
            onClick={() => unsaved.requestClose(() => goBackTo(parentPath))}>{t('Common_Cancel')}</button>
          <button type="submit" className="btn btn-primary" disabled={busy}>
            {busy ? t('Common_Saving') : t('AddEvent_SaveEvent')}
          </button>
          </div>

          {canDelete && (
            <button type="button" className="btn btn-danger delete-btn" onClick={handleDelete} disabled={busy}>
              {t('AddEvent_DeleteEvent')}
            </button>
          )}
        </fieldset>
      </form>
    </div>
  )
}
