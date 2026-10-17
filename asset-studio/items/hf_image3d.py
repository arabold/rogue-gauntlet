#!/usr/bin/env python3
"""
Step 2 of the 3D Asset Pipeline: Image -> 3D (GLB) via Hugging Face Space (`stabilityai/TripoSR`).

Supports:
  1. `gradio_client` (if installed in `.venv` or system Python)
  2. Pure-stdlib `urllib.request` Gradio v4/v5 HTTP/SSE client (zero external dependencies!)
  3. Optional automatic fallback to `align.py --from-image` when `--archetype`
     is supplied and Hugging Face's free ZeroGPU quota is exhausted.

Reads `HF_TOKEN` from the environment (or auto-loads `.env` in the repo root if present).

Usage:
    python3 asset-studio/items/hf_image3d.py INPUT_IMAGE [OUTPUT.glb] [RESOLUTION 32-320] [--archetype ARCH]
"""

import json
import mimetypes
import os
import shutil
import sys
import urllib.error
import urllib.request
import uuid

SPACE = os.environ.get("HF_SPACE", "stabilityai/TripoSR")
SPACE_HOST = "https://stabilityai-triposr.hf.space"


def load_dotenv_if_needed():
    if os.environ.get("HF_TOKEN"):
        return
    repo_env = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".env"))
    if os.path.exists(repo_env):
        with open(repo_env, "r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if line and not line.startswith("#") and "=" in line:
                    k, v = line.split("=", 1)
                    os.environ.setdefault(k.strip(), v.strip().strip('"').strip("'"))


def run_via_gradio_client(img_path, out_path, res, token):
    from gradio_client import Client, handle_file

    client = Client(SPACE, token=token, verbose=False)
    processed = client.predict(handle_file(img_path), True, 0.85, api_name="/preprocess")
    _obj, glb = client.predict(processed, res, api_name="/generate")
    shutil.copy(glb, out_path)
    return True


def run_via_stdlib_http(img_path, out_path, res, token):
    """Calls a Gradio 4/5 Space via standard library urllib (multipart upload + SSE queue)."""
    headers = {"User-Agent": "rogue-gauntlet-asset-pipeline/1.0"}
    if token:
        headers["Authorization"] = f"Bearer {token}"

    # 1. Discover API prefix (/gradio_api or "")
    api_prefix = "/gradio_api"
    try:
        req = urllib.request.Request(f"{SPACE_HOST}/info", headers=headers)
        with urllib.request.urlopen(req, timeout=15) as resp:
            if resp.status == 200:
                api_prefix = ""
    except Exception:
        api_prefix = "/gradio_api"

    # 2. Upload image via multipart/form-data
    boundary = f"----WebKitFormBoundary{uuid.uuid4().hex}"
    filename = os.path.basename(img_path)
    mime = mimetypes.guess_type(filename)[0] or "image/png"
    with open(img_path, "rb") as f:
        file_bytes = f.read()

    body = (
        f"--{boundary}\r\n"
        f'Content-Disposition: form-data; name="files"; filename="{filename}"\r\n'
        f"Content-Type: {mime}\r\n\r\n"
    ).encode("utf-8") + file_bytes + f"\r\n--{boundary}--\r\n".encode("utf-8")

    up_headers = dict(headers)
    up_headers["Content-Type"] = f"multipart/form-data; boundary={boundary}"
    up_req = urllib.request.Request(f"{SPACE_HOST}{api_prefix}/upload", data=body, headers=up_headers, method="POST")
    with urllib.request.urlopen(up_req, timeout=30) as resp:
        uploaded_paths = json.loads(resp.read().decode("utf-8"))
    remote_path = uploaded_paths[0]
    file_data = {"path": remote_path, "meta": {"_type": "gradio.FileData"}}

    def call_endpoint(endpoint, data_list):
        payload = json.dumps({"data": data_list}).encode("utf-8")
        call_headers = dict(headers)
        call_headers["Content-Type"] = "application/json"
        req = urllib.request.Request(
            f"{SPACE_HOST}{api_prefix}/call/{endpoint.lstrip('/')}",
            data=payload,
            headers=call_headers,
            method="POST",
        )
        with urllib.request.urlopen(req, timeout=30) as resp:
            event_id = json.loads(resp.read().decode("utf-8"))["event_id"]

        sse_req = urllib.request.Request(
            f"{SPACE_HOST}{api_prefix}/call/{endpoint.lstrip('/')}/{event_id}",
            headers=headers,
        )
        with urllib.request.urlopen(sse_req, timeout=180) as sse:
            event_type = None
            for raw_line in sse:
                line = raw_line.decode("utf-8", errors="replace").strip()
                if line.startswith("event:"):
                    event_type = line.split(":", 1)[1].strip()
                elif line.startswith("data:"):
                    data_str = line.split(":", 1)[1].strip()
                    if event_type == "error":
                        raise RuntimeError(f"Gradio Space error on {endpoint}: {data_str}")
                    if event_type == "complete":
                        return json.loads(data_str)
        raise RuntimeError(f"Gradio SSE stream ended without completion on {endpoint}")

    pre_out = call_endpoint("preprocess", [file_data, True, 0.85])
    processed_file = pre_out[0]
    gen_out = call_endpoint("generate", [processed_file, res])
    glb_info = gen_out[1]
    glb_url = glb_info.get("url") or f"{SPACE_HOST}{api_prefix}/file={glb_info['path']}"

    dl_req = urllib.request.Request(glb_url, headers=headers)
    with urllib.request.urlopen(dl_req, timeout=60) as resp:
        os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
        with open(out_path, "wb") as f:
            f.write(resp.read())
    return True


def main():
    load_dotenv_if_needed()

    # Parse optional --archetype while keeping positional args backward-compatible
    argv = sys.argv[1:]
    archetype = None
    clean_args = []
    i = 0
    while i < len(argv):
        if argv[i] == "--archetype" and i + 1 < len(argv):
            archetype = argv[i + 1]
            i += 2
        else:
            clean_args.append(argv[i])
            i += 1

    if len(clean_args) < 1:
        sys.exit("usage: hf_image_to_3d.py INPUT_IMAGE [OUTPUT.glb] [RESOLUTION] [--archetype ARCH]")

    img = clean_args[0]
    out = clean_args[1] if len(clean_args) > 1 else "output.glb"
    res = int(clean_args[2]) if len(clean_args) > 2 else 256
    token = os.environ.get("HF_TOKEN")
    if not token:
        print("WARN: no HF_TOKEN -- the GPU /generate step may hit ZeroGPU quota limits.", file=sys.stderr)

    try:
        try:
            import gradio_client  # noqa: F401
            has_gradio = True
        except ImportError:
            has_gradio = False

        if has_gradio:
            run_via_gradio_client(img, out, res, token)
        else:
            run_via_stdlib_http(img, out, res, token)

        print(f"GLB saved (via HF {SPACE}): {out} ({os.path.getsize(out)} bytes)")
    except Exception as exc:
        if archetype:
            print(
                f"WARN: HF Space {SPACE} unavailable or ZeroGPU quota reached ({exc}); "
                f"falling back to local contour-aware 3D reconstruction for archetype '{archetype}'.",
                file=sys.stderr,
            )
            from items import align as stylize_and_align
            verts, tris, cols = stylize_and_align.reconstruct_from_concept_image(img, archetype)
            prof = stylize_and_align.ARCHETYPE_PROFILES[archetype]
            palette_png = os.path.abspath(
                os.path.join(
                    os.path.dirname(__file__),
                    "..", "..", "..", "..",
                    "assets", "stylized-dungeon-remastered", "dungeon_texture.png",
                )
            )
            positions, normals, uvs, indices = stylize_and_align.map_faces_to_palette(
                verts, tris, cols, palette_png, prof["default_palette"]
            )
            stylize_and_align.write_item_glb(out, positions, normals, uvs, indices, palette_png)
            print(f"GLB saved (via contour 3D fallback): {out} ({os.path.getsize(out)} bytes)")
        else:
            raise


if __name__ == "__main__":
    main()
