"""
The building blocks every accessory is made of, in the KayKit vocabulary: chunky low-poly forms with chamfered edges
and one flat color per material slot.

- `Piece`: a mesh with a material slot per face (`SLOTS`), colored from a palette later; merge, mirror, place.
- `hull`: any convex form from its corner points (chamfered boxes, studs, spikes, gems, buckle plates).
- `host_band`: a closed profile swept around a host's cross-sections (belts, crown bands, cuffs, collars).
- `sweep`: a closed profile swept along a path (horns, straps, rims, chain links), capped when the path is open.
- `cap`: a curved shell with thickness and a bevelled rim (pauldrons, helmets).
- `Frame` / `band_frame`: local axes on a host's surface for placing parts (buckles, studs, spikes, gems).

Hosts expose `section(y) -> (cx, a, cz, b)` (an ellipse-ish ring of half widths `a` across and `b` deep) and `p`
(superellipse exponent), like the monster build's heads, torsos and limbs (`monster_body.RegionHost`).
"""

import numpy as np
from scipy.spatial import ConvexHull

# Material slots, colored from the monster's palette (or `DEMO_COLORS` in the catalog).
SLOTS = ("metal", "dark", "gold", "leather", "cloth", "gem", "bone", "fur", "trim")
DEMO_COLORS = {
    "metal": (176, 186, 199), "dark": (74, 76, 86), "gold": (232, 182, 64), "leather": (122, 78, 52),
    "cloth": (150, 40, 52), "gem": (214, 48, 74), "bone": (226, 216, 190), "fur": (110, 86, 70), "trim": (240, 236, 226),
}


class Piece:
    """A mesh with one material slot per face."""

    def __init__(self, verts, faces, slots):
        self.verts = np.asarray(verts, float).reshape(-1, 3)
        self.faces = np.asarray(faces, np.int64).reshape(-1, 3)
        self.slots = (np.full(len(self.faces), slots, object) if isinstance(slots, str)
                      else np.asarray(slots, object))

    @staticmethod
    def merge(*pieces):
        pieces = [p for p in pieces if p is not None and len(p.faces)]
        if not pieces:
            return Piece(np.zeros((0, 3)), np.zeros((0, 3)), np.zeros(0, object))
        offsets = np.cumsum([0] + [len(p.verts) for p in pieces[:-1]])
        return Piece(np.vstack([p.verts for p in pieces]), np.vstack([p.faces + o for p, o in zip(pieces, offsets)]),
                     np.concatenate([p.slots for p in pieces]))

    def moved(self, frame):
        """This piece, built in local axes (x along, y up, z out), placed in `frame`."""
        return Piece(frame.to_world(self.verts), self.faces, self.slots)

    def mirrored(self):
        """Mirrored across x = 0 (the character's other side), winding flipped to stay outward."""
        return Piece(self.verts * [-1.0, 1.0, 1.0], self.faces[:, ::-1], self.slots)

    def recolored(self, mapping):
        return Piece(self.verts, self.faces, np.array([mapping.get(s, s) for s in self.slots], object))


class Frame:
    """Local axes at a point: `x` along the surface, `y` up along it, `z` out of it (all unit, orthogonal)."""

    def __init__(self, origin, x, y, z):
        self.origin = np.asarray(origin, float)
        self.R = np.stack([x, y, z], 1).astype(float)
        if np.linalg.det(self.R) < 0:  # a mirroring frame would turn every piece placed with it inside out
            raise ValueError("left-handed Frame: use Piece.mirrored() to mirror a piece")

    @staticmethod
    def make(origin, out, up=(0.0, 1.0, 0.0)):
        z = np.asarray(out, float) / np.linalg.norm(out)
        x = np.cross(np.asarray(up, float), z)
        x = x / np.linalg.norm(x) if np.linalg.norm(x) > 1e-9 else np.array([1.0, 0.0, 0.0])
        return Frame(origin, x, np.cross(z, x), z)

    def to_world(self, P):
        return self.origin + np.asarray(P, float) @ self.R.T

    def rotated(self, deg, axis="z"):
        """The frame turned by `deg` degrees about one of its own axes."""
        i = "xyz".index(axis)
        a = np.radians(deg)
        c, s = np.cos(a), np.sin(a)
        j, k = [n for n in range(3) if n != i]
        R = self.R.copy()
        R[:, j], R[:, k] = c * self.R[:, j] + s * self.R[:, k], -s * self.R[:, j] + c * self.R[:, k]
        return Frame(self.origin, *R.T)


def rotation(axis, deg):
    """The 3x3 rotation by `deg` degrees about `axis` (right-handed)."""
    k = np.asarray(axis, float) / np.linalg.norm(axis)
    K = np.array([[0.0, -k[2], k[1]], [k[2], 0.0, -k[0]], [-k[1], k[0], 0.0]])
    a = np.radians(deg)
    return np.eye(3) + np.sin(a) * K + (1 - np.cos(a)) * K @ K


def _outward(verts, faces):
    """`faces` wound so their normals point away from the mesh's centre (for convex or star-shaped forms)."""
    tri = verts[faces]
    n = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    flip = ((tri.mean(1) - verts.mean(0)) * n).sum(1) < 0
    faces = faces.copy()
    faces[flip] = faces[flip][:, ::-1]
    return faces


def hull(points, slot):
    """The convex form spanned by `points` (local or world coordinates)."""
    P = np.asarray(points, float)
    return Piece(P, _outward(P, ConvexHull(P).simplices), slot)


def bevel_box(size, bevel, slot, center=(0.0, 0.0, 0.0)):
    """A box of full sizes `size` = (x, y, z) with every edge chamfered by `bevel` (KayKit's soft blocks)."""
    h = np.asarray(size, float) / 2
    b = min(bevel, *(0.49 * h * 2))
    pts = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                s = np.array([sx, sy, sz])
                for axis in range(3):
                    p = s * h
                    for other in range(3):
                        if other != axis:
                            p[other] -= s[other] * b
                    pts.append(p)
    return hull(np.asarray(pts) + center, slot)


def prism(polygon, depth, slot, bevel=0.0):
    """A flat convex `polygon` (n, 2: x, y) extruded `depth` along +z from z = 0, front edges chamfered by `bevel`."""
    poly = np.asarray(polygon, float)
    c = poly.mean(0)
    back = np.column_stack([poly, np.zeros(len(poly))])
    if bevel <= 0:
        return hull(np.vstack([back, back + [0.0, 0.0, depth]]), slot)
    inner = c + (poly - c) * (1 - bevel / max(np.linalg.norm(poly - c, axis=1).min(), 1e-9))
    mid = np.column_stack([poly, np.full(len(poly), depth - bevel)])
    front = np.column_stack([inner, np.full(len(poly), depth)])
    return hull(np.vstack([back, mid, front]), slot)


def triangulate(polygon):
    """Ear clipping: triangles (index triples) covering a simple polygon (n, 2) of either winding, concave or not."""
    P = np.asarray(polygon, float)
    idx = list(range(len(P)))
    area = 0.5 * np.sum(P[:, 0] * np.roll(P[:, 1], -1) - np.roll(P[:, 0], -1) * P[:, 1])
    if area < 0:
        idx.reverse()
    cross = lambda o, a, b: (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])  # noqa: E731
    tris = []
    guard = 0
    while len(idx) > 3 and guard < 10 * len(P):
        guard += 1
        for k in range(len(idx)):
            i0, i1, i2 = idx[k - 1], idx[k], idx[(k + 1) % len(idx)]
            a, b, c = P[i0], P[i1], P[i2]
            if cross(a, b, c) <= 1e-12:
                continue  # a reflex corner is no ear
            if any(cross(a, b, P[j]) >= 0 and cross(b, c, P[j]) >= 0 and cross(c, a, P[j]) >= 0
                   for j in idx if j not in (i0, i1, i2)):
                continue
            tris.append((i0, i1, i2))
            idx.pop(k)
            break
    tris.append(tuple(idx))
    return np.asarray(tris, np.int64)


def extrude(polygon, depth, slot):
    """A flat slab of any simple outline `polygon` (n, 2: x, y), concave or not, `depth` thick along +z."""
    P = np.asarray(polygon, float)
    if np.sum(P[:, 0] * np.roll(P[:, 1], -1) - np.roll(P[:, 0], -1) * P[:, 1]) < 0:
        P = P[::-1]  # counter-clockwise, so every wall below faces out
    n = len(P)
    T = triangulate(P)
    verts = np.vstack([np.column_stack([P, np.zeros(n)]), np.column_stack([P, np.full(n, depth)])])
    faces = [T[:, ::-1], T + n]
    for i in range(n):
        j = (i + 1) % n
        faces.append(np.array([[i, j, j + n], [i, j + n, i + n]]))
    return Piece(verts, np.vstack(faces), slot)


def _clip_poly(poly, nx, ny, c):
    """A convex polygon of 3D points cut to the half plane `nx * x + ny * y <= c` of its x, y projection (new
    corners on its own plane)."""
    out = []
    for i in range(len(poly)):
        a, b = poly[i], poly[(i + 1) % len(poly)]
        da, db = nx * a[0] + ny * a[1] - c, nx * b[0] + ny * b[1] - c
        if da <= 0:
            out.append(a)
        if da * db < 0:
            out.append(a + (b - a) * (da / (da - db)))
    return out


def paint_cut(piece, polygons, slot, paintable, min_facing=0.05):
    """
    `piece` with flat paint cut into its surface: every face in a `paintable` slot that faces the front (+z) is
    split along the outlines of `polygons` (convex, in front view x, y) and the parts inside them take `slot`. The
    paint is the surface itself, recolored: nothing stands off it.
    """
    if not len(piece.faces):
        return piece
    polys = [np.asarray(q, float) for q in polygons]
    polys = [q[::-1] if np.sum(q[:, 0] * np.roll(q[:, 1], -1) - np.roll(q[:, 0], -1) * q[:, 1]) < 0 else q
             for q in polys]
    boxes = [(q.min(0), q.max(0)) for q in polys]
    tri = piece.verts[piece.faces]
    n = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    facing = n[:, 2] / np.maximum(np.linalg.norm(n, axis=1), 1e-12) > min_facing
    candidates = facing & np.isin(piece.slots.astype(str), list(paintable))
    keep = ~candidates
    out_tri, out_slots = [tri[keep]], [piece.slots[keep]]
    for f in np.flatnonzero(candidates):
        parts = [([p for p in tri[f]], piece.slots[f])]
        lo, hi = tri[f, :, :2].min(0), tri[f, :, :2].max(0)
        for q, (qlo, qhi) in zip(polys, boxes):
            if (hi < qlo).any() or (lo > qhi).any():
                continue
            new = []
            for poly, sl in parts:
                if sl == slot:
                    new.append((poly, sl))
                    continue
                rest = poly
                for i in range(len(q)):  # peel off what lies outside each edge; what stays is inside
                    a, b = q[i], q[(i + 1) % len(q)]
                    nx, ny = b[1] - a[1], a[0] - b[0]
                    c = nx * a[0] + ny * a[1]
                    outside = _clip_poly(rest, -nx, -ny, -c)
                    if len(outside) >= 3:
                        new.append((outside, sl))
                    rest = _clip_poly(rest, nx, ny, c)
                    if len(rest) < 3:
                        break
                if len(rest) >= 3:
                    new.append((rest, slot))
            parts = new
        for poly, sl in parts:
            out_tri += [np.array([[poly[0], poly[k], poly[k + 1]] for k in range(1, len(poly) - 1)])]
            out_slots += [np.full(len(poly) - 2, sl, object)]
    T = np.vstack([t.reshape(-1, 3, 3) for t in out_tri if len(t)])
    return Piece(T.reshape(-1, 3), np.arange(3 * len(T)).reshape(-1, 3), np.concatenate(out_slots))


def _inside(P, outline):
    """Whether each point (n, 2) lies inside a simple polygon `outline` (even-odd rule; concave allowed)."""
    x, y = P[:, 0], P[:, 1]
    inside = np.zeros(len(P), bool)
    Q = np.asarray(outline, float)
    for i in range(len(Q)):
        a, b = Q[i], Q[i - 1]
        crosses = (a[1] > y) != (b[1] > y)
        at = a[0] + (b[0] - a[0]) * (y - a[1]) / np.where(np.abs(b[1] - a[1]) < 1e-30, 1e-30, b[1] - a[1])
        inside ^= crosses & (x < at)
    return inside


def _outline_distance(P, outline):
    """Each point's distance (n,) to the edges of a polygon."""
    Q = np.asarray(outline, float)
    best = np.full(len(P), np.inf)
    for i in range(len(Q)):
        a, b = Q[i - 1], Q[i]
        ab = b - a
        t = np.clip(((P - a) @ ab) / max(ab @ ab, 1e-30), 0.0, 1.0)
        best = np.minimum(best, np.linalg.norm(P - (a + t[:, None] * ab), axis=1))
    return best


def carve(piece, polygons, outline, slot, depth, center, paintable, min_facing=0.05):
    """
    `piece` with a real recess: its front faces inside `polygons` (convex, in front view; together they make the
    possibly concave `outline`) cut along them and recolored `slot` (`paint_cut`), and every point strictly inside
    `outline` sunk `depth` toward `center`: a mouth, a socket or a nostril is a dent in the surface, its rim the
    cut. Nothing is laid on top.
    """
    cut = paint_cut(piece, polygons, slot, paintable, min_facing)
    V = cut.verts.copy()
    ours = np.zeros(len(V), bool)
    ours[cut.faces[cut.slots == slot].ravel()] = True
    move = ours & _inside(V[:, :2], outline) & (_outline_distance(V[:, :2], outline) > 1e-7)
    d = V[move] - np.asarray(center, float)
    V[move] -= depth * d / np.maximum(np.linalg.norm(d, axis=1, keepdims=True), 1e-12)
    return Piece(V, cut.faces, cut.slots)


def spike(base, tip, radius, slot, sides=4, twist=45.0):
    """A pyramid from a `sides`-gon of `radius` at `base` to `tip` (KayKit spikes are 4-sided)."""
    base, tip = np.asarray(base, float), np.asarray(tip, float)
    f = Frame.make(base, tip - base)
    th = np.radians(twist) + 2 * np.pi * np.arange(sides) / sides
    ring = f.to_world(np.column_stack([radius * np.cos(th), radius * np.sin(th), np.zeros(sides)]))
    return hull(np.vstack([ring, tip]), slot)


def stud(radius, height, slot, sides=6):
    """A flat-topped stud on the local xy plane, rising along +z (a rivet or a round boss)."""
    th = 2 * np.pi * np.arange(sides) / sides
    base = np.column_stack([radius * np.cos(th), radius * np.sin(th), np.zeros(sides)])
    top = np.column_stack([0.6 * radius * np.cos(th), 0.6 * radius * np.sin(th), np.full(sides, height)])
    return hull(np.vstack([base, top]), slot)


def gem(size, slot="gem", cut="oval", depth=None):
    """A cut gem lying on the local xy plane, its table along +z: `oval` cabochon-like facets or a `diamond`."""
    d = depth or 0.6 * size
    if cut == "diamond":
        pts = [(0, size, 0), (0.7 * size, 0, 0), (0, -size, 0), (-0.7 * size, 0, 0), (0, 0, d)]
        pts += [(0.35 * size, 0.45 * size, 0.7 * d), (-0.35 * size, 0.45 * size, 0.7 * d),
                (0.35 * size, -0.45 * size, 0.7 * d), (-0.35 * size, -0.45 * size, 0.7 * d)]
        return hull(pts, slot)
    th = 2 * np.pi * np.arange(8) / 8
    girdle = np.column_stack([size * np.cos(th), 0.75 * size * np.sin(th), np.full(8, 0.25 * d)])
    table = np.column_stack([0.55 * size * np.cos(th), 0.42 * size * np.sin(th), np.full(8, d)])
    base = np.column_stack([0.85 * size * np.cos(th), 0.65 * size * np.sin(th), np.zeros(8)])
    return hull(np.vstack([girdle, table, base]), slot)


def ring_points(host, y, out, sides, theta=None):
    """Points around a host's cross-section at height `y`, `out` outside its surface (`y` and `out` per point or
    one value: a ring at varying heights winds round the host aslant)."""
    th = 2 * np.pi * np.arange(sides) / sides if theta is None else np.asarray(theta, float)
    if hasattr(host, "outline"):  # a fitted host knows its own (taut) outline
        return host.outline(y, th, out)
    ys = np.broadcast_to(np.asarray(y, float), th.shape)
    cx, a, cz, b = (np.broadcast_to(np.asarray(v, float).ravel(), th.shape) if np.ndim(y)
                    else float(np.asarray(v).ravel()[0]) for v in host.section(ys if np.ndim(y) else [y]))
    c, s = np.cos(th), np.sin(th)
    p = getattr(host, "p", 2.0)
    ux, uz = np.sign(c) * np.abs(c) ** (2 / p), np.sign(s) * np.abs(s) ** (2 / p)
    out = np.broadcast_to(np.asarray(out, float), th.shape)
    ra, rb = np.maximum(a + out, 1e-3), np.maximum(b + out, 1e-3)  # an inset ring where the host thins to nothing
    return np.column_stack([cx + ra * ux, ys, cz + rb * uz])  # must not turn through its axis


def _loft(rings, closed_rings=True, closed_profile=False):
    """Quads between consecutive rings (each an (n, 3) loop), wound for rings listed bottom-up around +y."""
    rings = [np.asarray(r, float) for r in rings]
    n = len(rings[0])
    verts = np.vstack(rings)
    faces = []
    m = len(rings) if closed_profile else len(rings) - 1
    for i in range(m):
        a0, b0 = i * n, ((i + 1) % len(rings)) * n
        for j in range(n if closed_rings else n - 1):
            k = (j + 1) % n
            faces += [(a0 + j, a0 + k, b0 + k), (a0 + j, b0 + k, b0 + j)]
    return verts, np.asarray(faces, np.int64)


def host_band(host, y0, y1, thickness, slot, bevel=0.35, sides=16, inset=0.01, profile=None, theta=None, tilt=0.0,
              phase=0.0, swell=0.0, swell_at=np.pi / 2):
    """
    A band around a host from `y0` up to `y1`, standing `thickness` off its surface, with chamfered edges (`bevel`
    as a share of the thickness) and a lip `inset` below the surface so no gap shows. `profile` overrides the cross
    section: a closed list of `(t, out)` with `t` from 0 (`y0`) to 1 (`y1`). The band follows the host's taper, so it
    sits flush on a waist or a skull. `tilt` winds it aslant: `tilt` higher at the angle `phase` (radians, 0 = +x)
    and as much lower opposite. `swell` makes it that much taller (half up, half down) at the angle `swell_at`
    (the front by default), easing back to its own height at the sides: a belt broad in front.
    """
    if profile is None:
        c = bevel * thickness
        dt = min(c / max(y1 - y0, 1e-9), 0.45)
        profile = [(0.0, -inset), (0.0, thickness - c), (dt, thickness), (1 - dt, thickness), (1.0, thickness - c),
                   (1.0, -inset)]
    th = 2 * np.pi * np.arange(sides) / sides if theta is None else np.asarray(theta, float)
    rise = tilt * np.cos(th - phase) if tilt else 0.0
    grow = swell * np.clip(np.cos(th - swell_at), 0.0, None) ** 2 if swell else 0.0
    rings = [ring_points(host, y0 - grow / 2 + t * (y1 - y0 + grow) + rise, out, sides, theta) for t, out in profile]
    verts, faces = _loft(rings, closed_rings=theta is None, closed_profile=True)
    if theta is not None:  # an arc (a breastplate): cap its two ends
        n = len(rings[0])
        verts, faces = _cap_loops(verts, faces, [[k * n for k in range(len(rings))],
                                                 [k * n + n - 1 for k in range(len(rings))]])
    return Piece(verts, _orient_closed(verts, faces), slot)


def _cap_loops(verts, faces, loops, centers=None):
    """Closes each loop of vertex indices with a fan around its centroid (or the matching point of `centers`), wound
    to match the faces beside it."""
    verts, extra = np.asarray(verts, float), []
    directed = {(int(a), int(b)) for f in faces for a, b in ((f[0], f[1]), (f[1], f[2]), (f[2], f[0]))}
    for i, loop in enumerate(loops):
        if (int(loop[0]), int(loop[1])) in directed:  # a cap runs each border edge against its neighbour
            loop = loop[::-1]
        c = len(verts) + len(extra)
        extra.append(verts[loop].mean(0) if centers is None else np.asarray(centers[i], float))
        faces = np.vstack([faces, [(c, loop[j], loop[(j + 1) % len(loop)]) for j in range(len(loop))]])
    return np.vstack([verts, extra]), faces


def _orient_closed(verts, faces):
    """A closed mesh's faces, all flipped if its signed volume says they point inward."""
    t = verts[faces]
    if np.einsum("ij,ij->i", t[:, 0], np.cross(t[:, 1], t[:, 2])).sum() < 0:
        faces = faces[:, ::-1]
    return faces


def sweep(path, profile, slot, up=(0.0, 1.0, 0.0), closed=False, scale=None):
    """
    A closed `profile` (m, 2: across, out) swept along `path` (k, 3), its `out` axis kept as close to `up` as the
    path allows (parallel transport), scaled per path point by `scale`. Open paths are capped at both ends.
    """
    P = np.asarray(path, float)
    T = np.gradient(P, axis=0)
    if closed:
        T = np.roll(P, -1, 0) - np.roll(P, 1, 0)
    T /= np.maximum(np.linalg.norm(T, axis=1, keepdims=True), 1e-12)
    n = np.asarray(up, float) - (np.asarray(up, float) @ T[0]) * T[0]
    if np.linalg.norm(n) < 1e-6:
        n = np.cross(T[0], [1.0, 0.0, 0.0])
    rings = []
    prof = np.asarray(profile, float)
    for i in range(len(P)):
        n = n - (n @ T[i]) * T[i]
        n /= np.linalg.norm(n)
        w = np.cross(T[i], n)
        k = 1.0 if scale is None else float(scale[i])
        rings.append(P[i] + k * (prof[:, 0:1] * w + prof[:, 1:2] * n))
    verts, faces = _loft(rings, closed_rings=True, closed_profile=closed)
    if not closed:
        m = len(prof)
        verts, faces = _cap_loops(verts, faces, [list(range(m)), list(range((len(P) - 1) * m, len(P) * m))])
    return Piece(verts, _orient_closed(verts, faces), slot)


def patch_shell(outer, inner, slot):
    """
    A closed slab between two grids of points (rows, cols, 3), `outer` facing out and `inner` below it: a patch of
    skin grown out of a surface, or a strip of paint. Each column's profile runs down `outer` and back up `inner`;
    the profiles are lofted across the columns and the first and last ones capped.
    """
    outer, inner = np.asarray(outer, float), np.asarray(inner, float)
    R, C = outer.shape[:2]
    rings = [np.vstack([outer[:, j], inner[::-1, j]]) for j in range(C)]
    verts, faces = _loft(rings, closed_rings=True, closed_profile=False)
    n = 2 * R
    verts, faces = _cap_loops(verts, faces, [list(range(n)), list(range((C - 1) * n, C * n))])
    return Piece(verts, _orient_closed(verts, faces), slot)


def ribbon(points, widths, thickness, slot, embed=0.0, across=2):
    """A flat strip along a 2D polyline (x, y) with a width per point, `thickness` over z = 0 and `embed` under it:
    a brush stroke (eyeliner, a brow, war paint) to bend onto a surface."""
    P = np.asarray(points, float)
    T = np.gradient(P, axis=0)
    T /= np.maximum(np.linalg.norm(T, axis=1, keepdims=True), 1e-12)
    N = np.column_stack([-T[:, 1], T[:, 0]])
    s = np.linspace(-0.5, 0.5, across + 1)[:, None, None]
    flat = P[None] + s * np.asarray(widths, float)[None, :, None] * N[None]  # (across + 1, len(points), 2)
    top = np.concatenate([flat, np.full(flat.shape[:2] + (1,), thickness)], 2)
    bottom = np.concatenate([flat, np.full(flat.shape[:2] + (1,), -embed)], 2)
    return patch_shell(top, bottom, slot)


def resample(points, step):
    """A polyline resampled at about `step` spacing (its corners kept), so a stroke bends with the surface."""
    P = np.asarray(points, float)
    out = [P[0]]
    for a, b in zip(P[:-1], P[1:]):
        n = max(1, int(np.ceil(np.linalg.norm(b - a) / step)))
        out += [a + (b - a) * k / n for k in range(1, n + 1)]
    return np.asarray(out)


def ray_distance(origins, dirs, verts, faces, chunk=128, faces_hit=False, reach=None):
    """Distance from each origin (or one for all) along its unit direction to the nearest face of a mesh it crosses
    (NaN if none), and with `faces_hit` that face's index (-1 if none): Moller-Trumbore, a chunk of rays against
    every triangle at once."""
    if reach is not None:  # short rays: test each spatially sorted chunk only against triangles near it
        O_all = np.broadcast_to(np.asarray(origins, float), np.shape(dirs))
        T_all = np.asarray(verts, float)[np.asarray(faces)]
        lo_t, hi_t = T_all.min(1), T_all.max(1)
        order = np.lexsort(np.round(O_all / max(reach, 1e-6)).T[::-1])
        out = np.full(len(order), np.nan)
        which = np.full(len(order), -1)
        for k in range(0, len(order), chunk):
            idx = order[k:k + chunk]
            lo, hi = O_all[idx].min(0) - reach, O_all[idx].max(0) + reach
            near = np.flatnonzero(((hi_t >= lo) & (lo_t <= hi)).all(1))
            if not len(near):
                continue
            d, f = ray_distance(O_all[idx], np.asarray(dirs, float)[idx], verts, np.asarray(faces)[near], chunk,
                                faces_hit=True)
            out[idx] = d
            which[idx] = np.where(f >= 0, near[np.maximum(f, 0)], -1)
        return (out, which) if faces_hit else out
    T = np.asarray(verts, float)[np.asarray(faces)]
    e1, e2 = T[:, 1] - T[:, 0], T[:, 2] - T[:, 0]
    dirs = np.asarray(dirs, float)
    O = np.broadcast_to(np.asarray(origins, float), dirs.shape)
    out = np.full(len(dirs), np.nan)
    which = np.full(len(dirs), -1)
    for i in range(0, len(dirs), chunk):
        D = dirs[i:i + chunk]
        s = O[i:i + chunk, None, :] - T[None, :, 0]
        q = np.cross(s, e1[None])
        p = np.cross(D[:, None, :], e2[None])
        det = (p * e1[None]).sum(2)
        inv = 1.0 / np.where(np.abs(det) < 1e-12, np.nan, det)
        u = (s * p).sum(2) * inv
        v = (D[:, None, :] * q).sum(2) * inv
        t = (e2[None] * q).sum(2) * inv
        eps = 1e-9  # a ray through a shared edge or corner counts on both sides, never on neither
        t = np.where((u >= -eps) & (v >= -eps) & (u + v <= 1 + eps) & (t > 1e-6), t, np.inf)
        best = t.min(1)
        out[i:i + chunk] = np.where(np.isfinite(best), best, np.nan)
        which[i:i + chunk] = np.where(np.isfinite(best), t.argmin(1), -1)
    return (out, which) if faces_hit else out


def rect_profile(width, height, bevel=0.0):
    """A `width` x `height` rectangle around the origin (across, out), corners chamfered by `bevel`."""
    w, h = width / 2, height / 2
    if bevel <= 0:
        return [(-w, -h), (w, -h), (w, h), (-w, h)]
    b = min(bevel, 0.45 * width, 0.45 * height)
    return [(-w + b, -h), (w - b, -h), (w, -h + b), (w, h - b), (w - b, h), (-w + b, h), (-w, h - b), (-w, -h + b)]


def rounded_box(size, slot, center=(0.0, 0.0, 0.0), roundness=0.5, n=16, rings=9):
    """A box of full `size` with every edge and corner rounded, like a pillow: a superellipsoid, `roundness` 0 a
    hard box and 1 an ellipsoid (KayKit's organic parts: fists, boots, noses)."""
    h = np.asarray(size, float) / 2
    e = max(roundness, 0.05)
    sp = lambda v: np.sign(v) * np.abs(v) ** e  # noqa: E731
    u = 2 * np.pi * np.arange(n) / n
    v = np.linspace(-np.pi / 2, np.pi / 2, rings)[1:-1]
    U, V = np.meshgrid(u, v)
    pts = np.column_stack([(h[0] * sp(np.cos(V)) * sp(np.cos(U))).ravel(), (h[1] * sp(np.sin(V))).ravel(),
                           (h[2] * sp(np.cos(V)) * sp(np.sin(U))).ravel()])
    pts = np.vstack([pts, [0, h[1], 0], [0, -h[1], 0]])
    return hull(pts + np.asarray(center, float), slot)


def lump(c, r, slot, n=10, lat=(-65, -35, 0, 35, 65)):
    """A low-poly ellipsoid of radii `r` around `c`."""
    pts = [np.asarray(c) + np.asarray(r) * [np.cos(a) * np.cos(b), np.sin(b), np.sin(a) * np.cos(b)]
           for a in np.radians(np.arange(n) * 360 / n) for b in np.radians(lat)]
    return hull(pts + [np.asarray(c) + [0, r[1], 0], np.asarray(c) - [0, r[1], 0]], slot)


def ring_profile(across, out, sides=8):
    """An ellipse of half sizes `across` x `out` around the origin as a `sides`-gon (a profile for `sweep`)."""
    t = 2 * np.pi * np.arange(sides) / sides
    return np.column_stack([across * np.cos(t), out * np.sin(t)])


def cap(rx, ry, rz, lat, thickness, slot, sides=12, rings=4, p=2.2, rim_bevel=0.5, start=0.0):
    """
    A shell over the top of an ellipsoid of radii `rx`, `ry`, `rz` (centre at the origin, top at `+ry`), from the top
    down to latitude `lat` (degrees below the equator are negative), `thickness` thick with a chamfered rim: a
    pauldron's dome or a helmet. `start` > 0 leaves a hole at the top (a stacked lame).
    """
    lats = np.radians(np.linspace(90.0 - start, lat, rings + 1))  # from the top: a collapsed ring closes it
    th = 2 * np.pi * np.arange(sides) / sides
    c, s = np.cos(th), np.sin(th)
    ux, uz = np.sign(c) * np.abs(c) ** (2 / p), np.sign(s) * np.abs(s) ** (2 / p)

    def surface(k):
        return [np.column_stack([k * rx * np.cos(L) * ux, np.full(sides, k * ry * np.sin(L)), k * rz * np.cos(L) * uz])
                for L in lats]

    outer = surface(1.0)
    k_in = 1.0 - thickness / max(min(rx, ry, rz), 1e-9)
    inner = surface(k_in)
    rim_out = outer[-1]
    lip = rim_out + (inner[-1] - rim_out) * 0.5 + [0.0, -rim_bevel * thickness, 0.0]
    rings_all = outer + [lip] + inner[::-1]
    verts, faces = weld_collapsed(*_loft(rings_all, closed_rings=True, closed_profile=start > 0))
    return Piece(verts, _orient_closed(verts, faces), slot)


def weld_collapsed(verts, faces, tol=1e-9):
    """Merges coincident vertices (a ring collapsed to a pole) and drops the faces that degenerate."""
    _, first, inverse = np.unique(np.round(np.asarray(verts) / tol).astype(np.int64), axis=0, return_index=True,
                                  return_inverse=True)
    return verts[first], _drop_degenerate(verts[first], inverse.reshape(-1)[faces])


def _drop_degenerate(verts, faces, eps=1e-12):
    faces = faces[(faces[:, 0] != faces[:, 1]) & (faces[:, 1] != faces[:, 2]) & (faces[:, 0] != faces[:, 2])]
    t = verts[faces]
    return faces[np.linalg.norm(np.cross(t[:, 1] - t[:, 0], t[:, 2] - t[:, 0]), axis=1) > eps]


def band_frame(host, y, theta, out=0.0):
    """The `Frame` on a host's surface at height `y` and angle `theta` (radians; 0 = +x, pi/2 = +z, the front),
    lifted `out` off it."""
    eps = 1e-3
    p0, p1, p2 = (ring_points(host, y, out, 1, theta=[t])[0] for t in (theta, theta - eps, theta + eps))
    tangent = p2 - p1
    up = np.array([0.0, 1.0, 0.0])
    z = np.cross(tangent, up)  # outward for counter-clockwise rings seen from above
    cx, _, cz, _ = (float(np.asarray(v).ravel()[0]) for v in host.section([y]))
    if (z * (p0 - [cx, y, cz])).sum() < 0:
        z = -z
    return Frame.make(p0, z, up)


def bezier(p0, p1, p2, n=8):
    """`n` + 1 points along the quadratic Bezier `p0 -> p1 -> p2`."""
    t = np.linspace(0.0, 1.0, n + 1)[:, None]
    p0, p1, p2 = (np.asarray(p, float) for p in (p0, p1, p2))
    return (1 - t) ** 2 * p0 + 2 * (1 - t) * t * p1 + t ** 2 * p2


def spline(points, n=8):
    """`n` + 1 points along the Catmull-Rom curve through `points` (its ends included)."""
    P = np.asarray(points, float)
    P = np.vstack([2 * P[0] - P[1], P, 2 * P[-1] - P[-2]])
    out = []
    for t in np.linspace(0.0, len(P) - 3, n + 1):
        i = min(int(t), len(P) - 4)
        u = t - i
        p0, p1, p2, p3 = P[i:i + 4]
        out.append(0.5 * (2 * p1 + (p2 - p0) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u ** 2
                          + (3 * p1 - p0 - 3 * p2 + p3) * u ** 3))
    return np.asarray(out)


def tube(path, radius, slot, sides=6, taper=0.92):
    """A `sides`-gonal tube along `path`, tapering to a point (`taper` shapes how fast)."""
    th = 2 * np.pi * np.arange(sides) / sides
    profile = np.column_stack([radius * np.cos(th), radius * np.sin(th)])
    t = np.linspace(0.0, 1.0, len(path))
    return sweep(path, profile, slot, scale=np.maximum((1 - t) ** taper, 0.04))


def horn(base, ctrl, tip, radius, slot, segments=8, sides=6, taper=0.92):
    """A tapering horn along the Bezier `base -> ctrl -> tip`, `sides`-gonal (KayKit horns are 5-6 sided)."""
    return tube(bezier(base, ctrl, tip, segments), radius, slot, sides, taper)
