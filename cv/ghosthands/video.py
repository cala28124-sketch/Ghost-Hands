"""Read frames from a video file or webcam, each with a timestamp that always increases."""

import time
from collections.abc import Iterator
from dataclasses import dataclass

import cv2
import numpy as np

FALLBACK_FPS = 30.0


@dataclass(frozen=True)
class VideoInfo:
    fps: float
    width: int
    height: int
    frame_count: int  # 0 when unknown (webcam)


@dataclass(frozen=True)
class VideoFrame:
    index: int
    t_ms: int
    image: np.ndarray  # BGR, as OpenCV reads it


def next_timestamp(reported_ms: float, prev_ms: int | None, fps: float) -> int:
    """Pick a frame's timestamp: the video's own if usable, otherwise estimate from fps.

    Phones record at a variable frame rate, so the reported time is more accurate than index / fps.
    MediaPipe's VIDEO mode rejects a timestamp that doesn't increase, so never return one <= prev_ms.
    """
    if prev_ms is None:
        return max(0, round(reported_ms))
    t = round(reported_ms)
    if t <= prev_ms:  # missing (0), repeated, or out of order
        t = prev_ms + max(1, round(1000 / fps))
    return t


class VideoSource:
    """A video file or webcam. Use as a context manager, then iterate over its frames.

        with VideoSource("clip.mp4") as video:
            for frame in video:
                ...
    """

    def __init__(self, source: str | int):
        self.source = source
        self.is_webcam = isinstance(source, int)
        self._cap = cv2.VideoCapture(source)
        if not self._cap.isOpened():
            raise OSError(f"Can't open video source {source!r}")
        fps = self._cap.get(cv2.CAP_PROP_FPS)
        self.info = VideoInfo(
            fps=fps if 1 < fps <= 240 else FALLBACK_FPS,  # webcams often report 0 or garbage
            width=int(self._cap.get(cv2.CAP_PROP_FRAME_WIDTH)),
            height=int(self._cap.get(cv2.CAP_PROP_FRAME_HEIGHT)),
            frame_count=0 if self.is_webcam else int(self._cap.get(cv2.CAP_PROP_FRAME_COUNT)),
        )

    def __iter__(self) -> Iterator[VideoFrame]:
        prev_ms = None
        index = 0
        start = time.monotonic()
        while True:
            ok, image = self._cap.read()
            if not ok:
                return
            # A file stores each frame's time; a live webcam doesn't, so use the clock.
            if self.is_webcam:
                reported = (time.monotonic() - start) * 1000
            else:
                reported = self._cap.get(cv2.CAP_PROP_POS_MSEC)
            prev_ms = next_timestamp(reported, prev_ms, self.info.fps)
            yield VideoFrame(index, prev_ms, image)
            index += 1

    def close(self) -> None:
        self._cap.release()

    def __enter__(self) -> "VideoSource":
        return self

    def __exit__(self, *exc) -> None:
        self.close()
