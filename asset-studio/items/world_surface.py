"""Export Studio pieces onto the dungeon pack's own texture and material, so props share the world's palette.

The pack's dungeon props are UV-mapped onto one 8x4 grid of vertical gradient swatches
(`assets/kaykit-dungeon-remastered/dungeon_texture.png`, byte-identical to the Studio's
`rigs/item_palette.png`) with a single plain material (metallic 0, roughness 0.45). Every
material is one swatch column, and the shade inside it follows height: measured on the pack's
tables, barrels, crates and beds, v runs from about 0.1 at a model's top to 0.86 at its foot.
`SWATCHES` names the cell used for each Studio material slot plus a shade offset; flames and
glowing brews alone get an emissive copy of the material.
"""

import json
import struct
from pathlib import Path

import numpy as np

from core.export import corner_normals

TEXTURE = Path(__file__).resolve().parents[2] / 'assets/kaykit-dungeon-remastered/dungeon_texture.png'
# Slot -> ((row, column) of the 8x4 grid, u inside the cell, shade offset added to v: + darker, - lighter).
SWATCHES = {
    'wood': ((0, 4), .45, 0), 'wood_dark': ((0, 4), .45, .12), 'bark': ((0, 4), .45, .06),
    'wood_light': ((0, 2), .45, -.12), 'endgrain': ((1, 1), .45, -.12),
    # The pack's walls sit in the lower, darker half of the stone swatch; statues are a lighter cut of the same stone.
    'stone': ((0, 1), .45, .18), 'stone_dark': ((0, 0), .45, .05), 'stone_light': ((0, 5), .45, 0),
    'marble': ((0, 1), .45, -.06), 'marble_dark': ((0, 1), .45, .22), 'rock': ((0, 7), .45, 0), 'rock_dark': ((0, 7), .45, .15),
    'iron': ((0, 0), .3, -.05), 'iron_dark': ((0, 0), .3, .15), 'steel': ((0, 1), .3, -.15), 'pot': ((0, 0), .3, 0),
    'pot_inner': ((0, 3), .4, 0), 'copper': ((0, 2), .45, 0), 'gold': ((2, 5), .45, 0), 'trim': ((2, 5), .45, -.05),
    'silver': ((1, 0), .45, 0),
    'wax': ((3, 0), .45, -.1), 'wick': ((0, 3), .45, 0), 'flame': ((2, 7), .45, -.35), 'coal': ((0, 3), .45, 0),
    'cloth_red': ((2, 3), .45, 0), 'cloth_blue': ((2, 6), .45, 0), 'cloth_green': ((2, 1), .45, 0),
    'cloth_purple': ((1, 3), .45, 0), 'cloth_yellow': ((2, 5), .45, -.05),
    'sheet': ((3, 0), .45, -.2), 'linen': ((1, 1), .45, -.1), 'blanket_grey': ((0, 5), .45, 0), 'blanket_tan': ((1, 1), .45, 0),
    'leather': ((0, 4), .45, .2), 'parchment': ((1, 1), .45, -.3), 'parchment_dark': ((1, 1), .45, 0), 'ink': ((0, 7), .45, .2),
    'straw': ((2, 5), .45, -.1), 'straw_dark': ((2, 4), .45, .1), 'rope': ((1, 1), .45, .1), 'bone': ((3, 0), .45, -.15),
    'socket': ((0, 3), .45, 0),
    'clay': ((0, 2), .45, 0), 'clay_dark': ((0, 4), .45, .25), 'glaze': ((3, 2), .45, -.1), 'glaze_light': ((1, 1), .45, -.2),
    'liquid': ((1, 6), .45, .3), 'ocean': ((2, 6), .45, 0), 'land': ((1, 1), .45, 0),
    'crust': ((0, 2), .45, .05), 'crumb': ((1, 1), .45, -.2), 'cheese': ((2, 5), .45, -.1), 'rind': ((2, 7), .45, 0),
    'meat': ((1, 7), .45, .1), 'herb': ((2, 1), .45, 0), 'lavender': ((1, 3), .45, -.2), 'leaf': ((1, 6), .45, 0),
}
EMISSIVE = {'flame', 'liquid'}


def export(path, name, piece, crease=40.0, slot_crease=None):
    """Write `piece` as a GLB on the pack's texture: its one material, plus an emissive copy for glowing slots."""
    slots = [str(s) for s in piece.slots]
    missing = sorted(set(slots) - set(SWATCHES))
    if missing:
        raise ValueError(f'No dungeon swatch for slots {missing}')
    tri = piece.verts[piece.faces]
    normals = corner_normals(piece.verts, piece.faces, crease=crease, slots=piece.slots, slot_crease=slot_crease or {})
    height = tri[:, :, 1]
    t = (height - height.min()) / max(float(np.ptp(height)), 1e-6)
    uv = np.zeros(tri.shape[:2] + (2,))
    for i, slot in enumerate(slots):
        (row, column), u, shade = SWATCHES[slot]
        uv[i, :, 0] = (column + u) / 8
        uv[i, :, 1] = (row + np.clip(.86 - .72 * t[i] + shade, .04, .96)) / 4
    glowing = np.array([slot in EMISSIVE for slot in slots])
    order = np.r_[np.flatnonzero(~glowing), np.flatnonzero(glowing)]
    positions = tri[order].reshape(-1, 3).astype(np.float32)
    blobs = [positions.tobytes(), normals[order].reshape(-1, 3).astype(np.float32).tobytes(),
             uv[order].reshape(-1, 2).astype(np.float32).tobytes()]
    lit_count = int((~glowing).sum()) * 3
    indices = np.arange(len(positions), dtype=np.uint32)
    groups = [(indices[:lit_count], 0), (indices[lit_count:], 1)]
    groups = [(group, material) for group, material in groups if len(group)]
    blobs += [group.tobytes() for group, _ in groups] + [TEXTURE.read_bytes()]
    views, binary = [], b''
    for blob in blobs:
        views.append({'buffer': 0, 'byteOffset': len(binary), 'byteLength': len(blob)})
        binary += blob + b'\0' * (-len(blob) % 4)
    for view in views[:3]:
        view['target'] = 34962
    for view in views[3:-1]:
        view['target'] = 34963
    accessors = [{'bufferView': 0, 'componentType': 5126, 'count': len(positions), 'type': 'VEC3',
                  'min': positions.min(0).tolist(), 'max': positions.max(0).tolist()},
                 {'bufferView': 1, 'componentType': 5126, 'count': len(positions), 'type': 'VEC3'},
                 {'bufferView': 2, 'componentType': 5126, 'count': len(positions), 'type': 'VEC2'}]
    primitives = []
    for k, (group, material) in enumerate(groups):
        accessors.append({'bufferView': 3 + k, 'componentType': 5125, 'count': len(group), 'type': 'SCALAR'})
        primitives.append({'attributes': {'POSITION': 0, 'NORMAL': 1, 'TEXCOORD_0': 2}, 'indices': len(accessors) - 1,
                           'material': material})
    texture = {'baseColorTexture': {'index': 0}, 'metallicFactor': 0, 'roughnessFactor': .45}
    gltf = {
        'asset': {'version': '2.0', 'generator': 'Rogue Gauntlet Asset Studio world_surface'},
        'scene': 0, 'scenes': [{'nodes': [0]}], 'nodes': [{'name': name, 'mesh': 0}],
        'meshes': [{'name': name, 'primitives': primitives}], 'accessors': accessors,
        'bufferViews': views, 'buffers': [{'byteLength': len(binary)}],
        'materials': [{'name': 'texture', 'pbrMetallicRoughness': texture},
                      {'name': 'texture_glow', 'pbrMetallicRoughness': texture,
                       'emissiveTexture': {'index': 0}, 'emissiveFactor': [1, 1, 1]}],
        'samplers': [{'magFilter': 9729, 'minFilter': 9987}], 'textures': [{'sampler': 0, 'source': 0}],
        'images': [{'name': 'dungeon_texture', 'mimeType': 'image/png', 'bufferView': len(views) - 1}],
    }
    if not any(material == 1 for _, material in groups):
        gltf['materials'].pop()
    text = json.dumps(gltf, separators=(',', ':')).encode()
    text += b' ' * (-len(text) % 4)
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    with open(path, 'wb') as f:
        f.write(struct.pack('<4sII', b'glTF', 2, 28 + len(text) + len(binary)))
        f.write(struct.pack('<I4s', len(text), b'JSON') + text)
        f.write(struct.pack('<I4s', len(binary), b'BIN\0') + binary)
