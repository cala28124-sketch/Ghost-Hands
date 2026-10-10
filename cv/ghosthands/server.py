"""Client for Anthony's lesson server (web/server.js). Needs only the server address, no Neon keys.

Video status flow: pending -> processing (claim) -> done (POST /lessons?videoId=) or failed (with a reason).
"""

import json
import shutil
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any

DEFAULT_URL = "http://localhost:3000"


class ServerError(Exception):
    def __init__(self, status: int, errors: list[str]):
        super().__init__(f"server answered {status}: {'; '.join(errors)}")
        self.status = status
        self.errors = errors


class LessonServer:
    def __init__(self, base_url: str = DEFAULT_URL, timeout: float = 30):
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout

    def _request(self, method: str, path: str, body: Any = None) -> Any:
        data = json.dumps(body).encode() if body is not None else None
        request = urllib.request.Request(self.base_url + path, data=data, method=method)
        if data is not None:
            request.add_header("Content-Type", "application/json")
        try:
            with urllib.request.urlopen(request, timeout=self.timeout) as response:
                return json.loads(response.read() or b"null")
        except urllib.error.HTTPError as e:
            # Every error from the server has the shape {"errors": ["..."]}.
            try:
                errors = json.loads(e.read()).get("errors") or [e.reason]
            except (ValueError, AttributeError):
                errors = [str(e.reason)]
            raise ServerError(e.code, errors) from None
        except urllib.error.URLError as e:
            raise ConnectionError(f"can't reach the lesson server at {self.base_url}: {e.reason}") from None

    def health(self) -> dict[str, Any]:
        return self._request("GET", "/health")

    def pending_videos(self) -> list[dict[str, Any]]:
        """Videos waiting to be processed, each with a fresh 15-minute downloadUrl."""
        return self._request("GET", "/videos?status=pending")

    def claim(self, video_id: int) -> bool:
        """Mark a video as being processed. False if someone else got it first (409)."""
        try:
            self._request("PATCH", f"/videos/{video_id}", {"status": "processing"})
            return True
        except ServerError as e:
            if e.status == 409:
                return False
            raise

    def release(self, video_id: int) -> None:
        """Put a claimed video back in the queue (e.g. processing was interrupted)."""
        self._request("PATCH", f"/videos/{video_id}", {"status": "pending"})

    def fail(self, video_id: int, reason: str) -> None:
        self._request("PATCH", f"/videos/{video_id}", {"status": "failed", "error": reason[:500]})

    def post_lesson(self, lesson: dict[str, Any], video_id: int | None = None) -> dict[str, Any]:
        """Save a lesson; with video_id the server also marks that video done."""
        query = f"?videoId={video_id}" if video_id is not None else ""
        return self._request("POST", f"/lessons{query}", lesson)


def download(url: str, path: Path, timeout: float = 120) -> Path:
    """Stream a file (e.g. a signed video link) to disk."""
    path.parent.mkdir(parents=True, exist_ok=True)
    partial = path.with_name(path.name + ".part")
    with urllib.request.urlopen(url, timeout=timeout) as response, open(partial, "wb") as out:
        shutil.copyfileobj(response, out)
    partial.replace(path)
    return path
