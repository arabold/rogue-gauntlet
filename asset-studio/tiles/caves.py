"""Cave walls on the KayKit wall footprints: big faceted boulders heaped between timber posts.

A piece is a set of arms. An open arm end carries a squared post on a stone footing, carved with a
few grain splits like the wooden shields; posts, footings and splits are mirror-symmetric, so the
end plane splits one post between two pieces and every end shows the same cross-section. Arms that
meet (corner, T-split, crossing) share a hub post. Boulders and cracked rubble fill stay between the
posts and vary freely per variant; the top stays open rock. Doorways and gates frame KayKit's door
opening in timber, a wall end slumps into rubble, and the pillar stands free. The gate leaf is its own
model, hinged like KayKit's door, for the game's Door to swing. Rock is faceted.
"""

import numpy as np

from core import kit
from prefabs.details.wear import solid_gash
from tiles.geometry import rng, union
from tiles.walls import HALF_THICK, HEIGHT


def boulder(r, center, radii, along, slot):
    """A faceted rock: the hull of jittered points on an ellipsoid (radii: along, up, across)."""
    u = r.normal(0, 1, (16, 3))
    u /= np.linalg.norm(u, axis=1, keepdims=True)
    u *= r.uniform(.82, 1.05, (16, 1)) * radii
    across = np.array([along[2], 0.0, -along[0]])
    points = np.asarray(center, float) + np.outer(u[:, 0], along) + np.outer(u[:, 1], [0, 1, 0]) + np.outer(u[:, 2], across)
    return kit.hull(points, slot)


def beam(p0, p1, side, half_side, half_other, slot, chamfer=.035):
    """A squared timber from p0 to p1 with chamfered long edges; `side` orients its cross-section."""
    p0, p1 = np.asarray(p0, float), np.asarray(p1, float)
    axis = (p1 - p0) / np.linalg.norm(p1 - p0)
    a = np.asarray(side, float) - axis * (np.asarray(side, float) @ axis)
    a /= np.linalg.norm(a)
    b = np.cross(axis, a)
    w, h, c = half_side, half_other, chamfer
    octagon = [(w, h - c), (w - c, h), (c - w, h), (-w, h - c), (-w, c - h), (c - w, -h), (w - c, -h), (w, c - h)]
    ring = np.array([u * a + v * b for u, v in octagon])
    return kit.hull(np.vstack([p0 + ring, p1 + ring]), slot)


def groove(p0, p1, face, slot, depth=.02, width=.012):
    """A V-cut along a face: a thin wedge to subtract, whose walls take `slot` (`face` is the
    face's outward offset vector from the cut's center line)."""
    p0, p1, face = np.asarray(p0, float), np.asarray(p1, float), np.asarray(face, float)
    out = face / np.linalg.norm(face)
    lateral = np.cross(out, (p1 - p0) / np.linalg.norm(p1 - p0)) * width
    lips = [face + out * .01 + lateral, face + out * .01 - lateral, face - out * depth]
    return kit.hull(np.vstack([p0 + np.array(lips), p1 + np.array(lips)]), slot)


def split(point, outward, lateral, length, width, depth, angle, slot):
    """A grain split, the wooden shields' V-cut: a cutter to subtract, laid on a face at `point`
    and turned `angle` degrees off vertical toward `lateral`."""
    cutter = solid_gash(length, width, depth, slot)
    t = np.radians(angle)
    up = np.array([0.0, 1.0, 0.0])
    grain, across = np.cos(t) * up + np.sin(t) * lateral, np.cos(t) * lateral - np.sin(t) * up
    local = cutter.verts
    # Mirrored placements reflect the cutter, so take the hull again to keep it outward-facing.
    return kit.hull(point + np.outer(local[:, 0], across) + np.outer(local[:, 1], grain) + np.outer(local[:, 2], outward), slot)


# Arms: start (x, z), direction (x, z), length, start open, end open. Closed ends meet the hub post
# at the origin. `opening` is KayKit's door opening (s0, s1 along the arm, height); `taper` slumps
# an arm toward its closed end instead of meeting a hub.
WEST, EAST, NORTH, SOUTH = ((-2.0, 0.0), (1.0, 0.0), 2.0), ((0.0, 0.0), (1.0, 0.0), 2.0), ((0.0, 0.0), (0.0, 1.0), 2.0), ((0.0, 0.0), (0.0, -1.0), 2.0)
DOOR = (1.0, 3.0, 2.8)
PIECES = {
    "wall": {"arms": [((-2.0, 0.0), (1.0, 0.0), 4.0, True, True)]},
    "wall_half": {"arms": [((0.0, 0.0), (1.0, 0.0), 2.0, True, True)]},
    "wall_corner": {"arms": [(*WEST, True, False), (*NORTH, False, True)]},
    "wall_Tsplit": {"arms": [(*WEST, True, False), (*EAST, False, True), (*NORTH, False, True)]},
    "wall_crossing": {"arms": [(*WEST, True, False), (*EAST, False, True), (*NORTH, False, True), (*SOUTH, False, True)]},
    "wall_doorway": {"arms": [((-2.0, 0.0), (1.0, 0.0), 4.0, True, True)], "opening": DOOR},
    "wall_gated": {"arms": [((-2.0, 0.0), (1.0, 0.0), 4.0, True, True)], "opening": DOOR, "bars": True},
    "wall_half_endcap": {"arms": [(*WEST, True, False)], "taper": True},
    "pillar": {"arms": [], "pillar": .75},
    "gate_leaf": {"arms": [], "leaf": True},
}
# The swinging leaf matches KayKit's door leaf: its origin is the hinge, set 0.82 left of the doorway
# center (wall_doorway_door's translation), and it spans x in [-0.18, 1.82] across the 2-unit opening.
LEAF_PIVOT, LEAF_SPAN, LEAF_HEIGHT = -0.82, (-0.16, 1.80), 2.72


def ends(piece):
    """The seam planes of a piece: (axis, value, along) for every open arm end."""
    out = []
    for (ox, oz), (ux, uz), length, start_open, end_open in PIECES[piece]["arms"]:
        for open_, s in ((start_open, 0.0), (end_open, length)):
            if open_:
                x, z = ox + ux * s, oz + uz * s
                out.append((0, x, 2) if ux else (2, z, 0))
    return out


def hub(piece):
    spec = PIECES[piece]
    return not spec.get("taper") and any(not (a[3] and a[4]) for a in spec["arms"])


def post(base, along, half_width, half_depth, design, family, wood, footing, height=HEIGHT):
    """A squared post on a stone footing, its splits mirrored along and across so that any two
    halves of it, cut at its center plane, are the same post."""
    across = np.array([along[2], 0.0, -along[0]])
    up = np.array([0.0, 1.0, 0.0])
    parts = [beam(base, base + up * height, across, half_depth, half_width, wood[0]),
             beam(base, base + up * .34, across, half_depth + .06, half_width + .07, footing, chamfer=.06)]
    f = rng(family, "post-splits")
    marks = [(f.uniform(.03, half_width - .07), f.uniform(.7, 3.5), f.uniform(.22, .45), f.uniform(-7, 7))
             for _ in range(design.get("splits", 3))]
    marks = [m for m in marks if m[1] + m[2] < height - .1] or marks[:1]
    cuts = []
    for lateral_dir, outward_dir, reach, half in ((along, across, half_depth, half_width), (across, along, half_width, half_depth)):
        for side in (1, -1):
            for offset, y, length, angle in marks:
                offset = min(offset, half - .07)
                for mirror in (1, -1):
                    point = base + up * y + outward_dir * side * reach + lateral_dir * mirror * offset
                    cuts.append(split(point, outward_dir * side, lateral_dir * mirror, length, .016, .022, angle, wood[1]))
            # One short branching split off the first, as on the shields.
            offset, y, length, angle = marks[0]
            for mirror in (1, -1):
                point = base + up * (y + length * .3) + outward_dir * side * reach + lateral_dir * mirror * min(offset, half - .07)
                cuts.append(split(point, outward_dir * side, lateral_dir * mirror, length * .45, .011, .016, angle - 18, wood[1]))
    return parts, cuts


def fill(r, origin, along, lo, hi, core, slot, skip):
    """Lumpy rubble packed between the boulders, cracked by a few grooves; `skip(s, y)` leaves gaps."""
    across = np.array([along[2], 0.0, -along[0]])
    lumps, cracks = [], []
    for side in (1, -1):
        for s in np.arange(lo + .2, hi, .65):
            for y in np.arange(.35, 3.5, .55):
                if skip(s, y):
                    continue
                center = origin + along * (s + r.normal(0, .12)) + [0, y + r.normal(0, .08), 0] + across * side * (core - .02)
                lumps.append(boulder(r, center, (r.uniform(.4, .6), r.uniform(.3, .45), r.uniform(.07, .11)), along, slot))
        for _ in range(r.poisson(3 * (hi - lo) / 3.6)):
            angle = r.uniform(-.45, .45)
            s, y = r.uniform(lo + .4, hi - .4), r.uniform(.5, 3.2)
            if hi - lo < .9 or skip(s, y):
                continue
            direction = along * np.cos(angle) + np.array([0, np.sin(angle), 0])
            center = origin + along * s + [0, y, 0]
            length = r.uniform(.3, .7)
            face = across * side * (core + .07)
            cracks.append(groove(center - direction * length / 2, center + direction * length / 2, face, slot, depth=.07, width=.018))
    return lumps, cracks


def gate_leaf(wood, iron):
    """A timber gate leaf in its own frame: origin on the hinge axis, closed along +x. Stiles, three
    rails and bars, with iron straps on the hinge side; the game's Door swings it like KayKit's door."""
    x0, x1 = LEAF_SPAN
    up = np.array([0.0, 1.0, 0.0])
    side = np.array([0.0, 0.0, 1.0])
    parts = [beam([x, .05, 0], [x, LEAF_HEIGHT, 0], side, .07, .07, wood[0], chamfer=.02) for x in (x0 + .07, x1 - .07)]
    rails = (.3, 1.4, LEAF_HEIGHT - .17)
    parts += [beam([x0, y, 0], [x1, y, 0], up, .09, .06, wood[0], chamfer=.02) for y in rails]
    parts += [beam([x, .3, 0], [x, LEAF_HEIGHT - .17, 0], side, .05, .05, wood[0], chamfer=.015)
              for x in np.linspace(x0 + .07, x1 - .07, 5)[1:-1]]
    parts += [beam([x0, y, 0], [x0 + .4, y, 0], up, .065, .075, iron, chamfer=.012) for y in rails[::2]]
    return parts


def doorframe(origin, along, opening, design, family, wood, footing, iron=None):
    """Timber posts and a lintel around KayKit's door opening; with `iron`, a closed gate leaf hangs
    in it exactly where the game's Door would hinge the separate `gate_leaf`."""
    s0, s1, height = opening
    width, depth = design.get("post", [.2, .3])
    across = np.array([along[2], 0.0, -along[0]])
    up = np.array([0.0, 1.0, 0.0])
    parts, cuts = [], []
    for s in (s0 - width, s1 + width):
        timber, splits = post(origin + along * s, along, width, depth, design, family, wood, footing, height=height + .3)
        parts += timber
        cuts += splits
    lintel = origin + up * (height + .17)
    parts.append(beam(lintel + along * (s0 - 2 * width - .15), lintel + along * (s1 + 2 * width + .15), across, depth + .02, .17, wood[0]))
    if iron:
        hinge = origin + along * ((s0 + s1) / 2 + LEAF_PIVOT)
        for leaf in gate_leaf(wood, iron):
            local = leaf.verts
            parts.append(kit.hull(hinge + np.outer(local[:, 0], along) + np.outer(local[:, 1], up) + np.outer(local[:, 2], across),
                                  str(leaf.slots[0])))
    return parts, cuts


def pillar(r, radius, design, family, tones, wood, iron):
    """A free-standing heap of big boulders around a stone core, bound by a bolted timber collar."""
    up = np.array([0.0, 1.0, 0.0])
    parts = [beam(np.zeros(3), up * 3.4, [1.0, 0, 0], .28, .28, tones[2], chamfer=.1)]
    # Big round boulders stacked to fill the footprint, each overlapping the one below.
    y = .45
    while y < 3.3:
        size = r.uniform(.5, .62)
        angle = r.uniform(0, 2 * np.pi)
        along = np.array([np.cos(angle), 0.0, np.sin(angle)])
        width = radius * r.uniform(.95, 1.15)
        parts.append(boulder(r, up * y + r.normal(0, .04, 3) * [1, 0, 1], (width, size, width * .95), along, tones[r.integers(2)]))
        y += size * 1.25
    # A capstone crowns the heap at the wall height.
    parts.append(boulder(r, up * 3.55, (radius * .85, .5, radius * .8), np.array([1.0, 0, 0]), tones[r.integers(2)]))
    sides, ring, band = 8, radius * .86, design.get("collar", 2.1)
    corners = [np.array([np.cos(a), 0.0, np.sin(a)]) * ring for a in (np.arange(sides) + .5) * 2 * np.pi / sides]
    for a, b in zip(corners, corners[1:] + corners[:1]):
        mid = (a + b) / 2
        parts.append(beam(a * 1.02 + up * band, b * 1.02 + up * band, up, .17, .07, wood[0], chamfer=.02))
        parts.append(beam(mid * 1.06 + up * band, mid * 1.16 + up * band, up, .035, .035, iron, chamfer=.012))
    parts.append(beam(up * (band - .17), up * (band + .17), [1.0, 0, 0], ring - .02, ring - .02, tones[2], chamfer=.2))
    return parts


def cave(design, piece, variant, family):
    """Core, posts on the open ends (and the hub), rubble fill and big boulders stacked between."""
    r = rng(family, variant, piece, "cave")
    tones = list(design.get("tones", ["rock", "rock_light", "rock_dark"]))
    wood = list(design.get("wood_tones", ["wood", "wood_cut"]))
    footing, iron = design.get("footing_tone", "footing"), design.get("iron_tone", "iron")
    core = design.get("core", .22)
    width, depth = design.get("post", [.2, .3])
    spec = PIECES[piece]
    if spec.get("leaf"):
        return union(gate_leaf(wood, iron))
    if spec.get("pillar"):
        radius = spec["pillar"]
        parts = pillar(r, radius, design, family, tones, wood, iron)
        bounds = [beam(np.zeros(3), [0, HEIGHT, 0], [1.0, 0, 0], radius, radius, tones[0], chamfer=0.0)]
        return union(parts, within=bounds)
    parts, cuts, bounds = [], [], []
    if hub(piece):
        timber, splits = post(np.zeros(3), np.array([1.0, 0, 0]), depth, depth, design, family, wood, footing)
        parts += timber
        cuts += splits
    for (ox, oz), (ux, uz), length, start_open, end_open in spec["arms"]:
        along, origin = np.array([ux, 0.0, uz]), np.array([ox, 0.0, oz])
        across = np.array([uz, 0.0, -ux])
        taper = spec.get("taper")
        closed_start, closed_end = not start_open, not end_open and not taper
        bounds.append(slab(origin, along, -HALF_THICK if closed_start else 0.0, length + (HALF_THICK if closed_end else 0.0),
                           HALF_THICK, HEIGHT, wood[0]))
        top = (lambda s: 3.6) if not taper else (lambda s: 3.6 - 3.0 * np.clip((s - .5) / (length - .5), 0, 1))
        a, b = -core if closed_start else 0.0, length + (core if closed_end else 0.0)
        opening = spec.get("opening")
        # The core is open below the lintel, so the passage needs no cut that would take the gate bars too.
        spans = [(a, b, 0.0)] if not opening else [(a, opening[0], 0.0), (opening[1], b, 0.0), (opening[0], opening[1], opening[2] + .3)]
        for c0, c1, floor in spans:
            parts.append(kit.hull(np.array([origin + along * s_ + side * across * core + [0, y, 0]
                                            for s_ in (c0, c1) for side in (1, -1) for y in (floor, top(s_))]), tones[2]))
        for open_, s_end in ((start_open, 0.0), (end_open, length)):
            if open_:
                timber, splits = post(origin + along * s_end, along, width, depth, design, family, wood, footing)
                parts += timber
                cuts += splits
        lo = width + .05 if start_open else depth + .05
        hi = length - (width + .05 if end_open else (0.05 if taper else depth + .05))
        if opening:
            frame, passage = doorframe(origin, along, opening, design, family, wood, footing, iron if spec.get("bars") else None)
            parts += frame
            cuts += passage
            clear = (opening[0] - 2 * width - .1, opening[1] + 2 * width + .1, opening[2] + .3)
        else:
            clear = None

        def skip(s, y, half=0.0):
            if y > top(s) + .1:
                return True
            return clear is not None and s + half > clear[0] and s - half < clear[1] and y < clear[2]

        lumps, cracks = fill(r, origin, along, lo, hi, core, tones[2], skip)
        parts += lumps
        cuts += cracks
        # Big stacked boulders, biggest at the foot, rounded inside the half-unit depth, between posts.
        for y, size in design.get("rows", [[.75, .82], [2.05, .74], [3.25, .55]]):
            for side in (1, -1):
                s, room = lo + r.uniform(0, .2), hi - lo
                while room > .3:
                    rs = min(size * r.uniform(.9, 1.3), room / 2)
                    center_s = s + rs
                    scale = min(1.0, max(.35, (top(center_s) - y + size) / (2 * size))) if taper else 1.0
                    if not skip(center_s, y, rs) and y - size * scale < top(center_s):
                        center = origin + along * center_s + [0, min(y, top(center_s)) + r.normal(0, .08), 0] + across * side * r.uniform(.08, .12)
                        parts.append(boulder(r, center, (rs, size * scale * r.uniform(.9, 1.1), .38 * max(scale, .7)), along,
                                             tones[r.integers(2)]))
                    step = rs * r.uniform(1.3, 1.6)
                    s, room = s + step, room - step
        if taper:
            # Rubble spills past the slumped end onto the floor.
            for _ in range(r.integers(5, 9)):
                s_ = r.uniform(length * .55, length - .15)
                rs = r.uniform(.15, .32)
                center = origin + along * s_ + [0, rs * .4, 0] + across * r.uniform(-.25, .25)
                parts.append(boulder(r, center, (rs, rs * .8, rs), along, tones[r.integers(2)]))
    # Cutting to the footprint trims everything to the wall's half-unit depth and halves the end posts.
    return union(parts, cuts=cuts, within=bounds)


def slab(origin, along, s0, s1, half, top, slot):
    across = np.array([along[2], 0.0, -along[0]]) * half
    a, b = origin + along * s0, origin + along * s1
    points = np.array([p + [0, y, 0] for p in (a + across, a - across, b + across, b - across) for y in (0.0, top)])
    return kit.hull(points, slot)


STYLES = {"cave": cave}
