import cv2
import numpy as np
from conftest import HEIGHT, WIDTH, make_hand, make_tracking, mock_joints

from ghosthands.keyframes import Keyframe
from ghosthands.review import contact_sheet, draw_hands, render_review


def test_drawing_leaves_the_original_frame_untouched():
    frame = np.zeros((480, 640, 3), dtype=np.uint8)
    drawn = draw_hands(frame, [make_hand("right", mock_joints("right"))], t_ms=100, banner="STEP 1")
    assert not frame.any()
    assert drawn.any()


def test_contact_sheet_grid():
    tiles = [np.full((10, 20, 3), i, dtype=np.uint8) for i in range(6)]
    sheet = contact_sheet(tiles, cols=4)
    assert sheet.shape == (20, 80, 3)  # 2 rows x 4 columns
    assert sheet[15, 25, 0] == 5      # 6th tile: row 2, column 2
    assert contact_sheet([]) is None


def test_review_renders_hand_written_keyframes(tmp_path):
    """A keyframe typed by hand may have only t_ms (no frame, no poseFrames)."""
    video = tmp_path / "clip.mp4"
    writer = cv2.VideoWriter(str(video), cv2.VideoWriter_fourcc(*"mp4v"), 30, (WIDTH, HEIGHT))
    for _ in range(20):
        writer.write(np.zeros((HEIGHT, WIDTH, 3), dtype=np.uint8))
    writer.release()
    hands = [make_hand("right", mock_joints("right"))]
    tracking = make_tracking([hands] * 20, source=str(video))

    files = render_review(tracking, tmp_path / "clip", [Keyframe("Hold it", t_ms=300), Keyframe("Pass", t_ms=500)])

    assert files.video.stat().st_size > 0
    sheet = cv2.imread(str(files.keyframe_sheet))
    assert sheet.shape[1] == 2 * 320  # one thumbnail per step
