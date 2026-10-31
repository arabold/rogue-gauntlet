"""KayKit Dungeon Remastered tiles (CC0) as studio pieces, so derived tiles keep the original footprint.

Each face takes the slot of the texture swatch its UVs sample; the swatch's middle color becomes
that slot's base color and the studio ramp replaces the painted gradient. Authored normals are kept.
"""

import json
from functools import lru_cache
from pathlib import Path

import numpy as np
from PIL import Image

from core import kit

SOURCE = Path(__file__).resolve().parents[2] / "assets/kaykit-dungeon-remastered"
GRID = (8, 4)  # dungeon_texture.png: 8 columns by 4 rows of vertical gradient swatches

COMPONENTS = {5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8}
WIDTH = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}


def accessor(g, binary, index):
    a = g["accessors"][index]
    view = g["bufferViews"][a["bufferView"]]
    start = view.get("byteOffset", 0) + a.get("byteOffset", 0)
    width = WIDTH[a["type"]]
    data = np.frombuffer(binary, COMPONENTS[a["componentType"]], a["count"] * width, start)
    return data.reshape(a["count"], width) if width > 1 else data


@lru_cache(maxsize=None)
def load(name):
    """(piece, colors) for one KayKit tile; nodes must carry no transforms (true for the walls)."""
    path = SOURCE / f"{name}.gltf"
    g = json.loads(path.read_text())
    binary = (path.parent / g["buffers"][0]["uri"]).read_bytes()
    texture = np.asarray(Image.open(path.parent / g["images"][0]["uri"]).convert("RGB"), float)
    if any(n.get("translation") or n.get("rotation") or n.get("scale") for n in g["nodes"] if "mesh" in n):
        raise ValueError(f"{name}: transformed KayKit nodes are not supported")
    verts, faces, normals, slots, colors = [], [], [], [], {}
    height, width = texture.shape[:2]
    for primitive in (p for m in g["meshes"] for p in m["primitives"]):
        p = accessor(g, binary, primitive["attributes"]["POSITION"]).astype(float)
        uv = accessor(g, binary, primitive["attributes"]["TEXCOORD_0"]).astype(float)
        n = accessor(g, binary, primitive["attributes"]["NORMAL"]).astype(float)
        f = accessor(g, binary, primitive["indices"]).astype(np.int64).reshape(-1, 3)
        center = uv[f].mean(axis=1)
        col = np.clip((center[:, 0] % 1) * GRID[0], 0, GRID[0] - 1).astype(int)
        row = np.clip((center[:, 1] % 1) * GRID[1], 0, GRID[1] - 1).astype(int)
        for c, r in set(zip(col.tolist(), row.tolist())):
            x = int((c + .4) * width / GRID[0])
            y = int((r + .5) * height / GRID[1])
            colors[f"kaykit_{r}{c}"] = tuple(int(v) for v in texture[y, x])
        faces.append(f + sum(len(v) for v in verts))
        verts.append(p)
        normals.append(n[f])
        slots += [f"kaykit_{r}{c}" for c, r in zip(col, row)]
    return kit.Piece(np.vstack(verts), np.vstack(faces), np.array(slots, object), normals=np.vstack(normals)), colors
