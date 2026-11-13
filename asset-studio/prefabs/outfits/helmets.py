"""
Helmets: separate pieces over a head (`heads.Head`), fitted tightly to the cranium so a character wears them without
bloating the head silhouette. Features on `heads.Head` that an enclosed or cheek-guarded helm covers are declared via
`piece.hides` so character assembly suppresses them automatically.

Existing styles refitted to a snug profile:
- `bear`: tight barbarian pelt cowl above the ears with brow snout, tucked ears, eyes, and fangs.
- `horned`: snug riveted half-helm with nose guard, spangen seams, crown ridge, and horns.
- `headdress`: war bonnet with brow band, horned skull, and feathered halo.
- `wizard`: crooked tall hat tilted back with a wavy rim, buckled band, runes, and clear crown envelope.

Hero armor family styles:
- `knight`: tight open-face visored helm with keeled crest, angled brow visor, cheek plates, and optional `bevor`.
- `crusader`: solid-cut cylindrical greathelm with flat chamfered crown, recessed dark eye slits, breathers, and cross trim.
- `dark_knight`: angular keeled dread helm with V-slit visor, forehead gem, swept brow-temple horns, and cheek gash.
- `kettle`: snug kettle helm above the ears with flared sloped brim, spangen ribs, and buckled cheek straps.
- `coif`: tight chain-mail hood and camail with curved wire-ring relief, circlet, and optional nasal bar.
- `hood`: draped ranger cowl with peaked brow overhang, folded cuff rim, center seam, and draped back tail.

    python asset-studio/studio.py catalog helmets -o helmets.png --yaws 0,-40,-90,180
"""

import numpy as np

from core import kit
from core.export import corner_normals
from items import solid
from prefabs.anatomy import heads
from prefabs.details import wear
from prefabs.outfits import armor, clothes

FRONT = np.pi / 2


def _with_hides(piece, *keys):
    """Attach head-feature keys hidden beneath this headwear."""
    piece.hides = tuple(keys)
    return piece


def _apply_tilt_y(piece, head, tilt=0.0, y_offset=0.0):
    """Pitch `piece` by `tilt` degrees around the head's centre and shift it up by `y_offset`."""
    if abs(tilt) <= 1e-4 and abs(y_offset) <= 1e-4:
        return piece
    R = kit.rotation((1.0, 0.0, 0.0), tilt)
    c = np.array([0.0, head.cy, 0.0])
    return piece.moved(kit.Frame(c - R @ c + [0.0, y_offset, 0.0], *R.T))


def _rim(head, line, theta, peak=0.0):
    """`phi` of a hairline cap's edge at angles `theta` (the same interpolation `heads.hair_cap` uses), dipped by
    `peak` at the front's middle."""
    from_front = np.degrees(np.abs(np.angle(np.exp(1j * (np.asarray(theta) - FRONT)))))
    deg, yn = np.asarray(line, float).T
    end = np.interp(from_front, deg, yn) - peak * np.exp(-(from_front / 15.0) ** 2)
    return head.phi(np.clip(end, -0.97, 0.97))


def _lump(c, r, slot):
    return kit.lump(c, r, slot, n=8, lat=(-55, -15, 25, 60))


def _over_crown(head, theta, phi0, phi1, out, n=14):
    """Points from `phi0` at angle `theta` up over the crown and down to `phi1` on the opposite side."""
    up = head.surface(np.full(n, theta), np.linspace(phi0, 0.0, n), out)
    down = head.surface(np.full(n - 1, theta + np.pi), np.linspace(0.0, phi1, n)[1:], out)
    return np.vstack([up, down])


def _head_frame(head, theta, phi, out):
    """A surface `Frame` (`z` = outward normal, `y` = up) on `head` at `(theta, phi)` lifted `out`."""
    (P,), (n,) = head.surface(theta, phi, out, normals=True)
    return kit.Frame.make(P, n, up=(0.0, 1.0, 0.0))


def _shell(head, line, thickness=0.032, inner=-0.014, slot="metal", rim_slot=None, rim_drop=0.035,
           rim_lip=0.5, rim_outer=True, peak=0.0, sides=36, rows=10, sculpt=None):
    """Closed snug skull shell down to `line` with an integrated chamfered rim (`rim_slot`)."""
    th = 2 * np.pi * np.arange(sides) / sides
    phi_end = _rim(head, line, th, peak)

    outer, inner_rings = [], []
    for i in range(rows + 1):
        t = i / rows
        P_out, n_out = head.surface(th, phi_end * t, thickness, normals=True)
        if sculpt is not None:
            P_out = sculpt(t, th, P_out, n_out)
        outer.append(P_out)
        inner_rings.append(head.surface(th, phi_end * t, inner))

    P_lip, n_lip = head.surface(th, np.clip(phi_end + rim_drop, 0.0, np.pi - 0.06), rim_lip * thickness, normals=True)
    if sculpt is not None:
        P_lip = sculpt(1.0, th, P_lip, n_lip)

    verts, faces = kit._loft(outer + [P_lip] + inner_rings[::-1], closed_rings=True, closed_profile=False)
    slots = np.full(len(faces), slot, dtype=object)
    if rim_slot is not None:
        start_band = rows - 1 if (rim_outer and rows >= 2) else rows
        slots[start_band * 2 * sides:(rows + 2) * 2 * sides] = rim_slot

    _, first, inverse = np.unique(np.round(verts / 1e-7).astype(np.int64), axis=0, return_index=True, return_inverse=True)
    verts = verts[first]
    faces = inverse.reshape(-1)[faces]
    keep = (faces[:, 0] != faces[:, 1]) & (faces[:, 1] != faces[:, 2]) & (faces[:, 0] != faces[:, 2])
    tri = verts[faces]
    keep &= np.linalg.norm(np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0]), axis=1) > 1e-12
    faces, slots = faces[keep], slots[keep]
    return kit.Piece(verts, kit._orient_closed(verts, faces), slots)


def _spangen_ribs(head, thetas, phi0, phi1, out, slot, width, depth, bevel):
    """Riveted structural ribs running down the crown along each azimuth in `thetas`."""
    prof = kit.rect_profile(width * head.A, depth, bevel)
    return [kit.sweep(head.surface(np.full(10, th), np.linspace(phi0, phi1, 10), out), prof, slot,
                      up=(0.0, 1.0, 0.0)) for th in thetas]


def _crown_keel(head, phi_front, phi_back, out, width, height, bevel, slot):
    """Sagittal comb ridge swept over the crown from `phi_front` to `phi_back`."""
    path = _over_crown(head, FRONT, phi_front, phi_back, out)
    return kit.sweep(path, kit.rect_profile(width * head.A, height, bevel), slot, up=(0.0, 0.0, 1.0))


def _rivet_arc(head, angles_deg, yn, out, radius, height, slot):
    """Evenly spaced studs hugging `head` at height share `yn`, at `angles_deg` from the front."""
    out_parts = []
    for deg in angles_deg:
        (Pr,), (nr,) = head.surface(FRONT + np.radians(deg), head.phi(yn), out, normals=True)
        out_parts.append(kit.stud(radius * head.A, height, slot).moved(kit.Frame.make(Pr, nr)))
    return out_parts


def _sculpted_point(head, line, sculpt, theta, yn, out=0.034):
    """A point on a `_shell` sculpted by `sculpt` (down to `line`) at angle `theta` and height share `yn`, lifted
    `out` off the head, with the head's normal there: for placing slits, bars and studs on the sculpted surface."""
    phi = float(head.phi(yn))
    t = np.clip(phi / max(float(_rim(head, line, theta)), 1e-6), 0.0, 1.0)
    (P,), (n,) = head.surface(theta, phi, out, normals=True)
    return sculpt(t, np.array([theta]), P[None, :], n[None, :])[0], n


def _eye_slits(assembly, head, point, d_theta, yn, slot, roll=0.0):
    """Cut a mirrored pair of bevelled eye slits into `assembly` at `FRONT +- d_theta` and height share `yn`;
    `point(theta, yn)` places them on the sculpted shell and `roll` tilts them (degrees, mirrored)."""
    for sgn in (-1, 1):
        P, n = point(FRONT + sgn * d_theta, yn)
        f = kit.Frame.make(P - 0.008 * n, n).rotated(sgn * roll, "z")
        assembly.cut(solid.box((0.40 * head.A, 0.105 * head.A, 0.068), (0.0, 0.0, 0.0), bevel=0.005).moved(f), slot)


def _horn_cuff(base, axis, radius, width, depth, slot):
    """Collar cuff seated perpendicular to a horn's exit direction `axis` at `base`."""
    axis = axis / max(np.linalg.norm(axis), 1e-9)
    ref = np.array([0.0, 1.0, 0.0]) if abs(axis[1]) < 0.9 else np.array([0.0, 0.0, 1.0])
    u = np.cross(axis, ref)
    u /= np.linalg.norm(u)
    v = np.cross(axis, u)
    ring = np.array([base + radius * (np.cos(a) * u + np.sin(a) * v) for a in np.linspace(0, 2 * np.pi, 11)[:-1]])
    return kit.sweep(ring, kit.rect_profile(width, depth, 0.008), slot, up=tuple(axis), closed=True)


def _merge_creased(*parts, crease=52.0):
    """Merge closed parts and generate crisp corner normals."""
    res = kit.Piece.merge(*parts)
    res.normals = corner_normals(res.verts, res.faces, crease=crease)
    return res


def bear(head, line=((0, 0.25), (52, 0.22), (98, 0.22), (132, -0.38), (160, -0.68), (180, -0.74)),
         thickness=0.036, slot="pelt", inner="pelt_dark", nose="dark", teeth="tooth", ears=1.0, snout=1.0,
         tooth_count=9, tilt=0.0, y_offset=0.0):
    """A tight bear-pelt hood hugging above the ears and dipping behind the nape, with compact brow snout,
    tucked concave ears, eyes, and sharp fangs."""
    A = head.A

    def _fur_sculpt(t, th, P, n):
        crest = 0.018 * A * np.sin(np.pi * t) ** 0.7 * (np.abs(np.sin(th)) ** 5)
        return P + crest[:, None] * n

    cap = _shell(head, line=line, thickness=thickness, slot=slot, rim_slot=inner, rim_drop=0.025, rim_lip=0.45,
                 rim_outer=False, sculpt=_fur_sculpt)
    parts = [cap]
    for sgn in (-1, 1):
        (P,), (n,) = head.surface(FRONT + sgn * 1.12, 0.56, thickness, normals=True)
        r = 0.27 * A * ears
        facing = [np.cos(FRONT + sgn * 0.52), 0.12, np.sin(FRONT + sgn * 0.52)]
        f = kit.Frame.make(P + 0.35 * r * n + [0, 0.32 * r, 0], facing)
        parts.append(_lump((0, 0, 0), (r, r, 0.42 * r), slot).moved(f))
        parts.append(_lump((0, -0.06 * r, 0.26 * r), (0.62 * r, 0.62 * r, 0.24 * r), inner).moved(f))
    brow = float(np.interp(0, *np.asarray(line, float).T))
    (P,), (n,) = head.surface(FRONT, head.phi(brow + 0.16), thickness, normals=True)
    s = 0.28 * A * snout
    f = kit.Frame.make(P - 0.008 * n, n)
    parts.append(_lump((0, 0.04 * s, 0.36 * s), (0.95 * s, 0.65 * s, 0.78 * s), slot).moved(f))
    parts.append(_lump((0, 0.14 * s, 0.98 * s), (0.45 * s, 0.30 * s, 0.25 * s), nose).moved(f))
    for sgn in (-1, 1):
        (Pe,), (ne,) = head.surface(FRONT + sgn * 0.42, head.phi(brow + 0.34), thickness, normals=True)
        parts.append(kit.gem(0.06 * A, nose, depth=0.025).moved(kit.Frame.make(Pe - 0.004 * ne, ne)))
    theta = FRONT + np.radians(np.linspace(-66, 66, tooth_count))
    phi = _rim(head, line, theta)
    rim = head.surface(theta, phi, 0.92 * thickness)
    along = head.surface(theta + 0.01, phi, 0.92 * thickness) - rim
    for k, (p, d, th) in enumerate(zip(rim, along, theta)):
        df = abs(th - FRONT)
        is_fang = abs(df - 0.42) < 0.14
        front = np.cos(0.5 * (th - FRONT)) ** 2
        w = (0.115 if is_fang else 0.082) * A
        h = A * ((0.24 if is_fang else 0.085 + 0.06 * front) * (1.0 if k % 2 == 0 or is_fang else 0.80))
        down = np.array([0.0, -1.0, 0.0])
        dx = d / max(np.linalg.norm(d), 1e-9)
        ft = kit.Frame(p, dx, -down, np.cross(dx, -down))
        parts.append(kit.prism([(-w, 0.03 * A), (w, 0.03 * A), (0.0, -h)], 0.026, teeth, bevel=0.005).moved(ft))
    res = _merge_creased(*parts, crease=60.0)
    return _with_hides(_apply_tilt_y(res, head, tilt=tilt, y_offset=y_offset), "hair")


def horned(head, brim=0.22, thickness=0.030, slot="metal", band="leather", horn="bone", rivet="metal", horns=1.0,
           horn_height=0.56, horn_angle=np.pi / 2, horn_radius=0.165, horn_spread=0.45, guard=1.0, ridge=True,
           seams=True, guard_upright=False, patina=False, dents=False, tilt=0.0, y_offset=0.0):
    """
    A tight viking half-helm clearing the ears: a riveted brow band, nasal guard with diamond boss, crown ridge
    and spangen ribs, and curved horns seated in beveled temple sockets.
    """
    A = head.A
    line = ((0, brim), (72, max(brim + 0.01, 0.22)), (108, max(brim + 0.01, 0.22)), (145, brim - 0.14), (180, brim - 0.24))
    cap = _shell(head, line=line, thickness=thickness, slot=slot, sides=32)
    band_out = thickness + 0.012
    brow_band = heads.surface_band(head, brim + 0.05, 0.15, band_out, band, tilt=(0.03, 0.0), sides=32, bevel=0.38)

    if patina or dents:
        light_s, worn_s, dark_s = f"{slot}_light", f"{slot}_worn", f"{slot}_dark"
        paintable_cap = {slot, light_s, worn_s, dark_s, "tarnish", "rust"}
        if patina:
            cap = kit.weather(cap, slot, light_slot=light_s, worn_slot=worn_s, dark_slot=dark_s)
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
                brow_band, _head_frame(head, FRONT + 0.35, head.phi(brim + 0.05), band_out),
                [p_band_rust], "rust" if not is_l_band else "grime")
        d_cnt, d_scale = kit.parse_dents(dents, max_allowed=2)
        if d_cnt >= 1:
            f_d1 = _head_frame(head, FRONT + 0.44, head.phi(brim + 0.46), thickness).rotated(32.0, "z")
            cap = kit.gash(cap, f_d1, length=0.45 * A * d_scale, width=0.12 * A * d_scale, depth=0.026 * A * d_scale,
                           slot=worn_s, shadow_slot=dark_s, paintable=paintable_cap)
        if d_cnt >= 2:
            f_d2 = _head_frame(head, FRONT - 0.46, head.phi(brim + 0.52), thickness).rotated(-36.0, "z")
            cap = kit.gash(cap, f_d2, length=0.34 * A * d_scale, width=0.10 * A * d_scale, depth=0.022 * A * d_scale,
                           slot=worn_s, shadow_slot=dark_s, paintable=paintable_cap)
            paintable_band = {band, "tarnish", "rust", "grime", "dirt"}
            f_b1 = _head_frame(head, FRONT + 0.58, head.phi(brim + 0.06), band_out).rotated(18.0, "z")
            brow_band = kit.gash(brow_band, f_b1, length=0.17 * A * d_scale, width=0.058 * A * d_scale,
                                 depth=0.014 * A * d_scale, slot="tarnish", shadow_slot="rust",
                                 paintable=paintable_band)

    parts = [cap, brow_band]
    if ridge:
        ridge_slot = f"{slot}_worn" if patina else slot
        r_piece = _crown_keel(head, head.phi(brim + 0.12), head.phi(brim - 0.06), thickness + 0.008,
                              0.14, 0.034, 0.010, ridge_slot)
        if patina:
            r_piece = kit.weather(r_piece, ridge_slot, light_slot=f"{slot}_light", dark_slot=f"{slot}_dark",
                                  rust_slot="tarnish", seed=23)
        parts.append(r_piece)
    if seams:
        seam_slot = f"{slot}_dark" if patina else slot
        parts.extend(_spangen_ribs(head, (FRONT - 0.92, FRONT + 0.92, 3 * np.pi / 2 - 0.92, 3 * np.pi / 2 + 0.92),
                                   head.phi(brim + 0.14), 0.30, thickness + 0.006, seam_slot,
                                   width=0.048, depth=0.018, bevel=0.005))
    (P,), (n,) = head.surface(FRONT, head.phi(brim + 0.03), thickness + 0.014, normals=True)
    g = guard * A
    f = kit.Frame.make(P, n * [1.0, 0.0, 1.0] if guard_upright else n).rotated(-5.0, "x")
    guard_piece = kit.prism([(-0.12 * g, 0.07 * g), (0.12 * g, 0.07 * g), (0.08 * g, -0.42 * g), (0.0, -0.58 * g),
                             (-0.08 * g, -0.42 * g)], 0.045 * max(guard, 0.3), slot, bevel=0.012)
    if patina:
        guard_piece = kit.weather(guard_piece, slot, light_slot=f"{slot}_light", worn_slot=f"{slot}_worn",
                                  dark_slot=f"{slot}_dark", seed=29)
    parts.append(guard_piece.moved(f))
    boss_slot = f"{slot}_light" if patina else slot
    parts.append(kit.gem(0.085 * g, boss_slot, cut="diamond", depth=0.038).moved(kit.Frame(f.to_world([0, 0.03 * g, 0.042]),
                                                                                           *f.R.T)))
    for idx_r, deg in enumerate(np.arange(-150, 151, 25)):
        (Pr,), (nr,) = head.surface(FRONT + np.radians(deg), head.phi(brim + 0.09), band_out + 0.005, normals=True)
        r_slot = (f"{slot}_light" if idx_r % 2 == 0 else "rust") if patina else rivet
        parts.append(kit.stud(0.038 * A, 0.018, r_slot).moved(kit.Frame.make(Pr, nr)))
    L, r = 0.96 * A * horns, horn_radius * A * horns
    for sgn in ((-1, 1) if horns else ()):
        theta = FRONT - sgn * horn_angle
        (Ph,), (nh,) = head.surface(theta, head.phi(horn_height), thickness - 0.004, normals=True)
        side = np.array([np.sign(np.cos(theta)) or sgn, 0.0, 0.0])
        up = np.array([0.0, 1.0, 0.0])
        k = horn_spread
        path = kit.spline([Ph - 0.02 * nh, Ph + 0.72 * k * L * side + 0.10 * L * up, Ph + k * L * side + 0.48 * L * up,
                           Ph + 0.78 * k * L * side + L * up], 10)
        parts.append(kit.tube(path, r, horn, sides=8, taper=0.82))
        parts.append(_horn_cuff(Ph + 0.042 * nh, path[1] - path[0], 1.15 * r, 0.08 * A, 0.042, band))
    res = _merge_creased(*parts, crease=55.0)
    return _with_hides(_apply_tilt_y(res, head, tilt=tilt, y_offset=y_offset), "hair")


def headdress(head, band_y=0.25, slot="leather", skull="bone", feather="feather", tip="feather_tip", count=13,
              length=0.90, spread=200.0, splay=(12.0, 72.0), horn_length=0.80, skull_size=0.55,
              skull_y=None, tilt=0.0, y_offset=0.0):
    """Shaman's war bonnet seated snugly at `band_y` above the ears: leather brow band, horned forehead skull,
    and two-toned feathered halo."""
    from prefabs.outfits import belts

    A = head.A
    parts = [heads.surface_band(head, band_y, 0.14, 0.028, slot, sides=32, bevel=0.35)]
    for k in range(count):
        theta = 3 * np.pi / 2 + np.radians(spread) * (k / (count - 1) - 0.5)
        (P,), (n,) = head.surface(theta, head.phi(band_y + 0.09), 0.018, normals=True)
        out = np.array([n[0], 0.0, n[2]]) / max(np.hypot(n[0], n[2]), 1e-9)
        lean = np.radians(splay[0] + (splay[1] - splay[0]) * abs(2 * k / (count - 1) - 1))
        up = np.cos(lean) * np.array([0.0, 1.0, 0.0]) + np.sin(lean) * out
        z = out - (out @ up) * up
        z /= np.linalg.norm(z)
        L, w = length * A * (0.85 + 0.15 * np.cos(np.pi * (k / (count - 1) - 0.5))), 0.15 * A
        vane = kit.prism([(-w, 0.12 * L), (0.0, 0.0), (w, 0.12 * L), (0.9 * w, 0.7 * L), (0.0, 0.78 * L),
                          (-0.9 * w, 0.7 * L)], 0.018, feather, bevel=0.004)
        end = kit.prism([(-0.9 * w, 0.7 * L), (0.9 * w, 0.7 * L), (0.0, L)], 0.022, tip, bevel=0.004)
        parts.append(kit.Piece.merge(vane, end).moved(kit.Frame(P, np.cross(up, z), up, z)))
    actual_skull_y = band_y if skull_y is None else skull_y
    (P,), (n,) = head.surface(FRONT, head.phi(actual_skull_y), 0.032, normals=True)
    f = kit.Frame.make(P, n * [1.0, 0.4, 1.0]).rotated(-15.0, "x")
    parts.append(belts.skull(skull_size * A, skull).moved(f))
    L, r = horn_length * A, 0.08 * A
    for sgn in (-1, 1):
        base = f.to_world([sgn * 0.3 * skull_size * A, 0.12 * skull_size * A, 0.1 * skull_size * A])
        side = sgn * f.R[:, 0]
        path = kit.spline([base, base + 0.55 * L * side + 0.05 * L * f.R[:, 1], base + 0.85 * L * side + 0.45 * L * f.R[:, 1],
                           base + 0.7 * L * side + 0.85 * L * f.R[:, 1]], 10)
        parts.append(kit.tube(path, r, skull, sides=6, taper=0.7))
    res = kit.Piece.merge(*parts)
    return _with_hides(_apply_tilt_y(res, head, tilt=tilt, y_offset=y_offset), "hair")


def wizard(head, brim_y=0.28, brim=1.68, height=2.25, bend=0.78, slot="hat", band="trim", runes="rune",
           sides=18, tilt=-7.0, y_offset=0.0):
    """A tilted wizard's hat: wide wavy brim with trimmed perimeter, clean buckled foot band with rune studs,
    and a tall crooked spire whose base clears the cranium completely."""
    from prefabs.outfits import belts

    A = head.A
    cx, cy, cz = head.centre
    y0 = head.y(brim_y)
    gap = 0.036  # the spire's clearance off the cranium
    sec = head.section([y0])
    r0_x, r0_z = float(sec[1][0]) + gap, float(sec[3][0]) + gap

    def around(a):
        """Directions at angles `a` round the cranium's squircle cross-section (`head.p`)."""
        return heads._spow(np.cos(a), 2.0 / head.p), heads._spow(np.sin(a), 2.0 / head.p)

    n_brim = sides * 2
    th = 2 * np.pi * np.arange(n_brim) / n_brim
    wave = (0.045 * np.cos(2 * th - 0.6) + 0.028 * np.sin(3 * th + 0.4)) * A
    notch = 1.0 - 0.06 * np.exp(-((th - 0.45) / 0.12) ** 2)

    def brim_ring(rx, rz, dy, add_wave=1.0):
        return np.column_stack([cx + rx * np.cos(th), y0 + dy + add_wave * wave, cz + rz * np.sin(th)])

    R_out = brim * A * notch
    brim_rings = [
        brim_ring(0.84 * r0_x, 0.84 * r0_z, 0.008 * A, 0.0),
        brim_ring(0.70 * R_out, 0.70 * R_out, -0.045 * A, 0.45),
        brim_ring(0.98 * R_out, 0.98 * R_out, -0.15 * A, 0.9),
        brim_ring(R_out, R_out, -0.19 * A, 1.0),
        brim_ring(0.96 * R_out, 0.96 * R_out, -0.23 * A, 0.95),
        brim_ring(0.70 * R_out, 0.70 * R_out, -0.11 * A, 0.45),
        brim_ring(0.84 * r0_x, 0.84 * r0_z, -0.07 * A, 0.0),
    ]
    bv, bf = kit._loft(brim_rings, closed_rings=True, closed_profile=True)
    bslots = np.full(len(bf), slot, dtype=object)
    # Color outer brim bevel bands (rings 2->3->4) in `band`.
    bslots[4 * n_brim:8 * n_brim] = band
    parts = [kit.Piece(bv, kit._orient_closed(bv, bf), bslots)]

    H = height * A
    y_crown = cy + head.Hh + 0.08 * A
    cone_rings = []
    steps = 14
    t_vals = np.linspace(0.0, 1.0, steps)
    dark_slot = f"{slot}_dark"
    th_cone = 2 * np.pi * np.arange(sides) / sides
    ux, uz = around(th_cone)
    for t in t_vals:
        rise = max(0.0, (t - 0.42) / 0.58)  # 0 up to the crown, then 0 -> 1 along the crooked spire
        y_ring = y0 + (y_crown - y0) * min(t / 0.42, 1.0) + rise * (y0 + 0.88 * H - y_crown)
        # Keep the spire centered over the cranium until above y_crown, then crook back cleanly.
        bend_t = rise ** 1.35
        zc = cz - 0.72 * bend * H * bend_t
        yc_drop = -0.14 * bend * H * max(0.0, (t - 0.68) / 0.32) ** 1.5
        k = max((1.0 - t) ** 0.85, 0.035)
        rx_t, rz_t = r0_x * k, r0_z * k
        if y_ring < cy + head.Hh + 0.02:
            s_ring = head.section([np.clip(y_ring, head.y(-0.9), head.y(0.99))])
            rx_t = max(rx_t, float(s_ring[1][0]) + gap)
            rz_t = max(rz_t, float(s_ring[3][0]) + gap)
        crinkle = 1.0 - 0.045 * rise * np.sin(np.pi * t) * np.cos(3 * th_cone + 2.0 * t)
        cone_rings.append(np.column_stack([
            cx + rx_t * crinkle * ux,
            np.full(sides, y_ring + yc_drop),
            zc + rz_t * crinkle * uz,
        ]))
    cv, cf = kit._loft(cone_rings, closed_rings=True, closed_profile=False)
    cv, cf = kit._cap_loops(cv, cf, [list(range(sides)), list(range((steps - 1) * sides, steps * sides))])
    parts.append(kit.Piece(cv, kit._orient_closed(cv, cf), slot))

    # Fitted squircle band hugging the spire base cleanly above the brim.
    y_band = y0 + 0.11 * A
    band_loop = np.column_stack([cx + 1.02 * r0_x * ux, np.full(sides, y_band),
                                 cz + 1.02 * r0_z * uz])
    parts.append(kit.sweep(band_loop, kit.rect_profile(0.032, 0.13 * A, 0.008), dark_slot if band == slot else band,
                           closed=True, up=(0.0, 1.0, 0.0)))
    ba = FRONT + 0.68
    b_ux, b_uz = around(ba)
    bp = np.array([cx + 1.04 * r0_x * b_ux, y_band, cz + 1.04 * r0_z * b_uz])
    parts.append(belts.frame_buckle(0.11 * A, band).moved(kit.Frame.make(bp, (np.cos(ba), 0.0, np.sin(ba)))))
    for k in range(5):
        a = FRONT + np.radians(-48 + 24 * k)
        a_ux, a_uz = around(a)
        P = np.array([cx + 1.055 * r0_x * a_ux, y_band + 0.005 * A, cz + 1.055 * r0_z * a_uz])
        f = kit.Frame.make(P, (np.cos(a), 0.0, np.sin(a)))
        parts.append(kit.prism([(-0.030, -0.035), (0.030, -0.035), (0.0, 0.042)] if k % 2 else
                               [(-0.025, -0.035), (0.025, -0.035), (0.025, 0.035), (-0.025, 0.035)],
                               0.012, runes, bevel=0.003).moved(f))
    res = _merge_creased(*parts, crease=52.0)
    return _with_hides(_apply_tilt_y(res, head, tilt=tilt, y_offset=y_offset), "hair")


def knight(head, slot="armor", trim="trim", rivet="rivet", bevor=False, crest=True, tilt=0.0, y_offset=0.0):
    """Snug open-face knight helm: raised keeled sallet dome, forward-peaked faceted brow visor, temple-hinged
    cheek plates, deep nape skirt, and optional `bevor` chin guard."""
    A = head.A
    line = ((0, 0.24), (52, 0.16), (82, -0.34), (118, -0.66), (180, -0.82))

    def _keel_sculpt(t, th, P, n):
        # Tall medieval sagittal crest ridge + subtle crown raise over the top.
        keel = 0.095 * A * np.sin(np.pi * t) ** 0.7 * (np.abs(np.sin(th)) ** 7)
        lift_top = 0.045 * A * ((1.0 - t) ** 1.4) * np.array([0.0, 1.0, 0.0])
        brow = 0.045 * A * (t ** 1.8) * (np.clip(np.sin(th), 0.0, None) ** 4)
        return P + (keel + brow)[:, None] * n + lift_top[None, :]

    cap = _shell(head, line=line, slot=slot, rim_slot=trim, rim_drop=0.03, rim_lip=0.55, peak=0.08,
                 sculpt=_keel_sculpt)
    parts = [cap]

    # Forward-angled faceted brow visor projecting cleanly over the forehead.
    th_v = FRONT + np.linspace(-1.02, 1.02, 17)
    outer_v, inner_v = [], []
    y_upper = 0.36 - 0.10 * np.cos(1.5 * (th_v - FRONT))
    y_lower = 0.22 - 0.13 * np.cos(1.5 * (th_v - FRONT))
    prow_v = 0.095 * A * np.exp(-((th_v - FRONT) / 0.56) ** 2)
    outer_v.append(head.surface(th_v, head.phi(y_upper), 0.034 + 0.35 * prow_v))
    outer_v.append(head.surface(th_v, head.phi(y_lower), 0.056 + prow_v))
    inner_v.append(head.surface(th_v, head.phi(y_upper), 0.010))
    inner_v.append(head.surface(th_v, head.phi(y_lower), 0.012))
    parts.append(kit.patch_shell(np.asarray(outer_v), np.asarray(inner_v), trim))

    # Thicker faceted cheek guards wrapping cleanly down along the temples/jaw.
    for sgn in (-1, 1):
        th_c = FRONT + sgn * np.linspace(0.58, 1.38, 8)
        y_rows = (-0.64, -0.26, 0.14)
        out_rows = (0.040, 0.046, 0.042)
        g_out = np.asarray([head.surface(th_c, head.phi(yr - 0.12 * np.linspace(0, 1, 8)), oo)
                            for yr, oo in zip(y_rows, out_rows)])
        g_in = np.asarray([head.surface(th_c, head.phi(yr - 0.12 * np.linspace(0, 1, 8)), -0.008)
                           for yr in y_rows])
        parts.append(kit.patch_shell(g_out, g_in, slot))
        (Ph,), (nh,) = head.surface(FRONT + sgn * 1.12, head.phi(0.14), 0.046, normals=True)
        fh = kit.Frame.make(Ph, nh)
        parts.append(kit.stud(0.090 * A, 0.024, trim).moved(fh))
        parts.append(kit.stud(0.042 * A, 0.034, rivet).moved(fh))

    if crest:
        # Raised architectural comb arch over the crown.
        parts.append(_crown_keel(head, head.phi(0.32), head.phi(-0.74), 0.048, 0.12, 0.052, 0.012, trim))
        parts.extend(_spangen_ribs(head, (FRONT - 0.92, FRONT + 0.92), head.phi(0.16), 0.25, 0.036, trim,
                                   width=0.046, depth=0.016, bevel=0.004))

    if bevor:
        th_b = FRONT + np.linspace(-1.08, 1.08, 17)
        y_rows = (-0.84, -0.52, -0.22)
        prow = 0.068 * A * np.cos(1.15 * (th_b - FRONT)) ** 2
        b_out = np.asarray([head.surface(th_b, head.phi(yr), 0.044 + prow) for yr in y_rows])
        b_in = np.asarray([head.surface(th_b, head.phi(yr), -0.008 + 0.4 * prow) for yr in y_rows])
        parts.append(kit.patch_shell(b_out, b_in, slot))
        parts.extend(_rivet_arc(head, (-42, 42), -0.44, 0.050, 0.036, 0.016, rivet))
        hides = ("ears", "hair", "beard")
    else:
        hides = ("ears", "hair")

    res = _merge_creased(*parts, crease=46.0)
    return _with_hides(_apply_tilt_y(res, head, tilt=tilt, y_offset=y_offset), *hides)


def crusader(head, slot="armor", trim="trim", rivet="rivet", dark="dark", tilt=0.0, y_offset=0.0):
    """Tight cylindrical greathelm with flat chamfered crown, V-prow faceplate, bold recessed dark eye slits,
    cheek breathers, and cruciform brass straps."""
    A = head.A
    cx, cy, cz = head.centre
    line = ((0, -0.84), (70, -0.86), (180, -0.88))

    def _greathelm_sculpt(t, th, P, n):
        barrel_r_scale = 1.0 + 0.36 * max(0.0, (t - 0.45) / 0.55) ** 1.4
        dx, dz = P[:, 0] - cx, P[:, 2] - cz
        prow = 0.085 * A * (np.clip(np.sin(th), 0.0, None) ** 2.2) * (t ** 0.7)
        out = np.column_stack([
            cx + dx * barrel_r_scale,
            P[:, 1] + 0.04 * A * (1.0 - t) ** 1.6,
            cz + dz * barrel_r_scale + prow,
        ])
        out[:, 1] = np.minimum(out[:, 1], cy + head.Hh + 0.055 * A)
        return out

    def _sculpted_pt(th, yn, out=0.034):
        return _sculpted_point(head, line, _greathelm_sculpt, th, yn, out)

    shell = _shell(head, line=line, thickness=0.034, inner=-0.018, slot=slot, rim_slot=trim, rim_drop=0.025,
                   rim_lip=0.55, sides=32, rows=12, sculpt=_greathelm_sculpt)

    assembly = solid.Assembly().add(shell)
    _eye_slits(assembly, head, _sculpted_pt, 0.28, 0.05, dark)
    for sgn in (-1, 1):
        for y_b in (-0.28, -0.46):
            for d_th in (0.34, 0.56):
                Pb, nb = _sculpted_pt(FRONT + sgn * d_th, y_b)
                fb = kit.Frame.make(Pb - 0.008 * nb, nb)
                assembly.cut(solid.box((0.068 * A, 0.068 * A, 0.064), (0.0, 0.0, 0.0), bevel=0.004).moved(fb), dark)
    hull = assembly.build()

    parts = [hull]
    # Vertical cruciform nasal bar & brow band sculpted flush on the actual barrel surface.
    n_pts = [_sculpted_pt(FRONT, yn, out=0.042)[0] for yn in np.linspace(0.58, -0.78, 12)]
    parts.append(kit.sweep(np.asarray(n_pts), kit.rect_profile(0.14 * A, 0.026, 0.006), trim, up=(0.0, 0.0, 1.0)))
    b_pts = [_sculpted_pt(th, 0.16, out=0.042)[0] for th in FRONT + np.linspace(-0.92, 0.92, 15)]
    parts.append(kit.sweep(np.asarray(b_pts), kit.rect_profile(0.105 * A, 0.024, 0.006), trim, up=(0.0, 1.0, 0.0)))
    parts.append(_crown_keel(head, head.phi(0.56), head.phi(-0.66), 0.056, 0.13, 0.022, 0.006, trim))
    for deg in (-42, -24, 24, 42):
        Pr, nr = _sculpted_pt(FRONT + np.radians(deg), 0.16, out=0.054)
        parts.append(kit.stud(0.036 * A, 0.016, rivet).moved(kit.Frame.make(Pr, nr)))
    res = _merge_creased(*parts, crease=42.0)
    return _with_hides(_apply_tilt_y(res, head, tilt=tilt, y_offset=y_offset), "ears", "hair", "beard")


def dark_knight(head, slot="armor_dark", trim="trim", gem="gem", horn="metal", dark="dark", gash=True, tilt=0.0,
                y_offset=0.0):
    """Angular keeled dread helm: tall ridged silhouette with flared lower cheek/nape guard, crisp V-slit visor,
    forehead ruby gem, swept brow-temple horns with inline cuffs, and chiseled cheek gash."""
    A = head.A
    cx, _, cz = head.centre
    line = ((0, -0.78), (62, -0.74), (108, -0.70), (180, -0.78))

    def _dread_sculpt(t, th, P, n):
        keel = 0.090 * A * np.sin(np.pi * t) ** 0.7 * (np.abs(np.sin(th)) ** 7)
        prow = 0.095 * A * (np.clip(np.sin(th), 0.0, None) ** 2.2) * (t ** 0.85)
        flare = 0.055 * A * (t ** 2.6) * (1.0 - 0.5 * np.clip(np.sin(th), 0.0, None))
        jaw_barrel = 0.20 * max(0.0, (t - 0.5) / 0.5) ** 1.4
        dx, dz = P[:, 0] - cx, P[:, 2] - cz
        return np.column_stack([cx + dx * (1.0 + jaw_barrel), P[:, 1], cz + dz * (1.0 + jaw_barrel)]) + \
            (keel + prow + flare)[:, None] * n

    def _dread_pt(th, yn, out=0.034):
        return _sculpted_point(head, line, _dread_sculpt, th, yn, out)

    shell = _shell(head, line=line, thickness=0.034, inner=-0.016, slot=slot, rim_slot=trim, rim_drop=0.03,
                   rim_lip=0.55, peak=0.06, sides=28, sculpt=_dread_sculpt)
    assembly = solid.Assembly().add(shell)

    # Wide crisp upward-angled V-slit cutouts right on the sculpted faceplate surface.
    _eye_slits(assembly, head, _dread_pt, 0.32, 0.06, dark, roll=18.0)
    if gash:
        Pg, ng = _dread_pt(FRONT + 0.62, -0.28)
        fg = kit.Frame.make(Pg, ng).rotated(28.0, "z")
        assembly.cut(wear.solid_gash(0.32 * A, 0.056 * A, 0.026).moved(fg), dark)
    hull = assembly.build()

    parts = [hull]
    parts.append(_crown_keel(head, head.phi(0.30), head.phi(-0.62), 0.054, 0.12, 0.048, 0.010, trim))
    # Forehead crimson gem in a faceted bezel.
    Pf, nf = _dread_pt(FRONT, 0.32, out=0.044)
    ff = kit.Frame.make(Pf, nf)
    parts.append(kit.gem(0.12 * A, trim, cut="diamond", depth=0.024).moved(ff))
    parts.append(kit.gem(0.082 * A, gem, cut="diamond", depth=0.038).moved(
        kit.Frame(ff.to_world([0.0, 0.0, 0.015]), *ff.R.T)))

    # Swept dread horns fanning outward from the upper brow-temples with inline cuffs.
    L, r = 1.25 * A, 0.155 * A
    for sgn in (-1, 1):
        theta = FRONT + sgn * 0.78
        (Ph,), (nh,) = head.surface(theta, head.phi(0.56), 0.026, normals=True)
        out_x = -sgn  # theta = pi/2 + sgn*0.78 puts +x for sgn=-1 and -x for sgn=+1
        side = np.array([out_x * 0.72, 0.32, -0.62])
        side /= np.linalg.norm(side)
        up = np.array([0.0, 1.0, 0.0])
        path = kit.spline([Ph - 0.015 * nh, Ph + 0.42 * L * side + 0.22 * L * up,
                           Ph + 0.78 * L * side + 0.70 * L * up, Ph + 0.56 * L * side + 1.08 * L * up], 10)
        parts.append(kit.tube(path, r, horn, sides=6, taper=0.80))
        parts.append(_horn_cuff(Ph + 0.05 * (path[1] - path[0]), path[1] - path[0], 1.15 * r, 0.08 * A, 0.038, trim))

    res = _merge_creased(*parts, crease=44.0)
    return _with_hides(_apply_tilt_y(res, head, tilt=tilt, y_offset=y_offset), "ears", "hair", "beard")


def kettle(head, slot="armor", trim="trim", strap="leather", rivet="rivet", brim_out=0.28, brim_drop=0.16,
           tilt=0.0, y_offset=0.0):
    """Tight kettle helm clearing the ears, with a flared sloped brim, spangen ribs, finial, and buckled cheek
    straps."""
    A = head.A
    line = ((0, 0.24), (75, 0.23), (112, 0.23), (150, 0.14), (180, 0.10))
    cap = _shell(head, line=line, thickness=0.028, slot=slot)
    parts = [cap]

    sides = 36
    th = 2 * np.pi * np.arange(sides) / sides
    phi_edge = _rim(head, line, th)
    P_in, n_in = head.surface(th, phi_edge, 0.012, normals=True)
    out_dir = np.column_stack([n_in[:, 0], np.zeros(sides), n_in[:, 2]])
    out_dir /= np.maximum(np.linalg.norm(out_dir, axis=1, keepdims=True), 1e-9)
    reach = (brim_out + 0.05 * np.sin(th) ** 2) * A
    drop = np.array([0.0, -brim_drop * A, 0.0])
    w_top = P_in + 0.016 * np.array([0.0, 1.0, 0.0])
    w_mid = P_in + 0.88 * reach[:, None] * out_dir + 0.75 * drop + 0.012 * np.array([0.0, 1.0, 0.0])
    w_tip = P_in + reach[:, None] * out_dir + drop
    w_bot_mid = P_in + 0.88 * reach[:, None] * out_dir + 0.75 * drop - 0.012 * np.array([0.0, 1.0, 0.0])
    w_bot = P_in - 0.014 * np.array([0.0, 1.0, 0.0])
    bv, bf = kit._loft([w_top, w_mid, w_tip, w_bot_mid, w_bot], closed_rings=True, closed_profile=True)
    bslots = np.full(len(bf), slot, dtype=object)
    bslots[sides * 2:sides * 6] = trim
    parts.append(kit.Piece(bv, kit._orient_closed(bv, bf), bslots))

    parts.extend(_spangen_ribs(head, (FRONT, 3 * np.pi / 2, FRONT - 1.15, FRONT + 1.15),
                               head.phi(0.24), 0.16, 0.032, trim, width=0.055, depth=0.016, bevel=0.005))
    (Pc,), (nc,) = head.surface(FRONT, 0.0, 0.032, normals=True)
    parts.append(kit.stud(0.095 * A, 0.030, trim).moved(kit.Frame.make(Pc, nc)))
    parts.extend(_rivet_arc(head, np.arange(-150, 151, 30), 0.30, 0.038, 0.032, 0.014, rivet))

    for sgn in (-1, 1):
        th_s = np.full(6, FRONT + sgn * 1.18)
        ys = np.linspace(0.20, -0.58, 6)
        path = head.surface(th_s, head.phi(ys), 0.022)
        parts.append(kit.sweep(path, kit.rect_profile(0.085 * A, 0.016, 0.004), strap, up=(0.0, 0.0, 1.0)))
        (Ps,), (ns,) = head.surface(FRONT + sgn * 1.18, head.phi(-0.26), 0.030, normals=True)
        parts.append(kit.stud(0.034 * A, 0.014, trim).moved(kit.Frame.make(Ps, ns)))

    res = _merge_creased(*parts, crease=50.0)
    return _with_hides(_apply_tilt_y(res, head, tilt=tilt, y_offset=y_offset), "hair")


def coif(head, slot="mail", dark_slot="mail_dark", band="leather", rivet="rivet", nasal=False,
         tilt=0.0, y_offset=0.0):
    """Snug chain-mail hood and neck camail: dense contoured wire rings embedded smoothly over a dark backing hood,
    crowned with a riveted brow circlet and optional nasal bar."""
    from prefabs.outfits.mail_costumes import _ring

    A = head.A
    line = ((0, 0.28), (44, 0.20), (62, -0.32), (82, -0.78), (180, -0.92))
    backing = _shell(head, line=line, thickness=0.025, slot=dark_slot, rim_slot=slot, rim_drop=0.028, rim_lip=0.60,
                     rows=11)
    parts = [backing]

    # Bend local wire-ring slightly backward in -z so every ring hugs curved cranium seamlessly.
    ring = _ring()
    ring.verts *= 1.08
    r_curve = np.sum(ring.verts[:, :2] ** 2, axis=1) / max(1.6 * A, 1e-6)
    ring.verts[:, 2] -= r_curve
    ring.slots[:] = slot

    ring_specs = (
        (-0.80, np.linspace(78, 282, 18)),
        (-0.56, np.linspace(70, 290, 20)),
        (-0.30, np.linspace(64, 296, 20)),
        (-0.04, np.linspace(58, 302, 21)),
        (0.18, np.linspace(54, 306, 21)),
        (0.46, np.linspace(0, 342, 22)),
        (0.68, np.linspace(20, 324, 15)),
    )
    for r_idx, (yn, degs) in enumerate(ring_specs):
        shift = 0.5 * (degs[1] - degs[0]) if r_idx % 2 else 0.0
        for d in degs:
            th = FRONT + np.radians(d + shift)
            phi_end = _rim(head, line, th)
            phi = head.phi(yn)
            if phi <= phi_end - 0.06:
                (P,), (n,) = head.surface(th, phi, 0.024, normals=True)
                parts.append(ring.moved(kit.Frame.make(P, n)))

    parts.append(heads.surface_band(head, 0.34, 0.11, 0.034, band, tilt=(0.02, 0.0), sides=32, bevel=0.35))
    parts.extend(_rivet_arc(head, np.arange(-150, 151, 30), 0.34, 0.038, 0.030, 0.012, rivet))
    if nasal:
        (Pn,), (nn,) = head.surface(FRONT, head.phi(0.28), 0.036, normals=True)
        fn = kit.Frame.make(Pn, nn).rotated(-4.0, "x")
        parts.append(kit.prism([(-0.08 * A, 0.05 * A), (0.08 * A, 0.05 * A), (0.06 * A, -0.34 * A),
                                (0.0, -0.44 * A), (-0.06 * A, -0.34 * A)], 0.028, slot, bevel=0.008).moved(fn))

    res = _merge_creased(*parts, crease=55.0)
    return _with_hides(_apply_tilt_y(res, head, tilt=tilt, y_offset=y_offset), "ears", "hair")


def hood(head, slot="cloak", dark_slot="cloak_dark", stitch="stitch", peak=0.10, tail=0.85,
         tilt=0.0, y_offset=0.0):
    """Snug draped ranger cowl with a peaked brow overhang, folded cuff rim, center seam, and draped back tail."""
    A = head.A
    line = ((0, 0.34), (48, 0.14), (66, -0.42), (90, -0.82), (180, -0.92))

    def _cowl_sculpt(t, th, P, n):
        front_w = np.clip(np.sin(th), 0.0, None) ** 3
        back_w = np.clip(-np.sin(th), 0.0, None) ** 2.5
        brow_out = 0.11 * A * (t ** 1.35) * front_w
        peak_up = (0.055 * A * (t ** 1.5) * (front_w ** 2))[:, None] * np.array([0.0, 1.0, 0.0])
        back_pull = 0.10 * A * (np.sin(np.pi * 0.5 * t) ** 0.8) * back_w
        folds = 0.022 * A * np.sin(np.pi * t) * np.cos(4.0 * th)
        return P + (brow_out + back_pull + folds)[:, None] * n + peak_up

    cowl = _shell(head, line=line, slot=slot, rim_slot=dark_slot, rim_lip=0.68, peak=peak, rows=11,
                  sculpt=_cowl_sculpt)
    parts = [cowl]

    parts.append(_crown_keel(head, head.phi(0.42), head.phi(-0.68), 0.035, 0.052, 0.014, 0.004, stitch))

    # Softly drooping tapered cloth hood tail flowing smoothly down the back of the cowl.
    (Pt,), (nt,) = head.surface(3 * np.pi / 2, 0.68, 0.024, normals=True)
    back = np.array([0.0, 0.0, -1.0])
    up = np.array([0.0, 1.0, 0.0])
    L = tail * A
    tail_path = kit.spline([
        Pt - 0.04 * nt,
        Pt + 0.24 * L * back - 0.18 * L * up,
        Pt + 0.44 * L * back - 0.54 * L * up,
        Pt + 0.50 * L * back - 0.96 * L * up,
    ], 12)
    parts.append(kit.tube(tail_path, 0.29 * A, slot, sides=10, taper=0.88))

    res = _merge_creased(*parts, crease=60.0)
    return _with_hides(_apply_tilt_y(res, head, tilt=tilt, y_offset=y_offset), "ears", "hair")


# Characters merge this palette over every other module's, so it adds helmet-only slots and never re-colors shared
# ones (`armor`, `mail`, `rivet`, `gem`, `gold`, `stitch`, `metal_*` come from the armor and demo palettes).
COLORS = {"pelt": (112, 96, 88), "pelt_dark": (86, 72, 66), "dark": (28, 24, 26), "tooth": (240, 240, 236),
          "metal": (170, 178, 190), "leather": (122, 78, 52), "bone": (232, 222, 196), "feather": (236, 232, 220),
          "feather_tip": (60, 44, 40), "hat": (40, 44, 80), "hat_dark": (30, 32, 64), "trim": (190, 196, 210),
          "rune": (150, 110, 230), "mail_dark": (92, 98, 108), "cloak": (62, 82, 66), "cloak_dark": (42, 56, 46)}

STYLES = {
    "bear": bear,
    "horned": horned,
    "headdress": headdress,
    "wizard": wizard,
    "knight": knight,
    "crusader": crusader,
    "dark_knight": dark_knight,
    "kettle": kettle,
    "coif": coif,
    "hood": hood,
}


def catalog():
    """Each helmet mounted on a plain human head with covered features suppressed."""
    plain = dict(form=heads.FORMS["human"], eyes=dict(), brows=dict(), nose=dict(style="button"),
                 mouth=dict(curve=0.15), ears=dict(style="round"))
    head, bare = heads.build(plain)
    worn = {frozenset(): bare}  # the plain head has no hair, so it differs only by the features a helmet drops
    for name, fn in STYLES.items():
        helm = fn(head)
        hidden = frozenset(helm.hides).intersection(plain)
        if hidden not in worn:
            worn[hidden] = heads.build(plain, suppress=hidden)[1]
        palette = {**kit.DEMO_COLORS, **clothes.COLORS, **armor.COLORS, **heads.COLORS, **COLORS}
        yield name, kit.Piece.merge(worn[hidden], helm), None, palette
