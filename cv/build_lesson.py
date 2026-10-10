"""Turn extracted frames + hand-picked keyframes into a lesson JSON, and validate it.

    python build_lesson.py out/clip.frames.json keyframes/clip.keyframes.json   # -> out/<lessonId>.json
    python build_lesson.py --check ../shared/lessons/example.json               # validate any lesson

Keyframes file (written by hand after watching the annotated video):

    {
      "lessonId": "peace-sign",
      "title": "Peace sign",
      "steps": [
        {"t_ms": 1200, "instruction": "Make a fist", "mode": "hold"},
        {"t_ms": 2500, "label": "V", "instruction": "Raise index and middle", "mode": "hold", "holdMs": 800},
        {"t_ms": 3100, "instruction": "Relax", "mode": "pass", "hands": ["right"]}
      ]
    }

Per step: t_ms (or frame), instruction, mode. Optional: label, holdMs (default 700 for hold),
durationMs (default: time since the previous keyframe), hands (default: every hand tracked there).
Each keyframe's pose is the median over a small window of frames around it, to smooth out jitter.
"""

import argparse
import json
import sys
from pathlib import Path

import jsonschema
import numpy as np

from handpose import lesson_hand

CV_DIR = Path(__file__).resolve().parent
SCHEMA_PATH = CV_DIR.parent / "shared" / "lesson.schema.json"


def pose_at(frames, idx, hand, window):
    """Median world landmarks for one hand over frames[idx-window : idx+window], or None if never seen."""
    samples = []
    for f in frames[max(0, idx - window): idx + window + 1]:
        for h in f["hands"]:
            if h["hand"] == hand:
                samples.append(h["world"])
    return np.median(np.array(samples), axis=0) if samples else None


def frame_index(frames, step):
    if "frame" in step:
        return min(int(step["frame"]), len(frames) - 1)
    t = step["t_ms"]
    return min(range(len(frames)), key=lambda i: abs(frames[i]["t_ms"] - t))


def build(frames_data, spec, window):
    frames = frames_data["frames"]
    steps, used_hands, prev_t = [], set(), None
    for n, s in enumerate(spec["steps"], 1):
        idx = frame_index(frames, s)
        t = frames[idx]["t_ms"]
        wanted = s.get("hands", ["left", "right"])
        hands = {}
        for hand in wanted:
            world = pose_at(frames, idx, hand, window)
            if world is not None:
                hands[hand] = lesson_hand(world, hand)
            elif "hands" in s:
                raise SystemExit(f"step {n} (t={t} ms): {hand} hand not tracked near that frame")
        if not hands:
            raise SystemExit(f"step {n} (t={t} ms): no hand tracked near that frame; pick another time")
        used_hands.update(hands)

        step = {"id": s.get("id", f"step-{n:02d}")}
        if "label" in s:
            step["label"] = s["label"]
        step["instruction"] = s["instruction"]
        step["mode"] = s.get("mode", "hold")
        if step["mode"] == "hold":
            step["holdMs"] = s.get("holdMs", 700)
        step["durationMs"] = s.get("durationMs", int(np.clip(t - prev_t, 0, 10000)) if prev_t is not None else 800)
        step["hands"] = dict(sorted(hands.items()))
        steps.append(step)
        prev_t = t

    return {
        "schemaVersion": 2,
        "lessonId": spec["lessonId"],
        "title": spec["title"],
        "hands": sorted(used_hands),
        "coordinateConvention": "wrist-relative",
        "angleUnit": "degrees",
        "positionUnit": "meters",
        "steps": steps,
    }


def validate(lesson):
    """Raise on any problem: the shared JSON Schema, plus the checks it can't express."""
    schema = json.loads(SCHEMA_PATH.read_text())
    jsonschema.validate(lesson, schema)
    problems = []
    for step in lesson["steps"]:
        for hand, data in step["hands"].items():
            if hand not in lesson["hands"]:
                problems.append(f"{step['id']}: uses {hand} hand, not listed in lesson hands")
            if abs(np.linalg.norm(data["wristRotation"]) - 1) > 0.01:
                problems.append(f"{step['id']}.{hand}: wristRotation is not a unit quaternion")
            if np.linalg.norm(data["joints"][0]) > 1e-6:
                problems.append(f"{step['id']}.{hand}: joint 0 (wrist) must be [0, 0, 0]")
    ids = [s["id"] for s in lesson["steps"]]
    if len(ids) != len(set(ids)):
        problems.append("step ids are not unique")
    if problems:
        raise ValueError("\n".join(problems))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("frames", nargs="?", help="out/<name>.frames.json from extract.py")
    ap.add_argument("keyframes", nargs="?", help="keyframes JSON")
    ap.add_argument("--out", default=str(CV_DIR / "out"), help="output folder (default cv/out)")
    ap.add_argument("--window", type=int, default=3, help="frames on each side to median over")
    ap.add_argument("--check", metavar="LESSON", help="only validate an existing lesson file")
    args = ap.parse_args()

    if args.check:
        try:
            validate(json.loads(Path(args.check).read_text()))
        except (jsonschema.ValidationError, ValueError) as e:
            sys.exit(f"INVALID {args.check}:\n{getattr(e, 'message', e)}")
        print(f"OK {args.check}")
        return
    if not (args.frames and args.keyframes):
        ap.error("give frames and keyframes, or --check LESSON")

    lesson = build(json.loads(Path(args.frames).read_text()), json.loads(Path(args.keyframes).read_text()), args.window)
    validate(lesson)
    out = Path(args.out) / f"{lesson['lessonId']}.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(lesson, indent=2))
    print(f"OK, wrote {out} ({len(lesson['steps'])} steps, hands: {', '.join(lesson['hands'])})")


if __name__ == "__main__":
    main()
