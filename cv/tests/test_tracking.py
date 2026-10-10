import numpy as np
import pytest
from conftest import make_hand, make_tracking, mock_joints

from ghosthands.tracking import load_tracking, save_tracking


def test_frames_file_round_trip(tmp_path):
    original = make_tracking([[make_hand("right", mock_joints("right"))], []])
    path = tmp_path / "clip.frames.json"
    save_tracking(original, path)
    loaded = load_tracking(path)

    assert (loaded.source, loaded.fps, loaded.width, loaded.height) == ("synthetic.mp4", 30, 640, 480)
    assert [f.t_ms for f in loaded.frames] == [0, 33]
    hand = loaded.frames[0].hand("right")
    assert hand.score == 0.95
    assert hand.world == pytest.approx(original.frames[0].hands[0].world, abs=1e-5)
    assert loaded.frames[1].hands == ()


def test_old_frames_files_are_rejected(tmp_path):
    path = tmp_path / "old.frames.json"
    path.write_text('{"source": "x", "frames": []}')
    with pytest.raises(ValueError, match="re-run extract.py"):
        load_tracking(path)


def test_active_sides_ignore_brief_mislabels():
    right = make_hand("right", mock_joints("right"))
    stray_left = make_hand("left", mock_joints("left"))
    tracking = make_tracking([[right]] * 9 + [[right, stray_left]])
    assert tracking.coverage("left") == pytest.approx(0.1)
    assert tracking.active_sides() == ["right"]
    assert np.isclose(tracking.coverage("right"), 1)
