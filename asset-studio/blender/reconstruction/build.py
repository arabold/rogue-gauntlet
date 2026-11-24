"""Headless entry point that turns an image-to-3D reconstruction into a rigged Blender source (`studio.py reconstruct build`).

    blender -b --factory-startup --python-exit-code 1 --python blender/reconstruction/build.py -- job.json

The job names the character's `reconstruction.json` settings, the reconstruction GLB, the turnaround views, the
palette atlas and its coordinates, the rig template and the output `.blend`. One fresh Blender registers and reduces
the parts, rebuilds the limbs, colors the faces, fits and binds the skeleton, then saves the source the
`blender_source` recipe exports.
"""

import json
import sys
from pathlib import Path

import bpy

sys.dont_write_bytecode = True
# The scripts' own package, and the Studio's numpy-only `core.color_math` (its other `core` modules need the Studio's
# dependencies and are never imported here).
sys.path[:0] = [str(Path(__file__).resolve().parents[1]), str(Path(__file__).resolve().parents[2])]

from reconstruction import chains, color, parts, rig  # noqa: E402


def build(job):
    config = job["config"]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    parts.import_parts(job["reconstruction"], config)
    parts.decimate(config)
    parts.rebuild_rings(config, chains.chains(config))
    report = {"slots": color.paint(config, job["views"], job["atlas_png"], job["atlas"], job["colors"])}

    kind = config["rig"].get("kind", "humanoid")
    arm = rig.create_armature(config) if kind == "authored" else rig.import_template(job["template"], config)
    if kind == "humanoid":
        rig.fit_rest(arm, config)
        rig.pose_onto_mesh(arm, config)
    elif kind == "creature":
        rig.fit_creature(arm, config)
        rig.pose_creature(arm, config)
    rig.bind(arm, config)
    rig.author_actions(arm, config)
    rig.hold_poses(arm, config)
    rig.complete_clip_channels(arm)
    arm.animation_data.action = bpy.data.actions[config["rig"]["idle"]]

    # The source holds one collection: the rig and its named parts. Dense references and helpers are dropped.
    name = config["name"]
    col = bpy.data.collections.new("COL_" + name)
    bpy.context.scene.collection.children.link(col)
    for o in list(bpy.data.objects):
        if o.name.startswith("LP_") or o == arm:
            for c in o.users_collection:
                c.objects.unlink(o)
            col.objects.link(o)
            if o.name.startswith("LP_"):
                o.name = o.data.name = f"SM_{name}_{o.name[3:]}"
        else:
            bpy.data.objects.remove(o)
    for c in list(bpy.data.collections):
        if c != col:
            bpy.data.collections.remove(c)
    bpy.data.orphans_purge(do_recursive=True)
    report["triangles"] = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in col.objects if o.type == "MESH"}
    bpy.ops.wm.save_as_mainfile(filepath=job["blend"], compress=True)
    Path(job["report"]).write_text(json.dumps(report, indent=1))


build(json.loads(Path(sys.argv[-1]).read_text()))
