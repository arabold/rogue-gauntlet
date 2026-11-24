"""Register an image-to-3D reconstruction in the rig frame and reduce each part to its triangle budget.

The reconstruction (Tripo, multiview, separate parts) faces +X with z in [-0.5, 0.5]; the KayKit rigs face -Y with the
feet on z = 0. Parts keep their dense copy as `REF_<part>` (the shape reference for limb rings and occlusion-free
sampling) and get a low-poly copy `LP_<part>`.
"""

import math

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

from .chains import polyline


def vertex_array(me, attribute="co"):
    """Every vertex's `co` (or another vector such as `normal`) of mesh `me` as an (n, 3) float array."""
    values = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get(attribute, values)
    return values.reshape(-1, 3).astype(float)


def import_parts(path, config):
    """Import the reconstruction, register it to the rig frame and join the pieces each named part lists."""
    bpy.ops.import_scene.gltf(filepath=str(path))
    pieces = [o for o in bpy.data.objects if o.type == "MESH"]
    for o in pieces:
        o.data.transform(o.matrix_world)
        o.matrix_world = Matrix.Identity(4)
        o.parent = None
    for o in [o for o in bpy.data.objects if o.type != "MESH"]:
        bpy.data.objects.remove(o)
    names = {i: part for part, indices in config["parts"].items() for i in indices}
    by_index = {int(o.name.rsplit("_", 1)[-1]): o for o in pieces}
    if set(by_index) != set(names):
        raise ValueError(f"reconstruction.json parts must name every piece exactly once: pieces {sorted(by_index)}")

    # Rotate the +X front to -Y, scale to the authored height, feet at z = 0, centered on the reference parts.
    points = {o: vertex_array(o.data) for o in pieces}
    zmin = min(p[:, 2].min() for p in points.values())
    zmax = max(p[:, 2].max() for p in points.values())
    anchors = [by_index[i] for part in config["center_on"] for i in config["parts"][part]]
    c = Vector(np.mean([points[o].mean(axis=0) for o in anchors], axis=0))
    xf = (Matrix.Translation(Vector(config.get("offset", (0, 0, 0)))) @ Matrix.Scale(config["height"] / (zmax - zmin), 4)
          @ Matrix.Rotation(math.radians(-90), 4, "Z") @ Matrix.Translation(Vector((-c.x, -c.y, -zmin))))
    for o in pieces:
        o.data.transform(xf)

    dense = bpy.data.collections.new("REF_Dense")
    bpy.context.scene.collection.children.link(dense)
    for part, indices in config["parts"].items():
        objs = [by_index[i] for i in indices]
        bpy.ops.object.select_all(action="DESELECT")
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        if len(objs) > 1:
            bpy.ops.object.join()
        o = bpy.context.view_layer.objects.active
        o.name = o.data.name = "REF_" + part
        for col in o.users_collection:
            col.objects.unlink(o)
        dense.objects.link(o)
        bm = bmesh.new()
        bm.from_mesh(o.data)
        if (box := config.get("drop_islands", {}).get(part)) is not None:
            # Loose shells a part should not keep, such as the chain links hanging from the minotaur's shackles, which
            # would swing stiffly with the forearm: every island whose center lies inside the box goes. Islands are
            # found before welding, which joins shells that merely touch.
            low, high = Vector(box["min"]), Vector(box["max"])
            drop = []
            for island in _islands(bm):
                verts = {v for f in island for v in f.verts}
                center = sum((v.co for v in verts), Vector()) / len(verts)
                if all(low[k] <= center[k] <= high[k] for k in range(3)):
                    drop += island
            bmesh.ops.delete(bm, geom=drop, context="FACES")
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        bm.to_mesh(o.data)
        bm.free()


def _islands(bm):
    """The faces of each connected island of a bmesh."""
    islands, seen = [], set()
    for face in bm.faces:
        if face in seen:
            continue
        stack, island = [face], []
        seen.add(face)
        while stack:
            f = stack.pop()
            island.append(f)
            for e in f.edges:
                for g in e.link_faces:
                    if g not in seen:
                        seen.add(g)
                        stack.append(g)
        islands.append(island)
    return islands


def _collapse(obj, target, symmetric=False):
    # Two passes: a single collapse from ~10^5 triangles to a few hundred leaves more slivers.
    for step in (0.06, None):
        tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
        ratio = step if step and tris * step > target * 3 else target / tris
        m = obj.modifiers.new("dec", "DECIMATE")
        m.decimate_type = "COLLAPSE"
        m.ratio = min(1.0, ratio)
        m.use_collapse_triangulate = True
        if symmetric:
            m.use_symmetry = True
            m.symmetry_axis = "X"
        with bpy.context.temp_override(object=obj, active_object=obj):
            bpy.ops.object.modifier_apply(modifier=m.name)


def _collapse_islands(obj, target, keep=0.02):
    """Collapse each island on its own share of the budget and drop the small enclosed ones.

    Overlapping pieces (a slime's flange lobes) collapsed together spend the budget where they intersect and shatter
    into shards; double-walled pieces hide small closed shells inside that no one sees.
    """
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    islands = [i for i in _islands(bm) if len(i) >= keep * len(bm.faces)]
    total = sum(len(i) for i in islands)
    pieces = []
    for island in islands:
        one = bmesh.new()
        index = {}
        for f in island:
            for v in f.verts:
                if v not in index:
                    index[v] = one.verts.new(v.co)
            one.faces.new([index[v] for v in f.verts])
        me = bpy.data.meshes.new(obj.name + "_island")
        one.to_mesh(me)
        one.free()
        piece = bpy.data.objects.new(me.name, me)
        bpy.context.scene.collection.objects.link(piece)
        _collapse(piece, max(8, round(target * len(island) / total)))
        pieces.append(piece)
    bm.free()
    out = bmesh.new()
    for piece in pieces:
        out.from_mesh(piece.data)
        bpy.data.objects.remove(piece)
    out.to_mesh(obj.data)
    out.free()


def decimate(config):
    """Collapse each dense part to its budget, symmetric where the design is and island by island for the parts in
    `by_island`."""
    low = bpy.data.collections.new("LOW")
    bpy.context.scene.collection.children.link(low)
    for part, target in config["budgets"].items():
        ref = bpy.data.objects["REF_" + part]
        lo = ref.copy()
        lo.data = ref.data.copy()
        lo.name = lo.data.name = "LP_" + part
        low.objects.link(lo)
        if part in config.get("by_island", ()):
            _collapse_islands(lo, target)
        else:
            _collapse(lo, target, part in config.get("symmetric", ()))


def rebuild_rings(config, chains):
    """Replace limb parts with ring loops along their joint chains, so elbows and knees deform on clean loops.

    Each ring casts rays from the chain outward onto the dense part; the end rings are capped, since sleeves, cuffs,
    skirts and boots hide them.
    """
    for part, spec in config.get("rings", {}).items():
        chain = chains[spec["chain"]]
        if "heights" in spec:  # legs: stations by height, from the top joint down
            top, bottom = chain[0].z, chain[-1].z
            fractions = [(top - z) / (top - bottom) for z in spec["heights"]]
        else:
            fractions = spec["fractions"]
        sides = spec.get("sides", 8)
        ref = bpy.data.objects["REF_" + part]
        bm_ref = bmesh.new()
        bm_ref.from_mesh(ref.data)
        tree = BVHTree.FromBMesh(bm_ref)
        bm = bmesh.new()
        prev, loops = None, []
        for f in fractions:
            c, d = polyline(chain, f)
            u = d.orthogonal().normalized()
            v = d.cross(u).normalized()
            dirs = [u * math.cos(2 * math.pi * k / sides) + v * math.sin(2 * math.pi * k / sides) for k in range(sides)]
            radii = []
            for k, r in enumerate(dirs):
                hit = tree.ray_cast(c, r, 0.4)
                radii.append((hit[0] - c).length if hit[0] is not None else (prev[k] if prev else 0.05))
            prev = radii
            loops.append([bm.verts.new(c + r * radius) for r, radius in zip(dirs, radii)])
        for a, b in zip(loops, loops[1:]):
            for k in range(sides):
                bm.faces.new((a[k], a[(k + 1) % sides], b[(k + 1) % sides], b[k]))
        bm.faces.new(loops[0])
        bm.faces.new(loops[-1])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(bpy.data.objects["LP_" + part].data)
        bm.free()
        bm_ref.free()
