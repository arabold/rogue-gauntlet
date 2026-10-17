"""
Plate armor for the torso, shaped like stylized's: every plate is one closed shell whose edges roll up into rounded
rims colored as trim (the plate's own faces, never a band laid on top of it), sculpted (pecs, cups, a keel) by
displacing that same surface, and riveted along the crests of its rims. Plates that overlap (a chest plate over
a belly plate) are separate shells, the way real armor is built from separate plates.

`plate` builds one plate from its outline: a bottom and a top edge (numbers or functions of `u`, -1 .. 1 across
the plate, +1 the character's left), a half width in degrees that can curve in toward the top (an armhole), the
edges that roll into rims, rivets per rim and an outward `flare` toward the bottom (a skirt). `ridge` raises a line
across a plate and colors it (a corset's seams). `VARIANTS` builds whole cuirasses:

- `knight`: a chest plate with a round neckline, a keel and an upward chevron over a belly plate tucked under it,
  scooped armholes, a matching back plate, and a skirt flaring below the belt with a notch in front; gold rims
  studded with rivets.
- `corset`: a sorceress's fitted plate, two cups, a pointed waist, trimmed seams down the panels and under the
  cups, laced sides and a skull clasp at the neckline.
- `orc`: crude dark iron: uneven plates bolted together with big rivets, a brutish muscle sculpt, jagged edges, a
  skull boss and leather straps over the shoulders.

    python asset-studio/studio.py catalog cuirass -o cuirass.png --yaws 0,-40,-90,180
"""

import numpy as np

from core import kit
from prefabs.outfits.armor import FRONT, BACK, _surface

RIM_STEPS = (0.0, 0.25, 0.5, 0.75, 1.0)  # across a rim: its roll is sin(pi d / rim), so it rises and falls rounded


def _value(f, u):
    return f(u) if callable(f) else np.full(np.shape(u), float(f))


def _smooth(x):
    x = np.clip(x, 0.0, 1.0)
    return x * x * (3 - 2 * x)


def plate(torso, bottom, top, side="front", width=80.0, width_top=None, armhole=0.62, sculpt=None, thickness=0.05,
          depth=0.03, slot="armor", trim="trim", rims=("top", "bottom", "sides"), rim=0.045, rim_height=0.024,
          rivets=None, rivet_slot="rivet", rivet_size=0.024, flare=0.0, rows=9, cols=13, bolts=0, seed=0):
    """
    One plate on the `side` ("front" or "back") of `torso`, between its `bottom` and `top` edges (rig heights, or
    functions of `u`), `width` degrees to either side, curving in to `width_top` above `armhole` of its height.
    It stands `thickness` off the torso and is `depth` thick; `flare` pushes it further out toward the bottom.
    `rims`: the edges ("top", "bottom", "left", "right" or "sides") rolled into a rounded rim `rim` wide (rig
    units) and `rim_height` high, colored `trim`; `rivets`: `{edge: count}` along the crests of those rims.
    `sculpt(u, t)` pushes the plate forward (a share of the torso's half width; `t` 0 .. 1 bottom to top). `bolts`
    scatters that many more rivets over the plate's face (an orc's bolted plates).
    """
    sides = {"left", "right"} if "sides" in rims else set()
    rims = set(rims) | sides
    fwd = np.array([0.0, 0.0, 1.0 if side == "front" else -1.0])
    centre, sgn_side = (FRONT, 1.0) if side == "front" else (BACK, -1.0)
    W0 = np.radians(width)
    W1 = np.radians(width if width_top is None else width_top)
    y_mid = 0.5 * float(np.mean(_value(bottom, np.zeros(1)))) + 0.5 * float(np.mean(_value(top, np.zeros(1))))
    _, a_mid, _, b_mid = (float(v[0]) for v in torso.section([y_mid]))
    radius = 0.5 * (a_mid + b_mid)  # rig units per radian round the torso, near enough for rim widths

    def edge_rows(edge):
        return [rim * k for k in RIM_STEPS] if edge in rims else [0.0]

    lo, hi = edge_rows("bottom"), edge_rows("top")
    rspec = [("lo", d) for d in lo] + [("mid", f) for f in np.linspace(0, 1, rows)[1:-1]] + [("hi", d) for d in hi[::-1]]
    rr, ll = edge_rows("right"), edge_rows("left")
    cspec = [("r", d) for d in rr] + [("mid", f) for f in np.linspace(0, 1, cols)[1:-1]] + [("l", d) for d in ll[::-1]]
    wb, wt = lo[-1], hi[-1]
    wr, wl = rr[-1], ll[-1]

    def half_width(t):
        return W0 + (W1 - W0) * _smooth((t - armhole) / max(1 - armhole, 1e-6)) ** 1.3

    def locate(cs, rs):
        """(y, offset angle, u, t, rim roll) of a column spec and a row spec."""
        kind, v = cs
        u_nom = {"r": -1 + v / (W0 * radius), "l": 1 - v / (W0 * radius)}.get(kind)
        if u_nom is None:
            u_nom = -1 + wr / (W0 * radius) + v * (2 - (wr + wl) / (W0 * radius))
        yb, yt = float(_value(bottom, np.array([u_nom]))[0]), float(_value(top, np.array([u_nom]))[0])
        rk, rv = rs
        if rk == "lo":
            y = yb + rv
        elif rk == "hi":
            y = yt - rv
        else:
            y = (yb + wb) + rv * ((yt - wt) - (yb + wb))
        t = (y - yb) / max(yt - yb, 1e-6)
        W = half_width(t)
        r_y = 0.5 * sum(float(x[0]) for x in np.asarray(torso.section([y]))[[1, 3]])
        if kind == "r":
            off = -(W - v / r_y)
        elif kind == "l":
            off = W - v / r_y
        else:
            off = -(W - wr / r_y) + v * (2 * W - (wr + wl) / r_y)
        roll = 0.0
        if rk in ("lo", "hi") and rv < rim and (("bottom" if rk == "lo" else "top") in rims):
            roll = max(roll, np.sin(np.pi * rv / rim))
        if kind in ("r", "l") and v < rim and ({"r": "right", "l": "left"}[kind] in rims):
            roll = max(roll, np.sin(np.pi * v / rim))
        return y, off, off / W, t, roll

    def point(cs, rs, extra=0.0, inner=False):
        y, off, u, t, roll = locate(cs, rs)
        base = thickness + flare * (1 - t) ** 2 + extra
        out = base - depth if inner else base + rim_height * roll
        P = _surface(torso, y, centre - sgn_side * off, out)
        push = float(sculpt(np.array([u]), np.array([t]))[0]) * a_mid if sculpt is not None else 0.0
        return np.asarray(P, float) + push * fwd

    outer = np.array([[point(c, r) for c in cspec] for r in rspec])
    inner = np.array([[point(c, r, inner=True) for c in cspec] for r in rspec])
    shell = kit.patch_shell(outer, inner, slot)

    # colors: a cell is trim when it lies in a rim band; walls and caps follow the cell beside them
    R, C = len(rspec), len(cspec)
    cell = np.full((R - 1, C - 1), slot, object)
    nb, nt = len(lo) - 1, len(hi) - 1
    nr, nl = len(rr) - 1, len(ll) - 1
    if "bottom" in rims:
        cell[:nb, :] = trim
    if "top" in rims:
        cell[R - 1 - nt:, :] = trim
    if "right" in rims:
        cell[:, :nr] = trim
    if "left" in rims:
        cell[:, C - 1 - nl:] = trim
    n = 2 * R
    slots = np.full(len(shell.faces), slot, object)
    k_face = np.arange((C - 1) * 2 * n)
    col, k = k_face // (2 * n), (k_face % (2 * n)) // 2
    outer_cell = k < R - 1
    slots[k_face[outer_cell]] = cell[k[outer_cell], col[outer_cell]]
    slots[k_face[k == R - 1]] = cell[R - 2, col[k == R - 1]]       # the top edge's wall
    slots[k_face[k == 2 * R - 1]] = cell[0, col[k == 2 * R - 1]]   # the bottom edge's wall
    caps = np.arange((C - 1) * 2 * n, len(shell.faces))
    half = len(caps) // 2
    if "right" in rims:
        slots[caps[:half]] = trim
    if "left" in rims:
        slots[caps[half:]] = trim
    parts = [kit.Piece(shell.verts, shell.faces, slots)]

    for edge, count in (rivets or {}).items():  # rivets along the rims' crests
        if not count or edge not in rims:
            continue
        for f in (np.arange(count) + 0.5) / count:
            if edge in ("bottom", "top"):
                cs, rs = ("mid", 0.04 + 0.92 * f), ("lo" if edge == "bottom" else "hi", 0.5 * rim)
            else:
                cs, rs = ("r" if edge == "right" else "l", 0.5 * rim), ("mid", 0.06 + 0.88 * f)
            p0 = point(cs, rs)
            du = (("mid", min(cs[1] + 0.01, 1.0)) if cs[0] == "mid" else (cs[0], cs[1] + 0.003))
            dv = (("mid", min(rs[1] + 0.01, 1.0)) if rs[0] == "mid" else (rs[0], rs[1] + 0.003))
            nrm = np.cross(point(du, rs) - p0, point(cs, dv) - p0)
            nrm /= max(np.linalg.norm(nrm), 1e-12)
            if nrm @ (p0 - [0.0, p0[1], 0.0]) < 0:
                nrm = -nrm
            parts.append(_rivet(p0, nrm, rivet_size, rivet_slot))
    if bolts:  # scattered over the face, on a jittered grid so they never bunch up
        rng = np.random.default_rng(seed)
        n = int(np.ceil(np.sqrt(bolts)))
        cells = rng.permutation(n * n)[:bolts]
        for c_ in cells:
            fu, ft = (c_ % n + rng.uniform(0.25, 0.75)) / n, (c_ // n + rng.uniform(0.25, 0.75)) / n
            cs, rs = ("mid", 0.1 + 0.8 * fu), ("mid", 0.12 + 0.76 * ft)
            p0 = point(cs, rs)
            nrm = np.cross(point(("mid", cs[1] + 0.01), rs) - p0, point(cs, ("mid", rs[1] + 0.01)) - p0)
            nrm /= max(np.linalg.norm(nrm), 1e-12)
            if nrm @ (p0 - [0.0, p0[1], 0.0]) < 0:
                nrm = -nrm
            parts.append(_rivet(p0, nrm, rivet_size, rivet_slot))
    piece = kit.Piece.merge(*parts)
    piece.surface_at = lambda u, t: point(("mid", 0.5 * (u + 1)), ("mid", t))  # for ridges, clasps, bosses
    return piece


def _rivet(p, n, size, slot):
    """A domed rivet head sitting on the surface at `p` facing `n`, its lower half sunk in."""
    dome = kit.lump((0.0, 0.0, 0.0), (size, size, 0.75 * size), slot, n=8, lat=(-30, 0, 35, 70))
    return dome.moved(kit.Frame.make(p, n))


def ridge(piece, path, width, height, slot, side="front"):
    """`piece` (a front `plate`) with a raised seam along `path` [(u, t), ...]: its surface lifted into a rounded
    bead `height` high and `width` wide (rig units) and the bead's faces recolored `slot` (cut along its outline in
    the front view). Nothing is laid on top."""
    pts = np.array([piece.surface_at(u, t) for u, t in kit.resample(np.asarray(path, float), 0.04)])
    V = piece.verts.copy()
    xy = V[:, :2]
    d = np.full(len(V), np.inf)
    for a, b in zip(pts[:-1, :2], pts[1:, :2]):
        ab = b - a
        tt = np.clip(((xy - a) @ ab) / max(ab @ ab, 1e-12), 0, 1)
        d = np.minimum(d, np.linalg.norm(xy - (a + tt[:, None] * ab), axis=1))
    front = V[:, 2] > np.median(V[:, 2]) - 0.01 if side == "front" else V[:, 2] < np.median(V[:, 2]) + 0.01
    lift = np.where(front & (d < width), height * np.cos(0.5 * np.pi * np.clip(d / width, 0, 1)) ** 2, 0.0)
    V[:, 2] += lift * (1 if side == "front" else -1)
    polys = []
    for a, b in zip(pts[:-1, :2], pts[1:, :2]):
        t_ = (b - a) / max(np.linalg.norm(b - a), 1e-9)
        nrm = np.array([-t_[1], t_[0]]) * 0.5 * width
        polys.append([a - nrm, b - nrm, b + nrm, a + nrm])
    for p_ in pts[1:-1, :2]:  # round joints
        th = 2 * np.pi * np.arange(10) / 10
        polys.append(p_ + 0.5 * width * np.column_stack([np.cos(th), np.sin(th)]))
    lifted = kit.Piece(V, piece.faces, piece.slots)
    cut = kit.paint_cut(lifted, polys, slot, {s for s in set(piece.slots) if s != slot}, min_facing=0.2)
    cut.surface_at = piece.surface_at
    return cut


# ---------------------------------------------------------------------------
# Variants after the concept designs
# ---------------------------------------------------------------------------
def _neckline(top, depth, half):
    """A round neckline `depth` deep and `half` wide (a share of the plate) in a top edge at `top`."""
    return lambda u: top - depth * np.sqrt(np.clip(1 - (np.asarray(u) / half) ** 2, 0, 1))


def knight(torso, waist=0.66, hem=0.4, thickness=0.05, slot="armor", trim="trim", rivet="rivet"):
    """A knight's cuirass after `parts/breastplate_knight`: chest plate over a belly plate, back plate, skirt."""
    top = torso.top - 0.03
    chev = waist + 0.36 * (top - waist)
    keel = lambda u, t: 0.07 * np.exp(-(u / 0.07) ** 2) * _smooth((t - 0.05) / 0.2) * (1 - _smooth((t - 0.8) / 0.15))  # noqa: E731
    pecs = lambda u, t: 0.06 * (np.exp(-((np.abs(u) - 0.42) / 0.3) ** 2 - ((t - 0.55) / 0.3) ** 2)) + keel(u, t)  # noqa: E731
    chest = plate(torso, lambda u: chev + 0.12 * (1 - np.abs(u)), _neckline(top, 0.1, 0.42), "front", 84.0, 48.0,
                  armhole=0.32, sculpt=pecs, thickness=thickness, slot=slot, trim=trim,
                  rivets={"top": 7, "bottom": 7, "left": 4, "right": 4}, rivet_slot=rivet, rows=10, cols=15)
    belly = plate(torso, waist - 0.05, lambda u: chev + 0.12 * (1 - np.abs(u)) + 0.04, "front", 84.0,
                  thickness=thickness - 0.012, slot=slot, trim=trim, rims=("sides",), rows=5, cols=13)
    back = plate(torso, waist - 0.05, _neckline(top, 0.04, 0.5), "back", 84.0, 50.0, armhole=0.35,
                 sculpt=lambda u, t: 0.05 * np.exp(-((np.abs(u) - 0.4) / 0.32) ** 2 - ((t - 0.7) / 0.25) ** 2),
                 thickness=thickness, slot=slot, trim=trim, rivets={"top": 6, "left": 4, "right": 4},
                 rims=("top", "sides"), rivet_slot=rivet, rows=10, cols=15)
    notch = lambda u: hem + 0.12 * _smooth((0.26 - np.abs(np.asarray(u))) / 0.06)  # noqa: E731
    skirt = [plate(torso, notch if s == "front" else hem, waist - 0.02, s, 90.0, flare=0.14,
                   thickness=thickness - 0.005, slot=slot, trim=trim, rims=("bottom", "sides"),
                   rivets={"bottom": 6}, rivet_slot=rivet, rows=6, cols=15) for s in ("front", "back")]
    return kit.Piece.merge(chest, belly, back, *skirt)


def corset(torso, waist=0.66, thickness=0.04, slot="corset", trim="corset_trim", lace="lace", clasp="bone"):
    """A sorceress's corset plate after `parts/breastplate_female`: cups, a pointed waist, trimmed seams, laced
    sides, a skull clasp at the neckline."""
    from prefabs.outfits.belts import skull

    top = torso.top - 0.05
    bottom = waist - 0.12
    cups = lambda u, t: 0.36 * np.exp(-((np.abs(u) - 0.36) / 0.22) ** 2 - ((t - 0.68) / 0.15) ** 2)  # noqa: E731
    front = plate(torso, lambda u: bottom - 0.08 * (1 - np.abs(u)) ** 1.5, _neckline(top, 0.09, 0.36), "front",
                  70.0, 50.0, armhole=0.55, sculpt=cups, thickness=thickness, depth=0.025, slot=slot, trim=trim,
                  rim=0.035, rim_height=0.018, rows=12, cols=17)
    for sgn in (-1, 1):  # the line under each cup, and the panel seams from it down to the waist
        front = ridge(front, [(sgn * 0.04, 0.56), (sgn * 0.2, 0.47), (sgn * 0.4, 0.45), (sgn * 0.62, 0.5),
                              (sgn * 0.74, 0.58)], 0.03, 0.012, trim)
        front = ridge(front, [(sgn * 0.36, 0.45), (sgn * 0.3, 0.04)], 0.03, 0.012, trim)
        front = ridge(front, [(sgn * 0.7, 0.53), (sgn * 0.62, 0.05)], 0.03, 0.012, trim)
    back = plate(torso, bottom, top - 0.02, "back", 70.0, 50.0, armhole=0.55, thickness=thickness, depth=0.025,
                 slot=slot, trim=trim, rim=0.035, rim_height=0.018, rows=8, cols=13)
    parts = [front, back]
    for sgn in (-1, 1):  # laced sides: cords crossing the gap between the plates
        for k in range(4):
            y = bottom + 0.06 + k * (top - bottom - 0.2) / 4
            pa = _surface(torso, y, FRONT - sgn * np.radians(72.0), thickness)
            pb = _surface(torso, y + 0.06, BACK + sgn * np.radians(72.0), thickness)
            pc = _surface(torso, y + 0.06, FRONT - sgn * np.radians(72.0), thickness)
            pd = _surface(torso, y, BACK + sgn * np.radians(72.0), thickness)
            for p_, q_ in ((pa, pb), (pc, pd)):
                parts.append(kit.sweep(np.array([p_, q_]), kit.ring_profile(0.008, 0.008, 6), lace, up=(0.0, 1.0, 0.0)))
    c = front.surface_at(0.0, 0.96)
    parts.append(skull(0.07, clasp).moved(kit.Frame.make(c + [0.0, -0.02, 0.01], (0.0, 0.0, 1.0))))
    return kit.Piece.merge(*parts)


def orc(torso, waist=0.66, thickness=0.06, slot="iron", rim="iron_dark", rivet="rivet_iron", strap="leather",
        boss="bone", seed=3):
    """Crude orc plate after `parts/breastplate_orc`: uneven riveted plates bolted together over a brute's chest,
    jagged edges, a skull boss, leather straps over the shoulders."""
    from prefabs.outfits.belts import skull

    rng = np.random.default_rng(seed)
    top = torso.top - 0.05
    mid = waist + 0.5 * (top - waist)
    jag = lambda base, amp: (lambda u: base + amp * np.interp(u, np.linspace(-1, 1, 9), rng.uniform(-1, 1, 9)))  # noqa: E731
    muscle = lambda u, t: 0.08 * np.exp(-((np.abs(u) - 0.4) / 0.32) ** 2 - ((t - 0.5) / 0.32) ** 2)  # noqa: E731
    pieces = []
    for sgn in (-1, 1):  # two upper chest plates meeting in the middle, overlapping a little
        pieces.append(plate(torso, jag(mid - 0.04, 0.025), jag(top, 0.02),
                            "front", 44.0, 34.0, armhole=0.5, sculpt=lambda u, t: muscle(0.5 * (u + sgn), t),
                            thickness=thickness + (0.008 if sgn > 0 else 0.0), depth=0.035, slot=slot, trim=rim,
                            rim=0.03, rim_height=0.012, rivets={"bottom": 2, "top": 2}, rivet_slot=rivet,
                            rivet_size=0.034, rows=7, cols=8, bolts=3, seed=seed + (sgn > 0))
                      .moved(_turn(sgn * 21.5)))
    pieces.append(plate(torso, jag(waist - 0.06, 0.03), jag(mid + 0.02, 0.02), "front", 86.0, thickness=thickness - 0.01,
                        depth=0.035, slot=slot, trim=rim, rim=0.03, rim_height=0.012,
                        rivets={"bottom": 4, "left": 2, "right": 2}, rivet_slot=rivet, rivet_size=0.034, rows=6, cols=13,
                        bolts=5, seed=seed + 2))
    pieces.append(plate(torso, waist - 0.06, top - 0.02, "back", 84.0, 66.0, armhole=0.55, thickness=thickness,
                        depth=0.035, slot=slot, trim=rim, rim=0.03, rim_height=0.012, rivets={"top": 4},
                        rivet_slot=rivet, rivet_size=0.034, rows=8, cols=13, bolts=7, seed=seed + 3))
    for sgn in (-1, 1):  # leather straps from the chest up over each shoulder and down the back
        for s_, centre in (("front", FRONT), ("back", BACK)):
            off = np.radians(26.0) * (sgn if s_ == "front" else -sgn)
            pieces.append(_strap(torso, mid + 0.06, top + 0.04, centre - off, 0.075, thickness + 0.035, strap))
    c = _surface(torso, mid - 0.04, FRONT, thickness - 0.02)  # a big skull boss sunk into the middle of the chest
    pieces.append(skull(0.27, boss).moved(kit.Frame.make(np.asarray(c, float), (0.0, 0.0, 1.0))))
    return kit.Piece.merge(*pieces)


def _strap(torso, y0, y1, theta, width, out, slot, depth=0.02):
    """A leather strap running up the torso from `y0` to `y1` at the angle `theta`, `width` wide, `out` off it."""
    y = np.linspace(y0, y1, 8)
    r = 0.5 * sum(float(v[0]) for v in np.asarray(torso.section([0.5 * (y0 + y1)]))[[1, 3]])
    th = theta + np.array([-0.5, 0.5]) * width / r
    Y, T = np.meshgrid(y, th, indexing="ij")
    return kit.patch_shell(_surface(torso, Y, T, out), _surface(torso, Y, T, out - depth), slot)


def _turn(deg):
    """A turn about the torso's vertical axis (the two orc chest plates are built centred, then swung to a side)."""
    R = kit.rotation((0.0, 1.0, 0.0), deg)
    return kit.Frame(np.zeros(3), *R.T)


VARIANTS = {"knight": knight, "corset": corset, "orc": orc}
COLORS = {"armor": (178, 186, 200), "trim": (226, 178, 64), "rivet": (236, 196, 90), "corset": (44, 36, 56),
          "corset_trim": (150, 96, 196), "lace": (120, 70, 170), "bone": (232, 222, 196), "iron": (78, 74, 78),
          "iron_dark": (56, 52, 56), "rivet_iron": (150, 146, 150), "leather": (116, 74, 50)}


def build(torso, variant="knight", **kw):
    return VARIANTS[variant](torso, **kw)


def catalog():
    """Each variant on a plain torso."""
    from prefabs.anatomy import bodies as body
    for name in VARIANTS:
        hosts, pieces = body.build(dict(torso="hero"))
        yield name, kit.Piece.merge(build(hosts["torso"], name), pieces["Body"]), None, {**body.COLORS, **COLORS}
