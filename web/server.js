// server.js: the Ghost Hands lesson server (Express 5), run on the laptop.
// Headset: GET /lessons, GET /lessons/:id, POST /results. Dominick's script and the webpage: POST /lessons.
// Videos: the webpage uploads through POST /videos (+ /uploaded); Dominick's script uses GET /videos and PATCH /videos/:id.
// Lessons fall back to shared/lessons/*.json when Neon is unreachable.
//
// ── START THE SERVER ─────────────────────────────────────────────────────────
//   cd web
//   npm install        (first time only)
//   npm start          (port 3000, or PORT from the repo-root .env.local)
//   Stop it with Ctrl+C.
//
// ── ADDRESS TO USE ───────────────────────────────────────────────────────────
//   On the laptop itself:   http://localhost:3000
//   From the headset or another laptop (same wifi):   http://<laptop-ip>:3000
//   The server prints <laptop-ip> when it starts ("Headset URL: ..."). It changes when the wifi changes.
//   Last seen on the hackathon wifi: http://10.32.217.238:3000
//
// ── EXAMPLE REQUESTS (replace localhost with <laptop-ip> from another device) ──
//   Check the server and database:
//     curl http://localhost:3000/health
//     → {"ok":true,"db":"neon"}        ("offline" = Neon unreachable; lessons come from local files)
//
//   List lessons (headset menu):
//     curl http://localhost:3000/lessons
//     → [{"lessonId":"example","title":"Example lesson ...","hands":["left","right"],"stepCount":5}]
//
//   Get one full lesson:
//     curl http://localhost:3000/lessons/example
//     → the whole lesson JSON (shape: shared/lesson.schema.json); 404 {"errors":[...]} if unknown
//
//   Save a lesson (Dominick's script, webpage); add ?videoId=5 to also mark video 5 done:
//     curl -X POST -H "Content-Type: application/json" \
//          --data @../shared/lessons/example.json http://localhost:3000/lessons
//     → 201 {"ok":true,"lessonId":"example"}   or   400 {"errors":["/steps/0 must have required property 'holdMs'"]}
//
//   Save a practice run (headset):
//     curl -X POST -H "Content-Type: application/json" http://localhost:3000/results --data \
//       '{"lessonId":"example","tester":"judge-1","device":"Quest 3S","completed":true,"recallPassed":true,
//         "steps":[{"stepId":"step-01","timeToMatchMs":2400,"retries":0,"trackingLosses":1}]}'
//     → 201 {"ok":true,"sessionId":1}   (tester, device, recallPassed are optional)
//
//   Upload a video (the webpage does these 3 calls):
//     curl -X POST -H "Content-Type: application/json" http://localhost:3000/videos \
//          --data '{"fileName":"cube.mp4","contentType":"video/mp4"}'
//     → 201 {"videoId":1,"uploadUrl":"https://...","contentType":"video/mp4"}
//     curl -X PUT -H "Content-Type: video/mp4" --data-binary @cube.mp4 "<uploadUrl>"     (straight to Neon storage)
//     curl -X POST http://localhost:3000/videos/1/uploaded
//     → {"ok":true,"videoId":1,"status":"pending","size":48211234}
//
//   Process videos (Dominick's script):
//     curl "http://localhost:3000/videos?status=pending"
//     → [{"videoId":1,"fileName":"cube.mp4","status":"pending",...,"downloadUrl":"https://... (15 min)"}]
//     curl -X PATCH -H "Content-Type: application/json" http://localhost:3000/videos/1 --data '{"status":"processing"}'
//     then either POST /lessons?videoId=1 with the lesson (→ video "done")
//     or   --data '{"status":"failed","error":"no hands found"}'
//
//   Every error answer has the same shape: {"errors":["..."]}.
//   400 = fix what you sent · 404 = not found · 409 = wrong state (e.g. not uploaded yet)
//   502 = video storage problem, try again · 503 = database problem, try again.

const fs = require('fs');
const os = require('os');
const path = require('path');
const express = require('express');
const db = require('./db');
const storage = require('./storage');
const { saveLesson } = require('./lessons');
const { validateLesson } = require('./validate');

const PORT = Number(process.env.PORT) || 3000;
const LESSONS_DIR = path.join(__dirname, '..', 'shared', 'lessons');
const VIDEO_TYPES = ['video/mp4', 'video/quicktime', 'video/webm'];
const VIDEO_STATUSES = ['uploading', 'pending', 'processing', 'done', 'failed'];

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

// "5" → 5; anything that isn't a whole number in the database's id range → null.
function parseId(raw) {
  return /^\d{1,10}$/.test(raw) && Number(raw) <= 2147483647 ? Number(raw) : null;
}

// Which status a video may move to from which (PATCH /videos/:id). 'done' only via POST /lessons?videoId=.
const VIDEO_MOVES = {
  processing: ['pending'],                // script claims a queued video
  failed: ['pending', 'processing'],      // script gives up, with a reason
  pending: ['failed', 'processing'],      // retry a failed video, or release a claim
};

// A videos table row → the shape callers get.
function videoInfo(v) {
  return {
    videoId: v.id, fileName: v.file_name, status: v.status, lessonId: v.lesson_id,
    error: v.error, uploadedAt: v.uploaded_at,
  };
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
  const videoId = req.query.videoId === undefined ? undefined : parseId(req.query.videoId);
  if (videoId === null) return res.status(400).json({ errors: ['videoId must be a whole number'] });

  const result = await saveLesson(req.body);
  if (!result.ok) return res.status(400).json({ errors: result.errors });

  if (videoId === undefined) return res.status(201).json({ ok: true, lessonId: req.body.lessonId });

  // Only a video that is queued or being processed can become done.
  const { rowCount } = await db.query(
    `update videos set status = 'done', lesson_id = $1, error = null
     where id = $2 and status in ('pending', 'processing')`,
    [req.body.lessonId, videoId],
  );
  // The lesson is saved either way; videoUpdated is false if the video doesn't exist or isn't pending/processing.
  res.status(201).json({ ok: true, lessonId: req.body.lessonId, videoUpdated: rowCount === 1 });
});

// Save one practice run and its steps, all or nothing.
app.post('/results', async (req, res) => {
  const r = req.body;
  if (!r || typeof r.lessonId !== 'string' || !Array.isArray(r.steps)) {
    return res.status(400).json({ errors: ['lessonId (text) and steps (list) are required'] });
  }
  const badStep = r.steps.findIndex((s) => !s || typeof s !== 'object' || typeof s.stepId !== 'string');
  if (badStep !== -1) {
    return res.status(400).json({ errors: [`steps[${badStep}] must be an object with a stepId (text)`] });
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

// Webpage: start a video upload. Creates the row ('uploading') and returns a one-time upload link.
// The file itself goes straight from the browser to Neon storage, never through this server.
app.post('/videos', async (req, res) => {
  const { fileName, contentType } = req.body ?? {};
  if (typeof fileName !== 'string' || !fileName.trim()) {
    return res.status(400).json({ errors: ['fileName (text) is required'] });
  }
  if (!VIDEO_TYPES.includes(contentType)) {
    return res.status(400).json({ errors: [`contentType must be one of: ${VIDEO_TYPES.join(', ')}`] });
  }

  // Make the link first, so a storage problem never leaves a row behind.
  const key = storage.makeVideoKey(fileName);
  const uploadUrl = await storage.uploadUrl(key, contentType);
  const { rows } = await db.query(
    "insert into videos (file_name, storage_key, status) values ($1, $2, 'uploading') returning id",
    [fileName, key],
  );
  res.status(201).json({ videoId: rows[0].id, uploadUrl, contentType });
});

// Webpage: the upload finished. Checks the file really is in storage, then queues it ('pending').
app.post('/videos/:id/uploaded', async (req, res) => {
  const id = parseId(req.params.id);
  if (id === null) return res.status(400).json({ errors: ['video id must be a whole number'] });

  const { rows } = await db.query('select storage_key, status from videos where id = $1', [id]);
  const video = rows[0];
  if (!video) return res.status(404).json({ errors: [`Video ${id} not found`] });
  if (video.status !== 'uploading') {
    return res.status(409).json({ errors: [`Video ${id} is already ${video.status}`] });
  }

  const file = await storage.fileExists(video.storage_key);
  if (!file.exists) {
    return res.status(409).json({ errors: ['File not found in storage yet. Upload it to uploadUrl first.'] });
  }

  await db.query("update videos set status = 'pending' where id = $1", [id]);
  res.json({ ok: true, videoId: id, status: 'pending', size: file.size });
});

// Dominick's script: GET /videos?status=pending. Webpage: GET /videos (all).
// Every video whose file is in storage gets a fresh 15-minute download link.
app.get('/videos', async (req, res) => {
  const { status } = req.query;
  if (status !== undefined && !VIDEO_STATUSES.includes(status)) {
    return res.status(400).json({ errors: [`status must be one of: ${VIDEO_STATUSES.join(', ')}`] });
  }

  const { rows } = status
    ? await db.query('select * from videos where status = $1 order by id', [status])
    : await db.query('select * from videos order by id');

  const videos = await Promise.all(rows.map(async (v) => ({
    ...videoInfo(v),
    downloadUrl: v.status === 'uploading' ? null : await storage.downloadUrl(v.storage_key),
  })));
  res.json(videos);
});

// Dominick's script: report progress on one video (processing, failed + reason, or back to pending).
// 'done' is only set by POST /lessons?videoId=, so a video can't count as done without a valid lesson.
app.patch('/videos/:id', async (req, res) => {
  const id = parseId(req.params.id);
  if (id === null) return res.status(400).json({ errors: ['video id must be a whole number'] });

  const { status, error } = req.body ?? {};
  if (!VIDEO_MOVES[status]) {
    return res.status(400).json({
      errors: ['status must be processing, failed, or pending (done is set by POST /lessons?videoId=)'],
    });
  }

  // Only change it if the current status allows the move (also stops two scripts claiming one video).
  const { rowCount } = await db.query(
    'update videos set status = $1, error = $2 where id = $3 and status = any($4::text[])',
    [status, status === 'failed' ? String(error ?? 'unknown error') : null, id, VIDEO_MOVES[status]],
  );
  if (rowCount === 0) {
    const { rows } = await db.query('select status from videos where id = $1', [id]);
    if (!rows[0]) return res.status(404).json({ errors: [`Video ${id} not found`] });
    return res.status(409).json({
      errors: [`Video ${id} is ${rows[0].status}; it can only become ${status} from ${VIDEO_MOVES[status].join(' or ')}`],
    });
  }
  res.json({ ok: true, videoId: id, status });
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
  if (err.$metadata) return res.status(502).json({ errors: ['Video storage unavailable or failed. Try again.'] });
  res.status(503).json({ errors: ['Database unavailable or failed. Try again.'] });
});

app.listen(PORT, '0.0.0.0', () => {
  const ips = Object.values(os.networkInterfaces()).flat()
    .filter((a) => a.family === 'IPv4' && !a.internal).map((a) => a.address);
  console.log(`Lesson server on port ${PORT}`);
  ips.forEach((ip) => console.log(`  Headset URL: http://${ip}:${PORT}/lessons`));
});
