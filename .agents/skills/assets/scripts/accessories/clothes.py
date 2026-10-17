"""
Clothes: shells fitted to a body's hosts (`body.Trunk`, `body.Limb`), so they dress any body like a belt fits any
waist, and stay separate pieces a character can put on or take off. A shell follows its host's cross-sections a
`thickness` off the skin and can flare at either end (a tunic's hem, a wizard's sleeve, a boot's cuff).

Garments: `shell` (a tunic, a breastplate, a boot shaft or sleeve on a limb via `on_limb`), `robe` (a torso shell
running on into a bell-shaped skirt, its hem straight or ragged), `shorts`/`pants`, `cuff`, `collar` (a studded
band or an Egyptian broad collar), `apron` (a kilt's front flap), `wraps` (a mummy's bandages), `shackle` (a cuff
with a broken chain).
"""

import numpy as np

from accessories import kit
from accessories.belts import _studs, chain_link

COLORS = {"cloth": (120, 60, 150), "cloth_dark": (70, 36, 90), "armor": (84, 74, 72), "armor_dark": (56, 50, 50),
          "pants": (150, 44, 44), "glove": (236, 236, 240), "metal": (176, 186, 199), "rust": (150, 80, 44),
          "wrap": (232, 214, 160), "wrap_dark": (190, 164, 112), "gold": (240, 186, 40), "lapis": (40, 80, 200)}


def neckline_dip(theta, style="v", front_dip=0.14, back_dip=0.06, front_width=np.radians(65), back_width=np.radians(55)):
    """A neckline cutout in a garment's top edge: dipping `front_dip` in front (theta = pi/2) as a 'v' or 'u'
    neck of angular half-width `front_width`, and dipping `back_dip` behind (theta = 3*pi/2) as a 'u' neck of
    half-width `back_width`. Shoulders (theta = 0 and pi) stay at full shoulder height."""
    th = np.asarray(theta, float) % (2 * np.pi)
    d_front = np.abs((th - np.pi / 2 + np.pi) % (2 * np.pi) - np.pi)
    d_back = np.abs((th - 3 * np.pi / 2 + np.pi) % (2 * np.pi) - np.pi)
    dip = np.zeros_like(th)

    in_front = d_front < front_width
    u_f = d_front[in_front] / front_width
    if style == "v":
        f_shape = (1.0 - u_f) ** 1.1
    else:
        f_shape = 0.5 * (1.0 + np.cos(np.pi * u_f))
    dip[in_front] += front_dip * f_shape

    in_back = d_back < back_width
    u_b = d_back[in_back] / back_width
    b_shape = 0.5 * (1.0 + np.cos(np.pi * u_b))
    dip[in_back] += back_dip * b_shape
    return dip


def shell(host, y0, y1, thickness, slot, flare=(0.0, 0.0), sides=20, inset=0.012, bevel=0.4, rows=6, neck=None):
    """
    A garment around a host from `y0` up to `y1`, `thickness` off its surface plus `flare` = (at y0, at y1)
    swelling toward either end, its edges chamfered by `bevel` (a share of the thickness). `neck` shapes the top edge
    into a neckline cutout: 'v' (default for torso garments), 'u', a depth float, or dict(...).
    """
    c = bevel * thickness
    if neck:
        n_style = "v"
        f_dip = 0.14
        b_dip = 0.06
        fw = np.radians(65)
        bw = np.radians(55)
        if isinstance(neck, str):
            n_style = neck
        elif isinstance(neck, dict):
            n_style = neck.get("style", "v")
            f_dip = neck.get("front", 0.14)
            b_dip = neck.get("back", 0.06)
            if "front_width" in neck:
                fw = np.radians(neck["front_width"]) if neck["front_width"] > np.pi else neck["front_width"]
            if "back_width" in neck:
                bw = np.radians(neck["back_width"]) if neck["back_width"] > np.pi else neck["back_width"]
        elif isinstance(neck, (int, float)):
            f_dip = float(neck)
            b_dip = float(neck) * 0.45
        th = 2 * np.pi * np.arange(sides) / sides
        y1 = y1 - neckline_dip(th, style=n_style, front_dip=f_dip, back_dip=b_dip, front_width=fw, back_width=bw)
    span = float(np.min(y1 - y0))
    dt = min(c / max(span, 1e-9), 0.45)
    t = np.linspace(dt, 1 - dt, rows)  # between the chamfers, so no two rings coincide on a short shell
    outer = thickness + flare[0] * (1 - t) ** 2 + flare[1] * t ** 2
    profile = [(0.0, -inset), (0.0, outer[0] - c)] + list(zip(t, outer)) + [(1.0, outer[-1] - c), (1.0, -inset)]
    return kit.host_band(host, y0, y1, thickness, slot, profile=profile, sides=sides)


def on_limb(limb, y0, y1, thickness, slot, **kw):
    """A `shell` on a limb, from `y0` to `y1` along it (shares of its length), moved into place."""
    return limb.place(shell(limb, y0 * limb.length, y1 * limb.length, thickness, slot, sides=kw.pop("sides", 12), neck=None, **kw))


class _Bell:
    """A host that is the trunk above `waist` and widens below it into a bell down to `hem` (half sizes `hem_a`
    across, `hem_b` deep), easing out like cloth falling from the hips."""

    def __init__(self, trunk, waist, hem, hem_a, hem_b):
        self.trunk, self.waist, self.hem, self.hem_a, self.hem_b, self.p = trunk, waist, hem, hem_a, hem_b, trunk.p
        self.w0 = trunk.section([waist])

    def section(self, y):
        y = np.atleast_1d(np.asarray(y, float))
        cx, a, cz, b = (np.array(v, float) for v in self.trunk.section(np.maximum(y, self.waist)))
        s = np.clip((self.waist - y) / (self.waist - self.hem), 0, 1)
        e = 1 - (1 - s) ** 2  # widens fast at first, then falls straight
        below = y < self.waist
        a = np.where(below, self.w0[1][0] + (self.hem_a - self.w0[1][0]) * e, a)
        b = np.where(below, self.w0[3][0] + (self.hem_b - self.w0[3][0]) * e, b)
        cz = np.where(below, self.w0[2][0] * (1 - e), cz)
        return cx, a, cz, b


def robe(trunk, top, hem=0.12, waist=0.62, hem_a=0.42, hem_b=0.36, thickness=0.03, slot="cloth", ragged=0, jag=0.08,
         dip=0.0, sides=24, trim=None, trim_width=0.05, neck="v"):
    """
    A robe from `top` (under the neck) down over the hips into a bell skirt to `hem`; `neck` shapes its top opening
    into a V-neck or U-neck showing skin; `ragged` > 0 cuts its hem into that many torn points hanging `jag` below it
    (a wraith's rags); `dip` lets the hem hang that much lower in front and behind than at the sides (a loincloth's
    flaps); `trim` edges a straight hem with a band in that slot.
    """
    if ragged:  # an even number of hem points per tooth: one at each tip, one at each notch
        per = max(2, 2 * int(round(sides / (2 * ragged))))
        sides = ragged * per
    piece = shell(_Bell(trunk, waist, hem, hem_a, hem_b), hem, top, thickness, slot, sides=sides, rows=12, neck=neck)
    if ragged:
        V = piece.verts.copy()
        low = V[:, 1] < hem + 1e-6
        ang = np.arctan2(V[low, 2], V[low, 0])
        saw = np.abs(((ang / (2 * np.pi) * ragged) % 1.0) - 0.5) * 2  # 1 at the tips, 0 at the notches
        V[low, 1] -= jag * saw  # only ever down, so the hem never folds over the ring above it
        piece = kit.Piece(V, piece.faces, piece.slots)
    if dip:
        V = piece.verts.copy()
        low = V[:, 1] < hem + 1e-6
        V[low, 1] -= dip * np.abs(np.sin(np.arctan2(V[low, 2], V[low, 0])))
        piece = kit.Piece(V, piece.faces, piece.slots)
    if trim and not ragged and not dip:
        piece = kit.Piece.merge(piece, shell(_Bell(trunk, waist, hem, hem_a, hem_b), hem - 0.004, hem + trim_width,
                                             thickness + 0.012, trim, sides=sides, rows=2, neck=None))
    return piece


def shorts(trunk, leg, top=0.62, down=0.45, thickness=0.03, slot="pants", flare=0.02):
    """Shorts (`down` = 0.45: to the knee) or pants (`down` = 1.0): a band over the hips and a tube down each leg."""
    hips = shell(trunk, trunk.bottom, top, thickness, slot, rows=8)
    # the crotch: the hip band's open bottom closed by a low dome, so the body never shows between the legs
    cx, a, cz, b = (float(v[0]) for v in trunk.section([trunk.bottom]))
    th = 2 * np.pi * np.arange(20) / 20
    rim = np.column_stack([cx + (a + thickness) * np.cos(th), np.full(20, trunk.bottom + 0.005),
                           cz + (b + thickness) * np.sin(th)])
    hips = kit.Piece.merge(hips, kit.hull(np.vstack([rim, rim * [0.8, 1.0, 0.8] + [0.2 * cx, -0.05, 0.2 * cz]]), slot))
    one = on_limb(leg, 0.0, down, thickness, slot, flare=(0.0, flare))
    return kit.Piece.merge(hips, one, one.mirrored())


def cuff(limb, at, width, thickness, slot, studs=0, stud_slot="metal", stud_radius=0.022):
    """A ring around a limb centred at `at` (a share of its length), `width` long: a bracer, an arm band, a glove's
    or a boot's cuff. `studs` puts that many studs around it."""
    y = at * limb.length
    parts = [kit.host_band(limb, y - width / 2, y + width / 2, thickness, slot, bevel=0.35, sides=12)]
    if studs:
        parts += [kit.stud(stud_radius, 0.6 * stud_radius, stud_slot).moved(kit.band_frame(limb, y, th, thickness))
                  for th in np.pi / 2 + 2 * np.pi * np.arange(studs) / studs]
    return limb.place(kit.Piece.merge(*parts))


def collar(trunk, top, style="studded", height=0.12, thickness=0.05, slot="armor", stud_slot="metal",
           stripes=("gold", "lapis", "gold", "lapis"), rays=0):
    """
    Round the neck: `studded`, a thick band in `slot` with studs in `stud_slot` (a necromancer's); `high`, a stiff
    wizard's collar `height` tall flaring up round the back of the head, open in front; `broad`, an
    Egyptian broad collar fanning out over the shoulders in `stripes` of slots, each wider and lower than the last,
    or with `rays` > 0 in that many pairs of wedges radiating from the neck (the first two `stripes` alternating, the
    first one centred in front) between rims of the first: it stands apart from bandages wound round the body.
    """
    if style == "high":  # a stiff collar standing up round the back of the neck and flaring out, open in front
        cx, a, cz, b = (float(v[0]) for v in trunk.section([top]))
        th = np.radians(np.linspace(-60, 240, 25))  # the back and sides, the front 60 degrees left open
        rings = []
        for t, grow, lift, out in ((0.0, 1.0, -0.02, 0.0), (0.0, 1.0, -0.02, thickness),
                                   (1.0, 1.55, height, thickness), (1.0, 1.55, height, 0.0)):
            r_a, r_b = grow * (a * 0.72) + out, grow * (b * 0.72) + out
            rings.append(np.column_stack([cx + r_a * np.cos(th), np.full(len(th), top + lift), cz + r_b * np.sin(th)]))
        verts, faces = kit._loft(rings, closed_rings=False, closed_profile=True)
        n = len(th)
        verts, faces = kit._cap_loops(verts, faces, [[k * n for k in range(4)], [k * n + n - 1 for k in range(4)]])
        return kit.Piece(verts, kit._orient_closed(verts, faces), slot)
    if style == "broad" and rays:
        low, lo_flare = top - height * (len(stripes) + 0.2), 0.03 + 0.012 * len(stripes)
        fan = shell(trunk, low, top - 0.02, thickness + 0.02, stripes[0], flare=(lo_flare, 0.0), sides=4 * rays,
                    rows=8)
        c = fan.verts[fan.faces].mean(1)
        cx, a, cz, b = (np.asarray(v, float) for v in trunk.section(c[:, 1]))
        u, v, k = (c[:, 0] - cx) / a, (c[:, 2] - cz) / b, trunk.p / 2
        theta = np.arctan2(np.sign(v) * np.abs(v) ** k, np.sign(u) * np.abs(u) ** k)  # the shell's own angle
        seg = np.pi / rays
        wedge = np.floor((theta - np.pi / 2 + seg / 2) / seg).astype(int) % 2
        fan = kit.Piece(fan.verts, fan.faces, np.where(wedge == 0, stripes[0], stripes[1]).astype(object))
        rim = 0.12 * (top - low)
        return kit.Piece.merge(
            fan, shell(trunk, low - 0.004, low + rim, thickness + 0.04, stripes[0], flare=(lo_flare, 0.88 * lo_flare)),
            shell(trunk, top - 0.02 - rim, top - 0.016, thickness + 0.035, stripes[0]))
    if style == "broad":
        out = []
        for i, s in enumerate(stripes):
            y0 = top - height * (i + 1.2)
            out.append(shell(trunk, y0, top - 0.02, thickness + 0.02 * (len(stripes) - i), s, flare=(0.03, 0.0),
                             sides=24))
        return kit.Piece.merge(*out)
    band = kit.host_band(trunk, top - height, top, thickness, slot, bevel=0.35, sides=24)
    return kit.Piece.merge(band, *_studs(trunk, top - height / 2, thickness, stud_slot, 10, skip=0.0, radius=0.03))


def wraps(host, y0, y1, slot="wrap", count=6, thickness=0.02, gap=0.25, sides=16, tilt=0.0):
    """
    A mummy's bandages: `count` bands between `y0` and `y1` with gaps (a share of each band) where the darker skin
    shows; on a limb `y0` and `y1` are shares of its length. `tilt` winds them aslant, each band the other way round
    and turned a little further (a share of a band's spacing): crossing strips, not stacked rings.
    """
    limb = hasattr(host, "place")
    if limb:
        y0, y1 = y0 * host.length, y1 * host.length
    step = (y1 - y0) / count
    out = []
    for i in range(count):
        out.append(kit.host_band(host, y0 + i * step, y0 + (i + 1 - gap) * step, thickness * (1 + 0.3 * (i % 2)),
                                 slot, bevel=0.4, sides=sides, tilt=tilt * step * (1 if i % 2 else -1),
                                 phase=np.pi / 2 + 0.9 * i))
    piece = kit.Piece.merge(*out)
    return host.place(piece) if limb else piece


def shackle(arm, at=0.93, slot="rust", links=3, size=0.06):
    """An iron cuff at the wrist with a broken chain hanging from it."""
    ring = cuff(arm, at, 0.07, 0.03, slot)
    y = at * arm.length
    a = float(arm.section([y])[1][0])
    out = [ring]
    for i in range(links):  # hanging along the arm's local +x (down in the T-pose), alternating flat and edge-on
        c = np.array([a + 0.02 + (i + 0.5) * 0.75 * size, y, 0.0])
        frame = (kit.Frame(c, [1, 0, 0], [0, 1, 0], [0, 0, 1]) if i % 2 == 0
                 else kit.Frame(c, [1, 0, 0], [0, 0, 1], [0, -1, 0]))
        out.append(arm.place(chain_link(size, 0.6 * size, 0.2 * size, slot).moved(frame)))
    return kit.Piece.merge(*out)


def apron(trunk, y, length=0.38, width=0.34, flare=1.35, slot="gold", stripe="lapis", thickness=0.02, out=0.03):
    """
    An Egyptian kilt's front flap hanging from the belt at `y`, `length` down, `width` wide at the top and `flare`
    times that at its pointed hem: one plate in `slot`, its front cut into two side panels in `stripe` framed by a
    border, with a centre panel widening from under the buckle down to the point. Leaning in a little toward the
    legs.
    """
    w0, w1, L = width / 2, flare * width / 2, length
    edge = 0.06 * width  # the border's width
    plate = kit.prism([(-w0, 0.0), (w0, 0.0), (w1, -0.88 * L), (0.0, -L), (-w1, -0.88 * L)], thickness, slot,
                      bevel=0.006)
    c0, c1 = 0.36 * w0, 0.62 * w1  # the centre panel's half widths at the top and the hem
    panels = [np.array([(sgn * (w0 - edge), -edge), (sgn * c0, -edge), (sgn * c1, -0.86 * L + edge),
                        (sgn * (w1 - edge), -0.86 * L + edge)]) for sgn in (-1, 1)]
    plate = kit.paint_cut(plate, panels, stripe, {slot})  # the panels: the plate's own front, recolored
    f = kit.band_frame(trunk, y, np.pi / 2, out).rotated(4.0, "x")
    return plate.moved(f)


def loincloth(trunk, y, hem=0.3, flare=0.05, ragged=7, jag=0.06, dip=0.08, thickness=0.025, slot="cloth",
              patina=False, dents=False):
    """A short rag skirt wrapped round the hips from a belt at `y` down to `hem`, flaring by `flare`, its hem torn
    into `ragged` points hanging up to `jag` lower and dipping `dip` further in front and behind (the flaps)."""
    from accessories.export import corner_normals
    _, a, _, b = (float(v[0]) for v in trunk.section([y]))
    piece = robe(trunk, y, hem, y, a + flare, b + flare, thickness, slot, ragged, jag, dip)
    if patina or dents:
        piece.normals = corner_normals(piece.verts, piece.faces, crease=60.0)
        light_s, worn_s, dark_s = f"{slot}_light", f"{slot}_worn", f"{slot}_dark"
        paintable = {slot, light_s, worn_s, dark_s, "dirt", "grime"}
        if patina:
            piece = kit.weather(piece, slot, light_slot=light_s, worn_slot=worn_s, dark_slot=dark_s, rust_slot="dirt",
                                seed=31)
            p_stain = np.array([(-0.04, -0.02), (0.04, -0.03), (0.05, 0.02), (-0.02, 0.03)])
            f_st = kit.band_frame(trunk, hem + 0.34 * (y - hem), np.pi / 2 - 0.82, thickness + 0.02)
            piece = kit.paint_cut_local(piece, f_st, [p_stain], "grime", paintable=paintable, max_z=0.12)
        d_cnt, d_scale = kit.parse_dents(dents, max_allowed=2)
        if d_cnt >= 1:
            ym = hem + 0.58 * (y - hem)
            # 1. Main dramatic battle gash on front-left with stitch (longer and exaggerated)
            f1 = kit.band_frame(trunk, ym, np.pi / 2 + 0.34, thickness + 0.015).rotated(34.0, "z")
            piece = kit.gash(piece, f1, length=0.22 * d_scale, width=0.056 * d_scale, depth=0.020 * d_scale,
                             slot=worn_s, shadow_slot=dark_s, paintable=paintable, stitch_slot="stitch")
        if d_cnt >= 2:
            # 2. Secondary angled battle slash on front-right
            f2 = kit.band_frame(trunk, ym - 0.03, np.pi / 2 - 0.38, thickness + 0.018).rotated(-38.0, "z")
            piece = kit.gash(piece, f2, length=0.17 * d_scale, width=0.046 * d_scale, depth=0.017 * d_scale,
                             slot=worn_s, shadow_slot=dark_s, paintable=paintable)
    return piece


def ribs(trunk, y0, y1, count=4, spread=55.0, thickness=0.014, slot="skin_dark", height=0.03):
    """A starved creature's ribs showing through its skin: `count` raised arcs across the chest between `y0` and
    `y1`, each `spread` degrees to either side of the breastbone, the lower ones curving down toward the sides."""
    out = []
    for i in range(count):
        y = y1 - (y1 - y0) * i / max(count - 1, 1)
        for sgn in (-1, 1):  # each side from near the breastbone round toward the flank
            theta = np.pi / 2 + sgn * np.radians(np.linspace(8, spread, 9))
            out.append(kit.host_band(trunk, y - height / 2, y + height / 2, thickness, slot, theta=theta, bevel=0.5,
                                     tilt=0.0))
    return kit.Piece.merge(*out)


def wings(trunk, y, span=0.8, fingers=3, sweep=15.0, scallop=0.28, membrane="wing", bone="wing_bone",
          claw="wing_claw"):
    """
    Bat wings on the back at `y`: an arm bone runs out and up from each shoulder blade to a wrist with a hooked
    thumb claw, `fingers` finger bones fan out from the wrist to the tips, and one membrane is stretched between
    them, from the arm's leading edge round the finger tips and back to the flank, its free edge curving in between
    the tips (`scallop`, a share of the way toward the wrist). `span` is a wing's reach, `sweep` how far it angles
    back. Rigid on the chest.
    """
    root, wrist = np.array([0.0, 0.0]), np.array([0.55, 0.42]) * span
    tips = [wrist + np.array([np.cos(a), np.sin(a)]) * span * (0.62 - 0.1 * i)
            for i, a in enumerate(np.radians(np.linspace(10, -75, fingers)))]
    flank = np.array([0.06, -0.4]) * span
    outline = [root + [0.0, 0.05 * span], wrist + [0.0, 0.03 * span]]
    edge = tips + [flank]
    for a, b in zip(edge[:-1], edge[1:]):  # each tip, then a curve sagging in toward the wrist to the next one
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
                          n=6, lat=(-45, 0, 45)))  # the wrist knuckle
    for t in tips:  # finger bones reach right to the membrane's tips
        parts.append(kit.sweep(np.array([(wrist[0], wrist[1], 0.006), (t[0], t[1], 0.006)]),
                               kit.ring_profile(r_finger, r_finger, 5), bone, up=(0.0, 0.0, 1.0),
                               scale=np.array([1.0, 0.35])))
    parts.append(kit.horn(np.array([wrist[0], wrist[1] + 0.02 * span, 0.006]),
                          np.array([wrist[0] + 0.02, wrist[1] + 0.12 * span, 0.006]),
                          np.array([wrist[0] - 0.06 * span, wrist[1] + 0.17 * span, 0.006]), 0.022 * span / 0.8, claw,
                          sides=5))
    one = kit.Piece.merge(*parts)
    at = kit.band_frame(trunk, y, 3 * np.pi / 2 + np.radians(28), 0.0).origin  # on the left shoulder blade
    x = np.array([np.cos(np.radians(sweep)), 0.0, -np.sin(np.radians(sweep))])  # outward, angled back
    R = np.stack([x, [0.0, 1.0, 0.0], np.cross(x, [0.0, 1.0, 0.0])], 1)
    left = kit.Piece(one.verts @ R.T + at, one.faces, one.slots)
    return kit.Piece.merge(left, left.mirrored())


def tail(trunk, y, length=0.75, radius=0.035, tip="tail_tip", slot="skin", curl=0.35):
    """A long thin tail from the small of the back at `y`, swinging down, back and up again, with an arrowhead tip
    (slot `tip`). Rigid: it rides the hips."""
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
        kit.Frame.make(path[-1], flat, up=d)))  # the arrowhead, pointing on along the tail
    return kit.Piece.merge(*parts)


def necklace(trunk, y, drop=0.08, cord="cord", charms=("tooth", "bead"), count=9, size=0.05, out=0.02):
    """A cord round the neck at `y`, sagging `drop` lower in front, hung with `count` charms across the front
    alternating between the `charms` slots: fangs (the first), round beads (the second), the middle one biggest."""
    th = 2 * np.pi * np.arange(48) / 48
    sag = -drop * np.clip(np.sin(th), 0.0, None) ** 2
    ring = kit.ring_points(trunk, y + sag, out, 48)
    parts = [kit.sweep(ring, kit.ring_profile(0.012, 0.012, 6), cord, closed=True)]
    for i in range(count):
        theta = np.pi / 2 + np.radians(70) * (2 * i / (count - 1) - 1)  # across the front, +-70 degrees
        yy = y - drop * np.sin(theta) ** 2
        f = kit.band_frame(trunk, yy, theta, out + 0.012)
        big = 1.5 if i == count // 2 else 1.0
        if i % 2 == 0:  # a fang hanging point down
            parts.append(kit.spike((0.0, 0.0, 0.0), (0.0, -2.2 * size * big, 0.01), 0.45 * size * big,
                                   charms[0]).moved(f))
        else:
            parts.append(kit.lump((0.0, -0.4 * size, 0.0), (0.5 * size, 0.5 * size, 0.4 * size), charms[1], n=6,
                                  lat=(-45, 0, 45)).moved(f))
    return kit.Piece.merge(*parts)


class Fitted:
    """
    A host shaped like what is really there: at each height, the smallest section of the host's own shape (its
    superellipse exponent) that holds every point of the `pieces` within `band` of that height, plus `clearance`.
    Bands (belts) built on it sit tight on the body and whatever plates and cloth it wears, instead of on the
    worst-case layer stack `Worn` records.
    """

    def __init__(self, host, pieces, band=0.05, clearance=0.003):
        self.host, self.band, self.clearance = host, band, clearance
        self.points = np.vstack([p.verts for p in pieces if len(p.verts)]) if pieces else np.zeros((0, 3))

    def __getattr__(self, name):
        return getattr(self.host, name)

    def section(self, y):
        y = np.atleast_1d(np.asarray(y, float))
        cx, a, cz, b = (np.asarray(v, float).copy() for v in self.host.section(y))
        p = getattr(self.host, "p", 2.0)
        for i, yy in enumerate(y):
            V = self.points[np.abs(self.points[:, 1] - yy) < self.band]
            if not len(V):
                continue
            x, z = np.abs(V[:, 0] - cx[i]), V[:, 2]
            ai, bi = max(x.max(), 1e-3), max(0.5 * (z.max() - z.min()), 1e-3)
            ci = 0.5 * (z.max() + z.min())
            k = ((x / ai) ** p + (np.abs(z - ci) / bi) ** p).max() ** (1 / p)  # grown until it holds them all
            cx[i], a[i], cz[i], b[i] = cx[i], k * ai + self.clearance, ci, k * bi + self.clearance
        return cx, a, cz, b

    def _hull(self, y):
        """The taut outline at height `y`: the convex hull (x, z) of everything there, and its centre."""
        key = round(float(y), 3)
        cache = self.__dict__.setdefault("_hulls", {})
        if key not in cache:
            from scipy.spatial import ConvexHull
            V = self.points[np.abs(self.points[:, 1] - y) < self.band][:, [0, 2]]
            if len(V) < 3:
                cache[key] = None
            else:
                H = V[ConvexHull(V).vertices]
                cache[key] = (H, H.mean(0))
        return cache[key]

    def outline(self, y, theta, out):
        """Points round the taut outline at heights `y` and angles `theta` (0 = +x, pi/2 = the front), `out`
        beyond it along the ray from its centre: a band pulled tight round whatever is worn there (`kit.ring_points`
        uses this when a host has it)."""
        theta = np.asarray(theta, float)
        y = np.broadcast_to(np.asarray(y, float), theta.shape)
        out = np.broadcast_to(np.asarray(out, float), theta.shape)
        res = np.zeros((len(theta), 3))
        for yy in np.unique(np.round(y, 4)):
            sel = np.abs(y - yy) < 5e-5
            hull = self._hull(yy)
            if hull is None:  # nothing there: the host's own section
                cx, a, cz, b = (float(v[0]) for v in self.host.section([yy]))
                d = np.column_stack([np.cos(theta[sel]), np.sin(theta[sel])])
                res[sel] = np.column_stack([cx + (a + out[sel]) * d[:, 0], np.full(sel.sum(), yy),
                                            cz + (b + out[sel]) * d[:, 1]])
                continue
            H, c = hull
            d = np.column_stack([np.cos(theta[sel]), np.sin(theta[sel])])
            t = np.full(len(d), 0.0)
            for i in range(len(H)):  # where each ray leaves the hull
                p0, e = H[i] - c, H[(i + 1) % len(H)] - H[i]
                den = d[:, 0] * e[1] - d[:, 1] * e[0]
                ok = np.abs(den) > 1e-12
                s_ = np.where(ok, (p0[0] * e[1] - p0[1] * e[0]) / np.where(ok, den, 1), -1)
                u_ = np.where(ok, (p0[0] * d[:, 1] - p0[1] * d[:, 0]) / np.where(ok, den, 1), -1)
                hit = ok & (s_ > 0) & (u_ >= -1e-9) & (u_ <= 1 + 1e-9)
                t = np.where(hit, np.maximum(t, s_), t)
            r = t + self.clearance + out[sel]
            res[sel] = np.column_stack([c[0] + r * d[:, 0], np.full(sel.sum(), yy), c[1] + r * d[:, 1]])
        return res


class Worn:
    """
    A host as dressed so far: its own sections grown by what it already wears (`wear`), so the next layer (a belt
    over a robe, a cape over a backplate) sits on top instead of fighting it. Layers accumulate, and remember their
    side: a breastplate pushes the front out, not the back a cape hangs from. Everything else is the host's.
    """

    def __init__(self, host):
        self.host, self.layers = host, []

    def __getattr__(self, name):
        return getattr(self.host, name)

    def wear(self, y0, y1, thickness, side="all"):
        """It now wears something `thickness` deep from `y0` to `y1` on `side` ("all", "front" or "back"), on top of
        whatever it wore there before."""
        y0, y1 = min(y0, y1), max(y0, y1)
        below = self._extra(np.linspace(y0, y1, 9))[{"all": 0, "front": 1, "back": 2}[side]].max()
        self.layers.append((y0, y1, below + thickness, side))

    def _extra(self, y):
        """How much it wears at heights `y`: (round the sides, at the front, at the back)."""
        out = np.zeros((3, len(y)))
        for y0, y1, t, side in self.layers:
            on = (y >= y0) & (y <= y1)
            for k, applies in enumerate((side == "all", side in ("all", "front"), side in ("all", "back"))):
                if applies:
                    out[k] = np.where(on, np.maximum(out[k], t), out[k])
        out[0] = np.maximum(out[0], 0.5 * np.maximum(out[1], out[2]))  # a plate wraps part way round the sides
        return out

    def section(self, y):
        cx, a, cz, b = self.host.section(y)
        side, front, back = self._extra(np.atleast_1d(np.asarray(y, float)))
        return cx, a + side, cz + 0.5 * (front - back), b + 0.5 * (front + back)


def fur_trim(host, y, count=20, length=0.07, thickness=0.025, out=0.03, toward=-1, slot="fur_trim"):
    """KayKit's zigzag of white fur: `count` teeth round a host at `y`, `length` long, pointing along its axis
    `toward` -1 (down a torso) or +1 (up it; on a leg, local +y runs down to the ankle). On a limb (`host.place`),
    `y` is a share of its length."""
    limb = hasattr(host, "place")
    y = y * host.length if limb else y
    cx, a, cz, b = (float(v[0]) for v in host.section([y]))
    w = np.pi * (3 * (a + b) - np.sqrt((3 * a + b) * (a + 3 * b))) / count  # each tooth one share of the girth
    tooth = kit.prism([(-0.55 * w, 0.0), (0.55 * w, 0.0), (0.0, toward * length)], thickness, slot, bevel=0.005)
    teeth = [tooth.moved(kit.band_frame(host, y, th, out - 0.012)) for th in np.pi / 2 + 2 * np.pi * np.arange(count) / count]
    band = kit.host_band(host, y - 0.012, y + 0.012, out + 0.005, slot, sides=count)
    piece = kit.Piece.merge(band, *teeth)
    return host.place(piece) if limb else piece
