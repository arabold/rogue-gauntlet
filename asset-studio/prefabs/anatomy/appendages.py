"""Creature appendages: wings, tails, exposed ribs, horns, and spines.

Provides modular monster and creature anatomy attachments that can be fitted to
body and head hosts:
- `wings`: Bat-style membranous wings with articulated finger bones and claws.
- `tail`: Long whip-like tail with spade arrowhead tip and adjustable curl.
- `ribs`: Starved or skeletal ribs wrapping around a torso.
- `horn_curved`: Curved beast horns on a cranium or shoulder.
"""

import numpy as np

from core import kit
from prefabs import library


def ribs(trunk, y0, y1, count=4, spread=55.0, thickness=0.014, slot="skin_dark", height=0.03):
    """A starved or undead creature's ribs showing through skin or exposed bone:
    `count` raised arcs across the chest between `y0` and `y1`, each `spread`
    degrees to either side of the breastbone."""
    out = []
    for i in range(count):
        y = y1 - (y1 - y0) * i / max(count - 1, 1)
        for sgn in (-1, 1):
            theta = np.pi / 2 + sgn * np.radians(np.linspace(8, spread, 9))
            out.append(kit.host_band(trunk, y - height / 2, y + height / 2, thickness, slot, theta=theta, bevel=0.5,
                                     tilt=0.0))
    return kit.Piece.merge(*out)


def wings(trunk, y, span=0.8, fingers=3, sweep=15.0, scallop=0.28, membrane="wing", bone="wing_bone",
          claw="wing_claw"):
    """
    Bat wings attached to the upper back at height `y`:
    Arm bone runs outward and up from each shoulder blade to a wrist with a hooked
    thumb claw, `fingers` finger bones fan out from the wrist to the tips, and a
    scalloped membrane stretches between them.
    """
    root, wrist = np.array([0.0, 0.0]), np.array([0.55, 0.42]) * span
    tips = [wrist + np.array([np.cos(a), np.sin(a)]) * span * (0.62 - 0.1 * i)
            for i, a in enumerate(np.radians(np.linspace(10, -75, fingers)))]
    flank = np.array([0.06, -0.4]) * span
    outline = [root + [0.0, 0.05 * span], wrist + [0.0, 0.03 * span]]
    edge = tips + [flank]
    for a, b in zip(edge[:-1], edge[1:]):
        mid = 0.5 * (a + b)
        pull = (wrist - mid) / np.linalg.norm(wrist - mid) * scallop * np.linalg.norm(b - a)
        outline.append(a)
        outline += [a + (b - a) * t + np.sin(np.pi * t) * pull for t in np.linspace(0.0, 1.0, 7)[1:-1]]
    outline.append(flank)
    parts = [kit.extrude(np.asarray(outline), 0.012, membrane)]
    r_arm, r_finger = 0.03 * span / 0.8, 0.018 * span / 0.8
    parts.append(kit.sweep(np.array([(0.0, 0.03 * span, 0.006), (0.25 * wrist[0], 0.55 * wrist[1], 0.006),
                                     (wrist[0], wrist[1] + 0.02 * span, 0.006)]), kit.ring_profile(r_arm, r_arm, 6),
                           bone, up=(0.0, 0.0, 1.0), scale=np.array([1.0, 0.85, 0.7])))
    parts.append(kit.lump((wrist[0], wrist[1] + 0.01 * span, 0.006), (1.2 * r_arm, 1.2 * r_arm, 1.2 * r_arm), bone,
                          n=6, lat=(-45, 0, 45)))
    for t in tips:
        parts.append(kit.sweep(np.array([(wrist[0], wrist[1], 0.006), (t[0], t[1], 0.006)]),
                               kit.ring_profile(r_finger, r_finger, 5), bone, up=(0.0, 0.0, 1.0),
                               scale=np.array([1.0, 0.35])))
    parts.append(kit.horn(np.array([wrist[0], wrist[1] + 0.02 * span, 0.006]),
                          np.array([wrist[0] + 0.02, wrist[1] + 0.12 * span, 0.006]),
                          np.array([wrist[0] - 0.06 * span, wrist[1] + 0.17 * span, 0.006]), 0.022 * span / 0.8, claw,
                          sides=5))
    one = kit.Piece.merge(*parts)
    at = kit.band_frame(trunk, y, 3 * np.pi / 2 + np.radians(28), 0.0).origin
    x = np.array([np.cos(np.radians(sweep)), 0.0, -np.sin(np.radians(sweep))])
    R = np.stack([x, [0.0, 1.0, 0.0], np.cross(x, [0.0, 1.0, 0.0])], 1)
    left = kit.Piece(one.verts @ R.T + at, one.faces, one.slots)
    return kit.Piece.merge(left, left.mirrored())


def tail(trunk, y, length=0.75, radius=0.035, tip="tail_tip", slot="skin", curl=0.35):
    """A long thin tail from the small of the back at `y`, swinging down, back and up again,
    with an arrowhead tip."""
    f = kit.band_frame(trunk, y, 3 * np.pi / 2, -0.02)
    P0 = f.origin
    L = length
    path = kit.spline([P0, P0 + [0.05 * L, -0.25 * L, -0.25 * L], P0 + [0.25 * L, -0.25 * L, -0.65 * L],
                       P0 + [0.35 * L, curl * L - 0.1 * L, -0.85 * L]], 14)
    t = np.linspace(0.0, 1.0, len(path))
    parts = [kit.sweep(path, kit.ring_profile(radius, radius, 6), slot, up=(0.0, 1.0, 0.0), scale=1.0 - 0.5 * t)]
    d = path[-1] - path[-2]
    d /= np.linalg.norm(d)
    flat = np.cross(d, [1.0, 0.0, 0.0]) if abs(d[0]) < 0.9 else np.array([0.0, 1.0, 0.0])
    parts.append(kit.prism([(-0.07, -0.02), (0.07, -0.02), (0.0, 0.13)], 0.03, tip, bevel=0.006).moved(
        kit.Frame.make(path[-1], flat, up=d)))
    return kit.Piece.merge(*parts)


def horn(center=(0.0, 0.0, 0.0), size=0.25, slot="horn"):
    """Curved beast horn scaled and placed at `center`."""
    return library.load("horn_curved", slot=slot, scale=size, offset=center)


APPENDAGES = {
    "wings": wings,
    "tail": tail,
    "ribs": ribs,
    "horn": horn,
}


def catalog():
    """Yield creature appendages on neutral stand-in body hosts."""
    from prefabs.anatomy import bodies as body
    hosts, _ = body.build({"torso": "standard"})
    t = hosts["torso"]

    colors = {
        "wing": (55, 45, 65),
        "wing_bone": (40, 32, 48),
        "wing_claw": (220, 200, 170),
        "skin": (120, 140, 100),
        "skin_dark": (75, 95, 60),
        "tail_tip": (45, 35, 30),
        "horn": (224, 210, 180),
    }

    yield "bat wings", wings(t, 1.05, span=0.65), t.mesh(), colors
    yield "spade tail", tail(t, 0.62, length=0.65), t.mesh(), colors
    yield "exposed ribs", ribs(t, 0.78, 1.08, count=4), t.mesh(), colors
    yield "curved horn", horn(size=0.35), None, colors
