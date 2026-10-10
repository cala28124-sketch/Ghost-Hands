"""The whole video -> lesson pipeline, shared by build_lesson.py and process.py.

    track (video -> frames file) -> keyframes (auto, or an edited keyframes file) -> lesson -> validate -> review files
"""

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from ghosthands.keyframes import (
    DEFAULT_SETTINGS,
    KeyframeSettings,
    KeyframesFile,
    auto_keyframes,
    load_keyframes,
    save_keyframes,
)
from ghosthands.landmarks import HandTracker
from ghosthands.lesson import LessonError, build_lesson, validate_lesson
from ghosthands.review import render_review, save_motion_plot
from ghosthands.tracking import Tracking, save_tracking, track
from ghosthands.video import VideoSource


@dataclass(frozen=True)
class LessonResult:
    lesson: dict[str, Any]
    lesson_path: Path
    keyframes_path: Path
    review_paths: list[Path]


def track_video(video_path: Path, frames_path: Path, *, mirrored: bool = False) -> Tracking:
    with VideoSource(str(video_path)) as video, HandTracker(mirrored=mirrored) as tracker:
        tracking = track(video, tracker)
    save_tracking(tracking, frames_path)
    return tracking


def make_lesson(tracking: Tracking, out_stem: Path, lesson_id: str, title: str, *,
                keyframes_path: Path | None = None, settings: KeyframeSettings = DEFAULT_SETTINGS,
                review: bool = True) -> LessonResult:
    """Keyframes (from keyframes_path if given, else automatic) -> validated lesson, saved next to out_stem.

    Writes <stem>.keyframes.json (edit it and pass it back as keyframes_path to fix the steps),
    <lessonId>.json, and the review files. Raises LessonError if the result isn't a valid lesson.
    """
    if not tracking.active_sides():
        raise LessonError("no hands found in the video: use bright light and keep the hands in the frame")
    if keyframes_path:
        kf_file = load_keyframes(keyframes_path)
    else:
        kf_file = KeyframesFile(lesson_id, title, auto_keyframes(tracking, settings))
        keyframes_path = out_stem.with_name(out_stem.name + ".keyframes.json")
        save_keyframes(kf_file, keyframes_path)

    lesson = build_lesson(tracking, kf_file.lesson_id, kf_file.title, kf_file.keyframes)
    if errors := validate_lesson(lesson):
        raise LessonError("lesson failed validation:\n" + "\n".join(errors))
    lesson_path = out_stem.parent / f"{lesson['lessonId']}.json"
    lesson_path.write_text(json.dumps(lesson, indent=2), encoding="utf-8")

    review_paths: list[Path] = []
    if review and Path(tracking.source).is_file():
        files = render_review(tracking, out_stem, kf_file.keyframes)
        review_paths = [p for p in (files.video, files.sheet, files.keyframe_sheet) if p]
        plot = out_stem.with_name(out_stem.name + ".motion.png")
        save_motion_plot(tracking, settings, plot)
        review_paths.append(plot)
    return LessonResult(lesson, lesson_path, keyframes_path, review_paths)
