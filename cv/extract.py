"""Run MediaPipe Hand Landmarker over a video (or webcam) and save per-frame landmarks.

    python extract.py clip.mp4                  # -> out/clip.frames.json, out/clip.annotated.mp4, out/clip.sheet.jpg
    python extract.py 0                         # webcam 0; press q to stop
    python extract.py clip.mp4 --mirrored       # the video is already flipped like a selfie camera

Record the expert from their own point of view (camera at the head or chest, looking down at the hands),
so the video's viewpoint matches the headset's.
"""

import argparse
import json
import math
import urllib.request
from pathlib import Path

import cv2
import mediapipe as mp
import numpy as np
from mediapipe.tasks.python import BaseOptions
from mediapipe.tasks.python.vision import HandLandmarker, HandLandmarkerOptions, HandLandmarksConnections, RunningMode

CV_DIR = Path(__file__).resolve().parent
MODEL_PATH = CV_DIR / "models" / "hand_landmarker.task"
MODEL_URL = "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task"

COLORS = {"left": (255, 160, 60), "right": (60, 200, 255)}  # BGR
CONNECTIONS = [(c.start, c.end) for c in HandLandmarksConnections.HAND_CONNECTIONS]


def ensure_model():
    if not MODEL_PATH.exists():
        MODEL_PATH.parent.mkdir(parents=True, exist_ok=True)
        print(f"Downloading hand landmarker model to {MODEL_PATH} ...")
        urllib.request.urlretrieve(MODEL_URL, MODEL_PATH)
    return MODEL_PATH


def make_landmarker(num_hands, min_conf):
    options = HandLandmarkerOptions(
        base_options=BaseOptions(model_asset_path=str(ensure_model())),
        running_mode=RunningMode.VIDEO,
        num_hands=num_hands,
        min_hand_detection_confidence=min_conf,
        min_hand_presence_confidence=min_conf,
        min_tracking_confidence=min_conf,
    )
    return HandLandmarker.create_from_options(options)


def detect(landmarker, frame_bgr, t_ms, mirrored, min_score):
    rgb = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2RGB)
    result = landmarker.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), t_ms)
    hands = []
    for cats, img_lms, world_lms in zip(result.handedness, result.hand_landmarks, result.hand_world_landmarks):
        label, score = cats[0].category_name.lower(), cats[0].score
        # The Tasks API labels un-mirrored footage correctly (checked on a sample photo); a selfie-mirrored
        # image shows a left hand as a right one, so swap.
        if mirrored:
            label = "left" if label == "right" else "right"
        if score < min_score:
            continue
        hands.append({
            "hand": label,
            "score": round(score, 3),
            "image": [[round(l.x, 5), round(l.y, 5), round(l.z, 5)] for l in img_lms],
            "world": [[round(l.x, 5), round(l.y, 5), round(l.z, 5)] for l in world_lms],
        })
    return hands


def draw(frame, hands, t_ms):
    h, w = frame.shape[:2]
    for hd in hands:
        color = COLORS[hd["hand"]]
        pts = [(int(x * w), int(y * h)) for x, y, _ in hd["image"]]
        for a, b in CONNECTIONS:
            cv2.line(frame, pts[a], pts[b], color, 2, cv2.LINE_AA)
        for i, p in enumerate(pts):
            cv2.circle(frame, p, 4 if i else 6, (255, 255, 255), -1, cv2.LINE_AA)
            cv2.circle(frame, p, 4 if i else 6, color, 1, cv2.LINE_AA)
        cv2.putText(frame, f"{hd['hand']} {hd['score']:.2f}", (pts[0][0] + 8, pts[0][1] + 20),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.6, color, 2, cv2.LINE_AA)
    label = f"t={t_ms} ms"
    cv2.putText(frame, label, (10, 28), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 0, 0), 4, cv2.LINE_AA)
    cv2.putText(frame, label, (10, 28), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 255), 2, cv2.LINE_AA)
    return frame


def contact_sheet(thumbs, cols=4):
    if not thumbs:
        return None
    th, tw = thumbs[0].shape[:2]
    rows = math.ceil(len(thumbs) / cols)
    sheet = np.full((rows * th, cols * tw, 3), 255, dtype=np.uint8)
    for i, t in enumerate(thumbs):
        r, c = divmod(i, cols)
        sheet[r * th:(r + 1) * th, c * tw:(c + 1) * tw] = t
    return sheet


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("source", help="video file, or a webcam index like 0")
    ap.add_argument("--out", default=str(CV_DIR / "out"), help="output folder (default cv/out)")
    ap.add_argument("--mirrored", action="store_true", help="input is selfie-mirrored (swaps MediaPipe's left/right labels)")
    ap.add_argument("--hands", type=int, default=2, help="max hands to track")
    ap.add_argument("--min-conf", type=float, default=0.5, help="detection/tracking confidence")
    ap.add_argument("--min-score", type=float, default=0.6, help="drop hands with a lower handedness score")
    ap.add_argument("--sheet", type=int, default=12, help="frames in the contact sheet")
    ap.add_argument("--no-preview", action="store_true", help="don't show a window")
    args = ap.parse_args()

    webcam = args.source.isdigit()
    cap = cv2.VideoCapture(int(args.source) if webcam else args.source)
    if not cap.isOpened():
        raise SystemExit(f"Can't open {args.source}")
    fps = cap.get(cv2.CAP_PROP_FPS) or 30.0
    if fps <= 1 or fps > 240:
        fps = 30.0
    width, height = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH)), int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    total = int(cap.get(cv2.CAP_PROP_FRAME_COUNT)) if not webcam else 0

    name = f"webcam{args.source}" if webcam else Path(args.source).stem
    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)
    writer = cv2.VideoWriter(str(out_dir / f"{name}.annotated.mp4"), cv2.VideoWriter_fourcc(*"mp4v"), fps, (width, height))
    raw_writer = cv2.VideoWriter(str(out_dir / f"{name}.raw.mp4"), cv2.VideoWriter_fourcc(*"mp4v"), fps, (width, height)) if webcam else None

    sheet_every = max(1, total // args.sheet) if total else int(fps)
    frames, thumbs, i = [], [], 0
    with make_landmarker(args.hands, args.min_conf) as landmarker:
        while True:
            ok, frame = cap.read()
            if not ok:
                break
            if raw_writer:
                raw_writer.write(frame)
            t_ms = int(round(i * 1000 / fps))
            hands = detect(landmarker, frame, t_ms, args.mirrored, args.min_score)
            frames.append({"i": i, "t_ms": t_ms, "hands": hands})
            annotated = draw(frame, hands, t_ms)
            writer.write(annotated)
            if i % sheet_every == 0 and len(thumbs) < args.sheet:
                thumbs.append(cv2.resize(annotated, (320, int(320 * height / width))))
            if not args.no_preview:
                cv2.imshow("ghost-hands extract (q to stop)", annotated)
                if cv2.waitKey(1) & 0xFF == ord("q"):
                    break
            i += 1

    cap.release()
    writer.release()
    if raw_writer:
        raw_writer.release()
    cv2.destroyAllWindows()

    sheet = contact_sheet(thumbs)
    if sheet is not None:
        cv2.imwrite(str(out_dir / f"{name}.sheet.jpg"), sheet)
    data = {"source": str(args.source), "fps": fps, "width": width, "height": height,
            "mirrored": args.mirrored, "frames": frames}
    (out_dir / f"{name}.frames.json").write_text(json.dumps(data))

    tracked = sum(1 for f in frames if f["hands"])
    print(f"{len(frames)} frames, {tracked} with hands ({100 * tracked / max(1, len(frames)):.0f}%)")
    print(f"Wrote {out_dir / name}.frames.json, .annotated.mp4, .sheet.jpg")


if __name__ == "__main__":
    main()
