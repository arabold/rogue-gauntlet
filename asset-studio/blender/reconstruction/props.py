"""Rigid hand-slot equipment (`props` in reconstruction.json), built procedurally by the Studio.

Each prop arrives placed on its verified attachment frame of the unfitted template. Fitting the skeleton keeps every
bone's direction and roll, so a slot's rest frame only moves; the prop moves with it, keeps its authored size, and is
skinned to the slot bone alone, already in the rest pose.
"""

import bpy
import numpy as np

from . import color


def template_frames(arm, props):
    """The rest frames of the props' slot bones, recorded before the skeleton is fitted."""
    return {p["bone"]: (arm.matrix_world @ arm.data.bones[p["bone"]].matrix_local).copy() for p in props.values()}


def attach(arm, config, props, template, atlas):
    """Create each prop as an `LP_` part at its fitted slot, palette-mapped and skinned rigidly to the slot bone."""
    mat = bpy.data.materials[f"{config['name']} | Palette"]
    for name, prop in props.items():
        bone = prop["bone"]
        move = np.array(arm.matrix_world @ arm.data.bones[bone].matrix_local @ template[bone].inverted())
        verts = np.asarray(prop["verts"], float) @ move[:3, :3].T + move[:3, 3]
        me = bpy.data.meshes.new("LP_" + name)
        me.from_pydata(verts.tolist(), [], prop["faces"])
        me.update()
        o = bpy.data.objects.new(me.name, me)
        bpy.context.scene.collection.objects.link(o)
        slots = sorted(set(prop["slots"]))
        labels = np.array([slots.index(s) for s in prop["slots"]])
        color.map_palette(me, [atlas[s] for s in slots], labels, mat, config.get("smooth_angle", 35))
        o.vertex_groups.new(name=bone).add(list(range(len(verts))), 1.0, "REPLACE")
        o.parent = arm
        o.matrix_parent_inverse = arm.matrix_world.inverted()
        o.modifiers.new("Armature", "ARMATURE").object = arm
