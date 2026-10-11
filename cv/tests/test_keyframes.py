import numpy as np
import pytest
from conftest import HEIGHT, PIXELS_PER_METER, WIDTH, make_hand, make_tracking, mock_joints, rotation_z

from ghosthands.handpose import Camera
from ghosthands.keyframes import (
    MAX_STILL_SPEED,
    Keyframe,
    KeyframeSettings,
    KeyframesFile,
    adapt_to_video,
    auto_keyframes,
    find_pauses,
    hand_speeds,
    keyframe_frames,
    load_keyframes,
    pose_window,
    rolling_median,
    save_keyframes,
)
from ghosthands.landmarks import Hand

FPS = 30


def both_hands(turn_degrees=0.0, x_px=320.0):
    """Two hands; the right one is turned by turn_degrees and placed at x_px."""
    return [
        make_hand("left", mock_joints("left"), position_px=(160, 240)),
        make_hand("right", mock_joints("right"), rotation_z(turn_degrees), position_px=(x_px, 240)),
    ]


def hold(seconds, **pose):
    return [both_hands(**pose) for _ in range(round(seconds * FPS))]


def move(seconds, start, end):
    """The right hand turns from start to end degrees, smoothly."""
    n = round(seconds * FPS)
    return [both_hands(turn_degrees=start + (end - start) * (i + 1) / n) for i in range(n)]


def test_still_hand_has_zero_speed_and_missing_hand_is_infinite():
    frames = hold(0.2) + [[make_hand("left", mock_joints("left"))]] + hold(0.2)
    speeds = hand_speeds(make_tracking(frames, FPS), "right")
    assert speeds[1] == pytest.approx(0, abs=1e-9)
    assert np.isinf(speeds[6]) and np.isinf(speeds[7])  # the gap, and the frame right after it


def test_sliding_across_the_frame_counts_as_motion():
    # The synthetic hand is drawn at 2000 px per meter: 3 px per frame at 30 fps = 0.045 m/s.
    # Its fingers point away from the camera, so the scale must come from the knuckles, not wrist-to-knuckle.
    frames = [both_hands(x_px=320 + 3 * i) for i in range(10)]
    speeds = hand_speeds(make_tracking(frames, FPS), "right")
    assert speeds[5] == pytest.approx(3 / 2000 * FPS, rel=0.05)


def test_a_wrist_roll_counts_as_motion():
    # Rolling 5 degrees a frame around the finger axis (the cube's R move) moves the fingertips ~0.26 m/s.
    frames = move(0.3, 0, 45)
    speeds = hand_speeds(make_tracking(frames, FPS), "right")
    assert speeds[5] == pytest.approx(np.radians(5) * FPS * 0.1, rel=0.05)
    assert speeds[5] > KeyframeSettings().still_speed


def test_rolling_median_ignores_one_frame_spikes():
    assert list(rolling_median(np.array([0, 0, 9, 0, 0.0]), 3)) == [0, 0, 0, 0, 0]


def test_two_held_poses_become_two_keyframes():
    frames = hold(0.6) + move(0.4, 0, 90) + hold(0.6, turn_degrees=90)
    tracking = make_tracking(frames, FPS)
    keyframes = auto_keyframes(tracking)

    assert len(keyframes) == 2
    first, second = keyframes
    assert first.t_ms == pytest.approx(300, abs=70)      # middle of the first hold
    assert second.t_ms == pytest.approx(1300, abs=70)    # middle of the second hold
    assert first.duration_ms == 800                      # first step: fixed ghost fade-in
    assert second.duration_ms == pytest.approx(400, abs=120)  # about the length of the move
    assert first.instruction == "Match pose 1" and first.mode == "hold"


def test_a_pause_shorter_than_the_minimum_is_ignored():
    frames = hold(0.6) + move(0.3, 0, 45) + hold(0.15, turn_degrees=45) + move(0.3, 45, 90) + hold(0.6, turn_degrees=90)
    assert len(auto_keyframes(make_tracking(frames, FPS))) == 2


def test_holding_the_same_pose_twice_makes_one_step():
    # Twitch away and back: two pauses, same pose.
    frames = hold(0.6) + move(0.2, 0, 30) + move(0.2, 30, 0) + hold(0.6)
    tracking = make_tracking(frames, FPS)
    assert len(find_pauses(tracking, ["left", "right"], KeyframeSettings())) == 2
    assert len(auto_keyframes(tracking)) == 1


def test_a_hidden_hand_breaks_the_pause():
    frames = hold(0.6)
    frames[9] = frames[9][:1]  # right hand missing in one frame
    pauses = find_pauses(make_tracking(frames, FPS), ["left", "right"], KeyframeSettings(smooth_frames=1))
    assert all(not (first <= 9 <= last) for first, last in pauses)


def test_pose_window_takes_the_middle():
    assert pose_window(0, 9, 15) == (0, 9)
    assert pose_window(0, 29, 15) == (7, 21)


def test_keyframes_file_round_trip(tmp_path):
    original = KeyframesFile("cube", "Cube", [
        Keyframe("Grip the cube", t_ms=300, frame=9, duration_ms=800, pose_frames=(2, 16)),
        Keyframe("Turn R", t_ms=1300, label="R", mode="pass", hands=["right"]),
    ])
    path = tmp_path / "k.json"
    save_keyframes(original, path)
    assert '"poseFrames": [' in path.read_text() and '"durationMs": 800' in path.read_text()
    assert load_keyframes(path) == original


def test_keyframes_file_errors_name_the_step(tmp_path):
    path = tmp_path / "k.json"
    path.write_text('{"lessonId": "x", "title": "X", "steps": [{"instruction": "no time"}]}')
    with pytest.raises(ValueError, match="step 1 needs t_ms or frame"):
        load_keyframes(path)
    path.write_text('{"lessonId": "x", "title": "X", "steps": [{"instruction": "a", "t_ms": 5, "colour": 1}]}')
    with pytest.raises(ValueError, match="step 1"):
        load_keyframes(path)


def test_keyframe_frames_from_time_only_or_frame():
    tracking = make_tracking(hold(1.0))  # 30 frames, 33 ms apart
    assert keyframe_frames(tracking, Keyframe("x", t_ms=500)) == (15, 12, 18)  # nearest frame, +-3 window
    assert keyframe_frames(tracking, Keyframe("x", frame=1)) == (1, 0, 4)       # window clipped at the start
    assert keyframe_frames(tracking, Keyframe("x", frame=99)) == (29, 26, 29)   # frame past the end
    assert keyframe_frames(tracking, Keyframe("x", frame=10, pose_frames=(5, 20))) == (10, 5, 20)


def jittery(frames, meters=0.0005, seed=0):
    """The same hands with tracking jitter: 0.5 mm per landmark per frame reads as ~0.12 m/s, like a 480x360 upload."""
    rng = np.random.default_rng(seed)
    pixels = meters * PIXELS_PER_METER / np.array([WIDTH, HEIGHT, PIXELS_PER_METER])  # image coords are 0..1
    return [
        [Hand(h.side, h.score, h.image + rng.normal(0, 1, h.image.shape) * pixels,
              h.world + rng.normal(0, meters, h.world.shape)) for h in hands]
        for hands in frames
    ]


def two_poses():
    return hold(1.0) + move(0.4, 0, 90) + hold(1.0, turn_degrees=90)


def test_a_sharp_video_keeps_the_default_stillness():
    assert adapt_to_video(make_tracking(two_poses(), FPS)).still_speed == KeyframeSettings().still_speed


def test_a_jittery_video_raises_the_stillness_threshold_and_still_finds_the_poses():
    tracking = make_tracking(jittery(two_poses()), FPS)
    assert auto_keyframes(tracking) == []  # the fixed 0.10 m/s: jitter alone is faster than that

    settings = adapt_to_video(tracking)
    assert KeyframeSettings().still_speed < settings.still_speed < MAX_STILL_SPEED
    assert len(auto_keyframes(tracking, settings)) == 2


def test_a_fixed_still_speed_is_not_adapted():
    tracking = make_tracking(jittery(two_poses()), FPS)
    fixed = KeyframeSettings(still_speed=0.12, adaptive=False)
    assert adapt_to_video(tracking, fixed) == fixed


def test_a_video_that_never_stops_is_capped():
    frames = move(1.0, 0, 360) + move(1.0, 360, 0)  # always turning, ~0.6 m/s at the fingertips
    assert adapt_to_video(make_tracking(frames, FPS)).still_speed == MAX_STILL_SPEED


def test_the_keyframes_file_remembers_the_camera(tmp_path):
    kf_file = KeyframesFile("demo", "Demo", [Keyframe("Hold", frame=3)], Camera(facing=True, pitch_degrees=30))
    save_keyframes(kf_file, tmp_path / "k.json")
    assert load_keyframes(tmp_path / "k.json").camera == Camera(facing=True, pitch_degrees=30)
