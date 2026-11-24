"""Color every low-poly face from the approved turnaround and map it into the palette atlas.

Each face samples the orthographic view that faces it best and sees it unoccluded, snaps that color to the nearest of
its part's palette slots and gets palette UVs. The ramp position follows part-relative height and slope with the
Studio's ramp (`core.color_math.ramp_coordinates`), per vertex so the faces of a slot share UVs.
"""

import math

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from core.color_math import ramp_coordinates, srgb_to_lab

from .parts import vertex_array

# Camera directions (object to camera) of the turnaround views in the rig frame: the front looks at -Y.
VIEWS = {"front": Vector((0, -1, 0)), "left": Vector((1, 0, 0)), "back": Vector((0, 1, 0)), "right": Vector((-1, 0, 0))}
BACKGROUND = 232  # a pixel with every channel at or above this is the white sheet
# A part's height ramp reaches full contrast over twice the Studio's span: the approved sources were built so, which
# shades parts under 0.14 m (fangs, eyes, hinges, clasps) flatter than a Studio export would.
RAMP_SPAN = 0.16


def _horizontal(view, p):
    """Picture x (increasing to the right) of rig-frame points seen from `view`."""
    return {"front": p[..., 0], "left": p[..., 1], "back": -p[..., 0], "right": -p[..., 1]}[view]


def _load_views(folder):
    images, boxes = {}, {}
    for n in VIEWS:
        im = bpy.data.images.load(f"{folder}/{n}.png")
        im.colorspace_settings.name = "Non-Color"
        w, h = im.size
        a = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)[::-1, :, :3] * 255
        bpy.data.images.remove(im)
        images[n] = a
        ys, xs = np.nonzero((a < BACKGROUND).any(axis=2))
        boxes[n] = (xs.min(), ys.min(), xs.max() + 1, ys.max() + 1)
    return images, boxes


def _register(images, boxes, points):
    """Per view: scale from the figure's height, horizontal offset from the box centers, refined by silhouette overlap."""
    zmin, zmax = points[:, 2].min(), points[:, 2].max()
    reg = {}
    for n in VIEWS:
        x0, y0, x1, y1 = boxes[n]
        s = (y1 - y0) / (zmax - zmin)
        u = _horizontal(n, points)
        off = (x0 + x1) / 2 - s * (u.min() + u.max()) / 2
        mask = (images[n] < BACKGROUND).any(axis=2)
        py = np.clip(np.round(y0 + (zmax - points[:, 2]) * s).astype(int), 0, mask.shape[0] - 1)
        best = (-1.0, 0)
        for dx in range(-12, 13):
            px = np.clip(np.round(off + dx + s * u).astype(int), 0, mask.shape[1] - 1)
            sil = np.zeros_like(mask)
            sil[py, px] = True
            best = max(best, ((sil & mask).sum() / max((sil | mask).sum(), 1), dx))
        reg[n] = (s, off + best[1], y0, zmax)
    return reg


def _occluders(objects):
    """One BVH tree over every low-poly part, which hides faces from the views."""
    verts, polys, base = [], [], 0
    for o in objects:
        verts.append(vertex_array(o.data))
        polys += [[base + i for i in p.vertices] for p in o.data.polygons]
        base += len(verts[-1])
    return BVHTree.FromPolygons(np.vstack(verts), polys)


def _material(config, atlas_png):
    img = bpy.data.images.load(str(atlas_png))
    img.name = config["name"].lower() + "_palette"
    img.pack()
    mat = bpy.data.materials.new(f"{config['name']} | Palette")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    uv = nt.nodes.new("ShaderNodeUVMap")
    uv.uv_map = "Palette"
    nt.links.new(uv.outputs["UV"], tex.inputs["Vector"])
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.85
    bsdf.inputs["Metallic"].default_value = 0.0
    return mat


def _region_test(spec):
    """A face-detail slot (eye, brow, mouth) is only allowed on the front of the face, where the turnaround draws it;
    elsewhere the sheet's darker skin shading would match it."""
    front = VIEWS["front"]
    return lambda c, n: (spec["z"][0] < c.z < spec["z"][1] and abs(c.x) < spec["half_width"]
                         and n.dot(front) > spec["facing"])


def _sample(c, nrm, images, reg, tree):
    """The sheet's median color around face center `c` in the view that faces it best and sees it unoccluded, or None
    when no view shows it."""
    for n in sorted(VIEWS, key=lambda n: -nrm.dot(VIEWS[n])):
        if nrm.dot(VIEWS[n]) < 0.05:
            return None
        if tree.ray_cast(c + nrm * 0.004, VIEWS[n], 10.0)[0] is not None:
            continue
        s, off, y0, zmax = reg[n]
        ix = int(round(float(off + s * _horizontal(n, np.array(c[:])))))
        iy = int(round(float(y0 + (zmax - c.z) * s)))
        patch = images[n][max(iy - 1, 0):iy + 2, max(ix - 1, 0):ix + 2].reshape(-1, 3)
        patch = patch[(patch < BACKGROUND).any(axis=1)]
        if len(patch):
            return np.median(patch, axis=0)
    return None


def _label_faces(me, slot_labs, regions, under, sample):
    """Each face's slot (an index into `slot_labs`, the slots' Lab colors) and whether the turnaround showed it.

    A face takes the slot nearest its sampled color among those its `regions` allow; `under` gives faces turned down
    past its `facing` its slot outright.
    """
    labels = np.zeros(len(me.polygons), int)
    seen = np.zeros(len(me.polygons), bool)
    for p in me.polygons:
        c, nrm = p.center, p.normal
        if under and nrm.z < -under["facing"]:
            labels[p.index], seen[p.index] = under["slot"], True
            continue
        color = sample(c, nrm)
        if color is None:
            continue
        dist = ((slot_labs - srgb_to_lab(color)) ** 2).sum(-1)
        for i, allowed in regions.items():
            if not allowed(c, nrm):
                dist[i] = np.inf
        labels[p.index], seen[p.index] = int(np.argmin(dist)), True
    return labels, seen


def _settle(me, labels, seen, n_slots, details):
    """Unseen faces and lone specks take their neighbours' majority slot over three passes; face-detail slots
    (`details`) may stand alone."""
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    seen = seen.copy()
    for _ in range(3):
        new = labels.copy()
        for f in bm.faces:
            nb = [g.index for e in f.edges for g in e.link_faces if g is not f]
            if not nb:
                continue
            cnt = np.bincount(labels[nb], minlength=n_slots)
            if not seen[f.index] or (cnt[labels[f.index]] == 0 and labels[f.index] not in details):
                new[f.index] = int(np.argmax(cnt))
        labels = new
        seen[:] = True
    bm.free()
    return labels


def _shade(me, smooth_angle):
    """Smooth shading with hard edges past `smooth_angle` degrees replaces the reconstruction's custom normals."""
    if "custom_normal" in me.attributes:
        me.attributes.remove(me.attributes["custom_normal"])
    for p in me.polygons:
        p.use_smooth = True
    me.set_sharp_from_angle(angle=math.radians(smooth_angle))


def _write_uvs(me, cells, labels):
    """The `Palette` UV layer, alone: each face's slot cell (`cells`, by slot index) at its vertices' ramp positions."""
    if "Palette" not in me.uv_layers:
        me.uv_layers.new(name="Palette")
    for layer in [layer for layer in me.uv_layers if layer.name != "Palette"]:
        me.uv_layers.remove(layer)
    t = ramp_coordinates(vertex_array(me)[:, 2], vertex_array(me, "normal")[:, 2], full_span=RAMP_SPAN)
    vertex = np.empty(len(me.loops), int)
    me.loops.foreach_get("vertex_index", vertex)
    totals = np.empty(len(me.polygons), int)
    me.polygons.foreach_get("loop_total", totals)
    cell = np.repeat(labels, totals)  # loops are stored face by face
    u, top, bottom = (np.array([c[key] for c in cells])[cell] for key in ("u", "v_top", "v_bot"))
    v = top + t[vertex] * (bottom - top)
    me.uv_layers["Palette"].data.foreach_set("uv", np.column_stack([u, 1.0 - v]).ravel())


def paint(config, views_folder, atlas_png, atlas, colors):
    """Assign palette slots and UVs to every `LP_` part; returns the face count per part and slot. `colors` gives each
    slot's base color, the color the turnaround is matched against. `undersides` (`{part: {"slot", "facing"}}`) gives
    faces turned down past `facing` a slot outright, since no view of the turnaround shows a belly from below."""
    images, boxes = _load_views(views_folder)
    dense = [o for o in bpy.data.objects if o.name.startswith("REF_")]
    reg = _register(images, boxes, np.vstack([vertex_array(o.data) for o in dense]))
    low = [o for o in bpy.data.objects if o.name.startswith("LP_")]
    tree = _occluders(low)
    regions = {slot: _region_test(spec) for slot, spec in config.get("regions", {}).items()}
    lab_of = {slot: srgb_to_lab(rgb) for slot, rgb in colors.items()}
    mat = _material(config, atlas_png)
    report = {}
    for o in low:
        part = o.name[3:]
        slots = config["slots"][part]
        under = config.get("undersides", {}).get(part)
        labels, seen = _label_faces(
            o.data, np.array([lab_of[s] for s in slots]),
            {i: regions[s] for i, s in enumerate(slots) if s in regions},
            under and {**under, "slot": slots.index(under["slot"])},
            lambda c, nrm: _sample(c, nrm, images, reg, tree))
        labels = _settle(o.data, labels, seen, len(slots), {i for i, s in enumerate(slots) if s in regions})
        report[part] = {slots[i]: int(c) for i, c in enumerate(np.bincount(labels, minlength=len(slots)))}
        _shade(o.data, config.get("smooth_angle", 35))
        _write_uvs(o.data, [atlas[s] for s in slots], labels)
        o.data.materials.clear()
        o.data.materials.append(mat)
    return report
