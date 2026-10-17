"""Intricate extremity prefabs — procedural hands (mitten, fist, claw, bony) and feet (boot, bare, paw)."""

import numpy as np
from core import kit

_lump = kit.lump
HAND_STYLES = ("mitten", "fist", "claw", "bony")
FOOT_STYLES = ("boot", "bare", "paw")


def hand(style="mitten", size=1.0, slot="skin", nail="nail"):
    """
    A hand from the wrist (y = 0) along +y, palm toward local +x (down in the T-pose), thumb toward +z (front).
    `mitten`: rounded paddle with a thumb; `fist`: a chunky closed fist that grips a weapon; `claw`: a
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
                                  segments=4, sides=4))
    return kit.Piece.merge(*parts)


def foot(style="boot", size=1.0, slot="boot", sole="sole"):
    """
    A foot from the ankle (y = 0) down the leg axis (+y toward the ground), toes toward local +z (front).
    `boot`: chunky boot with a sole; `bare`: rounded foot with toes; `paw`: beast padded foot.
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
    shaft = kit.rounded_box((0.17 * s, 0.15 * s, 0.17 * s), slot, center=(0, 0.04 * s, 0), roundness=0.45)
    toe = kit.rounded_box((0.18 * s, 0.11 * s, 0.27 * s), slot, center=(0, 0.095 * s, 0.07 * s), roundness=0.55)
    sole_ = kit.rounded_box((0.19 * s, 0.04 * s, 0.3 * s), sole, center=(0, 0.14 * s, 0.07 * s), roundness=0.3)
    return kit.Piece.merge(shaft, toe, sole_)
