"""A replay file: the tracked hands of a video in Unity's axes, for testing the Unity side with real hands.

Unity's end-to-end test plays it as if it were the headset's hand tracking: the expert's own recording
should pass the lesson made from it. Each step lists the frames its pose was built from, so the test can
hold the hands there the way a learner holds a pose.
"""

import json
from pathlib import Path
from typing import Any

from ghosthands.handpose import DEFAULT_CAMERA, Camera, to_unity_axes
from ghosthands.keyframes import Keyframe, keyframe_frames
from ghosthands.landmarks import SIDES
from ghosthands.tracking import Tracking

REPLAY_FORMAT = "ghosthands-replay"
REPLAY_VERSION = 1


def replay_data(tracking: Tracking, lesson_id: str, keyframes: list[Keyframe],
                camera: Camera = DEFAULT_CAMERA) -> dict[str, Any]:
    """Every frame's hands (21 points each, meters, Unity axes, in the learner's view; null when not seen)
    and each step's frames. Use the camera the lesson was built with."""
    to_learner = camera.to_learner()
    frames = []
    for f in tracking.frames:
        frame: dict[str, Any] = {"t_ms": f.t_ms}
        for side in SIDES:
            hand = f.hand(side)
            frame[side] = None if hand is None else [
                [round(float(v), 5) for v in p] for p in to_unity_axes(hand.world) @ to_learner.T
            ]
        frames.append(frame)
    steps = []
    for n, kf in enumerate(keyframes, 1):
        index, first, last = keyframe_frames(tracking, kf)
        steps.append({"id": f"step-{n:02d}", "frame": index, "first": first, "last": last})
    return {
        "format": REPLAY_FORMAT,
        "version": REPLAY_VERSION,
        "lessonId": lesson_id,
        "fps": tracking.fps,
        "frames": frames,
        "steps": steps,
    }


def save_replay(data: dict[str, Any], path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data), encoding="utf-8")
