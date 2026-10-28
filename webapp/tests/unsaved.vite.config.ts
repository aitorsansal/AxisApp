import { defineConfig, mergeConfig } from 'vite'
import appConfig from '../vite.config.ts'

export default mergeConfig(appConfig, defineConfig({
  server: { host: '127.0.0.1', port: 5174, strictPort: true },
  plugins: [{
    name: 'isolated-unsaved-editor-fixture',
    transformIndexHtml() {
      return [{ tag: 'script', attrs: { type: 'module', src: '/tests/fixtures/unsaved-api.js' }, injectTo: 'head-prepend' }]
    },
  }],
}))
