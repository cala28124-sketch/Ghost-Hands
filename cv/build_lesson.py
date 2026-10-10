"""Turn a frames file from extract.py into a lesson JSON, and validate lessons.

    python build_lesson.py out/clip.frames.json
        Picks keyframes automatically (pauses where every hand holds still), writes
        out/clip.keyframes.json, out/<lessonId>.json, and review files (keyframes.jpg, motion.png).
    python build_lesson.py out/clip.frames.json --keyframes out/clip.keyframes.json
        Rebuilds from a keyframes file you edited: fix times, write instructions, add labels, set "mode": "pass".
    python build_lesson.py --check ../shared/lessons/example.json
        Validates any lesson with the same rules as the server.

Keyframes file step fields: instruction, t_ms (or frame); optional label, mode ("hold"/"pass"), holdMs,
durationMs, hands (e.g. ["right"]), poseFrames ([first, last] frame averaged into the pose).
"""

import argparse
import json
import sys
from pathlib import Path

from ghosthands.keyframes import KeyframeSettings
from ghosthands.lesson import LessonError, slugify, title_from_name, validate_lesson
from ghosthands.pipeline import make_lesson
from ghosthands.tracking import load_tracking


def check(path: Path) -> int:
    try:
        lesson = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as e:
        print(f"INVALID {path}: can't read it as JSON ({e})")
        return 1
    if errors := validate_lesson(lesson):
        print(f"INVALID {path}:")
        for error in errors:
            print(f"  {error}")
        return 1
    print(f"OK {path}")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("frames", nargs="?", type=Path, help="out/<name>.frames.json from extract.py")
    parser.add_argument("--keyframes", type=Path, help="use this keyframes file instead of automatic keyframes")
    parser.add_argument("--lesson-id", help="default: from the video's file name")
    parser.add_argument("--title", help="default: from the video's file name")
    parser.add_argument("--still-speed", type=float, default=KeyframeSettings.still_speed,
                        help="m/s below which a hand counts as still (default %(default)s; see motion.png)")
    parser.add_argument("--min-still-ms", type=int, default=KeyframeSettings.min_still_ms,
                        help="how long a pause must last to become a step (default %(default)s)")
    parser.add_argument("--no-review", action="store_true", help="skip the review video and images")
    parser.add_argument("--check", type=Path, metavar="LESSON", help="only validate a lesson file")
    args = parser.parse_args()

    if args.check:
        return check(args.check)
    if not args.frames:
        parser.error("give a frames file, or --check LESSON")

    tracking = load_tracking(args.frames)
    name = args.frames.name.removesuffix(".frames.json")
    settings = KeyframeSettings(still_speed=args.still_speed, min_still_ms=args.min_still_ms)
    result = make_lesson(
        tracking, args.frames.with_name(name),
        lesson_id=args.lesson_id or slugify(name),
        title=args.title or title_from_name(name),
        keyframes_path=args.keyframes, settings=settings, review=not args.no_review,
    )

    lesson = result.lesson
    print(f"OK {result.lesson_path}: {len(lesson['steps'])} steps, hands: {', '.join(lesson['hands'])}")
    print(f"  keyframes: {result.keyframes_path}  (edit, then rebuild with --keyframes)")
    for path in result.review_paths:
        print(f"  {path}")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (LessonError, OSError, ValueError) as e:
        # Expected problems (missing or bad input file, invalid lesson): one clear line instead of a traceback.
        sys.exit(f"FAILED: {e}")
