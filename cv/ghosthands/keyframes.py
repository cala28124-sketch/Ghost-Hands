"""Keyframes: the moments in the video that become lesson steps.

Picked automatically where every hand in the lesson holds still (the plan: "both hands still for about 0.3 s"),
and saved as an editable keyframes file, so a wrong pick can be fixed by hand and the lesson rebuilt.
"""

import json
from dataclasses import asdict, dataclass, field, replace
from pathlib import Path

import numpy as np

from ghosthands.handpose import WRIST, Camera, hand_frame, lesson_hand, pose_difference, to_unity_axes
from ghosthands.landmarks import Hand, Side
from ghosthands.tracking import Tracking

HAND_LENGTH = 0.1  # m, wrist to fingertips (roughly); turns a rotation rate into a fingertip speed
PALM_EDGES = [(0, 5), (0, 17), (5, 17)]  # wrist-index knuckle, wrist-pinky knuckle, across the knuckles
FIRST_STEP_DURATION_MS = 800
MANUAL_POSE_WINDOW = 3  # frames on each side of a hand-picked keyframe averaged into its pose
NOISE_PERCENTILE = 10   # the video's steadiest tenth shows how fast tracking jitter makes a still hand seem to move
NOISE_FACTOR = 1.5      # the threshold sits this far above the jitter
MAX_STILL_SPEED = 0.3   # m/s; faster than this is movement, however jittery the video


@dataclass(frozen=True)
class KeyframeSettings:
    still_speed: float = 0.10      # m/s; a hand slower than this counts as still. Tune on a real clip.
    adaptive: bool = True          # raise still_speed on a jittery video (see adapt_to_video)
    min_still_ms: int = 300        # a pause must last this long to become a step
    smooth_frames: int = 5         # rolling median over this many frames, to ignore single-frame jitter
    same_pose_degrees: float = 10  # consecutive pauses closer than this (fingers and wrist) are one pose
    max_pose_frames: int = 15      # at most this many frames from a pause are averaged into its pose


DEFAULT_SETTINGS = KeyframeSettings()


@dataclass
class Keyframe:
    """One lesson step. Field names match the keyframes file; see save_keyframes()."""

    instruction: str
    t_ms: int | None = None
    frame: int | None = None
    mode: str = "hold"
    label: str | None = None
    hold_ms: int | None = None
    duration_ms: int | None = None
    hands: list[Side] | None = None              # None: every active hand
    pose_frames: tuple[int, int] | None = None   # first and last frame averaged into the pose


@dataclass
class KeyframesFile:
    lesson_id: str
    title: str
    keyframes: list[Keyframe] = field(default_factory=list)
    # How the video was filmed, set by hand; turns wristRotation into the learner's view.
    # None: worked out from the video each time the lesson is built (viewpoint.py).
    camera: Camera | None = None


def _hand_frame(hand: Hand) -> tuple[np.ndarray, np.ndarray]:
    """(rotation, joints in the hand frame) for one detected hand."""
    points = to_unity_axes(hand.world)
    points = points - points[WRIST]
    rotation = hand_frame(points, hand.side)
    return rotation, points @ rotation


def _meters_per_pixel(hand: Hand, pixels: np.ndarray) -> float:
    """Image scale at the hand. Uses the palm edge that is least shortened by pointing toward the camera."""
    ratios = [
        np.linalg.norm(hand.world[b] - hand.world[a])
        / max(np.linalg.norm((hand.image[b, :2] - hand.image[a, :2]) * pixels), 1e-6)
        for a, b in PALM_EDGES
    ]
    return float(min(ratios))


def keyframe_frames(tracking: Tracking, kf: Keyframe) -> tuple[int, int, int]:
    """(frame, first, last): the keyframe's frame index and the range of frames averaged into its pose.

    A hand-written keyframe may give only t_ms; it then uses the nearest frame and a small window around it.
    """
    last_index = len(tracking.frames) - 1
    if kf.frame is not None:
        index = min(max(kf.frame, 0), last_index)
    else:
        index = min(range(len(tracking.frames)), key=lambda i: abs(tracking.frames[i].t_ms - kf.t_ms))
    first, last = kf.pose_frames or (index - MANUAL_POSE_WINDOW, index + MANUAL_POSE_WINDOW)
    return index, max(first, 0), min(last, last_index)


def hand_speeds(tracking: Tracking, side: Side) -> np.ndarray:
    """Per-frame speed of one hand in m/s; inf where the hand isn't seen in this frame or the one before.

    The sum of three motions, each as roughly how fast the fingertips move:
      fingers  joints moving within the hand's own frame (opening, closing, bending)
      turn     the hand's rotation rate x HAND_LENGTH (a wrist roll barely moves points near its axis)
      slide    the wrist moving across the image, converted to meters at the hand's scale
    """
    frames = tracking.frames
    speeds = np.full(len(frames), np.inf)
    pixels = np.array([tracking.width, tracking.height])
    previous = None  # (hand, rotation, local joints) in the frame before
    for i, frame in enumerate(frames):
        hand = frame.hand(side)
        current = (hand, *_hand_frame(hand)) if hand is not None else None
        if current and previous:
            (before, rot_before, local_before), (now, rot_now, local_now) = previous, current
            dt = (frame.t_ms - frames[i - 1].t_ms) / 1000
            fingers = np.linalg.norm(local_now - local_before, axis=1).mean()
            relative = rot_now @ rot_before.T
            turn = np.arccos(np.clip((np.trace(relative) - 1) / 2, -1, 1)) * HAND_LENGTH
            slide = np.linalg.norm((now.image[WRIST, :2] - before.image[WRIST, :2]) * pixels)
            speeds[i] = (fingers + turn + slide * _meters_per_pixel(now, pixels)) / dt
        previous = current
    if len(frames) > 1 and frames[0].hand(side) is not None:
        speeds[0] = speeds[1]
    return speeds


def rolling_median(values: np.ndarray, window: int) -> np.ndarray:
    if window <= 1 or len(values) == 0:
        return values
    half = window // 2
    padded = np.pad(values, half, mode="edge")
    return np.array([np.median(padded[i:i + window]) for i in range(len(values))])


def find_pauses(tracking: Tracking, sides: list[Side], settings: KeyframeSettings) -> list[tuple[int, int]]:
    """(first, last) frame of every stretch where all the given hands are still for long enough."""
    if not sides:
        return []
    speeds = [rolling_median(hand_speeds(tracking, s), settings.smooth_frames) for s in sides]
    still = np.all([s < settings.still_speed for s in speeds], axis=0)
    times = [f.t_ms for f in tracking.frames]
    pauses, start = [], None
    for i, is_still in enumerate([*still, False]):  # the sentinel closes a pause that runs to the end
        if is_still and start is None:
            start = i
        elif not is_still and start is not None:
            if times[i - 1] - times[start] >= settings.min_still_ms:
                pauses.append((start, i - 1))
            start = None
    return pauses


def median_world(tracking: Tracking, side: Side, first: int, last: int) -> np.ndarray | None:
    samples = [h.world for f in tracking.frames[max(0, first):last + 1] if (h := f.hand(side)) is not None]
    return np.median(samples, axis=0) if samples else None


def pose_window(first: int, last: int, max_frames: int) -> tuple[int, int]:
    """The middle of a pause, at most max_frames long: the steadiest part, away from the moves on either side."""
    if last - first + 1 <= max_frames:
        return first, last
    start = (first + last + 1 - max_frames) // 2
    return start, start + max_frames - 1


def adapt_to_video(tracking: Tracking, settings: KeyframeSettings = DEFAULT_SETTINGS) -> KeyframeSettings:
    """The settings for this video: still_speed raised above its tracking jitter, if that's higher.

    A low-resolution upload (an iPhone shrinks videos picked from Photos to 480x360) makes a still hand seem
    to move at 0.10-0.15 m/s, so a fixed 0.10 finds no pauses. The jitter is measured where the video is
    steadiest (its slowest tenth of frames, with every lesson hand seen). A sharp video keeps still_speed.
    """
    sides = tracking.active_sides()
    if not settings.adaptive or not sides:
        return settings
    speeds = np.max([rolling_median(hand_speeds(tracking, s), settings.smooth_frames) for s in sides], axis=0)
    seen = speeds[np.isfinite(speeds)]
    if len(seen) == 0:
        return settings
    jitter = float(np.percentile(seen, NOISE_PERCENTILE))
    still = max(settings.still_speed, min(NOISE_FACTOR * jitter, MAX_STILL_SPEED))
    return replace(settings, still_speed=round(still, 3))


def auto_keyframes(tracking: Tracking, settings: KeyframeSettings = DEFAULT_SETTINGS) -> list[Keyframe]:
    sides = tracking.active_sides()
    keyframes: list[Keyframe] = []
    previous_pose, previous_end = None, None
    for first, last in find_pauses(tracking, sides, settings):
        window = pose_window(first, last, settings.max_pose_frames)
        pose = {s: lesson_hand(median_world(tracking, s, *window), s) for s in sides}
        if previous_pose and all(
            max(pose_difference(pose[s], previous_pose[s])) < settings.same_pose_degrees for s in sides
        ):
            previous_end = last  # same pose held again: extend the previous step instead of adding one
            continue
        start_ms = tracking.frames[first].t_ms
        duration = FIRST_STEP_DURATION_MS if previous_end is None else start_ms - tracking.frames[previous_end].t_ms
        middle = (window[0] + window[1]) // 2
        keyframes.append(Keyframe(
            instruction=f"Match pose {len(keyframes) + 1}",
            t_ms=tracking.frames[middle].t_ms,
            frame=middle,
            duration_ms=int(np.clip(duration, 0, 10_000)),
            pose_frames=window,
        ))
        previous_pose, previous_end = pose, last
    return keyframes


# The keyframes file uses the lesson schema's camelCase names, since it's edited next to lesson files.
_FILE_NAMES = {"t_ms": "t_ms", "hold_ms": "holdMs", "duration_ms": "durationMs", "pose_frames": "poseFrames"}


def save_keyframes(kf_file: KeyframesFile, path: Path) -> None:
    steps = []
    for kf in kf_file.keyframes:
        step = {_FILE_NAMES.get(k, k): v for k, v in asdict(kf).items() if v is not None}
        if "poseFrames" in step:
            step["poseFrames"] = list(step["poseFrames"])
        steps.append(step)
    data = {"lessonId": kf_file.lesson_id, "title": kf_file.title}
    if kf_file.camera is not None:
        data["camera"] = {"facing": kf_file.camera.facing, "pitchDegrees": kf_file.camera.pitch_degrees}
    data["steps"] = steps
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2), encoding="utf-8")


def load_keyframes(path: Path) -> KeyframesFile:
    data = json.loads(Path(path).read_text(encoding="utf-8"))
    from_file = {v: k for k, v in _FILE_NAMES.items()}
    keyframes = []
    for n, step in enumerate(data["steps"], 1):
        fields = {from_file.get(k, k): v for k, v in step.items()}
        if "t_ms" not in fields and "frame" not in fields:
            raise ValueError(f"{path}: step {n} needs t_ms or frame")
        if fields.get("pose_frames") is not None:
            fields["pose_frames"] = tuple(fields["pose_frames"])
        try:
            keyframes.append(Keyframe(**fields))
        except TypeError as e:
            raise ValueError(f"{path}: step {n}: {e}") from None
    camera = data.get("camera")
    if camera is not None:
        camera = Camera(facing=bool(camera.get("facing", False)), pitch_degrees=float(camera.get("pitchDegrees", 0)))
    return KeyframesFile(data["lessonId"], data["title"], keyframes, camera)
