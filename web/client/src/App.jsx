// App.jsx: the expert-video upload page. One video, one name, one Send button.
// The typed name becomes the file name, which Dominick's cv/process.py turns into the lesson title.
// Flow: POST /videos → PUT the file to the signed Neon link → POST /videos/:id/uploaded (video is now "pending").

import { useState } from 'react'

// Allowed video types (same list as VIDEO_TYPES in server.js), keyed by file extension.
const TYPES = { mp4: 'video/mp4', mov: 'video/quicktime', webm: 'video/webm' }

// videoType: works out the video's MIME type, e.g. 'video/mp4', or undefined if it isn't allowed.
// Purpose: the server and the signed upload link both need the exact type. Some browsers leave
// file.type empty for .mov, so it falls back to the file's extension.
function videoType(file) {
  if (Object.values(TYPES).includes(file.type)) return file.type
  return TYPES[file.name.split('.').pop().toLowerCase()]
}

// api: sends one request to OUR server (an endpoint in web/server.js) and returns its JSON reply.
// Purpose: one shared way to call any endpoint (POST /videos, POST /videos/:id/uploaded, later /lessons).
// On failure it throws the server's own message ({ errors: [...] }) so the status line shows the real reason.
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

// putFile: sends the video file itself to the Neon bucket, using the signed link from POST /videos.
// Purpose: the big file goes browser → Neon directly and never passes through our server.
// Uses XMLHttpRequest (not fetch) because only it reports upload progress (the "Uploading 40%…" line).
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

// App: the upload page. Remembers the picked file, typed name, status message, and whether an upload is running,
// and redraws the page whenever one of them changes.
export default function App() {
  const [file, setFile] = useState(null)
  const [name, setName] = useState('')
  const [status, setStatus] = useState('')
  const [busy, setBusy] = useState(false)

  // send: runs when the form is submitted (Send button or Enter).
  // Purpose: does the three upload requests in order, showing progress, success, or the error in the status line.
  //   1. POST /videos            → our server makes the database row and a signed upload link
  //   2. putFile                 → the file goes to the Neon bucket
  //   3. POST /videos/:id/uploaded → our server checks the file arrived and marks the video "pending"
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

  // What's drawn: title, the form (file picker, name box, Send), and the status line.
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
