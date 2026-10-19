import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // In dev, the browser only talks to Vite (localhost:5173). Vite forwards
    // /api/* to the ASP.NET Core API, so the app uses relative URLs and there
    // is no cross-origin request (no CORS setup needed). In production the API
    // serves the built files itself, so the same relative URLs keep working.
    proxy: {
      '/api': 'http://localhost:5080',
    },
  },
})
