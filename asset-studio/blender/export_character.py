"""Export the authored rig, skin, materials and actions from a saved Blender file."""

import json
from pathlib import Path
import sys

import bpy


def start_at_zero(action):
    """Preserve the authored duration and remove the leading hold of a frame-1 clip."""
    start, end = action.frame_range
    for layer in action.layers:
        for strip in layer.strips:
            for channels in strip.channelbags:
                for curve in channels.fcurves:
                    for key in curve.keyframe_points:
                        key.co_ui.x -= start
                    curve.update()
    if action.use_frame_range:
        action.frame_start = 0
        action.frame_end = end - start


def export_character(job):
    objects = bpy.data.collections[job["collection"]].objects
    rig = objects[job["rig_object"]]
    body = objects[job["body_object"]]
    attachments = {objects[source]: name for source, name in job.get("attachments", {}).items()}
    meshes = [obj for obj in objects if obj.type == "MESH"]
    for mesh in meshes:
        if not any(mod.type == "ARMATURE" and mod.object == rig for mod in mesh.modifiers):
            raise ValueError(f"{mesh.name} must be skinned to {rig.name}")
        for material in mesh.data.materials:
            for node in material.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.image and not node.image.packed_file:
                    raise ValueError(f"Pack {node.image.name} into the Blender source")
    for original, exported in job.get("image_names", {}).items():
        bpy.data.images[original].name = exported

    animation = rig.animation_data
    for track in list(animation.nla_tracks):
        animation.nla_tracks.remove(track)
    for action in bpy.data.actions:
        start_at_zero(action)
    idle = bpy.data.actions[job["idle"]]
    animation.action = idle
    bpy.context.scene.frame_set(0)

    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        obj.select_set(obj not in attachments)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    rig.name = job["rig_node"]
    body.name = job["body_node"]
    rig.select_set(True)
    for mesh, name in attachments.items():
        mesh.name = name
        mesh.select_set(True)

    bpy.ops.export_scene.gltf(
        filepath=job["glb"], export_format="GLB", use_selection=True,
        export_yup=True, export_animations=True, export_animation_mode="ACTIONS",
        export_force_sampling=False, export_anim_slide_to_zero=True,
        export_def_bones=False, export_optimize_animation_size=True, export_image_format="AUTO",
    )


export_character(json.loads(Path(sys.argv[-1]).read_text()))
