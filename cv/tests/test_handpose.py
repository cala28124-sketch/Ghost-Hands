import numpy as np
import pytest
from conftest import make_hand, mock_joints, rotation_x, rotation_z

from ghosthands.handpose import Camera, bend_angles, hand_frame, lesson_hand, pose_difference, quaternion


def random_rotation(seed: int) -> np.ndarray:
    q, r = np.linalg.qr(np.random.default_rng(seed).normal(size=(3, 3)))
    q = q * np.sign(np.diag(r))
    return q if np.linalg.det(q) > 0 else -q


def quat_to_matrix(q) -> np.ndarray:
    x, y, z, w = q
    return np.array([
        [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
        [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
        [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)],
    ])


@pytest.mark.parametrize("seed", range(20))
def test_quaternion_matches_its_rotation(seed):
    rotation = random_rotation(seed)
    q = quaternion(rotation)
    assert np.linalg.norm(q) == pytest.approx(1)
    assert q[3] >= 0
    assert quat_to_matrix(q) == pytest.approx(rotation, abs=1e-9)


@pytest.mark.parametrize("side", ["left", "right"])
@pytest.mark.parametrize("seed", range(5))
def test_recovers_hand_shape_and_rotation(side, seed):
    joints = mock_joints(side)
    canonical = hand_frame(joints, side)  # the mock's own axes, nearly identity
    turn = random_rotation(seed)
    result = lesson_hand(make_hand(side, joints, turn).world, side)

    assert np.array(result["joints"]) == pytest.approx(joints @ canonical, abs=2e-4)
    assert quat_to_matrix(result["wristRotation"]) == pytest.approx(turn @ canonical, abs=1e-3)
    assert result["joints"][0] == [0, 0, 0]


def test_left_and_right_hands_are_mirror_images():
    left = lesson_hand(make_hand("left", mock_joints("left")).world, "left")
    right = lesson_hand(make_hand("right", mock_joints("right")).world, "right")
    assert left["targetAngles"] == right["targetAngles"]
    assert np.array(left["joints"]) * [-1, 1, 1] == pytest.approx(np.array(right["joints"]), abs=1e-4)


def test_angles_agree_with_the_mock_lesson():
    """The mock's joints and targetAngles were written by hand; ours should be close (same definition)."""
    result = lesson_hand(make_hand("right", mock_joints("right", step=2)).world, "right")
    expected = {"index": [15, 10, 5], "middle": [50, 45, 20], "ring": [55, 45, 20], "pinky": [55, 40, 20]}
    for finger, angles in expected.items():
        assert result["targetAngles"][finger] == pytest.approx(angles, abs=8)


def test_bend_signs():
    chain = (0, 1, 2, 3)
    straight = np.array([[0, 0, 0], [0, 0, 1], [0, 0, 2], [0, 0, 3]], dtype=float)
    toward_palm = np.array([[0, 0, 0], [0, 0, 1], [0, -1, 2], [0, -2, 2]], dtype=float)  # palm faces -y
    backward = toward_palm * [1, -1, 1]
    assert bend_angles(straight, chain, signed=True) == pytest.approx([0, 0])
    assert bend_angles(toward_palm, chain, signed=True) == pytest.approx([45, 45])
    assert bend_angles(backward, chain, signed=True) == pytest.approx([-45, -45])
    assert bend_angles(backward, chain, signed=False) == pytest.approx([45, 45])


def test_pose_difference():
    a = lesson_hand(make_hand("right", mock_joints("right")).world, "right")
    b = lesson_hand(make_hand("right", mock_joints("right"), rotation_z(30)).world, "right")
    assert pose_difference(a, a) == pytest.approx((0, 0), abs=1e-6)
    finger, wrist = pose_difference(a, b)
    assert finger == pytest.approx(0, abs=0.2)
    assert wrist == pytest.approx(30, abs=0.5)


def test_a_camera_facing_the_expert_turns_the_wrist_around_but_keeps_the_pose():
    world = make_hand("right", mock_joints("right"), rotation_z(30)).world
    seen = lesson_hand(world, "right")
    learner = lesson_hand(world, "right", Camera(facing=True))

    assert learner["targetAngles"] == seen["targetAngles"] and learner["joints"] == seen["joints"]
    # Facing the camera, the palm faced the lens; from the expert's own eyes it's turned 180 degrees about vertical.
    expected = np.diag([-1.0, 1.0, -1.0]) @ quat_to_matrix(seen["wristRotation"])
    assert quat_to_matrix(learner["wristRotation"]) == pytest.approx(expected, abs=1e-3)


def test_camera_pitch_levels_a_camera_that_looked_down():
    # Looking down 45 degrees at a flat hand, the camera sees it tilted up by 45; leveling undoes exactly that.
    flat = lesson_hand(make_hand("right", mock_joints("right")).world, "right")
    seen_from_above = make_hand("right", mock_joints("right"), rotation_x(-45)).world
    leveled = lesson_hand(seen_from_above, "right", Camera(pitch_degrees=45))
    assert quat_to_matrix(leveled["wristRotation"]) == pytest.approx(quat_to_matrix(flat["wristRotation"]), abs=1e-3)
