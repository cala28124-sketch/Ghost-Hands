// db.js: the one connection to Neon Postgres, shared by every other file.
// Reads DATABASE_URL from the repo-root .env.local and keeps a small pool of reusable connections.
// Usage: const db = require('./db'); await db.query('select ... where id = $1', [id]);

const path = require('path');

// DATABASE_URL lives in the repo-root .env.local, written by `neon link` and never committed.
require('dotenv').config({ path: path.join(__dirname, '..', '.env.local'), quiet: true });

const { Pool } = require('pg');

if (!process.env.DATABASE_URL) {
  throw new Error('DATABASE_URL missing. Run `neon link` at the repo root to create .env.local');
}

// Explicit full certificate check (also silences pg's sslmode deprecation warning).
const url = new URL(process.env.DATABASE_URL);
url.searchParams.set('sslmode', 'verify-full');

const pool = new Pool({
  connectionString: url.toString(),
  max: 5,
  idleTimeoutMillis: 30_000,
  connectionTimeoutMillis: 5_000, // fail fast so the server can fall back to local lesson files
  query_timeout: 5_000,           // also give up on a query stuck on a dead connection (e.g. wifi changed)
});

// Neon drops idle connections when it scales to zero; log instead of crashing.
pool.on('error', (err) => console.error('Neon pool error:', err.message));

module.exports = {
  pool,
  query: (text, params) => pool.query(text, params),
};
