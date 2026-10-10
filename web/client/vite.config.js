// vite.config.js: build settings for the Ghost Hands webpage (React).
// Dev: `npm run dev` serves the page on http://localhost:5173 and forwards API calls to the
// lesson server on port 3000, so the page and API act like one address (no CORS needed).
// Demo: `npm run build` writes dist/, which web/server.js serves at http://<laptop-ip>:3000/.

import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

const API = 'http://localhost:3000'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/videos': API,
      '/lessons': API,
      '/results': API,
      '/health': API,
    },
  },
})
