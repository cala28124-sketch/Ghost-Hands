"""Copy a lesson and its tracked hands into Unity's end-to-end test (a real-hands fixture).

    python export_replay.py out/clip.frames.json out/clip.keyframes.json out/clip.json

Writes <name>.lesson.json and <name>.replay.json to unity/Assets/GhostHands/Tests/Editor/Fixtures/.
Then run Unity's EditMode tests: EndToEndReplayTests plays the replay into the lesson flow, as if the
expert were wearing the headset, and checks Unity agrees with the lesson's angles and wrist rotations.
"""

import argparse
import json
import sys
from pathlib import Path

from ghosthands.keyframes import load_keyframes
from ghosthands.lesson import LessonError, validate_lesson
from ghosthands.replay import replay_data, save_replay
from ghosthands.tracking import load_tracking
from ghosthands.viewpoint import resolve_camera

FIXTURES = Path(__file__).resolve().parents[1] / "unity" / "Assets" / "GhostHands" / "Tests" / "Editor" / "Fixtures"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("frames", type=Path, help="out/<name>.frames.json from extract.py")
    parser.add_argument("keyframes", type=Path, help="out/<name>.keyframes.json the lesson was built from")
    parser.add_argument("lesson", type=Path, help="the lesson JSON from build_lesson.py")
    parser.add_argument("--to", type=Path, default=FIXTURES, help="fixture folder (default: Unity's test fixtures)")
    args = parser.parse_args()

    lesson = json.loads(args.lesson.read_text(encoding="utf-8"))
    if errors := validate_lesson(lesson):
        raise LessonError(f"{args.lesson} is not a valid lesson: {errors[0]}")
    kf_file = load_keyframes(args.keyframes)
    keyframes = kf_file.keyframes
    if len(keyframes) != len(lesson["steps"]):
        raise LessonError(f"{args.keyframes} has {len(keyframes)} steps but the lesson has {len(lesson['steps'])}")

    name = lesson["lessonId"]
    lesson_path = args.to / f"{name}.lesson.json"
    replay_path = args.to / f"{name}.replay.json"
    args.to.mkdir(parents=True, exist_ok=True)
    lesson_path.write_text(json.dumps(lesson, indent=2), encoding="utf-8")
    tracking = load_tracking(args.frames)
    camera, _ = resolve_camera(kf_file.camera, tracking)  # the camera the lesson was built with
    save_replay(replay_data(tracking, name, keyframes, camera), replay_path)
    print(f"OK {lesson_path}\nOK {replay_path}")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (LessonError, OSError, ValueError, KeyError) as e:
        sys.exit(f"FAILED: {e}")
