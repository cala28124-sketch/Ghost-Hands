// App.jsx: the expert-video upload page. One video, one name, one Send button.
// The typed name becomes the file name, which Dominick's cv/process.py turns into the lesson title.
// Flow: POST /videos → PUT the file to the signed Neon link → POST /videos/:id/uploaded (video is now "pending").

import { useState } from 'react'

// The server only accepts these types; some browsers leave file.type empty for .mov, so fall back to the extension.
const TYPES = { mp4: 'video/mp4', mov: 'video/quicktime', webm: 'video/webm' }

function videoType(file) {
  if (Object.values(TYPES).includes(file.type)) return file.type
  return TYPES[file.name.split('.').pop().toLowerCase()]
}

// Calls the lesson server and throws its first error message ({ errors: [...] }) on failure.
async function api(method, path, body) {
  const res = await fetch(path, {
    method,
    headers: body ? { 'Content-Type': 'application/json' } : {},
    body: body ? JSON.stringify(body) : undefined,
  })
  const data = await res.json().catch(() => ({}))
  if (!res.ok) throw new Error(data.errors?.[0] ?? `Server answered ${res.status}`)
  return data
}

// Uploads the file straight to Neon storage. XMLHttpRequest (not fetch) so we get progress events.
function putFile(url, file, contentType, onProgress) {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('PUT', url)
    xhr.setRequestHeader('Content-Type', contentType)
    xhr.upload.onprogress = (e) => e.lengthComputable && onProgress(Math.round((e.loaded / e.total) * 100))
    xhr.onload = () => (xhr.status < 300 ? resolve() : reject(new Error(`Storage upload failed (${xhr.status})`)))
    xhr.onerror = () => reject(new Error('Storage upload failed (network)'))
    xhr.send(file)
  })
}

export default function App() {
  const [file, setFile] = useState(null)
  const [name, setName] = useState('')
  const [status, setStatus] = useState('')
  const [busy, setBusy] = useState(false)

  // Runs the three upload steps and reports each one in the status line.
  async function send(e) {
    e.preventDefault()
    const contentType = videoType(file)
    if (!contentType) return setStatus('Error: video must be .mp4, .mov, or .webm')

    setBusy(true)
    try {
      const ext = file.name.split('.').pop().toLowerCase()
      setStatus('Starting upload…')
      const { videoId, uploadUrl } = await api('POST', '/videos', { fileName: `${name.trim()}.${ext}`, contentType })
      await putFile(uploadUrl, file, contentType, (pct) => setStatus(`Uploading ${pct}%…`))
      await api('POST', `/videos/${videoId}/uploaded`)
      setStatus(`Uploaded ✓ (video ${videoId}, waiting for processing)`)
      setFile(null)
      setName('')
      e.target.reset()
    } catch (err) {
      setStatus(`Error: ${err.message}`)
    } finally {
      setBusy(false)
    }
  }

  return (
    <main>
      <h1>Ghost Hands</h1>
      <p className="sub">Upload an expert video</p>
      <form onSubmit={send}>
        <input type="file" accept=".mp4,.mov,.webm,video/*" onChange={(e) => setFile(e.target.files[0] ?? null)} />
        <input
          type="text"
          placeholder="Name, e.g. Rubik's cube R U R' U'"
          value={name}
          onChange={(e) => setName(e.target.value)}
        />
        <button type="submit" disabled={!file || !name.trim() || busy}>
          Send
        </button>
      </form>
      {status && <p className={status.startsWith('Error') ? 'status error' : 'status'}>{status}</p>}
    </main>
  )
}
