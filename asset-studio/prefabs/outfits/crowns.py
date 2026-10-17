"""
Crowns: a band resting on the head with something standing on it (points, spikes, horns, a gem), in variants
(`VARIANTS`). Built around the head's cross-sections (`build(head, y0, y1, ...)`), so the band follows the skull
where it sits. Bands are crafted, not plain rings: raised `rims` along both edges, optional `rivets`, `finials`
on the points and a set `front_gem`. Material slots: `band`, `points`, `gem`, `horn`.
"""

import numpy as np

from core import kit

FRONT = np.pi / 2
COLORS = {"gold": (232, 182, 64), "gold_dark": (176, 128, 40), "gem": (200, 40, 60)}


def _top_frame(head, y, theta, out):
    return kit.band_frame(head, y, theta, out)


def ridged(width, height, depth, slot, x0=0.0):
    """A crown point with a raised ridge down its middle (faceted like stylized's): back face flat on the band's
    plane, front rising to the ridge (local: x along the band, y up, z out), centred at `x0`."""
    w = width / 2
    pts = [(x0 - w, 0, 0), (x0 + w, 0, 0), (x0, height, 0), (x0 - 0.8 * w, 0, depth), (x0 + 0.8 * w, 0, depth),
           (x0, 0.92 * height, 0.5 * depth), (x0, 0.1 * height, 1.9 * depth), (x0, 0.55 * height, 1.3 * depth)]
    return kit.hull(pts, slot)


def plate_point(width, height, depth, slot, notch=0.0):
    """A ridged crown point standing on the band, or with a notched tip (`notch` > 0: two tips, like a fleur)."""
    if notch <= 0:
        return ridged(width, height, depth, slot)
    else:
        left = [(-width / 2, 0), (0, 0), (0, height * (1 - notch)), (-width * 0.18, height)]
        right = [(0, 0), (width / 2, 0), (width * 0.18, height), (0, height * (1 - notch))]
        return kit.Piece.merge(kit.prism(left, depth, slot, bevel=0.3 * depth),
                               kit.prism(right, depth, slot, bevel=0.3 * depth))


def _points(head, y1, thickness, count, height, width, slot, spread=2 * np.pi, front_scale=1.25, notch=0.0,
            depth=None, start=FRONT, jitter=None):
    """`count` plate points spread over `spread` radians of the band's top edge, centred on the front."""
    depth = depth or 0.8 * thickness
    out = []
    angles = start + (np.linspace(-spread / 2, spread / 2, count) if spread < 2 * np.pi
                      else 2 * np.pi * np.arange(count) / count)
    for i, theta in enumerate(angles):
        k = front_scale if abs(((theta - FRONT + np.pi) % (2 * np.pi)) - np.pi) < 1e-6 else 1.0
        h = height * k * (1.0 if jitter is None else jitter[i % len(jitter)])
        f = _top_frame(head, y1 - 0.15 * height, theta, 0.5 * thickness - 0.5 * depth)
        out.append(plate_point(width * k, h, depth, slot, notch).moved(f))
    return out


def _spikes(head, y1, thickness, count, height, radius, slot, lean=0.15, jitter=None, curved=False):
    """Spikes all around the band's top edge, straight (iron) or `curved` outward like little horns (bone)."""
    out = []
    for i in range(count):
        theta = FRONT + 2 * np.pi * i / count
        f = _top_frame(head, y1 - 0.5 * radius, theta, 0.5 * thickness)
        h = height * (1.0 if jitter is None else jitter[i % len(jitter)])
        if curved:
            out.append(kit.horn(f.origin, f.to_world([0.0, 0.55 * h, 0.05 * h]), f.to_world([0.0, h, 0.35 * h]),
                                radius, slot, segments=5, sides=5))
        else:
            out.append(kit.spike(f.origin, f.to_world([0.0, h, lean * h]), radius, slot))
    return out


def _gems(head, y, thickness, count, size, slot, spread=2 * np.pi, cut="oval"):
    angles = FRONT + (np.linspace(-spread / 2, spread / 2, count) if spread < 2 * np.pi
                      else 2 * np.pi * np.arange(count) / count)
    return [kit.gem(size, slot, cut=cut).moved(_top_frame(head, y, theta, thickness)) for theta in angles]


def _horns(head, y0, y1, thickness, length, radius, slot, curl=1.0, angle=1.25):
    """A pair of horns from the band's sides (`angle` radians from the front), curving outward and up (and back for
    `curl` > 1)."""
    out = []
    for side in (-1, 1):
        f = _top_frame(head, 0.5 * (y0 + y1), FRONT - side * angle, 0.5 * thickness)
        base = f.origin
        ctrl = f.to_world([side * -0.2 * length, 0.25 * length, 0.55 * length])
        tip = f.to_world([side * -0.05 * length, 0.95 * length, (0.15 + 0.3 * (curl - 1)) * length])
        out.append(kit.horn(base, ctrl, tip, radius, slot, segments=8, sides=6))
    return out


VARIANTS = {
    "royal": dict(band="gold", points=5, point="gold", gems="gem", notch=0.0, finials="gold"),
    "fleur": dict(band="gold", points=5, point="gold", gems="gem", notch=0.35),
    "jagged": dict(band="gold", points=7, point="gold", jitter=(1.0, 0.7, 0.9, 0.65, 0.95, 0.75, 0.85), rivets=8,
                   rivet="gold_dark", rim="gold_dark", front_gem="gem"),
    "spiked": dict(band="dark", spikes=9, spike="dark", spike_height=0.26, spike_radius=0.5),
    "horned": dict(band="bone", horns="bone", points=3, point="bone", spread=1.2, gems="gem", gem_count=1),
    "bone": dict(band="bone", spikes=7, spike="bone", spike_height=0.2, spike_radius=0.42, curved=True,
                 spike_jitter=(1.0, 0.65, 0.85, 0.55, 0.9, 0.6, 0.8)),
    "circlet": dict(band="gold", height_scale=0.45, front_gem="gem"),
}


def build(head, y0, y1, thickness=0.035, variant="royal", **overrides):
    """
    The crown `variant` on `head` with its band from `y0` up to `y1` (rig units, the head's frame), `thickness` off
    the skull. Points scale with the band's height, times `point_scale` (concepts draw them tall). `overrides`
    replace the variant's parameters.
    """
    v = {**VARIANTS[variant], **overrides}
    y1 = y0 + (y1 - y0) * v.get("height_scale", 1.0)
    band_h = y1 - y0
    tall = v.get("point_scale", 1.0)
    parts = [kit.host_band(head, y0, y1, thickness, v["band"], bevel=0.4, sides=32)]
    if v.get("rims", True):  # raised mouldings along both edges: a crafted band, not a plain ring
        rim = v.get("rim", v["band"])
        r = 0.16 * band_h
        parts += [kit.host_band(head, y, y + r, thickness + 0.018, rim, bevel=0.5, sides=32) for y in (y0, y1 - r)]
    if v.get("rivets"):  # studs round the middle of the band
        for theta in FRONT + 2 * np.pi * (np.arange(v["rivets"]) + 0.5) / v["rivets"]:
            parts.append(kit.stud(0.17 * band_h, 0.012, v.get("rivet", v["band"]), sides=8).moved(
                _top_frame(head, 0.5 * (y0 + y1), theta, thickness)))
    if v.get("points"):
        spread = v.get("spread", 2 * np.pi)
        count = v["points"]
        height = 1.6 * band_h * tall
        pts = _points(head, y1, thickness, count, height, 2.0 * band_h if spread >= 2 * np.pi else 1.4 * band_h,
                      v["point"], spread=spread, notch=v.get("notch", 0.0), jitter=v.get("jitter"))
        parts += pts
        if v.get("finials"):  # a ball on every tip
            for p in pts:
                tip = p.verts[np.argmax(p.verts[:, 1])]
                parts.append(kit.lump(tip + [0, 0.25 * band_h, 0], (0.26 * band_h,) * 3, v["finials"], n=8,
                                      lat=(-50, -15, 15, 50)))
        if v.get("gems"):
            parts += _gems(head, 0.5 * (y0 + y1), thickness, v.get("gem_count", count), 0.38 * band_h, v["gems"],
                           spread=spread)
    if v.get("spikes"):
        parts += _spikes(head, y1, thickness, v["spikes"], v.get("spike_height", 0.2) * tall,
                         v.get("spike_radius", 0.35) * band_h, v["spike"], jitter=v.get("spike_jitter"),
                         curved=v.get("curved", False))
    if v.get("horns"):
        parts += _horns(head, y0, y1, thickness, 0.42, 0.075, v["horns"])
    if v.get("front_gem"):
        mount = kit.gem(0.75 * band_h * 2.2, v["band"], cut="diamond", depth=0.03).moved(
            _top_frame(head, 0.5 * (y0 + y1), FRONT, thickness))
        stone = kit.gem(0.45 * band_h * 2.2, v["front_gem"], cut="diamond", depth=0.05).moved(
            _top_frame(head, 0.5 * (y0 + y1), FRONT, thickness + 0.012))
        parts += [mount, stone]
    res = kit.Piece.merge(*parts)
    tilt = v.get("tilt", 0.0)
    y_offset = v.get("y_offset", 0.0)
    if abs(tilt) > 1e-4 or abs(y_offset) > 1e-4:
        c = np.array([0.0, getattr(head, "cy", 0.5 * (y0 + y1)), 0.0])
        a_x = np.radians(tilt)
        Rx = np.array([[1, 0, 0], [0, np.cos(a_x), -np.sin(a_x)], [0, np.sin(a_x), np.cos(a_x)]])
        res = kit.Piece((res.verts - c) @ Rx.T + c + [0.0, y_offset, 0.0], res.faces, res.slots)
        if getattr(res, "normals", None) is not None:
            res.normals = res.normals @ Rx.T
    return res


def catalog():
    """Every variant on the mannequin head (`catalog.HEAD`)."""
    from core.catalog import HEAD
    host = HEAD.mesh()
    cy = 1.75
    for name in VARIANTS:
        yield name, build(HEAD, cy + 0.10, cy + 0.22, variant=name), host
