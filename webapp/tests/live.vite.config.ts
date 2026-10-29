import { defineConfig, mergeConfig, normalizePath } from 'vite'
import { fileURLToPath } from 'node:url'
import appConfig from '../vite.config.ts'

export default mergeConfig(appConfig, defineConfig({
  cacheDir: 'node_modules/.vite-live-fixture',
  server: { host: '127.0.0.1', port: 5176, strictPort: true },
  plugins: [{
    name: 'isolated-live-refresh-harness',
    enforce: 'pre',
    resolveId(source, importer) {
      if (source === './supabaseClient' && importer?.replaceAll('\\', '/').endsWith('/src/lib/useLiveRefresh.ts'))
        return normalizePath(fileURLToPath(new URL('./fixtures/live-supabase.ts', import.meta.url)))
    },
    transformIndexHtml(html) {
      return html.replace('/src/main.tsx', '/tests/fixtures/live-refresh.tsx')
    },
  }],
}))
