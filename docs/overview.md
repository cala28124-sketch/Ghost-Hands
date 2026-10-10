# Overview

Ghost Hands is a mixed-reality training prototype built for Knight Hacks IX, a 36-hour hackathon. It turns an expert's demonstration into a lesson, then guides a learner with a live, translucent ghost hand.

**Loop:** Demonstrate → Extract → Structure → Practice → Recall

**Principle:** Build one reliable end-to-end experience before expanding automation.

## How it works

1. **Capture**: Film an expert doing the task and upload the video on the webpage. It's stored in Neon Object Storage. (`web/`)
2. **Lesson file**: Dominick's script downloads each new video, MediaPipe finds 21 landmarks per hand in every frame, and the moments where the hands pause become steps: joints, finger angles, wrist rotation, and an instruction. The lesson is validated and saved in Neon. (`cv/`, `shared/`, `web/`)
3. **Practice**: The Quest fetches the lesson from the laptop server and shows the expert's pose as see-through ghost hands. The learner's real hands are tracked live, and each finger is colored by how close it is. (`unity/`)
4. **Results**: After a run, the headset posts practice results (time to match, retries, tracking losses, recall) to the server, which stores them in Neon. (`web/`)

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
- Load JSON generated on the laptop, fetched from the laptop lesson server
- Record and calibrate key poses; name, save, preview, load a lesson
- Test on Quest Pro
- Store videos, lessons, and practice results in Neon

**Stretch** (cut first)
- Automatic instruction writing (automatic step segmentation is built, see `cv/`)
- Anchoring to a handle, button, or tool
- Life-size expert head-and-hand replay
- General equipment recognition

**Not building:** accounts, an always-running processing worker, a custom-trained model, face or eye tracking, full room mapping, arbitrary task understanding, elaborate multiplayer.

## Team roles

| Owner | Workstream | Folder | Coordinates with |
|---|---|---|---|
| U1 · Unity: hands and headset | Unity setup, Quest build, live tracking, ghost rendering, wrist alignment, finger-color visuals; owns the scene | `unity/` | U2 on joint data and statuses; Dominick on target format |
| U2 · Unity: logic and flow | C# matcher (smoothing, tolerances, hold timer, hints), lesson loader, state machine, UI, fetching from the server | `unity/` | U1 for joint data; Anthony on endpoints; Dominick on schema |
| Dominick · Media pipeline | Python, OpenCV, MediaPipe, automatic keyframes, JSON exporter, schema | `cv/`, `shared/` | U1 and U2 on schema; Anthony on video endpoints and lesson upload |
| Anthony · Database / web / integration | Laptop lesson server (Node + Express), Neon database and video storage, upload page, schema validator, Git, integration, testing | `web/` | U2 on endpoints; Dominick on video endpoints; everyone on integration |

**Shared interfaces**
- Dominick owns the JSON schema (v2, agreed). Anthony's validator enforces it on every upload; Dominick's script checks the same rules before posting.
- U2's matcher takes target features, live features, and tracking state. It returns per-finger statuses, an overall match, and an optional hint.
- U1 exposes live joint data and ghost rendering with no dependency on lesson UI, and draws the statuses U2's matcher returns.
- The headset and Dominick's script only talk to Anthony's server, never to Neon directly. Endpoints: [architecture.md](architecture.md#lesson-server-web).
- Every workstream commits a mock or test fixture early.

## Timeline and gates

| Hours | Work | Done when |
|---|---|---|
| 0–2 | Confirm Unity/SDK/device path; repo, schema, sample lesson, roles (U1 leads device proof) | Baseline scene builds; schema and coordinates documented |
| 2–6 | Dominick extracts landmarks · U2 matcher and state flow against mocks · U1 live tracking + ghost · Anthony validator, Neon tables, server | Every branch has a demoable piece; interfaces compile or are mocked |
| **Gate 6** | No hand/ghost alignment → stop video automation, fix tracking and rendering | |
| 6–12 | Load JSON, render one target, compare live hand, feedback, hold-to-pass, advance | One pose works on Quest 3S with no code edits between lesson loads |
| **Gate 12** | No reliable single pose → simplify the matcher and the pose | |
| 12–16 | 3–5 poses, progression, tracking-loss handling, recall, results | A complete lesson runs start to results on Quest 3S |
| **Gate 16** | No complete lesson → cut video storage (use local files), object recognition, 3D replay | |
| 16–22 | Upload one expert video; Dominick's script turns it into a lesson with automatic keyframes (manual fixes allowed) | At least one video-assisted lesson imports into XR |
| 22–28 | Lesson review page or object anchor, only if stable; otherwise reliability and polish | Lesson delivery works with bundled fallback, or core reliability improves |
| **Gate 28** | Server or Neon unstable → use bundled JSON | |
| 28–32 | User tests, tolerance tuning, edge cases, visuals, backup recording | Known bugs documented; lesson repeatable; fallback ready |
| **Freeze 32** | No risky merges unless fixing a critical failure | |
| 32–36 | Final device test, rehearse 60–90 s demo, verify team entry | Live demo and backup both ready |

**First 60 minutes**
- [ ] Create the repo and Unity project; name one scene owner
- [ ] U1 proves live hand tracking on Quest 3S
- [ ] Dominick runs MediaPipe on a short clip and saves annotated output
- [ ] U2 implements matching against a mocked target and a mock lesson loader
- [ ] Anthony sets up Git, the Neon project, and a server that serves the mock lesson
- [ ] Agree on schema, handedness, joint order, coordinates, units, tracking-loss behavior
- [ ] Integration checkpoint at hour 2

**Communication rules**
- At each checkpoint, everyone posts: current result, next task, blocker, changed interfaces or files.
- Integrate continuously; avoid long-lived branches.
- Never change the JSON schema without telling all owners.
- Cut scope based on the working build, not optimism.
