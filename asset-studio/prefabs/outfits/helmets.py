"""
Helmets: separate pieces over a head (`heads.Head`), fitted tightly to the cranium so a character wears them without
bloating the head silhouette. Features on `heads.Head` that an enclosed or cheek-guarded helm covers are declared via
`piece.hides` so character assembly suppresses them automatically.

KayKit originals, worn exactly as KayKit modeled them and fitted from their KayKit head to the wearer's (`SOURCES`):
- `bear`: the barbarian's bear-head hood.
- `horned`: the skeleton warrior's spiked half-helm with wrapped horns.
- `wizard`: the mage's wide-brimmed pointed hat.
- `knight`: the knight's open-faced great helm.
- `hood`: the skeleton rogue's pointed cowl.

Procedural styles, for headgear KayKit does not have:
- `headdress`: war bonnet with brow band, horned skull, and feathered halo.
- `crusader`: solid-cut cylindrical greathelm with flat chamfered crown, recessed dark eye slits, breathers, and cross trim.
- `dark_knight`: angular keeled dread helm with V-slit visor, forehead gem, swept brow-temple horns, and cheek gash.
- `kettle`: snug kettle helm above the ears with flared sloped brim, spangen ribs, and buckled cheek straps.
- `coif`: tight chain-mail hood and camail with curved wire-ring relief, circlet, and optional nasal bar.

    python asset-studio/studio.py catalog helmets -o helmets.png --yaws 0,-40,-90,180
"""

import json
from functools import lru_cache
from pathlib import Path

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


def _over_crown(head, theta, phi0, phi1, out, n=14):
    """Points from `phi0` at angle `theta` up over the crown and down to `phi1` on the opposite side."""
    up = head.surface(np.full(n, theta), np.linspace(phi0, 0.0, n), out)
    down = head.surface(np.full(n - 1, theta + np.pi), np.linspace(0.0, phi1, n)[1:], out)
    return np.vstack([up, down])


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


# KayKit's own headgear (CC0), harvested into `meshes/` in the hat-prop frame by `harvest()`: prefab name ->
# (character model under `assets/`, prop node, the adventurer class head it is fitted from). The skeletons' helm
# and hood were modeled on skulls but fit the barbarian head more closely than any skull.
SOURCES = {
    "barbarian_hat": ("kaykit-adventurers/Barbarian.glb", "Barbarian_Hat", "barbarian"),
    "knight_helmet": ("kaykit-adventurers/Knight.glb", "Knight_Helmet", "knight"),
    "mage_hat": ("kaykit-adventurers/Mage.glb", "Mage_Hat", "mage"),
    "skeleton_warrior_helmet": ("kaykit-skeletons/Skeleton_Warrior.glb", "Skeleton_Warrior_Helmet", "barbarian"),
    "skeleton_rogue_hood": ("kaykit-skeletons/Skeleton_Rogue.glb", "Skeleton_Rogue_Hood", "barbarian"),
}
MESHES = Path(__file__).resolve().parent / "meshes"


def _reach(centre, directions, verts, faces, spread=10.0, rays=24):
    """Distance from `centre` to a head mesh along each unit direction, as the median over a cone of `rays` within
    `spread` degrees: the cranium's own shape, barely dented by ears, a nose or a brow ridge."""
    jitter = np.random.default_rng(0).normal(size=(rays, 3)) * np.radians(spread) / 2
    samples = directions[:, None, :] + jitter[None, :, :]
    samples /= np.linalg.norm(samples, axis=2, keepdims=True)
    distance = kit.ray_distance(centre, samples.reshape(-1, 3), verts, faces).reshape(len(directions), rays)
    return np.nanmedian(distance, axis=1)


def harvest():
    """Read each `SOURCES` prop from its KayKit character into the hat-prop frame and save it to
    `meshes/<name>.npz`, with how far its KayKit head reaches under each vertex (run once from `asset-studio/`:
    `python -c "from prefabs.outfits import helmets; helmets.harvest()"`; the result is committed). Swatch regions,
    gradient positions and authored normals are kept as KayKit drew them."""
    from core import hosts, mesh
    from prefabs import decor

    for name, (file, node, reference) in SOURCES.items():
        source = decor.ASSETS / file
        g, _, _ = mesh.read_glb(str(source))
        mesh_name = g["meshes"][next(n["mesh"] for n in g["nodes"] if n.get("name") == node)]["name"]
        piece, colors, _ = decor.material_piece(source, (mesh_name,))
        to_frame = hosts.hat_prop(source, node)
        verts = piece.verts @ to_frame[:3, :3].T + to_frame[:3, 3]
        normals = piece.normals @ np.linalg.inv(to_frame[:3, :3])
        normals /= np.linalg.norm(normals, axis=2, keepdims=True)
        head_verts, head_faces = hosts.class_head(reference)
        centre = heads.Head.fit(head_verts).centre
        offset = verts - centre
        reach = _reach(centre, offset / np.linalg.norm(offset, axis=1)[:, None], head_verts, head_faces)
        meta = {"name": name, "category": "headgear", "domain": "outfits", "source": file, "node": node,
                "license": "CC0 (KayKit)", "verts": len(verts), "tris": len(piece.faces),
                "span": [round(float(x), 4) for x in np.ptp(verts, axis=0)], "colors": colors,
                "head": {"class": reference, "centre": [round(float(x), 5) for x in centre]}}
        np.savez_compressed(MESHES / f"{name}.npz", verts=verts.astype(np.float32), faces=piece.faces.astype(np.int32),
                            slots=np.asarray(piece.slots, str), normals=normals.astype(np.float32),
                            uv_t=np.asarray(piece.uv_t, np.float32), reach=reach.astype(np.float32),
                            meta=json.dumps(meta))
        print(f"  [+] {name:24s} {len(piece.faces):4d} tris  {meta['span']}  <- {file}:{node}")


@lru_cache(maxsize=None)
def _stored(name):
    with np.load(MESHES / f"{name}.npz") as data:
        return {"verts": data["verts"].astype(float), "faces": data["faces"].astype(np.int64),
                "swatches": data["slots"].astype(object), "normals": data["normals"].astype(float),
                "uv_t": data["uv_t"].astype(float), "reach": data["reach"].astype(float),
                "meta": json.loads(str(data["meta"]))}


def _face_turns(before, after):
    """Per-face rotations turning each triangle's normal in `before` to its normal in `after` (n, 3, 3)."""
    def unit_normals(tri):
        n = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
        return n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)

    a, b = unit_normals(before), unit_normals(after)
    v, c = np.cross(a, b), np.sum(a * b, axis=1)
    skew = np.zeros((len(a), 3, 3))
    skew[:, 0, 1], skew[:, 0, 2], skew[:, 1, 2] = -v[:, 2], v[:, 1], -v[:, 0]
    skew -= skew.transpose(0, 2, 1)
    return np.eye(3) + skew + skew @ skew / np.maximum(1.0 + c, 1e-6)[:, None, None]


def _original(prefab, regions, hides, summary):
    """A style that wears KayKit's `prefab` exactly as authored, refitted from the KayKit head it sits on to the
    wearer's: along each direction from the head's centre, distances scale from where the KayKit head's surface
    lies to where the wearer's cranium does, so the clearance KayKit modeled carries over. `regions` is
    `{keyword: (KayKit swatch, default slot)}`: a recipe recolors a region by naming another slot, such as
    `slot="hat"`; any other keyword is an error, so a recipe never asks for detail the original lacks."""

    def style(head, tilt=0.0, y_offset=0.0, **slots):
        unknown = sorted(set(slots) - set(regions))
        if unknown:
            raise TypeError(f"{prefab} has no {unknown}; its regions are {sorted(regions)}")
        art = _stored(prefab)
        offset = art["verts"] - art["meta"]["head"]["centre"]
        direction = offset / np.linalg.norm(offset, axis=1)[:, None]
        verts = head.centre + offset * (head.radius(direction) / art["reach"])[:, None]
        faces = art["faces"]
        # Corner normals turn with their face, so KayKit's smooth and hard edges survive the refit.
        normals = np.einsum("fij,fcj->fci", _face_turns(art["verts"][faces], verts[faces]), art["normals"])
        names = {swatch: slots.get(keyword, default) for keyword, (swatch, default) in regions.items()}
        piece = kit.Piece(verts, faces, np.array([names[s] for s in art["swatches"]], object), normals=normals,
                          uv_t=art["uv_t"])
        return _with_hides(_apply_tilt_y(piece, head, tilt=tilt, y_offset=y_offset), *hides)

    style.__doc__ = f"{summary} (KayKit `{SOURCES[prefab][1]}`); regions: {', '.join(sorted(regions))}."
    style.prefab = prefab
    return style


# Characters merge this palette over every other module's, so it adds helmet-only slots and never re-colors shared
# ones (`armor`, `mail`, `rivet`, `gem`, `gold`, `stitch`, `metal_*` come from the armor and demo palettes).
COLORS = {"pelt": (112, 96, 88), "pelt_dark": (86, 72, 66), "dark": (28, 24, 26), "tooth": (240, 240, 236),
          "metal": (170, 178, 190), "leather": (122, 78, 52), "bone": (232, 222, 196), "feather": (236, 232, 220),
          "feather_tip": (60, 44, 40), "hat": (72, 68, 113), "hat_band": (180, 88, 41), "horn": (224, 189, 155),
          "horn_wrap": (108, 35, 60), "trim": (190, 196, 210), "mail_dark": (92, 98, 108), "cloak": (62, 82, 66),
          "cloak_dark": (42, 56, 46)}

STYLES = {
    "bear": _original("barbarian_hat", {"slot": ("kay_barbarian_07", "pelt"), "dark": ("kay_barbarian_02", "dark"),
                                        "tooth": ("kay_barbarian_12", "tooth")}, ("hair",),
                      "The barbarian's bear-head hood with ears, snout and fangs"),
    "horned": _original("skeleton_warrior_helmet",
                        {"slot": ("kay_skeleton_03", "metal"), "dark": ("kay_skeleton_04", "metal_dark"),
                         "horn": ("kay_skeleton_11", "horn"), "wrap": ("kay_skeleton_22", "horn_wrap"),
                         "band": ("kay_skeleton_26", "leather")}, ("ears", "hair"),
                        "A spiked, riveted half-helm with wrapped horns and cheek guards"),
    "headdress": headdress,
    "wizard": _original("mage_hat", {"slot": ("kay_mage_11", "hat"), "band": ("kay_mage_05", "hat_band"),
                                     "buckle": ("kay_mage_03", "metal")}, ("hair",),
                        "The mage's wide-brimmed pointed hat with a crooked tip and a buckled band"),
    "knight": _original("knight_helmet", {"slot": ("kay_knight_03", "armor"), "crest": ("kay_knight_07", "armor")},
                        ("ears", "hair"), "The knight's open-faced great helm with a visor and crest"),
    "crusader": crusader,
    "dark_knight": dark_knight,
    "kettle": kettle,
    "coif": coif,
    "hood": _original("skeleton_rogue_hood", {"slot": ("kay_skeleton_22", "cloak")}, ("ears", "hair"),
                      "A pointed, deep cowl"),
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
