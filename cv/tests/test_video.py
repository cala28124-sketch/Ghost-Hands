import cv2
import numpy as np
import pytest

from ghosthands.video import VideoSource, next_timestamp


class TestNextTimestamp:
    def test_first_frame_uses_reported_time(self):
        assert next_timestamp(0.0, None, 30) == 0
        assert next_timestamp(12.4, None, 30) == 12

    def test_uses_reported_time_when_it_increases(self):
        # Variable frame rate: gaps of 33 then 40 ms are kept as recorded, not evened out.
        assert next_timestamp(33.0, 0, 30) == 33
        assert next_timestamp(73.0, 33, 30) == 73

    @pytest.mark.parametrize("reported", [0.0, 33.0, 20.0])
    def test_falls_back_to_fps_when_missing_repeated_or_backwards(self, reported):
        assert next_timestamp(reported, 33, 30) == 33 + 33

    def test_always_increases_even_at_very_high_fps(self):
        assert next_timestamp(5.0, 5, 10_000) == 6


def write_test_video(path, n_frames=10, fps=20, size=(64, 48)):
    writer = cv2.VideoWriter(str(path), cv2.VideoWriter_fourcc(*"mp4v"), fps, size)
    for i in range(n_frames):
        writer.write(np.full((size[1], size[0], 3), i * 20, dtype=np.uint8))
    writer.release()


def test_video_source_reads_every_frame_in_order(tmp_path):
    path = tmp_path / "clip.mp4"
    write_test_video(path, n_frames=10, fps=20)

    with VideoSource(str(path)) as video:
        frames = list(video)

    assert video.info.fps == pytest.approx(20)
    assert (video.info.width, video.info.height) == (64, 48)
    assert [f.index for f in frames] == list(range(10))
    times = [f.t_ms for f in frames]
    assert times == sorted(set(times)), "timestamps must strictly increase"
    assert times[1] - times[0] == pytest.approx(50, abs=1)  # 20 fps = 50 ms apart


def test_video_source_rejects_missing_file(tmp_path):
    with pytest.raises(OSError, match="Can't open"):
        VideoSource(str(tmp_path / "nope.mp4"))
