import numpy as np
from conftest import make_hand, make_tracking, mock_joints, rotation_z

from ghosthands.keyframes import Keyframe
from ghosthands.lesson import build_lesson
from ghosthands.replay import replay_data


def test_replay_is_in_unity_axes_and_matches_the_lesson_steps():
    joints = mock_joints("right")
    hands = [[make_hand("right", joints, rotation_z(30))] for _ in range(20)]
    hands[3] = []  # a frame where the hand is lost
    tracking = make_tracking(hands)
    keyframes = [Keyframe("first", frame=10, pose_frames=(8, 12)), Keyframe("second", frame=18)]

    data = replay_data(tracking, "demo", keyframes)
    lesson = build_lesson(tracking, "demo", "Demo", keyframes)

    assert [s["id"] for s in data["steps"]] == [s["id"] for s in lesson["steps"]]
    assert data["steps"][0] == {"id": "step-01", "frame": 10, "first": 8, "last": 12}
    assert data["steps"][1]["last"] == 19  # a manual window is clipped to the video
    assert data["frames"][3] == {"t_ms": tracking.frames[3].t_ms, "left": None, "right": None}

    # Unity axes: wrist-relative points are the hand-frame joints turned by the hand's rotation (y up, not down).
    points = np.array(data["frames"][10]["right"])
    expected = joints @ rotation_z(30).T
    assert np.allclose(points - points[0], expected - expected[0], atol=1e-4)
