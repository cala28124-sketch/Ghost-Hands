# lessons

Lesson files: one JSON file per lesson, named `<lessonId>.json`.

## The mock lesson: `example.json`

`example.json` is a **fake lesson for building and testing**. It's not real captured data. Every number in it is a placeholder.

It exists so nobody waits on anybody:

| Who | Uses it to |
|---|---|
| U1 (ghost) | Draw two ghost hands from `joints` and `wristRotation` |
| U2 (matcher, flow) | Load a lesson, match `targetAngles`, step through `hold` and `pass` steps, run recall |
| M (cv/) | See the exact shape the exporter must produce |
| W (web/) | Test the validator, seed Neon, test the server endpoints |

It covers every schema feature on purpose: two hands, a step with only one hand, both step modes, and an optional `label`.

The content is a short Rubik's cube algorithm (`R U R' U'`) in 5 steps:

| Step | Label | Mode | Hands | What happens |
|---|---|---|---|---|
| step-01 | (none) | hold | both | Grip the cube |
| step-02 | R | hold | both | Right wrist rolls the right face up |
| step-03 | U | pass | right only | Right index pushes the top face; the ghost animates, nothing to match |
| step-04 | R' | hold | both | Right wrist rolls back |
| step-05 | U' | hold | both | Left index pushes the top face |

Real lessons from `cv/` replace the numbers. They don't replace the structure.

## File structure (schema version 2)

```
lesson
├── schemaVersion, lessonId, title, hands, coordinateConvention, angleUnit, positionUnit
└── steps[]                      in order
    ├── id, label?, instruction, mode, holdMs?, durationMs
    └── hands
        ├── left?                leave out if this hand doesn't matter in the step
        │   ├── targetAngles     finger bends, for the matcher
        │   ├── joints           21 points, for drawing the ghost
        │   └── wristRotation    which way the hand is turned
        └── right?               same as left
```

`?` = optional.

### Blank template

A lesson with one `hold` step and one `pass` step. Copy it and fill in the values. `joints` is shortened here; a real file needs all 21 points.

```json
{
  "schemaVersion": 2,
  "lessonId": "my-lesson",
  "title": "My lesson",
  "hands": ["left", "right"],
  "coordinateConvention": "wrist-relative",
  "angleUnit": "degrees",
  "positionUnit": "meters",
  "steps": [
    {
      "id": "step-01",
      "label": "R",
      "instruction": "What the learner should do",
      "mode": "hold",
      "holdMs": 700,
      "durationMs": 600,
      "hands": {
        "left": {
          "targetAngles": {
            "thumb":  [0, 0],
            "index":  [0, 0, 0],
            "middle": [0, 0, 0],
            "ring":   [0, 0, 0],
            "pinky":  [0, 0, 0]
          },
          "joints": [[0, 0, 0], [0, 0, 0], "... 21 points total"],
          "wristRotation": [0, 0, 0, 1]
        },
        "right": { "targetAngles": {}, "joints": [], "wristRotation": [0, 0, 0, 1] }
      }
    },
    {
      "id": "step-02",
      "instruction": "Ghost shows this motion; nothing to match",
      "mode": "pass",
      "durationMs": 500,
      "hands": {
        "right": { "targetAngles": {}, "joints": [], "wristRotation": [0, 0, 0, 1] }
      }
    }
  ]
}
```

### Machine-checkable schema: `shared/lesson.schema.json`

The tables in this README describe the rules for people. [`../lesson.schema.json`](../lesson.schema.json) is the same rules as a JSON Schema (draft-07) that code checks automatically. Both sides use the same file:

- **web/ (Node):** `ajv` validates uploads before they're saved to Neon.
- **cv/ (Python):** `pip install jsonschema`, then before exporting:

  ```python
  import json, jsonschema
  schema = json.load(open("shared/lesson.schema.json"))
  jsonschema.validate(json.load(open("out/my-lesson.json")), schema)
  ```

What it rejects:

- missing required fields
- unknown fields, which catches typos and old v1 fields like `hand`
- a `hold` step without `holdMs`
- a step with no hands
- not exactly 21 joints, or the wrong count of finger angles
- out-of-range values: angles outside −90 to 180, joint points more than 0.5 m from the wrist, quaternion values outside −1 to 1

A step's hands should also be listed in the lesson's top-level `hands`. JSON Schema can't check that, so the web validator checks it in code.

If you change this README's tables, change `lesson.schema.json` in the same commit.

### Lesson fields

| Field | Type | Example | Meaning |
|---|---|---|---|
| `schemaVersion` | integer | `2` | Version of these rules. Bump it on any breaking change, and tell every owner |
| `lessonId` | string | `"example"` | Unique ID with no spaces. Matches the file name and is the database key |
| `title` | string | `"Example lesson ..."` | Name shown on the webpage and in the headset |
| `hands` | array | `["left", "right"]` | Which hands the lesson uses: one or both |
| `coordinateConvention` | string | `"wrist-relative"` | All positions are measured from the wrist, not the camera. That's why a pose from a video still lines up in the headset |
| `angleUnit` | string | `"degrees"` | Unit for every angle |
| `positionUnit` | string | `"meters"` | Unit for every `joints` value |
| `steps` | array | | The poses, in lesson order |

### Step fields

| Field | Type | Example | Meaning |
|---|---|---|---|
| `id` | string | `"step-01"` | Unique within the lesson |
| `label` | string, optional | `"R'"` | Short tag such as cube notation |
| `instruction` | string | `"Turn the right face up..."` | Text shown to the learner |
| `mode` | `"hold"` or `"pass"` | `"hold"` | `hold`: the learner must match and hold the pose to advance. `pass`: the ghost animates through it to show motion, with nothing to match |
| `holdMs` | integer, `hold` steps only | `700` | Milliseconds the match must stay steady (about 500–800) |
| `durationMs` | integer | `600` | How long the ghost takes to move from the previous pose into this one |
| `hands` | object | `{ "left": {...}, "right": {...} }` | Target per hand. At least one hand is required |

### Hand fields (inside `hands.left` / `hands.right`)

| Field | Type | Meaning |
|---|---|---|
| `targetAngles` | object | How bent each finger is, in degrees (0 = straight). `thumb` has 2 numbers, `index`, `middle`, `ring`, and `pinky` have 3 each, ordered from the knuckle to the fingertip. **The matcher compares these.** Angles don't depend on hand size |
| `joints` | 21 × `[x, y, z]` | The full hand shape, in meters, with the wrist at `[0, 0, 0]`. Same order as MediaPipe: 0 wrist, 1–4 thumb, 5–8 index, 9–12 middle, 13–16 ring, 17–20 pinky (each from the base to the tip). **Used to draw the ghost** |
| `wristRotation` | `[x, y, z, w]` | Quaternion: which way the hand is turned. Unity's `Quaternion` takes these four numbers directly. A face turn is mostly a wrist twist, so the matcher needs this too |

### Axis conventions used by the mock (to confirm with the team)

- Axes are Unity-style: x = right, y = up, z = forward (away from the viewer).
- In the hand's own frame (`joints`), the palm faces down and the fingers point forward (+z). The left hand is the right hand mirrored across x.
- `wristRotation` is relative to the viewer: the camera in the video, the headset in XR. `[0, 0, 0, 1]` (no rotation) means palm down, fingers pointing forward.

### Exact definitions used by `cv/` (proposed by Dominick, to confirm with U1 and U2)

- **Hand frame** (the axes `joints` are in): forward (+z) = wrist → middle knuckle (landmark 9); right (+x) = across the knuckles, index (5) → pinky (17) on a right hand and pinky → index on a left hand; up (+y) = forward × right, the back of the hand.
- **`wristRotation`** turns the hand frame into the viewer's frame. To draw the ghost: `jointPosition = wristAnchor + wristRotation * joints[i]`.
- **`targetAngles`**: the bend at a joint is the angle between the two bones that meet there (0 = straight).
  - Fingers `[MCP, PIP, DIP]` are measured at landmarks 5/6/7 (index), 9/10/11, 13/14/15 and 17/18/19. The MCP bend uses the wrist → knuckle bone. Positive = toward the palm, negative = bent backwards.
  - Thumb `[MCP, IP]` at landmarks 2 and 3, always positive.
  - The matcher must compute live angles the same way: see `bend_angles` in `cv/ghosthands/handpose.py`.
- **Recording:** film from the expert's point of view (camera at the head or chest). The video's camera stands in for the learner's headset, so another angle makes `wristRotation` wrong in XR.

## Rules

- Every lesson must pass the validator before it's saved or served.
- Changing a field name or meaning is a breaking change: bump `schemaVersion` and tell M, U1, U2, and W.
- Missing or malformed lessons must give a useful error. Unity falls back to its bundled lesson.
