"""The whole video -> lesson pipeline, shared by build_lesson.py and process.py.

    track (video -> frames file) -> keyframes (auto, or an edited keyframes file) -> lesson -> validate -> review files
"""

import json
from dataclasses import dataclass, replace
from pathlib import Path
from typing import Any

from ghosthands.handpose import Camera
from ghosthands.keyframes import (
    DEFAULT_SETTINGS,
    KeyframeSettings,
    KeyframesFile,
    adapt_to_video,
    auto_keyframes,
    load_keyframes,
    save_keyframes,
)
from ghosthands.landmarks import HandTracker
from ghosthands.lesson import LessonError, build_lesson, validate_lesson
from ghosthands.review import render_review, save_motion_plot
from ghosthands.tracking import Tracking, save_tracking, track
from ghosthands.video import VideoSource
from ghosthands.viewpoint import resolve_camera


@dataclass(frozen=True)
class LessonResult:
    lesson: dict[str, Any]
    lesson_path: Path
    keyframes_path: Path
    review_paths: list[Path]
    still_speed: float  # the stillness threshold used for this video (see adapt_to_video)
    camera: Camera      # how the video was filmed, as the lesson was built
    camera_note: str    # why: set by hand, or what in the video showed it


def camera_option(view: str | None, pitch_degrees: float | None) -> Camera | None:
    """The command line's --camera and --camera-pitch; None when neither was given (keep the file's or detect)."""
    if view is None and pitch_degrees is None:
        return None
    return Camera(facing=view == "facing", pitch_degrees=pitch_degrees or 0.0)


def track_video(video_path: Path, frames_path: Path, *, mirrored: bool = False) -> Tracking:
    with VideoSource(str(video_path)) as video, HandTracker(mirrored=mirrored) as tracker:
        tracking = track(video, tracker)
    save_tracking(tracking, frames_path)
    return tracking


def make_lesson(tracking: Tracking, out_stem: Path, lesson_id: str, title: str, *,
                keyframes_path: Path | None = None, settings: KeyframeSettings = DEFAULT_SETTINGS,
                camera: Camera | None = None, review: bool = True) -> LessonResult:
    """Keyframes (from keyframes_path if given, else automatic) -> validated lesson, saved next to out_stem.

    Writes <stem>.keyframes.json (edit it and pass it back as keyframes_path to fix the steps),
    <lessonId>.json, and the review files. Raises LessonError if the result isn't a valid lesson.
    camera: how the video was filmed. None keeps the keyframes file's, or works it out from the video (viewpoint.py).
    """
    if not tracking.active_sides():
        raise LessonError("no hands found in the video: use bright light and keep the hands in the frame")
    settings = adapt_to_video(tracking, settings)
    if keyframes_path:
        kf_file = load_keyframes(keyframes_path)
        if camera and camera != kf_file.camera:
            kf_file = replace(kf_file, camera=camera)
            save_keyframes(kf_file, keyframes_path)  # the file keeps recording how its lesson was built
    else:
        kf_file = KeyframesFile(lesson_id, title, auto_keyframes(tracking, settings), camera)
        keyframes_path = out_stem.with_name(out_stem.name + ".keyframes.json")
        save_keyframes(kf_file, keyframes_path)

    used_camera, camera_note = resolve_camera(kf_file.camera, tracking)
    lesson = build_lesson(tracking, kf_file.lesson_id, kf_file.title, kf_file.keyframes, used_camera)
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
    return LessonResult(lesson, lesson_path, keyframes_path, review_paths, settings.still_speed,
                        used_camera, camera_note)
