"""
Golems on KayKit's large rig (Rig_Large from the Character Animations pack, read through `rig.Rig`): bodies of
chiselled boulders, every boulder bound rigidly to one bone (stone doesn't bend, so no skin weights to paint).

A golem is authored in the rig's idle pose (`POSE`), where a concept drawing maps straight onto it, then moved back
to the rest pose bone by bone; the right side is the left mirrored in the rest pose. Shoulder slabs ride a helper
bone that turns by half the arm's swing (`HELPERS`), so they stay upright beside the head while the arm moves below.

    python accessories/catalog.py golems -o golems.png --yaws 0,30,-30,180
"""

import numpy as np

from accessories import kit
from accessories.rig import Rig

POSE = ("Idle_A", 0.0)
HELPERS = {"shoulder.l": ("chest", "upperarm.l", 0.5), "shoulder.r": ("chest", "upperarm.r", 0.5)}
COLORS = {"stone": (152, 146, 128), "stone_dark": (108, 103, 94), "moss": (98, 132, 54), "crystal": (70, 170, 235),
          "glow": (200, 248, 255)}


def _basis(up, front=(0.0, 0.0, 1.0)):
    """Axes with y along `up` and z as close to `front` as possible."""
    y = np.asarray(up, float) / np.linalg.norm(up)
    z = np.asarray(front, float) - np.dot(front, y) * y
    z /= np.linalg.norm(z)
    return np.stack([np.cross(y, z), y, z], 1)


def rock(size, center, slot="stone", bevel=0.2, bulge=0.04, jitter=0.05, taper=0.0, turn=(0.0, 0.0, 0.0), up=None,
         seed=0):
    """
    A chiselled boulder: a block of full `size` with its corners cut by `bevel` (a share of the smallest side), its
    faces bulged by `bulge` and every point jittered by `jitter`, narrower at the top by `taper` (at the bottom when
    negative). Turned by `turn` degrees about its x, y, z, its y axis along `up`, placed at `center`.
    """
    rng = np.random.default_rng(seed)
    h = np.asarray(size, float) / 2
    b = bevel * 2 * h.min()
    pts = []
    for s in np.array(np.meshgrid([-1, 1], [-1, 1], [-1, 1])).reshape(3, -1).T.astype(float):
        for axis in range(3):
            p = s * h
            p = p - s * b * (np.arange(3) != axis)
            pts.append(p)
    for axis in range(3):  # bulged faces, their peaks off centre so the facets come out uneven
        for sgn in (-1, 1):
            p = rng.uniform(-0.45, 0.45, 3) * h
            p[axis] = sgn * (h[axis] + bulge * h.min())
            pts.append(p)
    P = np.asarray(pts)
    P[:, [0, 2]] *= np.where(P[:, 1:2] > 0, 1 - max(taper, 0.0), 1 + min(taper, 0.0))
    P += rng.normal(0.0, jitter * h.min(), P.shape)
    for axis, deg in zip(np.eye(3), turn):
        if deg:
            P = P @ kit.rotation(axis, deg).T
    if up is not None:
        P = P @ _basis(up).T
    return kit.hull(P + np.asarray(center, float), slot)


def crystal(width, height, depth, center, facing, up=(0.0, 1.0, 0.0), slot="crystal"):
    """A tall hexagonal crystal set into stone at `center`, its flat table (lit, `glow`) toward `facing`, in a rim
    of dark stone."""
    hexagon = np.array([(0, 0.5), (0.5, 0.25), (0.5, -0.25), (0, -0.5), (-0.5, -0.25), (-0.5, 0.25)]) * [width, height]

    def ring(scale, z):
        return np.column_stack([hexagon * scale, np.full(6, z)])

    f = kit.Frame.make(np.asarray(center, float), facing, up)
    gem = kit.hull(np.vstack([ring(1.0, -0.4 * depth), ring(1.0, 0.4 * depth), ring(0.55, depth)]), slot)
    table = gem.slots.copy()
    tri = gem.verts[gem.faces]
    table[np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])[:, 2] > 0.97 * np.linalg.norm(
        np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0]), axis=1)] = "glow"
    rim = kit.hull(np.vstack([ring(1.25, -0.4 * depth), ring(1.2, 0.15 * depth)]), "stone_dark")
    return kit.Piece.merge(kit.Piece(gem.verts, gem.faces, table), rim).moved(f)


def _noise(P, scale, seed):
    """Smooth value noise in [0, 1] at points `P`."""
    q = np.asarray(P, float) / scale
    i = np.floor(q).astype(np.int64)
    f = q - i
    f = f * f * (3 - 2 * f)
    out = 0.0
    for corner in np.array(np.meshgrid([0, 1], [0, 1], [0, 1])).reshape(3, -1).T:
        c = i + corner
        v = (c.astype(np.uint64) * np.array([374761393, 668265263, 2246822519], np.uint64)).sum(1) + np.uint64(seed)
        v = (v ^ (v >> np.uint64(13))) * np.uint64(1274126177)  # mixed, so every axis varies alike
        v = (v ^ (v >> np.uint64(16))) & np.uint64(0xFFFF)
        w = np.prod(np.where(corner, f, 1 - f), axis=1)
        out = out + w * v / 65535.0
    return out


def _split(tri):
    """Every triangle of `tri` (n, 3, 3) cut into four at its edge midpoints."""
    m = 0.5 * (tri[:, [0, 1, 2]] + tri[:, [1, 2, 0]])
    return np.concatenate([np.stack([tri[:, 0], m[:, 0], m[:, 2]], 1), np.stack([m[:, 0], tri[:, 1], m[:, 1]], 1),
                           np.stack([m[:, 2], m[:, 1], tri[:, 2]], 1), np.stack([m[:, 0], m[:, 1], m[:, 2]], 1)])


def _above(tri, f):
    """The parts of triangles `tri` (n, 3, 3) where the per-corner field `f` (n, 3) is positive, cut along its zero
    line and fanned back into triangles."""
    out = []
    for t, v in zip(tri, f):
        poly = []
        for i in range(3):
            j = (i + 1) % 3
            if v[i] > 0:
                poly.append(t[i])
            if (v[i] > 0) != (v[j] > 0):
                poly.append(t[i] + (t[j] - t[i]) * v[i] / (v[i] - v[j]))
        out += [(poly[0], poly[k], poly[k + 1]) for k in range(1, len(poly) - 1)]
    return np.asarray(out, float).reshape(-1, 3, 3)


def moss(piece, cover=0.5, scale=0.26, up=0.3, seed=3, slot="moss", lift=0.006):
    """
    `piece` grown over with moss: thin patches lying `lift` above its stone faces, blobs about `scale` across cut
    along the contour of a noise field (more of them with `cover`), favouring faces turned up by `up`. The faces are split
    once first, so the blob outlines come out rounder than the boulders' own facets.
    """
    tri = piece.verts[piece.faces][piece.slots == "stone"]
    tri = _split(tri)
    n = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)
    field = (_noise(tri.reshape(-1, 3), scale, seed).reshape(-1, 3) - 0.5) * 2.0 + 0.5 + up * n[:, 1:2] - (1.5 - cover)
    live = (field > 0).any(1) & (n[:, 1] > -0.3)
    patches = _above(tri[live] + lift * n[live][:, None], field[live])
    return kit.Piece.merge(piece, kit.Piece(patches.reshape(-1, 3), np.arange(3 * len(patches)).reshape(-1, 3), slot))


def stone_golem(rig, seed=7):
    """`{bone: piece}` for the left half and the middle of a moss-grown stone golem with crystals, in `POSE`."""
    W = rig.worlds(*POSE)
    J = {b: W[b][:3, 3] for b in W}
    along = lambda a, b: J[a] - J[b]  # noqa: E731 (a bone's direction, pointing back up toward its joint)
    s = iter(range(seed, seed + 100))
    parts = {
        "head": [rock((0.56, 0.62, 0.56), (0, 3.22, 0.14), seed=next(s)),
                 rock((0.62, 0.14, 0.24), (0, 3.34, 0.36), bevel=0.25, seed=next(s)),  # brow
                 rock((0.14, 0.2, 0.14), (0, 3.15, 0.44), bevel=0.3, seed=next(s)),  # nose
                 rock((0.46, 0.2, 0.42), (0, 2.99, 0.22), seed=next(s)),  # jaw
                 *(kit.bevel_box((0.12, 0.055, 0.06), 0.012, "glow", (x, 3.245, 0.42)) for x in (-0.12, 0.12))],
        "chest": [rock((0.55, 0.62, 0.85), (0.55, 3.3, -0.06), bevel=0.3, turn=(0, 0, 14), seed=next(s)),  # collar, left
                  rock((0.55, 0.62, 0.85), (-0.55, 3.3, -0.06), bevel=0.3, turn=(0, 0, -14), seed=next(s)),
                  rock((0.95, 0.6, 0.6), (0, 3.3, -0.36), bevel=0.3, seed=next(s)),  # behind the head
                  rock((0.92, 0.8, 0.98), (0.44, 2.72, 0.14), turn=(0, 10, 8), seed=next(s)),  # pecs
                  rock((0.92, 0.8, 0.98), (-0.44, 2.72, 0.14), turn=(0, -10, -8), seed=next(s)),
                  crystal(0.42, 0.66, 0.22, (0, 2.27, 0.5), (0, 0.15, 1))],
        "spine": [rock((1.4, 0.62, 1.0), (0, 2.1, 0.02), taper=-0.12, seed=next(s)),
                  rock((0.66, 0.26, 0.92), (0.33, 1.66, 0.02), "stone_dark", bevel=0.3, seed=next(s)),  # belt
                  rock((0.66, 0.26, 0.92), (-0.33, 1.66, 0.02), "stone_dark", bevel=0.3, seed=next(s))],
        "hips": [rock((1.15, 0.42, 0.85), (0, 1.33, 0.0), seed=next(s)),
                 kit.hull([(x, y, z) for x, y in ((0.36, 1.46), (-0.36, 1.46), (0.3, 1.2), (-0.3, 1.2), (0, 1.02))
                           for z in (0.3, 0.46)], "stone")],  # the V plate in front
        "shoulder.l": [rock((0.85, 1.25, 0.85), (1.24, 3.1, -0.05), bevel=0.22, bulge=0.0, seed=next(s)),
                       rock((0.65, 0.95, 0.7), (1.4, 2.95, -0.42), "stone_dark", bevel=0.28, seed=next(s))],
        "upperarm.l": [rock((0.55, 0.6, 0.55), 0.5 * (J["upperarm.l"] + J["lowerarm.l"]), "stone_dark",
                            up=along("upperarm.l", "lowerarm.l"), seed=next(s))],
    }
    fore_up = along("lowerarm.l", "wrist.l")
    fore = 0.5 * (J["lowerarm.l"] + J["wrist.l"]) + [0.32, 0.0, 0.05]
    B = _basis(fore_up)
    parts["lowerarm.l"] = [rock((0.95, 1.1, 0.92), fore, up=fore_up, seed=next(s)),
                           crystal(0.32, 0.6, 0.18, fore + 0.46 * B[:, 0], B[:, 0], up=fore_up)]
    hand_up = along("hand.l", "handslot.l")
    fist = J["hand.l"] - 0.5 * hand_up / np.linalg.norm(hand_up) + [0.28, 0.0, 0.05]
    F = _basis(hand_up)
    knuckles = [rock((0.6, 0.21, 0.26), fist + F @ (-0.08, y, 0.48), bevel=0.3, up=hand_up, seed=next(s))
                for y in (0.28, 0.07, -0.14, -0.35)]
    parts["hand.l"] = [rock((0.98, 1.0, 0.95), fist, up=hand_up, seed=next(s)), *knuckles,
                       rock((0.26, 0.42, 0.28), fist + F @ (-0.44, 0.18, 0.38), turn=(0, 0, 20), up=hand_up,
                            bevel=0.3, seed=next(s))]  # thumb
    parts["upperleg.l"] = [rock((0.55, 0.62, 0.6), 0.5 * (J["upperleg.l"] + J["lowerleg.l"]) + [0.1, 0, 0],
                                up=along("upperleg.l", "lowerleg.l"), seed=next(s))]
    parts["lowerleg.l"] = [rock((0.3, 0.26, 0.2), J["lowerleg.l"] + [0.1, 0.02, 0.3], bevel=0.3, seed=next(s)),
                           rock((0.8, 0.72, 0.85), (0.6, 0.52, 0.0), taper=0.08, seed=next(s))]  # knee, boot
    parts["foot.l"] = [rock((0.82, 0.3, 1.05), (0.58, 0.15, 0.12), bevel=0.3, seed=next(s))]
    return {bone: moss(kit.Piece.merge(*pieces), seed=seed + k) for k, (bone, pieces) in enumerate(parts.items())}


def build(make=stone_golem, size="large"):
    """`{bone: piece}` in the rig's rest pose, both sides: the golem authored in `POSE`, moved back to rest bone by
    bone, its left side mirrored onto the right."""
    rig = Rig.load(size)
    rest = rig.pose(make(rig), *POSE, helpers=HELPERS, inverse=True)
    rest.update({b[:-2] + ".r": p.mirrored() for b, p in rest.items() if b.endswith(".l")})
    return rest


def catalog():
    """The stone golem in its idle pose (as authored) and its rest pose."""
    rest = build()
    idle = Rig.load("large").pose(rest, *POSE, helpers=HELPERS)
    yield "stone golem (idle)", kit.Piece.merge(*idle.values()), None, COLORS
    yield "stone golem (rest)", kit.Piece.merge(*rest.values()), None, COLORS
