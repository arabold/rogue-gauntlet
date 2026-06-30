#!/usr/bin/env python3
"""
Image -> 3D (GLB) via a Hugging Face Space, run on HF hardware (no local GPU).
EXPERIMENTAL / "author our own" path -- see the caveats in SKILL.md and assets/AGENTS.md:
output is high-poly PBR + unrigged (needs a Blender low-poly/palette + skin pass), and the
GPU step needs ZeroGPU quota (a free token often hits the wall; HF Pro or a duplicated
Space is the reliable route).

Needs `gradio_client` (NOT stdlib):  python3 -m venv .venv && .venv/bin/pip install gradio_client
Reads HF_TOKEN from the environment (source the repo .env).

Usage:
    .venv/bin/python hf_image_to_3d.py INPUT_IMAGE [OUTPUT.glb] [RESOLUTION 32-320]
"""
import os, shutil, sys
from gradio_client import Client, handle_file

SPACE = os.environ.get("HF_SPACE", "stabilityai/TripoSR")

def main():
    if len(sys.argv) < 2:
        sys.exit("usage: hf_image_to_3d.py INPUT_IMAGE [OUTPUT.glb] [RESOLUTION]")
    img = sys.argv[1]
    out = sys.argv[2] if len(sys.argv) > 2 else "output.glb"
    res = int(sys.argv[3]) if len(sys.argv) > 3 else 256
    token = os.environ.get("HF_TOKEN")
    if not token:
        print("WARN: no HF_TOKEN -- the GPU /generate step will likely fail.", file=sys.stderr)

    client = Client(SPACE, token=token, verbose=False)  # gradio_client 2.x uses token=, not hf_token=
    processed = client.predict(handle_file(img), True, 0.85, api_name="/preprocess")  # CPU, token-free
    _obj, glb = client.predict(processed, res, api_name="/generate")                  # GPU (ZeroGPU)
    shutil.copy(glb, out)
    print(f"GLB saved: {out} ({os.path.getsize(out)} bytes)")

if __name__ == "__main__":
    main()
