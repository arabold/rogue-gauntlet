"""Export Studio pieces onto the dungeon pack's own texture and material, so props share the world's palette.

The pack's dungeon props are UV-mapped onto one 8x4 grid of vertical gradient swatches
(`assets/kaykit-dungeon-remastered/dungeon_texture.png`, byte-identical to the Studio's
`rigs/item_palette.png`) with a single plain material, matte as the project imports it (metallic 0, roughness 0.85). The
pack paints all its props with a handful of those swatches (`PACK`), and every Studio material slot maps onto one of them
(`SWATCHES`), so props never invent a shade of their own. The shade inside a swatch is the characters' ramp
(`core.export.gradient_coordinates`: height, darker on faces turned down) over the v range measured on the pack's
tables, barrels, crates and beds, from about 0.1 at a model's top to 0.86 at its foot. Flames and glowing brews alone get
an emissive copy of the material.
"""

import json
import struct
from pathlib import Path

import numpy as np

from core.export import corner_normals, gradient_coordinates

TEXTURE = Path(__file__).resolve().parents[2] / 'assets/kaykit-dungeon-remastered/dungeon_texture.png'
_TEXTURE_BYTES = None


def get_texture_bytes():
    global _TEXTURE_BYTES
    if _TEXTURE_BYTES is None:
        _TEXTURE_BYTES = TEXTURE.read_bytes()
    return _TEXTURE_BYTES
# The swatches the pack's own dungeon models are painted with, measured from their UVs: (row, column) of the 8x4 grid,
# and the u inside the cell. Props share these few colors and nothing else, so they sit in the world like the pack's own.
PACK = {
    'wood': ((0, 4), .45),        # planks, barrels, tables, chests
    'wood_light': ((0, 2), .45),  # barrel heads
    'wood_dark': ((0, 7), .45),   # the dark trunk, torch handles, wicks, rubble
    'stone': ((0, 1), .5),        # stone blocks and iron straps alike
    'stone_dark': ((0, 0), .45),  # pillar shafts, foundations
    'stone_light': ((0, 5), .45), # grey bedding, plates, dirt floors
    'black': ((0, 3), .5),        # grates
    'rust': ((0, 6), .5),         # brown banners and blankets
    'silver': ((1, 0), .45),      # blades
    'tan': ((1, 1), .45),         # pillows, bread, bottle labels
    'teal': ((2, 0), .45),        # weeds, green bottles
    'green': ((2, 1), .55), 'pink': ((2, 2), .3), 'red': ((2, 3), .55), 'yellow': ((2, 4), .55), 'blue': ((2, 6), .55),
    'gold': ((2, 7), .5),         # coins, gilt chests, candle flames
    'cream': ((3, 0), .52),       # candles, sheets, white banners
    # Glowing accents the pack has no counterpart for, from the same texture.
    'crystal': ((1, 5), .45), 'lavender': ((1, 3), .45),
}
# Studio material slot -> pack swatch. Shading comes from the shared ramp alone, never from per-slot shade offsets.
SWATCHES = {slot: PACK[swatch] for slot, swatch in {
    'wood': 'wood', 'wood_dark': 'wood_dark', 'bark': 'wood_dark', 'wood_light': 'wood_light', 'endgrain': 'wood_light',
    'stone': 'stone', 'stone_dark': 'stone_dark', 'stone_light': 'stone_light', 'marble': 'stone', 'marble_dark': 'stone_dark',
    'rock': 'wood_dark', 'rock_dark': 'stone_dark',
    'iron': 'stone', 'iron_dark': 'stone_dark', 'steel': 'silver', 'pot': 'stone_dark', 'pot_inner': 'black',
    'copper': 'wood_light', 'gold': 'gold', 'trim': 'gold', 'silver': 'silver',
    'wax': 'cream', 'wick': 'wood_dark', 'flame': 'gold', 'coal': 'black',
    'cloth_red': 'red', 'cloth_blue': 'blue', 'cloth_green': 'green', 'linen': 'tan', 'leather': 'wood_dark',
    'parchment': 'cream', 'parchment_dark': 'tan', 'ink': 'wood_dark',
    'straw': 'yellow', 'straw_dark': 'rust', 'hay': 'yellow', 'soil': 'wood_dark', 'sand': 'tan', 'rope': 'tan',
    'bone': 'cream', 'web': 'cream', 'socket': 'black',
    'clay': 'wood_light', 'clay_worn': 'wood_light', 'clay_dark': 'wood', 'glaze': 'blue', 'glaze_light': 'tan',
    'liquid': 'teal', 'ocean': 'blue', 'land': 'tan',
    'crust': 'wood_light', 'crumb': 'tan', 'cheese': 'yellow', 'rind': 'gold', 'herb': 'green', 'lavender': 'lavender',
    'leaf': 'green',
    # Sewer fixtures: the slimy wall's drain pipe and slime.
    'pipe': 'wood_dark', 'slime': 'green',
    # Mines: pyrite, crystals and the lantern's warm core.
    'ore_gold': 'gold', 'ore_crystal': 'crystal', 'ore_crystal_blue': 'blue', 'lantern_glow': 'gold',
}.items()}
EMISSIVE = {'flame', 'liquid', 'lantern_glow'}


def export(path, name, piece, crease=40.0, slot_crease=None, height_range=None):
    """Write `piece` as a GLB on the pack's texture: its one material, plus an emissive copy for glowing slots.

    The ramp spans the piece's own height unless `height_range` fixes it, so parts exported apart (a pendulum's blade
    and the shaft it hangs from) shade as one.
    """
    slots = [str(s) for s in piece.slots]
    missing = sorted(set(slots) - set(SWATCHES))
    if missing:
        raise ValueError(f'No dungeon swatch for slots {missing}')
    tri = piece.verts[piece.faces]
    normals = corner_normals(piece.verts, piece.faces, crease=crease, slots=piece.slots, slot_crease=slot_crease or {})
    # The characters' ramp (height plus slope: faces turned down shade darker), spread over the pack's measured v range.
    v = np.clip(.04 + .9 * gradient_coordinates(tri, normals, height_range), .03, .97)
    uv = np.empty(tri.shape[:2] + (2,), dtype=np.float32)
    names = np.array(slots)
    for slot in set(slots):
        mask = names == slot
        (row, column), u = SWATCHES[slot]
        uv[mask, :, 0] = (column + u) / 8
        uv[mask, :, 1] = (row + v[mask]) / 4
    glowing = np.array([slot in EMISSIVE for slot in slots])
    order = np.r_[np.flatnonzero(~glowing), np.flatnonzero(glowing)]
    positions = tri[order].reshape(-1, 3).astype(np.float32)
    blobs = [positions.tobytes(), normals[order].reshape(-1, 3).astype(np.float32).tobytes(),
             uv[order].reshape(-1, 2).astype(np.float32).tobytes()]
    lit_count = int((~glowing).sum()) * 3
    indices = np.arange(len(positions), dtype=np.uint32)
    groups = [(indices[:lit_count], 0), (indices[lit_count:], 1)]
    groups = [(group, material) for group, material in groups if len(group)]
    blobs += [group.tobytes() for group, _ in groups] + [get_texture_bytes()]
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
    texture = {'baseColorTexture': {'index': 0}, 'metallicFactor': 0, 'roughnessFactor': .85}
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
