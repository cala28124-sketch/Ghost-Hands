# Architecture

## Stack

| Layer | Technology | Responsibility | Folder |
|---|---|---|---|
| Headset app | Unity + C#; Meta XR SDK or OpenXR (decide after device test) | Quest app, XR scene, lesson flow | `unity/` |
| Live hand tracking | Quest hand-tracking API via Unity XR / Meta SDK | Learner's real hand joints and tracking state | `unity/.../Tracking` |
| Video processing | Python 3, OpenCV, MediaPipe Hand Landmarker | Read expert videos from storage, find landmarks for both hands, pick key poses automatically | `cv/` |
| Lesson authoring | Lesson JSON (schema v2) + Python export; automatic keyframes, manual fixes as fallback | Lesson contract between processor and Unity | `cv/`, `shared/` |
| Matcher | C# in Unity | Normalized features, per-finger error, smoothing, thresholds, hold timer | `unity/.../Matching` |
| Visuals / UI | Unity scene, C# UI | Ghost, finger colors, instructions, progress, results | `unity/.../Ghost`, `UI` |
| Lesson server | Node + Express 5 on the laptop; React (Vite) upload page | Upload videos, validate and store lessons, serve lessons to headsets, save results | `web/` |
| Storage | Neon Postgres + Neon Object Storage; local JSON files as fallback | Lessons, videos, practice sessions and results | `web/` |
| Demo capture | Quest casting/recording + external backup | Live demo and fallback video | — |

**Compatibility gate:** before building features, verify the Unity version, Meta XR SDK/OpenXR setup, hand tracking, passthrough (if needed), and the build/deploy workflow on the actual Quest 3S. Quest Pro is the secondary device.

## Data flow

```
LAPTOP / NEON                                                HEADSET
Expert video
  → upload page (web/client) ──→ Neon Object Storage, videos row "pending"
  → Dominick runs cv/process.py: download → OpenCV + MediaPipe
  → automatic keyframes (pauses) → lesson JSON (shared/ schema)
  → POST /lessons → validator → Neon lessons table
  → lesson server (web/) ──── local network ────→  Unity loader
                                                     (fallback: bundled lesson)
                                                   → target ghost
                                                   → live Quest tracking
                                                   → matcher
                                                   → feedback + progression
                                                   → recall + results
  Neon sessions + step_results  ←── POST /results ──
```

**Coordinate rule:** never send raw MediaPipe camera coordinates into Unity. Camera space and headset world space differ. Lessons store wrist-relative target geometry and joint-angle features, and the learner's live hand always comes from the headset's own tracking.

## Video-to-lesson pipeline (`cv/`)

1. Upload a short, well-lit clip on the webpage. It's stored in Neon Object Storage and marked `pending`.
2. Dominick runs `python process.py --server http://<laptop-ip>:3000`. It claims each pending video, downloads it with a signed link, and reads frames with OpenCV; MediaPipe finds landmarks for both hands.
3. Timestamps, handedness, and landmarks are saved; a hand counts only if it's seen in at least 30% of frames.
4. Keyframes are picked automatically: a pause where every hand moves slower than `--still-speed` (default 0.10 m/s; 0.2 worked better on real phone clips) for at least 300 ms becomes a step.
5. Landmarks are normalized to the wrist; finger angles and wrist rotation are computed.
6. The lesson is validated against the schema and posted with `POST /lessons?videoId=N`, which marks the video `done`. If it can't become a lesson, the video is marked `failed` with the reason.
7. Review files in `cv/out/` (keyframe pictures, motion graph). Fix a wrong keyframe by editing the keyframes file and re-running.
8. Load the lesson in Unity and check poses against live headset tracking.

MediaPipe estimates landmarks. It doesn't understand tasks, so instructions start as placeholders ("Match pose 1") and a person writes the real ones.

**Recording a clip that works:** phone propped up, filmed from the expert's point of view looking down at the hands, hands at arm's length with the wrist in frame, each pose frozen for a full second (elbow resting), bright light, 10–30 s.

## Hand matching (`unity/.../Matching`)

| Status | Color | Meaning |
|---|---|---|
| Matched | Green | Within tolerance |
| Close | Amber | Near tolerance |
| Incorrect | Red | Outside tolerance |
| Unavailable | Gray | Tracking lost |

1. Read live hand joints and tracking status.
2. If tracking is poor, show gray and pause the hold timer.
3. Convert live joints to the agreed wrist-relative form; lightly smooth jitter.
4. Compare each finger against the target with per-finger tolerances.
5. Classify each finger as matched, close, incorrect, or unavailable.
6. Give directional hints only when the error data supports them.
7. Require the pose to stay in tolerance for the hold duration (500–800 ms).
8. Advance once, reset the timer, and play a short success pulse or sound.

Don't use one global distance threshold. Tracking varies with orientation, occlusion, lighting, and hand size, so show uncertainty instead of falsely marking a match. Tune with real users.

## Lesson server (`web/`)

- Runs on the laptop (`cd web && npm start`, port 3000). The upload page is served at `/`; everything else is a JSON API.
- **Only the server talks to Neon.** The headset, the webpage, and Dominick's script only know the server's address. Video files are the exception: the server signs a 15-minute link, and the browser uploads (or the script downloads) the file directly to or from Neon storage.
- Every lesson is checked against `shared/lesson.schema.json` before it's saved.
- If Neon can't be reached, lessons are served from `shared/lessons/*.json` (header `X-Lesson-Source: local`) and `/health` reports `offline`.
- Unity always has a bundled lesson to fall back on. A judge should never wait on network transfer or processing.

| Endpoint | Called by | Does |
|---|---|---|
| `GET /health` | anyone | `{ ok, db: "neon" \| "offline" }` |
| `GET /lessons`, `GET /lessons/:id` | headset | lesson list, one full lesson |
| `POST /lessons[?videoId=N]` | Dominick's script | validate + save a lesson; with `videoId`, mark that video `done` |
| `POST /results` | headset | save a practice session and its step results |
| `POST /videos`, `POST /videos/:id/uploaded` | upload page | create the video row + signed upload link; confirm the file arrived (`pending`) |
| `GET /videos[?status=]`, `PATCH /videos/:id` | Dominick's script | pending videos with download links; claim / fail / release |

Request and response examples for every endpoint are at the top of `web/server.js`. Setup is in [`web/README.md`](../web/README.md).

## Repo layout

```
unity/     Quest app (Tracking, Ghost, Matching, Lessons, UI, bundled lessons)
cv/        Python video → lesson pipeline
web/       Laptop lesson server (Node) + Neon tables; web/client = React upload page
shared/    Lesson schema and example lessons
docs/      These docs
```
