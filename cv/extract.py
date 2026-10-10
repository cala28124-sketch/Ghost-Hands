"""Find the hands in every frame of a video (or webcam) and save them for build_lesson.py.

    python extract.py clip.mp4             -> out/clip.frames.json, out/clip.annotated.mp4, out/clip.sheet.jpg
    python extract.py 0                    webcam 0 (q or Ctrl+C to stop); also saves out/webcam0.raw.mp4
    python extract.py clip.mp4 --mirrored  footage flipped like a selfie camera (swaps left/right)
"""

import argparse
import dataclasses
import sys
from pathlib import Path

import cv2

from ghosthands.landmarks import HandTracker
from ghosthands.review import draw_hands, open_video_writer, render_review
from ghosthands.tracking import save_tracking, track
from ghosthands.video import VideoSource

OUT_DIR = Path(__file__).resolve().parent / "out"
WINDOW = "Ghost Hands extract (q to stop)"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("source", help="video file, or a webcam number like 0")
    parser.add_argument("--out", type=Path, default=OUT_DIR, help="output folder (default: cv/out)")
    parser.add_argument("--mirrored", action="store_true", help="footage is selfie-mirrored")
    parser.add_argument("--min-confidence", type=float, default=0.5, help="MediaPipe detection/tracking threshold")
    parser.add_argument("--no-preview", action="store_true", help="don't show the live window")
    args = parser.parse_args()

    webcam = args.source.isdigit()
    name = f"webcam{args.source}" if webcam else Path(args.source).stem
    stem = args.out / name
    raw_path = stem.with_name(name + ".raw.mp4")

    with VideoSource(int(args.source) if webcam else args.source) as video, \
            HandTracker(mirrored=args.mirrored, min_confidence=args.min_confidence) as tracker:
        # A webcam can't be replayed, so record it; the annotated review is rendered from that recording.
        raw = open_video_writer(raw_path, video.info.fps, (video.info.width, video.info.height)) if webcam else None

        def on_frame(frame, hands) -> bool:
            if raw:
                raw.write(frame.image)
            if args.no_preview:
                return True
            preview = draw_hands(frame.image, hands, frame.t_ms)
            cv2.imshow(WINDOW, cv2.flip(preview, 1) if webcam else preview)  # mirror the webcam view only
            return cv2.waitKey(1) & 0xFF != ord("q")

        try:
            tracking = track(video, tracker, on_frame)
        finally:
            if raw:
                raw.release()
            cv2.destroyAllWindows()

    if webcam:
        tracking = dataclasses.replace(tracking, source=str(raw_path))
    frames_path = stem.with_name(name + ".frames.json")
    save_tracking(tracking, frames_path)
    review = render_review(tracking, stem)

    seen = sum(1 for f in tracking.frames if f.hands)
    print(f"{len(tracking.frames)} frames, hands in {seen} ({100 * seen / max(1, len(tracking.frames)):.0f}%); "
          f"left {tracking.coverage('left'):.0%}, right {tracking.coverage('right'):.0%}")
    for path in (frames_path, review.video, review.sheet):
        if path:
            print(f"  {path}")
    print(f"Next: python build_lesson.py {frames_path}")


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError) as e:
        # Expected problems (missing or unreadable video): one clear line instead of a traceback.
        sys.exit(f"FAILED: {e}")
