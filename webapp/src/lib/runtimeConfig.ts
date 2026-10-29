export function assertSupabaseConfig(env: Record<string, unknown>): void {
  const url = env.VITE_SUPABASE_URL
  const key = env.VITE_SUPABASE_PUBLISHABLE_KEY
  if (typeof url !== 'string' || !url.trim())
    throw new Error('Missing VITE_SUPABASE_URL.')
  if (typeof key !== 'string' || !key.trim())
    throw new Error('Missing VITE_SUPABASE_PUBLISHABLE_KEY.')
  let parsed: URL
  try { parsed = new URL(url) } catch { throw new Error('Invalid VITE_SUPABASE_URL.') }
  if (!['https:', 'http:'].includes(parsed.protocol) || parsed.username || parsed.password
    || /YOUR-PROJECT-REF/i.test(url))
    throw new Error('Invalid VITE_SUPABASE_URL.')
  if (/\s/.test(key) || key === 'sb_publishable_...')
    throw new Error('Invalid VITE_SUPABASE_PUBLISHABLE_KEY.')
  if (key.startsWith('sb_secret_'))
    throw new Error('VITE_SUPABASE_PUBLISHABLE_KEY must be a public client key.')
}
