"""Lift parts out of the dungeon pack's glTF models with their own UVs, material and palette texture.

Some details already exist in the pack, such as the puffy pillow and soft blanket of the dungeon
beds; props reuse them exactly instead of approximating them. A part is the triangles of a
source model that pass a position test, written as a self-contained GLB that keeps the source
material and texture. Pack meshes are open shells, so parts skip the Studio's closed-surface
item gate and are written straight to the game art tree: `python -m items.borrowed_parts` from
`asset-studio/` regenerates every part in `PARTS`.
"""

import json
import struct
from pathlib import Path

import numpy as np

COMPONENTS = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}
TYPES = {5121: np.uint8, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}


def _read(g, buffers, index):
    accessor = g['accessors'][index]
    view = g['bufferViews'][accessor['bufferView']]
    count = accessor['count'] * COMPONENTS[accessor['type']]
    data = np.frombuffer(buffers[view['buffer']], TYPES[accessor['componentType']], count,
                         view.get('byteOffset', 0) + accessor.get('byteOffset', 0))
    return data.reshape(accessor['count'], -1) if COMPONENTS[accessor['type']] > 1 else data


def extract(source, target, keep, offset=(0, 0, 0)):
    """Write the triangles of `source` (.gltf) whose corners all pass `keep(points)` to `target` (.glb), moved by
    `offset`. The source must hold one untransformed mesh node, as the pack's single-object models do.
    """
    source = Path(source)
    g = json.loads(source.read_text())
    buffers = [(source.parent / b['uri']).read_bytes() for b in g['buffers']]
    nodes = [n for n in g['nodes'] if 'mesh' in n]
    if len(nodes) != 1 or any(key in nodes[0] for key in ('matrix', 'rotation', 'scale', 'translation')):
        raise ValueError(f'{source.name}: expected one untransformed mesh node')
    primitives = g['meshes'][nodes[0]['mesh']]['primitives']
    if len(primitives) != 1:
        raise ValueError(f'{source.name}: expected one primitive')
    primitive = primitives[0]
    position = _read(g, buffers, primitive['attributes']['POSITION'])
    normal = _read(g, buffers, primitive['attributes']['NORMAL'])
    uv = _read(g, buffers, primitive['attributes']['TEXCOORD_0'])
    faces = _read(g, buffers, primitive['indices']).reshape(-1, 3)
    faces = faces[np.all(keep(position[faces].reshape(-1, 3)).reshape(-1, 3), axis=1)]
    if not len(faces):
        raise ValueError(f'{source.name}: no triangles kept')
    used, remap = np.unique(faces, return_inverse=True)
    faces = remap.reshape(-1, 3).astype(np.uint32)

    material = g['materials'][primitive['material']]
    texture = g['textures'][material['pbrMetallicRoughness']['baseColorTexture']['index']]
    image = (source.parent / g['images'][texture['source']]['uri']).read_bytes()
    blobs = [np.ascontiguousarray(position[used] + np.asarray(offset, np.float32), np.float32).tobytes(), np.ascontiguousarray(normal[used], np.float32).tobytes(),
             np.ascontiguousarray(uv[used], np.float32).tobytes(), faces.tobytes(), image]
    views, binary = [], b''
    for blob in blobs:
        views.append({'buffer': 0, 'byteOffset': len(binary), 'byteLength': len(blob)})
        binary += blob + b'\0' * (-len(blob) % 4)
    for view, target_kind in zip(views[:4], (34962, 34962, 34962, 34963)):
        view['target'] = target_kind
    points = position[used] + np.asarray(offset, np.float32)
    material = json.loads(json.dumps(material))
    material['pbrMetallicRoughness']['baseColorTexture'] = {'index': 0}
    out = {
        'asset': {'version': '2.0', 'generator': 'Rogue Gauntlet Asset Studio borrowed_parts'},
        'scene': 0, 'scenes': [{'nodes': [0]}], 'nodes': [{'name': Path(target).stem, 'mesh': 0}],
        'meshes': [{'name': Path(target).stem, 'primitives': [{
            'attributes': {'POSITION': 0, 'NORMAL': 1, 'TEXCOORD_0': 2}, 'indices': 3, 'material': 0}]}],
        'accessors': [
            {'bufferView': 0, 'componentType': 5126, 'count': len(used), 'type': 'VEC3',
             'min': points.min(0).tolist(), 'max': points.max(0).tolist()},
            {'bufferView': 1, 'componentType': 5126, 'count': len(used), 'type': 'VEC3'},
            {'bufferView': 2, 'componentType': 5126, 'count': len(used), 'type': 'VEC2'},
            {'bufferView': 3, 'componentType': 5125, 'count': faces.size, 'type': 'SCALAR'}],
        'bufferViews': views, 'buffers': [{'byteLength': len(binary)}],
        'materials': [material], 'textures': [{'source': 0, **({'sampler': 0} if 'sampler' in texture else {})}],
        'samplers': [g['samplers'][texture['sampler']]] if 'sampler' in texture else [],
        'images': [{'name': g['images'][texture['source']].get('name', 'texture'), 'mimeType': 'image/png', 'bufferView': 4}],
    }
    if not out['samplers']:
        del out['samplers']
    text = json.dumps(out, separators=(',', ':')).encode()
    text += b' ' * (-len(text) % 4)
    Path(target).parent.mkdir(parents=True, exist_ok=True)
    with open(target, 'wb') as f:
        f.write(struct.pack('<4sII', b'glTF', 2, 28 + len(text) + len(binary)))
        f.write(struct.pack('<I4s', len(text), b'JSON') + text)
        f.write(struct.pack('<I4s', len(binary), b'BIN\0') + binary)
    return len(faces)


REPO = Path(__file__).resolve().parents[2]
PACK = REPO / 'assets/kaykit-dungeon-remastered'
# Part name -> (pack source, triangle test, offset). Bedding: the pillow and blanket above the bed frame's plank
# top (y > .68), lowered to rest on y = 0 so a bunk deck can carry it at any height.
PARTS = {
    'barracks/bed_bedding': (PACK / 'bed_frame.gltf', lambda p: p[:, 1] > .68, (0, -.7, 0)),
}


def main():
    for name, (source, keep, offset) in PARTS.items():
        target = REPO / f'assets/custom-props/{name}.glb'
        print(target.relative_to(REPO), extract(source, target, keep, offset), 'triangles')


if __name__ == '__main__':
    main()
