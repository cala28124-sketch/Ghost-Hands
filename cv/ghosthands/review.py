"""Things a person looks at: the annotated video, contact sheets, and the motion graph used to tune keyframes."""

import math
from dataclasses import dataclass
from pathlib import Path

import cv2
import numpy as np
from mediapipe.tasks.python.vision import HandLandmarksConnections

from ghosthands.keyframes import Keyframe, KeyframeSettings, find_pauses, hand_speeds, keyframe_frames, rolling_median
from ghosthands.landmarks import Hand
from ghosthands.tracking import Tracking
from ghosthands.video import VideoSource

BONES = [(c.start, c.end) for c in HandLandmarksConnections.HAND_CONNECTIONS]
SIDE_COLORS = {"left": (255, 160, 60), "right": (60, 160, 255)}  # BGR: left blue, right orange
STEP_COLOR = (80, 220, 80)
THUMB_WIDTH = 320


def _text(image: np.ndarray, text: str, origin: tuple[int, int], scale: float, color=(255, 255, 255)) -> None:
    """Text with a dark outline, readable on any background."""
    cv2.putText(image, text, origin, cv2.FONT_HERSHEY_SIMPLEX, scale, (0, 0, 0), 4, cv2.LINE_AA)
    cv2.putText(image, text, origin, cv2.FONT_HERSHEY_SIMPLEX, scale, color, 2, cv2.LINE_AA)


def draw_hands(image: np.ndarray, hands: tuple[Hand, ...] | list[Hand], t_ms: int, banner: str | None = None):
    """A copy of the frame with each hand's skeleton, side and score, the time, and an optional banner."""
    out = image.copy()
    h, w = out.shape[:2]
    for hand in hands:
        color = SIDE_COLORS[hand.side]
        points = [(int(x * w), int(y * h)) for x, y, _ in hand.image]
        for a, b in BONES:
            cv2.line(out, points[a], points[b], color, 2, cv2.LINE_AA)
        for i, p in enumerate(points):
            radius = 6 if i == 0 else 4
            cv2.circle(out, p, radius, (255, 255, 255), -1, cv2.LINE_AA)
            cv2.circle(out, p, radius, color, 1, cv2.LINE_AA)
        _text(out, f"{hand.side} {hand.score:.2f}", (points[0][0] + 8, points[0][1] + 20), 0.6, color)
    _text(out, f"t={t_ms} ms", (10, 28), 0.8)
    if banner:
        cv2.rectangle(out, (0, 0), (w - 1, h - 1), STEP_COLOR, 6)
        _text(out, banner, (10, h - 16), 0.9, STEP_COLOR)
    return out


def open_video_writer(path: Path, fps: float, size: tuple[int, int]) -> cv2.VideoWriter:
    """H.264 (plays in browsers) when this OpenCV build has it, otherwise MPEG-4 (plays in VLC and most players)."""
    path.parent.mkdir(parents=True, exist_ok=True)
    for codec in ("avc1", "mp4v"):
        writer = cv2.VideoWriter(str(path), cv2.VideoWriter_fourcc(*codec), fps, size)
        if writer.isOpened():
            return writer
        writer.release()
    raise OSError(f"Can't write video {path}")


def contact_sheet(tiles: list[np.ndarray], cols: int = 4) -> np.ndarray | None:
    """Thumbnails in a grid, read left to right."""
    if not tiles:
        return None
    tile_h, tile_w = tiles[0].shape[:2]
    rows = math.ceil(len(tiles) / cols)
    sheet = np.full((rows * tile_h, min(cols, len(tiles)) * tile_w, 3), 255, dtype=np.uint8)
    for i, tile in enumerate(tiles):
        r, c = divmod(i, cols)
        sheet[r * tile_h:(r + 1) * tile_h, c * tile_w:(c + 1) * tile_w] = tile
    return sheet


def _thumbnail(image: np.ndarray) -> np.ndarray:
    h, w = image.shape[:2]
    return cv2.resize(image, (THUMB_WIDTH, round(THUMB_WIDTH * h / w)))


@dataclass(frozen=True)
class ReviewFiles:
    video: Path
    sheet: Path | None
    keyframe_sheet: Path | None


def render_review(tracking: Tracking, out_stem: Path, keyframes: list[Keyframe] | None = None,
                  sheet_size: int = 12) -> ReviewFiles:
    """Second pass over the source video: <stem>.annotated.mp4, <stem>.sheet.jpg and, with keyframes,
    <stem>.keyframes.jpg (one thumbnail per step). Frames averaged into a step are outlined in green."""
    keyframes = keyframes or []
    step_at = {}     # frame index -> step number, for every frame averaged into a step's pose
    key_frames = {}  # the keyframe's own frame -> its keyframe
    for n, kf in enumerate(keyframes, 1):
        index, first, last = keyframe_frames(tracking, kf)
        for i in range(first, last + 1):
            step_at[i] = n
        key_frames[index] = kf
    sheet_every = max(1, len(tracking.frames) // sheet_size)

    sheet_tiles, step_tiles = [], {}
    video_path = out_stem.with_name(out_stem.name + ".annotated.mp4")
    with VideoSource(tracking.source) as video:
        writer = open_video_writer(video_path, tracking.fps, (tracking.width, tracking.height))
        try:
            for frame, tracked in zip(video, tracking.frames, strict=False):
                n = step_at.get(frame.index)
                image = draw_hands(frame.image, tracked.hands, tracked.t_ms, f"STEP {n}" if n else None)
                writer.write(image)
                if frame.index % sheet_every == 0 and len(sheet_tiles) < sheet_size:
                    sheet_tiles.append(_thumbnail(image))
                if kf := key_frames.get(frame.index):
                    tile = _thumbnail(image)
                    _text(tile, kf.instruction[:40], (6, tile.shape[0] - 40), 0.45, STEP_COLOR)
                    step_tiles[frame.index] = tile
        finally:
            writer.release()

    sheet_path = keyframe_path = None
    if (sheet := contact_sheet(sheet_tiles)) is not None:
        sheet_path = out_stem.with_name(out_stem.name + ".sheet.jpg")
        cv2.imwrite(str(sheet_path), sheet)
    if (sheet := contact_sheet([step_tiles[i] for i in sorted(step_tiles)])) is not None:
        keyframe_path = out_stem.with_name(out_stem.name + ".keyframes.jpg")
        cv2.imwrite(str(keyframe_path), sheet)
    return ReviewFiles(video_path, sheet_path, keyframe_path)


def save_motion_plot(tracking: Tracking, settings: KeyframeSettings, path: Path) -> None:
    """Hand speed over time, the stillness threshold, and the detected pauses. Use it to tune still_speed."""
    import matplotlib

    matplotlib.use("Agg")  # draw to a file; no window
    import matplotlib.pyplot as plt

    times = np.array([f.t_ms for f in tracking.frames]) / 1000
    sides = tracking.active_sides()
    fig, ax = plt.subplots(figsize=(10, 3.5))
    for side in sides:
        speed = rolling_median(hand_speeds(tracking, side), settings.smooth_frames)
        color = np.array(SIDE_COLORS[side][::-1]) / 255  # BGR -> RGB
        ax.plot(times, np.where(np.isinf(speed), np.nan, speed), label=f"{side} hand", color=color)
    for first, last in find_pauses(tracking, sides, settings):
        ax.axvspan(times[first], times[last], color="green", alpha=0.15)
    ax.axhline(settings.still_speed, color="gray", linestyle="--", label=f"still below {settings.still_speed} m/s")
    ax.set(xlabel="time (s)", ylabel="hand speed (m/s)", ylim=(0, max(0.5, settings.still_speed * 4)),
           title="Hand motion (green = pause that becomes a step)")
    ax.legend(loc="upper right")
    fig.tight_layout()
    path.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(path, dpi=120)
    plt.close(fig)
