"""Explicit publication receipts bind reviewed artifacts to their source inputs."""
import hashlib
import json
import shutil
from pathlib import Path


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def fingerprint(studio, recipe):
    """Conservatively invalidate review when shared modeling, shading or rig inputs change."""
    studio, recipe = Path(studio), Path(recipe)
    paths = {recipe}
    for directory in ("core", "prefabs", "items", "godot", "rigs"):
        paths.update(p for p in (studio / directory).rglob("*")
                     if p.is_file() and p.suffix in (".py", ".gd", ".glb", ".png"))
    paths.update(p for p in recipe.parent.iterdir() if p.is_file()
                 and (p.name.startswith("source.") or p.name.startswith("reference.")))
    data = json.loads(recipe.read_text())
    if recipe.name == "item.json":
        for field in ("source", "reference"):
            value = data.get(field, "")
            path = studio.parent / value[6:] if value.startswith("res://") else recipe.parent / value
            if value and path.is_file():
                paths.add(path)
    else:
        paths.add(studio / "fittings.py")
    hashes = {str(p.relative_to(studio.parent)): digest(p) for p in sorted(paths)}
    return hashlib.sha256(json.dumps(hashes, sort_keys=True).encode()).hexdigest()


def stage_stamp(folder, inputs):
    """Record the inputs used to build a candidate; later edits require a fresh build."""
    folder.mkdir(parents=True, exist_ok=True)
    (folder / "candidate.json").write_text(json.dumps({"inputs": inputs}, indent=2) + "\n")


def render_stamp(folder, inputs, files):
    """Bind the rendered review to the exact staged model and images."""
    stamp = json.loads((folder / "candidate.json").read_text())
    if stamp["inputs"] != inputs:
        raise ValueError("Candidate inputs changed; rebuild before rendering")
    stamp["files"] = {name: digest(folder / name) for name in files}
    (folder / "review.json").write_text(json.dumps(stamp, indent=2) + "\n")


def publish(source, destination, key, inputs, files, metadata=None):
    """Publish only complete, unchanged review candidates; never discover drafts by globbing."""
    source, destination = Path(source), Path(destination)
    review = json.loads((source / "review.json").read_text())
    if review["inputs"] != inputs:
        raise ValueError("Candidate inputs changed; rebuild and render before publishing")
    for name in files:
        if review.get("files", {}).get(name) != digest(source / name):
            raise ValueError(f"Unreviewed or changed artifact: {name}; render again")
    marker = destination / "manifest.json"
    if destination.exists() and any(destination.iterdir()):
        if not marker.exists() or json.loads(marker.read_text()).get("key") != key:
            raise ValueError(f"Publication destination contains unrelated files: {destination}")
    destination.mkdir(parents=True, exist_ok=True)
    for name in files:
        shutil.copy2(source / name, destination / name)
    receipt = {"key": key, "inputs": inputs, "files": {name: digest(destination / name) for name in files},
               "metadata": metadata or {}}
    marker.write_text(json.dumps(receipt, indent=2) + "\n")
    return receipt


def read(destination, inputs):
    """Verify final artifact integrity and expose source drift without endorsing a newer recipe."""
    destination = Path(destination)
    record = json.loads((destination / "manifest.json").read_text())
    for name, expected in record["files"].items():
        if digest(destination / name) != expected:
            raise ValueError(f"Published artifact changed: {destination / name}")
    record["source_current"] = record["inputs"] == inputs
    return record
