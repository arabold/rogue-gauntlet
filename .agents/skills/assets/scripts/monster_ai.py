#!/usr/bin/env python3
"""
Image and vision-LLM provider layer for the monster pipeline (`build_monster.py`).

Two providers expose the same three calls, so pipeline stages never depend on a vendor SDK:
  - `generate_image(prompt, aspect)`              text -> image (the concept turnaround)
  - `edit_image(prompt, refs, aspect)`            reference image(s) + text -> image (part isolation)
  - `analyze_json(prompt, images, schema)`        vision LLM -> JSON matching `schema` (part manifest)

Keys come from the repo `.env` (`GEMINI_API_KEY`, `OPENAI_API_KEY`); values are never printed.
Models default to the strongest image/vision models these keys can reach and can be overridden per call
site (`--image-model`, `--text-model`).
"""

import base64
import io
import json
import os
import time

from PIL import Image

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "..", ".."))

DEFAULT_MODELS = {
    "gemini": {"image": "gemini-3-pro-image", "text": "gemini-3.1-pro-preview"},
    "openai": {"image": "gpt-image-2", "text": "gpt-5.4"},
}

# OpenAI image sizes per aspect ratio (Gemini takes the ratio string directly).
_OPENAI_SIZES = {"1:1": "1024x1024", "16:9": "1536x1024", "4:3": "1536x1024", "3:2": "1536x1024", "9:16": "1024x1536"}


def load_dotenv(path=os.path.join(REPO_ROOT, ".env")):
    """Loads `KEY=value` lines into `os.environ` without overriding variables that are already set."""
    if not os.path.exists(path):
        return
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line and not line.startswith("#") and "=" in line:
                k, v = line.split("=", 1)
                os.environ.setdefault(k.strip(), v.strip().strip("'").strip('"'))


def _png_bytes(img):
    buf = io.BytesIO()
    img.convert("RGB").save(buf, format="PNG")
    return buf.getvalue()


def _retry(fn, attempts=3, delay=4.0):
    """Retries transient API failures (rate limits, 5xx, empty image responses) with linear backoff."""
    last = None
    for i in range(attempts):
        try:
            return fn()
        except Exception as e:  # noqa: BLE001 - surfaced after the final attempt
            last = e
            print(f"  [retry {i + 1}/{attempts}] {type(e).__name__}: {str(e)[:200]}")
            time.sleep(delay * (i + 1))
    raise last


class GeminiProvider:
    def __init__(self, image_model=None, text_model=None):
        from google import genai

        load_dotenv()
        self.client = genai.Client(api_key=os.environ["GEMINI_API_KEY"])
        self.image_model = image_model or DEFAULT_MODELS["gemini"]["image"]
        self.text_model = text_model or DEFAULT_MODELS["gemini"]["text"]

    def _image_call(self, contents, aspect):
        from google.genai import types

        cfg = types.GenerateContentConfig(
            response_modalities=["IMAGE"],
            image_config=types.ImageConfig(aspect_ratio=aspect, image_size="2K"),
        )

        def call():
            resp = self.client.models.generate_content(model=self.image_model, contents=contents, config=cfg)
            for cand in resp.candidates or []:
                for part in (cand.content.parts if cand.content else None) or []:
                    if part.inline_data and part.inline_data.data:
                        return Image.open(io.BytesIO(part.inline_data.data)).convert("RGB")
            raise RuntimeError(f"no image in response (finish: {[c.finish_reason for c in resp.candidates or []]})")

        return _retry(call)

    def generate_image(self, prompt, aspect="16:9"):
        return self._image_call([prompt], aspect)

    def edit_image(self, prompt, refs, aspect="16:9"):
        from google.genai import types

        parts = [types.Part.from_bytes(data=_png_bytes(r), mime_type="image/png") for r in refs]
        return self._image_call(parts + [prompt], aspect)

    def analyze_json(self, prompt, images, schema):
        from google.genai import types

        parts = [types.Part.from_bytes(data=_png_bytes(im), mime_type="image/png") for im in images]
        cfg = types.GenerateContentConfig(response_mime_type="application/json", response_json_schema=schema)

        def call():
            resp = self.client.models.generate_content(model=self.text_model, contents=parts + [prompt], config=cfg)
            return json.loads(resp.text)

        return _retry(call)


class OpenAIProvider:
    def __init__(self, image_model=None, text_model=None):
        import openai

        load_dotenv()
        self.client = openai.OpenAI(api_key=os.environ["OPENAI_API_KEY"])
        self.image_model = image_model or DEFAULT_MODELS["openai"]["image"]
        self.text_model = text_model or DEFAULT_MODELS["openai"]["text"]

    @staticmethod
    def _decode(resp):
        return Image.open(io.BytesIO(base64.b64decode(resp.data[0].b64_json))).convert("RGB")

    def generate_image(self, prompt, aspect="16:9"):
        size = _OPENAI_SIZES.get(aspect, "1536x1024")
        return _retry(lambda: self._decode(
            self.client.images.generate(model=self.image_model, prompt=prompt, size=size, quality="high")))

    def edit_image(self, prompt, refs, aspect="16:9"):
        size = _OPENAI_SIZES.get(aspect, "1536x1024")

        def call():
            files = [(f"ref{i}.png", _png_bytes(r), "image/png") for i, r in enumerate(refs)]
            return self._decode(self.client.images.edit(
                model=self.image_model, image=files, prompt=prompt, size=size, quality="high"))

        return _retry(call)

    def analyze_json(self, prompt, images, schema):
        content = [{"type": "input_text", "text": prompt}]
        for im in images:
            b64 = base64.b64encode(_png_bytes(im)).decode("ascii")
            content.append({"type": "input_image", "image_url": f"data:image/png;base64,{b64}"})

        def call():
            resp = self.client.responses.create(
                model=self.text_model,
                input=[{"role": "user", "content": content}],
                text={"format": {"type": "json_schema", "name": "result", "schema": schema, "strict": False}},
            )
            return json.loads(resp.output_text)

        return _retry(call)


def get_provider(name="gemini", image_model=None, text_model=None):
    if name == "gemini":
        return GeminiProvider(image_model, text_model)
    if name == "openai":
        return OpenAIProvider(image_model, text_model)
    raise ValueError(f"unknown provider {name!r} (expected 'gemini' or 'openai')")


# ---------------------------------------------------------------------------
# Image -> 3D (TRELLIS, MIT, on a Hugging Face Space)
# ---------------------------------------------------------------------------
TRELLIS_SPACE = "trellis-community/TRELLIS"


def image_to_3d(images, out_glb, seed=1, space=TRELLIS_SPACE):
    """
    One object reconstructed from one or more views (front, side, back crops on white) by TRELLIS's multi-image
    mode, saved as a textured GLB at `out_glb`. Uses `HF_TOKEN` from `.env` (free ZeroGPU quota per day).
    """
    import shutil
    import tempfile

    from gradio_client import Client, handle_file

    load_dotenv()
    client = Client(space, token=os.environ.get("HF_TOKEN"), verbose=False)
    client.predict(api_name="/start_session")
    tmp = tempfile.mkdtemp()
    paths = []
    for i, im in enumerate(images):
        p = os.path.join(tmp, f"view{i}.png")
        im.convert("RGB").save(p)
        paths.append(p)
    multi = [{"image": handle_file(p), "caption": None} for p in paths]

    def call():
        return client.predict(handle_file(paths[0]), multi, seed, 7.5, 12, 3.0, 12, "stochastic", 0.9, 1024,
                              api_name="/generate_and_extract_glb")

    result = _retry(call, attempts=2, delay=10.0)
    glb = next(r for r in result if isinstance(r, str) and r.endswith(".glb")) if isinstance(result, (list, tuple)) \
        else result
    shutil.copy(glb, out_glb)
    return out_glb


TRELLIS_MAC_DIR = os.environ.get("TRELLIS_MAC_DIR") or os.path.expanduser("~/Projects/Personal/trellis-mac")


def local_3d_available(root=TRELLIS_MAC_DIR):
    """Whether the local TRELLIS.2 port (`shivampkumar/trellis-mac`, set up with its `setup.sh`) is installed."""
    return os.path.exists(os.path.join(root, ".venv", "bin", "python")) and os.path.exists(os.path.join(root, "generate.py"))


def image_to_3d_local(jobs, root=TRELLIS_MAC_DIR):
    """
    Reconstructs objects with TRELLIS.2 on this Mac (`trellis_local_runner.py` in the port's own environment): one
    process for all `jobs` = [(rgba image, out_glb), ...], so the model loads once. Each image is one view (the
    front) with its mask as alpha; TRELLIS.2 takes a single image. Raises RuntimeError when a GLB is missing.
    """
    import subprocess
    import tempfile

    load_dotenv()
    tmp = tempfile.mkdtemp()
    args = []
    for i, (img, _) in enumerate(jobs):
        path = os.path.join(tmp, f"in{i}.png")
        img.save(path)
        args += [path, os.path.join(tmp, f"out{i}")]
    runner = os.path.join(os.path.dirname(os.path.abspath(__file__)), "trellis_local_runner.py")
    proc = subprocess.run([os.path.join(root, ".venv", "bin", "python"), runner, root, *args], cwd=root,
                          capture_output=True, text=True)
    for i, (_, out) in enumerate(jobs):
        glb = os.path.join(tmp, f"out{i}.glb")
        if not os.path.exists(glb):
            raise RuntimeError(f"local TRELLIS.2 produced no mesh: {(proc.stderr or proc.stdout)[-600:]}")
        import shutil

        shutil.copy(glb, out)
    return [out for _, out in jobs]
