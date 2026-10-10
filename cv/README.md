# cv

The laptop-side Python pipeline (OpenCV + MediaPipe Hand Landmarker). It turns an expert's demo video into lesson JSON.

1. Read a short, well-lit clip and find the 21 hand landmarks in each frame.
2. Save timestamps, handedness, landmarks, and tracking confidence. Skip low-confidence frames.
3. Make an annotated review video or contact sheet.
4. Pick keyframes (by hand is fine) and label each step with an instruction.
5. Convert to wrist-relative points and compute finger angles.
6. Export lesson JSON that matches the schema in `shared/`, and validate it.

MediaPipe only finds landmarks. It doesn't understand tasks, so step naming is manual or assisted.

## Setup

```powershell
python -m venv .venv
.venv\Scripts\Activate.ps1
pip install -r requirements.txt
```

The hand model (`models/hand_landmarker.task`) downloads on first run. `out/` and `models/` are git-ignored.

## Usage

```powershell
python extract.py clip.mp4          # or: python extract.py 0   (webcam, q to stop)
# watch out/clip.annotated.mp4 / out/clip.sheet.jpg, note the times (t_ms is drawn on each frame)
python build_lesson.py out/clip.frames.json keyframes/clip.keyframes.json   # -> out/<lessonId>.json, validated
python build_lesson.py --check ../shared/lessons/example.json               # validate any lesson
```

The keyframes file format is in the docstring at the top of `build_lesson.py`.

| File | Does |
|---|---|
| `extract.py` | Video/webcam → `frames.json` (per frame: time, hand, score, image + world landmarks), annotated video, contact sheet |
| `handpose.py` | World landmarks → schema hand: wrist-relative `joints` in the hand frame, `wristRotation`, `targetAngles` |
| `build_lesson.py` | Keyframes → lesson JSON (median over ±3 frames per keyframe), validated against `shared/lesson.schema.json` |

### Conventions the code implements

- **Recording:** shoot from the expert's point of view (camera at the head or chest), not facing them. Use `--mirrored` only for selfie-flipped footage.
- **Axes:** MediaPipe (x right, y down, z away) → Unity (x right, y up, z forward) by flipping y.
- **Hand frame:** forward = wrist → middle knuckle; right = across the knuckles (index → pinky for a left hand); up = back of the hand. `joints` are expressed in this frame; `wristRotation` maps it to the camera.
- **Angles:** the bend at each joint is the angle between the two bones that meet there; 0 = straight. Fingers: MCP, PIP, DIP, with positive = toward the palm and negative = hyperextended. Thumb: MCP, IP, unsigned. The matcher must compute live angles the same way (see `flexion_angles` in `handpose.py`).
