"""Synthetic hands for tests: MediaPipe-style landmarks made from the team's mock lesson, with known poses."""

import json
from pathlib import Path

import numpy as np

from ghosthands.landmarks import Hand
from ghosthands.tracking import TrackedFrame, Tracking

EXAMPLE_LESSON = Path(__file__).resolve().parents[2] / "shared" / "lessons" / "example.json"
WIDTH, HEIGHT = 640, 480
PIXELS_PER_METER = 2000  # how big the synthetic hand appears in the image


def example_lesson() -> dict:
    return json.loads(EXAMPLE_LESSON.read_text(encoding="utf-8"))


def mock_joints(side: str, step: int = 0) -> np.ndarray:
    """21 joints in the hand frame from shared/lessons/example.json."""
    return np.array(example_lesson()["steps"][step]["hands"][side]["joints"])


def rotation_x(degrees: float) -> np.ndarray:
    a = np.radians(degrees)
    return np.array([[1, 0, 0], [0, np.cos(a), -np.sin(a)], [0, np.sin(a), np.cos(a)]])


def rotation_z(degrees: float) -> np.ndarray:
    a = np.radians(degrees)
    return np.array([[np.cos(a), -np.sin(a), 0], [np.sin(a), np.cos(a), 0], [0, 0, 1]])


def make_hand(side: str, joints: np.ndarray, rotation: np.ndarray | None = None,
              position_px: tuple[float, float] = (320, 240), score: float = 0.95) -> Hand:
    """What MediaPipe would report for a hand with these hand-frame joints, turned by `rotation`, at `position_px`."""
    rotation = np.eye(3) if rotation is None else rotation
    viewer = joints @ rotation.T  # hand frame -> viewer (Unity axes), wrist at the origin
    world = viewer * np.array([1, -1, 1])  # Unity -> MediaPipe axes (y down)
    world = world - world.mean(axis=0)  # MediaPipe centers world landmarks on the hand
    image = np.column_stack([
        (position_px[0] + viewer[:, 0] * PIXELS_PER_METER) / WIDTH,
        (position_px[1] - viewer[:, 1] * PIXELS_PER_METER) / HEIGHT,
        viewer[:, 2],
    ])
    return Hand(side, score, image, world)


def make_tracking(per_frame_hands: list[list[Hand]], fps: float = 30, source: str = "synthetic.mp4") -> Tracking:
    frames = [TrackedFrame(i, round(i * 1000 / fps), tuple(hands)) for i, hands in enumerate(per_frame_hands)]
    return Tracking(source, fps, WIDTH, HEIGHT, False, frames)
