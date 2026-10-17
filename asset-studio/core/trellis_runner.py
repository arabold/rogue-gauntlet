#!/usr/bin/env python3
"""
Runs TRELLIS.2 locally on Apple Silicon through the community port `shivampkumar/trellis-mac` for several images
in one process (the model loads once, ~80 s). Called by `core.ai.image_to_3d_local` with the port's own
interpreter, never imported by the pipeline itself:

    <trellis-mac>/.venv/bin/python trellis_runner.py <trellis-mac dir> <in.png> <out stem> [<in.png> <out stem> ...]

Inputs are RGBA images whose alpha is our own drawing mask, so TRELLIS.2 never calls its background remover
(RMBG-2.0, non-commercial license): its loader is stubbed and the model is never downloaded. Each output is
`<stem>.glb`: the reconstructed mesh (+Y up, face toward +Z) with TRELLIS.2's own colors per vertex (`COLOR_0`,
linear), read straight from its voxel color field.
"""

import importlib.util
import json
import os
import struct
import sys

MAX_FACES = 200_000
SEED = 42


def write_glb(path, verts, faces, colors):
    """
    A minimal GLB: one primitive with positions, smooth normals and linear float RGB vertex colors, and no material
    (viewers such as Godot then show the vertex colors as albedo).
    """
    import numpy as np

    pos = np.ascontiguousarray(verts, np.float32)
    tri = pos[faces].astype(np.float64)
    fn = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    nrm = np.zeros((len(pos), 3))
    for k in range(3):
        np.add.at(nrm, faces[:, k], fn)
    nrm = np.ascontiguousarray(nrm / np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-12), np.float32)
    col = np.ascontiguousarray(colors, np.float32)
    idx = np.ascontiguousarray(faces, np.uint32).ravel()
    blobs = [pos.tobytes(), nrm.tobytes(), col.tobytes(), idx.tobytes()]
    offsets = np.cumsum([0] + [len(b) for b in blobs[:-1]]).tolist()
    g = {
        "asset": {"version": "2.0", "generator": "trellis_runner"},
        "scene": 0, "scenes": [{"nodes": [0]}], "nodes": [{"mesh": 0}],
        "meshes": [{"primitives": [{"attributes": {"POSITION": 0, "NORMAL": 1, "COLOR_0": 2}, "indices": 3}]}],
        "buffers": [{"byteLength": sum(len(b) for b in blobs)}],
        "bufferViews": [{"buffer": 0, "byteOffset": o, "byteLength": len(b)} for o, b in zip(offsets, blobs)],
        "accessors": [
            {"bufferView": 0, "componentType": 5126, "count": len(pos), "type": "VEC3",
             "min": pos.min(0).tolist(), "max": pos.max(0).tolist()},
            {"bufferView": 1, "componentType": 5126, "count": len(nrm), "type": "VEC3"},
            {"bufferView": 2, "componentType": 5126, "count": len(col), "type": "VEC3"},
            {"bufferView": 3, "componentType": 5125, "count": len(idx), "type": "SCALAR"},
        ],
    }
    js = json.dumps(g).encode()
    js += b" " * (-len(js) % 4)
    binary = b"".join(blobs)
    binary += b"\0" * (-len(binary) % 4)
    with open(path, "wb") as fh:
        fh.write(struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(binary)))
        fh.write(struct.pack("<II", len(js), 0x4E4F534A) + js)
        fh.write(struct.pack("<II", len(binary), 0x004E4942) + binary)


def main():
    root = os.path.abspath(sys.argv[1])
    jobs = list(zip(sys.argv[2::2], sys.argv[3::2]))
    sys.path.insert(0, root)
    spec = importlib.util.spec_from_file_location("trellis_generate", os.path.join(root, "generate.py"))
    gen = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(gen)

    import fast_simplification
    import numpy as np
    import torch
    from PIL import Image
    from scipy.spatial import cKDTree
    from trellis2.pipelines import rembg
    from trellis2.pipelines.trellis2_image_to_3d import Trellis2ImageTo3DPipeline

    for name in dir(rembg):
        if isinstance(getattr(rembg, name), type):
            setattr(rembg, name, lambda *a, **k: None)
    pipeline = Trellis2ImageTo3DPipeline.from_pretrained("microsoft/TRELLIS.2-4B")
    pipeline.to(torch.device("mps"))
    for image, stem in jobs:
        out = pipeline.run(Image.open(image), seed=SEED, pipeline_type="512")
        mesh = out[0] if isinstance(out, list) else out
        verts, faces = mesh.vertices.cpu().numpy(), mesh.faces.cpu().numpy()
        if not len(faces):
            print(f"{image}: empty mesh, skipped")
            continue
        size = float(np.asarray(mesh.voxel_size, float).ravel()[0])
        origin = mesh.origin.cpu().numpy() if hasattr(mesh, "origin") else np.full(3, -0.5)
        centers = mesh.coords.cpu().numpy()[:, -3:].astype(np.float64) * size + origin + 0.5 * size
        attrs = mesh.attrs.cpu().float().numpy()[:, mesh.layout["base_color"]]
        colors = np.clip(attrs[cKDTree(centers).query(verts)[1]], 0.0, 1.0)
        if len(faces) > MAX_FACES:
            collapses = fast_simplification.simplify(verts, faces, 1.0 - MAX_FACES / len(faces), return_collapses=True)[2]
            simple_v, simple_f, mapping = fast_simplification.replay_simplification(verts, faces, collapses)
            ok = mapping >= 0
            sums, counts = np.zeros((len(simple_v), 3)), np.zeros(len(simple_v))
            np.add.at(sums, mapping[ok], colors[ok])
            np.add.at(counts, mapping[ok], 1.0)
            lone = counts == 0
            if lone.any():
                sums[lone] = colors[cKDTree(verts).query(simple_v[lone])[1]]
                counts[lone] = 1.0
            verts, faces, colors = simple_v, simple_f, sums / counts[:, None]
        write_glb(stem + ".glb", np.column_stack([verts[:, 0], verts[:, 2], -verts[:, 1]]), faces, colors)
        print(f"{image}: {len(faces)} faces -> {stem}.glb")


if __name__ == "__main__":
    main()
