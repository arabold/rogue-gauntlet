"""Color every low-poly face from the approved turnaround and map it into the palette atlas.

Each face samples the orthographic view that faces it best and sees it unoccluded, snaps that color to the nearest of
its part's palette slots and gets palette UVs. The ramp position follows part-relative height and slope like the
Studio's character export (`core.export.gradient_coordinates`), per vertex so the faces of a slot share UVs.
"""

import math

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

# Camera directions (object to camera) of the turnaround views in the rig frame: the front looks at -Y.
VIEWS = {"front": Vector((0, -1, 0)), "left": Vector((1, 0, 0)), "back": Vector((0, 1, 0)), "right": Vector((-1, 0, 0))}
BACKGROUND = 232  # a pixel with every channel at or above this is the white sheet


def _lab(c):
    c = np.asarray(c, float) / 255.0
    c = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    m = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]])
    xyz = c @ m.T / np.array([0.9505, 1.0, 1.089])
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787 * xyz + 16 / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], -1)


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


def paint(config, views_folder, atlas_png, atlas, colors):
    """Assign palette slots and UVs to every `LP_` part; returns the face count per part and slot. `colors` gives each
    slot's base color, the color the turnaround is matched against. `undersides` (`{part: {"slot", "facing"}}`) gives
    faces turned down past `facing` a slot outright, since no view of the turnaround shows a belly from below."""
    images, boxes = _load_views(views_folder)
    dense = [o for o in bpy.data.objects if o.name.startswith("REF_")]
    reg = _register(images, boxes, np.vstack([np.array([v.co[:] for v in o.data.vertices]) for o in dense]))
    low = [o for o in bpy.data.objects if o.name.startswith("LP_")]
    verts, polys = [], []
    for o in low:
        base = len(verts)
        verts += [v.co.copy() for v in o.data.vertices]
        polys += [[base + i for i in p.vertices] for p in o.data.polygons]
    tree = BVHTree.FromPolygons(verts, polys)
    regions = {slot: _region_test(spec) for slot, spec in config.get("regions", {}).items()}
    lab_of = {slot: _lab(rgb) for slot, rgb in colors.items()}
    mat = _material(config, atlas_png)
    report = {}
    for o in low:
        part = o.name[3:]
        slots = config["slots"][part]
        lab = np.array([lab_of[s] for s in slots])
        me = o.data
        labels = np.zeros(len(me.polygons), int)
        seen = np.zeros(len(me.polygons), bool)
        under = config.get("undersides", {}).get(part)
        for p in me.polygons:
            c, nrm = p.center, p.normal
            if under and nrm.z < -under["facing"]:
                labels[p.index], seen[p.index] = slots.index(under["slot"]), True
                continue
            for n in sorted(VIEWS, key=lambda n: -nrm.dot(VIEWS[n])):
                if nrm.dot(VIEWS[n]) < 0.05:
                    break
                if tree.ray_cast(c + nrm * 0.004, VIEWS[n], 10.0)[0] is not None:
                    continue
                s, off, y0, zmax = reg[n]
                ix = int(round(float(off + s * _horizontal(n, np.array(c[:])))))
                iy = int(round(float(y0 + (zmax - c.z) * s)))
                patch = images[n][max(iy - 1, 0):iy + 2, max(ix - 1, 0):ix + 2].reshape(-1, 3)
                patch = patch[(patch < BACKGROUND).any(axis=1)]
                if len(patch) == 0:
                    continue
                dist = ((lab - _lab(np.median(patch, axis=0))) ** 2).sum(-1)
                for i, slot in enumerate(slots):
                    if slot in regions and not regions[slot](c, nrm):
                        dist[i] = np.inf
                labels[p.index] = int(np.argmin(dist))
                seen[p.index] = True
                break
        # Unseen faces and lone specks take their neighbours' majority; face details may stand alone.
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.faces.ensure_lookup_table()
        for _ in range(3):
            new = labels.copy()
            for f in bm.faces:
                nb = [g.index for e in f.edges for g in e.link_faces if g is not f]
                if not nb:
                    continue
                cnt = np.bincount(labels[nb], minlength=len(slots))
                if not seen[f.index] or (cnt[labels[f.index]] == 0 and slots[labels[f.index]] not in regions):
                    new[f.index] = int(np.argmax(cnt))
            labels = new
            seen[:] = True
        bm.free()
        report[part] = {slots[i]: int(c) for i, c in enumerate(np.bincount(labels, minlength=len(slots)))}

        # Smooth shading with hard edges past the authored angle replaces the reconstruction's custom normals.
        if "custom_normal" in me.attributes:
            me.attributes.remove(me.attributes["custom_normal"])
        for p in me.polygons:
            p.use_smooth = True
        me.set_sharp_from_angle(angle=math.radians(config.get("smooth_angle", 35)))
        uv = me.uv_layers.get("Palette") or me.uv_layers.new(name="Palette")
        for layer in [layer for layer in me.uv_layers if layer.name != "Palette"]:
            me.uv_layers.remove(layer)
        zs = np.array([v.co.z for v in me.vertices])
        span = max(zs.max() - zs.min(), 1e-4)
        weight = float(np.clip(span / 0.08 * 0.5, 0.25, 0.85))
        normal_z = np.array([v.normal.z for v in me.vertices])
        t = np.clip(0.5 + ((1 - (zs - zs.min()) / span) - 0.5) * weight + np.clip(-normal_z * 0.35, -0.25, 0.25), 0.06, 0.94)
        for p in me.polygons:
            cell = atlas[slots[labels[p.index]]]
            for li in p.loop_indices:
                v = cell["v_top"] + float(t[me.loops[li].vertex_index]) * (cell["v_bot"] - cell["v_top"])
                uv.data[li].uv = (cell["u"], 1.0 - v)
        me.materials.clear()
        me.materials.append(mat)
    return report
