"""KayKit-style construction vocabulary for room furnishings.

Everything is chunky. Boards are thick with broad chamfers, laid side by side so the
chamfers read as grooves; posts are square; stone is stacked from beveled blocks; rivets,
candles and heads are oversized. Boards and blocks vary a little in size and angle and lose
the odd corner to a chip, so a piece looks hand-made rather than machined. Each design
passes its own seeded generator, which keeps builds reproducible.

Placement frames: a `frame` is (turn degrees about x, y, z; origin) applied after a part is
placed in its local coordinates, so whole panels and racks can be tilted or turned.
"""

import numpy as np
import manifold3d as mf
from core import kit
from items.solid import box, lathe, tube, welded


def solid(piece):
    """A closed kit piece as a manifold, so it can be turned and cut like the other primitives."""
    p, f = welded(piece.verts, piece.faces)
    return mf.Manifold(mf.Mesh(np.asarray(p, np.float32), np.asarray(f, np.uint32)))


def chamfer(size):
    """KayKit's broad bevel: about a quarter of the thinnest side, never sharp, never mushy."""
    return float(min(.05, .26 * min(size)))


def block(size, center=(0, 0, 0), bevel=None):
    return solid(box(size, center, chamfer(size) if bevel is None else bevel))


def cushion(size, center=(0, 0, 0), roundness=.35):
    """A soft, rounded box: mattresses, pillows, blankets, sacks."""
    return solid(kit.rounded_box(size, 'base', center, roundness, 14, 9))


def post(radius, y0, y1, x=0, z=0, sides=10, top=None):
    """Turned upright (or frustum with `top`) on the Y axis at x, z."""
    return lathe([(radius, y0), (radius if top is None else top, y1)], sides).translate((x, 0, z))


def rod(a, b, radius, sides=8):
    return solid(tube(a, b, radius, sides))


def ball(center, radii, n=12):
    return mf.Manifold.sphere(1, n).scale(radii).translate(center)


def bend(path, radii, sides=7):
    """A tapered bar, limb or handle swept through `path`, one radius per point."""
    return solid(kit.sweep(path, kit.ring_profile(1, 1, sides), 'base', scale=radii))


def framed(shape, frame):
    return shape if frame is None else shape.rotate(tuple(frame[0])).translate(tuple(frame[1]))


def board(a, rng, size, center, slot='wood', frame=None, chips=1, wobble=.7, chip_slot='wood_light'):
    """One board, beam or stone: chamfered, a little uneven, and now and then chipped at an end corner."""
    size = np.asarray(size, float) * (1 + rng.uniform(-.02, .02, 3))
    shape = a.label(block(size), slot)
    along = int(np.argmax(size))
    for _ in range(chips):
        if rng.random() > .6:
            continue
        corner = np.where(rng.random(3) < .5, -1.0, 1.0) * size / 2
        if along != 1:
            corner[1] = size[1] / 2
        c = float(min(np.sort(size)[0] * rng.uniform(.55, .85), .16))
        chip = block((c, c, c), (0, 0, 0), c * .12).rotate(tuple(rng.uniform(20, 70, 3))).translate(tuple(corner))
        shape = shape - a.label(chip, chip_slot)
    shape = shape.rotate(tuple(rng.uniform(-wobble, wobble, 3))).translate(tuple(center))
    a.join(framed(shape, frame))


def planks(a, rng, length, width, thickness, frame, count=None, slot='wood', chips=1, y=0.0):
    """Parallel boards along local X, laid across local Z, `thickness` along Y: tops, seats, shelves, backs."""
    count = count or max(2, int(round(width / .3)))
    edges = np.cumsum(np.r_[0, rng.uniform(.85, 1.15, count)])
    edges = edges / edges[-1] * width - width / 2
    for z0, z1 in zip(edges[:-1], edges[1:]):
        shift = rng.uniform(-.03, .03) * length
        board(a, rng, (length * rng.uniform(.97, 1.0), thickness, z1 - z0 + .006), (shift * .2, y, (z0 + z1) / 2),
              slot, frame, chips, .5)


def square_post(a, rng, size, y0, y1, x=0, z=0, slot='wood', cap=None, frame=None):
    """A square timber post, optionally capped with a wider block."""
    # Posts sink a little into what they stand on so the joint stays one solid.
    y0 -= .02
    board(a, rng, (size, y1 - y0, size), (x, (y0 + y1) / 2, z), slot, frame, 1, .4)
    if cap:
        board(a, rng, (size + cap, cap * 1.4, size + cap), (x, y1, z), slot, frame, 1, .4)


def stones(a, rng, x0, x1, y0, y1, depth, z=0.0, rows=1, slot='stone', frame=None, span=(.5, .85), chips=1):
    """Courses of beveled stone blocks filling a box; joints stagger from row to row."""
    heights = np.linspace(y0, y1, rows + 1)
    for row, (b, t) in enumerate(zip(heights[:-1], heights[1:])):
        widths, total = [], x1 - x0
        start = rng.uniform(*span) * (.5 if row % 2 else 1)
        widths.append(min(start, total))
        while sum(widths) < total - 1e-6:
            widths.append(min(rng.uniform(*span), total - sum(widths)))
        if len(widths) > 1 and widths[-1] < span[0] * .45:
            widths[-2] += widths.pop()
        x = x0
        for w in widths:
            d = depth + rng.uniform(-.02, .03)
            board(a, rng, (w + .006, t - b + .006, d), (x + w / 2, (b + t) / 2, z + rng.uniform(-.012, .012)),
                  slot, frame, chips, .6, slot + '_dark' if not slot.endswith('_dark') else slot)
            x += w


def rivet(center, radius=.045, facing=(0, 0, 1)):
    """A domed rivet head, flattened against the surface it faces."""
    f = np.asarray(facing, float)
    scale = np.where(np.abs(f) > .5, .55, 1.0) * radius
    return ball(tuple(center), tuple(scale), 8)


def flame(x, y, z, radius):
    s = max(radius, .09)
    return lathe([(s * .42, y), (s * .7, y + s * .55), (s * .5, y + s * 1.35), (s * .03, y + s * 2.3)], 8).translate((x, 0, z))


def candle(a, tips, x, z, y, height, radius, drips=2, seed=0):
    """A chunky pillar candle: flared foot, slumped rim, fat wax runs and a thick wick. Its flame joins `tips`."""
    a.add(lathe([(radius * 1.16, y), (radius * 1.04, y + .05), (radius, y + .1), (radius, y + height - .06),
                 (radius * 1.04, y + height - .025), (radius * .82, y + height)], 10).translate((x, 0, z)), 'wax')
    for i in range(drips):
        t = seed * 2.4 + i * 2.3
        length = radius * (1.1 + .5 * (i % 2))
        a.add(ball((x + np.cos(t) * radius * .93, y + height - .04 - length * .55, z + np.sin(t) * radius * .93),
                   (radius * .3, length * .55, radius * .3), 7), 'wax')
    a.add(post(radius * .16, y + height - .03, y + height + .08, x, z, 5), 'wick')
    tips.append(flame(x, y + height + .055, z, radius))


def lit(a, tips, flames):
    """The model, or with `flames` only its flame tips, each a separate body."""
    if not flames:
        return a.build()
    meshes = [tip.to_mesh() for tip in tips]
    return kit.Piece.merge(*[kit.Piece(np.asarray(m.vert_properties[:, :3], float), np.asarray(m.tri_verts), 'flame')
                             for m in meshes])


# ---------------------------------------------------------------------------- outlines and placement

def outline(points):
    pts = np.asarray(points, float)
    if np.sum(pts[:, 0] * np.roll(pts[:, 1], -1) - pts[:, 1] * np.roll(pts[:, 0], -1)) < 0:
        pts = pts[::-1]
    return mf.CrossSection([pts])


def rect(width, height, center=(0, 0)):
    w, h = width / 2, height / 2
    return outline([(center[0] - w, center[1] - h), (center[0] + w, center[1] - h),
                    (center[0] + w, center[1] + h), (center[0] - w, center[1] + h)])


def star(points, outer, inner, center=(0, 0), turn=np.pi / 2):
    angles = turn + np.arange(points * 2) * np.pi / points
    radii = np.where(np.arange(points * 2) % 2, inner, outer)
    return outline(np.column_stack([center[0] + np.cos(angles) * radii, center[1] + np.sin(angles) * radii]))


def disc(radius, center=(0, 0), sides=20):
    t = np.linspace(0, 2 * np.pi, sides, endpoint=False)
    return outline(np.column_stack([center[0] + np.cos(t) * radius, center[1] + np.sin(t) * radius]))


def facade(section, thickness, z=0.0):
    """Extrude a section drawn in wall coordinates (x across, y up) forward from z."""
    return mf.Manifold.extrude(section, thickness).translate((0, 0, z))


def floor_plan(section, thickness, y=0.0):
    """Extrude a section drawn in floor coordinates (x, z) upward from y."""
    # Extrusion runs along +Z; turning 90 about X lays it down with section y along world z.
    return mf.Manifold.extrude(section, thickness).rotate((90, 0, 0)).translate((0, y + thickness, 0))


def jag(y, width=2.0, depth=2.0, above=True, amplitude=.07, teeth=5):
    """A jagged break: the solid above (or below) an irregular zigzag at height y, spanning x and z."""
    xs = np.linspace(-width / 2, width / 2, teeth * 2 + 1)
    edge = [(x + .04 * np.sin(i * 2.7), y + (amplitude if i % 2 else -amplitude) * (1 - .45 * (i % 3)) + .05 * np.sin(i * 1.3))
            for i, x in enumerate(xs)]
    cap = y + 4 if above else y - 4
    return facade(outline(edge + [(width / 2, cap), (-width / 2, cap)]), depth, -depth / 2)


def painted(shape, base, rule):
    """Color a solid's faces by `rule(centers, normals)`, a list of (mask, slot) pairs; later pairs win."""
    mesh = shape.to_mesh()
    result = kit.Piece(np.asarray(mesh.vert_properties[:, :3], float), np.asarray(mesh.tri_verts), base)
    tri = result.verts[result.faces]
    normals = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    normals /= np.maximum(np.linalg.norm(normals, axis=1, keepdims=True), 1e-12)
    for mask, slot in rule(tri.mean(1), normals):
        result.slots[mask] = slot
    return result


def bands(shape, base, ranges):
    """Paint horizontal bands: `ranges` of (y0, y1, slot). Keep profile points on the band edges."""
    return painted(shape, base, lambda c, n: [((c[:, 1] >= y0) & (c[:, 1] < y1), slot) for y0, y1, slot in ranges])


def turned(piece, degrees, axis='y'):
    """A built piece turned about the origin, for loose parts lying on the floor."""
    rotation = kit.rotation({'x': (1, 0, 0), 'y': (0, 1, 0), 'z': (0, 0, 1)}[axis], degrees)
    return kit.Piece(piece.verts @ rotation.T, piece.faces, piece.slots)


def moved(piece, offset):
    return kit.Piece(piece.verts + np.asarray(offset, float), piece.faces, piece.slots)


def grounded(piece, x=None, z=None):
    """Stand a loose piece on the floor, optionally centered at x, z."""
    lo, hi = piece.verts.min(0), piece.verts.max(0)
    dx = 0 if x is None else x - (lo[0] + hi[0]) / 2
    dz = 0 if z is None else z - (lo[2] + hi[2]) / 2
    return moved(piece, (dx, -lo[1], dz))


def ring(inner, outer, y0, y1, sides=12):
    """A flat band around the Y axis: hoops, rims, belts, tree rings."""
    section = mf.CrossSection([np.array([(inner, y0), (outer, y0), (outer, y1), (inner, y1)], float)])
    return mf.Manifold.revolve(section, sides).rotate((-90, 0, 0))


def chain(a, x, top, links, z=.14, size=1.0):
    """Hanging chain links in alternating planes, overlapping so the solid stays one piece. Returns the last link's center."""
    from items.solid import loop
    step = .15 * size
    for i in range(links):
        link = loop((0, 0, 0), (.065 * size, .105 * size), .034 * size, 10)
        if i % 2:
            link = link.rotate((0, 90, 0))
        a.add(link.translate((x, top - step * i, z)), 'iron')
    return top - step * (links - 1)
