const fs = require('fs');
const path = require('path');
const db = require('./db');
const { saveLesson } = require('./lessons');

const LESSONS_DIR = path.join(__dirname, '..', 'shared', 'lessons');

// Loads every shared/lessons/*.json into Neon. Safe to re-run: existing lessons are updated.
async function main() {
  const files = fs.readdirSync(LESSONS_DIR).filter((f) => f.endsWith('.json')).sort();
  let saved = 0;
  let failed = 0;

  for (const file of files) {
    let lesson;
    try {
      lesson = JSON.parse(fs.readFileSync(path.join(LESSONS_DIR, file), 'utf8'));
    } catch (err) {
      console.log(`✗ ${file}: not valid JSON (${err.message})`);
      failed++;
      continue;
    }

    const expectedId = path.basename(file, '.json');
    if (lesson.lessonId !== expectedId) {
      console.log(`! ${file}: lessonId is "${lesson.lessonId}", expected "${expectedId}"`);
    }

    const result = await saveLesson(lesson);
    if (result.ok) {
      console.log(`✓ ${file} → ${lesson.lessonId}`);
      saved++;
    } else {
      console.log(`✗ ${file}`);
      result.errors.forEach((e) => console.log(`    ${e}`));
      failed++;
    }
  }

  console.log(`${saved} saved, ${failed} failed`);
  return failed;
}

main()
  .then((failed) => { process.exitCode = failed ? 1 : 0; })
  .catch((err) => { console.error('Seed failed:', err.message); process.exitCode = 1; })
  .finally(() => db.pool.end());
