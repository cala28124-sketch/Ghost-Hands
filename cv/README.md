# cv

The laptop-side Python pipeline (OpenCV + MediaPipe Hand Landmarker). It turns an expert's demo video into a schema v2 lesson and posts it to the lesson server.

```
video ─► track hands ─► frames.json ─► automatic keyframes ─► keyframes.json ─► lesson.json ─► validate ─► POST /lessons
          (MediaPipe)                   (pauses where every     (editable: fix      (shared/lesson.schema.json)
                                         hand holds still)       steps, write
                                                                 instructions)
```

MediaPipe only finds landmarks. It doesn't understand the task, so the automatic instructions are placeholders ("Match pose 1"). A person writes the real ones, in the keyframes file or on the review page.

## Setup

```powershell
cd cv
python -m venv .venv
.venv\Scripts\Activate.ps1
pip install -r requirements-dev.txt   # or requirements.txt without the test tools
```

The hand model (`models/hand_landmarker.task`) downloads on first run. `out/` and `models/` are git-ignored.

## Usage

**Process the server's queue.** This is the normal path once videos are uploaded on the webpage:

```powershell
python process.py --server http://<anthony's-laptop-ip>:3000
```

For each pending video it claims the video, downloads it, builds the lesson, and posts it with `?videoId=` (the server marks it done). If a video can't become a lesson, it's marked `failed` with the reason. If the script crashes or you press Ctrl+C, the video goes back to `pending`. You can also set the address once with `$env:GHOSTHANDS_SERVER = "http://...:3000"`.

**Work on a local clip,** step by step:

```powershell
python extract.py clip.mp4               # or: python extract.py 0  (webcam; q or Ctrl+C to stop)
python build_lesson.py out/clip.frames.json
#   -> out/clip.keyframes.json, out/clip.json, out/clip.keyframes.jpg, out/clip.motion.png, out/clip.annotated.mp4
#   Check keyframes.jpg (one picture per step). To fix: edit clip.keyframes.json, then
python build_lesson.py out/clip.frames.json --keyframes out/clip.keyframes.json
python build_lesson.py --check out/clip.json        # same rules as the server
python process.py clip.mp4 --post                   # all of the above in one go, then upload
```

**Tuning keyframes:** open `out/<clip>.motion.png`. It plots each hand's speed, the stillness threshold (dashed), and the pauses that became steps (green). If the expert's holds aren't green, raise `--still-speed`. If moves are being split into extra steps, lower it. Pauses shorter than `--min-still-ms` (300) are ignored.

**Recording the expert:** 10–30 s, bright light, both hands in frame the whole time, and **hold each pose still for about half a second**. Film from the expert's point of view (phone at head or chest height, looking down at the hands). `wristRotation` is measured relative to the camera, so a camera facing the expert, or off to the side, makes the ghost turn the wrong way in the headset.

## Code

| File | Does |
|---|---|
| `extract.py`, `build_lesson.py`, `process.py` | Command-line entry points (the names in the project plan) |
| `ghosthands/video.py` | Frames from a file or webcam, with timestamps that always increase (handles phones' variable frame rate) |
| `ghosthands/landmarks.py` | `HandTracker` (MediaPipe in VIDEO mode), `Hand`, and `resolve_sides`: two hands never share a label |
| `ghosthands/tracking.py` | Tracks a whole video; saves/loads the frames file |
| `ghosthands/handpose.py` | MediaPipe world landmarks → schema hand: `joints`, `wristRotation`, `targetAngles` |
| `ghosthands/keyframes.py` | Hand speed, pause detection, automatic keyframes, the keyframes file |
| `ghosthands/lesson.py` | Builds the lesson; `validate_lesson` (the shared schema, plus the checks in `web/validate.js` and a few more) |
| `ghosthands/review.py` | Annotated video, contact sheets, motion graph |
| `ghosthands/pipeline.py` | Chains the steps; shared by `build_lesson.py` and `process.py` |
| `ghosthands/server.py` | Client for `web/server.js` (only needs the server address) |
| `tests/` | `python -m pytest` (synthetic hands built from `shared/lessons/example.json`, a fake server); `ruff check .` |

## Conventions (what Unity must match)

- **Axes:** Unity's: x right, y up, z forward. MediaPipe's y points down, so it's flipped.
- **`joints`:** 21 points in MediaPipe order, in meters, in the **hand's own frame**: wrist at `[0,0,0]`, fingers along +z, back of the hand +y (palm facing −y). A right hand's thumb is on −x; the left hand is a mirror image. Hand frame axes: forward = wrist → middle knuckle; right = across the knuckles (index → pinky on a right hand); up = forward × right.
- **`wristRotation`:** Unity quaternion `[x, y, z, w]` that turns the hand frame into the camera's frame. To draw the ghost: `position = wristAnchor + rotation * joint`.
- **`targetAngles`:** the bend at each joint in degrees, 0 = straight: the angle between the two bones that meet there, ordered knuckle to fingertip.
  - index/middle/ring/pinky: `[MCP, PIP, DIP]`, measured at landmarks 5-6-7 (index), and so on. The MCP bend uses the wrist → knuckle bone. **Positive = toward the palm**, negative = bent backwards.
  - thumb: `[MCP, IP]` at landmarks 2 and 3, always positive.
  - The matcher has to compute the learner's angles the same way. See `bend_angles` in `ghosthands/handpose.py`.
- **Hands in the lesson:** the hands seen in at least 30% of frames, so a brief mislabel doesn't add a hand. One-handed clips give one-handed lessons.
- **Keyframes:** a step is a pause where every hand in the lesson moves slower than 0.10 m/s (median of 5 frames) for at least 300 ms. The pose is the median of up to 15 frames from the middle of the pause. Consecutive pauses within 10° of each other (fingers and wrist) count as one step. `durationMs` is the time between the end of one pause and the start of the next (800 for the first step). `holdMs` defaults to 700.
- **IDs:** a server video gets `lessonId` = `<file-name>-<videoId>` (e.g. `cube-demo-3`), so re-uploading a file never overwrites an older lesson. A local clip uses just the file name.
