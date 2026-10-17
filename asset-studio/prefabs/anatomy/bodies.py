"""
Bodies in the standard vocabulary: a bean-shaped torso, tube arms ending in mitten hands, short tube legs on chunky
feet, one piece per stylized mesh (`Body`, `ArmLeft`, `ArmRight`, `LegLeft`, `LegRight`) that overlap where they meet,
like the standard own, so each can be skinned to its bones and swapped or dressed on its own. Placed on the stylized rig
(`JOINTS`): arms run from the shoulder to the wrist and legs from the hip to the ankle, their narrowest points near
the elbow and the knee; the torso spans the crotch to the neck.

`Trunk` (the torso) and `Limb` (an arm or a leg) are hosts like the head (`section`), so belts, collars, bracers and
boots fit them. A limb is modelled along its own vertical axis, root at y = 0, and placed by its `frame`.
"""

import numpy as np
from scipy.interpolate import PchipInterpolator

from core import kit
from prefabs.anatomy.limbs import Limb, ARMS, LEGS
from prefabs.anatomy.extremities import hand, foot

# The stylized rig (Skeleton_Warrior's bind pose): the character's left side (+x), T-pose, facing +z.
JOINTS = dict(hips=(0.0, 0.406, 0.0), chest=(0.0, 0.973, 0.0), neck=(0.0, 1.241, 0.0),
              shoulder=(0.212, 1.107, 0.0), elbow=(0.454, 1.107, 0.0), wrist=(0.713, 1.107, 0.0),
              hip=(0.171, 0.519, 0.0), knee=(0.171, 0.292, 0.0), ankle=(0.171, 0.145, 0.0), toes=(0.171, 0.026, 0.096))

COLORS = {"skin": (240, 190, 150), "skin_dark": (205, 145, 110), "nail": (236, 230, 214), "boot": (96, 66, 48),
          "sole": (60, 44, 36)}


def _envelope(t, q):
    """1 over the middle of [0, 1], rounding to 0 at both ends (a superellipse of exponent `q`)."""
    return (1 - np.abs(2 * np.clip(t, 0, 1) - 1) ** q) ** (1 / q)


class _Profile:
    """Half widths across, in front and behind along a length, smoothly through `rows` = [(t, across, front,
    back), ...] (t from 0 to 1; `back` defaults to `front`), rounded off at both ends by `_envelope(q)`."""

    def __init__(self, rows, q):
        r = np.array([row + (row[2],) if len(row) == 3 else row for row in rows], float)
        self.f = [PchipInterpolator(r[:, 0], r[:, k]) for k in (1, 2, 3)]
        self.q = q

    def __call__(self, t):
        t = np.clip(np.asarray(t, float), 0, 1)
        e = np.where((t <= 0) | (t >= 1), 0.0, _envelope(t, self.q))
        return tuple(np.maximum(f(t) * e, 0.0) for f in self.f)


def _closed_tube(section, y0, y1, p, sides, rings, slot, cut=0.04):
    """A closed tube around the vertical axis from `y0` to `y1` through `section(y) -> (cx, a, cz, b)`, its ends
    capped at the axis (rings spaced closer near the ends, where it rounds off)."""

    class Host:
        pass

    host = Host()
    host.section, host.p = section, p
    s = 0.5 - 0.5 * np.cos(np.linspace(cut, np.pi - cut, rings))  # cosine spacing: fine at the ends
    ys = y0 + (y1 - y0) * s
    rows = [kit.ring_points(host, y, 0.0, sides) for y in ys]
    verts, faces = kit._loft(rows)
    ends = [np.array([section([y])[0][0], y, section([y])[2][0]]) for y in (y0, y1)]
    verts, faces = kit._cap_loops(verts, faces, [list(range(sides)), list(range((rings - 1) * sides, rings * sides))],
                                  centers=ends)
    return kit.Piece(verts, kit._orient_closed(verts, faces), slot)


class Trunk:
    """The torso from `bottom` (under the crotch) to `top` (the neck), through `rows` of half sizes
    (`_Profile`), superellipse sections of exponent `p`."""

    def __init__(self, rows, bottom=0.33, top=1.32, p=2.8, q=5.0, cz=0.0):
        self.profile, self.bottom, self.top, self.p, self.cz = _Profile(rows, q), bottom, top, p, cz

    def t(self, y):
        return (np.asarray(y, float) - self.bottom) / (self.top - self.bottom)

    def section(self, y):
        y = np.atleast_1d(np.asarray(y, float))
        a, front, back = self.profile(self.t(y))
        return np.zeros(len(y)), a, self.cz + (front - back) / 2, (front + back) / 2

    def mesh(self, slot="skin", sides=28, rings=16):
        return _closed_tube(self.section, self.bottom, self.top, self.p, sides, rings, slot)


class Limb:
    """An arm or a leg along its own vertical axis (root at y = 0, tip at `length`), through `rows` of half sizes
    (`_Profile`), placed at `root` pointing along `direction` with its front (local +z) toward `front`."""

    def __init__(self, rows, length, root, direction, front=(0.0, 0.0, 1.0), p=2.2, q=6.0):
        self.profile, self.length, self.p = _Profile(rows, q), length, p
        d = np.asarray(direction, float) / np.linalg.norm(direction)
        z = np.asarray(front, float) - (np.asarray(front, float) @ d) * d
        z /= np.linalg.norm(z)
        self.frame = kit.Frame(root, np.cross(d, z), d, z)

    def section(self, y):
        y = np.atleast_1d(np.asarray(y, float))
        a, front, back = self.profile(y / self.length)
        return np.zeros(len(y)), a, (front - back) / 2, (front + back) / 2

    def local_mesh(self, slot="skin", sides=14, rings=14):
        return _closed_tube(self.section, 0.0, self.length, self.p, sides, rings, slot)

    def place(self, piece):
        """A piece built in this limb's own axes, moved onto it."""
        return piece.moved(self.frame)


# ---------------------------------------------------------------------------
# Presets: half sizes along the torso, arm and leg (t from root to tip)
# ---------------------------------------------------------------------------
# The torso presets were shaped like the standard dressed bodies; the kit dresses its bodies with real garments and
# armor on top, so the bare torso is slimmed by this (across, front to back). A recipe's torso sets its own `slim`
# (a number or [across, depth]) for a bulkier or a skinnier type.
SLIM = (0.86, 0.92)

TORSOS = {  # (t from the crotch to the neck, half width, half depth in front, behind), like the standard: a rounded
    # box about as wide at the shoulders as at the hips, the belly flat; only `round` is meant to look fat
    "standard": [(0.0, 0.29, 0.25), (0.16, 0.35, 0.3, 0.29), (0.38, 0.35, 0.29, 0.28), (0.62, 0.37, 0.31, 0.28),
               (0.84, 0.37, 0.29, 0.27), (1.0, 0.27, 0.2)],
    "hero": [(0.0, 0.28, 0.24), (0.18, 0.32, 0.27, 0.26), (0.38, 0.31, 0.26, 0.25), (0.66, 0.4, 0.31, 0.28),
             (0.86, 0.42, 0.29, 0.27), (1.0, 0.3, 0.2)],
    "female": [(0.0, 0.29, 0.25), (0.16, 0.35, 0.28, 0.31), (0.42, 0.28, 0.24, 0.25), (0.66, 0.32, 0.27, 0.25),
               (0.86, 0.33, 0.26, 0.24), (1.0, 0.24, 0.19)],
    "brute": [(0.0, 0.3, 0.27), (0.18, 0.38, 0.33, 0.31), (0.42, 0.38, 0.33, 0.31), (0.68, 0.44, 0.36, 0.32),
              (0.86, 0.45, 0.33, 0.3), (1.0, 0.32, 0.22)],
    "round": [(0.0, 0.3, 0.28), (0.25, 0.42, 0.4), (0.5, 0.44, 0.42), (0.75, 0.4, 0.37), (1.0, 0.26, 0.24)],
    "skinny": [(0.0, 0.21, 0.18), (0.25, 0.25, 0.2), (0.55, 0.25, 0.2), (0.8, 0.28, 0.21), (1.0, 0.2, 0.16)],
    "goblin": [(0.0, 0.22, 0.2), (0.2, 0.26, 0.31, 0.2), (0.4, 0.27, 0.36, 0.21), (0.6, 0.24, 0.26, 0.2),  # a pot belly
               (0.82, 0.25, 0.2, 0.19), (1.0, 0.18, 0.14)],
}
ARMS = {  # (t from the shoulder to the wrist, half thickness across, front, back)
    "normal": [(0.0, 0.1, 0.1), (0.3, 0.085, 0.085), (0.48, 0.075, 0.075), (0.75, 0.078, 0.078), (1.0, 0.065, 0.065)],
    "thin": [(0.0, 0.075, 0.075), (0.3, 0.06, 0.06), (0.5, 0.05, 0.05), (0.75, 0.055, 0.055), (1.0, 0.045, 0.045)],
    "muscular": [(0.0, 0.16, 0.16), (0.3, 0.15, 0.155), (0.5, 0.115, 0.115), (0.72, 0.135, 0.13), (1.0, 0.1, 0.1)],
    "gorilla": [(0.0, 0.24, 0.24), (0.28, 0.22, 0.225), (0.48, 0.18, 0.185), (0.72, 0.225, 0.22), (1.0, 0.18, 0.17)],
    "bony": [(0.0, 0.06, 0.06), (0.25, 0.04, 0.04), (0.48, 0.06, 0.06), (0.7, 0.038, 0.038), (1.0, 0.05, 0.05)],
}
LEGS = {  # (t from the hip to the ankle, half thickness across, front, back)
    "normal": [(0.0, 0.11, 0.11), (0.35, 0.1, 0.1), (0.6, 0.085, 0.085), (0.78, 0.09, 0.092), (1.0, 0.07, 0.07)],
    "stocky": [(0.0, 0.14, 0.14), (0.35, 0.135, 0.135), (0.6, 0.115, 0.115), (0.78, 0.125, 0.125), (1.0, 0.1, 0.1)],
    "thin": [(0.0, 0.075, 0.075), (0.4, 0.065, 0.065), (0.6, 0.05, 0.05), (0.8, 0.055, 0.058), (1.0, 0.045, 0.045)],
}


# ---------------------------------------------------------------------------
# Hands and feet, built at the wrist / ankle in the limb's axes (y along the limb, z to the front)
# ---------------------------------------------------------------------------
_lump = kit.lump


def hand(style="mitten", size=1.0, slot="skin", nail="nail"):
    """
    A hand from the wrist (y = 0) along +y, palm toward local +x (down in the T-pose), thumb toward +z (front).
    `mitten`: the standard rounded paddle with a thumb; `fist`: a chunky closed fist that grips a weapon; `claw`: a
    mitten with three hooked claws; `bony`: a small knuckled skeleton hand.
    """
    s = size
    if style == "fist":
        palm = kit.rounded_box((0.15 * s, 0.17 * s, 0.17 * s), slot, center=(0, 0.1 * s, 0), roundness=0.45)
        knuckles = kit.rounded_box((0.07 * s, 0.05 * s, 0.17 * s), slot, center=(0.07 * s, 0.17 * s, 0), roundness=0.5)
        thumb = _lump((0.03 * s, 0.1 * s, 0.09 * s), (0.04 * s, 0.06 * s, 0.035 * s), slot)
        return kit.Piece.merge(palm, knuckles, thumb)
    if style == "bony":
        palm = _lump((0, 0.06 * s, 0), (0.035 * s, 0.06 * s, 0.06 * s), slot)
        fingers = [kit.tube(kit.bezier((0, 0.1 * s, dz * s), (0.01 * s, 0.17 * s, dz * s), (0.04 * s, 0.2 * s, dz * s), 3),
                            0.016 * s, slot, sides=5, taper=0.4) for dz in (-0.04, 0.0, 0.04)]
        return kit.Piece.merge(palm, *fingers)
    paddle = _lump((0, 0.11 * s, 0), (0.075 * s, 0.12 * s, 0.1 * s), slot)
    thumb = _lump((0.0, 0.08 * s, 0.09 * s), (0.04 * s, 0.055 * s, 0.04 * s), slot)
    parts = [paddle, thumb]
    if style == "claw":
        for dz in (-0.05, 0.0, 0.05):
            base = np.array([0.0, 0.18 * s, dz * s])
            parts.append(kit.horn(base, base + [0.01 * s, 0.07 * s, 0], base + [0.07 * s, 0.1 * s, 0], 0.02 * s, nail,
                                  segments=4, sides=4))  # hooking toward the palm
    return kit.Piece.merge(*parts)


def foot(style="boot", size=1.0, slot="boot", sole="sole"):
    """
    A foot from the ankle (y = 0) down the leg's axis (+y toward the ground), toes toward local +z (front).
    `boot`: the standard chunky boot with a sole; `bare`: a rounded foot with toes; `paw`: a beast's padded foot.
    """
    s = size
    if style == "bare":
        sole_ = _lump((0, 0.1 * s, 0.07 * s), (0.08 * s, 0.05 * s, 0.15 * s), slot)
        heel = _lump((0, 0.06 * s, -0.02 * s), (0.07 * s, 0.07 * s, 0.07 * s), slot)
        toes = [_lump((dx * s, 0.12 * s, 0.2 * s), (0.022 * s, 0.025 * s, 0.03 * s), slot, n=8, lat=(-50, -15, 15, 50))
                for dx in (-0.05, -0.017, 0.017, 0.05)]
        return kit.Piece.merge(sole_, heel, *toes)
    if style == "paw":
        pad = _lump((0, 0.1 * s, 0.06 * s), (0.09 * s, 0.05 * s, 0.13 * s), slot)
        toes = [_lump((dx * s, 0.11 * s, 0.17 * s), (0.03 * s, 0.035 * s, 0.035 * s), slot, n=6, lat=(-45, 0, 45))
                for dx in (-0.055, 0.0, 0.055)]
        return kit.Piece.merge(pad, *toes)
    # the standard chunky boot: a rounded shaft, a toe cap swelling out in front, a flat sole a little wider than both
    shaft = kit.rounded_box((0.17 * s, 0.15 * s, 0.17 * s), slot, center=(0, 0.04 * s, 0), roundness=0.45)
    toe = kit.rounded_box((0.18 * s, 0.11 * s, 0.27 * s), slot, center=(0, 0.095 * s, 0.07 * s), roundness=0.55)
    sole_ = kit.rounded_box((0.19 * s, 0.04 * s, 0.3 * s), sole, center=(0, 0.14 * s, 0.07 * s), roundness=0.3)
    return kit.Piece.merge(shaft, toe, sole_)


# ---------------------------------------------------------------------------
# A whole body
# ---------------------------------------------------------------------------
def _rows(presets, spec):
    return presets[spec] if isinstance(spec, str) else spec


def build(recipe):
    """
    `(hosts, pieces)` for a recipe: `torso` (a `TORSOS` name, rows, or the `Trunk` arguments with `rows` a name or
    rows, and `slim`: see `SLIM`), `arms` = dict(style (an `ARMS` name or rows), hand, hand_size, hand_slot, scale (thickness)), `legs` =
    dict(style, foot, foot_size, foot_slot, scale), `skin` the skin slot. Pieces are
    the standard: `Body`, `ArmLeft`, `ArmRight` (with the hand), `LegLeft`, `LegRight` (with the foot); hosts are the
    `Trunk` and the left `Limb`s (`torso`, `arm`, `leg`) for accessories.
    """
    skin = recipe.get("skin", "skin")
    t = recipe.get("torso", "standard")
    t = dict(rows=t) if isinstance(t, (str, list)) else dict(t)
    sw, sd = np.broadcast_to(np.asarray(t.pop("slim", SLIM), float), (2,))  # the bare body under clothes and armor
    rows = [(r[0], r[1] * sw, *(v * sd for v in r[2:])) for r in _rows(TORSOS, t["rows"])]
    trunk = Trunk(**dict(t, rows=rows))
    arms, legs = dict(recipe.get("arms", {})), dict(recipe.get("legs", {}))
    for spec, known in ((arms, {"style", "hand", "hand_size", "hand_slot", "scale", "length"}),
                        (legs, {"style", "foot", "foot_size", "foot_slot", "scale"})):
        if set(spec) - known:
            raise ValueError(f"unknown body options {sorted(set(spec) - known)}")

    def scaled(rows, k):
        return [(r[0],) + tuple(v * k for v in r[1:]) for r in rows]

    sh, wr = np.array(JOINTS["shoulder"]), np.array(JOINTS["wrist"])
    root = sh - [0.08, 0.0, 0.0]  # the shoulder sinks into the torso
    arm_len = np.linalg.norm(wr - root) * arms.get("length", 1.0)
    arm = Limb(scaled(_rows(ARMS, arms.get("style", "normal")), arms.get("scale", 1.0)), arm_len,
               root, (1.0, 0.0, 0.0))
    hand_piece = hand(arms.get("hand", "mitten"), arms.get("hand_size", 1.0), arms.get("hand_slot", skin))
    left_arm = kit.Piece.merge(arm.place(arm.local_mesh(skin)),
                               arm.place(kit.Piece(hand_piece.verts + [0.0, arm.length - 0.02, 0.0], hand_piece.faces,
                                                   hand_piece.slots)))
    hp, an = np.array(JOINTS["hip"]), np.array(JOINTS["ankle"])
    top = hp + [0.0, 0.06, 0.0]  # the thigh starts inside the hips
    leg = Limb(scaled(_rows(LEGS, legs.get("style", "normal")), legs.get("scale", 1.0)), np.linalg.norm(an - top),
               top, (0.0, -1.0, 0.0))
    foot_piece = foot(legs.get("foot", "boot"), legs.get("foot_size", 1.0), legs.get("foot_slot", "boot"
                                                                                  if legs.get("foot", "boot") == "boot"
                                                                                  else skin))
    left_leg = kit.Piece.merge(leg.place(leg.local_mesh(skin)),
                               leg.place(kit.Piece(foot_piece.verts + [0.0, leg.length - 0.02, 0.0], foot_piece.faces,
                                                   foot_piece.slots)))
    pieces = dict(Body=trunk.mesh(skin), ArmLeft=left_arm, ArmRight=left_arm.mirrored(), LegLeft=left_leg,
                  LegRight=left_leg.mirrored())
    return dict(torso=trunk, arm=arm, leg=leg), pieces


def catalog():
    """Each torso, arm, leg, hand and foot preset on a plain body."""
    for name in TORSOS:
        _, pcs = build(dict(torso=name))
        yield f"torso {name}", kit.Piece.merge(*pcs.values()), None, COLORS
    for name in ARMS:
        _, pcs = build(dict(arms=dict(style=name, hand={"bony": "bony", "muscular": "fist", "thin": "claw"}.get(name, "mitten"))))
        yield f"arms {name}", kit.Piece.merge(*pcs.values()), None, COLORS
    for name in LEGS:
        _, pcs = build(dict(legs=dict(style=name, foot={"thin": "bare", "stocky": "paw"}.get(name, "boot"))))
        yield f"legs {name}", kit.Piece.merge(*pcs.values()), None, COLORS
