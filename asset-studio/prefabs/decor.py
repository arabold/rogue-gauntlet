#!/usr/bin/env python3
"""Colored decor prefabs harvested from KayKit's CC0 packs: books, bottles, candles, coins, keys, a skull.

Unlike the uncolored domain prefabs, decor keeps its original color regions (cover, pages and clasp;
glass, label and cork; bone and eye sockets) so it can furnish tiles and models exactly as KayKit drew
it. Each prefab stores per-face slots, corner normals, gradient positions on KayKit's texture swatches
(`uv_t`), and its palette and material specs, including each swatch's exact gradient. Harvested meshes
stand on y = 0, centered in x and z, facing +z, at the size they read best beside KayKit's 4-unit walls.

    ./.venv/bin/python asset-studio/prefabs/decor.py          # re-harvest every decor prefab
"""

import io
import json
import struct
import sys
from functools import lru_cache
from pathlib import Path

import numpy as np
from PIL import Image

STUDIO_DIR = Path(__file__).resolve().parents[1]
if str(STUDIO_DIR) not in sys.path:
    sys.path.insert(0, str(STUDIO_DIR))

from core import kit  # noqa: E402

ASSETS = STUDIO_DIR.parent / "assets"
MESHES = Path(__file__).resolve().parent / "decor" / "meshes"
DUNGEON = ASSETS / "kaykit-dungeon-remastered"
GRID = (8, 4)  # KayKit dungeon_texture.png: 8 columns by 4 rows of vertical gradient swatches
# name: (source file under assets/, meshes to keep or None for all, pose fix, scale)
DECOR = {
    "book_purple": ("kaykit-dungeon-legacy/bookB.gltf.glb", None, "standing", 1.1),
    "book_black": ("kaykit-dungeon-legacy/bookC.gltf.glb", None, "standing", 1.1),
    "book_gray": ("kaykit-dungeon-legacy/bookD.gltf.glb", None, "standing", 1.1),
    "book_teal": ("kaykit-dungeon-legacy/bookE.gltf.glb", None, "standing", 1.1),
    "book_blue": ("kaykit-dungeon-legacy/bookF.gltf.glb", None, "standing", 1.1),
    "book_open": ("kaykit-dungeon-legacy/bookOpenA.gltf.glb", None, None, .85),
    "spellbook": ("kaykit-dungeon-legacy/spellBook.gltf.glb", None, None, .7),
    "mug": ("kaykit-dungeon-legacy/mug.gltf.glb", None, None, 1.0),
    "stones": ("kaykit-dungeon-legacy/bricks.gltf.glb", None, None, .6),
    "bottle_green": ("kaykit-dungeon-remastered/bottle_A_labeled_green.gltf", None, None, .55),
    "bottle_brown": ("kaykit-dungeon-remastered/bottle_B_brown.gltf", None, None, .5),
    "flask_brown": ("kaykit-dungeon-remastered/bottle_C_brown.gltf", None, None, .5),
    "candles": ("kaykit-dungeon-remastered/candle_triple.gltf", None, None, .7),
    "candle_melted": ("kaykit-dungeon-remastered/candle_melted.gltf", None, None, .75),
    "candle_lit": ("kaykit-dungeon-remastered/candle_lit.gltf", None, None, .7),
    "coins": ("kaykit-dungeon-remastered/coin_stack_small.gltf", None, None, .4),
    "keyring": ("kaykit-dungeon-remastered/keyring.gltf", None, None, .45),
    "keyring_hanging": ("kaykit-dungeon-remastered/keyring_hanging.gltf", None, None, .8),
    "plate": ("kaykit-dungeon-remastered/plate_small.gltf", None, None, .8),
    "crate": ("kaykit-dungeon-remastered/box_small.gltf", None, None, .38),
    "skull": ("kaykit-skeletons/Skeleton_Minion.glb", ("Skeleton_Minion_Head", "Skeleton_Minion_Jaw"), None, .5),
}
COMPONENTS = {5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8}
WIDTH = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}


def accessor(g, binary, index):
    a = g["accessors"][index]
    view = g["bufferViews"][a["bufferView"]]
    width = WIDTH[a["type"]]
    data = np.frombuffer(binary, COMPONENTS[a["componentType"]], a["count"] * width,
                         view.get("byteOffset", 0) + a.get("byteOffset", 0))
    return data.reshape(a["count"], width) if width > 1 else data


def gradient_column(texture, r, c, samples=32):
    """One swatch's vertical gradient, top to bottom, sampled down the cell as KayKit's UVs read it."""
    h, w = texture.shape[:2]
    rows = ((r + (np.arange(samples) + .5) / samples) * h / GRID[1]).astype(int)
    return [tuple(int(v) for v in texture[y, int((c + .4) * w / GRID[0])]) for y in rows]


def swatch_faces(uv, faces, texture, prefix):
    """Per-face swatch slots on a KayKit 8x4 gradient atlas, each corner's position down its swatch
    (0 light top, 1 dark bottom), and every used swatch's middle color and exact gradient."""
    center = uv[faces].mean(axis=1)
    col = np.clip((center[:, 0] % 1) * GRID[0], 0, GRID[0] - 1).astype(int)
    row = np.clip((center[:, 1] % 1) * GRID[1], 0, GRID[1] - 1).astype(int)
    height, width = texture.shape[:2]
    colors, specs = {}, {}
    for c, r in set(zip(col.tolist(), row.tolist())):
        slot = f"{prefix}_{r}{c}"
        colors[slot] = tuple(int(v) for v in texture[int((r + .5) * height / GRID[1]), int((c + .4) * width / GRID[0])])
        specs[slot] = {"gradient": gradient_column(texture, r, c)}
    slots = [f"{prefix}_{r}{c}" for c, r in zip(col, row)]
    ramp_t = np.clip((uv[faces][:, :, 1] % 1) * GRID[1] - row[:, None], 0, 1)
    return slots, ramp_t, colors, specs


def swatch_piece(path):
    """A KayKit dungeon .gltf whose faces sample `dungeon_texture.png`: one slot per swatch, each
    corner's gradient position (`uv_t`), and the swatches' exact gradients as material specs."""
    g = json.loads(path.read_text())
    binary = (path.parent / g["buffers"][0]["uri"]).read_bytes()
    texture = np.asarray(Image.open(path.parent / g["images"][0]["uri"]).convert("RGB"), float)
    if any(n.get("translation") or n.get("rotation") or n.get("scale") for n in g["nodes"] if "mesh" in n):
        raise ValueError(f"{path.name}: transformed KayKit nodes are not supported")
    verts, faces, normals, slots, colors, specs, ramp_t = [], [], [], [], {}, {}, []
    for primitive in (p for m in g["meshes"] for p in m["primitives"]):
        p = accessor(g, binary, primitive["attributes"]["POSITION"]).astype(float)
        uv = accessor(g, binary, primitive["attributes"]["TEXCOORD_0"]).astype(float)
        n = accessor(g, binary, primitive["attributes"]["NORMAL"]).astype(float)
        f = accessor(g, binary, primitive["indices"]).astype(np.int64).reshape(-1, 3)
        face_slots, t, c, sp = swatch_faces(uv, f, texture, "kaykit")
        colors.update(c)
        specs.update(sp)
        ramp_t.append(t)
        faces.append(f + sum(len(v) for v in verts))
        verts.append(p)
        normals.append(n[f])
        slots += face_slots
    piece = kit.Piece(np.vstack(verts), np.vstack(faces), np.array(slots, object), normals=np.vstack(normals),
                      uv_t=np.vstack(ramp_t))
    return piece, colors, specs


@lru_cache(maxsize=None)
def gradients():
    """Every dungeon swatch's exact gradient, keyed by its `kaykit_rc` slot."""
    texture = np.asarray(Image.open(DUNGEON / "dungeon_texture.png").convert("RGB"), float)
    return {f"kaykit_{r}{c}": gradient_column(texture, r, c) for r in range(GRID[1]) for c in range(GRID[0])}


def srgb(linear):
    linear = np.clip(np.asarray(linear, float), 0, 1)
    return np.where(linear <= .0031308, linear * 12.92, 1.055 * linear ** (1 / 2.4) - .055) * 255


def material_piece(path, meshes=None):
    """A .glb with flat material colors (legacy dungeon) or a KayKit gradient atlas (skeletons), from
    the raw rest-pose positions. Flat colors sit at their base, as KayKit draws them; atlas faces keep
    their swatch and gradient position like the dungeon props."""
    data = path.read_bytes()
    length = struct.unpack("<I", data[12:16])[0]
    g, binary = json.loads(data[20:20 + length]), data[28 + length:]
    textures = {}
    verts, faces, normals, slots, colors, materials, ramp_t = [], [], [], [], {}, {}, []
    for mesh in g["meshes"]:
        if meshes and mesh["name"] not in meshes:
            continue
        for primitive in mesh["primitives"]:
            p = accessor(g, binary, primitive["attributes"]["POSITION"]).astype(float)
            n = accessor(g, binary, primitive["attributes"]["NORMAL"]).astype(float)
            f = accessor(g, binary, primitive["indices"]).astype(np.int64).reshape(-1, 3)
            material = g["materials"][primitive["material"]]
            pbr = material.get("pbrMetallicRoughness", {})
            spec = {"metallic": float(pbr.get("metallicFactor", 0.0)), "roughness": float(pbr.get("roughnessFactor", .5))}
            if "baseColorTexture" in pbr:
                image = g["images"][g["textures"][pbr["baseColorTexture"]["index"]]["source"]]
                if image["bufferView"] not in textures:
                    view = g["bufferViews"][image["bufferView"]]
                    start = view.get("byteOffset", 0)
                    textures[image["bufferView"]] = np.asarray(Image.open(io.BytesIO(binary[start:start + view["byteLength"]])).convert("RGB"), float)
                uv = accessor(g, binary, primitive["attributes"]["TEXCOORD_0"]).astype(float)
                prefix = "kay_" + image.get("name", "atlas").replace("_texture", "").lower()
                names, t, c, specs = swatch_faces(uv, f, textures[image["bufferView"]], prefix)
                colors.update(c)
                for name, extra in specs.items():
                    materials[name] = {**spec, **extra}
            else:
                name = "decor_" + material.get("name", "base").lower()
                colors[name] = tuple(int(c) for c in srgb(pbr.get("baseColorFactor", [1, 1, 1, 1])[:3]))
                materials[name] = spec
                names, t = [name] * len(f), np.full((len(f), 3), .5)
            faces.append(f + sum(len(v) for v in verts))
            verts.append(p)
            normals.append(n[f])
            slots += names
            ramp_t.append(t)
    piece = kit.Piece(np.vstack(verts), np.vstack(faces), np.array(slots, object), normals=np.vstack(normals),
                      uv_t=np.vstack(ramp_t))
    return piece, colors, materials


def harvest(names=None):
    """Read each decor source, fix its pose and size, and save it to `decor/meshes/<name>.npz`."""
    MESHES.mkdir(parents=True, exist_ok=True)
    for name in names or DECOR:
        file, meshes, pose, scale = DECOR[name]
        path = ASSETS / file
        piece, colors, materials = swatch_piece(path) if path.suffix == ".gltf" else material_piece(path, meshes)
        v, n = piece.verts, piece.normals
        if pose == "standing":
            # Legacy books lie along x: stand them with the spine out (+z), pages and clasp toward the back.
            pages = v[piece.faces[piece.slots == "decor_white"]].reshape(-1, 3)[:, 0].mean()
            sign = 1.0 if pages > v[:, 0].mean() else -1.0
            turn = np.array([[0, 0, sign], [0, 1, 0], [-sign, 0, 0]])
            v, n = v @ turn.T, n @ turn.T
        lo, hi = v.min(0), v.max(0)
        v = (v - [(lo[0] + hi[0]) / 2, lo[1], (lo[2] + hi[2]) / 2]) * scale
        uv_t = piece.uv_t
        from items.solid import inspect
        report = inspect(v, piece.faces)
        meta = {"name": name, "category": "decor", "domain": "decor", "source": file, "license": "CC0 (KayKit)",
                # KayKit's own topology, kept as authored (a few props are open or share edges).
                "topology": {k: int(report[k]) for k in ("boundary_edges", "nonmanifold_edges")},
                "verts": len(v), "tris": len(piece.faces), "span": [round(float(x), 4) for x in np.ptp(v, axis=0)],
                "colors": colors, "materials": materials}
        np.savez_compressed(MESHES / f"{name}.npz", verts=v.astype(np.float32), faces=piece.faces.astype(np.int32),
                            slots=np.asarray(piece.slots, str), normals=np.asarray(n, np.float32),
                            uv_t=np.asarray(uv_t, np.float32), meta=json.dumps(meta))
        print(f"  [+] {name:16s} {len(piece.faces):5d} tris  {meta['span']}  <- {file}")


@lru_cache(maxsize=None)
def _stored(name):
    path = MESHES / f"{name}.npz"
    if not path.exists():
        raise FileNotFoundError(f"Decor prefab {name!r} is not harvested; run prefabs/decor.py")
    with np.load(path) as data:
        meta = json.loads(str(data["meta"]))
        return (data["verts"].astype(float), data["faces"].astype(np.int64), data["slots"].astype(object),
                data["normals"].astype(float), data["uv_t"].astype(float), meta)


def load(name, at=(0.0, 0.0, 0.0), yaw=0.0, scale=1.0, tilt=0.0):
    """A colored decor prefab, tipped `tilt` degrees about +z onto its side (resting on its new base),
    turned `yaw` degrees about +y, scaled and set down at `at`."""
    v, f, slots, n, uv_t, _ = _stored(name)
    v = v * scale
    if tilt:
        t = np.radians(tilt)
        rz = np.array([[np.cos(t), -np.sin(t), 0], [np.sin(t), np.cos(t), 0], [0, 0, 1.0]])
        v, n = v @ rz.T, n @ rz.T
        v = v - [0, v[:, 1].min(), 0]
    a = np.radians(yaw)
    ry = np.array([[np.cos(a), 0, np.sin(a)], [0, 1, 0], [-np.sin(a), 0, np.cos(a)]])
    return kit.Piece(v @ ry.T + np.asarray(at, float), f, slots, normals=n @ ry.T, uv_t=uv_t)


def authored(name):
    """Open and shared edges KayKit authored into a prefab, which models placing it inherit."""
    return dict(_stored(name)[5]["topology"])


def size(name):
    """Width, height and depth of a decor prefab at its stored scale."""
    return np.asarray(_stored(name)[5]["span"], float)


def palette(names=None):
    """Colors and material specs of the given (default: every harvested) decor prefabs."""
    colors, materials = {}, {}
    for name in names or [p.stem for p in sorted(MESHES.glob("*.npz"))]:
        meta = _stored(name)[5]
        colors.update({k: tuple(v) for k, v in meta["colors"].items()})
        materials.update(meta["materials"])
    return colors, materials


if __name__ == "__main__":
    harvest(sys.argv[1:] or None)
