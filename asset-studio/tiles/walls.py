"""Wall tile builders on the KayKit footprint: 4 tall, 1 thick, origin at the base of the path.

`wall` spans x in [-2, 2], `wall_half` x in [0, 2] and `wall_corner` is the KayKit L (x in
[-2, 0.5], z in [-0.5, 2]). A mortar core carries coursed face stones on both sides, a plinth and
a jointed cap. Course heights are fixed; stone joints stagger per variant. Stones and cap blocks
crossing a piece end are cut flush with properties from their course alone, so every end shows
the same cross-section, front and back alike, and pieces join in any order or quarter turn.
"""

import numpy as np

from tiles.geometry import block, rect, rng, union

HEIGHT, HALF_THICK = 4.0, 0.5
# Path segments: start (x, z), direction (x, z), length.
PIECES = {
    "wall": [((-2.0, 0.0), (1.0, 0.0), 4.0)],
    "wall_half": [((0.0, 0.0), (1.0, 0.0), 2.0)],
    "wall_corner": [((-2.0, 0.0), (1.0, 0.0), 2.0), ((0.0, 0.0), (0.0, 1.0), 2.0)],
}


def courses(design):
    """Fixed course boundaries shared by every piece and variant of a family."""
    plinth, cap = design.get("plinth", .3), design.get("cap", .45)
    edges = np.linspace(plinth, HEIGHT - cap, design.get("courses", 7) + 1)
    return list(zip(edges[:-1], edges[1:]))


def stagger(r, start, end, open_start, open_end, design, previous, forced=()):
    """Joint positions along one course of a face; open ends are piece seams that joints avoid."""
    lo, hi = design.get("min_length", .55), design.get("max_length", 1.3)
    margin, spacing = design.get("margin", .3), design.get("stagger", .22)
    best = None
    for _ in range(40):
        u = start + (r.uniform(margin, hi * .8) if open_start else r.uniform(lo, hi))
        joints = []
        while u <= end - (margin if open_end else lo * .6):
            if all(abs(u - f) >= lo * .6 for f in forced):
                joints.append(u)
            u += r.uniform(lo, hi)
        joints = sorted(joints + list(forced))
        best = joints
        if all(abs(a - b) >= spacing for a in joints for b in previous):
            break
    return best


class Face:
    """One continuous masonry face: spans joined by outer corners, unfolded into one coordinate."""

    def __init__(self, spans, open_start, open_end):
        # span: (origin (x, z), along (x, z), outward normal (x, z), s_lo, s_hi)
        self.spans, self.open_start, self.open_end = spans, open_start, open_end
        self.starts = np.cumsum([0.0] + [s[4] - s[3] for s in spans])

    @property
    def length(self):
        return float(self.starts[-1])

    def corners(self):
        return [float(u) for u in self.starts[1:-1]]

    def pieces(self, u0, u1, reach):
        """Map an unfolded interval to (span, s0, s1, cut_lo, cut_hi); stones wrap outer corners by `reach`."""
        out = []
        for i, (origin, along, normal, s_lo, s_hi) in enumerate(self.spans):
            a, b = max(u0, self.starts[i]), min(u1, self.starts[i + 1])
            if b - a <= 1e-9:
                continue
            wraps_lo = i > 0 and u0 < self.starts[i] - 1e-9
            wraps_hi = i < len(self.spans) - 1 and u1 > self.starts[i + 1] + 1e-9
            s0 = s_lo + a - self.starts[i] - (reach if wraps_lo else 0.0)
            s1 = s_lo + b - self.starts[i] + (reach if wraps_hi else 0.0)
            seam_lo = i == 0 and self.open_start and abs(u0 - self.starts[0]) < 1e-9
            seam_hi = i == len(self.spans) - 1 and self.open_end and abs(u1 - self.length) < 1e-9
            out.append((i, s0, s1, wraps_lo or seam_lo, wraps_hi or seam_hi))
        return out


def faces(piece, core, face):
    """Both faces of a piece. The corner's outer face wraps; its inner faces stop at the inner corner."""
    segments = PIECES[piece]

    def span(seg, side, s_lo, s_hi, depth):
        (ox, oz), (ux, uz), _ = seg
        normal = np.array([uz, -ux]) * side
        return (np.array([ox, oz]) + normal * depth, np.array([ux, uz]), normal, s_lo, s_hi)

    if len(segments) == 1:
        length = segments[0][2]
        return [Face([span(segments[0], side, 0.0, length, core)], True, True) for side in (1, -1)]
    a, b = segments
    outer = Face([span(a, 1, 0.0, a[2] + core, core), span(b, 1, -core, b[2], core)], True, True)
    inner_a = Face([span(a, -1, 0.0, a[2] - face, core)], True, False)
    inner_b = Face([span(b, -1, face, b[2], core)], False, True)
    return [outer, inner_a, inner_b]


def masonry(design, piece, variant, family):
    """Mortar core, plinth, coursed face stones and a jointed cap, as one solid union."""
    core, face = design.get("core", .38), design.get("face", .47)
    bevel, gap = design.get("bevel", .03), design.get("gap", .03)
    shades = list(design.get("tones", ["stone", "stone_light", "stone_dark"]))
    jitter, tilt = design.get("jitter", .015), design.get("tilt", .02)
    plinth_h, cap_h = design.get("plinth", .3), design.get("cap", .45)
    r = rng(family, variant, piece, "masonry")
    course_tone = rng(family, "course-tones").integers(len(shades), size=64)
    segments = PIECES[piece]
    assembly = []
    for index, segment in enumerate(segments):
        # The corner's first arm reaches through the turn so both cores overlap there.
        reach = core if len(segments) > 1 and index == 0 else 0.0
        assembly.append(block(strip(segment, 0.0, segment[2] + reach, core), [False] * 4, HEIGHT - cap_h + .05,
                           plinth_h - .05, design.get("core_slot", "mortar"), bevel=0.0, gap=0.0))
    footprint(assembly, segments, 0.0, plinth_h, design.get("plinth_slot", "plinth"), bevel)
    cap(assembly, r, segments, design, family)

    for f in faces(piece, core, face):
        previous = []
        for k, (y0, y1) in enumerate(courses(design)):
            long, short = design.get("quoin", [.75, .4])
            quoins = [u + d for u in f.corners() for d in ((-short, long) if k % 2 else (-long, short))]
            joints = stagger(r, 0.0, f.length, f.open_start, f.open_end, design, previous, quoins)
            previous = joints
            ends = [0.0, *joints, f.length]
            for i in range(len(ends) - 1):
                u0, u1 = ends[i], ends[i + 1]
                crossing = (i == 0 and f.open_start) or (i == len(ends) - 2 and f.open_end)
                wraps = any(u0 < c < u1 for c in f.corners())
                if crossing:
                    tone, top, lean = shades[course_tone[k]], face, (0.0, 0.0)
                else:
                    tone = shades[r.integers(len(shades))]
                    lean = (r.uniform(-tilt, tilt) / max(u1 - u0, .3), r.uniform(-tilt, tilt) / (y1 - y0))
                    # A leaning stone must stay inside the wall's half-unit footprint at every corner.
                    rise = abs(lean[0]) * (u1 - u0) / 2 + abs(lean[1]) * (y1 - y0) / 2
                    top = min(face + r.uniform(-jitter, 0), HALF_THICK - rise)
                for span_index, s0, s1, cut_lo, cut_hi in f.pieces(u0, u1, face - core):
                    origin, along, normal, _, _ = f.spans[span_index]

                    def place(points, origin=origin, along=along, normal=normal):
                        world = origin[None, :] + points[:, [0]] * along[None, :] + points[:, [1]] * normal[None, :]
                        return np.column_stack([world[:, 0], points[:, 2], world[:, 1]])

                    stone = block(rect(s0, y0, s1, y1), [True, not cut_hi, True, not cut_lo], top - core, -.04,
                                  tone, bevel=bevel, gap=gap, tilt=lean if not wraps else (0.0, 0.0),
                                  lift=((s0 + s1) / 2, (y0 + y1) / 2), place=place)
                    if stone is not None:
                        assembly.append(stone)
    return union(assembly)


def strip(segment, a, b, half_width):
    """Footprint of the path interval [a, b] widened to both sides, counter-clockwise in (x, z)."""
    (ox, oz), (ux, uz), _ = segment
    along, normal = np.array([ux, uz]), np.array([uz, -ux]) * half_width
    start, end = np.array([ox, oz]) + along * a, np.array([ox, oz]) + along * b
    return np.array([start + normal, end + normal, end - normal, start - normal])


def footprint(assembly, segments, y0, y1, slot, bevel):
    """A continuous band over the whole wall footprint (the plinth), beveled along its outer edges."""
    corner = len(segments) > 1
    for index, segment in enumerate(segments):
        a = -HALF_THICK if corner and index == 1 else 0.0
        b = segment[2] + (HALF_THICK if corner and index == 0 else 0.0)
        open_start, open_end = not (corner and index == 1), not (corner and index == 0)
        assembly.append(block(strip(segment, a, b, HALF_THICK), [True, not open_end, True, not open_start],
                           y1, y0, slot, bevel=bevel, gap=0.0))


def cap(assembly, r, segments, design, family):
    """Cap blocks along the path; the block crossing a piece end has a fixed length and height."""
    cap_h, bevel, gap = design.get("cap", .45), design.get("bevel", .03) * 1.4, design.get("gap", .03)
    lo, hi = design.get("cap_lengths", [.8, 1.3])
    drop = design.get("cap_drop", .03)
    shades = list(design.get("cap_tones", ["cap", "cap_light", "cap_dark"]))
    seam_tone = shades[rng(family, "cap-seam").integers(len(shades))]
    corner = len(segments) > 1
    y0 = HEIGHT - cap_h
    for index, segment in enumerate(segments):
        length = segment[2]
        start = 0.0 if not (corner and index == 1) else HALF_THICK
        end = length if not (corner and index == 0) else length - HALF_THICK
        cuts, u = [], start + r.uniform(.3, hi * .7)
        while u < end - .3:
            cuts.append(u)
            u += r.uniform(lo, hi)
        ends = [start, *cuts, end]
        for i in range(len(ends) - 1):
            a, b = ends[i], ends[i + 1]
            open_lo = i == 0 and not (corner and index == 1)
            open_hi = i == len(ends) - 2 and not (corner and index == 0)
            poly = strip(segment, a, b, HALF_THICK)
            seam = open_lo or open_hi
            top = HEIGHT if seam else HEIGHT - r.uniform(0, drop)
            tone = seam_tone if seam else shades[r.integers(len(shades))]
            assembly.append(block(poly, [True, not open_hi, True, not open_lo], top, y0 - .02, tone, bevel=bevel, gap=gap))
    if corner:
        square = rect(-HALF_THICK, -HALF_THICK, HALF_THICK, HALF_THICK)
        assembly.append(block(square, [True] * 4, HEIGHT, y0 - .02, shades[r.integers(len(shades))], bevel=bevel, gap=gap))


STYLES = {"masonry": masonry}
