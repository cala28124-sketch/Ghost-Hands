// validate.js: checks a lesson object against shared/lesson.schema.json (schema v2).
// Used by lessons.js before anything is saved to Neon.
// Usage: const { validateLesson } = require('./validate'); validateLesson(lesson) → { ok, errors[] }

const path = require('path');
const Ajv = require('ajv');

const schema = require(path.join(__dirname, '..', 'shared', 'lesson.schema.json'));

const ajv = new Ajv({ allErrors: true });
const check = ajv.compile(schema);

// Returns { ok, errors[] } with readable messages like "/steps/0 must have required property 'holdMs'".
function validateLesson(lesson) {
  const errors = [];

  if (!check(lesson)) {
    for (const e of check.errors) {
      if (e.keyword === 'if') continue; // duplicate of the real error ("must match then schema")
      errors.push(`${e.instancePath || '/'} ${e.message}`);
    }
  }

  // JSON Schema can't express this: step ids must be unique (results are stored per stepId).
  if (lesson && Array.isArray(lesson.steps)) {
    const seen = new Set();
    lesson.steps.forEach((step, i) => {
      if (!step || typeof step.id !== 'string') return;
      if (seen.has(step.id)) errors.push(`/steps/${i}/id "${step.id}" is used by an earlier step`);
      seen.add(step.id);
    });
  }

  // JSON Schema can't express this either: every hand used in a step must be listed in the lesson's "hands".
  if (lesson && Array.isArray(lesson.hands) && Array.isArray(lesson.steps)) {
    lesson.steps.forEach((step, i) => {
      if (!step || typeof step.hands !== 'object' || step.hands === null) return;
      for (const hand of Object.keys(step.hands)) {
        if (!lesson.hands.includes(hand)) {
          errors.push(`/steps/${i}/hands/${hand} is not listed in "hands"`);
        }
      }
    });
  }

  return { ok: errors.length === 0, errors };
}

module.exports = { validateLesson };
