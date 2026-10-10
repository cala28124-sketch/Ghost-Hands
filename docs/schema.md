# Lesson JSON schema

The lesson file is the contract between `cv/` (writes it), `web/` (stores and serves it), and `unity/` (reads it). Freeze it in hours 0–2. P1 owns it; P2 and P3 approve it before implementation.

**Never change the schema without telling every owner.** Bump `schemaVersion` on any breaking change.

## Fields

| Field | Meaning |
|---|---|
| `schemaVersion` | Integer version of this contract |
| `lessonId` | Stable unique ID |
| `title` | Display name |
| `hand` | `"left"` or `"right"` |
| `coordinateConvention` | `"wrist-relative"` |
| `angleUnit` | `"degrees"` |
| `steps[]` | Ordered steps |
| `steps[].id` | Step ID |
| `steps[].instruction` | Text shown to the learner |
| `steps[].targetAngles` | Per-finger joint angles (thumb, index, middle, ring, pinky) |
| `steps[].holdMs` | How long the pose must be held (about 700) |
| `anchor` *(optional, stretch)* | Object the lesson is placed relative to |

## Example

Illustrative only. Values are placeholders to be captured and calibrated.

```json
{
  "schemaVersion": 1,
  "lessonId": "example-grip",
  "title": "Example Grip",
  "hand": "right",
  "coordinateConvention": "wrist-relative",
  "angleUnit": "degrees",
  "steps": [
    {
      "id": "step-01",
      "instruction": "Form the target grip",
      "targetAngles": {
        "thumb":  [20, 35],
        "index":  [55, 70, 20],
        "middle": [60, 75, 25],
        "ring":   [58, 72, 22],
        "pinky":  [50, 65, 18]
      },
      "holdMs": 700
    }
  ]
}
```

## To decide in hours 0–2

Record each decision here once it's made.

- [ ] **Joint order**: which joints each angle in `targetAngles` refers to, in order
- [ ] **Angle definition**: how each angle is measured (flexion between which bones)
- [ ] **Left/right mirroring**: how a right-hand lesson works for a left-handed learner, if at all
- [ ] **Tracking loss**: expected behavior when joints are missing (gray status, timer pauses)
- [ ] **Ghost geometry**: whether to also store wrist-relative joint positions or rotations for more reliable ghost rendering
- [ ] **Per-finger tolerances**: stored in the lesson, or set in the matcher

## Rules

- Keep a small example lesson in `shared/lessons/` so everyone can work in parallel.
- Missing or malformed JSON must give a useful error or fall back to the bundled lesson.
