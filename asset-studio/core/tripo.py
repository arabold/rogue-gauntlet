"""Tripo's multiview image-to-3D API: upload four turnaround views, run one job, download its GLB.

The key comes from the repo `.env` (`TRIPO_API_KEY`) and is never printed. Every job spends account credits, so callers
start one only on an explicit command. Tripo's edge rejects Python's default User-Agent (HTTP 403, "error code: 1010")
on both the API and its file CDN, so requests name the Studio instead.
"""

import json
import os
import time
import urllib.error
import urllib.request
import uuid
from pathlib import Path

from core import ai

API = "https://api.tripo3d.ai/v2/openapi"
AGENT = "rogue-gauntlet-asset-studio/1.0"
VIEW_ORDER = ("front", "left", "back", "right")  # Tripo's multiview order
DONE = {"success", "failed", "cancelled", "banned", "expired", "unknown"}


def _request(method, path, body=None, headers=None):
    ai.load_dotenv()
    key = os.environ.get("TRIPO_API_KEY")
    if not key:
        raise ValueError("Set TRIPO_API_KEY in the repo .env")
    h = {"Authorization": f"Bearer {key}", "User-Agent": AGENT, **(headers or {})}
    if isinstance(body, dict):
        body = json.dumps(body).encode()
        h["Content-Type"] = "application/json"
    try:
        with urllib.request.urlopen(urllib.request.Request(API + path, data=body, method=method, headers=h), timeout=120) as r:
            out = json.loads(r.read())
    except urllib.error.HTTPError as e:
        raise ValueError(f"Tripo {method} {path}: HTTP {e.code} {e.read().decode(errors='replace')[:400]}") from e
    if out.get("code") != 0:
        raise ValueError(f"Tripo {method} {path}: {out}")
    return out["data"]


def _upload(path):
    boundary = uuid.uuid4().hex
    body = (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{Path(path).name}"\r\n'
            "Content-Type: image/png\r\n\r\n").encode() + Path(path).read_bytes() + f"\r\n--{boundary}--\r\n".encode()
    return _request("POST", "/upload/sts", body, {"Content-Type": f"multipart/form-data; boundary={boundary}"})["image_token"]


def multiview(views, settings, out_glb, poll=10.0, log=print):
    """Run a `multiview_to_model` job on `views` (folder of front/left/back/right PNGs) and save its model GLB.

    `settings` is the provenance `reconstruction` block: `model_version`, `seed`, `textured`, `separate_parts`,
    and optional `omit_views`, views left out (Tripo takes an empty slot for any view but the front), such as a
    back view the image model drew from the wrong camera. Returns the finished task record.
    """
    omit = set(settings.get("omit_views", []))
    if "front" in omit:
        raise ValueError("Tripo needs the front view")
    files = [{} if name in omit else {"type": "png", "file_token": _upload(Path(views) / f"{name}.png")}
             for name in VIEW_ORDER]
    task = _request("POST", "/task", {
        "type": "multiview_to_model", "files": files, "model_version": settings["model_version"],
        "model_seed": settings["seed"], "texture": settings["textured"], "pbr": settings["textured"],
        "generate_parts": settings["separate_parts"]})["task_id"]
    log(f"tripo task {task}")
    while (record := _request("GET", f"/task/{task}"))["status"] not in DONE:
        time.sleep(poll)
    if record["status"] != "success":
        raise ValueError(f"Tripo task {task} ended {record['status']}")
    url = record["output"]["model"]
    with urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": AGENT}), timeout=600) as r:
        Path(out_glb).write_bytes(r.read())
    log(f"tripo task {task}: {record.get('consumed_credit')} credits, {out_glb}")
    return record
