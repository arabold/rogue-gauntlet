"""Facial and head detail prefabs — eyes, brows, noses, mouths, jaws, tusks, beards, ears, horns, hair, wraps, scars, and warpaint."""

import math
import numpy as np
from core import kit
from prefabs.anatomy.heads import COLORS, _bump, _spow, Head

# ---------------------------------------------------------------------------
# Flat shapes for the face
# ---------------------------------------------------------------------------
def blob(w, h, n=14, p=2.4):
    """A rounded-rectangle outline `w` x `h` around the origin (superellipse exponent `p`)."""
    t = 2 * np.pi * (np.arange(n) + 0.5) / n
    return np.column_stack([w / 2 * _spow(np.cos(t), 2 / p), h / 2 * _spow(np.sin(t), 2 / p)])


def clip(poly, nx, ny, c):
    """The convex polygon `poly` cut to the half plane `nx * x + ny * y <= c`."""
    out = []
    poly = np.asarray(poly, float)
    for i in range(len(poly)):
        a, b = poly[i], poly[(i + 1) % len(poly)]
        da, db = nx * a[0] + ny * a[1] - c, nx * b[0] + ny * b[1] - c
        if da <= 0:
            out.append(a)
        if da * db < 0:
            out.append(a + (b - a) * da / (da - db))
    return np.asarray(out)


def slab(poly, depth, slot, embed=0.015, bevel=0.004):
    """A flat face piece: `poly` standing `depth` over the skin, sunk `embed` into it so no gap shows."""
    p = kit.prism(poly, depth + embed, slot, bevel=bevel)
    return kit.Piece(p.verts - [0.0, 0.0, embed], p.faces, slot)


# ---------------------------------------------------------------------------
# Features
# ---------------------------------------------------------------------------
def bead_mesh(w, h, d=None, p=2.0, sides=16, rings=8, slot="eye", angry=0.0):
    """
    A 3D rounded bead centered at (0, 0, 0) with width `w`, height `h`, and depth `d`.
    Its equator (z = 0) divides the bead:
      z in [-d/2, 0]: back half, sunk into the head or socket
      z in [0, +d/2]: front half, protruding outward
    Normals are outward facing and smooth.
    """
    if d is None:
        d = 0.85 * min(w, h)
    u = 2 * np.pi * np.arange(sides) / sides
    v = np.linspace(-np.pi / 2, np.pi / 2, rings + 1)

    verts = []
    verts.append([0.0, 0.0, -d / 2])

    sp = lambda val, exp: np.sign(val) * (np.abs(val) ** exp)

    for vi in v[1:-1]:
        cv = np.cos(vi)
        sv = np.sin(vi)
        r_scale = sp(cv, 2.0 / p)
        xs = (w / 2) * r_scale * sp(np.cos(u), 2.0 / p)
        ys = (h / 2) * r_scale * sp(np.sin(u), 2.0 / p)
        zs = np.full(sides, (d / 2) * sv)
        for x, y, z in zip(xs, ys, zs):
            verts.append([x, y, z])

    verts.append([0.0, 0.0, d / 2])
    verts = np.asarray(verts, float)

    if angry and abs(w - h) > 1e-4:
        ang = np.radians(-angry * 15.0)
        R = np.array([[np.cos(ang), -np.sin(ang), 0.0],
                      [np.sin(ang),  np.cos(ang), 0.0],
                      [0.0,          0.0,         1.0]])
        verts = verts @ R.T

    faces = []
    for i in range(sides):
        nxt = (i + 1) % sides
        faces.append([0, 1 + nxt, 1 + i])

    for r in range(rings - 2):
        r1 = 1 + r * sides
        r2 = 1 + (r + 1) * sides
        for i in range(sides):
            nxt = (i + 1) % sides
            faces.append([r1 + i, r1 + nxt, r2 + nxt])
            faces.append([r1 + i, r2 + nxt, r2 + i])

    top_pole = len(verts) - 1
    last_r = 1 + (rings - 2) * sides
    for i in range(sides):
        nxt = (i + 1) % sides
        faces.append([last_r + i, last_r + nxt, top_pole])

    faces = np.asarray(faces, int)
    return kit.Piece(verts, faces, slot)


def eyes(head, style="dot", x=0.42, y=-0.2, w=0.2, h=0.27, angry=0.0, pupil=False, glint=False, outline=0.32,
         socket=0.0, liner=0.0, flick=0.0, lashes=0, halo=0.0, uneven=1.0, cut="round",
         carve_socket=False, socket_depth=None, socket_slot="socket", depth=None, out=None,
         slot=None, glow=False, ring=0.0, rings=None, makeup=None, ring_slot="ring",
         outline_slot="eye", iris_slot="iris", outer=False, outer_shape=False, bead_scale=None,
         gap=0.0, liner_slot="liner"):
    """
    Both eyes. `bead`: small solid 3D rounded bead catching specular light reflections (sunk halfway).
    `dot`: stylized's dark rounded pills. `glow`: a colored iris (slot `iris`) in a dark rim (slot `eye`,
    `outline` its margin as a share of the iris) with an optional `pupil` and an optional `glint`.
    `angry` slants the top edge down toward the nose (0 .. 1).

    Around them: `socket` > 0 sets the eye in a shadowed patch (slot `socket`) that much bigger;
    `carve_socket` carves the eye socket as a 3D recess in the cranium (for ghoul and zombie);
    `ring` / `makeup` / `rings` draws rings or makeup patches of customizable size and slot around the eyes;
    `lashes` adds stylized female winged cat-eye lash on the upper-outer corner;
    `liner` draws a heavy upper lid (slot `liner`, its thickness a share of the eye's height) that
    sweeps past the outer corner by `flick` (a share of its width); `halo` rings
    a glowing eye in a lighter glow (slot `halo`) that much bigger; `uneven` scales the right eye (a zombie's
    mismatched stare). `cut="triangle"` gives a devilish eye: the slanted top meets a straight edge rising from the
    bottom in a sharp inner corner (an imp's); `pupil="slit"` a cat's narrow upright pupil.
    """
    A = head.A
    W, H = w * A, h * A
    x0, y0 = x * A, head.y(y)
    bead_slot = slot or ("glow" if (glow or style == "glow_bead") else "eye")

    def top_line(margin):  # the slanted lid of an angry eye: y <= c + slope * x
        return angry * H / W, 0.55 * H - 0.5 * angry * H + margin

    def shape(k, margin=0.0):
        poly = blob(W * k, H * k)
        if angry:  # the eye at +x has its inner corner toward -x
            slope, c = top_line(margin)
            poly = clip(poly, -slope, 1.0, c)
            if cut == "triangle":  # a straight lower-inner edge from the inner corner down to the bottom
                x0_, y0_ = -0.5 * W * k, c - slope * 0.5 * W * k
                s2 = (-0.5 * H * k - y0_) / (0.65 * W * k)
                poly = clip(poly, s2, -1.0, -y0_ - s2 * (-x0_) + 0.0)
        return poly

    has_outer = bool(pupil or cut == "triangle" or outer or outer_shape)
    rim = 1.0 + (outline if (outline is not None and (has_outer or style not in ("dot", "bead", "glow_bead"))) else 0.0)
    skin_cuts = []  # (slot, polygon) in eye coordinates, cut into the face around it: never patches laid on it
    carve_list = []

    if carve_socket:
        sw = W * (1.0 + (socket if socket > 0 else 0.55))
        sh = H * (1.0 + 1.1 * (socket if socket > 0 else 0.55))
        sox = 0.02 * W
        soy = -0.12 * H
        sp_poly = blob(sw, sh) + [sox, soy]
        poly_left = sp_poly + [x0, y0]
        poly_right = ((blob(sw * uneven, sh * uneven) + [sox * uneven, soy * uneven]) * [-1, 1] + [-x0, y0])[::-1]
        c_depth = (socket_depth if socket_depth is not None else 0.055) * A
        carve_list.append((socket_slot, [poly_left], poly_left, c_depth))
        carve_list.append((socket_slot, [poly_right], poly_right, c_depth))
        bead_out = out if out is not None else (-c_depth * 0.55)
    else:
        if socket:
            skin_cuts.append((socket_slot, blob(W * (1 + socket), H * (1 + 1.1 * socket)) + [0.04 * W, -0.22 * socket * H]))
        bead_out = out if out is not None else 0.0

    # Collect rings / makeup specifications around the eyes
    ring_entries = []
    if makeup is not None and not ring and not rings:
        ring = makeup

    if rings:
        if isinstance(rings, (list, tuple)):
            for r in rings:
                if isinstance(r, dict):
                    ring_entries.append((r.get("slot", ring_slot), float(r.get("scale", r.get("ring", 1.0))),
                                         float(r.get("x", 0.04 * W)), float(r.get("y", -0.15 * H))))
                elif isinstance(r, (list, tuple)):
                    sl_ = r[0] if isinstance(r[0], str) else ring_slot
                    sc_ = float(r[1] if isinstance(r[0], str) else r[0])
                    ox_ = float(r[2]) if len(r) > 2 else 0.04 * W
                    oy_ = float(r[3]) if len(r) > 3 else -0.15 * sc_ * H
                    ring_entries.append((sl_, sc_, ox_, oy_))
                else:
                    sc_ = float(r)
                    ring_entries.append((ring_slot, sc_, 0.04 * W, -0.15 * sc_ * H))
    elif ring:
        if isinstance(ring, dict):
            ring_entries.append((ring.get("slot", ring_slot), float(ring.get("scale", 1.0)),
                                 float(ring.get("x", 0.04 * W)), float(ring.get("y", -0.15 * H))))
        else:
            sc_ = float(ring)
            ring_entries.append((ring_slot, sc_, 0.04 * W, -0.15 * sc_ * H))

    for r_slot, r_scale, rox, roy in ring_entries:
        rw = W * (1.0 + r_scale)
        rh = H * (1.0 + 1.1 * r_scale)
        skin_cuts.append((r_slot, blob(rw, rh) + [rox, roy]))

    if halo:
        skin_cuts.append(("halo", shape(rim + halo, (rim + halo - 1) * 0.5 * H)))

    if style in ("bead", "glow_bead", "glow"):
        if has_outer:
            # Outer eye shape retained by color on the head mesh
            if outline > 0:
                skin_cuts.append((outline_slot or "eye", shape(rim)))
            if pupil or cut == "triangle":
                skin_cuts.append((iris_slot or "iris", shape(1.0)))

            if bead_scale is not None:
                if isinstance(bead_scale, (tuple, list)):
                    bw, bh = W * bead_scale[0], H * bead_scale[1]
                else:
                    bw, bh = W * bead_scale, H * bead_scale
            elif pupil == "slit":
                bw, bh = W * 0.35, H * 0.65
            elif pupil:
                bw, bh = W * 0.55, H * 0.60
            else:
                bw, bh = W * 0.50, H * 0.50
        else:
            bw, bh = W, H

        ball = bead_mesh(bw, bh, d=depth * A if depth is not None else None, angry=angry * 0.5 if has_outer else angry, slot=bead_slot)
        if lashes and not liner:
            rx, ry = 0.5 * W, 0.5 * H
            l_sc = float(lashes) if not isinstance(lashes, bool) else 1.0
            wing_pts = np.array([
                [0.2 * rx, 0.4 * ry],
                [0.2 * rx, 1.0 * ry],
                [1.25 * rx * l_sc, 0.98 * ry],
                [1.48 * rx * l_sc, 0.76 * ry],
                [1.0 * rx, 0.0 * ry],
                [0.2 * rx, 0.0 * ry],
            ])
            if angry:
                ang = np.radians(-angry * 15.0)
                R = np.array([[np.cos(ang), -np.sin(ang)], [np.sin(ang), np.cos(ang)]])
                wing_pts = wing_pts @ R.T
            wing_slab = slab(wing_pts, depth=0.012 * A, slot=bead_slot, embed=0.015 * A, bevel=0.002)
            ball = kit.Piece.merge(ball, wing_slab)
            skin_cuts.append((bead_slot, wing_pts))
        cuts = []  # specular light reflections come from PBR roughness, no white paint cuts
    elif style == "dot":
        ball = slab(shape(1.0), 0.022, "eye")
        cuts = [("glint", blob(0.3 * W, 0.3 * W) + [0.18 * W, 0.18 * H], {"eye"})] if glint else []
    else:
        m = outline * H
        ball = slab(shape(rim, m), 0.026, "eye")
        cuts = [("iris", shape(1.0), {"eye"})]
        if pupil == "slit":
            cuts.append(("eye", blob(0.14 * W, 0.62 * H) + [0.04 * W, -0.04 * H], {"iris"}))
        elif pupil:
            cuts.append(("eye", clip(blob(0.38 * W, 0.62 * H), -angry * H / W, 1.0, 0.3 * H), {"iris"}))
        if glint:
            cuts.append(("glint", blob(0.26 * W, 0.26 * W) + [0.2 * W, 0.08 * H], {"iris", "eye"}))

    for sl, poly, on in cuts:
        ball = kit.paint_cut(ball, [poly], sl, on)

    if liner or flick:
        l_slot = liner_slot or "liner"
        r_x = 0.5 * W * rim
        r_y = 0.5 * H * rim
        T = 0.055 * A * (liner / 0.5 if liner else 1.0)
        wing_origin = np.array([r_x * 0.95, (r_y + gap * A) * 0.45])
        flick_len = (flick if flick else 0.5) * W * 1.15
        flick_dir = np.array([np.cos(0.52), np.sin(0.52)])  # ~30 deg up-and-out

        # 1. Cat-eye upper lid crescent with optional visual skin gap above bead
        if liner:
            t_lid = np.linspace(np.pi * 0.95, np.pi * 0.05, 11)
            gap_h = gap * A * np.sin(t_lid)
            lid_in = np.column_stack([r_x * np.cos(t_lid), r_y * np.sin(t_lid) + gap_h])
            t_w = np.sin(np.linspace(0.1, np.pi - 0.1, 11)) ** 0.8
            lid_out = np.column_stack([r_x * np.cos(t_lid), r_y * np.sin(t_lid) + gap_h + T * t_w])
            for k in range(len(lid_in) - 1):
                skin_cuts.append((l_slot, np.array([lid_in[k], lid_in[k + 1], lid_out[k + 1], lid_out[k]])))

        # 2. Bold Winged Flick sweeping upward-outward toward the temple
        if flick:
            f_slot = l_slot if liner else (slot or "eye")
            wing_tip = wing_origin + flick_len * flick_dir
            wing_top = wing_origin + np.array([-0.012 * A, 0.65 * T])
            skin_cuts.append((f_slot, np.array([wing_origin, wing_top, wing_tip])))

        # 3. Flared distinct lashes fanning from the outer upper lid and wing
        if lashes and liner:
            lash_specs = [
                (np.array([r_x * 0.68, (r_y + gap * A) * 0.76]), 0.82, 0.95 * flick_len, 0.22 * T),
                (wing_origin + 0.35 * flick_len * flick_dir, 0.62, 1.05 * flick_len, 0.25 * T),
                (wing_origin + 0.85 * flick_len * flick_dir, 0.40, 0.75 * flick_len, 0.20 * T)
            ]
            for b_pt, ang, l_len, l_w in lash_specs[:lashes]:
                d = np.array([np.cos(ang), np.sin(ang)])
                n = np.array([-d[1], d[0]]) * l_w
                skin_cuts.append((l_slot, np.array([b_pt - n, b_pt + n, b_pt + l_len * d])))

    one = head.conform(ball, x0, y0, out=bead_out)
    other = one.mirrored()
    if uneven != 1.0:  # one eye bigger than the other (a zombie's), grown about its own centre across the face
        c = other.verts.mean(0)
        other = kit.Piece(c + (other.verts - c) * [uneven, uneven, 1.0], other.faces, other.slots)
    both = kit.Piece.merge(one, other)
    paint_ = []
    for sl, poly in skin_cuts:  # around the left eye, and mirrored round the right one
        q = np.asarray(poly, float)
        paint_.append((sl, q + [x0, y0]))
        paint_.append((sl, (q * [-uneven, uneven] + [-x0, y0])[::-1]))
    both.paint = paint_
    both.carve = carve_list
    return both


def brows(head, style="bar", x=0.44, y=0.06, length=0.5, thickness=0.11, angle=0.0, depth=0.04, slot="brow",
          taper=0.0, arch=0.12):
    """
    Both brows over the eyes, the inner end `angle` degrees lower (angry) or higher (worried). `bar`: stylized's
    bevelled blocks (`taper` < 1 thins the outer end); `bushy`: a bar with a ragged top of tufts. `arc`: a brush stroke arching by `arch` (a share of its
    length), thick at the inner end and tapering outward.
    """
    A = head.A
    L, T = length * A, thickness * A
    a = np.radians(angle)
    rot = np.array([[np.cos(a), np.sin(a)], [-np.sin(a), np.cos(a)]])
    if style == "bushy":  # a soft bar with a ragged top edge of tufts, thickest at the inner end (stylized's orcs)
        u = np.linspace(-0.5, 0.5, 11)
        bend = arch * L * (1 - (2 * u) ** 2)  # arching up in the middle
        top = T * (0.5 - 0.22 * (u + 0.5)) + 0.22 * T * (np.arange(11) % 2) + bend  # tufts every other point
        bottom = -T * (0.42 - 0.12 * (u + 0.5)) + bend
        poly = np.vstack([np.column_stack([u * L, bottom]), np.column_stack([u[::-1] * L, top[::-1]])]) @ rot
        piece = kit.extrude(poly, depth, slot)
        lift = np.clip(piece.verts[:, 2] / max(depth, 1e-9), 0, 1)  # rounded: the front face shrinks in a little
        c = poly.mean(0)
        piece = kit.Piece(np.column_stack([c + (piece.verts[:, :2] - c) * (1 - 0.1 * lift[:, None]),
                                           piece.verts[:, 2]]), piece.faces, piece.slots)
    elif style == "arc":
        u = np.linspace(-0.5, 0.5, 9)
        path = np.column_stack([u * L, arch * L * (1 - (2 * u) ** 2)]) @ rot
        piece = kit.ribbon(path, T * (1 - taper * (u + 0.5)), depth, slot, embed=0.015)
    else:
        poly = blob(L, T, n=18, p=3.2)  # a soft bar, its ends rounded like stylized's, thinning outward by `taper`
        poly[:, 1] *= 1 - taper * (poly[:, 0] / L + 0.5)
        piece = slab(poly @ rot, depth, slot, bevel=0.45 * depth)
    one = head.conform(piece, x * A, head.y(y))
    return kit.Piece.merge(one, one.mirrored())


def nose(head, style="button", y=-0.38, size=1.0, slot="skin", wart=False, nostril="nostril", embed=0.12,
         width=1.0, length=1.0, height=None, depth=1.0, projection=None, w=None, h=None, d=None):
    """`button` (a small rounded knob), `chevron` (an orc's: a short rounded bridge swelling out of the face to a
    broad V-shaped tip), `teardrop` (stylized's knight: a narrow bridge swelling to a round tip), `flat`
    (stylized's barbarian: a broad rounded block), `wedge` (a ridge from the brow to the tip), `wide` (a broad
    flat snout with dark nostrils), `snub` (two nostril dents, nothing raised: for undead and ghosts), `prominent`
    (a high bridge with defined tip overhanging a mustache/beard), or `hooked` (a goblin's or a witch's).
    Supports independent scaling: `width` (or `w`), `length` / `height` (or `h`), and `depth` / `projection` (or `d`)."""
    sx = float(w if w is not None else width)
    sy = float(h if h is not None else (height if height is not None else length))
    sz = float(d if d is not None else (projection if projection is not None else depth))
    s = size * head.A
    if style == "hooked":  # too long to bend onto the face: lofted from its profile, set rigidly on it, pointing ahead
        # the side profile (y, z) from the root inside the face toward the tip, its top and bottom lines, with the
        # half widths of the bridge on top and of the nostril wings below
        top = [(0.14, -0.12), (0.1, 0.12), (0.04, 0.3), (-0.08, 0.46), (-0.26, 0.6), (-0.44, 0.66)]
        bottom = [(-0.42, -0.12), (-0.46, 0.1), (-0.5, 0.28), (-0.54, 0.42), (-0.58, 0.52), (-0.62, 0.58)]
        bridge = [0.07, 0.07, 0.075, 0.085, 0.1, 0.09]
        wings = [0.2, 0.17, 0.12, 0.11, 0.11, 0.08]
        rings = []
        for (yt, zt), (yb, zb), wt, wb in zip(top, bottom, bridge, wings):
            T, B = np.array([yt, zt]), np.array([yb, zb])
            hi, lo = T + 0.2 * (B - T), T + 0.75 * (B - T)
            rings.append(np.array([(0.0, *T), (wt, *hi), (wb, *lo), (0.0, *B), (-wb, *lo), (-wt, *hi)]) * s)
        verts, faces = kit._loft(rings)
        k = len(rings) * 6
        verts, faces = kit._cap_loops(verts, faces, [list(range(6)), list(range(k - 6, k))],
                                      centers=[rings[0].mean(0), np.array([0.0, -0.6, 0.66]) * s])  # the blunt hooked tip
        parts = [kit.Piece(verts, kit._orient_closed(verts, faces), slot)]
        if wart:
            parts.append(kit.lump((0.08 * s, -0.08 * s, 0.4 * s), (0.04 * s, 0.04 * s, 0.04 * s), "wart", n=6,
                                  lat=(-40, 20)))
        merged = kit.Piece.merge(*parts)
        if sx != 1.0 or sy != 1.0 or sz != 1.0:
            merged = kit.Piece(merged.verts * [sx, sy, sz], merged.faces, merged.slots)
        (P,), _ = head.surface(*head.front_angles(0.0, head.y(y)), normals=True)
        return merged.moved(kit.Frame.make(P, (0.0, 0.0, 1.0)))
    elif style == "teardrop":  # stylized's knight: a narrow bridge swelling to a round tip
        bridge = [(dx * 0.05 * s, 0.3 * s, 0.025 * s) for dx in (-1, 1)] + [(0, 0.32 * s, 0.0)]
        bulb = kit.lump((0, -0.08 * s, 0.12 * s), (0.13 * s, 0.11 * s, 0.11 * s), slot, n=12,
                        lat=(-70, -40, -12, 15, 45, 70))
        base = [(dx * 0.12 * s, -0.12 * s, -0.02) for dx in (-1, 1)] + [(0, 0.2 * s, -0.02)]
        piece = kit.hull(np.vstack([bridge, bulb.verts, base]), slot)
    elif style == "flat":  # stylized's barbarian: a broad rounded block of a nose
        piece = kit.rounded_box((0.5 * s, 0.32 * s, 0.3 * s), slot, center=(0, 0.04 * s, 0.1 * s), roundness=0.5)
    elif style == "slender":  # elegant, feminine, softly sloping bridge to smooth rounded tip & subtle wings
        k = np.linspace(-1, 1, 17)
        pts = []
        cross_sections = [
            ( 0.18, 0.040, 0.020), # root between eyes (blends flush out of forehead)
            ( 0.08, 0.050, 0.060), # upper bridge
            ( 0.00, 0.060, 0.090), # mid bridge
            (-0.06, 0.075, 0.115), # supratip
            (-0.11, 0.085, 0.125), # soft rounded tip
            (-0.15, 0.110, 0.085), # integrated nostril wings
            (-0.19, 0.045, 0.025), # columella base tucking back into lip
        ]
        for y_, a_, d_ in cross_sections:
            pts += [(a_ * s * kk, y_ * s, d_ * s * (1 - np.abs(kk) ** 2.2) ** 0.65) for kk in k]
        for y_, a_, _ in cross_sections:
            for kk in (-1, 0, 1):
                pts.append((a_ * s * kk, y_ * s, -embed * s))
        piece = kit.hull(np.asarray(pts), slot)
    elif style in ("button", "stubby", "bulb"):  # cute, soft, compact button/bulb nose (never a pipe or tube)
        k = np.linspace(-1, 1, 19)
        pts = []
        cross_sections = [
            ( 0.14, 0.045, 0.005), # top skirt flaring flush into forehead
            ( 0.10, 0.065, 0.035),
            ( 0.05, 0.095, 0.080), # upper swell
            ( 0.00, 0.115, 0.115), # mid bulb
            (-0.06, 0.125, 0.135), # rounded tip projection
            (-0.11, 0.115, 0.110), # lower bulb
            (-0.16, 0.085, 0.055), # underside tucking inward
            (-0.21, 0.050, 0.015), # bottom skirt flaring into philtrum
            (-0.24, 0.025, 0.002), # base flush at philtrum
        ]
        for y_, a_, d_ in cross_sections:
            profile = (np.maximum(0.0, 1.0 - np.abs(k) ** 2.0)) ** 1.6
            for kk, prof in zip(k, profile):
                pts.append((a_ * s * kk, y_ * s, d_ * s * prof))
        for y_, a_, _ in cross_sections:
            for sgn in (-1, 1):
                pts.append((sgn * a_ * s * 1.15, y_ * s, 0.001 * s))
        for y_, a_, _ in cross_sections:
            for kk in (-1, 0, 1):
                pts.append((a_ * s * kk, y_ * s, -embed * s))
        piece = kit.hull(np.asarray(pts), slot)
    elif style == "chevron":  # an orc's: a short bridge swelling out of the face to a broad V-shaped tip (flat boxer snout)
        k = np.linspace(-1, 1, 15)
        pts = []
        for y_, a_, d_ in ((0.22, 0.06, 0.03), (0.08, 0.1, 0.1), (-0.06, 0.17, 0.2), (-0.14, 0.2, 0.25),
                           (-0.2, 0.13, 0.21), (-0.25, 0.04, 0.12)):
            pts += [(a_ * s * kk, y_ * s, d_ * s * (1 - np.abs(kk) ** 2.2) ** 0.7 - 0.02) for kk in k]
        for y_, a_, _ in ((0.22, 0.06, 0.03), (0.08, 0.1, 0.1), (-0.06, 0.17, 0.2), (-0.14, 0.2, 0.25),
                           (-0.2, 0.13, 0.21), (-0.25, 0.04, 0.12)):
            for kk in (-1, 0, 1):
                pts.append((a_ * s * kk, y_ * s, -embed * s))
        piece = kit.hull(np.asarray(pts), slot)
    elif style == "prominent":  # a high, prominent bridge with defined tip overhanging the mustache (for dark wizard)
        k = np.linspace(-1, 1, 17)
        pts = []
        cross_sections = [
            ( 0.25, 0.050, 0.005), # top blend
            ( 0.20, 0.075, 0.040),
            ( 0.08, 0.110, 0.110),
            (-0.06, 0.180, 0.210),
            (-0.14, 0.210, 0.260), # max tip projection
            (-0.20, 0.140, 0.220),
            (-0.25, 0.050, 0.120),
            (-0.28, 0.030, 0.010), # bottom blend
        ]
        for y_, a_, d_ in cross_sections:
            profile = (np.maximum(0.0, 1.0 - np.abs(k) ** 2.0)) ** 1.5
            for kk, prof in zip(k, profile):
                pts.append((a_ * s * kk, y_ * s, d_ * s * prof))
        for y_, a_, _ in cross_sections:
            for sgn in (-1, 1):
                pts.append((sgn * a_ * s * 1.12, y_ * s, 0.001 * s))
        for y_, a_, _ in cross_sections:
            for kk in (-1, 0, 1):
                pts.append((a_ * s * kk, y_ * s, -embed * s))
        piece = kit.hull(np.asarray(pts), slot)
    elif style == "broad":  # broad rounded nose with wide integrated nostrils (never separate spheres)
        k = np.linspace(-1, 1, 17)
        pts = []
        cross_sections = [
            ( 0.16, 0.080, 0.030), # broad root
            ( 0.06, 0.100, 0.075), # bridge
            (-0.04, 0.135, 0.120),
            (-0.10, 0.160, 0.140), # broad tip
            (-0.15, 0.195, 0.095), # wide integrated nostril wings
            (-0.20, 0.080, 0.030), # base
        ]
        for y_, a_, d_ in cross_sections:
            pts += [(a_ * s * kk, y_ * s, d_ * s * (1 - np.abs(kk) ** 2.2) ** 0.7) for kk in k]
        for y_, a_, _ in cross_sections:
            for kk in (-1, 0, 1):
                pts.append((a_ * s * kk, y_ * s, -embed * s))
        piece = kit.hull(np.asarray(pts), slot)
    elif style == "wedge":  # rounded ridge from brow to tip (stylized male humanoid)
        k = np.linspace(-1, 1, 17)
        pts = []
        cross_sections = [
            ( 0.22, 0.040, 0.025),
            ( 0.10, 0.060, 0.070),
            ( 0.00, 0.075, 0.110),
            (-0.10, 0.095, 0.145),
            (-0.18, 0.120, 0.100),
            (-0.22, 0.050, 0.025),
        ]
        for y_, a_, d_ in cross_sections:
            pts += [(a_ * s * kk, y_ * s, d_ * s * (1 - np.abs(kk) ** 2.2) ** 0.7) for kk in k]
        for y_, a_, _ in cross_sections:
            for kk in (-1, 0, 1):
                pts.append((a_ * s * kk, y_ * s, -embed * s))
        piece = kit.hull(np.asarray(pts), slot)
    elif style == "wide":
        bridge = kit.hull([(-0.17 * s, -0.1 * s, -embed * s), (0.17 * s, -0.1 * s, -embed * s), (0.0, 0.16 * s, -embed * s),
                           (-0.13 * s, -0.11 * s, 0.11 * s), (0.13 * s, -0.11 * s, 0.11 * s),
                           (-0.07 * s, 0.06 * s, 0.07 * s), (0.07 * s, 0.06 * s, 0.07 * s)], slot)
        piece = bridge
        for dx in (-1, 1):  # nostrils dented into the nose's lower front, not plates on it
            hole = blob(0.07 * s, 0.045 * s) + [dx * 0.07 * s, -0.085 * s]
            piece = kit.carve(piece, [hole], hole, nostril, 0.03 * s, piece.verts.mean(0) + [0, 0.05 * s, 0], {slot})
    elif style == "snub":  # two slits dented into the face itself
        empty = kit.Piece.merge()
        empty.carve = []
        for dx in (-1, 1):
            a_ = np.radians(-dx * 25)
            R = np.array([[np.cos(a_), -np.sin(a_)], [np.sin(a_), np.cos(a_)]])
            hole = blob(0.06 * s * sx, 0.09 * s * sy) @ R.T + [dx * 0.05 * s * sx, head.y(y)]
            empty.carve.append(("dark", [hole], hole, 0.03 * s * sz))
        return empty
    else:
        return nose(head, style="button", y=y, size=size, slot=slot, wart=wart, nostril=nostril, embed=embed,
                    width=sx, length=sy, depth=sz)
    if sx != 1.0 or sy != 1.0 or sz != 1.0:
        piece = kit.Piece(piece.verts * [sx, sy, sz], piece.faces, piece.slots)
    return head.conform(piece, 0.0, head.y(y))


def _two_tone(poly, root_y, toward, split, depth, slot, tip, bevel=0.003):
    """A flat tooth (`poly`, convex) as two prisms side by side: from its root at `root_y` to `split` of the way to
    its point (`toward` +1 up, -1 down) in `slot`, the rest in `tip` (stylized's yellowed teeth with white points)."""
    P = np.asarray(poly, float)
    reach = np.max(toward * (P[:, 1] - root_y))
    cut = root_y + toward * split * reach
    near = clip(P, 0.0, toward, toward * cut)          # toward * y <= toward * cut: the root part
    far = clip(P, 0.0, -toward, -toward * cut)          # the rest: the point
    parts = [kit.prism(q, depth, sl, bevel=bevel) for q, sl in ((near, slot), (far, tip or slot)) if len(q) >= 3]
    return kit.Piece.merge(*parts)


def mouth(head, y=-0.7, width=0.32, curve=0.0, thickness=0.035, teeth=(), tooth=0.07, slot="mouth", tooth_slot="tusk",
          tip="tusk_tip", split=0.45, style="line", height=0.25, upper=None, lower=None, lip="skin_dark",
          lip_width=0.05, depth=0.05, seed=0):
    """
    A mouth, its corners `curve` up (a smile, > 0) or down (a frown, < 0) as a share of its width.

    `line`: a closed mouth, a crease cut into the skin (`thickness` wide); `teeth` = `(x, length)` pairs along it
    (x a share of the half width, length of the head's half width): fangs over the upper lip (length > 0) or up
    from the lower one (< 0), `tooth` wide.

    `open`: a sculpted open mouth `height` tall at its middle (a share of the half width), tapering shut at the
    corners: an opening carved `depth` (a share of the half width) into the face, colored `slot`, its lips the
    skin round it recolored `lip`; upper and lower rows of teeth fitted inside it, following its curve: `upper` /
    `lower` = `dict(count, shape="row" | "fang" | "needle", length (a share of the opening), span (a share of the
    width they cover), crooked (0..1: uneven, tilted, a gap or two))`. A `row` is one band of teeth side by side,
    notched where they meet; a grin is a wide `curve`d mouth with a full `row` above.

    Every tooth is two-toned: `tooth_slot` at the root, `tip` past `split` of its length (None: one color). The
    returned piece is the teeth; the opening, lips and creases are outlines `build` cuts into the skin.
    """
    A = head.A
    W = width * A
    y0 = head.y(y)
    parts, paint, carve = [], [], []

    def to_face(poly):  # mouth coordinates -> the front view `build` cuts in
        return np.asarray(poly, float) + [0.0, y0]

    if style != "open":
        lip_y = lambda u: curve * W * (2 * u / W) ** 2  # noqa: E731
        u = np.linspace(-W / 2, W / 2, 9)
        half = 0.5 * thickness * A
        for a, b in zip(u[:-1], u[1:]):  # the crease, a strip of quads cut into the skin
            paint.append((slot, to_face([(a, lip_y(a) - half), (b, lip_y(b) - half), (b, lip_y(b) + half),
                                          (a, lip_y(a) + half)])))
        for x, length in teeth:
            ux, w, L = x * W / 2, tooth * A / 2, length * A
            root = lip_y(ux) - np.sign(L) * 0.4 * thickness * A
            parts.append(_two_tone([(ux - w, root), (ux + w, root), (ux, root - L)], root, -np.sign(L), split, 0.03,
                                   tooth_slot, tip, bevel=0.006))
    else:
        H = height * A
        top = lambda s_: curve * W * s_ ** 2  # noqa: E731
        bottom = lambda s_: top(s_) - H * np.clip(1 - s_ ** 2, 0, 1) ** 0.8  # noqa: E731
        s_ = np.linspace(-1, 1, 17)
        strips = []
        lw = lip_width * A
        for a, b in zip(s_[:-1], s_[1:]):  # the opening as convex quads, and a band of lip along each edge
            xa, xb = a * W / 2, b * W / 2
            strips.append(to_face([(xa, bottom(a)), (xb, bottom(b)), (xb, top(b)), (xa, top(a))]))
            for edge, sgn in ((top, 1), (bottom, -1)):
                ka, kb = lw * (1 - 0.6 * a ** 2), lw * (1 - 0.6 * b ** 2)
                paint.append((lip, to_face([(xa, edge(a)), (xb, edge(b)), (xb, edge(b) + sgn * kb),
                                            (xa, edge(a) + sgn * ka)])))
        outline = to_face([(x * W / 2, top(x)) for x in s_] + [(x * W / 2, bottom(x)) for x in s_[::-1][1:-1]])
        carve.append((slot, strips, outline, depth * A))
        rng = np.random.default_rng(seed)
        for spec, sgn in ((upper, -1), (lower, 1)):  # upper teeth hang from the top edge, lower ones stand up
            if not spec:
                continue
            n, shape = spec.get("count", 6), spec.get("shape", "row")
            crooked, length = spec.get("crooked", 0.0), spec.get("length", 0.5)
            span = spec.get("span", 0.9 if shape == "row" else 0.7)
            edges = np.linspace(-span, span, n + 1)
            for k in range(n):
                a, b = edges[k], edges[k + 1]
                if shape != "row":  # fangs and needles: narrower than their slot, never wider than a fang should be
                    c, half = 0.5 * (a + b), min(0.3 * (b - a), 0.14 * span)
                    a, b = c - half, c + half
                if crooked and rng.random() < 0.18 * crooked:
                    continue  # a lost tooth
                us = np.linspace(a, b, 4)
                edge = top if sgn < 0 else bottom
                gap = np.array([top(u_) - bottom(u_) for u_ in us])
                if gap.min() < 0.12 * H:
                    continue
                L = gap * length * (1 + crooked * rng.uniform(-0.35, 0.25))
                drop = L * (np.array([0.72, 1.0, 1.0, 0.72]) if shape == "row" else np.array([0.0, 0.6, 0.6, 0.0]))
                root = np.array([edge(u_) for u_ in us]) + sgn * 0.02 * gap  # just inside the lip
                pts = [(u_ * W / 2, r) for u_, r in zip(us, root)]
                pts += [(u_ * W / 2, r + sgn * d) for u_, r, d in zip(us[::-1], root[::-1], drop[::-1])]
                if shape != "row":
                    pts = pts[:4] + [(0.5 * (a + b) * W / 2,
                                      root.mean() + sgn * L.max() * (1.25 if shape == "needle" else 1.0))]
                if crooked:  # tilted a little about its root
                    t = np.radians(crooked * rng.uniform(-12, 12))
                    x0, yr = 0.5 * (a + b) * W / 2, root.mean()
                    R = np.array([[np.cos(t), -np.sin(t)], [np.sin(t), np.cos(t)]])
                    pts = [tuple(R @ (np.array(p_) - [x0, yr]) + [x0, yr]) for p_ in pts]
                parts.append(_two_tone(pts, root.mean(), sgn, split, 0.008, tooth_slot, tip, bevel=0.0))
    teeth_piece = head.conform(kit.Piece.merge(*parts), 0.0, y0) if parts else kit.Piece.merge()
    teeth_piece.paint, teeth_piece.carve = paint, carve
    return teeth_piece


def tusks(head, x=0.2, y=-0.68, length=0.3, radius=0.07, spread=-0.2, curve=0.35, broken=0.0, mirror=True,
          out=-0.01, slot="tusk", tip="tusk_tip", split=0.5, blend=True):
    """
    Tusks from the lower jaw (`out` over the skin: a jaw piece's ledge), curving up past the lip by `curve`, their
    tips leaning in toward the nose (`spread` < 0) so they frame the face rather than hide the eyes. A pair at
    +-`x`, or one at `x` (`mirror` false: an odd tooth); `broken` > 0 snaps that share off the tip; `tip` colors
    the part past `split` of its length in that slot (stylized's yellowed tusks with white points; blended smoothly
    when `blend` is true).
    """
    A = head.A
    (theta,), (phi,) = head.front_angles(x * A, head.y(y))
    (P,), (n,) = head.surface(theta, phi, out, normals=True)
    L = length * A
    up = np.array([0.0, 1.0, 0.0])
    side = np.array([np.sign(x) or 1.0, 0.0, 0.0])
    fwd = n * [0.2, 1.0, 1.0]  # out of the face, not sideways where the face turns
    fwd /= np.linalg.norm(fwd)
    path = kit.bezier(P, P + curve * L * fwd + 0.4 * L * up, P + curve * L * fwd + L * up + spread * L * side, 8)
    path = np.vstack([P - 0.35 * radius * A * fwd - 0.2 * radius * A * up, path])  # rooted inside the lip
    keep = int(round(9 * (1 - broken))) + 1
    t = np.linspace(0.0, 1.0, 10)[:keep]
    th = 2 * np.pi * np.arange(8) / 8
    one = kit.sweep(path[:keep], np.column_stack([radius * A * np.cos(th), 0.8 * radius * A * np.sin(th)]), slot,
                    scale=np.maximum(np.where(t < 0.15, 1.0, (1 - (t - 0.15) / 0.85) ** 0.7), 0.06))
    if tip and not blend:  # discrete two-toned: yellowed at the root, white toward the tip
        c = one.verts[one.faces].mean(1)
        along = np.linalg.norm(c - path[0], axis=1) / max(np.linalg.norm(path[keep - 1] - path[0]), 1e-9)
        one = kit.Piece(one.verts, one.faces, np.where(along > split, tip, slot).astype(object))
    return kit.Piece.merge(one, one.mirrored()) if mirror else one


def jaw(head, top=-0.48, droop=0.2, width=80.0, chin=0.35, jowl=0.2, lip=0.16, bottom=-0.98, rise=0.3, cols=23,
        rows=14, slot="skin", mouth="mouth", tusks=None, lip_slot="skin"):
    """
    A heavy lower jaw: a smooth patch of skin swelling out of the lower face and sinking back into it at its sides
    and bottom, so it reads as part of the head (stylized's skeletons keep the jaw a separate piece too). Its top
    edge, the mouth line, `droop`s toward the corners (a scowl; shares of the half height); its lower edge rises
    by `rise` toward the sides. `chin` juts out its middle, `jowl` swells it between chin and sides, `lip` rolls a
    rounded lower lip along the top edge (an underbite; shares of the half width), in `lip_slot`. `mouth` is the
    dark crease above the lip, cut flat into the face (the piece carries it as `paint` outlines for `build`);
    `tusks` (a dict or a list of `tusks` arguments: mix pairs, odd teeth and broken ones) stand on the lip.
    """
    A = head.A
    u = np.linspace(-1, 1, cols)
    th = FRONT + np.radians(width) * u
    t = np.linspace(0, 1, rows)[:, None]

    def edge_top(u):
        return top - droop * u ** 2

    low = np.minimum(bottom + rise * u ** 2, edge_top(u) - 0.1)  # its corners keep some height
    yn = edge_top(u) + (low - edge_top(u)) * t
    swell = np.sin(np.pi * np.clip(t, 0, 1) ** 1.1) ** 0.8  # rounded: nothing at the edges, full through the middle
    lipped = np.clip(1 - t / 0.25, 0, 1) ** 2  # the top edge stands proud too, so the lip sits on a shelf
    out = A * (swell * (chin * (1 - u ** 2) ** 2 + jowl * 4 * u ** 2 * (1 - u ** 2))
               + 0.6 * lip * (1 - u ** 2) ** 1.5 * lipped)
    phi = head.phi(yn)
    grid = np.broadcast_to(th, yn.shape)
    outer = head.surface(grid, phi, out).reshape(rows, cols, 3)
    inner = head.surface(grid, phi, -0.03).reshape(rows, cols, 3)
    parts = [kit.patch_shell(outer, inner, slot)]
    if lip:  # the lower lip: a rounded roll along the top edge, thick in the middle, thinning to the corners
        ul = np.linspace(-0.92, 0.92, 15)
        thl = FRONT + np.radians(width) * ul
        k = (1 - ul ** 2) ** 0.7
        path = head.surface(thl, head.phi(edge_top(ul) - 0.05), A * 0.6 * lip * (1 - ul ** 2) ** 1.5)
        parts.append(kit.sweep(path, kit.ring_profile(0.09 * A, 0.07 * A, 8), lip_slot, up=(0.0, 1.0, 0.0),
                               scale=np.maximum(k, 0.15)))
    piece = kit.Piece.merge(*parts)
    paint = []
    if mouth:  # the crease above the lip, cut flat into the face
        um = np.linspace(-0.85, 0.85, 9)
        line = head.surface(FRONT + np.radians(width) * um, head.phi(edge_top(um) + 0.035))
        path = (line[:, :2] - [head.cx, head.cy]) / [A, head.Hh]
        paint = FEATURES["paint"](head, [dict(path=path, width=(0.03, 0.06, 0.07, 0.06, 0.03), slot=mouth)])
    parts = [piece]
    for spec in ([] if not tusks else tusks if isinstance(tusks, list) else [tusks]):
        spec = dict(spec)
        x = spec.pop("x", 0.26)
        (theta,), _ = head.front_angles(x * A, head.y(top))
        ux = (theta - FRONT) / np.radians(width)
        parts.append(FEATURES["tusks"](head, x=x, y=edge_top(ux) - 0.04, out=0.6 * lip * (1 - ux ** 2) ** 1.5 * A,
                                       **spec))
    out_piece = kit.Piece.merge(*parts)
    out_piece.paint = paint
    return out_piece


def block_jaw(head, top=-0.36, bottom=-1.12, width=0.86, front=1.12, chin=0.66, back=0.05, slot="skin",
              shade="skin_dark", lip="lip", mouth="mouth", tusk="tusk", tusks=0.75, tusk_x=0.62, tusk_width=0.17,
              tooth=0.16):
    """
    An orc's underbite: a squared jaw block jutting out under the face, wider than the skull, from the mouth line
    (`top`) down past it (`bottom`; shares of the half height), `width` half wide and reaching `front` forward
    (shares of the half width), tapering to a `chin` wide; darker (`shade`) below its middle. A thick `lip` rolls
    along its top front edge, the mouth's crease above it is cut into the face, and two `tusks` (length, a share of
    the jaw's height) rise from its corners at +-`tusk_x`, with one small chipped `tooth` between them.
    """
    A = head.A
    # rows down the jaw: (height share, half width, reach forward, reach back; shares of the half width)
    rows = [(top, width * 0.92, front, back + 0.25), (top - 0.22, width, front * 1.01, back + 0.35),
            (top + 0.6 * (bottom - top), width * 0.96, front * 0.94, back + 0.4),
            (bottom, width * chin, front * 0.76, back + 0.15)]
    def ring(yn, hw, fz, bz, k=1.0):  # a squarish ring: flat front, rounded corners
        out = []
        for a in np.radians(np.arange(0, 360, 12)):
            c, sn = np.cos(a), np.sin(a)
            z = (fz if sn > 0 else bz) * np.sign(sn) * abs(sn) ** 0.55
            out.append((head.cx + k * hw * A * np.sign(c) * abs(c) ** 0.45, head.y(yn), head.cz0 + k * z * A))
        return out

    mid = top + 0.5 * (bottom - top)
    upper = kit.hull(np.asarray(ring(*rows[0]) + ring(*rows[1]) + ring(mid, *rows[2][1:])), slot)
    lower = kit.hull(np.asarray(ring(mid + 0.04, *rows[2][1:], k=0.985) + ring(*rows[3], k=0.985)
                                + ring(*rows[2], k=0.985)), shade)  # the darker lower jaw, a clean band below
    parts = [upper, lower]
    # the lower lip: a thick roll along the top front edge
    ys = head.y(top) - 0.02
    ul = np.linspace(-0.85, 0.85, 13)
    zf = head.cz0 + front * A * np.abs(np.sin(np.arccos(np.clip(ul, -1, 1)))) ** 0.55 - 0.03
    path = np.column_stack([head.cx + ul * width * 0.9 * A, np.full(len(ul), ys), zf])
    parts.append(kit.sweep(path, kit.ring_profile(0.13 * A, 0.1 * A, 10), lip, up=(0.0, 1.0, 0.0),
                           scale=np.maximum((1 - ul ** 2) ** 0.35, 0.3)))
    H = (top - bottom) * head.Hh
    for sgn in (-1, 1):  # tusks from the corners, thick at the root, curving up past the lip and in toward the cheek
        base = np.array([head.cx + sgn * tusk_x * A, ys, head.cz0 + front * A * 0.86])
        L = tusks * H
        path = kit.bezier(base - [0, 0.12 * L, 0.06 * L], base + [sgn * 0.06 * L, 0.55 * L, 0.1 * L],
                          base + [-sgn * 0.1 * L, L, 0.0], 9)
        t = np.linspace(0, 1, len(path))
        parts.append(kit.sweep(path, kit.ring_profile(tusk_width * A, 0.75 * tusk_width * A, 8), tusk,
                               up=(0.0, 0.0, 1.0), scale=np.maximum((1 - t) ** 0.8, 0.05)))
    if tooth:  # a small chipped tooth off centre
        b = np.array([head.cx + 0.14 * A, ys + 0.02, head.cz0 + front * A * 0.95])
        parts.append(kit.rounded_box((tooth * A, 1.2 * tooth * A, 0.6 * tooth * A), tusk, center=b + [0, 0.5 * tooth * A, 0],
                                     roundness=0.4))
    piece = kit.Piece.merge(*parts)
    um = np.linspace(-0.78, 0.78, 7)  # the mouth's crease, cut flat into the face just above the lip
    piece.paint = paint(head, [dict(path=[(u * width * 0.9, top + 0.07) for u in um], width=(0.04, 0.09, 0.04),
                                    slot=mouth)])
    return piece


def beard(head, top=0.0, under_nose=-0.52, width=92.0, bulk=0.16, length=0.55, tufts=5, mustache=0.1, cols=21,
          rows=10, slot="beard"):
    """
    A full beard: a patch of hair running from sideburns at the temples (`top`) down round the cheeks to under the
    nose (`under_nose`, where a drooping `mustache` lies over the mouth) and over the chin, `bulk` thick (shares of
    the half width), lumpy in `tufts`, its lower edge pulled down into a jagged point `length` below the chin.
    """
    A = head.A
    u = np.linspace(-1, 1, cols)
    th = FRONT + np.radians(width) * u
    t = np.linspace(0, 1, rows)[:, None]
    edge = under_nose + (top - under_nose) * np.abs(u) ** 4  # bare cheeks under the eyes, sideburns at the sides
    yn = edge + (-0.97 - edge) * t
    phi = head.phi(yn)
    grid = np.broadcast_to(th, yn.shape)
    lumps = 1 + 0.3 * np.abs(np.sin(0.5 * np.pi * tufts * (u + 1)))
    swell = A * bulk * lumps * (1 - 0.6 * u ** 2) * np.sin(np.pi * (0.12 + 0.88 * t) ** 0.8) + 0.012
    jag = 1 + 0.35 * np.abs(np.sin(0.5 * np.pi * (2 * tufts + 1) * (u + 1)))  # the lower edge in points
    sag = (length * A * t ** 2 * (1 - u ** 2) ** 1.5 * jag)[..., None] * [0.0, -1.0, 0.25]
    outer = head.surface(grid, phi, swell).reshape(rows, cols, 3) + sag
    inner = head.surface(grid, phi, -0.02).reshape(rows, cols, 3) + 0.8 * sag
    parts = [kit.patch_shell(outer, inner, slot)]
    if mustache:
        for sgn in (-1, 1):
            pts = [(sgn * 0.04, under_nose + 0.04), (sgn * 0.28, under_nose - 0.02), (sgn * 0.46, under_nose - 0.16),
                   (sgn * 0.5, under_nose - 0.32)]
            (th_m, ph_m) = head.front_angles(np.array([p[0] for p in pts]) * A, head.y(np.array([p[1] for p in pts])))
            path = head.surface(th_m, ph_m, A * bulk * 0.6 + 0.03)
            parts.append(kit.tube(kit.spline(path, 8), mustache * A, slot, sides=6, taper=0.6))
    return kit.Piece.merge(*parts)


def _ear_shape(style, s, base, angle, notch_depth=0.0):
    """
    One ear in its own frame (x across its thickness, the inner side toward -x; y up; z out from the head): a
    lofted leaf with rounded rims, thinning toward the tip, its inner side a sunken bowl (deepest near the root).
    Returns `(piece, lower_edge(t) -> (y, z), slot_of_face)` where faces carry "skin" / "inner" / "canal".
    `notch_depth` > 0 bites a rounded dip out of the upper rim.
    """
    L = {"round": 0.42, "long": 0.8}.get(style, 0.85) * s
    a = np.radians({"round": 0.0, "long": 60.0}.get(style, angle))
    h = 0.56 * s * base
    th0 = 0.075 * s * base * (0.8 if style == "leaf" else 1.0)

    def half_width(t):
        if style == "round":  # stylized's half disc
            return 0.62 * h * np.sqrt(np.clip(1 - t ** 2, 0, 1)) + 0.04 * h
        if style == "leaf":  # broadest a third of the way out
            return 0.62 * h * (1 - t) ** 0.75 * (0.75 + 1.1 * t)
        return 0.5 * h * (1 - t) ** 0.9 * (1 + 0.3 * np.sin(np.pi * t))

    ts = np.r_[-0.3, -0.15, np.linspace(0.0, 0.94, 15), 0.985]  # the root runs on into the skull
    phis = 2 * np.pi * (np.arange(20) + 0.5) / 20
    rings, info = [], []
    for t in ts:
        w = half_width(max(t, 0.0))
        w_up = w * (1 - notch_depth * np.exp(-((t - 0.55) / 0.07) ** 2))
        w_low = 0.94 * w
        th = th0 * (1 - 0.55 * max(t, 0.0))
        rise, z = (L * np.sin(a) * t + 0.04 * L * t * (1 - t), L * np.cos(a) * t) if t >= 0 else (0.0, L * t)
        c, sn = np.cos(phis), np.sin(phis)
        dish = np.clip(1 - (c / 0.68) ** 2, 0, 1) * np.clip(np.sin(np.pi * np.clip((t - 0.04) / 0.8, 0, 1)), 0, 1)
        x = th * sn + np.where(sn < 0, 1.35 * th * dish * -sn, 0.0)  # the inner side sunk into a bowl
        y = rise + np.where(c >= 0, w_up, w_low) * np.sign(c) * np.abs(c) ** 0.85
        rings.append(np.column_stack([x, y, np.full(len(c), z)]))
        info.append(np.column_stack([np.full(len(c), t), c, sn, dish]))
    verts, faces = kit._loft(rings)
    tip = np.array([0.0, L * np.sin(a) + 0.0, L * np.cos(a)])
    n = len(phis)
    verts, faces = kit._cap_loops(verts, faces, [list(range(n)), list(range((len(ts) - 1) * n, len(ts) * n))],
                                  centers=[rings[0].mean(0), tip])
    info_full = np.vstack([np.vstack(info), [ts[0], 0.0, 0.0, 0.0], [ts[-1], 0.0, 0.0, 0.0]])
    t_v, sn_v, dish_v = info_full[:, 0], info_full[:, 2], info_full[:, 3]
    depth = np.clip(-sn_v, 0.0, 1.0) * dish_v
    inner_depth = np.clip((depth - 0.02) / 0.45, 0.0, 1.0)
    s_bowl = inner_depth * inner_depth * (3.0 - 2.0 * inner_depth)
    canal = s_bowl * np.clip((0.35 - t_v) / 0.25, 0.0, 1.0)
    s_canal = canal * canal * (3.0 - 2.0 * canal)
    vert_uv_t = np.clip(0.5 * s_bowl + 0.45 * s_canal, 0.0, 0.95)
    faces_orient = kit._orient_closed(verts, faces)
    tri_uv_t = vert_uv_t[faces_orient]

    piece = kit.Piece(verts, faces_orient, "ear", uv_t=tri_uv_t)

    def lower_edge(t):
        return L * np.sin(a) * t + 0.04 * L * t * (1 - t) - 0.94 * half_width(t), L * np.cos(a) * t

    return piece, lower_edge


def ears(head, style="round", y=-0.15, size=1.0, angle=0.0, sweep=20.0, base=1.0, slot="skin", inner="skin_dark",
         canal=None, ring=None, ring_slot="gold", rings=1, ring_size=0.07, notch=None, notch_depth=0.35):
    """
    Both ears on the sides of the head, lofted with rounded rims and a sunken inner bowl (`inner`; a `canal` slot
    darkens its deepest part near the root, for big ears). `round`: stylized's half discs. `pointed`: leaves reaching out (and up by `angle`
    degrees), swept back by `sweep`, an orc's or a goblin's. `long`: tall pointed ears standing up and back, an
    elf's or a demon's. `leaf`: huge ears broadest well out from the head, a goblin's. Jewelry: `rings` rings
    `ring_size` across through the lower rim of the `ring` ear ("left", "right", "both"), in `ring_slot`; a `notch`
    (a rounded dip `notch_depth` of the rim's half height deep) in the upper rim of one ear ("left", "right",
    "both"). `base` scales the ear's root height and thickness against its length.
    """
    s = size * head.A
    f = kit.band_frame(head, head.y(y), 0.0, -0.06 * s).rotated(-sweep, "y")  # local x points back, z out

    def built(depth):
        piece, edge = _ear_shape(style, s, base, angle, depth)
        ear_slot = slot if inner == slot else "ear"
        uv_t = None if inner == slot else piece.uv_t
        return kit.Piece(piece.verts, piece.faces, ear_slot, uv_t=uv_t).moved(f), edge

    one, lower_edge = built(0.0)
    cut = built(notch_depth)[0] if notch else one
    ears_ = [cut if notch in ("left", "both") else one, (cut if notch in ("right", "both") else one).mirrored()]
    if ring:  # rings pierced through the lower rim, spaced out from the root
        r = ring_size * s
        loops = []
        for k in range(rings):
            yy, zz = lower_edge(0.2 + 0.17 * k)
            t = 2 * np.pi * np.arange(12) / 12
            loops.append(kit.sweep(np.column_stack([r * np.cos(t), yy + 0.25 * r - r + r * np.sin(t) * 1.0,
                                                    np.full(12, zz)]),
                                   kit.ring_profile(0.22 * r, 0.22 * r, 6), ring_slot, closed=True).moved(f))
        loop = kit.Piece.merge(*loops)
        for side, k in (("left", 0), ("right", 1)):
            if ring in (side, "both"):
                ears_.append(loop if k == 0 else loop.mirrored())
    return kit.Piece.merge(*ears_)


def horns(head, x=0.38, y=0.62, length=1.0, radius=0.17, out=0.35, back=0.3, curl=0.25, slot="horn"):
    """A pair of curved horns growing from the top of the forehead at +-`x`: up, out by `out` and back by `back`
    (shares of their `length`, in head half widths), their tips curling in by `curl` (a devil's or an imp's)."""
    A = head.A
    L = length * A
    out_h = []
    for sgn in (-1, 1):
        (P,), (n,) = head.surface(*head.front_angles(sgn * x * A, head.y(y)), normals=True)
        side, up, rear = np.array([sgn, 0.0, 0.0]), np.array([0.0, 1.0, 0.0]), np.array([0.0, 0.0, -1.0])
        path = kit.spline([P - 0.7 * radius * A * n, P + L * (0.35 * out * side + 0.35 * up + 0.2 * back * rear),
                           P + L * (out * side + 0.75 * up + 0.6 * back * rear),
                           P + L * ((out - curl) * side + 1.0 * up + back * rear)], 10)
        out_h.append(kit.tube(path, radius * A, slot, sides=7, taper=0.8))
    return kit.Piece.merge(*out_h)


def hair(head, style="cap", **kw):
    """Scripted hair (`cap`, `topknot`, `tuft`)."""
    return {"cap": hair_cap, "topknot": topknot, "tuft": tuft}.get(style, hair_cap)(head, **kw)


def hair_cap(head, line=((0, 0.45), (40, 0.3), (90, -0.2), (130, -0.8), (180, -0.85)), peak=0.0, thickness=0.05,
             slot="hair",
             sides=32, rows=9):
    """
    A shell of hair over the cranium down to a hairline: `line` gives its height (a share of the half height) at
    angles from the front (degrees, 0 = the forehead, 180 = the nape), `peak` dips it at the forehead's middle (a
    widow's peak). Its rim is chamfered like stylized's hair.
    """
    th = 2 * np.pi * np.arange(sides) / sides
    from_front = np.degrees(np.abs(np.angle(np.exp(1j * (th - FRONT)))))
    deg, yn = np.asarray(line, float).T
    end = np.interp(from_front, deg, yn) - peak * np.exp(-(from_front / 14.0) ** 2)
    phi_end = head.phi(np.clip(end, -0.97, 0.97))
    outer = [head.surface(th, phi_end * i / rows, thickness) for i in range(rows + 1)]
    inner = [head.surface(th, phi_end * i / rows, -0.02) for i in range(rows + 1)]
    lip = head.surface(th, phi_end + 0.04, 0.4 * thickness)
    verts, faces = kit.weld_collapsed(*kit._loft(outer + [lip] + inner[::-1]), tol=1e-7)
    return kit.Piece(verts, kit._orient_closed(verts, faces), slot)


def topknot(head, size=0.3, tail=0.75, slot="hair", tie="tie"):
    """A warrior's bun on the crown, tied off, its tail falling back."""
    A = head.A
    r = size * A
    (P,), _ = head.surface(3 * np.pi / 2, 0.3, 0.0, normals=True)
    up = np.array([0.0, 1.0, 0.0])
    c = P + 0.55 * r * up  # its lower part inside the scalp: no gap under a rounded crown
    t = np.radians(np.arange(0, 360, 45))
    bun = kit.hull([c + [r * np.cos(a) * np.cos(b), 0.9 * r * np.sin(b), r * np.sin(a) * np.cos(b)]
                    for a in t for b in np.radians((-50, 0, 45, 80))], slot)
    band = kit.sweep(np.array([P + 0.15 * r * up + 0.62 * r * np.array([np.cos(a), 0.0, np.sin(a)])
                               for a in np.linspace(0, 2 * np.pi, 9)[:-1]]),
                     kit.rect_profile(0.35 * r, 0.25 * r, 0.05 * r), tie, closed=True)
    back = np.array([0.0, 0.0, -1.0])
    L = tail * A
    tail_piece = kit.tube(kit.spline([c + 0.4 * r * back, c + 0.45 * L * back + 0.15 * L * up,
                                      c + 0.75 * L * back - 0.35 * L * up, c + 0.8 * L * back - 0.8 * L * up], 10),
                          0.7 * r, slot, sides=6, taper=0.9)
    return kit.Piece.merge(bun, band, tail_piece)


def tuft(head, columns=4, height=3, size=0.17, slot="hair", seed=4):
    """A mohawk tuft (stylized's orc): `columns` columns of chunky faceted clumps along the top of the head from the
    hairline back, the front ones stacked `height` clumps tall, each column leaning further back."""
    from prefabs.creatures.golems import rock

    A = head.A
    rng = np.random.default_rng(seed)
    parts = []
    for k in range(columns):
        phi = 0.36 - 0.2 * k  # from above the forehead back over the crown, the clumps touching
        theta = np.pi / 2 if phi >= 0 else 3 * np.pi / 2
        (P,), (n,) = head.surface(theta, abs(phi), 0.0, normals=True)
        r = size * A * (1.0 - 0.12 * k)
        back = np.radians(12 + 14 * k)
        lean = np.array([0.0, np.cos(back), -np.sin(back)])
        for lvl in range(max(1, height - k // 2)):
            c = P + 0.25 * r * n + (0.15 + 0.85 * lvl) * r * lean + rng.normal(0.0, 0.12 * r, 3) * [1.5, 0.2, 0.5]
            sz = r * (1.0 - 0.12 * lvl) * rng.uniform(0.85, 1.15)
            parts.append(rock((1.15 * sz, 1.6 * sz, 1.1 * sz), c, slot, bevel=0.2, bulge=0.04, jitter=0.16, taper=0.35,
                              turn=(-np.degrees(back) + rng.uniform(-15, 15), rng.uniform(-40, 40), rng.uniform(-20, 20)),
                              seed=seed + 10 * k + lvl))
    return kit.Piece.merge(*parts)


def surface_band(head, yn, height, thickness, slot, tilt=(0.0, 0.0), theta=None, sides=28, bevel=0.35, inset=0.012):
    """
    A band over the cranium centred on the height share `yn`, rising by `tilt` = (toward the front, toward +x) as it
    goes round, `height` tall (a share of the half height), `thickness` off the skin. `theta` limits it to an arc of
    angles (then its ends are capped): a mummy's wraps, an eye slit, a headband.
    """
    th = 2 * np.pi * np.arange(sides) / sides if theta is None else np.asarray(theta, float)
    centre = yn + tilt[0] * np.sin(th) + tilt[1] * np.cos(th)
    c = bevel * thickness
    dt = min(c / max(height * head.Hh, 1e-9), 0.45)
    profile = [(0.0, -inset), (0.0, thickness - c), (dt, thickness), (1 - dt, thickness), (1.0, thickness - c),
               (1.0, -inset)]
    rings = [head.surface(th, head.phi(np.clip(centre + (t - 0.5) * height, -0.97, 0.97)), out) for t, out in profile]
    verts, faces = kit._loft(rings, closed_rings=theta is None, closed_profile=True)
    if theta is not None:
        n = len(th)
        verts, faces = kit._cap_loops(verts, faces, [[k * n for k in range(len(rings))],
                                                     [k * n + n - 1 for k in range(len(rings))]])
    return kit.Piece(verts, kit._orient_closed(verts, faces), slot)


def wraps(head, bands=((-0.9, 0.16, (0.06, 0.0)), (-0.68, 0.2, (-0.05, 0.03)), (0.3, 0.2, (0.05, -0.02)),
                       (0.52, 0.2, (-0.06, 0.03)), (0.72, 0.16, (0.04, 0.0))),
          slit=(-0.12, 0.42, 62.0), crown=0.62, diagonal=(-0.1, 0.3, 0.22, 85.0), thickness=0.03, slot="wrap",
          slit_slot="dark"):
    """
    A mummy's bandages: tilted bands (`bands` = (height share, band height, tilt)) over a cranium of a darker wrap
    color, a wrapped `crown` down to that height share, an eye `slit` = (height share, height, half angle in
    degrees) of dark cloth across the face, and a `diagonal` bandage over the back of the head where the slit's
    height leaves it bare = (height share at the back, band height, rise from one side to the other, half angle).
    """
    out = [surface_band(head, yn, h, thickness, slot, tilt=tilt) for yn, h, tilt in bands]
    out.append(hair_cap(head, line=((0, crown), (180, crown + 0.08)), thickness=0.6 * thickness, slot=slot))
    if slit:
        yn, h, half = slit
        arc = FRONT + np.radians(np.linspace(-half, half, 13))
        out.append(surface_band(head, yn, h, 0.012, slit_slot, theta=arc))
        for edge, tilt in ((yn + h / 2 + 0.05, (0.03, 0.0)), (yn - h / 2 - 0.05, (-0.03, 0.0))):
            out.append(surface_band(head, edge, 0.17, thickness + 0.004, slot, tilt=tilt))
    if diagonal:
        yn, h, rise, half = diagonal
        arc = 3 * np.pi / 2 + np.radians(np.linspace(-half, half, 15))
        out.append(surface_band(head, yn, h, thickness + 0.008, slot, tilt=(0.0, rise), theta=arc))
    return kit.Piece.merge(*out)


def scar(head, x=-0.42, y=-0.05, length=0.55, width=0.05, slot="scar", count=2, angle=-55.0):
    """Slashes across one eye as `paint` strokes: `count` parallel cuts `length` long (half widths) at `angle`
    degrees, centred on (`x`, `y`) in the face's shares."""
    a = np.radians(angle)
    d = np.array([np.cos(a), np.sin(a)])
    strokes = []
    for i in range(count):
        off = np.array([np.sin(a), -np.cos(a)]) * (i - (count - 1) / 2) * 0.12
        ends = [np.array([x, 0.0]) + off + sgn * 0.5 * length * d for sgn in (-1, 1)]
        strokes.append(dict(path=[(e[0], y + e[1] * head.A / head.Hh) for e in ends], width=(0.6 * width, width,
                                                                                              0.4 * width),
                            slot=slot))
    return strokes


def paint(head, strokes, slot="paint"):
    """
    Face paint, scars and tattoos as outlines to cut into the face. `strokes` mixes `WARPAINT` pattern names
    (reusable on any head: "war", "stripe", ...) and strokes; each stroke `dict(path=[(x, y), ...],
    width=..., mirror=False, slot=...)` (x and width shares of the half width, y of the half height; `width` one
    value or one per path point) becomes convex `(slot, polygon)` pieces in front view: a band per segment and a
    round joint at every bend. `build` cuts them into the cranium, jaw and nose (`kit.paint_cut`), so paint is flat
    on the skin, never a raised bar, and keeps the one-flat-color-per-slot look without a texture.
    """
    A = head.A
    out = []
    if isinstance(strokes, str):
        strokes = [strokes]
    strokes = [s_ for item in strokes for s_ in (WARPAINT[item] if isinstance(item, str) else [item])]
    for stroke in strokes:
        P = np.asarray(stroke["path"], float) * [A, head.Hh] + [head.cx, head.cy]
        dense = kit.resample(P, 0.03) if len(P) > 1 else P
        width = np.atleast_1d(np.asarray(stroke.get("width", 0.08), float)) * A  # one value, or a profile along it
        w = np.interp(np.linspace(0, 1, len(dense)), np.linspace(0, 1, len(width)), width) / 2
        sl = stroke.get("slot", slot)
        polys = []
        for k in range(len(dense) - 1):
            a, b = dense[k], dense[k + 1]
            t = (b - a) / max(np.linalg.norm(b - a), 1e-9)
            nrm = np.array([-t[1], t[0]])
            polys.append([a + w[k] * nrm, b + w[k + 1] * nrm, b - w[k + 1] * nrm, a - w[k] * nrm])
        for k in range(1, len(dense) - 1):  # round joints, so bends show no notches
            th = 2 * np.pi * np.arange(10) / 10
            polys.append(dense[k] + w[k] * np.column_stack([np.cos(th), np.sin(th)]))
        for q in polys:
            q = np.asarray(q, float)
            out.append((sl, q))
            if stroke.get("mirror"):
                out.append((sl, (q * [-1, 1] + [2 * head.cx, 0])[::-1]))
    return out


FEATURES = {"earrings": None, "eyes": eyes, "brows": brows, "nose": nose, "mouth": mouth, "jaw": jaw, "block_jaw": block_jaw,
            "tusks": tusks, "horns": horns,
            "beard": beard,
            "ears": ears, "hair": hair, "wraps": wraps, "scar": scar, "paint": paint}
SKIN_FEATURES = ("nose", "jaw", "block_jaw")  # what face paint is projected onto, with the cranium




WARPAINT = {
    # an orc's: two stripes down the forehead, a slash under each eye out to the cheek, a stripe down the chin
    "war": [dict(path=[(0.24, 0.94), (0.24, 0.42)], width=(0.13, 0.15), mirror=True),
            dict(path=[(0.42, -0.12), (0.72, -0.44)], width=(0.15, 0.07), mirror=True),
            dict(path=[(0.0, -0.6), (0.0, -0.92)], width=(0.18, 0.06))],
    "slashes": [dict(path=[(0.3, 0.5), (0.5, -0.3)], width=(0.07, 0.1, 0.05)),  # three claw marks over one eye
                dict(path=[(0.42, 0.5), (0.62, -0.3)], width=(0.07, 0.1, 0.05)),
                dict(path=[(0.54, 0.45), (0.72, -0.2)], width=(0.06, 0.09, 0.04))],
    "stripe": [dict(path=[(0.0, 0.75), (0.0, -0.8)], width=(0.16, 0.12))],
    "under eyes": [dict(path=[(0.18, -0.42), (0.45, -0.4), (0.68, -0.32)], width=(0.07, 0.1, 0.05), mirror=True)],
    "mask": [dict(path=[(-0.85, -0.12), (0.0, -0.2), (0.85, -0.12)], width=(0.3, 0.36, 0.3), slot="paint")],
}
