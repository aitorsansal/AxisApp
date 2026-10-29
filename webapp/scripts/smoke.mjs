import { readFile } from 'node:fs/promises'

const base = new URL(process.env.AXIS_SMOKE_BASE_URL || 'https://app.axisapp.aitorsansal.com/')
const index = await readFile(new URL('../dist/index.html', import.meta.url), 'utf8')
const entry = index.match(/<script\b[^>]*\btype="module"[^>]*\bsrc="([^"]+)"/i)?.[1]
if (!entry) throw new Error('Built index has no module entry. Run the production build first.')

async function check() {
  for (const route of ['/', '/profile']) {
    const response = await fetch(new URL(route, base), { cache: 'no-store', signal: AbortSignal.timeout(15000) })
    if (!response.ok) throw new Error(`Smoke check failed for ${route}: HTTP ${response.status}.`)
    const html = await response.text()
    if (!response.headers.get('content-type')?.includes('text/html') || !html.includes(entry) || !html.includes('id="root"'))
      throw new Error(`Smoke check failed for ${route}: expected build was not served.`)
  }
  const asset = await fetch(new URL(entry, base), { cache: 'no-store', signal: AbortSignal.timeout(15000) })
  if (!asset.ok || !/javascript/.test(asset.headers.get('content-type') || '') || !(await asset.text()).trim())
    throw new Error('Smoke check failed: the deployed JavaScript entry is unavailable.')
}

for (let attempt = 1; ; attempt++) {
  try {
    await check()
    console.log('Deployment smoke checks passed: /, /profile and the expected JavaScript entry.')
    break
  } catch (error) {
    if (attempt === 3) throw error
    console.log(`Deployment not ready (attempt ${attempt}/3); retrying in 5 seconds.`)
    await new Promise((resolve) => setTimeout(resolve, 5000))
  }
}
