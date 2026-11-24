"""Color every low-poly face from the approved turnaround and map it into the palette atlas.

Each face samples the orthographic view that faces it best and sees it unoccluded, snaps that color to the nearest of
its part's palette slots and gets palette UVs. The ramp position follows part-relative height and slope with the
Studio's ramp (`core.color_math.ramp_coordinates`), per vertex so the faces of a slot share UVs.
"""

import functools
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
        pixels = np.empty(w * h * 4, np.float32)
        im.pixels.foreach_get(pixels)
        a = pixels.reshape(h, w, 4)[::-1, :, :3] * 255
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
    return BVHTree.FromPolygons(np.vstack(verts).tolist(), polys)


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


def _label_faces(me, slots, lab_of, regions, under, sample):
    """Each face's slot (an index into `slots`) and whether the turnaround showed it.

    A face takes the slot with a shade (`lab_of`, Lab colors per slot) nearest its sampled color, among those its
    `regions` allow; `under` gives faces turned down past its `facing` its slot outright.
    """
    slot_labs = np.array([lab_of[s] for s in slots])
    labels = np.zeros(len(me.polygons), int)
    seen = np.zeros(len(me.polygons), bool)
    for p in me.polygons:
        c, nrm = p.center, p.normal
        if under and nrm.z < -under["facing"]:
            labels[p.index], seen[p.index] = slots.index(under["slot"]), True
            continue
        color = sample(c, nrm)
        if color is None:
            continue
        dist = ((slot_labs - srgb_to_lab(color)) ** 2).sum(-1).min(-1)
        for i, slot in enumerate(slots):
            if slot in regions and not regions[slot](c, nrm):
                dist[i] = np.inf
        labels[p.index], seen[p.index] = int(np.argmin(dist)), True
    return labels, seen


def _neighbours(me):
    """Each face's edge-adjacent faces."""
    bm = bmesh.new()
    bm.from_mesh(me)
    neighbours = [np.array([g.index for e in f.edges for g in e.link_faces if g is not f], int) for f in bm.faces]
    bm.free()
    return neighbours


def _settle(neighbours, labels, seen, slots, details):
    """Unseen faces and lone specks take their neighbours' majority slot over three passes; face-detail slots
    (`details`) may stand alone."""
    for _ in range(3):
        new = labels.copy()
        for f, nb in enumerate(neighbours):
            if not nb.size:
                continue
            cnt = np.bincount(labels[nb], minlength=len(slots))
            if not seen[f] or (cnt[labels[f]] == 0 and slots[labels[f]] not in details):
                new[f] = int(np.argmax(cnt))
        labels, seen = new, np.ones_like(seen)
    return labels


def _absorb_islands(neighbours, labels, slots, details, min_faces):
    """Relabel each connected same-slot island under `min_faces` faces with the slot bordering it most, leaving
    face-detail slots (`details`) alone, until no such island remains."""
    for _ in range(4):
        changed, done = False, np.zeros(len(labels), bool)
        for start in range(len(labels)):
            if done[start]:
                continue
            label, island, border, stack = labels[start], [], [], [start]
            done[start] = True
            while stack:
                f = stack.pop()
                island.append(f)
                for g in neighbours[f]:
                    if labels[g] != label:
                        border.append(labels[g])
                    elif not done[g]:
                        done[g] = True
                        stack.append(g)
            if len(island) < min_faces and border and slots[label] not in details:
                labels[island] = np.bincount(border).argmax()
                changed = True
        if not changed:
            break
    return labels


def _shade(me, smooth_angle):
    """Smooth shading with hard edges past `smooth_angle` degrees replaces the reconstruction's custom normals."""
    if "custom_normal" in me.attributes:
        me.attributes.remove(me.attributes["custom_normal"])
    me.polygons.foreach_set("use_smooth", np.ones(len(me.polygons), bool))
    me.set_sharp_from_angle(angle=math.radians(smooth_angle))


def _write_uvs(me, cells, labels):
    """The `Palette` UV layer, alone: each face's slot cell (`cells`, by slot index) at its vertices' ramp positions."""
    if "Palette" not in me.uv_layers:
        me.uv_layers.new(name="Palette")
    for layer in [layer for layer in me.uv_layers if layer.name != "Palette"]:
        me.uv_layers.remove(layer)
    t = ramp_coordinates(vertex_array(me)[:, 2], vertex_array(me, "normal")[:, 2], full_span=RAMP_SPAN)
    vertex = np.empty(len(me.loops), np.int32)
    me.loops.foreach_get("vertex_index", vertex)
    totals = np.empty(len(me.polygons), np.int32)
    me.polygons.foreach_get("loop_total", totals)
    cell = np.repeat(labels, totals)  # loops are stored face by face
    u, top, bottom = (np.array([c[key] for c in cells])[cell] for key in ("u", "v_top", "v_bot"))
    v = top + t[vertex] * (bottom - top)
    me.uv_layers["Palette"].data.foreach_set("uv", np.column_stack([u, 1.0 - v]).astype(np.float32).ravel())


def map_palette(me, cells, labels, mat, smooth_angle):
    """Shade a labelled mesh and give it the palette's UVs (`cells`, by label) and material: the last step of `paint`,
    and all a procedural prop needs, since its faces already name their slots."""
    _shade(me, smooth_angle)
    _write_uvs(me, cells, labels)
    me.materials.clear()
    me.materials.append(mat)


def paint(config, views_folder, atlas_png, atlas, colors):
    """Assign palette slots and UVs to every `LP_` part; returns the face count per part and slot. `colors` gives each
    slot's base color, the color the turnaround is matched against; `shades` (value multipliers, default `[1]`) also
    matches each slot's lit and shadowed tones, so a palette of albedo colors matches the shaded sheet. `undersides`
    (`{part: {"slot", "facing"}}`) gives faces turned down past `facing` a slot outright, since no view of the
    turnaround shows a belly from below. `min_island` absorbs same-slot islands of fewer faces into their
    surroundings, as speckle the sheet's shading and registration leave on broad plates."""
    images, boxes = _load_views(views_folder)
    dense = [o for o in bpy.data.objects if o.name.startswith("REF_")]
    reg = _register(images, boxes, np.vstack([vertex_array(o.data) for o in dense]))
    low = [o for o in bpy.data.objects if o.name.startswith("LP_")]
    regions = {slot: _region_test(spec) for slot, spec in config.get("regions", {}).items()}
    shades = np.asarray(config.get("shades", [1.0]), float)
    lab_of = {slot: srgb_to_lab(np.clip(np.outer(shades, rgb), 0, 255)) for slot, rgb in colors.items()}
    sample = functools.partial(_sample, images=images, reg=reg, tree=_occluders(low))
    mat = _material(config, atlas_png)
    report = {}
    for o in low:
        part = o.name[3:]
        slots = config["slots"][part]
        labels, seen = _label_faces(o.data, slots, lab_of, regions, config.get("undersides", {}).get(part), sample)
        neighbours = _neighbours(o.data)
        labels = _settle(neighbours, labels, seen, slots, regions)
        labels = _absorb_islands(neighbours, labels, slots, regions, config.get("min_island", 1))
        report[part] = {slots[i]: int(c) for i, c in enumerate(np.bincount(labels, minlength=len(slots)))}
        map_palette(o.data, [atlas[s] for s in slots], labels, mat, config.get("smooth_angle", 35))
    return report
