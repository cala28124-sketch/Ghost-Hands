"""Track hands through a whole video, and save/load the result (the frames file)."""

import json
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from ghosthands.landmarks import SIDES, Hand, HandTracker, Side
from ghosthands.video import VideoFrame, VideoSource

FRAMES_FORMAT = "ghosthands-frames"
FRAMES_VERSION = 1


@dataclass(frozen=True)
class TrackedFrame:
    index: int
    t_ms: int
    hands: tuple[Hand, ...]

    def hand(self, side: Side) -> Hand | None:
        return next((h for h in self.hands if h.side == side), None)


@dataclass(frozen=True)
class Tracking:
    """Every frame of one video, with the hands found in it."""

    source: str
    fps: float
    width: int
    height: int
    mirrored: bool
    frames: list[TrackedFrame]

    def coverage(self, side: Side) -> float:
        """Fraction of frames in which this hand was found."""
        if not self.frames:
            return 0.0
        return sum(f.hand(side) is not None for f in self.frames) / len(self.frames)

    def active_sides(self, min_coverage: float = 0.3) -> list[Side]:
        """Hands that are in the video for real, not a brief mislabel."""
        return [s for s in SIDES if self.coverage(s) >= min_coverage]


# Called for each frame while tracking; return False to stop early (e.g. q pressed in a preview).
FrameCallback = Callable[[VideoFrame, list[Hand]], bool]


def track(video: VideoSource, tracker: HandTracker, on_frame: FrameCallback | None = None) -> Tracking:
    """Every frame of the video, with its hands. For a webcam, Ctrl+C ends the recording and keeps it."""
    frames = []
    try:
        for frame in video:
            hands = tracker.detect(frame.image, frame.t_ms)
            frames.append(TrackedFrame(frame.index, frame.t_ms, tuple(hands)))
            if on_frame and on_frame(frame, hands) is False:
                break
    except KeyboardInterrupt:
        if not video.is_webcam:
            raise
    info = video.info
    return Tracking(str(video.source), info.fps, info.width, info.height, tracker.mirrored, frames)


def _points(a: np.ndarray) -> list[list[float]]:
    return [[round(float(v), 5) for v in p] for p in a]


def save_tracking(tracking: Tracking, path: Path) -> None:
    data = {
        "format": FRAMES_FORMAT,
        "version": FRAMES_VERSION,
        "source": tracking.source,
        "fps": tracking.fps,
        "width": tracking.width,
        "height": tracking.height,
        "mirrored": tracking.mirrored,
        "frames": [
            {
                "i": f.index,
                "t_ms": f.t_ms,
                "hands": [
                    {"side": h.side, "score": h.score, "image": _points(h.image), "world": _points(h.world)}
                    for h in f.hands
                ],
            }
            for f in tracking.frames
        ],
    }
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data), encoding="utf-8")


def load_tracking(path: Path) -> Tracking:
    data = json.loads(Path(path).read_text(encoding="utf-8"))
    if data.get("format") != FRAMES_FORMAT or data.get("version") != FRAMES_VERSION:
        raise ValueError(f"{path} is not a version {FRAMES_VERSION} frames file; re-run extract.py")
    frames = [
        TrackedFrame(
            f["i"],
            f["t_ms"],
            tuple(Hand(h["side"], h["score"], np.array(h["image"]), np.array(h["world"])) for h in f["hands"]),
        )
        for f in data["frames"]
    ]
    return Tracking(data["source"], data["fps"], data["width"], data["height"], data["mirrored"], frames)
