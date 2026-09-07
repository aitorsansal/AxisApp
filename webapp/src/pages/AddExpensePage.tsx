import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { supabase } from '../lib/supabaseClient'
import { useAuth } from '../context/AuthContext'
import { CATEGORIES, type Group, type MemberRow } from '../lib/types'
import { AppHeader } from '../components/AppHeader'
import './AddExpensePage.css'

export function AddExpensePage() {
  const { groupId } = useParams<{ groupId: string }>()
  const { session } = useAuth()
  const navigate = useNavigate()

  const [group, setGroup] = useState<Group | null>(null)
  const [members, setMembers] = useState<MemberRow[]>([])
  const [description, setDescription] = useState('')
  const [amount, setAmount] = useState('')
  const [category, setCategory] = useState('general')
  const [occurredOn, setOccurredOn] = useState(() => new Date().toISOString().slice(0, 10))
  const [paidBy, setPaidBy] = useState('')
  const [participants, setParticipants] = useState<Set<string>>(new Set())
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!groupId) return
    ;(async () => {
      const [groupRes, membersRes] = await Promise.all([
        supabase.from('groups').select('id, name, currency, created_by').eq('id', groupId).single(),
        supabase
          .from('group_members')
          .select('member_id, members(display_name, account_id)')
          .eq('group_id', groupId),
      ])
      if (groupRes.error) return setError(groupRes.error.message)
      if (membersRes.error) return setError(membersRes.error.message)

      setGroup(groupRes.data as Group)
      const memberRows = (membersRes.data as unknown as MemberRow[]) ?? []
      setMembers(memberRows)
      setParticipants(new Set(memberRows.map((m) => m.member_id)))

      const myRow = memberRows.find((m) => m.members.account_id === session?.user.id)
      if (myRow) setPaidBy(myRow.member_id)
      else if (memberRows[0]) setPaidBy(memberRows[0].member_id)
    })()
  }, [groupId, session])

  function toggleParticipant(memberId: string) {
    setParticipants((prev) => {
      const next = new Set(prev)
      if (next.has(memberId)) next.delete(memberId)
      else next.add(memberId)
      return next
    })
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    if (!group || !groupId) return
    setError(null)

    const total = Number(amount)
    if (!total || total <= 0) {
      setError('Enter a valid amount')
      return
    }
    if (participants.size === 0) {
      setError('Pick at least one participant')
      return
    }
    if (!paidBy) {
      setError('Pick who paid')
      return
    }

    setBusy(true)

    const { data: expense, error: expenseError } = await supabase
      .from('expenses')
      .insert({
        group_id: groupId,
        paid_by_member_id: paidBy,
        amount: total,
        currency: group.currency,
        description,
        category,
        occurred_at: new Date(occurredOn).toISOString(),
      })
      .select('id')
      .single()

    if (expenseError) {
      setError(expenseError.message)
      setBusy(false)
      return
    }

    const shareAmount = Math.round((total / participants.size) * 100) / 100
    const shares = Array.from(participants).map((memberId) => ({
      expense_id: expense.id,
      member_id: memberId,
      share_amount: shareAmount,
    }))

    const { error: sharesError } = await supabase.from('expense_shares').insert(shares)
    setBusy(false)
    if (sharesError) {
      setError(sharesError.message)
      return
    }

    navigate(`/groups/${groupId}`)
  }

  if (!group) {
    return (
      <div className="page">
        <AppHeader title="Add expense" back />
        {error ? <p className="error-text">{error}</p> : <div className="spinner">Loading…</div>}
      </div>
    )
  }

  return (
    <div className="page">
      <AppHeader title="Add expense" back />

      <form onSubmit={handleSubmit}>
        <div className="field">
          <label htmlFor="description">Description</label>
          <input
            id="description"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="Dinner, groceries…"
          />
        </div>

        <div className="field">
          <label htmlFor="amount">Amount ({group.currency})</label>
          <input
            id="amount"
            type="number"
            step="0.01"
            min="0.01"
            required
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
          />
        </div>

        <div className="field">
          <label htmlFor="category">Category</label>
          <select id="category" value={category} onChange={(e) => setCategory(e.target.value)}>
            {CATEGORIES.map((c) => (
              <option key={c.key} value={c.key}>
                {c.label}
              </option>
            ))}
          </select>
        </div>

        <div className="field">
          <label htmlFor="occurred">Date</label>
          <input
            id="occurred"
            type="date"
            required
            value={occurredOn}
            onChange={(e) => setOccurredOn(e.target.value)}
          />
        </div>

        <div className="field">
          <label htmlFor="paidBy">Paid by</label>
          <select id="paidBy" value={paidBy} onChange={(e) => setPaidBy(e.target.value)}>
            {members.map((m) => (
              <option key={m.member_id} value={m.member_id}>
                {m.members.display_name}
              </option>
            ))}
          </select>
        </div>

        <div className="field">
          <label>Split equally between</label>
          <div className="participant-list">
            {members.map((m) => (
              <label key={m.member_id} className="participant-item">
                <input
                  type="checkbox"
                  checked={participants.has(m.member_id)}
                  onChange={() => toggleParticipant(m.member_id)}
                />
                {m.members.display_name}
              </label>
            ))}
          </div>
        </div>

        {error && <p className="error-text">{error}</p>}

        <button type="submit" className="btn btn-primary" disabled={busy}>
          {busy ? 'Saving…' : 'Save expense'}
        </button>
      </form>
    </div>
  )
}
