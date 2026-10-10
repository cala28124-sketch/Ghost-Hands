# Overview

Ghost Hands is a mixed-reality training prototype built for Knight Hacks IX, a 36-hour hackathon. It turns an expert's demonstration into a lesson, then guides a learner with a live, translucent ghost hand.

**Loop:** Demonstrate → Extract → Structure → Practice → Recall

**Principle:** Build one reliable end-to-end experience before expanding automation.

## How it works

1. **Capture**: Film an expert doing the task. MediaPipe finds 21 hand landmarks in every frame. (`cv/`)
2. **Lesson file**: Pick the key moments and save each as a step: finger angles plus an instruction. This JSON file is the handoff between laptop and headset. (`shared/`)
3. **Practice**: The Quest shows the expert's pose as a see-through ghost. The learner's real hand is tracked live, and each finger is colored by how close it is. (`unity/`)
4. **Transfer** *(should ship)*: A server on the laptop moves lessons to the headsets over the local network. (`web/`)

## What the learner sees

1. **Find the ghost**: fingers that are far off show red.
2. **Adjust**: close fingers turn amber, matched ones green.
3. **Hold**: all green for about 700 ms moves to the next step.
4. **Recall**: the ghost disappears, and the learner forms the pose from memory.

## Scope

**Must ship** (the MVP judges see)
- Unity XR app running on the Quest 3S used for judging
- One lesson with 3–5 target poses
- Live hand tracking, wrist-aligned ghost, visible real hand
- Per-finger feedback: matched, close, incorrect, unavailable
- Stable-hold confirmation, progression, completion screen, recall round
- One video-processing path that produces or assists lesson data (manual annotation is fine)
- Bundled local lesson and recorded backup demo

**Should ship** (only if the core is stable)
- Load JSON generated on the laptop
- Record and calibrate key poses; name, save, preview, load a lesson
- Test on Quest Pro
- Transfer JSON between devices through a local HTTP endpoint on the laptop

**Stretch** (cut first)
- Automatic step segmentation and instruction generation
- Anchoring to a handle, button, or tool
- Life-size expert head-and-hand replay
- General equipment recognition

**Not building:** accounts, cloud databases, a custom-trained model, face or eye tracking, full room mapping, arbitrary task understanding, elaborate multiplayer.

## Team roles

| Owner | Workstream | Folder | Coordinates with |
|---|---|---|---|
| P1 · Video / CV / data | Python, OpenCV, MediaPipe, keyframes, JSON export | `cv/`, `shared/` | P2 on schema; P3 on coordinates |
| P2 · Matcher / feedback | Comparison, smoothing, tolerances, hold timer, hints | `unity/.../Matching` | P3 for joint data; P4 for UI events |
| P3 · XR / tracking / ghost | Unity setup, Quest build, tracking, ghost rendering | `unity/.../Tracking`, `Ghost` | P2 on target format; P4 on scene/UI |
| P4 · Product / flow / integration | UI, lesson state machine, loader, recall/results, Git, demo | `unity/.../Lessons`, `UI`, `web/` | Everyone; owns interfaces and integration |

**Shared interfaces**
- P1 owns the JSON schema; P2 and P3 approve it before implementation.
- The matcher takes target features, live features, and tracking state. It returns per-finger statuses, an overall match, and an optional hint.
- Tracking exposes live joint data, and Ghost exposes rendering, with no dependency on lesson UI.
- P4's state machine calls the loader, renderer, matcher, and UI through agreed interfaces.
- Every workstream commits a mock or test fixture early.

## Timeline and gates

| Hours | Work | Done when |
|---|---|---|
| 0–2 | Confirm Unity/SDK/device path; repo, schema, sample lesson, roles (P3 leads device proof) | Baseline scene builds; schema and coordinates documented |
| 2–6 | P1 extracts landmarks · P2 matcher against mocks · P3 live tracking + ghost · P4 UI and state mock | Every branch has a demoable piece; interfaces compile or are mocked |
| **Gate 6** | No hand/ghost alignment → stop video automation, fix tracking and rendering | |
| 6–12 | Load JSON, render one target, compare live hand, feedback, hold-to-pass, advance | One pose works on Quest 3S with no code edits between lesson loads |
| **Gate 12** | No reliable single pose → simplify the matcher and the pose | |
| 12–16 | 3–5 poses, progression, tracking-loss handling, recall, results | A complete lesson runs start to results on Quest 3S |
| **Gate 16** | No complete lesson → cut recording, relay, object recognition, 3D replay | |
| 16–22 | Process one expert video to JSON (manual keyframe labels allowed) | At least one video-assisted lesson imports into XR |
| 22–28 | JSON relay or object anchor only if stable; otherwise reliability and polish | Transfer works with bundled fallback, or core reliability improves |
| **Gate 28** | Relay unstable → use bundled JSON | |
| 28–32 | User tests, tolerance tuning, edge cases, visuals, backup recording | Known bugs documented; lesson repeatable; fallback ready |
| **Freeze 32** | No risky merges unless fixing a critical failure | |
| 32–36 | Final device test, rehearse 60–90 s demo, verify team entry | Live demo and backup both ready |

**First 60 minutes**
- [ ] Create the repo and Unity project; name one scene owner
- [ ] P3 proves live hand tracking on Quest 3S
- [ ] P1 runs MediaPipe on a short clip and saves annotated output
- [ ] P2 implements matching against a mocked target
- [ ] P4 builds the state flow and a mock lesson loader
- [ ] Agree on schema, handedness, joint order, coordinates, units, tracking-loss behavior
- [ ] Integration checkpoint at hour 2

**Communication rules**
- At each checkpoint, everyone posts: current result, next task, blocker, changed interfaces or files.
- Integrate continuously; avoid long-lived branches.
- Never change the JSON schema without telling all owners.
- Cut scope based on the working build, not optimism.
