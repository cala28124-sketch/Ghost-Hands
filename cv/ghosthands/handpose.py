"""Hand geometry: MediaPipe world landmarks -> the lesson schema's hand (joints, wristRotation, targetAngles).

Conventions (documented in shared/lessons/README.md):
  - Unity axes: x right, y up, z forward (away from the viewer).
  - joints: 21 points in the hand's own frame, in meters. Wrist at the origin, palm facing -y,
    fingers along +z. A right hand's thumb is on -x; a left hand is a right hand mirrored across x.
  - wristRotation: Unity quaternion [x, y, z, w] that turns the hand frame into the learner's view: what the expert
    would see with their head level, looking straight ahead. A Camera says how the video's camera differs from that
    (looking down at the hands, or facing the expert); without one the camera is taken as that view.
  - targetAngles: bend at each joint in degrees, 0 = straight, ordered knuckle to fingertip.
      index/middle/ring/pinky: [MCP, PIP, DIP]; positive bends toward the palm, negative is hyperextension.
      thumb: [MCP, IP], unsigned (its hinge isn't aligned with the hand's x axis).
    The bend at joint B between neighbors A and C is the angle between bones A->B and B->C.
    The matcher must compute live angles the same way.
"""

from dataclasses import dataclass
from typing import Any

import numpy as np

from ghosthands.landmarks import Side

WRIST, INDEX_MCP, MIDDLE_MCP, PINKY_MCP = 0, 5, 9, 17


@dataclass(frozen=True)
class Camera:
    """How the video's camera was placed, to turn what it saw into the learner's view (head level, looking ahead).

    facing: the camera faced the expert (a phone propped up in front of them), so it saw the hands from the
        other side; they're turned around to the expert's own view. False: filmed from the expert's point of view.
    pitch_degrees: how far the camera looked down. A phone at the chest looking down at the hands: about 45.
    """

    facing: bool = False
    pitch_degrees: float = 0.0

    def to_learner(self) -> np.ndarray:
        """3x3 rotation: camera axes -> the learner's view (both in Unity axes)."""
        a = np.radians(self.pitch_degrees)
        # Undo the tilt: the camera's forward (0, 0, 1) was really (0, -sin a, cos a), pointing down by a.
        level = np.array([[1, 0, 0], [0, np.cos(a), -np.sin(a)], [0, np.sin(a), np.cos(a)]])
        # Turn around (180 degrees about the vertical): what faced the camera faces away from the learner.
        turn = np.diag([-1.0, 1.0, -1.0]) if self.facing else np.eye(3)
        return turn @ level


DEFAULT_CAMERA = Camera()

# Landmark chains, wrist side first. Angles are measured at each interior landmark.
FINGER_CHAINS = {
    "thumb": (1, 2, 3, 4),
    "index": (0, 5, 6, 7, 8),
    "middle": (0, 9, 10, 11, 12),
    "ring": (0, 13, 14, 15, 16),
    "pinky": (0, 17, 18, 19, 20),
}


def to_unity_axes(world: np.ndarray) -> np.ndarray:
    """MediaPipe (x right, y down, z away) -> Unity (x right, y up, z forward): flip y."""
    return np.asarray(world, dtype=float) * np.array([1.0, -1.0, 1.0])


def hand_frame(points: np.ndarray, side: Side) -> np.ndarray:
    """3x3 rotation whose columns are the hand's right, up and forward axes, in viewer space."""
    forward = points[MIDDLE_MCP] - points[WRIST]
    forward = forward / np.linalg.norm(forward)
    # Palm down, fingers forward: a right hand's index knuckle is on -x, a left hand's on +x.
    across = points[PINKY_MCP] - points[INDEX_MCP]
    right = across if side == "right" else -across
    right = right - right.dot(forward) * forward  # make it exactly perpendicular to forward
    right = right / np.linalg.norm(right)
    up = np.cross(forward, right)  # back of the hand
    return np.column_stack([right, up, forward])


def quaternion(rotation: np.ndarray) -> np.ndarray:
    """Rotation matrix -> unit quaternion [x, y, z, w] with w >= 0."""
    m = rotation
    trace = np.trace(m)
    if trace > 0:
        s = 2 * np.sqrt(trace + 1)
        q = [(m[2, 1] - m[1, 2]) / s, (m[0, 2] - m[2, 0]) / s, (m[1, 0] - m[0, 1]) / s, s / 4]
    elif m[0, 0] > m[1, 1] and m[0, 0] > m[2, 2]:
        s = 2 * np.sqrt(1 + m[0, 0] - m[1, 1] - m[2, 2])
        q = [s / 4, (m[0, 1] + m[1, 0]) / s, (m[0, 2] + m[2, 0]) / s, (m[2, 1] - m[1, 2]) / s]
    elif m[1, 1] > m[2, 2]:
        s = 2 * np.sqrt(1 + m[1, 1] - m[0, 0] - m[2, 2])
        q = [(m[0, 1] + m[1, 0]) / s, s / 4, (m[1, 2] + m[2, 1]) / s, (m[0, 2] - m[2, 0]) / s]
    else:
        s = 2 * np.sqrt(1 + m[2, 2] - m[0, 0] - m[1, 1])
        q = [(m[0, 2] + m[2, 0]) / s, (m[1, 2] + m[2, 1]) / s, s / 4, (m[1, 0] - m[0, 1]) / s]
    q = np.array(q) / np.linalg.norm(q)
    return q if q[3] >= 0 else -q


def bend_angles(local: np.ndarray, chain: tuple[int, ...], signed: bool) -> list[float]:
    """Bend in degrees at each interior landmark of a chain (see module docstring)."""
    angles = []
    for a, b, c in zip(chain, chain[1:], chain[2:], strict=False):
        bone1, bone2 = local[b] - local[a], local[c] - local[b]
        cross = np.cross(bone1, bone2)
        angle = np.degrees(np.arctan2(np.linalg.norm(cross), bone1.dot(bone2)))
        # In the hand frame the fingers hinge around x; bending toward the palm makes cross.x positive.
        if signed and cross[0] < 0:
            angle = -angle
        angles.append(float(np.clip(angle, -90.0, 180.0)))
    return angles


def lesson_hand(world: np.ndarray, side: Side, camera: Camera = DEFAULT_CAMERA) -> dict[str, Any]:
    """One hand's MediaPipe world landmarks -> the schema's hand object. Only wristRotation depends on the camera."""
    points = to_unity_axes(world) @ camera.to_learner().T
    points = points - points[WRIST]
    rotation = hand_frame(points, side)
    local = points @ rotation  # each point expressed in the hand's own axes
    angles = {name: bend_angles(local, chain, signed=name != "thumb") for name, chain in FINGER_CHAINS.items()}
    return {
        "targetAngles": {name: [round(a, 1) for a in values] for name, values in angles.items()},
        "joints": [[round(float(v), 4) for v in p] for p in local],
        "wristRotation": [round(float(v), 4) for v in quaternion(rotation)],
    }


def pose_difference(a: dict[str, Any], b: dict[str, Any]) -> tuple[float, float]:
    """(largest finger-angle difference, wrist rotation difference), both in degrees, between two schema hands."""
    finger = max(
        abs(x - y)
        for name in FINGER_CHAINS
        for x, y in zip(a["targetAngles"][name], b["targetAngles"][name], strict=True)
    )
    qa, qb = np.array(a["wristRotation"]), np.array(b["wristRotation"])
    # Normalize: the stored quaternions are rounded, and arccos is very sensitive near 1.
    dot = abs(float(np.dot(qa / np.linalg.norm(qa), qb / np.linalg.norm(qb))))
    wrist = float(np.degrees(2 * np.arccos(min(1.0, dot))))
    return finger, wrist
