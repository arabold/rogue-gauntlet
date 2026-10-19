"""
Torso armor, built from separate pieces that combine into fantasy outfits from D&D knights to Conan's harness:
each its own piece (put on or taken off alone), fitted to the torso as worn so far (`clothes.Worn`: over a tunic,
a robe or another layer), in chunky stylized proportions.

- `plate` / `breastplate` / `backplate`: a sculpted plate (`SCULPTS`: male pecs and abs, female cups, a brute's
  muscle cuirass, a keel, the back's shoulder blades) with raised trim and rivets.
- `lames`: overlapping horizontal bands (a fauld under a breastplate, or a whole segmented cuirass).
- `mail`: a chainmail shirt, ridged in rows, its hem scalloped. `scales`: scale mail, rows of overlapping scales.
- `quilted`: a padded gambeson in puffed channels. `tabard`: a cloth panel front and back, over mail.
- `straps`: crossed leather straps with a ring boss; `fur_collar`: a ruff of fur round the shoulders.
- `emblem` (a sunburst, a cross, a skull, a gem) and `spikes` to put on any of them.

`VARIANTS` combines them (knight, paladin, maiden, segmented, crusader, scale, gambeson, barbarian, brigandine,
dark knight); `outfit(variant, torso)` builds one as named pieces.

    python asset-studio/studio.py catalog armor -o armor.png --yaws 0,-40,-90,180
"""

import numpy as np

from core import kit

FRONT, BACK = np.pi / 2, 3 * np.pi / 2


def _bump(u, t, u0, t0, su, st):
    return np.exp(-((u - u0) / su) ** 2 - ((t - t0) / st) ** 2)


# Sculpts: what swells out of (or cuts into) a plate, over u (-1 .. 1 across it) and t (0 .. 1 bottom to top);
# heights are shares of the torso's half width at the chest.
SCULPTS = {
    "male": lambda u, t: (0.12 * (_bump(u, t, -0.38, 0.72, 0.32, 0.15) + _bump(u, t, 0.38, 0.72, 0.32, 0.15))
                          + 0.04 * sum(_bump(u, t, s * 0.16, t0, 0.12, 0.06) for s in (-1, 1) for t0 in (0.18, 0.34, 0.5))
                          + 0.04 * np.exp(-(u / 0.07) ** 2) * (t < 0.62)),
    "female": lambda u, t: (0.3 * (_bump(u, t, -0.36, 0.66, 0.26, 0.16) + _bump(u, t, 0.36, 0.66, 0.26, 0.16))
                            + 0.04 * np.exp(-(u / 0.08) ** 2) * (t < 0.45)),
    "muscle": lambda u, t: (0.17 * (_bump(u, t, -0.38, 0.72, 0.34, 0.14) + _bump(u, t, 0.38, 0.72, 0.34, 0.14))
                            + 0.07 * sum(_bump(u, t, s * 0.17, t0, 0.12, 0.055) for s in (-1, 1) for t0 in (0.15, 0.31, 0.46))
                            - 0.05 * np.exp(-(u / 0.06) ** 2)),
    "plain": lambda u, t: 0.06 * np.exp(-(u / 0.1) ** 2),  # a keel down the middle
    "none": lambda u, t: 0.0 * (u + t),
    "back": lambda u, t: (0.1 * (_bump(u, t, -0.42, 0.7, 0.3, 0.18) + _bump(u, t, 0.42, 0.7, 0.3, 0.18))
                          + 0.05 * np.exp(-(u / 0.07) ** 2)),
}


def _surface(host, y, theta, out):
    """Points on a host at heights `y` and angles `theta`, `out` off its surface (all broadcast together)."""
    y, theta, out = np.broadcast_arrays(np.asarray(y, float), np.asarray(theta, float), np.asarray(out, float))
    cx, a, cz, b = (np.asarray(v, float).reshape(y.shape) for v in host.section(y.ravel()))
    c, s = np.cos(theta), np.sin(theta)
    p = getattr(host, "p", 2.0)
    ux, uz = np.sign(c) * np.abs(c) ** (2 / p), np.sign(s) * np.abs(s) ** (2 / p)
    return np.stack([cx + np.maximum(a + out, 1e-3) * ux, y, cz + np.maximum(b + out, 1e-3) * uz], -1)


def plate(torso, y0, y1, side="front", sculpt="male", width=86.0, thickness=0.045, neck=0.08, point=0.0,
          trim="trim", trim_width=0.12, rivets=6, rivet_slot="rivet", slot="armor", rows=12, cols=17, push="forward",
          edges="all", rivet_rows=1, neck_shape="round", rivet_edge="bottom"):
    """
    One plate over the `side` ("front" or "back") of `torso` from `y0` up to `y1`, `width` degrees to either side,
    `thickness` off it, its `sculpt` (`SCULPTS` name) pushed straight forward (or back) off it so pecs and cups
    stand out from the chest rather than sideways along its curve. Its top edge dips by `neck` at the middle (a
    neckline, `neck_shape` "round" or a "v"), its bottom edge points down by `point` at the middle. `trim` raises a
    band of that slot along the `edges` ("all", or any of "top", "bottom", "sides"; `trim_width` a share of the
    plate), with `rivets` rivets along the `rivet_edge` (`rivet_rows` rows of them up the plate: a brigandine).
    `push` "out" swells the sculpt along the surface instead (quilting that wraps round the sides).
    """
    A = float(torso.section([0.5 * (y0 + y1)])[1][0])
    centre = FRONT if side == "front" else BACK
    u = np.linspace(-1, 1, cols)
    t = np.linspace(0, 1, rows)[:, None]
    shape = SCULPTS[sculpt] if isinstance(sculpt, str) else sculpt

    def at(u, t, extra=0.0):
        top = y1 - neck * ((1 - np.abs(u)) if neck_shape == "v" else (1 - u ** 2))
        bottom = y0 - point * (1 - np.abs(u))
        y = bottom + (top - bottom) * t
        theta = centre - np.radians(width) * u * (1 if side == "front" else -1)  # +u: the character's left
        if push == "out":
            return _surface(torso, y, theta, thickness + extra + A * shape(u, t))
        forward = np.array([0.0, 0.0, 1.0 if side == "front" else -1.0])
        return _surface(torso, y, theta, thickness + extra) + (A * shape(u, t))[..., None] * forward

    def shell(u, t, lift=0.0, slot=slot):
        return kit.patch_shell(at(u, t, lift), at(u, t, lift - thickness - 0.012), slot)

    parts = [shell(u, t)]
    if trim:
        w = trim_width
        which = {"top", "bottom", "sides"} if edges == "all" else {edges} if isinstance(edges, str) else set(edges)
        bands = [(u, np.linspace(0, w, 3)[:, None])] if "bottom" in which else []
        if "top" in which:
            bands.append((u, np.linspace(1 - w, 1, 3)[:, None]))
        if "sides" in which:
            bands += [(np.linspace(1 - w / 2, 1, 3), t), (np.linspace(-1, -1 + w / 2, 3), t)]
        parts += [kit.patch_shell(at(bu, bt, 0.015), at(bu, bt, -0.01), trim) for bu, bt in bands]
    for k in range(rivets * rivet_rows):  # along the bottom trim, and up the plate in rows
        uk = -0.85 + 1.7 * (k % rivets + 0.5) / rivets
        tk = 0.5 * trim_width + (k // rivets) * (1 - trim_width) / max(rivet_rows, 1)
        tk = 1 - tk if rivet_edge == "top" else tk
        p0, p1, p2 = (at(np.array([uu]), np.array([[tk]]), 0.02)[0, 0] for uu in (uk, uk - 0.01, uk + 0.01))
        q = at(np.array([uk]), np.array([[tk + 0.02]]), 0.02)[0, 0]
        n = np.cross(p2 - p1, q - p0)
        n *= np.sign(n @ (p0 - [0.0, p0[1], float(torso.section([p0[1]])[2][0])]))
        parts.append(kit.stud(0.028, 0.02, rivet_slot).moved(kit.Frame.make(p0, n, q - p0)))
    return kit.Piece.merge(*parts)


def _at_x(torso, y, x, side="front"):
    """The angle on a host's ring at height `y` whose point lies `x` across from its middle (+x: the character's
    left), on the front or the back."""
    p = getattr(torso, "p", 2.0)
    a = float(torso.section([y])[1][0])
    c = np.sign(x) * min(abs(x) / max(a, 1e-6), 0.98) ** (p / 2)
    theta = np.arccos(c)  # 0 .. pi: the front half
    return theta if side == "front" else 2 * np.pi - theta


def _on(torso, piece, x, y, out, side="front", tilt=0.0):
    """A piece built in a surface frame (x along the ring, y up, z out) placed at (`x` across, `y`) on the torso,
    `out` off it, leaning back by `tilt` degrees at its top."""
    f = kit.band_frame(torso, y, _at_x(torso, y, x, side), out)
    return piece.moved(f.rotated(-tilt, "x") if tilt else f)


def _ball(r, slot, squash=0.9, sides=8, rings=5):
    """A low-poly sphere of radius `r`, its front (+z) a little flattened: a breast cup, a boss."""
    pts = [r * np.array([np.cos(a) * np.cos(b), np.sin(b), squash * np.sin(a) * np.cos(b) + 0.15 * (1 - squash)])
           for a in np.linspace(0, 2 * np.pi, sides, endpoint=False) for b in np.linspace(-np.pi / 2, np.pi / 2, rings)[1:-1]]
    return kit.hull(pts + [(0, r, 0), (0, -r, 0)], slot)


def figure(torso, y0, y1, build="male", thickness=0.045, slot="armor", scale=1.0, side="front"):
    """
    The chiseled relief on a plate, in stylized's chunky facets rather than smooth bulges: `male` pectoral slabs and
    a six-pack of blocks, `muscle` the same bigger and deeper (a brute's), `female` two faceted spheres, `plain` a
    ridge down the middle; on the `back`, two shoulder-blade slabs and a spine ridge. It sits on the plate's surface
    (`thickness` off the torso), sized by the torso's width at the chest.
    """
    yc = y1 - 0.2 * scale
    A = float(torso.section([yc])[1][0]) * scale
    out = thickness - 0.012
    parts = []
    if build in ("male", "muscle"):
        k = 1.2 if build == "muscle" else 1.0
        w, h, d = 0.5 * A * k, 0.38 * A * k, 0.05 * k
        pec = kit.prism([(-0.5 * w, 0.42 * h), (0.38 * w, 0.5 * h), (0.5 * w, 0.18 * h), (0.32 * w, -0.5 * h),
                         (-0.42 * w, -0.46 * h), (-0.5 * w, -0.12 * h)], d, slot, bevel=0.016 * k)
        parts.append(_on(torso, pec, 0.3 * A * k, yc, out, side, tilt=6))
        bw, bh, gap = 0.24 * A * k, 0.17 * A * k, 0.022
        y = yc - 0.5 * h - gap - 0.5 * bh
        for _ in range(3):  # a six-pack, as far down as the plate goes
            if y - 0.5 * bh < y0 + 0.03:
                break
            block = kit.bevel_box((bw, bh, 0.035 * k), 0.012, slot, center=(0, 0, 0.0175 * k))
            parts.append(_on(torso, block, 0.5 * bw + 0.5 * gap, y, out, side))
            y -= bh + gap
    elif build == "female":
        r = 0.4 * A
        parts.append(_on(torso, _ball(r, slot, squash=0.95), 0.36 * A, yc - 0.03, out - 0.18 * r, side))
    elif build == "back":
        w, h, d = 0.55 * A, 0.42 * A, 0.035
        blade = kit.prism([(-0.5 * w, 0.5 * h), (0.5 * w, 0.4 * h), (0.42 * w, -0.3 * h), (-0.1 * w, -0.5 * h),
                           (-0.5 * w, -0.2 * h)], d, slot, bevel=0.012)
        parts.append(_on(torso, blade, 0.32 * A, yc, out, side, tilt=4))
    if build in ("plain", "back", "muscle"):  # a ridge down the middle
        L = (y1 - y0) * (0.85 if build == "plain" else 0.7)
        ridge = kit.bevel_box((0.1 * A, L, 0.035), 0.012, slot, center=(0, 0, 0.0175))
        center = _on(torso, ridge, 0.0, y1 - 0.06 - 0.5 * L, out, side)
        mirrored = [p.mirrored() for p in parts]
        return kit.Piece.merge(*parts, *mirrored, center)
    return kit.Piece.merge(*parts, *(p.mirrored() for p in parts))


def knight_plate(torso, y0, y1, seam=None, chevron=0.07, neck=0.11, thickness=0.045, slot="armor", trim="trim",
                 rivet_slot="rivet", **kw):
    """
    A knight's breastplate in two plates: the chest from a V-shaped neckline down to a seam that points down at the
    middle (`chevron`), both edges trimmed and riveted, a ridge down it; the belly plate below tucked under that
    seam, trimmed at its bottom.
    """
    seam = y0 + 0.55 * (y1 - y0) if seam is None else seam
    chest = plate(torso, seam, y1, "front", "none", thickness=thickness, neck=neck, neck_shape="v", point=chevron,
                  slot=slot, trim=trim, rivets=5, rivet_slot=rivet_slot, rivet_edge="top", rows=4, cols=7, **kw)
    seam_rivets = plate(torso, seam, y1, "front", "none", thickness=thickness, neck=neck, neck_shape="v",
                        point=chevron, slot=slot, trim=None, rivets=6, rivet_slot=rivet_slot, rows=2, cols=3, **kw)
    belly = plate(torso, y0, seam + 0.04, "front", "none", thickness=thickness - 0.012, neck=chevron, neck_shape="v",
                  slot=slot, trim=trim, edges="bottom", rivets=0, rows=3, cols=7, **kw)
    ridge = figure(torso, seam, y1, "plain", thickness, slot)
    return kit.Piece.merge(chest, kit.Piece(*_studs_only(seam_rivets, rivet_slot)), belly, ridge)


def _studs_only(piece, slot):
    """The faces of `piece` in `slot` alone (to keep a plate's rivets without its shell)."""
    keep = piece.slots == slot
    used, faces = np.unique(piece.faces[keep], return_inverse=True)
    return piece.verts[used], faces.reshape(-1, 3), piece.slots[keep]


def faulds(torso, y0, y1, count=3, flare=0.06, slot="armor", trim="trim", rivets=4, rivet_slot="rivet", **kw):
    """A skirt of overlapping plates hanging from the waist (`y1`) over the hips to `y0`, flaring out by `flare`,
    front and back."""
    from prefabs.outfits.clothes import _Bell
    a, b = (float(v[0]) for v in np.asarray(torso.section([y1]))[[1, 3]])
    bell = _Bell(torso, y1, y0, a + flare, b + flare)
    return kit.Piece.merge(*(lames(bell, y0, y1, count, side, 92.0, slot=slot, trim=trim, rivets=rivets,
                                   rivet_slot=rivet_slot, **kw) for side in ("front", "back")))


def limb_plate(limb, y0, y1, thickness=0.035, slot="armor", trim="trim", straps=2, strap_slot="leather",
               flare=(0.0, 0.0)):
    """A plate tube round a limb from `y0` to `y1` (shares of its length): a vambrace, a rerebrace, a greave, trimmed
    at both ends, `straps` leather straps round it."""
    from prefabs.outfits.clothes import cuff, shell
    L = limb.length
    parts = [limb.place(shell(limb, y0 * L, y1 * L, thickness, slot, flare=flare, sides=12))]
    if trim:
        parts += [cuff(limb, y0 + 0.03, 0.04, thickness + flare[0] + 0.012, trim),
                  cuff(limb, y1 - 0.03, 0.04, thickness + flare[1] + 0.012, trim)]
    for k in range(straps):
        parts.append(cuff(limb, y0 + (y1 - y0) * (k + 1) / (straps + 1), 0.035, thickness + 0.008, strap_slot))
    return kit.Piece.merge(*parts)


def cop(limb, at, size=0.08, theta=np.pi / 2, thickness=0.06, slot="armor", trim="trim"):
    """A rounded plate over a joint (`at`, a share of the limb's length) on the side at angle `theta` (the front of a
    knee; an elbow points back in the T-pose), with a fan-shaped wing round it."""
    f = kit.band_frame(limb, at * limb.length, theta, thickness)
    dome = _ball(size, slot, squash=0.55)
    wing = kit.prism([(-1.6 * size, 0.0), (0.0, 1.0 * size), (1.6 * size, 0.0), (0.0, -1.0 * size)], 0.02, trim,
                     bevel=0.006)
    return limb.place(kit.Piece.merge(dome, wing.moved(kit.Frame((0, 0, -0.012), [1, 0, 0], [0, 1, 0], [0, 0, 1])))
                      .moved(f))


def breastplate(torso, y0=0.62, y1=None, sculpt="male", gorget=None, **kw):
    """The front plate from the waist (`y0`) up to the collarbone (`y1`, default just under the neck), its `sculpt` a
    chiseled `figure` (male, muscle, female, plain) or a smooth `SCULPTS` relief; `gorget` adds a flared collar ring
    in that slot (stylized's knight)."""
    y1 = torso.top - 0.07 if y1 is None else y1
    thickness = kw.get("thickness", 0.045)
    if sculpt == "knight":
        return knight_plate(torso, y0, y1, **kw)
    if sculpt in ("male", "muscle", "female", "plain"):
        parts = [plate(torso, y0, y1, "front", "none", **dict(dict(rows=9, cols=13), **kw)),
                 figure(torso, y0, y1, sculpt, thickness, kw.get("slot", "armor"))]
    else:
        parts = [plate(torso, y0, y1, "front", sculpt, **kw)]
    if gorget:
        from prefabs.outfits.clothes import shell
        parts.append(shell(torso, torso.top - 0.13, torso.top + 0.03, thickness + 0.02, gorget, flare=(0.0, 0.07)))
    return kit.Piece.merge(*parts)


def backplate(torso, y0=0.62, y1=None, sculpt="back", **kw):
    """The back plate: chiseled shoulder blades and a spine ridge, no neckline dip."""
    y1 = torso.top - 0.06 if y1 is None else y1
    kw = dict(dict(neck=0.03), **kw)
    if sculpt == "back":
        return kit.Piece.merge(plate(torso, y0, y1, "back", "none", **dict(dict(rows=9, cols=13), **kw)),
                               figure(torso, y0, y1, "back", kw.get("thickness", 0.045), kw.get("slot", "armor"),
                                      side="back"))
    return plate(torso, y0, y1, "back", sculpt, **kw)


def lames(torso, y0, y1, count=3, side="front", width=78.0, thickness=0.04, flare=0.012, overlap=0.25, slot="armor",
          trim="trim", rivets=0, **kw):
    """
    `count` horizontal bands from `y1` down to `y0`, each a little proud of and wider than the one above and
    overlapping it by `overlap` (a share of a band): a fauld below a breastplate, or a whole segmented cuirass.
    """
    h = (y1 - y0) / count
    out = []
    for i in range(count):
        top = y1 - i * h
        out.append(plate(torso, top - h * (1 + overlap), top, side, "none", width + 2 * i, thickness + i * flare,
                         neck=0.0, slot=slot, trim=trim, edges="bottom", trim_width=0.28, rivets=rivets, rows=4,
                         cols=15, **kw))
    return kit.Piece.merge(*out)


def mail(torso, y0, y1, thickness=0.025, ridges=10, ridge=0.012, scallops=14, depth=0.035, slot="mail", sides=None):
    """A chainmail shirt round the torso from `y0` to `y1`: `ridges` raised rows of rings, a hem of `scallops`
    hanging arcs `depth` deep."""
    sides = sides or scallops * 4
    profile = [(0.0, -0.012), (0.0, thickness)]
    profile += [(k / (2 * ridges), thickness + ridge * (k % 2)) for k in range(1, 2 * ridges)]
    profile += [(1.0, thickness), (1.0, -0.012)]
    piece = kit.host_band(torso, y0, y1, thickness, slot, profile=profile, sides=sides)
    V = piece.verts.copy()
    low = V[:, 1] < y0 + 1e-6
    V[low, 1] -= depth * np.abs(np.sin(0.5 * scallops * np.arctan2(V[low, 2], V[low, 0])))
    return kit.Piece(V, piece.faces, piece.slots)


def scales(torso, y0, y1, side="front", width=90.0, rows=6, cols=10, thickness=0.02, slot="scale", trim=None,
           backing="scale_back"):
    """Scale mail over one side: `rows` x `cols` leaf-shaped scales, each row offset by half a scale and lying a
    little over the row below, their lower edges lifting off the body like shingles, sewn onto a solid `backing`
    so the gaps between them show leather, not skin."""
    out = []
    if backing:
        out.append(plate(torso, y0 - 0.03, y1, side, "none", width + 4, 0.018, neck=0.0, slot=backing, trim=None,
                         rivets=0, rows=8, cols=13))
    dy = (y1 - y0) / rows
    centre = FRONT if side == "front" else BACK
    for r in range(rows):
        y = y1 - (r + 0.5) * dy
        A = float(torso.section([y])[1][0])
        arc = np.radians(2 * width) * A / cols
        n = cols + (r % 2)
        for c in range(n):
            u = -1 + (2 * c + 1 - (r % 2)) / cols
            theta = centre - np.radians(width) * u * (1 if side == "front" else -1)
            w, h = 0.62 * arc, 0.85 * dy
            pts = [(x * w, yy * h, z) for x, yy in ((-1, 0.5), (1, 0.5), (1, -0.1), (0.6, -0.45), (0, -0.6), (-0.6, -0.45),
                                                     (-1, -0.1)) for z in (0.0, thickness)]
            scale = kit.hull(pts, slot)
            f = kit.band_frame(torso, y, theta, 0.02 + (rows - r) * 0.004).rotated(-14.0, "x")
            out.append(scale.moved(f))
    if trim:
        out.append(kit.host_band(torso, y1 - 0.02, y1 + 0.02, 0.04, trim, sides=24))
    return kit.Piece.merge(*out)


def quilted(torso, y0, y1, side="front", width=90.0, channels=6, seams=4, puff=0.08, thickness=0.03, slot="cloth",
            **kw):
    """A padded gambeson panel: soft puffs between `channels` stitched seams running down it and `seams` across it
    (quilting that wraps round the sides)."""
    def quilt(u, t):
        return puff * np.sqrt(np.abs(np.sin(0.5 * np.pi * channels * (u + 1))) * np.abs(np.sin(np.pi * seams * t)))
    return plate(torso, y0, y1, side, quilt, width, thickness, push="out", slot=slot, cols=4 * channels + 1,
                 rows=4 * seams + 1, **kw)


def _strip(torso, ends, width, thickness, slot, steps=14):
    """A strap lying on the torso between two (y, theta) points, `width` tall."""
    s = np.linspace(0, 1, steps)
    (ya, ta), (yb, tb) = ends
    y = (ya + (yb - ya) * s)[None, :] + np.array([-0.5, 0.5])[:, None] * width
    theta = np.broadcast_to(ta + (tb - ta) * s, y.shape)
    return kit.patch_shell(_surface(torso, y, theta, thickness), _surface(torso, y, theta, -0.012), slot)


def straps(torso, y0, y1, width=0.08, thickness=0.03, spread=62.0, slot="leather", boss="metal", sides=("front", "back"),
           cross=True, patina=False, dents=False):
    """Crossed leather straps over the chest (and back) from each shoulder to the opposite hip, a round boss where
    they cross: a barbarian's harness. `cross` false: one baldric from the left shoulder to the right hip."""
    from core.export import corner_normals
    out = []
    ym = 0.5 * (y0 + y1)
    for side in sides:
        c = FRONT if side == "front" else BACK
        for idx_s, sgn in enumerate((-1, 1) if cross else ((-1,) if side == "front" else (1,))):
            st = _strip(torso, ((y1, c + sgn * np.radians(spread)), (y0, c - sgn * np.radians(spread))), width,
                        thickness, slot)
            d_cnt, d_scale = kit.parse_dents(dents, max_allowed=2)
            if patina or d_cnt:
                st.normals = corner_normals(st.verts, st.faces, crease=55.0)
                if patina:
                    st = kit.weather(st, slot, light_slot=f"{slot}_light", worn_slot=f"{slot}_worn",
                                     dark_slot=f"{slot}_dark", rust_slot="grime", seed=41 + idx_s)
                # At most 1-2 gashes total across straps, on front straps only
                if d_cnt and side == "front" and idx_s < d_cnt:
                    t_cut = 0.35 if idx_s == 0 else 0.65
                    y_cut = y1 + (y0 - y1) * t_cut
                    th_cut = c + sgn * np.radians(spread) * (1.0 - 2.0 * t_cut)
                    f_cut = kit.band_frame(torso, y_cut, th_cut, thickness).rotated(35.0 * sgn, "z")
                    st = kit.gash(st, f_cut, length=0.11 * d_scale, width=0.034 * d_scale, depth=0.016 * d_scale,
                                  slot=slot, shadow_slot=f"{slot}_dark")
            out.append(st)
        if boss:
            f = kit.band_frame(torso, ym, c, thickness)
            ring_slot = f"{boss}_worn" if patina else boss
            stud_slot = f"{boss}_light" if patina else boss
            ring = kit.sweep(np.array([[0.07 * np.cos(a), 0.07 * np.sin(a), 0.012] for a in np.linspace(0, 2 * np.pi, 13)[:-1]]),
                             kit.rect_profile(0.03, 0.03, 0.008), ring_slot, up=(0, 0, 1), closed=True)
            if patina:
                ring = kit.weather(ring, ring_slot, dark_slot=f"{boss}_dark", rust_slot="rust", seed=47)
            out.append(kit.Piece.merge(ring, kit.stud(0.05, 0.03, stud_slot)).moved(f))
    return kit.Piece.merge(*out)


def fur_collar(torso, y, count=16, size=0.1, slot="fur", seed=2):
    """A ruff of fur round the shoulders: lumps of uneven size (a barbarian's mantle)."""
    rng = np.random.default_rng(seed)
    out = []
    for k in range(count):
        theta = 2 * np.pi * k / count
        r = size * (0.85 + 0.35 * rng.random())
        f = kit.band_frame(torso, y + 0.02 * rng.standard_normal(), theta, 0.3 * r)
        pts = [r * np.array([0.9 * np.cos(a) * np.cos(b), 0.7 * np.sin(b), 0.6 * np.sin(a) * np.cos(b)])
               for a in np.radians(np.arange(0, 360, 60) + 30 * (k % 2)) for b in np.radians((-50, 0, 50))]
        out.append(kit.hull(pts + [(0, 0.7 * r, 0), (0, -0.7 * r, 0)], slot).moved(f))
    return kit.Piece.merge(*out)


def emblem(torso, y, style="sunburst", size=0.12, out=0.07, slot="gold", gem="gem", side="front"):
    """A device on the chest (or back): a `sunburst` round a gem, a `cross`, a `skull`, or a plain `gem` in a mount."""
    f = kit.band_frame(torso, y, FRONT if side == "front" else BACK, out)
    if style == "sunburst":
        rays = [kit.prism([(-0.16 * size, 0.0), (0.16 * size, 0.0), (0.0, size)], 0.02, slot, bevel=0.004).moved(
            kit.Frame((0, 0, 0), [1, 0, 0], [0, 1, 0], [0, 0, 1]).rotated(a, "z")) for a in np.arange(0, 360, 45)]
        piece = kit.Piece.merge(kit.stud(0.5 * size, 0.03, slot, sides=10), kit.gem(0.3 * size, gem, depth=0.05).moved(
            kit.Frame((0, 0, 0.02), [1, 0, 0], [0, 1, 0], [0, 0, 1])), *rays)
    elif style == "cross":
        piece = kit.Piece.merge(kit.bevel_box((0.3 * size, 1.4 * size, 0.03), 0.008, slot, center=(0, -0.15 * size, 0.015)),
                                kit.bevel_box((1.0 * size, 0.3 * size, 0.03), 0.008, slot, center=(0, 0.15 * size, 0.015)))
    elif style == "skull":
        from prefabs.outfits.belts import skull
        piece = skull(0.75 * size, slot)
    else:
        piece = kit.Piece.merge(kit.gem(0.6 * size, slot, depth=0.03), kit.gem(0.4 * size, gem, depth=0.05).moved(
            kit.Frame((0, 0, 0.01), [1, 0, 0], [0, 1, 0], [0, 0, 1])))
    return piece.moved(f)


def spikes(torso, y, side="front", count=3, spread=40.0, height=0.12, radius=0.035, out=0.06, slot="metal"):
    """A row of spikes standing out of a plate."""
    c = FRONT if side == "front" else BACK
    out_ = []
    for theta in c + np.radians(spread) * np.linspace(-1, 1, count):
        f = kit.band_frame(torso, y, theta, out)
        out_.append(kit.spike(f.origin, f.to_world([0.0, 0.25 * height, height]), radius, slot))
    return kit.Piece.merge(*out_)


def tabard(torso, top, hem=0.18, width=34.0, waist=0.62, slot="cloth", trim="gold", sides=("front", "back"),
           flare=0.0):
    """A cloth panel hanging front and back from the shoulders past the hips, its edges trimmed (over mail);
    `flare` widens it toward the hem (a cape sweeping out)."""
    from prefabs.outfits.clothes import _Bell
    bell = _Bell(torso, waist, hem, float(torso.section([waist])[1][0]) + 0.03 + flare,
                 float(torso.section([waist])[3][0]) + 0.05 + 0.5 * flare)
    return kit.Piece.merge(*(plate(bell, hem, top, side, "none", width, 0.05, neck=0.05, point=-0.06, slot=slot,
                                   trim=trim, trim_width=0.06, rivets=0, rows=14, cols=9) for side in sides))


COLORS = {"armor": (150, 158, 170), "trim": (90, 96, 110), "rivet": (220, 200, 120), "leather": (122, 78, 52),
          "mail": (128, 134, 146), "scale": (96, 140, 110), "fur": (120, 92, 70), "gold": (232, 182, 64),
          "cloth": (70, 96, 170), "gem": (200, 40, 60), "metal": (176, 186, 199), "dark": (60, 54, 60),
          "scale_back": (70, 54, 44)}

# Outfits: (torso preset, colors, [(element, arguments), ...]); `y`s are rig heights on the default torso.
VARIANTS = {
    "knight": ("hero", {}, [("breastplate", dict(y0=0.72, sculpt="plain", gorget="armor")),
                            ("lames", dict(y0=0.5, y1=0.76, count=3)),
                            ("backplate", dict(y0=0.6))]),
    "paladin": ("hero", dict(trim=(232, 182, 64)), [
        ("breastplate", dict(y0=0.66, sculpt="male", rivets=0)), ("emblem", dict(y=1.13, style="sunburst", size=0.1)),
        ("lames", dict(y0=0.46, y1=0.7, count=3)), ("backplate", dict(y0=0.6))]),
    "maiden": ("female", dict(armor=(200, 206, 220), trim=(180, 140, 70)), [
        ("breastplate", dict(y0=0.74, sculpt="female", neck=0.1, rivets=0)), ("emblem", dict(y=0.82, style="gem", size=0.08)),
        ("lames", dict(y0=0.52, y1=0.78, count=2)), ("backplate", dict(y0=0.6))]),
    "segmented": ("standard", dict(armor=(170, 150, 120), trim=(120, 90, 60)), [
        ("lames", dict(y0=0.5, y1=1.22, count=6, side="front", rivets=4)),
        ("lames", dict(y0=0.5, y1=1.22, count=6, side="back"))]),
    "crusader": ("hero", dict(cloth=(236, 236, 230), gold=(200, 40, 40)), [
        ("mail", dict(y0=0.42, y1=1.24)), ("tabard", dict(top=1.24)), ("emblem", dict(y=0.98, style="cross", slot="gold"))]),
    "scale": ("standard", dict(scale=(90, 140, 110), trim=(180, 140, 70)), [
        ("scales", dict(y0=0.5, y1=1.2, side="front", trim="trim")), ("scales", dict(y0=0.5, y1=1.2, side="back"))]),
    "gambeson": ("standard", dict(cloth=(190, 170, 130)), [
        ("quilted", dict(y0=0.45, y1=1.24, side="front", trim="leather", rivets=0)),
        ("quilted", dict(y0=0.45, y1=1.24, side="back", trim="leather", rivets=0))]),
    "barbarian": ("hero", {}, [("straps", dict(y0=0.62, y1=1.18)), ("fur_collar", dict(y=1.2))]),
    "brigandine": ("standard", {}, [
        ("breastplate", dict(y0=0.5, sculpt="plain", slot="leather", rivets=6, rivet_rows=4)),
        ("backplate", dict(y0=0.5, slot="leather", rivets=6, rivet_rows=4))]),
    "dark knight": ("brute", dict(armor=(60, 56, 66), trim=(120, 30, 40), metal=(200, 204, 214)), [
        ("breastplate", dict(y0=0.66, sculpt="muscle", point=0.06)), ("spikes", dict(y=1.2, count=3, height=0.16, spread=55)),
        ("lames", dict(y0=0.46, y1=0.7, count=2)), ("backplate", dict(y0=0.6)),
        ("spikes", dict(y=1.0, side="back", count=3))]),
}

ELEMENTS = {"breastplate": breastplate, "backplate": backplate, "plate": plate, "lames": lames, "faulds": faulds,
            "mail": mail,
            "scales": scales, "quilted": quilted, "straps": straps, "fur_collar": fur_collar, "emblem": emblem,
            "spikes": spikes, "tabard": tabard}


def outfit(variant, torso):
    """The `VARIANTS` outfit on `torso` (a host, ideally `clothes.Worn`), one named piece per element."""
    return {f"{name.capitalize()}{i}": ELEMENTS[name](torso, **kw) for i, (name, kw) in enumerate(VARIANTS[variant][2])}


def catalog():
    """Each variant on a plain body of its torso preset."""
    from prefabs.anatomy import bodies as body
    from prefabs.outfits import clothes
    for name, (torso, colors, _) in VARIANTS.items():
        hosts, pieces = body.build(dict(torso=torso))
        worn = clothes.Worn(hosts["torso"])
        yield name, kit.Piece.merge(*pieces.values(), *outfit(name, worn).values()), None, {**body.COLORS, **COLORS,
                                                                                               **colors}
