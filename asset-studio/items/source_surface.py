"""Recover authored palette regions without remeshing or baking old lighting twice."""

import io
import numpy as np
from PIL import Image
from core import kit, mesh
from items import align, model


def potion_liquid(piece, size):
    """Unify liquid regions in the preserved octagonal bottle layouts.

    Source swatches are also used by labels, straps and collars, so color identity
    alone cannot distinguish liquid. Use the authored profile and label facets;
    leave every triangle and all non-liquid slots untouched.
    """
    heights = {"small": .54, "medium": .68, "large": .84}
    if size not in heights:
        raise ValueError(f"Unknown preserved potion size: {size}")
    triangles = piece.verts[piece.faces]
    low, high = piece.verts[:, 1].min(), piece.verts[:, 1].max()
    y = (triangles[:, :, 1].mean(axis=1) - low) / (high - low) * heights[size]
    if size == "small":
        liquid = y < .365
    elif size == "large":
        liquid = ((y > .045) & (y < .215)) | ((y > .345) & (y < .590))
    else:
        centers = triangles.mean(axis=1)
        facet = (np.rint(np.arctan2(-centers[:, 2], centers[:, 0]) / (np.pi / 4)).astype(int) - 1) % 8
        label = (y > .115) & (y < .364) & np.isin(facet, [4, 5, 6])
        rune = (y > .225) & (y < .255) & (facet == 5)
        liquid = (y < .480) & (~label | rune)
    if not np.any(liquid):
        raise ValueError("Preserved potion contains no liquid faces")
    slots = piece.slots.copy()
    slots[liquid] = "liquid"
    return kit.Piece(piece.verts, piece.faces, slots), {"liquid_triangles": int(liquid.sum()), "size": size}


def palette_piece(source, palette, slots=None):
    """Read the verified studio 8×4 palette; arbitrary textures require authored regions."""
    p, f, _ = model.geometry(source)
    g, binary, accessor = mesh.read_glb(str(source))
    with Image.open(palette) as image:
        expected = np.asarray(image.convert("RGB"))
    names = {cell: name for name, cell in align.SWATCH_NAMES.items()}
    swatches = {s["cell"]: s["rgb"] for s in align.load_palette_swatches(str(palette))}
    labels, colors = [], {}
    verified = set()

    def visit(index):
        node = g["nodes"][index]
        if "mesh" in node:
            for primitive in g["meshes"][node["mesh"]]["primitives"]:
                attrs = primitive["attributes"]
                material = g["materials"][primitive["material"]]
                pbr = material.get("pbrMetallicRoughness", {})
                if "baseColorTexture" not in pbr or "COLOR_0" in attrs:
                    raise ValueError("Studio palette conversion requires unmodified palette UV colors")
                if pbr.get("baseColorFactor", [1, 1, 1, 1]) != [1, 1, 1, 1]:
                    raise ValueError("Palette conversion does not support a base color multiplier")
                texture = pbr["baseColorTexture"]
                image_index = g["textures"][texture["index"]]["source"]
                if image_index not in verified:
                    view = g["bufferViews"][g["images"][image_index]["bufferView"]]
                    start = view.get("byteOffset", 0)
                    with Image.open(io.BytesIO(binary[start:start + view["byteLength"]])) as image:
                        if not np.array_equal(np.asarray(image.convert("RGB")), expected):
                            raise ValueError("Source texture does not match the studio palette; author color regions explicitly")
                    verified.add(image_index)
                key = f"TEXCOORD_{texture.get('texCoord', 0)}"
                if key not in attrs:
                    raise ValueError("Source is missing palette UV coordinates")
                indices = (accessor(primitive["indices"]).ravel() if "indices" in primitive
                           else np.arange(len(accessor(attrs["POSITION"]))))
                uv = accessor(attrs[key])[indices.reshape(-1, 3)]
                cells = np.floor(uv * [8, 4]).astype(int)
                if np.any(cells != cells[:, :1]) or np.any(cells < 0) or np.any(cells >= [8, 4]):
                    raise ValueError("Every source triangle must remain within one palette cell")
                for col, row in cells[:, 0]:
                    cell = (int(row), int(col))
                    name = names.get(cell, f"swatch_{row}_{col}")
                    slot = (slots or {}).get(name, name)
                    if not isinstance(slot, str) or not slot:
                        raise ValueError("Surface slot names must be nonempty strings")
                    labels.append(slot)
                    if slot in colors and colors[slot] != swatches[cell]:
                        raise ValueError("Different source swatches need distinct slots to preserve their boundaries")
                    colors[slot] = swatches[cell]
        for child in node.get("children", []):
            visit(child)

    for root in g["scenes"][g.get("scene", 0)]["nodes"]:
        visit(root)
    if len(labels) != len(f):
        raise ValueError("Source palette labels do not match scene triangles")
    return kit.Piece(p, f, np.array(labels, dtype=object)), colors
