"""KayKit dungeon walls (CC0) dressed for realms: moss and jungle growth, sewer slime and drain pipes,
prison chains, and KayKit-style alcoves holding shelves of books, skulls, candles or nothing.
(KayKit's own `wall_broken` is the breach.)

The original wall is the baseline. Growth and fixtures are closed, separate pieces placed by casting
rays onto the wall's own surfaces, at KayKit's exaggerated scale. Alcoves and pipe bores are real openings: KayKit's flat face
panel is re-triangulated around each hole, keeping its outer edges, and the hole is closed by recess
walls sharing its vertices. Everything keeps clear of piece ends, so the original seams and
pairings with plain KayKit walls are untouched.
"""

import manifold3d as mf
import numpy as np

from core import kit
from core.export import corner_normals
from tiles import kaykit
from tiles.caves import beam
from items.solid import welded as solid_welded
from tiles.geometry import offset, rng, union
from prefabs import decor
from tiles.walls import HALF_THICK, PIECES

END_MARGIN = .3


def surfaces(segment, side, s, y, mesh):
    """Hit points and normals on one side of a wall segment for samples (s along, y up)."""
    (ox, oz), (ux, uz), _ = segment
    along, normal = np.array([ux, 0.0, uz]), np.array([uz, 0.0, -ux]) * side
    origins = np.array([ox, 0.0, oz]) + np.outer(s, along) + np.outer(y, [0, 1, 0]) + normal * 1.5
    distance, hit = kit.ray_distance(origins, np.tile(-normal, (len(s), 1)), mesh.verts, mesh.faces, faces_hit=True)
    return surface_hits(origins, -normal, distance, hit, mesh)


def tops(points, mesh):
    """Hit points and normals straight down onto the wall from above."""
    origins = np.column_stack([points[:, 0], np.full(len(points), 6.0), points[:, 1]])
    distance, hit = kit.ray_distance(origins, np.tile([0, -1.0, 0], (len(points), 1)), mesh.verts, mesh.faces, faces_hit=True)
    return surface_hits(origins, np.array([0, -1.0, 0]), distance, hit, mesh)


def surface_hits(origins, direction, distance, hit, mesh):
    ok = np.isfinite(distance)
    points = origins + direction * np.where(ok, distance, 0)[:, None]
    tri = mesh.verts[mesh.faces[np.where(ok, hit, 0)]]
    normals = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    normals /= np.maximum(np.linalg.norm(normals, axis=1, keepdims=True), 1e-12)
    normals *= np.sign(-(normals @ direction))[:, None]
    return points[ok], normals[ok]


def frame(normal):
    """Two tangents completing an orthonormal frame around a surface normal."""
    helper = np.array([0, 1.0, 0]) if abs(normal[1]) < .9 else np.array([1.0, 0, 0])
    t1 = np.cross(normal, helper)
    t1 /= np.linalg.norm(t1)
    return t1, np.cross(normal, t1)


def blob(r, point, normal, radius, thickness, slot, sides=7, stretch=(1.0, 1.0)):
    """A rounded cushion of growth hugging the surface, its base sunk slightly into the stone."""
    t1, t2 = frame(normal)
    angles = (np.arange(sides) + r.uniform(-.3, .3, sides)) * 2 * np.pi / sides
    ring = np.cos(angles)[:, None] * t1 * stretch[0] + np.sin(angles)[:, None] * t2 * stretch[1]
    ring *= r.uniform(.8, 1.1, (sides, 1)) * radius
    lift = thickness * r.uniform(.85, 1.15)
    points = np.vstack([point + ring - normal * .02, point + ring * .85 + normal * lift * .65,
                        point + ring * .45 + normal * lift, [point + normal * lift * 1.08], [point - normal * lift * .6]])
    piece = kit.hull(points, slot)
    piece.normals = corner_normals(piece.verts, piece.faces, crease=55.0)
    return piece


def strand(r, top, normal, length, radius, slot):
    """A hanging vine or slime run: a tapered tube down the face, flattened onto it."""
    t1, _ = frame(normal)
    ys = np.linspace(0, length, max(3, int(length / .12) + 1))
    sway = np.cumsum(r.normal(0, .015, len(ys)))
    path = top + np.outer(-ys, [0, 1.0, 0]) + np.outer(sway, t1) + normal * radius * .3
    piece = kit.tube(path, radius, slot, sides=5, taper=r.uniform(.82, .95))
    offset = (piece.verts - top) @ normal
    piece = kit.Piece(piece.verts - np.outer(offset * .45, normal), piece.faces, piece.slots)
    piece.normals = corner_normals(piece.verts, piece.faces, crease=60.0)
    return piece, path[-1]


def blade(r, root, normal, height, slot, room, width=(.012, .022)):
    """One grass blade: a thin three-sided spike leaning off the wall, at most `room` outward."""
    lean = normal * r.uniform(.15, .45) + r.normal(0, .2, 3) * [1, 0, 1]
    lean *= min(1.0, max(room - .025, 0.0) / max(abs(lean @ normal) * height, 1e-6))
    tip = root + np.array([0, height, 0]) + lean * height
    piece = kit.spike(root - [0, .02, 0], tip, r.uniform(*width), slot, sides=3)
    piece.normals = corner_normals(piece.verts, piece.faces, crease=40.0)
    return piece


def goo(r, segment, side, s0, s1, mesh, radius, slot):
    """A slime line along the top of a face, thickest in the middle, both ends rounded off."""
    s = np.linspace(s0, s1, max(4, int((s1 - s0) / .15)))
    points, normals = surfaces(segment, side, s, 3.2 + r.normal(0, .02, len(s)), mesh)
    if len(points) < 3:
        return None
    path = points + normals * radius * .25
    t = np.linspace(0, 1, len(path))
    profile = np.column_stack([np.cos(a := np.arange(6) * np.pi / 3), np.sin(a)]) * radius
    piece = kit.sweep(path, profile, slot, up=(0.0, 1.0, 0.0), scale=np.maximum(np.sin(np.pi * t) ** .5, .25))
    piece.normals = corner_normals(piece.verts, piece.faces, crease=70.0)
    return piece


FACE_Z, PANEL_SLOT = .25, "kaykit_01"
# KayKit exaggerates props: fixtures read at the game camera only when they are chunky.
PIPE, LINK = .3, (.3, .18, .045)


def arch(cx, y0, width, height, rise=None, segments=9):
    """An arched opening outline (x, y), counter-clockwise: straight jambs under a half-round head, or
    a flatter elliptical head `rise` tall."""
    r = width / 2
    rise = r if rise is None else rise
    spring = y0 + height - rise
    head = [(cx + r * np.cos(a), spring + rise * np.sin(a)) for a in np.linspace(0, np.pi, segments + 1)]
    return np.array([(cx - r, y0), (cx + r, y0), *head])


def circle(cx, cy, radius, sides=12):
    a = np.arange(sides) * 2 * np.pi / sides
    return np.column_stack([cx + radius * np.cos(a), cy + radius * np.sin(a)])


def carve(mesh, holes, slots):
    """Cut holes into the flat face panels and close each with recess or tunnel walls.

    `holes` are dicts with `side` (+1 front, -1 back, 0 through both), `outline` (x, y, counter-
    clockwise, convex), `depth` and optional `bevel` (a chamfer around the opening's edge). A through
    hole is lined by a tunnel instead of a recess. Holes on one face must not overlap. KayKit's panel
    is one quad per side; its corners stay, so the panel's outer edges and neighbors are unchanged.
    Brick accents on a panel that a hole would cut are removed with it.
    """
    verts, normals = list(mesh.verts), list(mesh.normals)
    faces, face_slots = [list(f) for f in mesh.faces], list(mesh.slots)
    ramp = list(mesh.uv_t) if mesh.uv_t is not None else [np.full(3, np.nan)] * len(faces)
    panel_ramp = {}
    keep = np.ones(len(faces), bool)
    tri = mesh.verts[mesh.faces]
    area = np.linalg.norm(np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0]), axis=1) / 2
    ring = {}

    def add_face(a, b, c, slot, facing, gradient=None):
        n = np.cross(verts[b] - verts[a], verts[c] - verts[a])
        if n @ facing < 0:
            b, c = c, b
            n = -n
        n = n / max(np.linalg.norm(n), 1e-12)
        faces.append([a, b, c])
        face_slots.append(slot)
        normals.append(np.tile(n, (3, 1)))
        ramp.append(np.full(3, np.nan) if gradient is None else np.array([gradient(verts[i]) for i in (a, b, c)]))

    labels = label_of_faces(mesh)
    components = accents(mesh, labels)
    for side in (1, -1):
        mine = [(i, h) for i, h in enumerate(holes) if h["side"] in (side, 0)]
        if not mine:
            continue
        panel = np.flatnonzero(np.all(np.abs(tri[:, :, 2] - side * FACE_Z) < 1e-6, axis=1) & (area > .5))
        keep[panel] = False
        if mesh.uv_t is not None:
            # The panel's gradient is affine in (x, y); refit it so cut panels keep KayKit's shading.
            corners = tri[panel].reshape(-1, 3)
            fit = np.linalg.lstsq(np.column_stack([corners[:, :2], np.ones(len(corners))]), mesh.uv_t[panel].ravel(), rcond=None)[0]
            panel_ramp[side] = lambda v, fit=fit: float(fit @ [v[0], v[1], 1.0])
        for label, (lo, hi) in components.items():
            # A margin clears room for an alcove's framing stones as well as the opening itself.
            if any(np.all(lo[:2] < h["outline"].max(0) + .04) and np.all(hi[:2] > h["outline"].min(0) - .04)
                   for _, h in mine) and np.sign(hi[2] + lo[2]) == side:
                keep[labels == label] = False
        rim = boundary(tri[panel])
        outlines = [rim[:, :2]] + [h["outline"] for _, h in mine]
        start = len(verts)
        verts.extend(rim)
        for outline in outlines[1:]:
            verts.extend(np.column_stack([outline, np.full(len(outline), side * FACE_Z)]))
        offsets = np.cumsum([start] + [len(o) for o in outlines])
        for i, (index, _) in enumerate(mine):
            ring[(index, side)] = list(range(offsets[i + 1], offsets[i + 2]))
        polygons = [outlines[0]] + [o[::-1] for o in outlines[1:]]
        order = np.concatenate([np.arange(offsets[0], offsets[1])] +
                               [np.arange(offsets[i + 1], offsets[i + 2])[::-1] for i in range(len(mine))])
        for a, b, c in np.asarray(mf.triangulate(polygons)):
            add_face(order[a], order[b], order[c], PANEL_SLOT, np.array([0, 0, side]), panel_ramp.get(side))
    def shade(spec):
        """A recess slot, optionally `(slot, t)`: a fixed position on that KayKit swatch's gradient."""
        return (spec[0], lambda v, t=spec[1]: t) if isinstance(spec, tuple) else (spec, None)

    def band(front, back, outline, spec, gradient=None):
        slot, fixed = shade(spec)
        for k in range(len(outline)):
            j = (k + 1) % len(outline)
            # Walls face the opening's inside: the left normal of a counter-clockwise outline edge.
            edge = outline[j] - outline[k]
            inward = np.array([-edge[1], edge[0], 0.0])
            add_face(front[k], front[j], back[j], slot, inward, gradient or fixed)
            add_face(front[k], back[j], back[k], slot, inward, gradient or fixed)

    for index, hole in enumerate(holes):
        outline = hole["outline"]
        side = hole["side"]
        if side == 0:
            band(ring[(index, 1)], ring[(index, -1)], outline, slots["wall"])
            continue
        front, bevel = ring[(index, side)], hole.get("bevel", 0.0)
        inner = offset(outline, np.full(len(outline), bevel)) if bevel else outline
        if bevel:
            # A chamfer around the opening, in the face's own stone, catches light like KayKit's arches.
            lip = list(range(len(verts), len(verts) + len(inner)))
            verts.extend(np.column_stack([inner, np.full(len(inner), side * (FACE_Z - bevel))]))
            band(front, lip, outline, PANEL_SLOT, panel_ramp.get(side))
            front = lip
        back = list(range(len(verts), len(verts) + len(inner)))
        verts.extend(np.column_stack([inner, np.full(len(inner), side * (FACE_Z - hole["depth"]))]))
        order = np.asarray(back)
        back_slot, back_t = shade(slots["back"])
        for a, b, c in np.asarray(mf.triangulate([inner])):
            add_face(order[a], order[b], order[c], back_slot, np.array([0, 0, side]), back_t)
        band(front, back, inner, slots["wall"])
    kept = np.r_[keep, np.ones(len(faces) - len(keep), bool)]
    return kit.Piece(np.asarray(verts), np.asarray(faces)[kept], np.asarray(face_slots, object)[kept],
                     normals=np.asarray(normals).reshape(-1, 3, 3)[kept], uv_t=np.asarray(ramp)[kept])


def boundary(triangles):
    """The outer loop of a flat patch of triangles, counter-clockwise in (x, y), as the original 3D
    points: every vertex on it is kept exactly, so neighbors sharing those vertices stay joined."""
    points = triangles.reshape(-1, 3)
    _, first, index = np.unique(np.round(points[:, :2] * 1e5).astype(np.int64), axis=0, return_index=True, return_inverse=True)
    faces = index.reshape(-1, 3)
    uses = {}
    for a, b, c in faces:
        for u, v in ((a, b), (b, c), (c, a)):
            uses[(u, v)] = uses.get((u, v), 0) + 1
    edges = {u: v for (u, v) in uses if (v, u) not in uses}
    loop = [next(iter(edges))]
    while edges[loop[-1]] != loop[0]:
        loop.append(edges[loop[-1]])
    # The original coordinates, not the rounded keys, so the new panel welds to its neighbors exactly.
    outline = points[first[loop]]
    if np.sum(outline[:, 0] * np.roll(outline[:, 1], -1) - np.roll(outline[:, 0], -1) * outline[:, 1]) < 0:
        outline = outline[::-1]
    return outline


def label_of_faces(mesh):
    """Connected surface components of a mesh, as one label per face (positions welded)."""
    _, welded = np.unique(np.round(mesh.verts * 1e5).astype(np.int64), axis=0, return_inverse=True)
    parent = np.arange(welded.max() + 1)

    def root(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    for a, b, c in welded.ravel()[mesh.faces]:
        for u, v in ((a, b), (b, c)):
            ru, rv = root(u), root(v)
            parent[max(ru, rv)] = min(ru, rv)
    return np.array([root(i) for i in welded.ravel()[mesh.faces[:, 0]]])


def accents(mesh, labels):
    """KayKit's raised brick accents: small separate components standing off a face panel."""
    out = {}
    for label in np.unique(labels):
        points = mesh.verts[mesh.faces[labels == label]].reshape(-1, 3)
        lo, hi = points.min(0), points.max(0)
        if np.all(hi - lo < [1.6, 1.0, .5]) and np.sign(lo[2]) == np.sign(hi[2]) and min(abs(lo[2]), abs(hi[2])) >= FACE_Z - .02:
            out[label] = (lo, hi)
    return out


def cylinder(p0, p1, radius, slot, sides=12):
    """A closed round prism between two points (axis need not be vertical)."""
    p0, p1 = np.asarray(p0, float), np.asarray(p1, float)
    axis = (p1 - p0) / np.linalg.norm(p1 - p0)
    t1 = frame(axis)[0]
    t2 = np.cross(axis, t1)
    a = np.arange(sides) * 2 * np.pi / sides
    rim = np.outer(np.cos(a), t1) * radius + np.outer(np.sin(a), t2) * radius
    return kit.hull(np.vstack([p0 + rim, p1 + rim]), slot)


def pipe(r, cx, cy, slots):
    """A drain pipe run through the wall, flanged and standing out of both faces; a trickle runs from
    one end to a puddle at the foot."""
    base = np.array([cx, cy, 0.0])
    z = np.array([0, 0, 1.0])
    parts = [cylinder(base - z * .56, base + z * .56, PIPE + .1, slots["pipe"])]
    parts += [cylinder(base + z * a, base + z * b, PIPE + .18, slots["pipe"]) for a, b in ((.22, .32), (-.32, -.22))]
    tube = union(parts, cuts=[cylinder(base - z * .7, base + z * .7, PIPE, slots["pipe"])])
    side = r.choice([-1, 1])
    lip = base + z * side * .54 + [0, -PIPE - .02, 0]
    path = np.array([lip, lip + [r.normal(0, .02), -.15, 0] - z * side * .14, [cx, cy - PIPE - .45, side * .32], [cx, .62, side * .3],
                     [cx + r.normal(0, .03), .42, side * .38], [cx, .27, side * .5], [cx, .1, side * .54]])
    trickle = kit.tube(path, .09, slots["trickle"], sides=7, taper=.25)
    trickle.normals = corner_normals(trickle.verts, trickle.faces, crease=70.0)
    puddle = blob(r, np.array([cx, .06, side * .48]), np.array([0, 1.0, 0]), .36, .035, slots["trickle"], sides=10, stretch=(.42, 1.6))
    return [tube, trickle, puddle]


def link(center, up, normal, slot, length=LINK[0], width=LINK[1], wire=LINK[2]):
    """One chain link: a closed stadium-shaped loop in the plane spanned by `up` and `normal x up`."""
    across = np.cross(normal, up)
    a = np.linspace(0, np.pi, 5)
    half = (length - width) / 2
    loop = [center + up * (half + width / 2 * np.sin(t)) + across * (width / 2 * np.cos(t)) for t in a]
    loop += [center - up * (half + width / 2 * np.sin(t)) - across * (width / 2 * np.cos(t)) for t in a]
    th = np.arange(5) * 2 * np.pi / 5
    piece = kit.sweep(np.array(loop), np.column_stack([np.cos(th), np.sin(th)]) * wire, slot, up=tuple(normal), closed=True)
    piece.normals = corner_normals(piece.verts, piece.faces, crease=60.0)
    return piece


def chain(top, bottom, normal, slots):
    """Links from an anchor plate on the face (outward `normal`) down to a thick manacle band."""
    normal = np.asarray(normal, float)
    plate = top + normal * .04
    out = [beam(plate - normal * .06, plate + normal * .03, [0, 1.0, 0], .16, .16, slots["iron"], chamfer=.03)]
    count = max(3, int(np.linalg.norm(bottom - top) / (LINK[0] * .8)))
    down = np.array([0, -1.0, 0])
    for k in range(count + 1):
        point = top + (bottom - top) * k / count + normal * .12
        plane = normal if k % 2 else np.cross(down, normal)
        out.append(link(point, down, plane / np.linalg.norm(plane), slots["iron"]))
    # The manacle is a broad iron band facing out of the wall, hanging from the last link.
    center = bottom - np.array([0, .26, 0]) + normal * .12
    out.append(union([cylinder(center - normal * .05, center + normal * .05, .19, slots["iron"])],
                     cuts=[cylinder(center - normal * .1, center + normal * .1, .12, slots["iron"])]))
    return out


# KayKit alcove proportions, measured from KayKit's own alcove walls on a 4-unit wall: an opening
# almost half the wall wide, from just above the plinth to just under the cap, a flattened head,
# deep enough for real shelves; boards 0.17 thick, 0.7 apart, flush with the face.
ALCOVE = {"width": 2.04, "sill": .8, "top": 3.12, "rise": .45, "depth": .4, "bevel": .1, "board": .19, "spacing": .7}
WOOD = ("kaykit_04", .45)  # KayKit's plank swatch, as its wooden floors and shelves use it.
BOARD_T = {"top": .55, "front": .67, "end": .64, "under": .75}


def carved(design, piece, variant, r, mesh, slots):
    """Choose this variant's openings and carve them; returns the wall and what fills them.

    Only straight pieces carry openings. The front face gets one KayKit-style alcove (`alcove.chance`),
    furnished from `alcove.contents` in turn by variant letter, so a family's variants show every kind.
    KayKit's own accent bricks that the opening removes are set into its back. `pipes` run drain pipes
    through both faces. (Breaches are KayKit's own `wall_broken`.)
    """
    if len(PIECES[piece]) != 1:
        return mesh, []
    (ox, _), _, length = PIECES[piece][0]
    x0, x1 = ox + .55, ox + length - .55
    holes, extras = [], []
    alcove = design.get("alcove")
    if alcove and r.random() < alcove.get("chance", 1.0):
        shape = {**ALCOVE, **{k: v for k, v in alcove.items() if k in ALCOVE}}
        shape["width"] = min(shape["width"], length - .7)
        cx = ox + length / 2
        outline = arch(cx, shape["sill"], shape["width"], shape["top"] - shape["sill"], shape["rise"])
        hole = {"side": 1, "outline": outline, "depth": shape["depth"], "bevel": shape["bevel"]}
        bricks = cut_bricks(mesh, outline)
        holes.append(hole)
        contents = alcove.get("contents", ["empty"])
        kind = contents[(ord(variant) - ord("A")) % len(contents)]
        hook = (cx + shape["width"] / 2 + .1, 1.0, cx + shape["width"] / 2 + .9, 2.9) if kind == "keys" else None
        extras += frame_bricks(r, mesh, bricks, outline, ox, ox + length, hook)
        extras += furnish(r, kind, cx, shape, bricks, slots, ox + length)
    # Pipes run through the wall, so they claim both faces.
    for _ in range(r.poisson(design.get("pipes", 0.0) * length / 4)):
        cx = r.uniform(x0 + .25, x1 - .25)
        if any(cx + PIPE + .6 > h["outline"][:, 0].min() and cx - PIPE - .6 < h["outline"][:, 0].max() for h in holes):
            continue
        holes.append({"side": 0, "outline": circle(cx, 1.5, PIPE + .05, sides=14)})
        extras += pipe(r, cx, 1.5, slots)
    return (carve(mesh, holes, slots) if holes else mesh), extras


def cut_bricks(mesh, outline):
    """KayKit's front accent bricks that an opening will remove, as separate pieces to reuse."""
    labels = label_of_faces(mesh)
    out = []
    for label, (lo, hi) in accents(mesh, labels).items():
        if hi[2] > 0 and np.all(lo[:2] < outline.max(0) + .04) and np.all(hi[:2] > outline.min(0) - .04):
            keep = labels == label
            used, faces = np.unique(mesh.faces[keep], return_inverse=True)
            out.append(capped(kit.Piece(mesh.verts[used], faces.reshape(-1, 3), mesh.slots[keep], normals=mesh.normals[keep],
                                        uv_t=mesh.uv_t[keep])))
    return out


def capped(brick):
    """A KayKit accent brick closed at its back: it was open where it sat on the panel, and a copy
    standing anywhere else must be a solid of its own."""
    p, f = solid_welded(brick.verts, brick.faces)
    uses = {}
    for a, b, c in f:
        for u, v in ((a, b), (b, c), (c, a)):
            uses[(u, v)] = uses.get((u, v), 0) + 1
    edges = {u: v for (u, v) in uses if (v, u) not in uses}
    caps, normals = [], []
    while edges:
        loop = [next(iter(edges))]
        while edges.get(loop[-1]) not in (None, loop[0]):
            loop.append(edges.pop(loop[-1]))
        edges.pop(loop[-1], None)
        if len(loop) < 3:
            continue
        # The open rim runs around the brick's flat back; walking it backwards gives outward faces.
        tris = np.asarray(mf.triangulate([p[loop][::-1, :2]]))
        ring = np.asarray(loop)[::-1]
        for t in tris:
            caps.append(ring[t])
            normals.append(np.tile([0, 0, -1.0], (3, 1)))
    if not caps:
        return brick
    start = len(brick.verts)
    faces = np.vstack([brick.faces, np.asarray(caps) + start])
    return kit.Piece(np.vstack([brick.verts, p]), faces, np.concatenate([brick.slots, np.full(len(caps), brick.slots[0], object)]),
                     normals=np.concatenate([brick.normals, np.asarray(normals)]),
                     uv_t=np.concatenate([brick.uv_t, np.full((len(caps), 3), np.nanmean(brick.uv_t))]))


def moved(brick, center, z_back=None):
    """A copy of a KayKit accent brick centered at (x, y), on the face or set into a recess back."""
    lo, hi = brick.verts.min(0), brick.verts.max(0)
    dz = 0.0 if z_back is None else z_back - FACE_Z
    shift = np.array([center[0] - (lo[0] + hi[0]) / 2, center[1] - (lo[1] + hi[1]) / 2, dz])
    return kit.Piece(brick.verts + shift, brick.faces, brick.slots, normals=brick.normals, uv_t=brick.uv_t)


def frame_bricks(r, mesh, bricks, outline, x_lo, x_hi, keep_clear=None):
    """A few KayKit accent bricks flanking the arch, as KayKit frames its alcoves: copies of its own
    bricks, two or three per side at irregular heights, skipping any spot an original brick (or
    `keep_clear`, an (x0, y0, x1, y1) box for a fixture) already takes."""
    if not bricks:
        return []
    kept = [(lo, hi) for lo, hi in accents(mesh, label_of_faces(mesh)).values()
            if hi[2] > 0 and not (np.all(lo[:2] < outline.max(0) + .04) and np.all(hi[:2] > outline.min(0) - .04))]
    left, right = outline[:, 0].min(), outline[:, 0].max()
    out = []
    for side, edge in ((-1, left), (1, right)):
        heights = np.sort(r.choice([.95, 1.3, 1.85, 2.3, 2.85], r.integers(2, 4), replace=False))
        for k, y in enumerate(heights):
            brick = bricks[r.integers(len(bricks))]
            half = (brick.verts.max(0) - brick.verts.min(0)) / 2
            x = edge + side * (half[0] + .1 + (.12 if k % 2 else 0))
            if x - half[0] < x_lo + .15 or x + half[0] > x_hi - .15:
                continue
            boxes = kept + ([(np.array(keep_clear[:2]), np.array(keep_clear[2:]))] if keep_clear else [])
            if any(lo[0] < x + half[0] + .05 and hi[0] > x - half[0] - .05 and lo[1] < y + half[1] + .05 and hi[1] > y - half[1] - .05
                   for lo, hi in boxes + [(b.verts.min(0), b.verts.max(0)) for b in out]):
                continue
            out.append(moved(brick, (x, y)))
    return out


def board(cx, y, half, z0, z1, thick, slot=WOOD, tilt=0.0):
    """A chamfered shelf board, its top at `y`, spanning x in cx +- half and z0..z1, tipped `tilt` degrees."""
    a = np.radians(tilt)
    run = np.array([np.cos(a), np.sin(a), 0.0])
    middle = np.array([cx, y - thick / 2, (z0 + z1) / 2])
    piece = beam(middle - run * half, middle + run * half, [-np.sin(a), np.cos(a), 0], thick / 2, (z1 - z0) / 2,
                 slot[0], chamfer=.025)
    # KayKit shades its shelf boards by face direction on the plank swatch: light tops, mid fronts and
    # ends, dark undersides (measured on shelf_small, the plain board closest to ours).
    tri = piece.verts[piece.faces]
    n = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)
    t = np.where(n[:, 1] > .5, BOARD_T["top"], np.where(n[:, 1] < -.5, BOARD_T["under"],
                 np.where(np.abs(n[:, 2]) > .5, BOARD_T["front"], BOARD_T["end"])))
    piece.uv_t = np.repeat(t[:, None], 3, axis=1)
    return piece


# Alcove contents: per shelf level (0 sill board, 1, 2), (decor prefab, x from the alcove center, yaw,
# tilt); a list of layouts per kind, chosen by variant. Few things, each different, as KayKit does.
BOOKS = ["book_teal", "book_gray", "book_blue", "book_black", "book_purple"]
LAYOUTS = {
    "books": [
        {0: [("crate", -.5, 8, 0), ("book_purple", .1, 90, 90), ("coins", .55, 20, 0)],
         1: [("books", -.55, 0, 0), ("bottle_green", -.05, 15, 0), ("flask_brown", .5, -10, 0)],
         2: [("book_black", -.6, 90, 90), ("book_teal", -.6, 75, 90), ("bottle_brown", 0, 0, 0), ("books", .45, 0, 0)]},
        {0: [("books", -.5, 0, 0), ("spellbook", .4, 10, 0)],
         1: [("candle_melted", -.55, 0, 0), ("book_open", .15, -8, 0)],
         2: [("flask_brown", -.55, 0, 0), ("books", .0, 0, 0), ("mug", .6, -30, 0)]},
    ],
    "skulls": [
        {0: [("coins", -.45, 0, 0), ("book_open", .35, -10, 0)],
         1: [("candle_melted", -.55, 0, 0), ("skull", .05, -12, 0)],
         2: [("book_gray", .5, 90, 90), ("candle_lit", -.45, 0, 0)]},
    ],
    "candles": [{0: [("candles", -.2, 10, 0), ("candle_lit", .3, 0, 0)]}],
    "keys": [
        {0: [("keyring", .1, 15, 0)],
         1: [("candle_melted", .45, 0, 0)],
         2: [("book_gray", -.45, 90, 90), ("coins", .4, 0, 0)]},
    ],
    "shelves": [{}],
    "empty": [{}],
}
SHELVED = {"books", "skulls", "keys", "shelves"}
# Contents read at KayKit's exaggerated scale: props a little larger than their stored size.
CONTENT_SCALE = {"candles": 1.45, "candle_melted": 1.3, "candle_lit": 1.25}


def furnish(r, kind, cx, shape, bricks, slots, x_hi):
    """Shelves, contents and back bricks for one alcove of `kind`."""
    back = FACE_Z - shape["depth"]
    z0, z1 = back, FACE_Z + .015
    zc = (z0 + z1) / 2
    half = shape["width"] / 2 - shape["bevel"] + .005
    sill, thick, spacing = shape["sill"], shape["board"], shape["spacing"]
    levels = [sill + thick + k * spacing for k in range(3)]
    parts = []
    # Copies of KayKit's own accent bricks set into the back: a loose cluster when the alcove is bare,
    # one or two peeking out between the boards otherwise.
    if bricks:
        spots = ([(-.3, 1.35), (.25, 1.35), (0, 1.68), (-.45, 2.05), (.4, 2.3)] if kind in ("empty", "candles")
                 else [(-.45, levels[0] + .38), (.4, levels[1] + .36)])
        for k, (dx, y) in enumerate(spots):
            parts.append(moved(bricks[(k + r.integers(len(bricks))) % len(bricks)], (cx + dx, y), back))
    if kind in SHELVED:
        parts += [board(cx, y, half, z0, z1, thick) for y in levels]
    def headroom(level, x):
        """Free height above a shelf at x: to the next board's underside, or to the arch ceiling."""
        y = levels[level] if kind in SHELVED else sill
        if kind in SHELVED and level < len(levels) - 1:
            return levels[level + 1] - thick - y - .04
        r_x = shape["width"] / 2 - shape["bevel"]
        spring = shape["top"] - shape["rise"]
        ceiling = spring + shape["rise"] * np.sqrt(max(0.0, 1 - ((x - cx) / r_x) ** 2)) - shape["bevel"]
        return ceiling - y - .04

    def fitted(name, at, yaw, tilt, scale, room):
        """A decor prefab at its intended scale, shrunk only as far as it must to clear `room`."""
        piece = decor.load(name, at=at, yaw=yaw, tilt=tilt, scale=scale)
        height = piece.verts[:, 1].max() - at[1]
        if height > room:
            piece = decor.load(name, at=at, yaw=yaw, tilt=tilt, scale=scale * room / height)
        piece.decor = name
        return piece

    layouts = LAYOUTS.get(kind, [{}])
    layout = layouts[r.integers(len(layouts))]
    fit = half / (ALCOVE["width"] / 2 - ALCOVE["bevel"])  # Layouts are authored for a full wall's alcove.
    for level, items in layout.items():
        y = levels[level] if kind in SHELVED else sill
        for name, dx, yaw, tilt in items:
            if name == "books":
                # A run of two or three standing books in different covers, the last one leaning.
                picks = r.choice(BOOKS, r.integers(2, 4), replace=False)
                x = cx + dx * fit - .15
                for i, book in enumerate(picks):
                    lean = 12 if i == len(picks) - 1 and r.random() < .5 else 0
                    parts.append(fitted(book, (x, y, zc), r.uniform(-4, 4), -lean, 1.0, headroom(level, x)))
                    x += decor.size(book)[0] + .01
                continue
            x = cx + dx * fit
            parts.append(fitted(name, (x, y, zc), yaw, tilt, CONTENT_SCALE.get(name, 1.1), headroom(level, x)))
    if kind == "keys" and x_hi - (cx + half) > .9:
        # A ring of keys hangs on a hook in the face beside the alcove.
        parts.append(decor.load("keyring_hanging", at=(cx + half + .5, 1.75, FACE_Z + .16)))
        parts[-1].decor = "keyring_hanging"
    return parts


def dressed(design, piece, variant, family):
    """A KayKit wall with its realm dressing: a furnished alcove or pipe bores, then fixtures and growth.

    Growth is deliberately chunky, like the rest of the KayKit set: a few large cushions read at
    the game camera, a scatter of small chips does not.
    """
    mesh, _ = kaykit.load(design.get("source", {}).get(piece, piece))
    r = rng(family, variant, piece, "overgrowth")
    # Recess walls take KayKit's own wall swatch, a little down its gradient so the opening reads deep.
    slots = {"wall": (PANEL_SLOT, .62), "back": (PANEL_SLOT, .78), "pipe": "pipe", "trickle": "slime", "iron": "iron",
             **design.get("slots", {})}
    mesh, extras = carved(design, piece, variant, r, mesh, slots)
    moss = list(design.get("moss_tones", ["moss", "moss_dark", "moss_light"]))
    grass = list(design.get("grass_tones", ["grass", "grass_dark"]))
    growth = []

    depth = design.get("thickness", [.05, .1])

    def cushion(segment, side, cs, cy, count, spread, size, y_range=(.55, 3.25)):
        s = np.clip(cs + r.normal(0, spread[0], count), lo, hi)
        y = np.clip(cy + r.normal(0, spread[1], count), *y_range)
        for point, normal in zip(*surfaces(segment, side, s, y, mesh)):
            growth.append(blob(r, point, normal, r.uniform(*size), r.uniform(*depth), moss[r.integers(len(moss))]))

    for segment in PIECES[piece]:
        length = segment[2]
        lo, hi = END_MARGIN, length - END_MARGIN
        if hi <= lo:
            continue
        scale = length / 4
        (ox, oz), (ux, uz), _ = segment
        for side in (1, -1):
            outward = np.array([uz, 0.0, -ux]) * side
            # Damp patches on the face, mostly toward the foot.
            for _ in range(r.poisson(design.get("patches", 1.5) * scale)):
                cushion(segment, side, r.uniform(lo + .3, hi - .3), r.choice([r.uniform(.6, 1.3), r.uniform(.6, 2.6)]),
                        r.integers(8, 16), (.32, .26), (.1, .22))
            # Moss on the cap, spilling over the edge and creeping down the face.
            for _ in range(r.poisson(design.get("cap_moss", 1.0) * scale)):
                cs = r.uniform(lo + .35, hi - .35)
                spread = np.column_stack([cs + r.normal(0, .3, 8), r.uniform(.05, .32, 8)])
                top = np.array([[ox + ux * a + outward[0] * b, oz + uz * a + outward[2] * b] for a, b in spread])
                for point, normal in zip(*tops(top[(spread[:, 0] > lo) & (spread[:, 0] < hi)], mesh)):
                    if point[1] > 3.9:
                        growth.append(blob(r, point, normal, r.uniform(.12, .24), r.uniform(.05, .09), moss[r.integers(len(moss))]))
                cushion(segment, side, cs, 3.72, r.integers(5, 9), (.28, .1), (.1, .2), (3.55, 3.85))
                cushion(segment, side, cs, 3.1, r.integers(3, 8), (.25, .25), (.08, .16), (2.4, 3.4))
            # Leafy vines hanging from under the cap.
            for _ in range(r.poisson(design.get("vines", 1.0) * scale)):
                points, normals = surfaces(segment, side, np.array([r.uniform(lo + .1, hi - .1)]), np.array([3.3]), mesh)
                for point, normal in zip(points, normals):
                    vine, _ = strand(r, point, normal, r.uniform(.6, 2.2), r.uniform(.03, .05), moss[1])
                    growth.append(vine)
                    path = point - np.outer(np.arange(.15, 2.0, .22), [0, 1.0, 0])
                    leaves_s = np.clip((point - [ox, 0, oz]) @ [ux, 0, uz] + r.normal(0, .08, len(path)), lo, hi)
                    count = r.integers(2, len(path))
                    for leaf, n in zip(*surfaces(segment, side, leaves_s[:count], path[:count, 1], mesh)):
                        growth.append(blob(r, leaf, n, r.uniform(.05, .09), .03, moss[r.integers(len(moss))], sides=5))
            # Roots hang from the cap, thicker than vines and forking.
            for _ in range(r.poisson(design.get("roots", 0.0) * scale)):
                for point, normal in zip(*surfaces(segment, side, np.array([r.uniform(lo + .1, hi - .1)]), np.array([3.3]), mesh)):
                    root, _ = strand(r, point, normal, r.uniform(1.0, 2.6), r.uniform(.035, .06), "root")
                    fork, _ = strand(r, point - [0, r.uniform(.3, .8), 0], normal, r.uniform(.4, 1.0), .03, "root")
                    growth += [root, fork]
            # Ferns: broad fronds fanning out at the foot.
            for _ in range(r.poisson(design.get("ferns", 0.0) * scale)):
                s = r.uniform(lo + .2, hi - .2)
                count = r.integers(5, 9)
                for point in surfaces(segment, side, s + r.normal(0, .08, count), np.full(count, .36), mesh)[0]:
                    room = HALF_THICK + .06 - (point - [ox, 0, oz]) @ outward
                    growth.append(blade(r, point, outward, r.uniform(.32, .6), ["fern", "fern_dark"][r.integers(2)], room,
                                        width=(.04, .06)))
            # Manacles bolted to the face in pairs, an arm's width apart, hanging at one height;
            # pairs keep a body's width between them.
            pairs = []
            for _ in range(r.poisson(design.get("chains", 0.0) * scale)):
                s = r.uniform(lo + .2, hi - .75)
                if any(abs(s - other) < 1.1 for other in pairs):
                    continue
                pairs.append(s)
                y, drop = r.uniform(2.6, 3.0), r.uniform(1.0, 1.4)
                for anchor in surfaces(segment, side, np.array([s, s + r.uniform(.5, .6)]), np.full(2, y), mesh)[0]:
                    growth += chain(anchor, anchor - [0, drop, 0], outward, slots)
            # Grass tufts and moss at the foot, rooted on the plinth.
            for _ in range(r.poisson(design.get("grass", 2.0) * scale)):
                s = r.uniform(lo + .15, hi - .15)
                count = r.integers(8, 15)
                points, _ = surfaces(segment, side, s + r.normal(0, .14, count), np.full(count, .36), mesh)
                for point in points:
                    room = HALF_THICK + .06 - (point - [ox, 0, oz]) @ outward
                    growth.append(blade(r, point, outward, r.uniform(.18, .45), grass[r.integers(len(grass))], room))
                cushion(segment, side, s, .45, r.integers(2, 5), (.2, .05), (.08, .14), (.35, .6))
            # Slime: a goo line under the cap, glossy runs down the face ending in drops, pools at the foot.
            for _ in range(r.poisson(design.get("slime", 0.0) * scale)):
                s0 = r.uniform(lo, hi - .5)
                s1 = min(hi, s0 + r.uniform(.5, 1.6))
                radius = r.uniform(.06, .09)
                line = goo(r, segment, side, s0, s1, mesh, radius, "slime")
                if line is not None:
                    growth.append(line)
                for s in r.uniform(s0 + .1, s1 - .1, r.integers(1, 4)):
                    for point, normal in zip(*surfaces(segment, side, np.array([s]), np.array([3.18]), mesh)):
                        drip = r.uniform(.05, .09)
                        run, end = strand(r, point, normal, r.uniform(.4, 2.4), drip, "slime")
                        growth += [run, blob(r, end, normal, drip * 1.5, drip * 1.2, "slime", sides=6)]
            for _ in range(r.poisson(design.get("pools", 0.0) * scale)):
                points, normals = surfaces(segment, side, np.array([r.uniform(lo + .4, hi - .4)]), np.array([.4]), mesh)
                for point, normal in zip(points, normals):
                    growth.append(blob(r, point, normal, r.uniform(.2, .3), .04, "slime_dark", sides=9, stretch=(1.8, .45)))
    piece = kit.Piece.merge(mesh, *growth, *extras)
    # Placed KayKit decor keeps its authored topology; validation allows exactly those exceptions.
    piece.authored = {}
    for part in extras:
        for key, count in (decor.authored(part.decor) if hasattr(part, "decor") else {}).items():
            piece.authored[key] = piece.authored.get(key, 0) + count
    return piece


def source_colors(design, pieces):
    """The KayKit swatch colors a family's pieces use, for recipes that keep the original stone."""
    colors = {}
    for piece in pieces:
        colors.update(kaykit.load(design.get("source", {}).get(piece, piece))[1])
    return colors


STYLES = {"dressed": dressed}
