// server.js: the Ghost Hands lesson server (Express 5), run on the laptop.
// Headset: GET /lessons, GET /lessons/:id, POST /results. Dominick's script and the webpage: POST /lessons.
// Lessons fall back to shared/lessons/*.json when Neon is unreachable.
// Run from web/: npm start  (listens on port 3000, or PORT from .env.local)

const fs = require('fs');
const os = require('os');
const path = require('path');
const express = require('express');
const db = require('./db');
const { saveLesson } = require('./lessons');
const { validateLesson } = require('./validate');

const PORT = Number(process.env.PORT) || 3000;
const LESSONS_DIR = path.join(__dirname, '..', 'shared', 'lessons');

// Fallback source when Neon is down: the valid lesson files on disk.
function loadLocalLessons() {
  return fs.readdirSync(LESSONS_DIR)
    .filter((f) => f.endsWith('.json'))
    .map((f) => {
      try { return JSON.parse(fs.readFileSync(path.join(LESSONS_DIR, f), 'utf8')); } catch { return null; }
    })
    .filter((lesson) => lesson && validateLesson(lesson).ok);
}

function summary(lesson) {
  return { lessonId: lesson.lessonId, title: lesson.title, hands: lesson.hands, stepCount: lesson.steps.length };
}

const app = express();
app.use(express.json({ limit: '1mb' }));

// Is the server up, and can it reach Neon?
app.get('/health', async (req, res) => {
  try {
    await db.query('select 1');
    res.json({ ok: true, db: 'neon' });
  } catch {
    res.json({ ok: true, db: 'offline' });
  }
});

// Lesson menu for the headset.
app.get('/lessons', async (req, res) => {
  try {
    const { rows } = await db.query('select body from lessons order by lesson_id');
    res.json(rows.map((r) => summary(r.body)));
  } catch (err) {
    console.warn('Neon unavailable, serving local lessons:', err.message);
    res.set('X-Lesson-Source', 'local').json(loadLocalLessons().map(summary));
  }
});

// One full lesson, exactly as validated.
app.get('/lessons/:id', async (req, res) => {
  let lesson;
  try {
    const { rows } = await db.query('select body from lessons where lesson_id = $1', [req.params.id]);
    lesson = rows[0]?.body;
  } catch (err) {
    console.warn('Neon unavailable, serving local lessons:', err.message);
    lesson = loadLocalLessons().find((l) => l.lessonId === req.params.id);
    res.set('X-Lesson-Source', 'local');
  }
  if (!lesson) return res.status(404).json({ errors: [`Lesson "${req.params.id}" not found`] });
  res.json(lesson);
});

// Save a lesson (Dominick's script, webpage). ?videoId=5 also marks that video done.
app.post('/lessons', async (req, res) => {
  const videoId = req.query.videoId;
  if (videoId !== undefined && !/^\d+$/.test(videoId)) {
    return res.status(400).json({ errors: ['videoId must be a whole number'] });
  }

  const result = await saveLesson(req.body);
  if (!result.ok) return res.status(400).json({ errors: result.errors });

  if (videoId) {
    await db.query(
      "update videos set status = 'done', lesson_id = $1, error = null where id = $2",
      [req.body.lessonId, Number(videoId)],
    );
  }
  res.status(201).json({ ok: true, lessonId: req.body.lessonId });
});

// Save one practice run and its steps, all or nothing.
app.post('/results', async (req, res) => {
  const r = req.body;
  if (!r || typeof r.lessonId !== 'string' || !Array.isArray(r.steps)) {
    return res.status(400).json({ errors: ['lessonId (text) and steps (list) are required'] });
  }

  const client = await db.pool.connect();
  try {
    await client.query('begin');
    const { rows } = await client.query(
      `insert into sessions (lesson_id, tester, device, completed, recall_passed)
       values ($1, $2, $3, $4, $5) returning id`,
      [r.lessonId, r.tester ?? null, r.device ?? null, r.completed === true, r.recallPassed ?? null],
    );
    const sessionId = rows[0].id;
    for (const s of r.steps) {
      await client.query(
        `insert into step_results (session_id, step_id, time_to_match_ms, retries, tracking_losses)
         values ($1, $2, $3, $4, $5)`,
        [sessionId, s.stepId, s.timeToMatchMs ?? null, s.retries ?? 0, s.trackingLosses ?? 0],
      );
    }
    await client.query('commit');
    res.status(201).json({ ok: true, sessionId });
  } catch (err) {
    await client.query('rollback').catch(() => {});
    throw err;
  } finally {
    client.release();
  }
});

// Unknown address.
app.use((req, res) => {
  res.status(404).json({ errors: [`No endpoint ${req.method} ${req.path}`] });
});

// Turn any thrown error into a clear JSON answer.
app.use((err, req, res, next) => {
  if (err.type === 'entity.parse.failed') return res.status(400).json({ errors: ['Body is not valid JSON'] });
  if (err.type === 'entity.too.large') return res.status(413).json({ errors: ['Body is larger than 1 MB'] });
  if (err.code === '23503') return res.status(400).json({ errors: ['lessonId does not match a saved lesson'] });
  if (err.code === '23502' || err.code?.startsWith('22')) {
    return res.status(400).json({ errors: [`Invalid value: ${err.message}`] });
  }
  console.error(err);
  res.status(503).json({ errors: ['Database unavailable or failed. Try again.'] });
});

app.listen(PORT, '0.0.0.0', () => {
  const ips = Object.values(os.networkInterfaces()).flat()
    .filter((a) => a.family === 'IPv4' && !a.internal).map((a) => a.address);
  console.log(`Lesson server on port ${PORT}`);
  ips.forEach((ip) => console.log(`  Headset URL: http://${ip}:${PORT}/lessons`));
});
