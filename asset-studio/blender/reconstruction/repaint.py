"""Headless entry point that recolors a saved reconstruction source (`studio.py reconstruct palette`).

    blender -b --factory-startup source.blend --python-exit-code 1 --python blender/reconstruction/repaint.py -- job.json

Blender opens the saved source; the job names it for the save, its packed palette image and the new atlas. The new
atlas must cover exactly the pixels the old one did: a palette with other slots would move cells under the faces' UVs,
so it needs `reconstruct build`.
"""

import json
import sys
from pathlib import Path

import bpy
import numpy as np


def repaint(job):
    image = bpy.data.images[job["image"]]
    fresh = bpy.data.images.load(job["atlas_png"])
    old = np.array(image.pixels[:], dtype=np.float32).reshape(image.size[1], image.size[0], 4)
    new = np.array(fresh.pixels[:], dtype=np.float32).reshape(fresh.size[1], fresh.size[0], 4)
    if old.shape != new.shape or not np.array_equal(old[..., :3].any(axis=2), new[..., :3].any(axis=2)):
        raise ValueError("The palette's slots changed; rebuild the source with `reconstruct build`")
    image.pixels.foreach_set(new.ravel())
    image.pack()
    bpy.data.images.remove(fresh)
    bpy.ops.wm.save_mainfile(filepath=job["blend"], compress=True)


repaint(json.loads(Path(sys.argv[-1]).read_text()))
