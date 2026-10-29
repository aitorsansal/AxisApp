import { createClient } from '@supabase/supabase-js'
import { assertSupabaseConfig } from './runtimeConfig'

const url = import.meta.env.VITE_SUPABASE_URL
const publishableKey = import.meta.env.VITE_SUPABASE_PUBLISHABLE_KEY

assertSupabaseConfig(import.meta.env)

export const supabase = createClient(url, publishableKey)
