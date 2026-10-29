export function showStartupFailure(): void {
  let language = navigator.language?.slice(0, 2)
  try { language = localStorage.getItem('axis_language_override') || language } catch { /* Use browser language. */ }
  const spanish = language === 'es'
  document.documentElement.lang = spanish ? 'es' : 'en'
  const panel = document.createElement('main')
  panel.className = 'startup-message'
  panel.setAttribute('role', 'alert')
  const title = document.createElement('h1')
  title.textContent = spanish ? 'Axis no ha podido iniciarse' : 'Axis could not start'
  title.tabIndex = -1
  const message = document.createElement('p')
  message.textContent = spanish
    ? 'Vuelve a intentarlo. Si el problema continúa, inténtalo más tarde.'
    : 'Try again. If the problem continues, please try again later.'
  const retry = document.createElement('button')
  retry.className = 'btn btn-primary'
  retry.textContent = spanish ? 'Reintentar' : 'Try again'
  retry.addEventListener('click', () => window.location.reload())
  panel.append(title, message, retry)
  document.getElementById('root')?.replaceChildren(panel)
  title.focus()
}
