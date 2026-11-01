"""Jungle floor plants: ferns, leafy and broadleaf bushes, root tangles and mushroom clusters.

They are the growth on the jungle walls (`tiles/dressing.py`) come down to the floor: the recipes take the
walls' moss, fern and root colors with the tiles' gentle ramp, so a fern in a room matches the ferns at a
wall's foot. Built chunky like the rest of the set: few broad fronds and leaves with a folded midrib,
faceted moss cushions, thick tapered roots, fat mushrooms. Plants stand on y = 0 around the origin.
"""

import zlib

import numpy as np
import manifold3d as mf
from core import kit
from items.solid import Assembly, lathe
from items.furnishing_kit import ball, bend, block, outline, painted


def random(name):
    return np.random.default_rng(zlib.crc32(name.encode()))


def lump(r, center, radii, slot, points=16):
    """A faceted cushion of growth: the hull of jittered points on an ellipsoid, flat underneath, like the walls' moss."""
    d = r.normal(size=(points, 3))
    d /= np.linalg.norm(d, axis=1, keepdims=True)
    d[:, 1] = np.abs(d[:, 1]) * .9 - .25
    p = np.asarray(center) + d * np.asarray(radii) * r.uniform(.85, 1.1, (points, 1))
    return kit.hull(p, slot)


def blade(length, width, teeth=0, notch=.45):
    """A leaf outline along +X from the origin, widest past its middle: smooth, or with `teeth` leaflets a side."""
    t = np.linspace(0, 1, max(2 * teeth + 1, 13))
    half = width / 2 * np.sin(np.pi * t ** .8) ** .8
    if teeth:
        half = half * np.where(np.arange(len(t)) % 2, 1.0, notch)
    half[0] = width * .06
    top = np.column_stack([t * length, half])
    bottom = np.column_stack([t[1:-1] * length, -half[1:-1]])[::-1]
    return outline(np.vstack([top, [(length * 1.02, 0)], bottom]))


def leaf(r, length, width, yaw, root, rise=.6, droop=.15, fold=.3, teeth=0, thickness=.04):
    """A frond or leaf: `blade` thickened, folded along its midrib and arched from `root` (rising to `rise` of its
    length, its tip falling to `droop`), turned `yaw` degrees about Y from +X."""
    shape = mf.Manifold.extrude(blade(length, width, teeth), thickness).translate((0, 0, -thickness / 2))
    shape = shape.refine_to_length(length / 8)
    p0, p1, p2 = np.zeros(2), np.array([length * .32, length * rise]), np.array([length * .88, length * droop])

    def arch(v):
        v = np.array(v)
        t = np.clip(v[:, 0] / length, 0, 1.02)[:, None]
        point = (1 - t) ** 2 * p0 + 2 * (1 - t) * t * p1 + t ** 2 * p2
        tangent = 2 * (1 - t) * (p1 - p0) + 2 * t * (p2 - p1)
        tangent /= np.linalg.norm(tangent, axis=1, keepdims=True)
        normal = np.column_stack([-tangent[:, 1], tangent[:, 0]])
        # Thickness follows the arch's normal; the blade's halves lift away from the midrib.
        flat = point + normal * v[:, 2:3]
        # Across maps to -Z so the warp keeps the solid's handedness (outward faces stay outward).
        return np.column_stack([flat[:, 0], flat[:, 1] + np.abs(v[:, 1]) * fold, -v[:, 1]])
    shape = shape.warp_batch(arch)
    return shape.rotate((0, yaw, 0)).translate(tuple(root))


def fiddlehead(r, root, yaw, height, radius=.04):
    """A young fern frond still curled: a stalk rising into a tight spiral."""
    turns = np.linspace(0, 1.7 * np.pi, 9)
    curl = .11 * height
    path = [(0, 0, 0), (.02, height * .45, 0)] + [(.05 + curl * np.sin(a), height * .8 + curl * np.cos(a), 0) for a in turns]
    path = np.array(path) * [1, 1, 1]
    path = path @ kit.rotation((0, 1, 0), yaw).T + np.asarray(root)
    return bend(path, np.linspace(radius, radius * .6, len(path)), 6)


def fern(fronds=9, size=1.0, curls=2, seed='fern'):
    """A fern clump: broad notched fronds arching out from a mossy crown, a few curled young fronds in the middle."""
    a, r = Assembly(), random(seed)
    a.add(lump(r, (0, .04, 0), (.3 * size, .2 * size, .3 * size), 'moss_dark'))
    for k in range(fronds):
        yaw = 360 * k / fronds + r.uniform(-14, 14)
        length = size * r.uniform(.85, 1.15)
        a.add(leaf(r, length, .3 * size, yaw, (0, .08 * size, 0), r.uniform(.5, .75), r.uniform(.05, .22), .28, teeth=7),
              'fern' if k % 2 else 'fern_dark')
    for k in range(curls):
        a.add(fiddlehead(r, (r.uniform(-.06, .06), .1, r.uniform(-.06, .06)), 120 * k + r.uniform(0, 60), .55 * size), 'fern')
    return a.build()


def bush(lobes=7, size=1.0, leaves=8, seed='bush'):
    """A round leafy shrub: faceted cushions of foliage heaped on a dome, broad leaves poking out between them."""
    a, r = Assembly(), random(seed)
    tones = ['moss', 'moss_dark', 'moss_light']
    centers = []
    for k in range(lobes):
        # A ring of lobes resting on the floor around one heaped on top; lumps reach a quarter of their height below center.
        angle = 2 * np.pi * k / (lobes - 1) + r.uniform(-.3, .3)
        ring = .45 * size if k < lobes - 1 else 0
        radius = r.uniform(.36, .44) * size * (1.15 if not ring else 1)
        center = (np.cos(angle) * ring, (.1 if ring else .42) * size, np.sin(angle) * ring)
        a.add(lump(r, center, (radius, radius * 1.25, radius), tones[k % 3], 18))
        centers.append((np.array(center), radius))
    for k in range(leaves):
        center, radius = centers[k % len(centers)]
        yaw = r.uniform(0, 360)
        out = np.array([np.cos(np.radians(-yaw)), 0, np.sin(np.radians(-yaw))])
        root = center + out * radius * .5 + (0, radius * r.uniform(.15, .35), 0)
        a.add(leaf(r, .55 * size, .3 * size, yaw, root, r.uniform(.2, .45), r.uniform(-.1, .1), .18), 'fern' if k % 2 else 'moss_light')
    return a.build()


def broadleaf(leaves=6, size=1.0, seed='broadleaf'):
    """A jungle broadleaf: big rounded leaves on long stalks, rising from a mossy crown and bowing outward."""
    a, r = Assembly(), random(seed)
    a.add(lump(r, (0, .04, 0), (.28 * size, .18 * size, .28 * size), 'moss_dark'))
    for k in range(leaves):
        yaw = 360 * k / leaves + r.uniform(-18, 18)
        reach, height = r.uniform(.25, .5) * size, r.uniform(.55, 1.0) * size
        heading = kit.rotation((0, 1, 0), yaw) @ np.array([1.0, 0, 0])
        stalk = np.array([(0, .06, 0), heading * reach * .3 + (0, height * .6, 0), heading * reach + (0, height, 0)])
        a.add(bend(stalk, [.05, .04, .035], 6), 'fern_dark')
        a.add(leaf(r, .8 * size, .48 * size, yaw, stalk[-1] - heading * .04, .22, -.32, .22), 'fern' if k % 2 else 'fern_dark')
    return a.build()


def root(r, start, heading, length, radius, humps=1):
    """A thick root snaking out from `start` along `heading`: half buried, arching clear of the floor `humps`
    times, tapering as it dives back under."""
    t = np.linspace(0, 1, 11)
    side = np.cross(heading, (0, 1, 0))
    sway = np.sin(t * np.pi * r.uniform(1.2, 2.2) + r.uniform(0, np.pi)) * r.uniform(.05, .1) * length
    arch = np.abs(np.sin(np.pi * humps * t)) * r.uniform(.12, .2) * (1 - .5 * t)
    path = np.asarray(start) + np.outer(t, heading) * length + np.outer(sway - sway[0], side)
    path[:, 1] = start[1] * (1 - t) ** 2 + arch - radius * .6 * t
    return bend(path, radius * (1 - .62 * t), 7)


def roots(count=5, size=1.0, seed='roots', knot=True):
    """Thick roots breaking out of the floor: radiating from a gnarled knot, or (`knot` off) long roots snaking
    across the floor side by side. Moss sits in their crooks; everything below the floor is cut away."""
    a, r = Assembly(), random(seed)
    if knot:
        a.add(lump(r, (0, .02, 0), (.5 * size, .62 * size, .5 * size), 'root', 22))
    for k in range(count):
        if knot:
            heading = kit.rotation((0, 1, 0), 360 * k / count + r.uniform(-20, 20)) @ np.array([1.0, 0, 0])
            start, length, humps = heading * .25 * size + (0, .22 * size, 0), r.uniform(.95, 1.3) * size, 1
        else:
            heading = kit.rotation((0, 1, 0), r.uniform(-12, 12)) @ np.array([1.0, 0, 0])
            offset = (k - (count - 1) / 2) * .55 + r.uniform(-.1, .1)
            start, length, humps = heading * -1.2 * size + np.cross(heading, (0, 1, 0)) * offset * size + (0, -.04, 0), \
                r.uniform(2.1, 2.5) * size, 2
        a.join(a.label(root(r, start, heading, length, .17 * size if knot else .15 * size, humps), 'root'))
        # A rootlet forks off a third of the way along.
        fork = kit.rotation((0, 1, 0), r.choice([-1, 1]) * r.uniform(30, 50)) @ heading
        a.join(a.label(root(r, np.asarray(start) + heading * length * .3 + (0, .02, 0), fork, length * .4, .08 * size), 'root'))
    spots = [(0, .36, 0)] if knot else []
    spots += [(r.uniform(-.8, .8), .1, r.uniform(-.5, .5)) for _ in range(3 - len(spots))]
    for k, spot in enumerate(spots):
        a.add(lump(r, np.asarray(spot) * size, (.22 * size, .12 * size, .22 * size), ['moss', 'moss_light', 'moss_dark'][k]))
    a.cut(block((8, 2, 8), (0, -1.02, 0), 0), 'root')
    return a.build(loose=not knot)


# Cap profiles as (radius, rise) in cap radii: a fat dome, a broad flat cap, a tall bell.
CAPS = {'dome': [(.3, 0), (.98, .08), (1, .2), (.8, .5), (.42, .7), (.01, .76)],
        'flat': [(.3, 0), (1, .06), (.98, .18), (.62, .3), (.01, .34)],
        'bell': [(.3, 0), (.86, .05), (.8, .4), (.46, .9), (.01, 1.15)]}


def mushroom(a, r, x, z, height, cap, spots=0, lean=0.0, turn=0.0, shape='dome'):
    """One chunky mushroom: a swollen stem under a fat cap with dark gills beneath and raised warts on top."""
    m = Assembly()
    m.slots = a.slots
    stem = cap * .4
    m.add(lathe([(stem * 1.25, 0), (stem * 1.15, height * .25), (stem, height * .7), (stem * .9, height + cap * .1)], 10), 'stem')
    dome = lathe([(cap * radius, height + cap * rise) for radius, rise in CAPS[shape]], 14)
    m.add(painted(dome, 'cap', lambda c, n: [(n[:, 1] < -.4, 'gill')]))
    for k in range(spots):
        angle, rise = 2 * np.pi * k / spots + r.uniform(-.4, .4), r.uniform(.25, .6)
        radial = cap * (.95 - .5 * rise)
        point = (np.cos(angle) * radial, height + cap * (.2 + .55 * rise), np.sin(angle) * radial)
        m.add(ball(point, (cap * .14, cap * .07, cap * .14), 6), 'spot')
    a.join(m.build_solid().rotate((0, 0, lean)).rotate((0, turn, 0)).translate((x, 0, z)))


def mushrooms(spec, spots=0, seed='mushrooms', shape='dome'):
    """A cluster of mushrooms of different sizes, leaning apart, on a cushion of moss. `spec` lists (x, z, height,
    cap radius) per mushroom; `shape` names their cap profile in `CAPS`."""
    a, r = Assembly(), random(seed)
    extent = max(max(abs(x), abs(z)) + cap * .6 for x, z, _, cap in spec)
    a.add(lump(r, (0, 0, 0), (max(.55, extent), .16, max(.45, extent * .85)), 'moss_dark', 18))
    for x, z, height, cap in spec:
        lean = np.degrees(np.arctan2(np.hypot(x, z), 1.5)) * .6
        mushroom(a, r, x, z, height, cap, spots, -lean, np.degrees(np.arctan2(-z, x)), shape)
    return a.build()


KINDS = {'fern': fern, 'bush': bush, 'broadleaf': broadleaf, 'roots': roots, 'mushrooms': mushrooms}
