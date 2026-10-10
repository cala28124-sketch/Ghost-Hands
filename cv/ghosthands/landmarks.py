"""Find hands in a frame with MediaPipe Hand Landmarker (21 landmarks per hand)."""

import dataclasses
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from typing import Literal

import cv2
import mediapipe as mp
import numpy as np
from mediapipe.tasks.python import BaseOptions
from mediapipe.tasks.python.vision import HandLandmarker, HandLandmarkerOptions, RunningMode

Side = Literal["left", "right"]
SIDES: tuple[Side, Side] = ("left", "right")

MODEL_PATH = Path(__file__).resolve().parent.parent / "models" / "hand_landmarker.task"
MODEL_URL = "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task"


def other_side(side: Side) -> Side:
    return "left" if side == "right" else "right"


@dataclass(frozen=True, eq=False)
class Hand:
    side: Side
    score: float        # MediaPipe's confidence in the left/right label, 0-1. Not landmark accuracy.
    image: np.ndarray   # (21, 3): x, y as fractions of the frame width/height; z is relative depth
    world: np.ndarray   # (21, 3): meters, origin near the hand's center; x right, y down, z away from camera


def resolve_sides(hands: list[Hand]) -> list[Hand]:
    """Make sure no two hands share a side.

    MediaPipe labels each hand independently, so overlapping hands can both come back "right".
    The more confident hand keeps its label; the other gets the opposite one.
    """
    if len(hands) != 2 or hands[0].side != hands[1].side:
        return sorted(hands, key=lambda h: h.side)
    keep, flip = sorted(hands, key=lambda h: h.score, reverse=True)
    flipped = dataclasses.replace(flip, side=other_side(flip.side), score=round(1 - flip.score, 3))
    return sorted([keep, flipped], key=lambda h: h.side)


def ensure_model(path: Path = MODEL_PATH) -> Path:
    """Download the model once. Writes to a temp file first, so an interrupted download can't leave a broken model."""
    if not path.exists():
        path.parent.mkdir(parents=True, exist_ok=True)
        partial = path.with_suffix(".part")
        print(f"Downloading hand landmarker model to {path} ...")
        urllib.request.urlretrieve(MODEL_URL, partial)
        partial.replace(path)
    return path


class HandTracker:
    """MediaPipe Hand Landmarker in VIDEO mode: hands are tracked from frame to frame.

    Timestamps passed to detect() must strictly increase (VideoSource guarantees this).
    """

    def __init__(self, *, max_hands: int = 2, min_confidence: float = 0.5, mirrored: bool = False):
        self.mirrored = mirrored
        options = HandLandmarkerOptions(
            base_options=BaseOptions(model_asset_path=str(ensure_model())),
            running_mode=RunningMode.VIDEO,
            num_hands=max_hands,
            min_hand_detection_confidence=min_confidence,
            min_hand_presence_confidence=min_confidence,
            min_tracking_confidence=min_confidence,
        )
        self._landmarker = HandLandmarker.create_from_options(options)

    def detect(self, image_bgr: np.ndarray, t_ms: int) -> list[Hand]:
        rgb = cv2.cvtColor(image_bgr, cv2.COLOR_BGR2RGB)
        result = self._landmarker.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), t_ms)
        hands = []
        for categories, image_points, world_points in zip(
            result.handedness, result.hand_landmarks, result.hand_world_landmarks, strict=True
        ):
            side: Side = categories[0].category_name.lower()
            # Checked on a sample photo: labels are right for normal footage, swapped for selfie-mirrored footage.
            if self.mirrored:
                side = other_side(side)
            hands.append(Hand(
                side=side,
                score=round(categories[0].score, 3),
                image=np.array([[p.x, p.y, p.z] for p in image_points]),
                world=np.array([[p.x, p.y, p.z] for p in world_points]),
            ))
        return resolve_sides(hands)

    def close(self) -> None:
        self._landmarker.close()

    def __enter__(self) -> "HandTracker":
        return self

    def __exit__(self, *exc) -> None:
        self.close()
