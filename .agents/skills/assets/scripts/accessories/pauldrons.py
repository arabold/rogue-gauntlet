"""
Pauldrons (shoulder plates): a shell over the shoulder, sloping down the arm, in variants (`VARIANTS`). Built for
the character's left shoulder (`build(top, size, ...)`, the arm along +x like KayKit's T-pose) and mirrored for the
right (`kit.Piece.mirrored`). Material slots: `plate`, `trim`, `rivet`, `spike`, `fur`, `bone`.
"""

import numpy as np

from accessories import kit
from accessories.belts import skull


def _shell(size, slot, lat=-8.0, thickness=0.03, scale=(1.0, 0.7, 0.95), start=0.0, sides=14):
    rx, ry, rz = (size * k for k in scale)
    return kit.cap(rx, ry, rz, lat, thickness, slot, sides=sides, rings=5, p=2.4, start=start)


def _rim(size, slot, lat=-2.0, scale=(1.0, 0.7, 0.95), width=0.03, height=0.055, drop=0.0, flare=1.0, sides=14):
    """A trim band on the shell just above its lower edge (`flare` > 1 widens it into a turned-out lip)."""
    rx, ry, rz = (size * k for k in scale)
    L = np.radians(lat)
    th = 2 * np.pi * np.arange(sides) / sides
    c, s = np.cos(th), np.sin(th)
    ux, uz = np.sign(c) * np.abs(c) ** (2 / 2.4), np.sign(s) * np.abs(s) ** (2 / 2.4)
    path = np.column_stack([flare * rx * np.cos(L) * ux, np.full(sides, ry * np.sin(L) - drop),
                            flare * rz * np.cos(L) * uz])
    return kit.sweep(path, kit.rect_profile(width, height, 0.3 * height), slot, closed=True)


def _spikes(size, slot, count=3, height=0.55, radius=0.12, along=0.55):
    """Spikes along the shell's top ridge, from the neck side out along the arm, leaning outward."""
    out = []
    for i, x in enumerate(np.linspace(-along, along, count) * size):
        y = 0.7 * size * np.sqrt(max(0.0, 1 - (x / size) ** 2)) - 0.01
        h = height * size * (1.0 if i == count // 2 else 0.75)
        out.append(kit.spike((x, y, 0.0), (x + 0.25 * h, y + h, 0.0), radius * size, slot))
    return out


def _fur(size, slot, count=16):
    """A thick ruff of fur along the shell's neck side: two rows of short, wide tufts fanning out and up."""
    out = []
    for row, (r, lift, length) in enumerate(((0.8, 0.2, 0.32), (0.95, 0.05, 0.26))):
        for i in range(count):
            a = np.pi * (0.45 + 1.1 * (i + 0.5 * row) / count)  # the half facing the neck (-x)
            base = np.array([r * size * np.cos(a), lift * size, r * 0.95 * size * np.sin(a)])
            out_dir = np.array([np.cos(a), 0.0, np.sin(a)])
            tip = base + length * size * (0.55 * out_dir + [0.0, 0.85, 0.0]) * (0.75 + 0.5 * ((i * 7 + row) % 3) / 2)
            out.append(kit.spike(base, tip, 0.2 * size, slot, sides=3, twist=37.0 * i))
    return out


VARIANTS = {
    "dome": dict(plate="metal", rim="dark", rivet="dark"),
    "layered": dict(plate="metal", lames=3, rim="dark"),
    "spiked": dict(plate="dark", rim="metal", spikes="metal"),
    "flared": dict(plate="metal", rim="metal", flare=1.18, spikes="metal", spike_count=2, rivet="gold"),
    "fur": dict(plate="leather", rim="dark", fur="fur"),
    "skull": dict(plate="dark", rim="bone", skull="bone"),
}


def build(top, size=0.24, variant="dome", tilt=-15.0, **overrides):
    """
    The left pauldron `variant` with the top of its shell at `top` (rig units), `size` its half width along the arm,
    sloping `tilt` degrees down the arm in the T-pose. It turns half as far as the arm, so it is authored tipped up
    (`tilt` < 0): with the arms resting at the sides, it lies close to level on the shoulder, like KayKit's.
    `overrides` replace the variant's parameters.
    """
    v = {**VARIANTS[variant], **overrides}
    parts = []
    lames = v.get("lames", 1)
    for i in range(lames):  # each lower lame steps out along the arm and down, a little wider
        k = 1.0 + 0.16 * i
        shell = _shell(size * k, v["plate"], start=0.0 if i == 0 else 30.0 + 12 * i)
        rim = _rim(size * k, v["rim"]) if v.get("rim") else None
        lame = kit.Piece.merge(shell, rim)
        parts.append(kit.Piece(lame.verts + [0.22 * size * i, -0.2 * size * i, 0.0], lame.faces, lame.slots))
    if v.get("flare"):
        parts.append(_rim(size, v["rim"], flare=v["flare"], width=0.07, drop=0.02))
    if v.get("spikes"):
        parts += _spikes(size, v["spikes"], count=v.get("spike_count", 3), height=v.get("spike_height", 0.55))
    if v.get("rivet"):
        parts.append(kit.stud(0.12 * size, 0.02, v["rivet"]).moved(
            kit.Frame((0.0, 0.7 * size - 0.004, 0.0), [1, 0, 0], [0, 0, -1], [0, 1, 0])))
    if v.get("fur"):
        parts += _fur(size, v["fur"])
    if v.get("skull"):
        parts.append(skull(0.8 * size, v["skull"]).moved(  # on the outer slope, looking out along the arm
            kit.Frame((0.5 * size, 0.5 * size, 0.0), [0, 0, -1], [0, 1, 0], [1, 0, 0]).rotated(-40, "x")))
    piece = kit.Piece.merge(*parts)
    frame = kit.Frame(np.asarray(top, float) - [0.0, 0.7 * size, 0.0], [1, 0, 0], [0, 1, 0], [0, 0, 1]).rotated(
        -tilt, "z")
    return piece.moved(frame)


def catalog():
    """Every variant on the mannequin's left shoulder (`catalog.shoulder_stand`, mirrored to the left)."""
    from accessories.catalog import shoulder_stand
    stand = shoulder_stand().mirrored()
    for name in VARIANTS:
        yield name, build((0.38, 1.33, 0.0), variant=name), stand
