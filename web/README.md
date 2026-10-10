# web

A lesson server and simple webpage that run on the laptop, for moving lessons between the laptop and the headsets.

- The page lets you upload, preview, and pick lessons. Headsets fetch lessons over the local network.
- Storage: Neon Postgres. If Neon can't be reached, the server reads `shared/lessons/*.json` instead. No user accounts.
- Uploads are checked against `shared/lesson.schema.json`.
- This is a should-ship feature. The bundled lesson in Unity is always the fallback, and a judge should never wait on the network.

## Setup

1. Install packages: `cd web && npm install`
2. Get the database connection, once per laptop, from the repo root:
   ```
   npm i -g neon@latest && neon login
   neon link --project-id restless-heart-52741937 --branch production -y
   ```
   This writes `DATABASE_URL` (pooled) to `.env.local` at the repo root. Git ignores it. Never commit it or paste it in chat.
3. The server loads `../.env.local` itself. Optional: set `PORT` there (default `3000`).
