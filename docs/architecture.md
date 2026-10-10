# Architecture

## Stack

| Layer | Technology | Responsibility | Folder |
|---|---|---|---|
| Headset app | Unity + C#; Meta XR SDK or OpenXR (decide after device test) | Quest app, XR scene, lesson flow | `unity/` |
| Live hand tracking | Quest hand-tracking API via Unity XR / Meta SDK | Learner's real hand joints and tracking state | `unity/.../Tracking` |
| Video processing | Python 3, OpenCV, MediaPipe Hand Landmarker | Read expert video, find landmarks, sample key poses | `cv/` |
| Lesson authoring | JSON + Python export; manual labels at first | Lesson contract between processor and Unity | `cv/`, `shared/` |
| Matcher | C# in Unity | Normalized features, per-finger error, smoothing, thresholds, hold timer | `unity/.../Matching` |
| Visuals / UI | Unity scene, C# UI | Ghost, finger colors, instructions, progress, results | `unity/.../Ghost`, `UI` |
| Lesson transfer *(should ship)* | Local HTTP server on the laptop + simple webpage + local storage | Move lessons to headsets on the same network | `web/` |
| Demo capture | Quest casting/recording + external backup | Live demo and fallback video | — |

**Compatibility gate:** before building features, verify the Unity version, Meta XR SDK/OpenXR setup, hand tracking, passthrough (if needed), and the build/deploy workflow on the actual Quest 3S. Quest Pro is the secondary device.

## Data flow

```
LAPTOP                                                      HEADSET
Expert video
  → OpenCV + MediaPipe (cv/)
  → key-pose landmarks
  → normalized features + step labels
  → versioned lesson JSON (shared/ schema)
  → [optional] web/ server  ──── local network ────→  Unity loader
                                                        (fallback: bundled lesson)
                                                      → target ghost
                                                      → live Quest tracking
                                                      → matcher
                                                      → feedback + progression
                                                      → recall + results
```

**Coordinate rule:** never send raw MediaPipe camera coordinates into Unity. Camera space and headset world space differ. Lessons store wrist-relative target geometry and/or joint-angle features, and the learner's live hand always comes from the headset's own tracking.

## Video-to-lesson pipeline (`cv/`)

1. Use a short, well-lit clip with hands clearly visible.
2. Read frames with OpenCV; find landmarks with MediaPipe Hand Landmarker.
3. Save timestamps, handedness, landmarks, and tracking confidence.
4. Generate an annotated review video or contact sheet.
5. Pick keyframes manually at first; label step names and instructions.
6. Normalize landmarks to the wrist and hand scale; compute finger angles.
7. Export versioned JSON and validate it against the schema.
8. Load it in Unity and check the poses against live headset tracking.

MediaPipe estimates landmarks. It doesn't understand tasks or reliably come up with instructions, so treat segmentation and step naming as manual or assisted until proven.

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

## Lesson transfer (`web/`)

- Runs on the laptop. A simple page uploads, previews, and lists lessons; headsets fetch them over the local network.
- Storage stays local (a folder of JSON files, or SQLite if queries are needed). No cloud database, no accounts.
- Uploads are validated against the schema in `shared/`.
- Unity always has a bundled lesson to fall back on. A judge should never wait on network transfer or processing.

## Repo layout

```
unity/     Quest app (Tracking, Ghost, Matching, Lessons, UI, bundled lessons)
cv/        Python video → lesson pipeline
web/       Laptop lesson server + page
shared/    Lesson schema and example lessons
docs/      These docs
```
