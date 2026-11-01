"""Pose a KayKit character (CC0) in Blender and store its closed parts for stone figures in props.

Runs inside Blender, headless and apart from any open session:

    /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python asset-studio/items/pose_kaykit.py -- \
        assets/kaykit-adventurers/Knight.glb Idle asset-studio/items/meshes/knight_idle.npz

The character is imported, set to the first frame of the named clip, and each of its own mesh parts
(not the weapons and shields hanging from its hand slots, nor the helper sphere) is evaluated in that
pose. Parts are welded, their few open rims capped and their faces turned outward, so each one is a
closed solid the studio can union and cut. The npz holds `<part>__v` (Y-up, as in Godot) and
`<part>__f` arrays; `furnishings.figure` turns them into one solid.
"""

import sys

import bmesh
import bpy
import numpy as np

argv = sys.argv[sys.argv.index('--') + 1:]
source, clip, target = argv[:3]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=source)
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
action = bpy.data.actions[clip]
rig.animation_data_create()
rig.animation_data.action = action
if action.slots:
    rig.animation_data.action_slot = action.slots[0]
bpy.context.scene.frame_set(int(action.frame_range[0]))
graph = bpy.context.evaluated_depsgraph_get()
parts = {}
for o in bpy.data.objects:
    # Body parts are skinned to the rig or ride a bone (helmet, cape); held items hang from hand-slot bones.
    if o.type != 'MESH' or o.parent is not rig or o.parent_bone.startswith('handslot'):
        continue
    mesh = o.evaluated_get(graph).to_mesh()
    mesh.transform(o.matrix_world)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-4)
    bmesh.ops.holes_fill(bm, edges=bm.edges[:], sides=0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    v = np.array([vert.co[:] for vert in bm.verts], float)
    f = np.array([[vert.index for vert in face.verts] for face in bm.faces], np.int64)
    bm.free()
    o.evaluated_get(graph).to_mesh_clear()
    # Blender is Z-up; the studio and Godot are Y-up.
    parts[o.name + '__v'] = np.column_stack([v[:, 0], v[:, 2], -v[:, 1]])
    parts[o.name + '__f'] = f
np.savez_compressed(target, **parts)
print('POSED', target, sorted(name[:-3] for name in parts if name.endswith('__v')))
