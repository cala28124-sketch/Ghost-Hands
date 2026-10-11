import json
from pathlib import Path

import pytest
from conftest import make_hand, make_tracking, mock_joints, rotation_z

from ghosthands.handpose import Camera
from ghosthands.lesson import build_lesson
from ghosthands.pipeline import make_lesson
from ghosthands.tracking import load_tracking
from ghosthands.viewpoint import detect_viewpoint, resolve_camera

OUT = Path(__file__).resolve().parents[1] / "out"


def two_hands(right_x: float, left_x: float, frames: int = 30, turn: float = 0.0):
    return [[make_hand("left", mock_joints("left"), position_px=(left_x, 240)),
             make_hand("right", mock_joints("right"), rotation_z(turn), position_px=(right_x, 240))]
            for _ in range(frames)]


def test_right_hand_on_the_left_of_the_picture_means_the_camera_faced_the_expert():
    viewpoint = detect_viewpoint(make_tracking(two_hands(right_x=160, left_x=480)))
    assert viewpoint.camera == Camera(facing=True) and viewpoint.detected
    assert "left of the left hand in 100% of 30 frames" in viewpoint.reason


def test_right_hand_on_the_right_means_the_experts_own_view():
    viewpoint = detect_viewpoint(make_tracking(two_hands(right_x=480, left_x=160)))
    assert viewpoint.camera == Camera(facing=False) and viewpoint.detected


def test_a_few_crossed_frames_dont_change_the_answer():
    frames = two_hands(right_x=160, left_x=480, frames=25) + two_hands(right_x=480, left_x=160, frames=5)
    assert detect_viewpoint(make_tracking(frames)).camera.facing


@pytest.mark.parametrize("x, facing", [(150, True), (500, False)])
def test_one_hand_is_judged_by_which_half_of_the_picture_it_stays_in(x, facing):
    frames = [[make_hand("right", mock_joints("right"), position_px=(x, 240))] for _ in range(30)]
    viewpoint = detect_viewpoint(make_tracking(frames))
    assert viewpoint.camera.facing == facing and viewpoint.detected


def test_a_hand_in_the_middle_cant_tell_so_the_experts_view_is_assumed():
    frames = [[make_hand("right", mock_joints("right"), position_px=(330, 240))] for _ in range(30)]
    viewpoint = detect_viewpoint(make_tracking(frames))
    assert viewpoint.camera == Camera() and not viewpoint.detected


def test_a_camera_set_by_hand_wins():
    tracking = make_tracking(two_hands(right_x=160, left_x=480))
    assert resolve_camera(Camera(pitch_degrees=45), tracking) == (Camera(pitch_degrees=45), "set by hand")
    assert resolve_camera(None, tracking)[0] == Camera(facing=True)


def test_a_lesson_filmed_facing_the_expert_is_turned_to_their_view(tmp_path):
    frames = two_hands(160, 480) + [h for t in range(12) for h in two_hands(160, 480, 1, 90 * (t + 1) / 12)] + \
        two_hands(160, 480, turn=90)
    tracking = make_tracking(frames)
    result = make_lesson(tracking, tmp_path / "clip", "demo", "Demo", review=False)
    assert result.camera == Camera(facing=True) and result.camera_note.startswith("detected")
    expected = build_lesson(tracking, "demo", "Demo", _keyframes(result.keyframes_path), Camera(facing=True))
    assert result.lesson["steps"] == expected["steps"]
    # The keyframes file leaves the camera out, so it's worked out again from the video on every rebuild.
    assert "camera" not in json.loads(result.keyframes_path.read_text())


def _keyframes(path: Path):
    from ghosthands.keyframes import load_keyframes
    return load_keyframes(path).keyframes


# The real recordings, when they're on this machine (cv/out is not in git).
@pytest.mark.parametrize("frames_file, facing", [
    ("video-14/Count.frames.json", True),      # confirmed on the headset: the ghost was turned until set to facing
    ("video-16/PaulCube.frames.json", True),   # reported turned 180 degrees when built as the expert's view
    ("video-17/ASL.frames.json", True),        # one hand, signed toward the camera
    ("video-18/Write.frames.json", False),
])
def test_real_recordings(frames_file, facing):
    path = OUT / frames_file
    if not path.exists():
        pytest.skip(f"{path} is not on this machine")
    assert detect_viewpoint(load_tracking(path)).camera.facing == facing
