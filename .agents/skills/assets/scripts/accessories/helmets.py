"""
Helmets: separate pieces over a head (`heads.Head`), so a character puts one on or takes it off without touching
the head beneath. A helmet's shell is a hairline cap (`heads.hair_cap`) standing well off the skull; its features
sit on that shell.

- `bear`: KayKit's barbarian hood: a pelt with round ears, a snout and nose over the brow, eyes, and a row of teeth
  along its rim (the pelt down the back is a separate `cape`).
- `horned`: a round viking helm with a riveted brow band, a nose guard and two horns.

    python accessories/catalog.py helmets -o helmets.png --yaws 0,-40,-90,180
"""

import numpy as np

from accessories import heads, kit

FRONT = np.pi / 2


def _rim(head, line, theta):
    """`phi` of a hairline cap's edge at angles `theta` (the same interpolation `heads.hair_cap` uses)."""
    from_front = np.degrees(np.abs(np.angle(np.exp(1j * (np.asarray(theta) - FRONT)))))
    deg, yn = np.asarray(line, float).T
    return head.phi(np.clip(np.interp(from_front, deg, yn), -0.97, 0.97))


def _lump(c, r, slot, n=8):
    pts = [np.asarray(c) + np.asarray(r) * [np.cos(a) * np.cos(b), np.sin(b), np.sin(a) * np.cos(b)]
           for a in np.radians(np.arange(n) * 360 / n) for b in np.radians((-55, -15, 25, 60))]
    return kit.hull(pts + [np.asarray(c) + [0, r[1], 0], np.asarray(c) - [0, r[1], 0]], slot)


def bear(head, line=((0, 0.36), (45, 0.18), (90, -0.4), (140, -0.7), (180, -0.75)), thickness=0.11, slot="pelt",
         inner="pelt_dark", nose="dark", teeth="tooth", ears=1.0, snout=1.0, tooth_count=15):
    """A bear's head worn as a hood: its pelt down to `line` (`heads.hair_cap`'s hairline), `thickness` off the
    skull, with round `ears` on top, a `snout` with a black nose over the brow, beady eyes, and teeth hanging over
    the brow and round the rim."""
    A = head.A
    parts = [heads.hair_cap(head, line=line, thickness=thickness, slot=slot, sides=36)]
    for sgn in (-1, 1):  # ears: flattened discs standing up on the crown's sides, a darker inside
        (P,), (n,) = head.surface(FRONT + sgn * 1.15, 0.62, thickness, normals=True)
        r = 0.34 * A * ears
        facing = [np.cos(FRONT + sgn * 0.6), 0.0, np.sin(FRONT + sgn * 0.6)]  # forward, a little outward
        f = kit.Frame.make(P + 0.5 * r * n + [0, 0.45 * r, 0], facing)
        parts.append(_lump((0, 0, 0), (r, r, 0.45 * r), slot).moved(f))
        parts.append(_lump((0, -0.1 * r, 0.3 * r), (0.6 * r, 0.6 * r, 0.25 * r), inner).moved(f))
    brow = np.interp(0, *np.asarray(line, float).T)  # the hood's front edge; the snout sits just above it
    (P,), (n,) = head.surface(FRONT, head.phi(brow + 0.16), thickness, normals=True)
    s = 0.36 * A * snout
    f = kit.Frame.make(P, n)
    parts.append(_lump((0, 0, 0.3 * s), (0.85 * s, 0.6 * s, 0.7 * s), slot).moved(f))
    parts.append(_lump((0, 0.15 * s, 0.98 * s), (0.42 * s, 0.28 * s, 0.24 * s), nose).moved(f))
    for sgn in (-1, 1):  # eyes above the snout
        (Pe,), (ne,) = head.surface(FRONT + sgn * 0.45, head.phi(brow + 0.34), thickness, normals=True)
        parts.append(kit.gem(0.07 * A, nose, depth=0.03).moved(kit.Frame.make(Pe - 0.005 * ne, ne)))
    theta = FRONT + np.radians(np.linspace(-150, 150, tooth_count))  # a zigzag of teeth along the rim
    phi = _rim(head, line, theta)
    rim = head.surface(theta, phi, 0.7 * thickness)
    along = head.surface(theta + 0.01, phi, 0.7 * thickness) - rim
    for k, (p, d, th) in enumerate(zip(rim, along, theta)):
        front = np.cos(0.5 * (th - FRONT)) ** 2
        w, h = 0.11 * A, A * (0.1 + 0.12 * front) * (1.0 if k % 2 == 0 else 0.75)
        down = np.array([0.0, -1.0, 0.0])
        f = kit.Frame(p, d / np.linalg.norm(d), -down, np.cross(d / np.linalg.norm(d), -down))
        parts.append(kit.prism([(-w, 0.03 * A), (w, 0.03 * A), (0.0, -h)], 0.03, teeth, bevel=0.006).moved(f))
    return kit.Piece.merge(*parts)


def _over_crown(head, theta, phi0, phi1, out, n=14):
    """Points from `phi0` at angle `theta` up over the crown and down to `phi1` on the opposite side."""
    up = head.surface(np.full(n, theta), np.linspace(phi0, 0.0, n), out)
    down = head.surface(np.full(n - 1, theta + np.pi), np.linspace(0.0, phi1, n)[1:], out)
    return np.vstack([up, down])


def _head_frame(head, theta, phi, out):
    """A surface `Frame` (`z` = outward normal, `y` = up) on `head` at `(theta, phi)` lifted `out`."""
    (P,), (n,) = head.surface(theta, phi, out, normals=True)
    return kit.Frame.make(P, n, up=(0.0, 1.0, 0.0))


def horned(head, brim=0.22, thickness=0.06, slot="metal", band="leather", horn="bone", rivet="metal", horns=1.0,
           horn_height=0.54, horn_angle=np.pi / 2, horn_radius=0.2, horn_spread=0.42, guard=1.0, ridge=True,
           seams=True, guard_upright=False, patina=False, dents=False):
    """
    A round viking helm down to the brow (`brim`, a height share): a raised `ridge` from front to back over the crown
    and `seams` down its sides, a riveted leather band round the brow, a long nose guard with a knob where it meets
    the band (`guard` scales it), and two big horns on leather sockets at the sides (none with `horns` = 0: an
    iron half-helm; `horn_angle` radians from the
    front, at `horn_height`) sweeping out by `horn_spread` of their length and then straight up.
    """
    from accessories.export import corner_normals
    A = head.A
    line = ((0, brim), (90, brim - 0.08), (180, brim - 0.15))
    cap = heads.hair_cap(head, line=line, thickness=thickness, slot=slot, sides=32)
    brow_band = heads.surface_band(head, brim + 0.06, 0.18, thickness + 0.025, band, tilt=(0.04, 0.0), sides=32)

    if patina or dents:
        cap.normals = corner_normals(cap.verts, cap.faces, crease=60.0)
        brow_band.normals = corner_normals(brow_band.verts, brow_band.faces, crease=55.0)
        light_s, worn_s, dark_s = f"{slot}_light", f"{slot}_worn", f"{slot}_dark"
        paintable_cap = {slot, light_s, worn_s, dark_s, "tarnish", "rust"}
        if patina:
            cap = kit.weather(cap, slot, light_slot=light_s, worn_slot=worn_s, dark_slot=dark_s)
            # Polygonal patina/rust stains on lower dome plates (flush with surface)
            p_rust = np.array([(-0.07, -0.035), (0.06, -0.04), (0.08, 0.03), (-0.03, 0.05)]) * A
            cap = kit.paint_cut_local(cap, _head_frame(head, FRONT + 0.52, head.phi(brim + 0.28), thickness),
                                      [p_rust], "tarnish", paintable=paintable_cap)
            cap = kit.paint_cut_local(cap, _head_frame(head, FRONT - 0.58, head.phi(brim + 0.28), thickness),
                                      [p_rust], "rust", paintable=paintable_cap)
            is_l_band = "leather" in band or "hide" in band
            brow_band = kit.weather(brow_band, band, worn_slot="grime" if is_l_band else "tarnish",
                                    dark_slot="dirt" if is_l_band else "rust")
            p_band_rust = np.array([(-0.06, -0.03), (0.05, -0.03), (0.07, 0.025), (-0.02, 0.035)]) * A
            brow_band = kit.paint_cut_local(
                brow_band, _head_frame(head, FRONT + 0.35, head.phi(brim + 0.06), thickness + 0.025),
                [p_band_rust], "rust" if not is_l_band else "grime")
        d_cnt, d_scale = kit.parse_dents(dents, max_allowed=2)
        if d_cnt >= 1:
            # 1. Deep, long diagonal battle gash on the front-left dome plate (exaggerated)
            f_d1 = _head_frame(head, FRONT + 0.44, head.phi(brim + 0.46), thickness).rotated(32.0, "z")
            cap = kit.gash(cap, f_d1, length=0.48 * A * d_scale, width=0.13 * A * d_scale, depth=0.032 * A * d_scale,
                           slot=worn_s, shadow_slot=dark_s, paintable=paintable_cap)
        if d_cnt >= 2:
            # 2. Angled battle scratch on the front-right dome plate
            f_d2 = _head_frame(head, FRONT - 0.46, head.phi(brim + 0.52), thickness).rotated(-36.0, "z")
            cap = kit.gash(cap, f_d2, length=0.36 * A * d_scale, width=0.11 * A * d_scale, depth=0.026 * A * d_scale,
                           slot=worn_s, shadow_slot=dark_s, paintable=paintable_cap)
            # Subtle nick on the brow band
            paintable_band = {band, "tarnish", "rust", "grime", "dirt"}
            f_b1 = _head_frame(head, FRONT + 0.58, head.phi(brim + 0.07), thickness + 0.025).rotated(18.0, "z")
            brow_band = kit.gash(brow_band, f_b1, length=0.18 * A * d_scale, width=0.062 * A * d_scale, depth=0.016 * A * d_scale,
                                 slot="tarnish", shadow_slot="rust", paintable=paintable_band)

    parts = [cap, brow_band]
    if ridge:  # a comb from the brow band over the crown to the back
        path = _over_crown(head, FRONT, head.phi(brim + 0.14), head.phi(brim + 0.02), thickness + 0.012)
        ridge_slot = f"{slot}_worn" if patina else slot
        r_piece = kit.sweep(path, kit.rect_profile(0.2 * A, 0.04, 0.012), ridge_slot, up=(0.0, 0.0, 1.0))
        if patina:
            r_piece = kit.weather(r_piece, ridge_slot, light_slot=f"{slot}_light", dark_slot=f"{slot}_dark",
                                  rust_slot="tarnish", seed=23)
        parts.append(r_piece)
    if seams:
        seam_slot = f"{slot}_dark" if patina else slot
        for th in (FRONT - 0.9, FRONT + 0.9, 3 * np.pi / 2 - 0.9, 3 * np.pi / 2 + 0.9):
            path = head.surface(np.full(10, th), np.linspace(head.phi(brim + 0.16), 0.35, 10), thickness + 0.008)
            parts.append(kit.sweep(path, kit.rect_profile(0.05 * A, 0.025, 0.006), seam_slot, up=(0.0, 1.0, 0.0)))
    (P,), (n,) = head.surface(FRONT, head.phi(brim + 0.04), thickness + 0.02, normals=True)
    g = guard * A
    # the guard follows the brim's slope, or hangs plumb (`guard_upright`) where a heavy brow would tip it out
    f = kit.Frame.make(P, n * [1.0, 0.0, 1.0] if guard_upright else n).rotated(-6.0, "x")
    guard_piece = kit.prism([(-0.15 * g, 0.08 * g), (0.15 * g, 0.08 * g), (0.1 * g, -0.45 * g), (0.0, -0.66 * g),
                             (-0.1 * g, -0.45 * g)], 0.06 * guard, slot, bevel=0.015)
    if patina:
        guard_piece.normals = corner_normals(guard_piece.verts, guard_piece.faces, crease=45.0)
        guard_piece = kit.weather(guard_piece, slot, light_slot=f"{slot}_light", worn_slot=f"{slot}_worn",
                                  dark_slot=f"{slot}_dark", seed=29)
    parts.append(guard_piece.moved(f))
    boss_slot = f"{slot}_light" if patina else slot
    parts.append(kit.gem(0.1 * g, boss_slot, cut="diamond", depth=0.05).moved(kit.Frame(f.to_world([0, 0.03 * g, 0.05]),
                                                                                         *f.R.T)))
    for idx_r, th in enumerate(FRONT + np.radians(np.arange(-150, 151, 25))):
        (Pr,), (nr,) = head.surface(th, head.phi(brim + 0.11), thickness + 0.035, normals=True)
        r_slot = (f"{slot}_light" if idx_r % 2 == 0 else "rust") if patina else rivet
        parts.append(kit.stud(0.05 * A, 0.025, r_slot).moved(kit.Frame.make(Pr, nr)))
    L, r = 1.15 * A * horns, horn_radius * A * horns
    for sgn in ((-1, 1) if horns else ()):  # horns in the front plane: out from the sides, then straight up
        theta = FRONT - sgn * horn_angle
        (Ph,), (nh,) = head.surface(theta, head.phi(horn_height), thickness, normals=True)
        side = np.array([np.sign(np.cos(theta)) or sgn, 0.0, 0.0])
        up = np.array([0.0, 1.0, 0.0])
        k = horn_spread
        path = kit.spline([Ph - 0.05 * nh, Ph + 0.72 * k * L * side + 0.08 * L * up, Ph + k * L * side + 0.45 * L * up,
                           Ph + 0.78 * k * L * side + L * up], 10)
        parts.append(kit.tube(path, r, horn, sides=7, taper=0.75))
        sock = Ph + 0.06 * nh
        ring = np.array([sock + 1.15 * r * (np.cos(a) * np.array([0, 1.0, 0]) + np.sin(a) * np.cross(nh, [0, 1.0, 0]))
                         for a in np.linspace(0, 2 * np.pi, 11)[:-1]])
        parts.append(kit.sweep(ring, kit.rect_profile(0.09 * A, 0.05, 0.01), band, up=tuple(nh), closed=True))
    return kit.Piece.merge(*parts)


COLORS = {"pelt": (112, 96, 88), "pelt_dark": (86, 72, 66), "dark": (28, 24, 26), "tooth": (240, 240, 236),
          "metal": (170, 178, 190), "leather": (122, 78, 52), "bone": (232, 222, 196), "feather": (236, 232, 220),
          "feather_tip": (60, 44, 40), "hat": (40, 44, 80), "trim": (190, 196, 210), "rune": (150, 110, 230)}

def headdress(head, band_y=0.24, slot="leather", skull="bone", feather="feather", tip="feather_tip", count=13,
              length=0.95, spread=200.0, splay=(12.0, 72.0), horn_length=0.85, skull_size=0.6):
    """
    A shaman's war bonnet: a leather band round the brow, a horned animal skull worn on the forehead, and a fan of
    `count` feathers (two-toned, `tip` at the end) round the crown over `spread` degrees of the back and sides, each
    `length` (head half widths) long, fanning out from `splay[0]` degrees off upright at the back to `splay[1]` at the
    temples: a halo of feathers seen from the front.
    """
    from accessories import belts

    A = head.A
    parts = [heads.surface_band(head, band_y, 0.16, 0.05, slot, sides=32)]
    for k in range(count):  # the fan: centred on the back, reaching round to the temples
        theta = 3 * np.pi / 2 + np.radians(spread) * (k / (count - 1) - 0.5)
        (P,), (n,) = head.surface(theta, head.phi(band_y + 0.12), 0.03, normals=True)
        out = np.array([n[0], 0.0, n[2]]) / max(np.hypot(n[0], n[2]), 1e-9)
        lean = np.radians(splay[0] + (splay[1] - splay[0]) * abs(2 * k / (count - 1) - 1))
        up = np.cos(lean) * np.array([0.0, 1.0, 0.0]) + np.sin(lean) * out
        z = out - (out @ up) * up
        z /= np.linalg.norm(z)
        L, w = length * A * (0.85 + 0.15 * np.cos(np.pi * (k / (count - 1) - 0.5))), 0.16 * A
        vane = kit.prism([(-w, 0.12 * L), (0.0, 0.0), (w, 0.12 * L), (0.9 * w, 0.7 * L), (0.0, 0.78 * L),
                          (-0.9 * w, 0.7 * L)], 0.02, feather, bevel=0.004)
        end = kit.prism([(-0.9 * w, 0.7 * L), (0.9 * w, 0.7 * L), (0.0, L)], 0.024, tip, bevel=0.004)
        parts.append(kit.Piece.merge(vane, end).moved(kit.Frame(P, np.cross(up, z), up, z)))
    (P,), (n,) = head.surface(FRONT, head.phi(band_y + 0.22), 0.04, normals=True)
    f = kit.Frame.make(P, n * [1.0, 0.4, 1.0]).rotated(-15.0, "x")
    parts.append(belts.skull(skull_size * A, skull).moved(f))
    L, r = horn_length * A, 0.09 * A
    for sgn in (-1, 1):  # the beast's horns: out from the skull's temples, then curling up
        base = f.to_world([sgn * 0.3 * skull_size * A, 0.12 * skull_size * A, 0.1 * skull_size * A])
        side = sgn * f.R[:, 0]
        path = kit.spline([base, base + 0.55 * L * side + 0.05 * L * f.R[:, 1], base + 0.85 * L * side + 0.45 * L * f.R[:, 1],
                           base + 0.7 * L * side + 0.85 * L * f.R[:, 1]], 10)
        parts.append(kit.tube(path, r, skull, sides=6, taper=0.7))
    return kit.Piece.merge(*parts)


def wizard(head, brim_y=0.3, brim=1.75, height=2.3, bend=0.75, slot="hat", band="trim", runes="rune", sides=20):
    """
    A wizard's hat: a wide floppy-edged brim (`brim` times the head's half width) round the head at `brim_y`, a tall
    cone (`height` half widths) whose tip bends back by `bend` (crooked, a share of its height), and a band round
    its foot with little rune plates in `runes`.
    """
    A = head.A
    cx, cy, cz = head.centre
    y0 = head.y(brim_y)
    r0 = 1.06 * max(float(v[0]) for v in head.section([y0])[1::2])  # just wider than the head at the brim
    # the brim: a flat ring drooping a little at its edge, a slab with thickness
    th = 2 * np.pi * np.arange(sides * 2) / (sides * 2)
    ring = lambda r, dy: np.column_stack([cx + r * np.cos(th), np.full(len(th), y0 + dy), cz + r * np.sin(th)])  # noqa: E731
    R = brim * A
    rings = [ring(0.8 * r0, 0.04 * A), ring(0.7 * R, 0.0), ring(R, -0.2 * A), ring(R, -0.3 * A), ring(0.7 * R, -0.12 * A),
             ring(0.8 * r0, -0.08 * A)]  # thick at the crown, thinning and drooping toward its edge
    verts, faces = kit._loft(rings, closed_profile=True)
    parts = [kit.Piece(verts, kit._orient_closed(verts, faces), slot)]
    # the cone along a bent path: straight up, then over toward the back
    H = height * A
    path = kit.spline([(cx, y0, cz), (cx, y0 + 0.45 * H, cz - 0.04 * H), (cx, y0 + 0.8 * H, cz - 0.3 * bend * H),
                       (cx, y0 + 0.88 * H, cz - 0.75 * bend * H)], 14)
    t = np.linspace(0.0, 1.0, len(path))
    parts.append(kit.sweep(path, kit.ring_profile(r0, r0, sides), slot, up=(0.0, 0.0, 1.0),
                           scale=np.maximum((1 - t) ** 0.9, 0.02)))
    parts.append(kit.sweep(ring(r0, 0.09 * A)[::2], kit.rect_profile(0.03, 0.16 * A, 0.01), band, closed=True,
                           up=(0.0, 1.0, 0.0)))  # the band round the cone's foot
    for k in range(9):  # rune plates round the front half of the band
        a = FRONT + np.radians(-80 + 20 * k)
        P = np.array([cx + 1.03 * r0 * np.cos(a), y0 + 0.09 * A, cz + 1.03 * r0 * np.sin(a)])
        f = kit.Frame.make(P, (np.cos(a), 0.0, np.sin(a)))
        parts.append(kit.prism([(-0.035, -0.04), (0.035, -0.04), (0.0, 0.045)] if k % 2 else
                               [(-0.03, -0.04), (0.03, -0.04), (0.03, 0.04), (-0.03, 0.04)], 0.012, runes).moved(f))
    return kit.Piece.merge(*parts)


STYLES = {"bear": bear, "horned": horned, "headdress": headdress, "wizard": wizard}


def catalog():
    """Each helmet on a plain head."""
    plain = dict(form=heads.FORMS["human"], eyes=dict(), brows=dict(), nose=dict(style="button"), mouth=dict(curve=0.15),
                 ears=dict(style="round"))
    for name, fn in STYLES.items():
        head, piece = heads.build(plain)
        yield name, kit.Piece.merge(piece, fn(head)), None, {**heads.COLORS, **COLORS}
