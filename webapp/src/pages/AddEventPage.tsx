import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { useAuth } from '../context/AuthContext'
import { useLocale } from '../context/LocaleContext'
import { AppHeader } from '../components/AppHeader'
import { DateField } from '../components/DateField'
import './AddEventPage.css'

function toLocalDateInput(iso: string): string {
  const d = new Date(iso)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function toLocalTimeInput(iso: string): string {
  const d = new Date(iso)
  return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
}

function combineToIso(date: string, time: string): string {
  return new Date(`${date}T${time}`).toISOString()
}

export function AddEventPage() {
  const { groupId, eventId } = useParams<{ groupId: string; eventId?: string }>()
  const { session } = useAuth()
  const { t } = useLocale()
  const navigate = useNavigate()

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
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!eventId) return
    ;(async () => {
      const { data, error } = await supabase.from('events').select('*').eq('id', eventId).single()
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
      }
      setNeedsTransport(data.needs_transport)
      setCanDelete(data.created_by === session?.user.id)
      setLoaded(true)
    })()
  }, [eventId, session])

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    if (!title.trim() || !groupId) {
      setError(t('AddEvent_TitleRequired'))
      return
    }
    setError(null)
    setBusy(true)

    const payload = {
      group_id: groupId,
      title: title.trim(),
      description: description.trim() || null,
      location: location.trim() || null,
      starts_at: combineToIso(startDate, startTime),
      ends_at: hasEndTime ? combineToIso(endDate, endTime) : null,
      needs_transport: needsTransport,
    }

    if (eventId) {
      const { error } = await supabase.from('events').update(payload).eq('id', eventId)
      setBusy(false)
      if (error) return setError(error.message)
    } else {
      const { data: inserted, error } = await supabase
        .from('events')
        .insert({ ...payload, created_by: session?.user.id })
        .select('id')
        .single()
      if (error) {
        setBusy(false)
        return setError(error.message)
      }
      // Creator auto-RSVPs "going" — same as AddEventViewModel.Save.
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
      setBusy(false)
    }

    navigate(`/groups/${groupId}`)
  }

  async function handleDelete() {
    if (!window.confirm(t('AddEvent_DeleteConfirm'))) return
    setBusy(true)
    const { error } = await supabase.from('events').delete().eq('id', eventId)
    setBusy(false)
    if (error) return setError(error.message)
    navigate(`/groups/${groupId}`)
  }

  const title_ = isEditMode ? t('AddEvent_EditTitle') : t('AddEvent_Title')

  if (!loaded) {
    return (
      <div className="page">
        <AppHeader title={title_} back />
        {error ? <p className="error-text">{error}</p> : <div className="spinner">{t('Common_Loading')}</div>}
      </div>
    )
  }

  return (
    <div className="page">
      <AppHeader title={title_} back />

      <form onSubmit={handleSubmit}>
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

        {error && <p className="error-text">{error}</p>}

        <button type="submit" className="btn btn-primary" disabled={busy}>
          {busy ? t('Common_Saving') : t('AddEvent_SaveEvent')}
        </button>

        {canDelete && (
          <button type="button" className="btn btn-danger delete-btn" onClick={handleDelete} disabled={busy}>
            {t('AddEvent_DeleteEvent')}
          </button>
        )}
      </form>
    </div>
  )
}
