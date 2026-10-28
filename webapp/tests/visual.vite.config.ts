import { defineConfig, mergeConfig } from 'vite'
import appConfig from '../vite.config.ts'

// Visual review only: serves the app against tests/fixtures/visual-api.js (mocked, read-only
// data). Never use this config for a production build or deploy.
export default mergeConfig(appConfig, defineConfig({
  server: { host: '127.0.0.1', port: 5175, strictPort: true },
  plugins: [{
    name: 'isolated-visual-fixture',
    transformIndexHtml() {
      return [{ tag: 'script', attrs: { type: 'module', src: '/tests/fixtures/visual-api.js' }, injectTo: 'head-prepend' }]
    },
  }],
}))
