// main.jsx: entry point. Mounts <App /> (the upload page) into index.html's #root div.
// Loaded by index.html; nothing else imports it.

import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.jsx'

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
