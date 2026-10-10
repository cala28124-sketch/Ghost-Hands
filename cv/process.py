"""Turn uploaded expert videos into lessons on the lesson server. Run it when videos are waiting.

    python process.py                     process every pending video on the server
    python process.py --server http://10.32.217.238:3000
    python process.py clip.mp4            run the same pipeline on a local file (no server)
    python process.py clip.mp4 --post     ... and upload the lesson to the server

The server address comes from --server, else the GHOSTHANDS_SERVER environment variable, else localhost:3000.
For each pending video: claim it -> download -> extract -> automatic keyframes -> lesson -> validate ->
POST /lessons?videoId=N (the server marks it done). If it can't become a lesson, it's marked failed with the reason.
"""

import argparse
import os
import sys
from pathlib import Path

from ghosthands.keyframes import KeyframeSettings
from ghosthands.lesson import LessonError, slugify, title_from_name
from ghosthands.pipeline import make_lesson, track_video
from ghosthands.server import DEFAULT_URL, LessonServer, ServerError, download

OUT_DIR = Path(__file__).resolve().parent / "out"


def process_file(video_path: Path, out_dir: Path, lesson_id: str, title: str, settings: KeyframeSettings) -> dict:
    stem = out_dir / video_path.stem
    print(f"  tracking hands in {video_path.name} ...")
    tracking = track_video(video_path, stem.with_name(stem.name + ".frames.json"))
    result = make_lesson(tracking, stem, lesson_id, title, settings=settings)
    print(f"  {len(result.lesson['steps'])} steps -> {result.lesson_path}")
    return result.lesson


def update_status(action, video_id: int, *args) -> None:
    """Report a video's status without letting a second error stop the rest of the queue."""
    try:
        action(video_id, *args)
    except (ServerError, OSError) as e:
        print(f"  (couldn't update video {video_id} on the server: {e})")


def process_pending(server: LessonServer, out_dir: Path, settings: KeyframeSettings) -> int:
    videos = server.pending_videos()
    if not videos:
        print("No pending videos.")
        return 0
    failures = 0
    for video in videos:
        video_id, file_name = video["videoId"], video["fileName"]
        print(f"Video {video_id} ({file_name})")
        if not server.claim(video_id):
            print("  already claimed by someone else, skipping")
            continue
        try:
            folder = out_dir / f"video-{video_id}"
            path = download(video["downloadUrl"], folder / Path(file_name).name)
            name = Path(file_name).stem
            lesson = process_file(path, folder, f"{slugify(name)}-{video_id}", title_from_name(name), settings)
            answer = server.post_lesson(lesson, video_id=video_id)
            print(f"  posted lesson {answer['lessonId']}; video marked done: {answer.get('videoUpdated')}")
        except (LessonError, ServerError, OSError) as e:
            # Expected failures: the reason is useful to whoever uploaded the video.
            failures += 1
            print(f"  FAILED: {e}")
            update_status(server.fail, video_id, str(e))
        except BaseException:
            # A bug or Ctrl+C: put the video back in the queue instead of leaving it stuck in "processing".
            update_status(server.release, video_id)
            raise
    return 1 if failures else 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("video", nargs="?", type=Path, help="process this local file instead of the server's queue")
    parser.add_argument("--server", default=os.environ.get("GHOSTHANDS_SERVER", DEFAULT_URL))
    parser.add_argument("--post", action="store_true", help="with a local video: upload the lesson to the server")
    parser.add_argument("--out", type=Path, default=OUT_DIR, help="output folder (default: cv/out)")
    parser.add_argument("--still-speed", type=float, default=KeyframeSettings.still_speed,
                        help="m/s below which a hand counts as still (default %(default)s)")
    args = parser.parse_args()

    settings = KeyframeSettings(still_speed=args.still_speed)
    server = LessonServer(args.server)
    if args.video is None:
        return process_pending(server, args.out, settings)
    lesson = process_file(args.video, args.out, slugify(args.video.stem), title_from_name(args.video.stem), settings)
    if args.post:
        print(f"  posted lesson {server.post_lesson(lesson)['lessonId']}")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (LessonError, ServerError, OSError, ValueError) as e:
        # Expected problems (unreadable video, invalid lesson, server down or refusing): one clear line.
        sys.exit(f"FAILED: {e}")
