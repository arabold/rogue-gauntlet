"""
Snapshot and compare Asset Studio outputs, so a cleanup can prove it changed nothing it shouldn't have.

    fingerprint.py snapshot NAME [--character family/name ...] [--glb path ...]
    fingerprint.py compare NAME [--atol 1e-6]

`snapshot` records the current outputs; `compare` rebuilds the same targets and reports every difference.
Characters are rebuilt from their recipes: every piece's vertices, faces, material slots and custom normals, plus
the slot colors. GLB files are compared by content hash, so rebuild them before `compare`. Vertex positions may move
by `atol` (vectorizing a loop can reorder float math); everything else must match exactly.

Snapshots live in OS temporary storage, never in the repository. Run with the repository's `.venv` Python.
"""
import argparse
import hashlib
import json
import sys
import tempfile
from pathlib import Path

import numpy as np

REPO = Path(__file__).resolve().parents[4]
STUDIO = REPO / "asset-studio"
STORE = Path(tempfile.gettempdir()) / "rogue-gauntlet-simplify"


def _json_default(value):
    return value.tolist() if hasattr(value, "tolist") else str(value)


def character_outputs(key):
    """The arrays that define a built character, keyed `key:piece:field`."""
    if str(STUDIO) not in sys.path:
        sys.path.insert(0, str(STUDIO))
    from prefabs.creatures import characters

    pieces, colors = characters.build(characters.load(key))
    out = {f"{key}:colors": np.asarray(json.dumps(colors, sort_keys=True, default=_json_default))}
    for name, piece in pieces.items():
        if not hasattr(piece, "verts"):
            out[f"{key}:{name}:repr"] = np.asarray(repr(piece))
            continue
        out[f"{key}:{name}:verts"] = np.asarray(piece.verts, float)
        out[f"{key}:{name}:faces"] = np.asarray(piece.faces, np.int64)
        out[f"{key}:{name}:slots"] = np.asarray([str(s) for s in piece.slots], dtype=str)
        if getattr(piece, "normals", None) is not None:
            out[f"{key}:{name}:normals"] = np.asarray(piece.normals, float)
    return out


def glb_outputs(path):
    data = Path(path).read_bytes()
    return {f"glb:{Path(path).resolve()}": np.asarray(hashlib.sha256(data).hexdigest())}


def collect(targets):
    out = {}
    for key in targets.get("characters", []):
        out.update(character_outputs(key))
    for path in targets.get("glbs", []):
        out.update(glb_outputs(path))
    return out


def snapshot(name, targets):
    STORE.mkdir(parents=True, exist_ok=True)
    arrays = collect(targets)
    keys = sorted(arrays)
    np.savez_compressed(STORE / f"{name}.npz", **{f"a{i}": arrays[k] for i, k in enumerate(keys)})
    (STORE / f"{name}.json").write_text(json.dumps({"targets": targets, "keys": keys}, indent=1))
    print(f"snapshot '{name}': {len(keys)} arrays from {targets} -> {STORE / (name + '.npz')}")


def compare(name, atol):
    meta = json.loads((STORE / f"{name}.json").read_text())
    stored = np.load(STORE / f"{name}.npz", allow_pickle=False)
    before = {k: stored[f"a{i}"] for i, k in enumerate(meta["keys"])}
    after = collect(meta["targets"])
    problems = [f"missing now: {k}" for k in sorted(set(before) - set(after))]
    problems += [f"new now: {k}" for k in sorted(set(after) - set(before))]
    worst = 0.0
    for key in sorted(set(before) & set(after)):
        a, b = before[key], after[key]
        if a.shape != b.shape:
            problems.append(f"{key}: shape {a.shape} -> {b.shape}")
        elif a.dtype.kind == "f":
            delta = float(np.max(np.abs(a - b))) if a.size else 0.0
            worst = max(worst, delta)
            if delta > atol:
                problems.append(f"{key}: moved by up to {delta:.3g}")
        elif not np.array_equal(a, b):
            problems.append(f"{key}: changed")
    for line in problems:
        print(line)
    status = "differs" if problems else "identical"
    print(f"compare '{name}': {status} ({len(before)} arrays, largest float delta {worst:.3g}, atol {atol:g})")
    return 1 if problems else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__.strip().splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    snap = sub.add_parser("snapshot", help="record the current outputs of the given targets")
    snap.add_argument("name")
    snap.add_argument("--character", action="append", default=[], help="character key, e.g. orc/orc_king")
    snap.add_argument("--glb", action="append", default=[], help="staged GLB file to hash")
    comp = sub.add_parser("compare", help="rebuild a snapshot's targets and diff them")
    comp.add_argument("name")
    comp.add_argument("--atol", type=float, default=1e-6)
    args = parser.parse_args()
    if args.command == "snapshot":
        if not (args.character or args.glb):
            parser.error("snapshot needs at least one --character or --glb")
        snapshot(args.name, {"characters": args.character, "glbs": args.glb})
        return 0
    return compare(args.name, args.atol)


if __name__ == "__main__":
    sys.exit(main())
