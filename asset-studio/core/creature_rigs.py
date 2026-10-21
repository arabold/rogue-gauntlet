"""Independent creature rigs, animation roles, and binding of original static art.

Technical rigs contain skeletons and animation only. Selected art uses the normal
character export workflow; donor comparisons and weight transfer need an explicit external reference.
"""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import subprocess

import numpy as np
from PIL import Image, ImageDraw
from scipy.spatial import cKDTree

from core import export, mesh
from core.paths import work_root
from core.rig import _sample
from items import model

STUDIO = Path(__file__).resolve().parents[1]
PROFILES = STUDIO / "rigs/creatures"


def profile(name):
    """Load an explicit rig family; never infer anatomy from humanoid conventions."""
    if name not in {p.parent.name for p in PROFILES.glob("*/profile.json")}:
        raise ValueError(f"Unknown creature rig: {name}")
    p = json.loads((PROFILES / name / "profile.json").read_text())
    if p.get("version") != 1 or not np.isfinite(p["scale"]) or p["scale"] <= 0:
        raise ValueError("Invalid creature profile version or scale")
    actual = hashlib.sha256((PROFILES / name / "rig.glb").read_bytes()).hexdigest()
    if actual != p["rig_sha256"]:
        raise ValueError("Technical rig changed; verify provenance and update its profile")
    return p


def source(name):
    return PROFILES / name / "rig.glb"


def _doc(path):
    g, binary, acc = mesh.read_glb(str(path))
    if any("uri" in x for x in g.get("buffers", []) + g.get("images", [])):
        raise ValueError("Creature inputs must be self-contained GLBs")
    if any("sparse" in a for a in g.get("accessors", [])):
        raise ValueError("Sparse accessors must be expanded before creature binding")
    if g.get("extensionsRequired"):
        raise ValueError("Decode required glTF extensions before creature binding")
    return g, binary, acc


def _data(w, g, binary, required=None):
    """Copy accessors with their complete buffer views, preserving strides and offsets."""
    accessors, views = {}, {}
    for i in (range(len(g.get("accessors", []))) if required is None else sorted(required)):
        a = copy.deepcopy(g["accessors"][i])
        v = a["bufferView"]
        if v not in views:
            old = g["bufferViews"][v]
            start = old.get("byteOffset", 0)
            views[v] = w._view(binary[start:start + old["byteLength"]], old.get("target"))
            if "byteStride" in old:
                w.g["bufferViews"][views[v]]["byteStride"] = old["byteStride"]
        a["bufferView"] = views[v]
        accessors[i] = len(w.g["accessors"])
        w.g["accessors"].append(a)
    return accessors


def _clips(g, p):
    original = {a["name"]: a for a in g.get("animations", [])}
    result = []
    for role, name in p["clips"].items():
        if name not in original:
            raise ValueError(f"Missing source clip {name!r}")
        a = copy.deepcopy(original[name])
        a["name"] = role
        a["extras"] = {"source_clip": name, "loop": role in p["loops"]}
        result.append(a)
    return result


def _remap_clips(animations, accessors, offset=0):
    for a in animations:
        for s in a["samplers"]:
            s["input"], s["output"] = accessors[s["input"]], accessors[s["output"]]
        for ch in a["channels"]:
            ch["target"]["node"] += offset


def _complete_clips(w):
    """Restore omitted animated properties so clip switches cannot inherit stale poses.

    Source clips omit constant tracks. A runtime mixer can leave those properties
    at the preceding clip's values; explicitly key their original rest transforms.
    Original keyed channels are preserved unchanged.
    """
    animations = w.g["animations"]
    targets = {(ch["target"]["node"], ch["target"]["path"])
               for a in animations for ch in a["channels"]}
    defaults = {"translation": [0., 0., 0.], "rotation": [0., 0., 0., 1.], "scale": [1., 1., 1.]}
    for a in animations:
        present = {(ch["target"]["node"], ch["target"]["path"]) for ch in a["channels"]}
        missing = sorted(targets - present)
        if not missing:
            continue
        duration = max(w.g["accessors"][sampler["input"]]["max"][0] for sampler in a["samplers"])
        times = w.add(np.array([[0.], [duration]]), "SCALAR", 5126, bounds=True)
        for node, path in missing:
            if path not in defaults or "matrix" in w.g["nodes"][node]:
                raise ValueError("Creature rest-track completion requires node TRS transforms")
            value = w.g["nodes"][node].get(path, defaults[path])
            values = w.add(np.tile(value, (2, 1)), "VEC4" if path == "rotation" else "VEC3", 5126)
            a["channels"].append({"sampler": len(a["samplers"]), "target": {"node": node, "path": path}})
            a["samplers"].append({"input": times, "output": values, "interpolation": "LINEAR"})
        a.setdefault("extras", {})["restored_constant_tracks"] = len(missing)


def extract(name, out):
    """Extract only skeleton, bind data and selected clips; no mesh/texture bytes."""
    p = profile(name)
    g, binary, _ = _doc(source(name))
    w = export._Writer()
    w.g = {"asset": {"version": "2.0", "generator": export.GENERATOR},
           "scene": g.get("scene", 0), "scenes": copy.deepcopy(g["scenes"]),
           "nodes": copy.deepcopy(g["nodes"]), "skins": copy.deepcopy(g["skins"]),
           "animations": _clips(g, p), "bufferViews": [], "accessors": []}
    for n in w.g["nodes"]:
        for key in ("mesh", "skin", "weights"):
            n.pop(key, None)
    needed = {s["inverseBindMatrices"] for s in w.g["skins"]}
    needed.update(s[k] for a in w.g["animations"] for s in a["samplers"] for k in ("input", "output"))
    am = _data(w, g, binary, needed)
    for s in w.g["skins"]:
        s["inverseBindMatrices"] = am[s["inverseBindMatrices"]]
    _remap_clips(w.g["animations"], am)
    _complete_clips(w)
    w.save(str(out))
    return Path(out)


def donor(name, reference=None):
    """All active mesh primitives in bind/world space, including normalized weights."""
    if reference is None:
        raise ValueError("Weight transfer requires --reference pointing to an external donor GLB")
    g, _, acc = _doc(reference)
    worlds = mesh._node_worlds(g)
    positions, bones, weights = [], [], []
    active = set()
    def visit(i):
        if i in active:
            return
        active.add(i)
        for c in g["nodes"][i].get("children", []):
            visit(c)
    for i in g["scenes"][g.get("scene", 0)]["nodes"]:
        visit(i)
    for i in sorted(active):
        n = g["nodes"][i]
        if "mesh" not in n or "skin" not in n:
            continue
        sk = g["skins"][n["skin"]]
        for prim in g["meshes"][n["mesh"]]["primitives"]:
            a = prim["attributes"]
            v = np.asarray(acc(a["POSITION"]), float)
            j = np.asarray(acc(a["JOINTS_0"]), int)
            wt = np.asarray(acc(a["WEIGHTS_0"]), float)
            if not np.isfinite(wt).all() or np.any(wt < 0) or np.any(wt.sum(1) <= 0):
                raise ValueError("Invalid donor skin weights")
            wt /= wt.sum(1, keepdims=True)
            positions.append(v @ worlds[i][:3, :3].T + worlds[i][:3, 3])
            bones.append(np.array([[g["nodes"][sk["joints"][k]]["name"] for k in row] for row in j], object))
            weights.append(wt)
    if not positions:
        raise ValueError("Rig has no skinned weight reference")
    return np.vstack(positions), np.vstack(bones), np.vstack(weights)


def inspect(name):
    p = profile(name)
    g, _, acc = _doc(source(name))
    joints = [g["nodes"][j]["name"] for j in g["skins"][0]["joints"]]
    if len(joints) != len(set(joints)):
        raise ValueError("Joint names must be unique within a creature rig")
    missing = set(p["bones"].values()) - set(joints)
    if missing:
        raise ValueError(f"Unknown profile bones: {sorted(missing)}")
    clips = _clips(g, p)
    return {"name": name, "family": p["family"], "joints": joints,
            "clips": {a["name"]: max(float(acc(s["input"]).max()) for s in a["samplers"]) for a in clips},
            "loops": p["loops"], "bones": p["bones"], "scale": p["scale"],
            "geometry": "none; technical rig only",
            "license": p["license"], "rig_sha256": hashlib.sha256(source(name).read_bytes()).hexdigest()}


def _reference(name, out, reference=None):
    """Preserve donor visuals for motion review, with role names and game-unit scale."""
    p = profile(name)
    if reference is None:
        raise ValueError("Original appearance review requires an explicit external --reference GLB")
    g, binary, _ = _doc(reference)
    p = {**p, "clips": p["source_clips"]}
    # Existing accessor/view numbering is preserved because reference art is unchanged.
    g = copy.deepcopy(g)
    g["animations"] = _clips(g, p)
    roots = g["scenes"][g.get("scene", 0)]["nodes"]
    roots_copy = list(roots)
    roots[:] = [len(g["nodes"])]
    g["nodes"].append({"name": "CreatureScale", "scale": [p["scale"]] * 3, "children": roots_copy})
    w = export._Writer()
    w.g, w.bin = g, bytearray(binary)
    _complete_clips(w)
    w.save(str(out))


def bind(name, art, rig_path, out, attachments=None, reference=None):
    """Bind original static GLB art, preserving all materials, UVs and primitives.

    Author art in game units aligned with reference.glb's rest pose (+Z forward).
    Optional mesh-node-name -> profile bone role/joint binds rigid parts explicitly.
    Other vertices transfer weights from the nearest donor surface vertex. Inspect
    resulting deformation; a shared rig does not imply automatic proportion fitting.
    """
    p = profile(name)
    g, chunks = model.read(art)
    _doc(art)
    if g.get("animations"):
        raise ValueError("Binding requires static art without existing animations")
    rg, rb, _ = _doc(rig_path)
    if len(rg["skins"]) != 1:
        raise ValueError("Creature binding currently requires one skeleton")
    g = copy.deepcopy(g)
    w = export._Writer()
    # Keep the art's complete binary layout, so embedded textures and authored surfaces survive.
    _, art_binary, _ = mesh.read_glb(str(art))
    w.g, w.bin = g, bytearray(art_binary)
    w.g.setdefault("accessors", [])
    w.g.setdefault("bufferViews", [])
    worlds = mesh._node_worlds(g)
    count = len(g["nodes"])
    old_nodes = list(g["nodes"])
    am = _data(w, rg, rb)
    for n in copy.deepcopy(rg["nodes"]):
        if "children" in n:
            n["children"] = [i + count for i in n["children"]]
        g["nodes"].append(n)
    skeleton_roots = [i + count for i in rg["scenes"][rg.get("scene", 0)]["nodes"]]
    parent = len(g["nodes"])
    g["nodes"].append({"name": "CreatureRig", "scale": [p["scale"]] * 3, "children": skeleton_roots})
    g["scenes"][g.get("scene", 0)]["nodes"].append(parent)
    g["animations"] = copy.deepcopy(rg["animations"])
    _remap_clips(g["animations"], am, count)
    joints = rg["skins"][0]["joints"]
    names = {rg["nodes"][n]["name"]: i for i, n in enumerate(joints)}
    rw = mesh._node_worlds(rg)
    scale = np.diag([p["scale"], p["scale"], p["scale"], 1.])
    joint_world = np.array([scale @ rw[n] for n in joints])
    points, bone_names, wt = donor(name, reference)
    tree = cKDTree(points * p["scale"])
    attachments = attachments or {}
    unknown = set(attachments) - {n.get("name") for n in old_nodes if "mesh" in n}
    if unknown:
        raise ValueError(f"Unknown attachment mesh nodes: {sorted(unknown)}")
    _, _, acc = mesh.read_glb(str(art))
    g["skins"] = []
    # Visit only the active art scene; unused scenes do not acquire a skeleton.
    active = set()
    def visit(i):
        if i in active:
            return
        active.add(i)
        for c in old_nodes[i].get("children", []):
            visit(c)
    for i in list(g["scenes"][g.get("scene", 0)]["nodes"]):
        if i < count:
            visit(i)
    for i in sorted(active):
        n = old_nodes[i]
        if "mesh" not in n:
            continue
        original_mesh = copy.deepcopy(g["meshes"][n["mesh"]])
        # Instanced geometry needs separate weights and bind matrices per transform.
        n["mesh"] = len(g["meshes"])
        g["meshes"].append(original_mesh)
        for prim in original_mesh["primitives"]:
            if prim.get("mode", 4) != 4 or prim.get("targets"):
                raise ValueError("Creature art must contain static triangle primitives")
            attrs = prim["attributes"]
            v = np.asarray(acc(attrs["POSITION"]), float)
            if not np.isfinite(v).all():
                raise ValueError("Nonfinite creature vertices")
            position = v @ worlds[i][:3, :3].T + worlds[i][:3, 3]
            rigid = attachments.get(n.get("name"))
            if rigid is not None:
                bone = p["bones"].get(rigid, rigid)
                if bone not in names:
                    raise ValueError(f"Unknown attachment bone: {rigid}")
                j = np.full((len(v), 4), names[bone], dtype=int)
                weights = np.tile([1., 0., 0., 0.], (len(v), 1))
            else:
                _, nearest = tree.query(position)
                j = np.array([[names[b] for b in row] for row in bone_names[nearest]], int)
                weights = wt[nearest]
            attrs["JOINTS_0"] = w.add(j, "VEC4", 5123, 34962)
            attrs["WEIGHTS_0"] = w.add(weights, "VEC4", 5126, 34962)
        ibm = np.linalg.inv(joint_world) @ worlds[i]
        n["skin"] = len(g["skins"])
        g["skins"].append({"joints": [j + count for j in joints],
                           "inverseBindMatrices": w.add(ibm.transpose(0, 2, 1).reshape(-1, 16), "MAT4", 5126)})
    if not g["skins"]:
        raise ValueError("Active creature scene contains no mesh")
    g["asset"]["generator"] = export.GENERATOR
    w.save(str(out))


def build(name, art=None, attachments=None, reference=None):
    if art is None and reference is None:
        from studio import Character
        from core import character_workflow
        if not (STUDIO / "characters/beasts" / name / "character.json").exists():
            raise ValueError("No authored creature yet; use extract for its technical rig or provide --reference")
        return character_workflow.build(Character("beasts/" + name))
    folder = work_root() / "creatures" / name / ("custom" if art else "reference")
    folder.mkdir(parents=True, exist_ok=True)
    report = inspect(name)
    extract(name, folder / "rig.glb")
    target = folder / "model.glb"
    if art:
        bind(name, art, folder / "rig.glb", target, attachments, reference)
    else:
        _reference(name, target, reference)
    report["art"] = "bound_static_mesh" if art else "third_party_motion_reference"
    report["model"] = str(target)
    if art:
        report["mesh_sha256"] = hashlib.sha256(Path(art).read_bytes()).hexdigest()
    (folder / "manifest.json").write_text(json.dumps(report, indent=2) + "\n")
    return folder


def motion_bounds(path):
    """Union sampled skinned bounds across clips; importer rest AABBs miss long tails."""
    g, _, acc = _doc(path)
    meshes = []
    for n in g["nodes"]:
        if "mesh" not in n or "skin" not in n:
            continue
        sk = g["skins"][n["skin"]]
        ibm = acc(sk["inverseBindMatrices"]).reshape(-1, 4, 4).transpose(0, 2, 1)
        for prim in g["meshes"][n["mesh"]]["primitives"]:
            a = prim["attributes"]
            v = np.asarray(acc(a["POSITION"]), float)
            j = np.asarray(acc(a["JOINTS_0"]), int)
            wt = np.asarray(acc(a["WEIGHTS_0"]), float)
            wt /= wt.sum(1, keepdims=True)
            meshes.append((v, j, wt, sk["joints"], ibm))
    bounds = []
    for anim in g["animations"]:
        duration = max(float(acc(s["input"]).max()) for s in anim["samplers"])
        for t in np.linspace(0, duration, 7):
            posed = {**g, "nodes": [dict(n) for n in g["nodes"]]}
            for ch in anim["channels"]:
                target = ch["target"]
                if target["path"] == "weights":
                    raise ValueError("Creature motion bounds do not support morph animation")
                sampler = anim["samplers"][ch["sampler"]]
                if sampler.get("interpolation", "LINEAR") != "LINEAR":
                    raise ValueError("Creature motion requires LINEAR sampled clips")
                value = _sample(acc(sampler["input"])[:, 0], acc(sampler["output"]), t, target["path"] == "rotation")
                posed["nodes"][target["node"]][target["path"]] = value.tolist()
                posed["nodes"][target["node"]].pop("matrix", None)
            worlds = mesh._node_worlds(posed)
            for v, j, wt, joints, ibm in meshes:
                matrices = np.array([worlds[i] for i in joints]) @ ibm
                points = np.zeros_like(v)
                for k in range(4):
                    m = matrices[j[:, k]]
                    points += (np.einsum("nij,nj->ni", m[:, :3, :3], v) + m[:, :3, 3]) * wt[:, k, None]
                bounds.extend([points.min(0), points.max(0)])
    bounds = np.asarray(bounds)
    if not len(bounds) or not np.isfinite(bounds).all():
        raise ValueError("Cannot frame creature motion")
    return [bounds.min(0).tolist(), bounds.max(0).tolist()]


def lineup(name, art=None, attachments=None, reference=None):
    """Render every mapped clip in Godot with a fixed camera; no publication."""
    if art is None and reference is None:
        from studio import Character
        from core import character_workflow
        return character_workflow.lineup(Character("beasts/" + name))
    folder = build(name, art, attachments, reference)
    frames = folder / "frames"
    frames.mkdir(exist_ok=True)
    p = profile(name)
    job = folder / "render-job.json"
    job.write_text(json.dumps([[str(folder / "model.glb"), str(frames), list(p["clips"]), motion_bounds(folder / "model.glb")]]))
    command = [str(STUDIO / "godot/godot.sh"),
               "--path", str(STUDIO.parent), "--script", str(STUDIO / "godot/render_creature.gd"), "--", str(job)]
    result = subprocess.run(command, capture_output=True, text=True, timeout=300)
    if result.returncode or "CREATURE_REVIEW_COMPLETE" not in result.stdout:
        raise ValueError("Creature rendering failed:\n" + result.stdout + result.stderr)
    captures = json.loads((frames / "captures.json").read_text())
    images, durations = [], []
    for c in captures:
        with Image.open(frames / (c["label"] + ".png")) as im:
            frame = im.convert("RGB")
        ImageDraw.Draw(frame).text((12, 12), name + " | " + c["clip"] + (" | source reference" if not art else " | bound art"), fill="white")
        images.append(frame)
        durations.append(max(30, round(c["duration"] * 1000 / 12)))
    images[0].save(folder / "motion.gif", save_all=True, append_images=images[1:], duration=durations, loop=0)
    tiles = [images[i * 12 + (11 if role == "Death" else 4)].resize((256, 256))
             for i, role in enumerate(p["clips"])]
    sheet = Image.new("RGB", (256 * 4, 256 * ((len(tiles) + 3) // 4)), (20, 23, 31))
    for i, tile in enumerate(tiles):
        sheet.paste(tile, (256 * (i % 4), 256 * (i // 4)))
    sheet.save(folder / "lineup.png")
    import shutil
    shutil.rmtree(frames)
    return folder


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("command", choices=["list", "inspect", "extract", "build", "lineup"])
    ap.add_argument("name", nargs="?")
    ap.add_argument("--mesh", type=Path, help="Original static GLB, in game units and the reference bind pose")
    ap.add_argument("--attachments", type=Path, help="JSON mesh-node name -> bone role/joint for rigid parts")
    ap.add_argument("--reference", type=Path, help="Explicit external donor for original-art review or static-mesh weight transfer")
    args = ap.parse_args(argv)
    names = sorted(p.parent.name for p in PROFILES.glob("*/profile.json")) if args.command == "list" else [args.name]
    for name in names:
        if name is None:
            ap.error("A creature rig name is required")
        if args.command == "list":
            info = inspect(name)
            print(f"{name:10s} {info['family']:10s} {len(info['joints']):2d} joints  " + ", ".join(info["clips"]))
        elif args.command == "inspect":
            print(json.dumps(inspect(name), indent=2))
        elif args.command == "extract":
            folder = work_root() / "creatures" / name
            folder.mkdir(parents=True, exist_ok=True)
            print(extract(name, folder / "rig.glb"))
        else:
            attachments = json.loads(args.attachments.read_text()) if args.attachments else None
            if attachments and not args.mesh:
                ap.error("--attachments requires --mesh")
            print((lineup if args.command == "lineup" else build)(name, args.mesh, attachments, args.reference))
