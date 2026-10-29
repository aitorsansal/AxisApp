import { createRoot, type Root } from 'react-dom/client'
import { useState } from 'react'
import { useLiveRefresh } from '../../src/lib/useLiveRefresh'
import { emitLiveChange, emitLiveConnected, liveChannelCount } from './live-supabase'

function Harness() {
  const [pending, setPending] = useState(false)
  const [fail, setFail] = useState(false)
  const [started, setStarted] = useState(0)
  const [completed, setCompleted] = useState(0)
  const [mounted, setMounted] = useState(true)
  const [subscriptions, setSubscriptions] = useState(0)
  return <main><h1>Isolated live refresh QA</h1><p>No backend connection is used.</p>
    <p>Last inspected subscriptions {subscriptions}</p>
    <p role="status">Started {started}; completed {completed}</p>
    <label><input type="checkbox" checked={pending} onChange={e => setPending(e.target.checked)} />Pending write</label>
    <label><input type="checkbox" checked={fail} onChange={e => setFail(e.target.checked)} />Fail refresh</label>
    <button onClick={emitLiveConnected}>Connect or reconnect</button>
    <button onClick={emitLiveChange}>Remote change</button>
    <button onClick={() => window.dispatchEvent(new Event('focus'))}>Focus recovery</button>
    <button onClick={() => window.dispatchEvent(new Event('online'))}>Online recovery</button>
    <button onClick={() => setMounted(!mounted)}>{mounted ? 'Unmount observer' : 'Mount observer'}</button>
    <button onClick={() => setSubscriptions(liveChannelCount())}>Inspect subscriptions</button>
    {mounted && <Observer pending={pending} refresh={async () => {
      setStarted(n => n + 1)
      if (fail) throw new Error('Simulated refresh failure')
      setCompleted(n => n + 1)
    }} />}
  </main>
}
function Observer({ pending, refresh }: { pending: boolean; refresh: () => Promise<void> }) {
  useLiveRefresh('isolated-live-qa', [{ table: 'events' }], refresh, () => pending)
  return <p>Observer mounted</p>
}
declare global { interface Window { axisLiveQaRoot?: Root } }
window.axisLiveQaRoot ??= createRoot(document.getElementById('root')!)
window.axisLiveQaRoot.render(<Harness />)
