"""The server client and process.py's loop, against a small fake of web/server.js's video endpoints."""

import json
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse

import pytest

import process
from ghosthands.lesson import LessonError
from ghosthands.server import LessonServer, ServerError

# The moves web/server.js allows on PATCH /videos/:id: new status -> statuses it can come from.
VIDEO_MOVES = {"processing": ["pending"], "failed": ["pending", "processing"], "pending": ["failed", "processing"]}


class FakeServer:
    def __init__(self):
        self.videos = {}
        self.lessons = {}
        self.calls = []
        fake = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass

            def _send(self, status, body, content_type="application/json"):
                data = body if isinstance(body, bytes) else json.dumps(body).encode()
                self.send_response(status)
                self.send_header("Content-Type", content_type)
                self.send_header("Content-Length", str(len(data)))
                self.end_headers()
                self.wfile.write(data)

            def _body(self):
                return json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))) or b"null")

            def do_GET(self):
                url = urlparse(self.path)
                fake.calls.append(("GET", self.path))
                if url.path == "/videos":
                    status = parse_qs(url.query).get("status", [None])[0]
                    videos = [v for v in fake.videos.values() if status in (None, v["status"])]
                    base = f"http://127.0.0.1:{fake.port}"
                    return self._send(200, [v | {"downloadUrl": f"{base}/files/{v['videoId']}"} for v in videos])
                if url.path.startswith("/files/"):
                    return self._send(200, b"fake video bytes", "video/mp4")
                self._send(404, {"errors": [f"No endpoint GET {url.path}"]})

            def do_PATCH(self):
                body = self._body()
                fake.calls.append(("PATCH", self.path, body))
                video = fake.videos.get(int(self.path.rsplit("/", 1)[1]))
                if video is None:
                    return self._send(404, {"errors": ["Video not found"]})
                if video["status"] not in VIDEO_MOVES[body["status"]]:
                    return self._send(409, {"errors": [f"Video is {video['status']}"]})
                video.update(status=body["status"], error=body.get("error"))
                self._send(200, {"ok": True})

            def do_POST(self):
                url = urlparse(self.path)
                lesson = self._body()
                fake.calls.append(("POST", self.path, lesson["lessonId"]))
                if not lesson.get("steps"):
                    return self._send(400, {"errors": ["/steps must NOT have fewer than 1 items"]})
                fake.lessons[lesson["lessonId"]] = lesson
                updated = False
                if video_id := parse_qs(url.query).get("videoId"):
                    video = fake.videos[int(video_id[0])]
                    if video["status"] in ("pending", "processing"):
                        video.update(status="done", lessonId=lesson["lessonId"])
                        updated = True
                self._send(201, {"ok": True, "lessonId": lesson["lessonId"], "videoUpdated": updated})

        self.httpd = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.port = self.httpd.server_address[1]
        self.url = f"http://127.0.0.1:{self.port}"
        threading.Thread(target=self.httpd.serve_forever, daemon=True).start()

    def add_video(self, video_id, file_name, status="pending"):
        self.videos[video_id] = {"videoId": video_id, "fileName": file_name, "status": status, "error": None}


@pytest.fixture
def fake():
    server = FakeServer()
    yield server
    server.httpd.shutdown()


def lesson_named(lesson_id):
    return {"lessonId": lesson_id, "steps": [{}]}


def test_claim_once_then_409(fake):
    fake.add_video(1, "cube.mp4")
    client = LessonServer(fake.url)
    assert client.claim(1) is True
    assert client.claim(1) is False


def test_errors_carry_the_server_messages(fake):
    with pytest.raises(ServerError) as info:
        LessonServer(fake.url).post_lesson({"lessonId": "x", "steps": []})
    assert info.value.status == 400
    assert info.value.errors == ["/steps must NOT have fewer than 1 items"]


def test_unreachable_server_is_a_clear_error():
    with pytest.raises(ConnectionError, match="can't reach the lesson server"):
        LessonServer("http://127.0.0.1:9", timeout=2).health()


def test_process_pending_end_to_end(fake, tmp_path, monkeypatch):
    fake.add_video(1, "Cube Demo.mp4")
    fake.add_video(2, "blurry.mp4")
    fake.add_video(3, "taken.mp4", status="processing")  # someone else has it; not in the pending list

    def fake_process_file(video_path: Path, out_dir, lesson_id, title, settings, camera):
        assert video_path.read_bytes() == b"fake video bytes"  # downloaded from downloadUrl
        if "blurry" in video_path.name:
            raise LessonError("no hands found")
        return lesson_named(lesson_id)

    monkeypatch.setattr(process, "process_file", fake_process_file)
    exit_code = process.process_pending(LessonServer(fake.url), tmp_path, settings=None)

    assert exit_code == 1  # one video failed
    assert fake.videos[1]["status"] == "done"
    assert fake.videos[1]["lessonId"] == "cube-demo-1"  # lessonId: file name + video id, so re-uploads don't clash
    assert fake.videos[2]["status"] == "failed" and fake.videos[2]["error"] == "no hands found"
    assert fake.videos[3]["status"] == "processing"
    assert ("POST", "/lessons?videoId=1", "cube-demo-1") in fake.calls


def test_a_crash_puts_the_video_back_in_the_queue(fake, tmp_path, monkeypatch):
    fake.add_video(1, "cube.mp4")

    def crash(*args, **kwargs):
        raise RuntimeError("bug")

    monkeypatch.setattr(process, "process_file", crash)
    with pytest.raises(RuntimeError):
        process.process_pending(LessonServer(fake.url), tmp_path, settings=None)
    assert fake.videos[1]["status"] == "pending"


def test_nothing_pending(fake, tmp_path, capsys):
    assert process.process_pending(LessonServer(fake.url), tmp_path, settings=None) == 0
    assert "No pending videos" in capsys.readouterr().out


def test_a_failed_status_update_does_not_stop_the_queue(fake, tmp_path, monkeypatch):
    fake.add_video(1, "first.mp4")
    fake.add_video(2, "second.mp4")

    def fail_and_lose_the_video(video_path, *args):
        if "first" in video_path.name:
            fake.videos[1]["status"] = "done"  # changed elsewhere meanwhile: marking it failed gets a 409
            raise LessonError("no hands found")
        return lesson_named("second-2")

    monkeypatch.setattr(process, "process_file", fail_and_lose_the_video)
    process.process_pending(LessonServer(fake.url), tmp_path, settings=None)
    assert fake.videos[2]["status"] == "done"
