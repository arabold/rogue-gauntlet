"""Headless entry point that renders each reconstruction piece highlighted against the rest (`studio.py reconstruct parts`).

    blender -b --factory-startup --python-exit-code 1 --python blender/reconstruction/part_sheet.py -- job.json

Front, side and top Workbench renders per piece, named `<piece index>_<view>.png`, identify the pieces when writing a
character's `reconstruction.json` part map. The reconstruction is shown as delivered: +X front, z up.
"""

import json
import math
import sys
from pathlib import Path

import bpy

job = json.loads(Path(sys.argv[-1]).read_text())
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=job["reconstruction"])
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "OBJECT"
scene.render.resolution_x = scene.render.resolution_y = 320
scene.world = bpy.data.worlds.new("Sheet")
scene.world.color = (1, 1, 1)
cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
scene.collection.objects.link(cam)
scene.camera = cam
cam.data.type = "ORTHO"
cam.data.ortho_scale = 1.15
pieces = sorted([o for o in bpy.data.objects if o.type == "MESH"], key=lambda o: int(o.name.rsplit("_", 1)[-1]))
views = {"front": ((3, 0, 0), (90, 0, 90)), "side": ((0, 3, 0), (90, 0, 180)), "top": ((0, 0, 3), (0, 0, 90))}
for piece in pieces:
    for o in pieces:
        o.color = (0.85, 0.15, 0.1, 1) if o == piece else (0.8, 0.8, 0.8, 1)
    for view, (location, rotation) in views.items():
        cam.location = location
        cam.rotation_euler = [math.radians(a) for a in rotation]
        scene.render.filepath = f"{job['out']}/{piece.name.rsplit('_', 1)[-1]}_{view}.png"
        bpy.ops.render.render(write_still=True)
