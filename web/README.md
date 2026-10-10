# web

The lesson server and upload page. They run on a laptop, and everything else (the headset, the upload page, Dominick's `cv/process.py`) talks only to this server.

- **Server:** Node + Express 5 on port 3000. Stores lessons, videos, and practice results in Neon Postgres, and video files in Neon Object Storage.
- **Upload page:** React (Vite) in `client/`, served by the server at `/`. Pick a video, type a name (it becomes the lesson title), press Send.
- **Validation:** every lesson is checked against `shared/lesson.schema.json` before it's saved.
- **Fallback:** if Neon can't be reached, lessons are served from `shared/lessons/*.json` and `/health` says `offline`. Unity always has a bundled lesson too.

## Setup

1. **Neon keys.** The server needs `.env.local` in the **repo root** (not in `web/`). Either:
   - link this laptop (once): `npm i -g neon@latest && neon login`, then from the repo root `neon link --project-id restless-heart-52741937 --branch production -y`, or
   - get a copy of `.env.local` from Anthony privately.

   It holds `DATABASE_URL` and the `AWS_*` storage keys. Git ignores it. Never commit it, paste it in chat, or screenshot it.
2. **Install and build:**
   ```
   cd web
   npm install
   cd client && npm install && npm run build && cd ..
   ```
3. **Start:** `npm start`. It prints the address for other devices (`Headset URL: http://<laptop-ip>:3000/lessons`). Open `http://localhost:3000/` for the upload page and `http://localhost:3000/health` to check Neon.

First time on a new Neon branch only: `psql "$DATABASE_URL" -f schema.sql` (tables), `npm run seed` (example lesson), `node setup-storage.js` (bucket CORS).

Working on the page: `cd client && npm run dev` serves it at `http://localhost:5173` with live reload and forwards API calls to port 3000. Run `npm run build` afterwards, or port 3000 keeps showing the old version.

## Files

| File | Does |
|---|---|
| `server.js` | All endpoints, the Neon fallback, and serving the page. Request examples for every endpoint are in its header comment |
| `db.js` | The one connection pool to Neon Postgres |
| `storage.js` | Neon Object Storage: signed upload/download links, file-exists check |
| `validate.js` | `validateLesson(lesson)` → `{ ok, errors[] }` using the shared schema plus unique step ids and listed hands |
| `lessons.js` | `saveLesson(lesson)`: validate, then insert or update in Neon |
| `schema.sql` | Neon tables: `lessons`, `videos`, `sessions`, `step_results` (safe to re-run) |
| `seed.js` | `npm run seed`: loads `shared/lessons/*.json` into Neon |
| `setup-storage.js` | One-time bucket CORS setup plus an upload/download check |
| `client/` | React upload page (`src/App.jsx`), built into `client/dist/` |

## Endpoints

| Endpoint | Called by | Does |
|---|---|---|
| `GET /health` | anyone | `{ ok, db: "neon" \| "offline" }` |
| `GET /lessons` | headset | `[{ lessonId, title, hands, stepCount }]` |
| `GET /lessons/:id` | headset | one full lesson |
| `POST /lessons[?videoId=N]` | `cv/process.py` | validate and save; with `videoId`, mark that video `done` |
| `POST /results` | headset | save a practice session and its steps together |
| `POST /videos` | upload page | create the video row (`uploading`) and a 15-minute upload link |
| `POST /videos/:id/uploaded` | upload page | confirm the file is in storage, mark `pending` |
| `GET /videos[?status=]` | `cv/process.py` | videos with 15-minute download links |
| `PATCH /videos/:id` | `cv/process.py` | `processing` (claim), `failed` (with `error`), `pending` (release) |

Errors always look like `{ "errors": ["..."] }`: `400` fix the request, `404` not found, `409` wrong status, `502` storage problem, `503` database problem (try again).
