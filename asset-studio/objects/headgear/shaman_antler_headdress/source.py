"""Shaman antler headdress: a faceted leather skullcap and brow band, an animal skull over the forehead, two branching
bone antlers with leather collars and wraps, and teal feather fans with amber tips on leather side bosses.

Modeled against `reference.png` (front, three-quarter and side panels on the faced dummy head). Run by
`studio.py item model headgear/shaman_antler_headdress`; Blender frame: Z up, front toward -Y, hat-prop frame of the
adventurer heads. Parts are modeled around `HEAD`, the centre of the head the design was fitted on (`ctx.origin`).
"""

import math

import bmesh
from mathutils import Vector

from modeling import finish, shells, solids

HEAD = Vector((0.0, 0.0, 0.443))  # the reference head's centre in the hat-prop frame (fitted to the class heads)
FIT_HOST = "barbarian"  # the class head the cap clears; the closest match to the dummy head of the concept


def P(x, y, z):
    """A point relative to the head centre."""
    return Vector((x, y, z))


def _band_bottom(theta):
    """Bottom rim height of the headband at azimuth `theta` (0 = front -Y, pi = back +Y): low at the brow, lifted
    over the ears, dipping again at the nape."""
    front = math.cos(theta)
    z = 0.088 + 0.100 * front
    if front < 0.0:
        z -= 0.020 * math.sin(theta) ** 2 * -front
    return z


def leather_cap(ctx):
    """Faceted skullcap down to the stepped headband, kept clear of the head and thickened inward."""
    around, up, top = 18, 6, 0.538
    rows = []
    for iv in range(up + 1):
        lift = iv / up * 0.5 * math.pi * 0.92
        row = []
        for iu in range(around):
            phi = iu / around * 2.0 * math.pi
            dx, dy = math.sin(phi), -math.cos(phi)
            bottom = _band_bottom(phi)
            z = bottom + (top - bottom) * math.sin(lift)
            squash = math.cos(lift) * (1.0 + 0.048 * (abs(dx) * abs(dy)) ** 0.85)  # the head's square corners
            ry = 0.488 if dy < 0 else 0.494
            row.append(P(dx * 0.472 * squash, dy * ry * squash - 0.004 * (1.0 - iv / up), z))
        rows.append(row)
    shells.clear_host(rows, ctx.host_bvh(FIT_HOST), clearance=0.038, center=P(0.0, -0.02, 0.05))

    step = 0.015  # the headband stands proud of the cap
    proud = [[p + Vector((p.x, p.y, 0.0)).normalized() * step for p in rows[i]] for i in (0, 1)]
    grid = proud + rows[1:]
    bm = bmesh.new()
    verts = [[bm.verts.new(p) for p in row] for row in grid]
    for iv in range(len(grid) - 1):
        for iu in range(around):
            nu = (iu + 1) % around
            face = bm.faces.new((verts[iv][iu], verts[iv][nu], verts[iv + 1][nu], verts[iv + 1][iu]))
            face.material_index = 1 if iv == 0 else 2 if iv == 1 else 0  # band, its dark crease shelf, dome
    crown = bm.faces.new(verts[-1])
    crown.material_index = 0
    bmesh.ops.poke(bm, faces=[crown])
    shells.solidify(bm, thickness=0.030, inner_index=2, center=P(0.0, -0.02, 0.02))
    ctx.add("LeatherCap", bm, ["leather", "leather_light", "leather_dark"])


def bone_skull(ctx):
    """Low-poly animal skull over the brow: rounded cranium, six-sided orbital rims, recessed sockets and nasal
    cavity, a tapering snout with teeth over the brow band."""
    bm = bmesh.new()

    def v(x, y, z):
        return bm.verts.new(P(x, y, z))

    top_back, top, fore = v(0.0, -0.410, 0.468), v(0.0, -0.455, 0.472), v(0.0, -0.516, 0.412)
    glabella, bridge, nose_top = v(0.0, -0.552, 0.336), v(0.0, -0.566, 0.242), v(0.0, -0.560, 0.160)
    jaw_mid, cleft = v(0.0, -0.534, 0.074), v(0.0, -0.514, 0.018)

    def half(sx):
        named = {
            "cr_top": (0.078, -0.452, 0.460), "cr_out": (0.134, -0.464, 0.398),
            "brow_in": (0.050, -0.542, 0.344), "brow_top": (0.104, -0.528, 0.356), "brow_out": (0.152, -0.504, 0.314),
            "cheek_u": (0.154, -0.506, 0.238), "cheek_m": (0.104, -0.530, 0.166), "bridge_s": (0.040, -0.554, 0.236),
            "ey_ti": (0.056, -0.536, 0.314), "ey_t": (0.095, -0.524, 0.330), "ey_to": (0.132, -0.508, 0.298),
            "ey_bo": (0.128, -0.512, 0.246), "ey_b": (0.092, -0.532, 0.216), "ey_bi": (0.055, -0.546, 0.240),
            "nose_u": (0.016, -0.552, 0.136), "nose_l": (0.022, -0.536, 0.090),
            "snout_u": (0.060, -0.540, 0.148), "snout_l": (0.064, -0.524, 0.082),
            "jaw_in": (0.018, -0.524, 0.045), "tooth_in": (0.013, -0.502, -0.022), "jaw_mid": (0.036, -0.520, 0.040),
            "tooth_out": (0.046, -0.500, -0.010), "jaw_out": (0.058, -0.514, 0.056),
            "bk_top": (0.086, -0.405, 0.442), "bk_brow": (0.140, -0.432, 0.318), "bk_cheek": (0.096, -0.458, 0.172),
            "bk_jaw": (0.046, -0.482, 0.050),
        }
        return {key: v(sx * x, y, z) for key, (x, y, z) in named.items()}

    def face(corners, index=0, flip=False):
        bm.faces.new(list(reversed(corners)) if flip else corners).material_index = index

    for s, flip in ((half(1.0), False), (half(-1.0), True)):
        face([top_back, s["bk_top"], s["cr_top"], top], 0, flip)
        face([top, s["cr_top"], s["brow_in"], fore], 0, flip)
        face([fore, s["brow_in"], glabella], 0, flip)
        face([s["cr_top"], s["cr_out"], s["brow_top"], s["brow_in"]], 0, flip)
        face([s["cr_out"], s["brow_out"], s["brow_top"]], 0, flip)
        face([s["bk_top"], s["bk_brow"], s["cr_out"], s["cr_top"]], 2, flip)
        face([s["cr_out"], s["bk_brow"], s["brow_out"]], 2, flip)
        face([s["bk_brow"], s["bk_cheek"], s["cheek_u"], s["brow_out"]], 2, flip)
        face([s["cheek_u"], s["bk_cheek"], s["cheek_m"]], 2, flip)
        face([s["cheek_m"], s["bk_cheek"], s["bk_jaw"], s["snout_l"]], 2, flip)
        face([glabella, s["brow_in"], s["bridge_s"], bridge], 0, flip)
        face([bridge, s["bridge_s"], s["snout_u"], nose_top], 0, flip)
        # Orbital bevel ring, its lower and inner bevels in shade.
        face([s["brow_in"], s["brow_top"], s["ey_t"], s["ey_ti"]], 2, flip)
        face([s["brow_top"], s["brow_out"], s["ey_to"], s["ey_t"]], 2, flip)
        face([s["brow_out"], s["cheek_u"], s["ey_bo"], s["ey_to"]], 2, flip)
        face([s["cheek_u"], s["cheek_m"], s["ey_b"], s["ey_bo"]], 0, flip)
        face([s["cheek_m"], s["snout_u"], s["ey_bi"], s["ey_b"]], 0, flip)
        face([s["snout_u"], s["bridge_s"], s["ey_ti"], s["ey_bi"]], 2, flip)
        face([s["bridge_s"], s["brow_in"], s["ey_ti"]], 2, flip)
        face([nose_top, s["snout_u"], s["nose_u"]], 0, flip)
        face([s["snout_u"], s["cheek_m"], s["snout_l"]], 0, flip)
        face([s["snout_u"], s["snout_l"], s["nose_l"], s["nose_u"]], 0, flip)
        face([s["nose_l"], s["snout_l"], s["jaw_mid"], s["jaw_in"]], 0, flip)
        face([s["snout_l"], s["jaw_out"], s["jaw_mid"]], 0, flip)
        face([s["snout_l"], s["bk_jaw"], s["jaw_out"]], 2, flip)
        face([jaw_mid, s["nose_l"], s["jaw_in"]], 0, flip)
        face([jaw_mid, s["jaw_in"], cleft], 0, flip)
        face([cleft, s["jaw_in"], s["tooth_in"]], 0, flip)
        face([s["jaw_in"], s["jaw_mid"], s["tooth_in"]], 0, flip)
        face([s["jaw_mid"], s["jaw_out"], s["tooth_out"]], 0, flip)
        face([s["jaw_out"], s["bk_jaw"], s["tooth_out"]], 2, flip)
        face([s["bk_jaw"], s["tooth_in"], s["tooth_out"]], 2, flip)
        face([s["jaw_mid"], s["tooth_out"], s["tooth_in"]], 2, flip)
        # Recessed eye socket and nasal cavity, cut into the face rather than painted on.
        rim = [s[k] for k in ("ey_ti", "ey_t", "ey_to", "ey_bo", "ey_b", "ey_bi")]
        sunk = [bm.verts.new(Vector((r.co.x * 0.88, r.co.y + 0.032, r.co.z + 0.002))) for r in rim]
        for k in range(len(rim)):
            face([rim[k], rim[(k + 1) % len(rim)], sunk[(k + 1) % len(rim)], sunk[k]], 1, flip)
        face(sunk, 1, flip)
        nostril = [nose_top, s["nose_u"], s["nose_l"], jaw_mid]
        cavity = [bm.verts.new(Vector((r.co.x * 0.72, r.co.y + 0.024, r.co.z))) for r in nostril]
        for k in range(len(nostril) - 1):
            face([nostril[k], nostril[k + 1], cavity[k + 1], cavity[k]], 1, flip)
        face(cavity, 1, flip)
    finish.close_open(bm, 2)  # the skull's back, hidden against the band
    ctx.add("BoneSkull", bm, ["bone", "socket", "bone_dark"])


def antlers(ctx):
    """Per side: a leather collar coaxial with the antler's base, a branching bone antler (main beam, inner fork,
    front tine; branches start inside the beam so their roots stay hidden) and two snug leather wraps."""
    for name, sx in (("R", 1.0), ("L", -1.0)):
        root, first = P(sx * 0.380, -0.015, 0.395), P(sx * 0.482, -0.005, 0.515)
        axis = (first - root).normalized()
        bm = bmesh.new()
        solids.tapered_tube(bm, [(root - axis * 0.110, 0.104), (root + axis * 0.032, 0.095),
                                 (root + axis * 0.068, 0.078)], sides=8, pointed=False)
        ctx.add(f"AntlerCollar{name}", bm, ["leather_light"])

        bm = bmesh.new()
        beam = [(root, 0.062), (first, 0.057), (P(sx * 0.522, 0.015, 0.610), 0.053), (P(sx * 0.550, 0.040, 0.700), 0.050),
                (P(sx * 0.580, 0.065, 0.785), 0.047), (P(sx * 0.648, 0.095, 0.890), 0.040),
                (P(sx * 0.676, 0.120, 1.010), 0.030), (P(sx * 0.662, 0.130, 1.150), 0.012)]
        inner_fork = [(P(sx * 0.568, 0.055, 0.755), 0.025), (P(sx * 0.545, 0.054, 0.805), 0.036),
                      (P(sx * 0.500, 0.050, 0.900), 0.030), (P(sx * 0.464, 0.048, 0.980), 0.022),
                      (P(sx * 0.435, 0.048, 1.050), 0.011)]
        front_tine = [(P(sx * 0.478, -0.006, 0.505), 0.025), (P(sx * 0.535, -0.028, 0.538), 0.038),
                      (P(sx * 0.625, -0.068, 0.590), 0.034), (P(sx * 0.695, -0.092, 0.668), 0.026),
                      (P(sx * 0.715, -0.095, 0.745), 0.012)]
        for branch in (beam, inner_fork, front_tine):
            solids.tapered_tube(bm, branch, sides=6)
        ctx.add(f"Antler{name}", bm, ["bone"])

        bm = bmesh.new()
        for wrap in ([(P(sx * 0.527, 0.020, 0.626), 0.058), (P(sx * 0.544, 0.034, 0.678), 0.056)],
                     [(P(sx * 0.550, 0.040, 0.698), 0.056), (P(sx * 0.570, 0.056, 0.752), 0.054)]):
            solids.tapered_tube(bm, wrap, sides=6, pointed=False)
        ctx.add(f"AntlerWrap{name}", bm, ["leather_dark"])


def feathers(ctx):
    """Per side: a round leather boss on the band and a fan of four feathers, the first tucked along the temple and
    the rest sweeping back and down along the jaw line."""
    for name, sx in (("R", 1.0), ("L", -1.0)):
        bm = bmesh.new()
        solids.tapered_tube(bm, [(P(sx * 0.430, -0.022, 0.148), 0.090), (P(sx * 0.492, -0.022, 0.152), 0.086),
                                 (P(sx * 0.496, -0.022, 0.152), 0.048), (P(sx * 0.515, -0.022, 0.154), 0.044)], sides=10)
        ctx.add(f"FeatherBoss{name}", bm, ["leather_light"])

        bm = bmesh.new()
        out = Vector((sx * 0.90, -0.35, 0.15)).normalized()
        for root, tip, width in ((P(sx * 0.472, -0.005, 0.195), P(sx * 0.660, 0.355, 0.610), 0.056),
                                 (P(sx * 0.478, 0.005, 0.170), P(sx * 0.705, 0.415, 0.455), 0.064),
                                 (P(sx * 0.478, 0.005, 0.142), P(sx * 0.672, 0.355, 0.235), 0.060),
                                 (P(sx * 0.472, -0.005, 0.118), P(sx * 0.615, 0.255, -0.015), 0.054)):
            solids.blade(bm, root, tip, out, half_width=width)
        ctx.add(f"Feathers{name}", bm, ["feather", "feather_tip"])


def build(ctx):
    ctx.origin = HEAD
    leather_cap(ctx)
    bone_skull(ctx)
    antlers(ctx)
    feathers(ctx)
