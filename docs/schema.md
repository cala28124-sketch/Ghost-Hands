# Lesson JSON schema

The lesson file is the contract between `cv/` (writes it), `web/` (stores and serves it), and `unity/` (reads it). Dominick owns it; U1 and U2 approve changes. Anthony's validator enforces it on every upload.

**Never change the schema without telling every owner.** Bump `schemaVersion` on any breaking change.

## Current version: 2 (agreed)

Version 2 supports two-handed, multi-step tasks such as a Rubik's cube algorithm. Each step can target both hands, with finger angles, 21 wrist-relative joints, and a wrist rotation. A step is either `hold` (the learner must match it) or `pass` (the ghost animates through it).

| What | Where |
|---|---|
| Every field, its type and meaning, a blank template, the axis conventions | [`shared/lessons/README.md`](../shared/lessons/README.md) |
| Machine-checkable rules (JSON Schema, used by `web/validate.js` and `cv/ghosthands/lesson.py`) | [`shared/lesson.schema.json`](../shared/lesson.schema.json) |
| Mock lesson to build against (5 steps, `R U R' U'`) | [`shared/lessons/example.json`](../shared/lessons/example.json) |

### Changes from version 1

| v1 | v2 |
|---|---|
| `"hand": "right"` | `"hands": ["left", "right"]` |
| `targetAngles` on the step | `steps[].hands.left/right.targetAngles` |
| (none) | `joints` (21 points), `wristRotation` (quaternion), `positionUnit` |
| (none) | `mode` (`hold` / `pass`), `durationMs`, optional `label` |

## Decisions

Record each decision here once it's made.

- [x] **Approve v2**: agreed by the team
- [ ] **Axis conventions**: confirm the mock's assumptions (x right, y up, z forward; `[0,0,0,1]` = palm down, fingers forward) against Unity and the Meta SDK
- [ ] **Angle definition**: Dominick's proposal (the bend between the two bones at each joint, knuckle to fingertip) is in [`shared/lessons/README.md`](../shared/lessons/README.md#exact-definitions-used-by-cv-proposed-by-dominick-to-confirm-with-u1-and-u2); U1 and U2 to confirm
- [ ] **`targetAngles` vs `joints`**: keep both, or have the matcher compute angles from `joints` and drop `targetAngles`
- [ ] **Left/right mirroring**: how a right-hand lesson works for a left-handed learner, if at all
- [ ] **Tracking loss**: expected behavior when joints are missing (gray status, timer pauses)
- [ ] **Per-finger tolerances**: stored in the lesson, or set in the matcher

## Rules

- Keep a small example lesson in `shared/lessons/` so everyone can work in parallel.
- Missing or malformed JSON must give a useful error or fall back to the bundled lesson.
- When the rules change, update `shared/lessons/README.md` and `shared/lesson.schema.json` in the same commit.
