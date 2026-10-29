import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'
import { assertSupabaseConfig } from './src/lib/runtimeConfig.ts'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), {
    name: 'required-client-configuration',
    configResolved(config) {
      if (config.command === 'build') assertSupabaseConfig(config.env)
    },
  }],
})
