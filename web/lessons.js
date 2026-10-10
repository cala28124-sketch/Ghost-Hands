const db = require('./db');
const { validateLesson } = require('./validate');

// Validates a lesson, then inserts it into Neon or updates the existing row with the same lessonId.
async function saveLesson(lesson) {
  const { ok, errors } = validateLesson(lesson);
  if (!ok) return { ok: false, errors };

  await db.query(
    `insert into lessons (lesson_id, title, hands, schema_version, body)
     values ($1, $2, $3, $4, $5)
     on conflict (lesson_id) do update
       set title = excluded.title,
           hands = excluded.hands,
           schema_version = excluded.schema_version,
           body = excluded.body,
           updated_at = now()`,
    [lesson.lessonId, lesson.title, lesson.hands, lesson.schemaVersion, JSON.stringify(lesson)],
  );

  return { ok: true, errors: [] };
}

module.exports = { saveLesson };
