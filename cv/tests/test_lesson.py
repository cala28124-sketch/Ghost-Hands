import copy

import pytest
from conftest import example_lesson, make_hand, make_tracking, mock_joints

from ghosthands.keyframes import Keyframe
from ghosthands.lesson import LessonError, build_lesson, slugify, validate_lesson


def two_hand_tracking(n_frames=30):
    hands = [make_hand("left", mock_joints("left"), position_px=(160, 240)), make_hand("right", mock_joints("right"))]
    return make_tracking([hands] * n_frames)


def test_example_lesson_is_valid():
    assert validate_lesson(example_lesson()) == []


def test_built_lesson_is_valid_and_complete():
    keyframes = [Keyframe("Grip", t_ms=100), Keyframe("Push", t_ms=700, label="U", mode="pass", hands=["right"])]
    lesson = build_lesson(two_hand_tracking(), "cube", "Cube", keyframes)

    assert validate_lesson(lesson) == []
    assert lesson["hands"] == ["left", "right"]
    first, second = lesson["steps"]
    assert first == first | {"id": "step-01", "instruction": "Grip", "mode": "hold", "holdMs": 700, "durationMs": 800}
    assert set(first["hands"]) == {"left", "right"}
    assert second["label"] == "U" and "holdMs" not in second and list(second["hands"]) == ["right"]
    assert second["durationMs"] == pytest.approx(600, abs=40)  # time since the previous step


def test_missing_hand_is_an_error_not_a_silent_drop():
    tracking = make_tracking([[make_hand("right", mock_joints("right"))]] * 10 + [[]] * 10)
    keyframes = [Keyframe("Pose", frame=15, hands=["right"])]
    with pytest.raises(LessonError, match=r"step 1 .*right hand isn't visible"):
        build_lesson(tracking, "x", "X", keyframes)


def test_no_keyframes_is_an_error():
    with pytest.raises(LessonError, match="no keyframes"):
        build_lesson(two_hand_tracking(), "x", "X", [])


@pytest.mark.parametrize(("change", "message"), [
    (lambda lesson: lesson["steps"][0].pop("holdMs"), "'holdMs' is a required property"),
    (lambda lesson: lesson.update(hands=["left"]), '/steps/0/hands/right is not listed in "hands"'),
    (lambda lesson: lesson["steps"][0]["hands"]["left"].update(wristRotation=[0.5, 0, 0, 0.5]), "unit quaternion"),
    (lambda lesson: lesson["steps"][1].update(id="step-01"), "step ids must be unique"),
    (lambda lesson: lesson["steps"][0]["hands"]["left"]["joints"][0].__setitem__(0, 0.01), "the wrist"),
    (lambda lesson: lesson.update(hand="right"), "Additional properties"),
])
def test_validation_catches(change, message):
    lesson = copy.deepcopy(example_lesson())
    change(lesson)
    errors = validate_lesson(lesson)
    assert any(message in e for e in errors), errors


def test_validation_of_non_object():
    assert validate_lesson([]) != []


@pytest.mark.parametrize(("name", "slug"), [
    ("Cube R U (take 2)", "cube-r-u-take-2"),
    ("  --  ", "lesson"),
    ("A" * 100, "a" * 64),
])
def test_slugify(name, slug):
    assert slugify(name) == slug
