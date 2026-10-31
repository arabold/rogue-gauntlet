"""Grid-tile geometry: clipped beveled blocks, seam-safe ground slabs and seam profile checks.

Every tile family shares one seam contract. Its pattern repeats every `SEAM` world units (the
2x2 small tile), so 4x4 and 2x2 tiles of a family mix freely at one fixed orientation. Cells
wholly inside a tile vary per variant. Cells crossing a tile edge are cut flush there and take
their properties from their periodic class alone, so the neighbor tile continues them exactly.
Polygons are (n, 2) arrays in the floor plane: (x, z) for floors, (along, up) for wall faces.
"""

import hashlib

import manifold3d as mf
import numpy as np

from core import kit
from items.solid import welded

SEAM = 2.0
EPS = 1e-7


def rng(*key):
    """A reproducible stream per named purpose, independent of Python's hash seed."""
    text = "/".join(str(k) for k in key)
    return np.random.default_rng(int.from_bytes(hashlib.sha256(text.encode()).digest()[:8], "little"))


def area(poly):
    p = np.asarray(poly, float)
    return 0.5 * float(np.sum(p[:, 0] * np.roll(p[:, 1], -1) - np.roll(p[:, 0], -1) * p[:, 1]))


def rect(x0, z0, x1, z1):
    return np.array([[x0, z0], [x1, z0], [x1, z1], [x0, z1]], float)


def clip(poly, joints, lo, hi):
    """Clip a convex polygon to the box [lo, hi], labeling each edge as a joint (True) or a flush cut.

    Edges lying on the box keep their label: a cell edge exactly on a tile edge is still a joint.
    """
    p, j = [np.asarray(v, float) for v in poly], list(joints)
    for axis in (0, 1):
        for sign, limit in ((1, hi[axis]), (-1, -lo[axis])):
            out_p, out_j = [], []
            for i in range(len(p)):
                a, b = p[i], p[(i + 1) % len(p)]
                da, db = sign * a[axis] - limit, sign * b[axis] - limit
                if da <= EPS:
                    out_p.append(a)
                    out_j.append(j[i])
                    if db > EPS:
                        out_p.append(a + (b - a) * (da / (da - db)))
                        out_j.append(False)
                elif db <= EPS:
                    out_p.append(a + (b - a) * (da / (da - db)))
                    out_j.append(j[i])
            p, j = out_p, out_j
            if len(p) < 3:
                return None, None
    p = np.array(p)
    keep = np.linalg.norm(p - np.roll(p, -1, axis=0), axis=1) > 1e-9
    p, j = p[keep], [label for label, k in zip(j, keep) if k]
    if len(p) < 3 or abs(area(p)) < 1e-6:
        return None, None
    return p, j


def offset(poly, distances):
    """Move each edge of a convex counter-clockwise polygon inward by its own distance."""
    p = np.asarray(poly, float)
    n = len(p)
    edges = np.roll(p, -1, axis=0) - p
    normals = np.column_stack([-edges[:, 1], edges[:, 0]]) / np.linalg.norm(edges, axis=1)[:, None]
    starts = p + normals * np.asarray(distances, float)[:, None]
    out = []
    for i in range(n):
        k = i - 1
        d1, d2 = edges[k], edges[i]
        cross = d1[0] * d2[1] - d1[1] * d2[0]
        if abs(cross) < 1e-12:
            out.append(starts[i])
            continue
        t = ((starts[i][0] - starts[k][0]) * d2[1] - (starts[i][1] - starts[k][1]) * d2[0]) / cross
        out.append(starts[k] + t * d1)
    return np.array(out)


def ccw(poly, joints):
    """Counter-clockwise order in the plane's own (u, v) axes, carrying edge labels along."""
    if area(poly) >= 0:
        return np.asarray(poly, float), list(joints)
    n = len(poly)
    # Reversed edge k runs p[n-1-k] -> p[n-2-k], the original edge n-2-k walked backwards.
    return np.asarray(poly, float)[::-1], [joints[(n - 2 - k) % n] for k in range(n)]


def block(poly, joints, top, base, slot, bevel=0.025, gap=0.02, tilt=(0.0, 0.0), lift=None, place=None):
    """A convex beveled block standing on a floor plane, as a closed piece in (x, y, z).

    Joint edges pull back by half the `gap` and are chamfered by `bevel`; flush cut edges keep a
    vertical face so a neighbor tile's half of the same cell continues it. `tilt` is the top's slope
    per unit (x, z) around the uncut centroid `lift` (defaults to this polygon's centroid).
    `place` maps the local points into another frame (wall faces) before the hull is taken.
    """
    p, j = ccw(poly, joints)
    outer = offset(p, [gap / 2 if label else 0.0 for label in j])
    if area(outer) <= 1e-5:
        return None
    inner = offset(outer, [bevel if label else 0.0 for label in j])
    if area(inner) <= 1e-6:
        inner = outer.mean(0) + (outer - outer.mean(0)) * .3
    center = p.mean(0) if lift is None else np.asarray(lift, float)

    def height(q):
        return top + tilt[0] * (q[:, 0] - center[0]) + tilt[1] * (q[:, 1] - center[1])

    ring = lambda q, y: np.column_stack([q[:, 0], y, q[:, 1]])  # noqa: E731
    points = np.vstack([ring(outer, np.full(len(outer), base)), ring(outer, height(outer) - bevel),
                        ring(inner, height(inner))])
    return kit.hull(points if place is None else place(points), slot)


def union(pieces, cuts=(), within=()):
    """Solid union of single-slot blocks, minus `cuts`, intersected with the union of `within`.

    Every face keeps the slot of the block (or cut, or bounding piece) it came from. Tiles stack many
    coplanar faces on their seam planes; float32 input would turn those into slivers that pinch
    when welded, so the whole union runs on 64-bit meshes.
    """
    slots, parts, holes, region = [], [], [], []
    for piece, into in [*((piece, parts) for piece in pieces), *((cut, holes) for cut in cuts),
                        *((bound, region) for bound in within)]:
        p, f = welded(piece.verts, piece.faces)
        slot = str(piece.slots[0])
        if slot not in slots:
            slots.append(slot)
        properties = np.column_stack([p, np.full(len(p), slots.index(slot), float)])
        shape = mf.Manifold(mf.Mesh64(np.ascontiguousarray(properties), np.ascontiguousarray(f, dtype=np.uint64)))
        if shape.status() != mf.Error.NoError:
            raise ValueError(f"Invalid tile block for slot {slot}: {shape.status()}")
        into.append(shape)
    result = mf.Manifold.batch_boolean(parts, mf.OpType.Add)
    if holes:
        result = result - mf.Manifold.batch_boolean(holes, mf.OpType.Add)
    if region:
        result = result ^ mf.Manifold.batch_boolean(region, mf.OpType.Add)
    if result.status() != mf.Error.NoError:
        raise ValueError(f"Tile union failed: {result.status()}")
    # Cuts can shave off specks of no real volume; drop them, but never a real detached part.
    bodies = [c for c in result.decompose() if c.volume() > 1e-5]
    if len(bodies) != 1:
        raise ValueError("Tile blocks do not form one connected solid")
    result = bodies[0]
    mesh = result.to_mesh64()
    properties, faces = np.asarray(mesh.vert_properties), np.asarray(mesh.tri_verts, np.int64)
    labels = np.rint(properties[:, 3][faces].mean(axis=1)).astype(int)
    p, welded_faces = welded(properties[:, :3], faces)
    # Collinear seam edges can leave a zero-area flap: the same triangle wound both ways. It
    # encloses nothing, so dropping the pair restores a two-faced edge everywhere.
    tri = p[welded_faces]
    flat = np.linalg.norm(np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0]), axis=1) < 1e-12
    keys = [tuple(sorted(face)) for face in welded_faces[flat].tolist()]
    paired = {key for key in keys if keys.count(key) == 2}
    keep = ~flat
    keep[np.flatnonzero(flat)] = [key not in paired for key in keys]
    return kit.Piece(properties[:, :3], faces[keep], np.array(slots, dtype=object)[labels[keep]])


def box(x0, y0, z0, x1, y1, z1, slot):
    """Axis-aligned closed box."""
    corners = np.array([[x, y, z] for x in (x0, x1) for y in (y0, y1) for z in (z0, z1)], float)
    return kit.hull(corners, slot)


def rock(center, radius, slot, seed, squash=0.55, facets=9):
    """A low-poly faceted pebble: the hull of a few jittered points, flattened to sit on a floor."""
    r = rng("rock", seed)
    angles = np.sort(r.uniform(0, 2 * np.pi, facets))
    ring = np.column_stack([np.cos(angles), np.zeros(facets), np.sin(angles)]) * r.uniform(.75, 1.1, (facets, 1))
    top = r.normal(0, .25, (3, 3)) * [1, 0, 1] + [0, 1, 0]
    bottom = np.array([[0, -.6, 0]])
    points = np.vstack([ring, top, bottom]) * [radius, radius * squash, radius]
    return kit.hull(points + np.asarray(center, float), slot)


def smoothstep(d, width):
    t = np.clip(np.asarray(d, float) / width, 0, 1)
    return t * t * (3 - 2 * t)


def ground_slab(half, heights, slots_of, base=-0.1, steps=20):
    """A closed slab whose top is a height field over [-half, half]^2 with analytic corner normals.

    `heights(x, z)` must be seam-safe: equal on every tile edge for all variants (variant relief
    faded to zero value and slope at the edge). Normals come from the field itself, so lighting is
    continuous across tiles. `slots_of(cx, cz)` colors top faces; `(None, None)` asks for the sides.
    """
    n = steps
    xs = np.linspace(-half, half, n + 1)
    gx, gz = np.meshgrid(xs, xs, indexing="ij")
    gy = heights(gx, gz)
    top = np.stack([gx, gy, gz], axis=-1).reshape(-1, 3)
    e = 1e-3
    dx = (heights(gx + e, gz) - heights(gx - e, gz)) / (2 * e)
    dz = (heights(gx, gz + e) - heights(gx, gz - e)) / (2 * e)
    normal = np.stack([-dx, np.ones_like(dx), -dz], axis=-1)
    normal = (normal / np.linalg.norm(normal, axis=-1, keepdims=True)).reshape(-1, 3)
    idx = np.arange((n + 1) ** 2).reshape(n + 1, n + 1)
    faces, face_normals, slots = [], [], []
    for i in range(n):
        for k in range(n):
            a, b, c, d = idx[i, k], idx[i + 1, k], idx[i + 1, k + 1], idx[i, k + 1]
            # Alternate diagonals so the facets do not all lean one way.
            quads = [(a, d, c), (a, c, b)] if (i + k) % 2 else [(a, d, b), (b, d, c)]
            for tri in quads:
                faces.append(tri)
                face_normals.append(normal[list(tri)])
                center = top[list(tri)].mean(0)
                slots.append(slots_of(center[0], center[2]))
    verts = [top]
    # Sides follow the boundary profile exactly, so neighbors share the same cross-section.
    rim = np.concatenate([idx[:, 0], idx[n, 1:], idx[::-1, n][1:], idx[0, ::-1][1:-1]])
    count = len(top)
    bottom = top[rim].copy()
    bottom[:, 1] = base
    verts.append(bottom)
    center_bottom = count + len(rim)
    verts.append([[0.0, base, 0.0]])
    side_slot = slots_of(None, None)
    for m in range(len(rim)):
        t0, t1 = rim[m], rim[(m + 1) % len(rim)]
        b0, b1 = count + m, count + (m + 1) % len(rim)
        for tri in ((t0, t1, b1), (t0, b1, b0), (center_bottom, b0, b1)):
            faces.append(tri)
            face_normals.append(None)
            slots.append(side_slot)
    verts = np.vstack([np.asarray(v, float) for v in verts])
    faces = np.asarray(faces)
    # The rim walks counter-clockwise seen from below; orient every face outward explicitly.
    tri = verts[faces]
    geometric = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    outward = np.einsum("ij,ij->i", geometric, tri.mean(1) - [0.0, (base + gy.mean()) / 2, 0.0]) >= 0
    top_faces = np.array([fn is not None for fn in face_normals])
    flip = np.where(top_faces, geometric[:, 1] < 0, ~outward)
    faces[flip] = faces[flip][:, ::-1]
    corner = np.zeros((len(faces), 3, 3))
    for f, fn in enumerate(face_normals):
        if fn is not None:
            corner[f] = fn[::-1] if flip[f] else fn
        else:
            g = np.cross(verts[faces[f, 1]] - verts[faces[f, 0]], verts[faces[f, 2]] - verts[faces[f, 0]])
            corner[f] = g / max(np.linalg.norm(g), 1e-12)
    return kit.Piece(verts, faces, np.asarray(slots, object), normals=corner)


def boundary_profile(piece, axis, value, along, ys, cross=None):
    """Sample which (along, y) points of the plane `coord[axis] == value` the piece's faces cover.

    `along` is the in-plane horizontal axis index; returns a boolean grid (len(ys), len(cross)).
    """
    tri = piece.verts[piece.faces]
    on = np.all(np.abs(tri[:, :, axis] - value) < 1e-5, axis=1)
    planar = tri[on][:, :, [along, 1]]
    u, v = np.meshgrid(cross, ys)
    points = np.column_stack([u.ravel(), v.ravel()])
    covered = np.zeros(len(points), bool)
    for a, b, c in planar:
        d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(d) < 1e-14:
            continue
        w1 = ((b[1] - c[1]) * (points[:, 0] - c[0]) + (c[0] - b[0]) * (points[:, 1] - c[1])) / d
        w2 = ((c[1] - a[1]) * (points[:, 0] - c[0]) + (a[0] - c[0]) * (points[:, 1] - c[1])) / d
        covered |= (w1 >= -1e-9) & (w2 >= -1e-9) & (1 - w1 - w2 >= -1e-9)
    return covered.reshape(len(ys), len(cross))
