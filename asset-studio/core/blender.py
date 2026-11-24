"""Headless Blender for Studio model scripts: find the pinned Blender, run one job, record its provenance.

Blender runs at authoring time only. `item model` regenerates an asset's committed `source.glb` from its committed
`source.py` and writes the sidecar `source.json` beside it; builds read the committed GLB and only compare the sidecar
with the current script, modeling helpers and host, so they never need Blender. Jobs run in a fresh factory-settings
Blender (`-b --factory-startup`), never in the user's open session.
"""

import hashlib
import json
import os
import re
import shutil
import struct
import subprocess
import tempfile
from pathlib import Path

STUDIO = Path(__file__).resolve().parents[1]
BLENDER = STUDIO / "blender"
RUNNER = BLENDER / "runner.py"
CANDIDATES = ("/Applications/Blender.app/Contents/MacOS/Blender", "/usr/bin/blender", "/usr/local/bin/blender",
              "C:/Program Files/Blender Foundation/Blender/blender.exe")


def binary():
    """The Blender executable: `BLENDER_BIN`, a standard install, or `blender` on the PATH."""
    found = os.environ.get("BLENDER_BIN") or next((c for c in CANDIDATES if Path(c).is_file()), None) or shutil.which("blender")
    if not found or not Path(found).is_file():
        raise ValueError("Blender not found; install it or set BLENDER_BIN")
    return found


def pinned():
    """The Blender release (`major.minor`) that generates committed models."""
    return json.loads((BLENDER / "blender.json").read_text())["version"]


def version(executable):
    """The full version string (`5.2.2`) reported by `executable`."""
    out = subprocess.run([executable, "--version"], capture_output=True, text=True, timeout=120).stdout
    match = re.search(r"Blender (\d+\.\d+(?:\.\d+)?)", out)
    if not match:
        raise ValueError(f"Cannot read the Blender version from {executable}")
    return match.group(1)


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


# What `item model` jobs run on. The character export script beside them is not one of them: character publications
# hash it in their own signature, so changing it must not mark scripted item models for regeneration.
MODEL_HELPERS = (BLENDER / "blender.json", BLENDER / "modeling", RUNNER)


def helpers_digest():
    """One hash over the runner, the pinned release and the modeling helpers, so a change marks every model for
    regeneration."""
    h = hashlib.sha256()
    files = (p for root in MODEL_HELPERS for p in ([root] if root.is_file() else root.rglob("*")))
    for path in sorted(p for p in files if p.is_file() and p.suffix in (".py", ".json")):
        h.update(path.relative_to(BLENDER).as_posix().encode())
        h.update(path.read_bytes())
    return h.hexdigest()


def provenance(script, host, glb):
    """What a committed `source.glb` was generated from (written by `item model`, checked by `item build`)."""
    return {"script": digest(script), "helpers": helpers_digest(), "host": digest(host) if host else None,
            "glb": digest(glb)}


def stale(sidecar, script, host, glb):
    """Why a committed model no longer matches its inputs: an empty list when it is current."""
    if not Path(sidecar).is_file():
        return ["no source.json: run `item model`"]
    recorded = json.loads(Path(sidecar).read_text())
    current = provenance(script, host, glb)
    names = {"script": "source.py", "helpers": "Blender modeling helpers", "host": "host", "glb": "source.glb"}
    return [f"{names[key]} changed since `item model`" for key in names if recorded.get(key) != current[key]]


def checked_binary():
    """Select a Blender executable on the Studio's pinned release."""
    executable = binary()
    found = version(executable)
    if not (found == pinned() or found.startswith(pinned() + ".")):
        raise ValueError(f"Blender {found} found; the Studio pins {pinned()} (asset-studio/blender/blender.json)")
    return executable, found


def export_character(source, target, options, scale=1):
    """Export a saved character scene in a fresh Blender process, without saving edits to the source, at the recipe's
    `scale` (see `finish_export`)."""
    target = Path(target)
    with tempfile.TemporaryDirectory(dir=target.parent) as staging:
        output = Path(staging) / "model.glb"
        run({**options, "glb": str(output)}, target.parent, BLENDER / "export_character.py", source, outputs=("glb",))
        finish_export(output, scale)
        output.replace(target)


def finish_export(path, scale=1):
    """Give a Blender export the project's matte surfaces, its game scale and a byte-stable layout, in place.

    `scale` (a number, or `[width, height, depth]` in glTF axes) multiplies the scene's root nodes. On a character that
    is the rig root, so the skinned result grows after posing: clips, bind pose and weights stay as authored, and a
    stockier non-uniform scale cannot shear a joint chain.

    Non-metal surfaces turn matte as everywhere else in the Studio (`export.matte_roughness`). Blender lists each clip's
    channels, and with them their accessors and buffer views, in a different order on every run; sorting channels by
    target and laying the buffer out in order of first use makes an unchanged source export byte-identical, so a
    re-export changes neither the committed GLBs nor their publication digests.
    """
    from core.export import matte_roughness
    from core.mesh import read_glb
    g, binary, _ = read_glb(str(path))
    if len(g.get("buffers", [])) != 1 or any(view.get("buffer", 0) for view in g.get("bufferViews", [])):
        raise ValueError(f"{path}: expected one embedded buffer")
    for material in g.get("materials", []):
        if pbr := material.get("pbrMetallicRoughness"):
            pbr["roughnessFactor"] = matte_roughness(pbr.get("metallicFactor", 1), pbr.get("roughnessFactor", 1))

    for animation in g.get("animations", []):
        animation["channels"].sort(key=lambda channel: (channel["target"].get("node", -1), channel["target"]["path"]))
        order, new = _first_use([channel["sampler"] for channel in animation["channels"]], len(animation["samplers"]))
        for channel in animation["channels"]:
            channel["sampler"] = new[channel["sampler"]]
        animation["samplers"] = [animation["samplers"][i] for i in order]

    primitives = [primitive for mesh in g.get("meshes", []) for primitive in mesh["primitives"]]
    accessor_slots = [(attributes, name) for primitive in primitives
                      for attributes in [primitive["attributes"], *primitive.get("targets", [])] for name in sorted(attributes)]
    accessor_slots += [(primitive, "indices") for primitive in primitives if "indices" in primitive]
    accessor_slots += [(skin, "inverseBindMatrices") for skin in g.get("skins", []) if "inverseBindMatrices" in skin]
    accessor_slots += [(sampler, key) for animation in g.get("animations", []) for sampler in animation["samplers"]
                       for key in ("input", "output")]
    order, new = _first_use([owner[key] for owner, key in accessor_slots], len(g.get("accessors", [])))
    for owner, key in accessor_slots:
        owner[key] = new[owner[key]]
    g["accessors"] = [g["accessors"][i] for i in order]

    sparse = [part for accessor in g["accessors"] if "sparse" in accessor
              for part in (accessor["sparse"]["indices"], accessor["sparse"]["values"])]
    view_slots = [owner for owner in [*g["accessors"], *sparse, *g.get("images", [])] if "bufferView" in owner]
    order, new = _first_use([owner["bufferView"] for owner in view_slots], len(g.get("bufferViews", [])))
    data = bytearray()
    for view in (g["bufferViews"][i] for i in order):
        data += bytes(-len(data) % 4)
        start = view.get("byteOffset", 0)
        view["byteOffset"] = len(data)
        data += binary[start:start + view["byteLength"]]
    data += bytes(-len(data) % 4)
    for owner in view_slots:
        owner["bufferView"] = new[owner["bufferView"]]
    g["bufferViews"] = [g["bufferViews"][i] for i in order]
    g["buffers"][0]["byteLength"] = len(data)

    factors = list(scale) if isinstance(scale, (list, tuple)) else [scale] * 3
    if factors != [1, 1, 1]:
        for node in (g["nodes"][i] for i in g["scenes"][g.get("scene", 0)]["nodes"]):
            if "matrix" in node or ("rotation" in node and len(set(factors)) > 1):
                raise ValueError(f"{path}: scale {factors} needs an unrotated TRS root, not {node.get('name')}")
            node["scale"] = [a * b for a, b in zip(node.get("scale", [1, 1, 1]), factors)]

    text = json.dumps(g, separators=(",", ":")).encode()
    text += b" " * (-len(text) % 4)
    body = struct.pack("<I4s", len(text), b"JSON") + text + struct.pack("<I4s", len(data), b"BIN\0") + bytes(data)
    Path(path).write_bytes(struct.pack("<4sII", b"glTF", 2, 12 + len(body)) + body)


def _first_use(indices, count):
    """`0..count-1` in order of first appearance in `indices` (any never named follow in their own order), and each
    index's new position."""
    order = list(dict.fromkeys(indices))
    named = set(order)
    order += [i for i in range(count) if i not in named]
    return order, {old: new for new, old in enumerate(order)}


def run(job, workdir, script=RUNNER, source=None, outputs=("glb", "report"), timeout=900):
    """Run one job `script` in a fresh headless Blender, opening the saved `source` scene first when given, and return
    the job's JSON report (`job["report"]`, plus the Blender version that wrote it), or None when it names none.

    The default script runs a Studio model script (`blender/runner.py`). The job and Blender's output stay in `workdir`
    as `<script>.job.json` and `<script>.log`. Fails with the log's last lines when the script raises, Blender is not
    the pinned release, or a file the job names under one of the `outputs` keys was not written.
    """
    executable, found = checked_binary()
    workdir = Path(workdir)
    workdir.mkdir(parents=True, exist_ok=True)
    name = Path(script).stem
    job_path = workdir / f"{name}.job.json"
    job_path.write_text(json.dumps(job, indent=1))
    scene = [str(source)] if source else []
    result = subprocess.run([executable, "-b", "--factory-startup", *scene, "--python-exit-code", "1",
                             "--python", str(script), "--", str(job_path)], capture_output=True, text=True,
                            timeout=timeout, env={**os.environ, "PYTHONDONTWRITEBYTECODE": "1"})
    log = workdir / f"{name}.log"
    log.write_text(result.stdout + result.stderr)
    if result.returncode != 0 or not all(Path(job[key]).is_file() for key in outputs):
        tail = "\n".join((result.stdout + result.stderr).strip().splitlines()[-25:])
        raise ValueError(f"Blender {name} failed (log: {log}):\n{tail}")
    if not job.get("report"):
        return None
    report = json.loads(Path(job["report"]).read_text())
    report["blender"] = found
    return report
