"""KayKit dungeon walls (CC0) grown over with moss, grass, hanging vines or sewer slime.

The original wall is kept exactly; growth is added as closed, separate pieces found by casting
rays onto the wall's own surfaces, so moss sits on the stone it grows from. Growth keeps clear of
piece ends, so the original seams and pairings with plain KayKit walls are untouched.
"""

import numpy as np

from core import kit
from core.export import corner_normals
from tiles import kaykit
from tiles.geometry import rng
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


def blade(r, root, normal, height, slot, room):
    """One grass blade: a thin three-sided spike leaning off the wall, at most `room` outward."""
    lean = normal * r.uniform(.15, .45) + r.normal(0, .2, 3) * [1, 0, 1]
    lean *= min(1.0, max(room - .025, 0.0) / max(abs(lean @ normal) * height, 1e-6))
    tip = root + np.array([0, height, 0]) + lean * height
    piece = kit.spike(root - [0, .02, 0], tip, r.uniform(.012, .022), slot, sides=3)
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


def overgrown(design, piece, variant, family):
    """Moss cushions, cap moss spilling over the edge, leafy vines, grass at the foot, or sewer slime.

    Growth is deliberately chunky, like the rest of the KayKit set: a few large cushions read at
    the game camera, a scatter of small chips does not.
    """
    mesh, _ = kaykit.load(design.get("source", {}).get(piece, piece))
    r = rng(family, variant, piece, "overgrowth")
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
    return kit.Piece.merge(mesh, *growth)


def source_colors(design, pieces):
    """The KayKit swatch colors a family's pieces use, for recipes that keep the original stone."""
    colors = {}
    for piece in pieces:
        colors.update(kaykit.load(design.get("source", {}).get(piece, piece))[1])
    return colors


STYLES = {"overgrown": overgrown}
