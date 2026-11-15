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
import subprocess
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


def helpers_digest():
    """One hash over the runner and modeling helpers, so a helper change marks every model for regeneration."""
    h = hashlib.sha256()
    for path in sorted(p for p in BLENDER.rglob("*") if p.is_file() and p.suffix in (".py", ".json")):
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


def run(job, workdir, timeout=900):
    """Run one job (see `blender/runner.py`) in a fresh headless Blender and return its report.

    Fails with Blender's own error output when the script raises, Blender is not the pinned release, or the job
    writes nothing.
    """
    executable = binary()
    found = version(executable)
    if not (found == pinned() or found.startswith(pinned() + ".")):
        raise ValueError(f"Blender {found} found; the Studio pins {pinned()} (asset-studio/blender/blender.json)")
    workdir = Path(workdir)
    workdir.mkdir(parents=True, exist_ok=True)
    job_path = workdir / "job.json"
    job_path.write_text(json.dumps(job, indent=1))
    result = subprocess.run([executable, "-b", "--factory-startup", "--python-exit-code", "1", "--python", str(RUNNER),
                             "--", str(job_path)], capture_output=True, text=True, timeout=timeout,
                            env={**os.environ, "PYTHONDONTWRITEBYTECODE": "1"})
    (workdir / "blender.log").write_text(result.stdout + result.stderr)
    if result.returncode != 0 or not Path(job["glb"]).is_file() or not Path(job["report"]).is_file():
        tail = "\n".join((result.stdout + result.stderr).strip().splitlines()[-25:])
        raise ValueError(f"Blender model job failed (log: {workdir / 'blender.log'}):\n{tail}")
    report = json.loads(Path(job["report"]).read_text())
    report["blender"] = found
    return report
