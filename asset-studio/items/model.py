"""Static GLB inspection and placement shared by item backends.

Preserve authored materials, textures, hierarchy, and UVs. Geometry inspection follows
the active scene and every primitive, so pivots and budgets reflect instanced meshes.
The software preview samples base color; Godot remains the final material authority.
"""

import io
import json
import struct
from pathlib import Path

import numpy as np
from PIL import Image

from core import mesh


def read(path):
    """Read a self-contained GLB, rejecting dependencies that would break on export."""
    data = Path(path).read_bytes()
    if len(data) < 20 or struct.unpack_from("<4sII", data) != (b"glTF", 2, len(data)):
        raise ValueError(f"Invalid GLB: {path}")
    chunks, offset = [], 12
    while offset < len(data):
        if offset + 8 > len(data):
            raise ValueError("Truncated GLB chunk")
        size, kind = struct.unpack_from("<I4s", data, offset)
        end = offset + 8 + size
        if end > len(data):
            raise ValueError("Truncated GLB data")
        chunks.append((kind, data[offset + 8:end]))
        offset = end
    if not chunks or chunks[0][0] != b"JSON":
        raise ValueError("GLB must start with JSON")
    g = json.loads(chunks[0][1])
    if any("uri" in x for x in g.get("buffers", []) + g.get("images", [])):
        raise ValueError("Export a self-contained GLB with embedded textures and buffers")
    if g.get("skins"):
        raise ValueError("Item workflow handles static meshes; use the character workflow for skinned assets")
    return g, chunks[1:]


def write(path, g, chunks):
    """Replace GLB JSON without altering embedded binary data."""
    payload = json.dumps(g, separators=(",", ":"), allow_nan=False).encode()
    payload += b" " * (-len(payload) % 4)
    parts = [(b"JSON", payload), *chunks]
    data = b"".join(struct.pack("<I4s", len(body), kind) + body for kind, body in parts)
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    Path(path).write_bytes(struct.pack("<4sII", b"glTF", 2, len(data) + 12) + data)


def geometry(path):
    """World-space positions, triangles, and approximate base colors of the active scene."""
    g, _ = read(path)
    _, binary, acc = mesh.read_glb(str(path))
    worlds = mesh._node_worlds(g)
    verts, faces, colors = [], [], []
    textures = {}

    def image(index):
        if index not in textures:
            source = g["textures"][index]["source"]
            view = g["bufferViews"][g["images"][source]["bufferView"]]
            start = view.get("byteOffset", 0)
            textures[index] = Image.open(io.BytesIO(binary[start:start + view["byteLength"]])).convert("RGB")
        return textures[index]

    def visit(index, ancestors):
        if index in ancestors:
            raise ValueError("Cycle in GLB scene hierarchy")
        node = g["nodes"][index]
        if "mesh" in node:
            for primitive in g["meshes"][node["mesh"]]["primitives"]:
                if primitive.get("mode", 4) != 4:
                    raise ValueError("Only triangle primitives are supported")
                attrs = primitive["attributes"]
                p = acc(attrs["POSITION"]).astype(float)
                f = (acc(primitive["indices"]).ravel() if "indices" in primitive else np.arange(len(p)))
                if len(f) % 3 or len(f) == 0 or f.min() < 0 or f.max() >= len(p):
                    raise ValueError("Invalid triangle indices")
                f = f.reshape(-1, 3).astype(int)
                p = (np.column_stack([p, np.ones(len(p))]) @ worlds[index].T)[:, :3]
                if np.linalg.det(worlds[index][:3, :3]) < 0:
                    f = f[:, ::-1]
                material = g.get("materials", [])[primitive["material"]] if "material" in primitive else {}
                pbr = material.get("pbrMetallicRoughness", {})
                factor = np.array(pbr.get("baseColorFactor", [1, 1, 1, 1])[:3], dtype=float)
                col = np.tile(factor, (len(f), 1))
                if "baseColorTexture" in pbr:
                    texture = pbr["baseColorTexture"]
                    uv_key = f"TEXCOORD_{texture.get('texCoord', 0)}"
                    if uv_key in attrs:
                        im = image(texture["index"])
                        uv = acc(attrs[uv_key])[f].mean(axis=1) % 1
                        xy = (uv * [im.width, im.height]).astype(int)
                        sampled = np.asarray(im)[xy[:, 1], xy[:, 0]] / 255.0
                        col = np.where(sampled <= .04045, sampled / 12.92, ((sampled + .055) / 1.055) ** 2.4) * factor
                if "COLOR_0" in attrs:
                    c = acc(attrs["COLOR_0"])
                    divisor = np.iinfo(c.dtype).max if np.issubdtype(c.dtype, np.integer) else 1
                    col *= (c[:, :3].astype(float) / divisor)[f].mean(axis=1)
                faces.append(f + sum(len(v) for v in verts))
                verts.append(p)
                col = np.where(col <= .0031308, col * 12.92, 1.055 * np.maximum(col, 0) ** (1 / 2.4) - .055)
                colors.append(np.clip(col * 255, 0, 255).astype(np.uint8))
        for child in node.get("children", []):
            visit(child, ancestors | {index})

    for root in g["scenes"][g.get("scene", 0)].get("nodes", []):
        visit(root, set())
    if not verts:
        raise ValueError("GLB active scene contains no mesh triangles")
    return np.vstack(verts), np.vstack(faces), np.vstack(colors)


def place(source, target, placement):
    """Add a scene parent for orientation, dimensions, and pivot without destructive remeshing."""
    from core import kit

    g, chunks = read(source)
    p, _, _ = geometry(source)
    rotation = placement.get("rotation_degrees", [0, 0, 0])
    r = kit.rotation([0, 0, 1], rotation[2]) @ kit.rotation([0, 1, 0], rotation[1]) @ kit.rotation([1, 0, 0], rotation[0])
    p = p @ r.T
    scale = float(placement.get("scale", 1))
    if placement.get("dimensions"):
        span = np.ptp(p, axis=0)
        if np.any(span < 1e-8):
            raise ValueError("Cannot fit dimensions on a flat or empty axis")
        # Uniform fit preserves the design; dimensions are a bounding envelope.
        scale *= float(np.min(np.array(placement["dimensions"]) / span))
    p *= scale
    pivot = placement.get("pivot", "authored")
    anchor = np.zeros(3)
    if pivot in ("center", "ground"):
        anchor = (p.min(axis=0) + p.max(axis=0)) / 2
        if pivot == "ground":
            anchor[1] = p[:, 1].min()
    elif pivot == "grip":
        anchor = np.array(placement.get("grip", [0, 0, 0])) @ r.T * scale
    offset = np.array(placement.get("offset", [0, 0, 0]), float)
    if scale == 1 and np.allclose(r, np.eye(3)) and np.allclose(anchor, 0) and np.allclose(offset, 0):
        Path(target).parent.mkdir(parents=True, exist_ok=True)
        Path(target).write_bytes(Path(source).read_bytes())
        return
    matrix = np.eye(4)
    matrix[:3, :3] = r * scale
    matrix[:3, 3] = -anchor + offset
    scene = g["scenes"][g.get("scene", 0)]
    index = len(g["nodes"])
    g["nodes"].append({"name": "ItemPlacement", "matrix": matrix.T.ravel().tolist(), "children": scene.get("nodes", [])})
    scene["nodes"] = [index]
    write(target, g, chunks)


def colored(path, vertices, faces, colors, crease=50):
    """Write surface colors as glTF COLOR_0 on one material, with character-style crease normals.

    Vertices split at face color/normal boundaries for glTF, while their positions
    retain the shared modeled surface. sRGB authoring colors become linear attributes.
    """
    from core.export import corner_normals

    p, f = np.asarray(vertices, float), np.asarray(faces, int)
    c = np.asarray(colors, float) / 255
    c = np.where(c <= .04045, c / 12.92, ((c + .055) / 1.055) ** 2.4)
    arrays = [p[f].reshape(-1, 3), corner_normals(p, f, crease=crease).reshape(-1, 3), np.repeat(c, 3, axis=0)]
    binary, views, accessors = bytearray(), [], []
    for values in arrays:
        values = np.asarray(values, dtype="<f4")
        views.append({"buffer": 0, "byteOffset": len(binary), "byteLength": values.nbytes, "target": 34962})
        accessors.append({"bufferView": len(views) - 1, "componentType": 5126, "count": len(values), "type": "VEC3"})
        binary.extend(values.tobytes())
    accessors[0].update(min=p.min(axis=0).tolist(), max=p.max(axis=0).tolist())
    g = {"asset": {"version": "2.0", "generator": "Rogue Gauntlet asset-studio"},
         "scene": 0, "scenes": [{"nodes": [0]}], "nodes": [{"name": "Item", "mesh": 0}],
         "meshes": [{"primitives": [{"attributes": {"POSITION": 0, "NORMAL": 1, "COLOR_0": 2}, "material": 0}]}],
         "materials": [{"name": "SurfaceColor", "extras": {"asset_studio_vertex_color": True},
                        "pbrMetallicRoughness": {"metallicFactor": 0, "roughnessFactor": .85}}],
         "buffers": [{"byteLength": len(binary)}], "bufferViews": views, "accessors": accessors}
    write(path, g, [(b"BIN\x00", bytes(binary))])
