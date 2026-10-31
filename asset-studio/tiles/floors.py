"""Floor tile builders on the KayKit footprint: 4x4 (`large`) and 2x2 (`small`) slabs, top at y=0.05.

Cell floors (planks, parquet, flagstones) are beveled blocks unioned onto a grout slab. Ground
floors (sand, dirt) are seam-safe height fields with analytic normals plus loose stones. See
`tiles.geometry` for the seam contract every builder honors.
"""

import numpy as np

from core import kit
from core.export import corner_normals
from tiles.geometry import SEAM, block, box, clip, ground_slab, rect, rng, rock, smoothstep, union

SIZES = {"large": 4.0, "small": 2.0}
TOP, BASE, GROUT, PIT = 0.05, -0.1, 0.0, -0.07


def frame(size):
    """Half extent and the pattern coordinate of the tile center: small tiles sit on odd units."""
    half = SIZES[size] / 2
    return half, 0.0 if size == "large" else 1.0


def class_key(poly, center):
    """A cell's periodic identity: its centroid in pattern space reduced by the seam lattice."""
    c = (np.asarray(poly, float).mean(0) + center) % SEAM
    return tuple(np.round(c, 3) % SEAM)


def cell_floor(cells, half, design, pits=(), rubble=(), slot_grout="grout"):
    """Union beveled cells onto the grout slab; `cells` yields (polygon, joints, slot, top, tilt, lift).

    `pits` are (polygon, joints) of missing cells sunk into the slab, so a lost board or slab reads
    as a hole; `rubble` are loose stones resting in them. Pits stay inside their cell's gap, so
    a tile edge keeps the family's seam cross-section.
    """
    bevel, gap = design.get("bevel", .022), design.get("gap", .014)
    blocks = [block(poly, joints, top, GROUT - .02, slot, bevel=bevel, gap=gap, tilt=tilt, lift=lift)
              for poly, joints, slot, top, tilt, lift in cells]
    holes = [block(poly, joints, .3, PIT, slot_grout, bevel=0.0, gap=gap) for poly, joints in pits]
    solid = union([box(-half, BASE, -half, half, GROUT, half, slot_grout), *[b for b in blocks if b is not None]],
                  cuts=[h for h in holes if h is not None])
    return kit.Piece.merge(solid, *rubble)


def loose_stones(r, poly, slots, key, count, size):
    """A few faceted stones lying on the floor of a pit."""
    lo, hi = poly.min(0) + size[1], poly.max(0) - size[1]
    stones = []
    for i in range(count if np.all(hi > lo) else 0):
        x, z = r.uniform(lo, hi)
        radius = r.uniform(*size)
        stone = rock((x, PIT + radius * .25, z), radius, slots[r.integers(len(slots))], (*key, i))
        stone.normals = corner_normals(stone.verts, stone.faces, crease=35.0)
        stones.append(stone)
    return stones


def tones(design, default):
    return list(design.get("tones", default))


def planks(design, size, variant, family):
    """Boards along X. Rows align to the seam lattice; joints stagger per variant and never sit on a
    tile edge, so a board crossing a seam is one continuous board with its row's class tone."""
    half, center = frame(size)
    width = design.get("plank_width", .4)
    rows = round(2 * half / width)
    if abs(rows * width - 2 * half) > 1e-6 or abs(round(SEAM / width) * width - SEAM) > 1e-6:
        raise ValueError("plank_width must divide the 2-unit seam")
    shades = tones(design, ["plank", "plank_light", "plank_dark", "plank_warm"])
    lo, hi = design.get("min_length", .9), design.get("max_length", 2.4)
    margin, stagger = design.get("margin", .35), design.get("stagger", .3)
    jitter, tilt = design.get("height_jitter", .006), design.get("tilt", .004)
    missing = design.get("missing", 0.0)
    family_rng = rng(family, "plank-rows")
    row_tone = [shades[i] for i in family_rng.permutation(np.resize(np.arange(len(shades)), round(SEAM / width)))]
    r = rng(family, variant, size, "planks")
    previous, cells, pits = [], [], []
    for row in range(rows):
        z0 = -half + row * width
        for _ in range(40):
            x, joints = -half + r.uniform(margin, max(margin, hi * .8)), []
            while x <= half - margin:
                joints.append(x)
                x += r.uniform(lo, hi)
            if all(abs(a - b) >= stagger for a in joints for b in previous):
                break
        previous = joints
        ends = [-half, *joints, half]
        crossing_tone = row_tone[round((z0 + center) / width) % len(row_tone)]
        for k in range(len(ends) - 1):
            x0, x1 = ends[k], ends[k + 1]
            crosses = k == 0 or k == len(ends) - 2
            poly = rect(x0, z0, x1, z0 + width)
            seams = [True, x1 < half - 1e-9, True, x0 > -half + 1e-9]
            if crosses:
                cells.append((poly, seams, crossing_tone, TOP, (0.0, 0.0), None))
            elif r.random() < missing:
                pits.append((poly, seams))
            else:
                cells.append((poly, seams, shades[r.integers(len(shades))], TOP + r.uniform(-jitter, jitter),
                              (r.uniform(-tilt, tilt) / (x1 - x0), r.uniform(-tilt, tilt) / width), None))
    return cell_floor(cells, half, design, pits)


def herringbone(design, size, variant, family):
    """Parquet bricks (2:1) on the herringbone lattice t1=(w, w), t2=(2w, -2w), which repeats every
    seam for w = 0.5 or 0.25. Bricks are lattice cells, so cut bricks are class-colored."""
    half, center = frame(size)
    w = design.get("brick_width", .5)
    if w not in (.5, .25):
        raise ValueError("herringbone brick_width must be 0.5 or 0.25 to repeat every seam")
    shades = tones(design, ["plank", "plank_light", "plank_dark", "plank_warm"])
    jitter = design.get("height_jitter", .004)
    r = rng(family, variant, size, "herringbone")
    pair = [rect(0, 0, 2 * w, w), rect(0, w, w, 3 * w)]
    t1, t2 = np.array([w, w]), np.array([2 * w, -2 * w])
    reach = int(np.ceil(2 * half / w)) + 4
    cells, seen = [], set()
    for a in range(-reach, reach + 1):
        for b in range(-reach, reach + 1):
            shift = a * t1 + b * t2 - center
            for brick in pair:
                poly = brick + shift
                if poly[:, 0].max() <= -half or poly[:, 0].min() >= half or poly[:, 1].max() <= -half or poly[:, 1].min() >= half:
                    continue
                key = tuple(np.round(poly.mean(0), 4))
                if key in seen:
                    continue
                seen.add(key)
                cut, joints = clip(poly, [True] * 4, (-half, -half), (half, half))
                if cut is None:
                    continue
                if not all(joints):
                    tone = shades[rng(family, "brick", class_key(poly, center)).integers(len(shades))]
                    cells.append((cut, joints, tone, TOP, (0.0, 0.0), None))
                else:
                    cells.append((cut, joints, shades[r.integers(len(shades))], TOP + r.uniform(-jitter, jitter),
                                  (0.0, 0.0), None))
    return cell_floor(cells, half, design)


def split_rects(r, x0, z0, x1, z1, grid, smallest, largest):
    """Recursive guillotine split of one seam block into flagstones snapped to `grid`."""
    w, h = x1 - x0, z1 - z0
    big = max(w, h)
    if big <= smallest + 1e-9 or (big <= largest + 1e-9 and r.random() < .45):
        return [(x0, z0, x1, z1)]
    along_x = w > h + 1e-9 or (abs(w - h) < 1e-9 and r.random() < .5)
    length = w if along_x else h
    choices = np.arange(grid, length - grid / 2, grid)
    choices = choices[(choices >= smallest - 1e-9) & (length - choices >= smallest - 1e-9)]
    if not len(choices):
        return [(x0, z0, x1, z1)]
    cut = float(r.choice(choices))
    if along_x:
        return split_rects(r, x0, z0, x0 + cut, z1, grid, smallest, largest) + split_rects(r, x0 + cut, z0, x1, z1, grid, smallest, largest)
    return split_rects(r, x0, z0, x1, z0 + cut, grid, smallest, largest) + split_rects(r, x0, z0 + cut, x1, z1, grid, smallest, largest)


def crack(r, poly):
    """Split a convex slab along a random chord: two real pieces with a groove between them."""
    c = poly.mean(0) + r.uniform(-.15, .15, 2) * np.ptp(poly, axis=0)
    angle = r.uniform(0, np.pi)
    n = np.array([np.cos(angle), np.sin(angle)])
    halves = []
    for sign in (1, -1):
        pts = []
        for i in range(len(poly)):
            a, b = poly[i], poly[(i + 1) % len(poly)]
            da, db = sign * (a - c) @ n, sign * (b - c) @ n
            if da <= 0:
                pts.append(a)
            if da * db < 0:
                pts.append(a + (b - a) * (da / (da - db)))
        if len(pts) >= 3:
            halves.append(np.array(pts))
    return halves


def chip(r, poly, size):
    """Knock one corner off a stone so the slabs read as worn, not machined."""
    k = r.integers(len(poly))
    a, b, c = poly[k - 1], poly[k], poly[(k + 1) % len(poly)]
    s = r.uniform(.4, 1) * size
    cut = [b + (a - b) / np.linalg.norm(a - b) * s, b + (c - b) / np.linalg.norm(c - b) * s]
    return np.vstack([poly[:k], cut, poly[k + 1:]])


def flagstones(design, size, variant, family):
    """Stone slabs and bricks. `layout` is `random` (guillotine split per seam block), `grid` or
    `running` (offset courses). Random and grid seams lie in grooves, so every slab is interior and
    varies per variant; running-bond bricks cross the seams and are cut with class properties."""
    half, center = frame(size)
    shades = tones(design, ["stone", "stone_light", "stone_dark"])
    jitter, tilt = design.get("height_jitter", .008), design.get("tilt", .012)
    chips, cracks, missing = design.get("chips", .25), design.get("cracks", 0.0), design.get("missing", 0.0)
    layout = design.get("layout", "random")
    checker = design.get("checker", False)
    r = rng(family, variant, size, "flagstones")
    rects = []
    if layout == "running":
        course, length = design.get("course", .5), design.get("length", 1.0)
        for z in np.arange(-half, half - 1e-9, course):
            shift = length / 2 * (round((z + center) / course) % 2) - center
            for k in range(int(np.floor((-half - shift) / length)) - 1, int(np.ceil((half - shift) / length)) + 1):
                rects.append((k * length + shift, z, (k + 1) * length + shift, z + course))
    else:
        for bx in np.arange(-half, half - 1e-9, SEAM):
            for bz in np.arange(-half, half - 1e-9, SEAM):
                if layout == "random":
                    rects += split_rects(r, bx, bz, bx + SEAM, bz + SEAM, design.get("grid", .25),
                                         design.get("smallest", .5), design.get("largest", 1.0))
                elif layout == "grid":
                    cell = design.get("cell", 1.0)
                    rects += [(x, z, x + cell, z + cell) for x in np.arange(bx, bx + SEAM - 1e-9, cell)
                              for z in np.arange(bz, bz + SEAM - 1e-9, cell)]
                else:
                    raise ValueError(f"Unknown flagstone layout {layout}")
    cells, pits, rubble = [], [], []
    for x0, z0, x1, z1 in rects:
        poly, joints = clip(rect(x0, z0, x1, z1), [True] * 4, (-half, -half), (half, half))
        if poly is None:
            continue
        if not all(joints):
            tone = shades[rng(family, "stone", class_key(rect(x0, z0, x1, z1), center)).integers(len(shades))]
            cells.append((poly, joints, tone, TOP, (0.0, 0.0), None))
            continue
        if r.random() < missing:
            pits.append((poly, joints))
            rubble += loose_stones(r, poly, shades, (family, variant, size, len(pits)), r.integers(1, 4), (.06, .13))
            continue
        # A checkerboard keys tone to the cell's parity in pattern space, so it runs across seams.
        parity = int(np.floor((x0 + center) / (x1 - x0) + 1e-6) + np.floor((z0 + center) / (z1 - z0) + 1e-6)) % 2
        if r.random() < chips:
            poly = chip(r, poly, design.get("chip_size", .12))
        for piece in crack(r, poly) if r.random() < cracks else [poly]:
            tone = shades[parity * (len(shades) // 2) + r.integers(len(shades) // 2)] if checker else shades[r.integers(len(shades))]
            cells.append((piece, [True] * len(piece), tone, TOP + r.uniform(-jitter, jitter),
                          (r.uniform(-tilt, tilt), r.uniform(-tilt, tilt)), None))
    return cell_floor(cells, half, design, pits, rubble)


def ground(design, size, variant, family):
    """Dirt or cave ground: per-variant relief that fades to a flat seam band, with loose stones and
    clods resting wholly inside the tile."""
    half, _ = frame(size)
    r = rng(family, variant, size, "ground")
    amp = design.get("amplitude", .03)
    bumps = [(r.uniform(-half * .75, half * .75), r.uniform(-half * .75, half * .75), r.uniform(.25, .7), r.normal(0, 1))
             for _ in range(int(design.get("bumps", 7) * (half / 2) ** 2) + 1)]
    margin = design.get("margin", .55 if size == "large" else .4)

    def window(x, z):
        return smoothstep(half - np.abs(x), margin) * smoothstep(half - np.abs(z), margin)

    def detail(x, z):
        value = sum(s * np.exp(-((x - bx) ** 2 + (z - bz) ** 2) / (2 * rad ** 2)) for bx, bz, rad, s in bumps)
        return value * amp * .6

    def heights(x, z):
        return TOP - amp * .2 + window(x, z) * detail(x, z)

    shades = tones(design, ["ground", "ground_light", "ground_dark"])
    pieces = [ground_slab(half, heights, lambda x, z: shades[0], base=BASE, steps=int(design.get("steps", 10) * half))]
    stone_slots = design.get("stone_tones", ["pebble", "pebble_dark"])
    count = int(design.get("stones", 6) * (half / 2) ** 2)
    for i in range(count):
        x, z = r.uniform(-half + .2, half - .2, 2)
        radius = r.uniform(*design.get("stone_size", [.05, .12]))
        y = float(heights(np.array(x), np.array(z)))
        stone = rock((x, y + radius * .1, z), radius, stone_slots[r.integers(len(stone_slots))], (family, variant, size, i))
        stone.normals = corner_normals(stone.verts, stone.faces, crease=35.0)
        pieces.append(stone)
    # Clods: lumps of the ground itself, sunk deeper and flatter than stones.
    for i in range(int(design.get("clods", 0) * (half / 2) ** 2)):
        x, z = r.uniform(-half + .25, half - .25, 2)
        radius = r.uniform(*design.get("clod_size", [.07, .16]))
        y = float(heights(np.array(x), np.array(z)))
        clod = rock((x, y - radius * .1, z), radius, shades[1 + r.integers(len(shades) - 1)], (family, variant, size, "clod", i),
                    squash=.4, facets=7)
        clod.normals = corner_normals(clod.verts, clod.faces, crease=45.0)
        pieces.append(clod)
    return kit.Piece.merge(*pieces)


STYLES = {"planks": planks, "herringbone": herringbone, "flagstones": flagstones, "ground": ground}
