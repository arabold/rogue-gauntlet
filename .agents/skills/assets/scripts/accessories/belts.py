"""
Belts: a band around the waist with a buckle at the front, in variants (`VARIANTS`). Built around a host's
cross-sections (`build(host, y, ...)`), so the band follows the waist's taper and sits flush on it.

Material slots: `band` (the strap, usually leather), `buckle` (metal, gold or bone), plus `stud`, `gem` and `trim`.
A variant maps them to `kit.SLOTS` names; the monster build recolors them from its drawing.
"""

import numpy as np

from accessories import kit

FRONT = np.pi / 2  # angle of the front (+z) on a host ring


def _band(host, y, height, thickness, slot, sides=32, swell=0.0):
    """The leather band: rounded edges, and with `swell` taller at the front (KayKit's barbarian belt)."""
    return kit.host_band(host, y - height / 2, y + height / 2, thickness, slot, bevel=0.6, sides=sides,
                         swell=swell * height)


def _fur_edges(host, y, height, thickness, swell, slot, count=26, length=0.055):
    """Fur teeth along the band's top edge pointing up and its bottom edge pointing down, rooted on the band's
    surface so no gap opens between them, following its `swell` toward the front."""
    out = []
    cx, a, cz, b = (float(v[0]) for v in host.section([y]))
    w = np.pi * (3 * (a + b) - np.sqrt((3 * a + b) * (a + 3 * b))) / count
    for k in range(count):
        theta = 2 * np.pi * (k + 0.5) / count
        grow = swell * max(np.cos(theta - FRONT), 0.0) ** 2
        for sgn in (1, -1):
            edge = y + sgn * (height / 2 + grow / 2 - 0.012)
            tooth = kit.prism([(-0.55 * w, 0.0), (0.55 * w, 0.0), (0.0, sgn * length)][::sgn], 0.022, slot,
                              bevel=0.004)
            out.append(tooth.moved(_at(host, edge, theta, thickness - 0.012)))
    return out


def _at(host, y, theta, out):
    return kit.band_frame(host, y, theta, out)


def frame_buckle(height, slot):
    """A rectangular metal frame with a prong, KayKit's plain buckle (local axes: x along the belt, z out)."""
    w, h, bar, d = 1.3 * height, 1.35 * height, 0.27 * height, 0.045
    bars = [kit.bevel_box((w, bar, d), 0.3 * bar, slot, center=(0, (h - bar) / 2, d / 2)),
            kit.bevel_box((w, bar, d), 0.3 * bar, slot, center=(0, -(h - bar) / 2, d / 2)),
            kit.bevel_box((bar, h, d), 0.3 * bar, slot, center=((w - bar) / 2, 0, d / 2)),
            kit.bevel_box((bar, h, d), 0.3 * bar, slot, center=(-(w - bar) / 2, 0, d / 2)),
            kit.bevel_box((0.55 * w, 0.32 * bar, 0.6 * d), 0.1 * bar, slot, center=(0, 0, 0.45 * d))]
    return kit.Piece.merge(*bars)


def plate_buckle(height, slot, rivet_slot):
    """A big chamfered plate with a rivet in each corner."""
    w, h, d = 1.45 * height, 1.25 * height, 0.04
    plate = kit.bevel_box((w, h, d), 0.012, slot, center=(0, 0, d / 2))
    rivets = [kit.stud(0.11 * height, 0.012, rivet_slot).moved(kit.Frame((sx * 0.36 * w, sy * 0.34 * h, d),
                                                                           [1, 0, 0], [0, 1, 0], [0, 0, 1]))
              for sx in (-1, 1) for sy in (-1, 1)]
    return kit.Piece.merge(plate, *rivets)


def skull(height, slot="bone", eye_slot="dark"):
    """A chunky skull facing +z: rounded cranium, a jaw block, dark eye sockets and nose."""
    s = 1.35 * height
    th = np.radians(np.arange(0, 360, 36))
    pts = []
    for lat, k in ((-30, 0.85), (0, 1.0), (35, 0.85), (65, 0.45)):
        L = np.radians(lat)
        pts += [(0.5 * s * k * np.cos(t), 0.12 * s + 0.42 * s * np.sin(L), 0.32 * s * k * np.sin(t) + 0.3 * s)
                for t in th]
    cranium = kit.hull(pts, slot)
    jaw = kit.bevel_box((0.62 * s, 0.32 * s, 0.42 * s), 0.05 * s, slot, center=(0, -0.3 * s, 0.33 * s))
    front = 0.3 * s + 0.31 * s
    eyes = [kit.gem(0.12 * s, eye_slot, depth=0.03 * s).moved(kit.Frame((sx * 0.2 * s, 0.12 * s, front),
                                                                         [1, 0, 0], [0, 1, 0], [0, 0, 1]))
            for sx in (-1, 1)]
    nose = kit.prism([(-0.05 * s, 0), (0.05 * s, 0), (0, -0.09 * s)], 0.03 * s, eye_slot).moved(  # pointing down
        kit.Frame((0, -0.12 * s, front - 0.03 * s), [1, 0, 0], [0, 1, 0], [0, 0, 1]))
    teeth = [kit.bevel_box((0.06 * s, 0.1 * s, 0.03 * s), 0.008 * s, eye_slot,
                           center=(dx * s, -0.3 * s, 0.33 * s + 0.21 * s)) for dx in (-0.14, 0.0, 0.14)]
    return kit.Piece.merge(cranium, jaw, *eyes, nose, *teeth)


def scarab(height, shell="gem", body="gold"):
    """An Egyptian scarab, domed and chunky: two wing covers in `shell` meeting at a seam down its back, a wide
    thorax and a small head above them, and three bent legs a side in `body`."""
    s = 1.7 * height
    parts = [kit.lump((sx * 0.16 * s, -0.1 * s, 0.12 * s), (0.2 * s, 0.3 * s, 0.13 * s), shell) for sx in (-1, 1)]
    parts += [kit.lump((0.0, 0.25 * s, 0.1 * s), (0.3 * s, 0.12 * s, 0.11 * s), shell),
              kit.lump((0.0, 0.42 * s, 0.08 * s), (0.13 * s, 0.08 * s, 0.08 * s), body)]
    leg = kit.rect_profile(0.06 * s, 0.05 * s, 0.015 * s)
    for sx in (-1, 1):  # the front pair reaches up, the middle one out, the hind one down
        for path in (((0.24, 0.22), (0.46, 0.34), (0.52, 0.5)), ((0.3, 0.0), (0.55, 0.03), (0.62, -0.1)),
                     ((0.26, -0.22), (0.48, -0.36), (0.5, -0.52))):
            pts = np.array([(sx * x * s, y * s, 0.08 * s) for x, y in path])
            parts.append(kit.sweep(pts, leg, body, up=(0.0, 0.0, 1.0)))
    return kit.Piece.merge(*parts)


def chain_link(length, width, wire, slot, flat=True):
    """One oval chain link in local axes, lying in the xy plane (`flat`) or standing in xz."""
    t = np.linspace(0, 2 * np.pi, 13)[:-1]
    path = np.column_stack([0.5 * length * np.cos(t), 0.5 * width * np.sin(t), np.zeros(len(t))])
    if not flat:
        path = path[:, [0, 2, 1]]
    return kit.sweep(path, kit.rect_profile(wire, wire, 0.3 * wire), slot, up=(0, 0, 1) if flat else (0, 1, 0),
                     closed=True)


def _chain(host, y, height, slot, out=0.04):
    """Links all around the waist, alternating flat and standing, like the wraith's anchor chain."""
    cx, a, cz, b = (float(v[0]) for v in host.section([y]))
    perim = np.pi * (3 * (a + b) - np.sqrt((3 * a + b) * (a + 3 * b)))
    link = 1.35 * height
    n = max(8, int(perim / (0.6 * link)) // 2 * 2)  # links overlap by a wire's width at each end
    links = []
    for i in range(n):
        theta = 2 * np.pi * i / n
        f = _at(host, y, theta, out)
        links.append(chain_link(link, 0.6 * link, 0.24 * height, slot, flat=i % 2 == 0).moved(f))
    return kit.Piece.merge(*links)


def _studs(host, y, thickness, slot, count, skip=0.5, radius=0.018):
    """Studs evenly around the band, leaving `skip` radians free on either side of the front for the buckle."""
    out = []
    for theta in FRONT + skip + np.linspace(0, 2 * np.pi - 2 * skip, count, endpoint=False):
        out.append(kit.stud(radius, 0.014, slot, sides=10).moved(_at(host, y, theta, thickness)))
    return out


def pouch(height, slot, flap_slot, button_slot):
    """A small belt pouch hanging from the band: a block, a flap over its top, a button."""
    w, h, d = 1.25 * height, 1.55 * height, 0.1
    body = kit.bevel_box((w, h, d), 0.02, slot, center=(0, -0.55 * h, d / 2))
    flap = kit.bevel_box((1.05 * w, 0.45 * h, 0.4 * d), 0.012, flap_slot, center=(0, -0.2 * h, d + 0.012))
    button = kit.stud(0.1 * height, 0.015, button_slot).moved(kit.Frame((0, -0.38 * h, d + 0.03), [1, 0, 0],
                                                                         [0, 1, 0], [0, 0, 1]))
    return kit.Piece.merge(body, flap, button)


def sash_knot(height, slot):
    """A cloth knot with two hanging tails (local axes: knot at the origin, tails down along -y)."""
    knot = kit.bevel_box((0.7 * height, 0.75 * height, 0.08), 0.025, slot, center=(0, 0, 0.04))
    tails = []
    for sx, tilt in ((-1, 8), (1, -6)):
        poly = [(-0.22 * height, 0), (0.22 * height, 0), (0.3 * height, -1.9 * height), (-0.14 * height, -2.2 * height)]
        tail = kit.prism(poly, 0.03, slot, bevel=0.008).moved(
            kit.Frame((sx * 0.12 * height, -0.2 * height, 0.02), [1, 0, 0], [0, 1, 0], [0, 0, 1]).rotated(tilt))
        tails.append(tail)
    return kit.Piece.merge(knot, *tails)


VARIANTS = {
    "leather": dict(band="leather", buckle="frame", buckle_slot="metal", buckle_scale=1.35),
    "plate": dict(band="leather", buckle="plate", buckle_slot="metal", buckle_scale=1.35),
    "skull": dict(band="dark", buckle="skull", buckle_slot="bone", buckle_scale=2.0),
    "studded": dict(band="leather", buckle="frame", buckle_slot="metal", studs=12, buckle_scale=1.35),
    "double": dict(band="leather", buckle="frame", buckle_slot="metal", double=True),
    "pouches": dict(band="leather", buckle="frame", buckle_slot="gold", pouches=True, buckle_scale=1.35),
    "chain": dict(band=None, chain="dark"),
    "sash": dict(band="cloth", buckle="knot", height_scale=1.4, thickness_scale=1.4),
    "scarab": dict(band="gold", buckle="scarab", buckle_slot="gem", trim="dark", buckle_scale=1.5),
    "barbarian": dict(band="leather", buckle="boss", buckle_slot="metal", studs=8, buckle_scale=1.5, swell=0.55,
                      fur="fur_trim"),
}


def build(host, y, height=0.10, thickness=0.032, variant="leather", **overrides):
    """
    The belt `variant` around `host` at height `y` (its middle), `height` tall and `thickness` off the surface.
    `overrides` replace the variant's parameters (e.g. `buckle="skull"`, `band="cloth"`). Buckles are drawn larger
    than life (`buckle_scale`, a share of the band's height), the way the concepts exaggerate them.
    """
    v = {**VARIANTS[variant], **overrides}
    height *= v.get("height_scale", 1.0)
    thickness *= v.get("thickness_scale", 1.0)
    parts = []
    if v.get("double"):
        for dy in (-0.27, 0.27):
            parts.append(_band(host, y + dy * height, 0.42 * height, thickness, v["band"]))
            parts.append(frame_buckle(0.62 * height, v["buckle_slot"]).moved(
                _at(host, y + dy * height, FRONT - 0.12 * np.sign(dy), thickness)))
        return kit.Piece.merge(*parts)
    if v.get("band"):
        parts.append(_band(host, y, height, thickness, v["band"], swell=v.get("swell", 0.0)))
    if v.get("trim"):  # thin rims along both edges
        for dy in (-0.5, 0.5):
            parts.append(kit.host_band(host, y + dy * height - 0.01, y + dy * height + 0.01, thickness + 0.006,
                                       v["trim"], bevel=0.3, sides=20))
    if v.get("chain"):
        parts.append(_chain(host, y, height, v["chain"]))
    front = _at(host, y, FRONT, thickness)
    buckle, big = v.get("buckle"), height * v.get("buckle_scale", 1.0)
    if buckle == "frame":
        parts.append(frame_buckle(big, v["buckle_slot"]).moved(front))
    elif buckle == "plate":
        parts.append(plate_buckle(big, v["buckle_slot"], "dark").moved(front))
    elif buckle == "skull":
        parts.append(skull(big, v["buckle_slot"]).moved(front))
    elif buckle == "boss":  # KayKit's barbarian: a big octagonal steel boss in a dark ring, stepped up to its middle
        parts.append(kit.Piece.merge(kit.stud(0.62 * big, 0.03, v.get("boss_rim", "dark"), sides=8),
                                     kit.stud(0.54 * big, 0.055, v["buckle_slot"], sides=8),
                                     kit.stud(0.3 * big, 0.08, v["buckle_slot"], sides=8)).moved(front))
    elif buckle == "scarab":
        parts.append(scarab(big, v["buckle_slot"], "gold").moved(front))
    elif buckle == "knot":
        parts.append(sash_knot(height, v["band"]).moved(_at(host, y, FRONT - 0.7, thickness)))
    if v.get("studs"):
        parts += _studs(host, y, thickness, v.get("stud_slot", "metal"), v["studs"], radius=0.026)
    if v.get("fur"):  # KayKit's barbarian: zigzags of fur along both edges, following the band's swell
        parts += _fur_edges(host, y, height, thickness, v.get("swell", 0.0) * height, v["fur"])
    if v.get("pouches"):
        for theta in (FRONT - 0.8, FRONT + 0.8):
            parts.append(pouch(height, "leather", "dark", "gold").moved(_at(host, y, theta, thickness)))
    return kit.Piece.merge(*parts)


def catalog():
    """Every variant on the mannequin waist (`catalog.WAIST`)."""
    from accessories.catalog import WAIST
    host = WAIST.mesh(0.62, 1.25)
    for name in VARIANTS:
        yield name, build(WAIST, 0.9, variant=name), host
