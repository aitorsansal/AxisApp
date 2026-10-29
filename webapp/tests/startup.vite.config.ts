import { defineConfig, mergeConfig } from 'vite'
import appConfig from '../vite.config.ts'

export default mergeConfig(appConfig, defineConfig({
  cacheDir: 'node_modules/.vite-startup-fixture',
  server: { host: '127.0.0.1', port: 5175, strictPort: true },
  define: {
    'import.meta.env.VITE_SUPABASE_URL': JSON.stringify(''),
    'import.meta.env.VITE_SUPABASE_PUBLISHABLE_KEY': JSON.stringify(''),
  },
}))
