"""Build a schema v2 lesson from tracked frames + keyframes, and validate it."""

import json
import re
from functools import cache
from pathlib import Path
from typing import Any

import numpy as np
from jsonschema import Draft7Validator

from ghosthands.handpose import DEFAULT_CAMERA, Camera, lesson_hand
from ghosthands.keyframes import FIRST_STEP_DURATION_MS, Keyframe, keyframe_frames, median_world
from ghosthands.tracking import Tracking

SCHEMA_PATH = Path(__file__).resolve().parents[2] / "shared" / "lesson.schema.json"
DEFAULT_HOLD_MS = 700


class LessonError(Exception):
    """This video or keyframes file can't become a valid lesson. The message says why."""


def slugify(name: str) -> str:
    """'Cube R U (take 2)' -> 'cube-r-u-take-2'. Valid as a lessonId (schema: ^[a-z0-9][a-z0-9-]*$)."""
    slug = re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-")[:64].strip("-")
    return slug or "lesson"


def title_from_name(name: str) -> str:
    """'cube_demo-take2' -> 'cube demo take2'. A placeholder title; edit it in the keyframes file."""
    return re.sub(r"[_-]+", " ", name).strip() or "Untitled lesson"


def build_lesson(tracking: Tracking, lesson_id: str, title: str, keyframes: list[Keyframe],
                 camera: Camera = DEFAULT_CAMERA) -> dict[str, Any]:
    if not tracking.frames:
        raise LessonError("the video has no frames")
    if not keyframes:
        raise LessonError("no keyframes: hold each pose still for about half a second, or pick them by hand")
    default_sides = tracking.active_sides()
    steps, used_sides, previous_t = [], set(), None
    for n, kf in enumerate(keyframes, 1):
        index, first, last = keyframe_frames(tracking, kf)
        t_ms = tracking.frames[index].t_ms
        sides = kf.hands or default_sides
        if not sides:
            raise LessonError("no hands were found in the video")
        hands = {}
        for side in sides:
            world = median_world(tracking, side, first, last)
            if world is None:
                raise LessonError(
                    f"step {n} ({t_ms} ms): the {side} hand isn't visible there; "
                    f'pick another moment, or list only the visible hands in the step\'s "hands"'
                )
            hands[side] = lesson_hand(world, side, camera)
        used_sides.update(hands)

        if kf.duration_ms is not None:
            duration = kf.duration_ms
        elif previous_t is None:
            duration = FIRST_STEP_DURATION_MS
        else:
            duration = int(np.clip(t_ms - previous_t, 0, 10_000))
        step: dict[str, Any] = {"id": f"step-{n:02d}"}
        if kf.label:
            step["label"] = kf.label
        step["instruction"] = kf.instruction
        step["mode"] = kf.mode
        if kf.mode == "hold":
            step["holdMs"] = kf.hold_ms or DEFAULT_HOLD_MS
        step["durationMs"] = duration
        step["hands"] = dict(sorted(hands.items()))
        steps.append(step)
        previous_t = t_ms

    return {
        "schemaVersion": 2,
        "lessonId": lesson_id,
        "title": title,
        "hands": sorted(used_sides),
        "coordinateConvention": "wrist-relative",
        "angleUnit": "degrees",
        "positionUnit": "meters",
        "steps": steps,
    }


@cache
def _schema_validator() -> Draft7Validator:
    return Draft7Validator(json.loads(SCHEMA_PATH.read_text(encoding="utf-8")))


def validate_lesson(lesson: Any) -> list[str]:
    """Every problem with a lesson, as readable messages; [] means valid.

    Same rules as web/validate.js (the shared JSON Schema + every step hand listed in "hands"),
    plus checks that catch exporter bugs: unit quaternions, unique step ids, wrist at the origin.
    """
    errors = [
        f"/{'/'.join(map(str, e.absolute_path))} {e.message}"
        for e in sorted(_schema_validator().iter_errors(lesson), key=lambda e: list(map(str, e.absolute_path)))
    ]
    if errors or not isinstance(lesson, dict):
        return errors
    ids = [step["id"] for step in lesson["steps"]]
    if len(ids) != len(set(ids)):
        errors.append("/steps step ids must be unique")
    for i, step in enumerate(lesson["steps"]):
        for side, hand in step["hands"].items():
            where = f"/steps/{i}/hands/{side}"
            if side not in lesson["hands"]:
                errors.append(f'{where} is not listed in "hands"')
            if abs(np.linalg.norm(hand["wristRotation"]) - 1) > 0.01:
                errors.append(f"{where}/wristRotation must be a unit quaternion")
            if np.linalg.norm(hand["joints"][0]) > 1e-6:
                errors.append(f"{where}/joints/0 (the wrist) must be [0, 0, 0]")
    return errors
