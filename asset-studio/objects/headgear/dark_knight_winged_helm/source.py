"""Dark knight winged helm: a fluted steel dome with a sagittal crest, a gold-trimmed face opening with a ruby in
a hexagonal bezel, cheek guards with recessed vents, a flared bell skirt at the nape, and swept four-blade wings on
round ear medallions.

Modeled against `reference.png` (front, three-quarter and side panels on the dummy head). Run by
`studio.py item model headgear/dark_knight_winged_helm`; Blender frame: Z up, front toward -Y, hat-prop frame of
the adventurer heads. Parts are modeled around `HEAD`, the centre of the head the design was fitted on
(`ctx.origin`).
"""

import math

import bmesh
from mathutils import Matrix, Vector

from modeling import shells, solids

HEAD = Vector((0.0, 0.0, 0.464))  # the design head's centre in the hat-prop frame (the rig mannequin's head)
FIT_HOST = "barbarian"  # the class head the shell clears
CLEAR_CENTER = Vector((0.0, -0.02, 0.05))  # rays for the clearance push start here
SOLID_CENTER = Vector((0.0, -0.02, 0.02))  # the shell thickens toward here


def helmet_shell_and_trim(ctx):
    """The steel shell (fluted dome, brow cutout, cheek guards with recessed vents, flared bell skirt at the nape),
    kept clear of the head, and the gold trim framing the face opening from the gem down both cheek guards."""
    az_deg = [0.0, 12.0, 29.0, 33.0, 48.0, 62.0, 66.0, 82.0, 100.0, 120.0, 142.0, 164.0, 180.0]
    ncols = len(az_deg)
    nrows = 13

    def dome_xy(az_d: float, rx: float, ry_front: float, ry_back: float, y_shift: float = -0.035) -> tuple[float, float]:
        rad = math.radians(az_d)
        s = math.sin(rad)
        c = math.cos(rad)
        # Add subtle superellipse corner fullness in the rear so the dome wraps cleanly around KayKit's boxy head corners
        corner_boost = 1.0 + (0.095 * ((2.0 * s * c) ** 2) if c < 0.0 else 0.0)
        x = rx * s * corner_boost
        ry = ry_front if c >= 0.0 else ry_back * corner_boost
        y = y_shift - ry * c
        return x, y

    grid: list[list[Vector]] = [[Vector((0, 0, 0)) for _ in range(ncols)] for _ in range(nrows)]

    dome_levels = [
        (1.00, 0.752, 0.752, 0.742, 0.052, 0.062, 0.062, -0.012),
        (0.86, 0.712, 0.718, 0.698, 0.192, 0.232, 0.232, -0.018),
        (0.68, 0.632, 0.642, 0.612, 0.330, 0.382, 0.382, -0.024),
        (0.48, 0.522, 0.538, 0.498, 0.424, 0.480, 0.482, -0.030),
        (0.28, 0.392, 0.412, 0.362, 0.468, 0.528, 0.525, -0.032),
        (0.12, 0.272, 0.298, 0.225, 0.484, 0.548, 0.542, -0.035),
    ]

    for i, (_, zf, zs, zb, rx, ryf, ryb, ysh) in enumerate(dome_levels):
        for j, az in enumerate(az_deg):
            x, y = dome_xy(az, rx, ryf, ryb, ysh)
            rad = math.radians(az)
            c = math.cos(rad)
            s = math.sin(rad)
            if c >= 0:
                z = zf * (c * c) + zs * (s * s)
            else:
                z = zb * (c * c) + zs * (s * s)
            if j == 0:
                z -= 0.018
                y += 0.012
            elif j == ncols - 1:
                z -= 0.014
                y -= 0.012
            grid[i][j] = Vector((x, y, z))

    for i in range(1, 6):
        grid[i][1] = grid[i][1].lerp(grid[i][0].lerp(grid[i][2], 0.42), 0.48)
        grid[i][4] = grid[i][4].lerp(grid[i][3].lerp(grid[i][5], 0.50), 0.48)

    # Row i = 6: Brow boundary (centered behind SM_DK_GoldTrim and clearing rear head corners)
    brow_pts = {
        0: Vector((0.000, -0.574,  0.008)),
        1: Vector((0.075, -0.558,  0.075)),
        2: Vector((0.155, -0.524,  0.148)),
        3: Vector((0.198, -0.500,  0.165)),
        4: Vector((0.285, -0.436,  0.180)),
        5: Vector((0.365, -0.356,  0.186)),
        6: Vector((0.398, -0.312,  0.186)),
        7: Vector((0.476, -0.155,  0.148)),
        8: Vector((0.486,  0.045,  0.125)),
        9: Vector((0.468,  0.292,  0.102)),
        10: Vector((0.395, 0.418,  0.085)),
        11: Vector((0.205, 0.502,  0.072)),
        12: Vector((0.000, 0.518,  0.068)),
    }
    for j, pt in brow_pts.items():
        grid[6][j] = pt

    # Row i = 7: Mid-eye level
    row7_pts = {
        0: Vector((0.000, -0.570, -0.100)),
        1: Vector((0.075, -0.554, -0.065)),
        2: Vector((0.155, -0.520, -0.030)),
        3: Vector((0.198, -0.498, -0.040)),
        4: Vector((0.280, -0.432, -0.010)),
        5: Vector((0.352, -0.362,  0.020)),
        6: Vector((0.388, -0.320,  0.065)),
        7: Vector((0.468, -0.158,  0.032)),
        8: Vector((0.478,  0.042,  0.008)),
        9: Vector((0.462,  0.288, -0.014)),
        10: Vector((0.390, 0.412, -0.030)),
        11: Vector((0.202, 0.495, -0.042)),
        12: Vector((0.000, 0.512, -0.045)),
    }
    for j, pt in row7_pts.items():
        grid[7][j] = pt

    # Row i = 8: Cheekbone Diagonal (j=3..6) & Smooth Nape Waist Nadir (j=7..12)
    row8_pts = {
        0: Vector((0.000, -0.548, -0.158)),
        1: Vector((0.070, -0.532, -0.158)),
        2: Vector((0.155, -0.508, -0.158)),
        3: Vector((0.202, -0.492, -0.162)),
        4: Vector((0.268, -0.438, -0.112)),
        5: Vector((0.338, -0.372, -0.050)),
        6: Vector((0.382, -0.328, -0.014)),
        7: Vector((0.462, -0.158, -0.085)),
        8: Vector((0.472,  0.038, -0.135)),
        9: Vector((0.456,  0.282, -0.140)),
        10: Vector((0.384, 0.405, -0.144)),
        11: Vector((0.198, 0.488, -0.146)),
        12: Vector((0.000, 0.506, -0.148)),
    }
    for j, pt in row8_pts.items():
        grid[8][j] = pt

    # Enforce KayKit head clearance on rows 0..8 before interpolating the lower skirt
    shells.clear_host(grid[:9], ctx.host_bvh(FIT_HOST), 0.056, CLEAR_CENTER)

    # Rows i = 9, 10, 11, 12: Cheek Guard (j=3..6) & Smooth Bell-Curve Flared Neck Skirt (j=7..12)
    bottom_pts = {
        3:  Vector((0.148, -0.436, -0.532)),  # Inner bottom cheek tip
        4:  Vector((0.224, -0.382, -0.504)),
        5:  Vector((0.304, -0.312, -0.460)),
        6:  Vector((0.366, -0.252, -0.412)),
        7:  Vector((0.472, -0.118, -0.362)),  # Smooth S-curve bridge to side skirt
        8:  Vector((0.526,  0.038, -0.334)),  # Side skirt bell flare (matched to Side concept rim height)
        9:  Vector((0.512,  0.310, -0.326)),
        10: Vector((0.432,  0.465, -0.322)),
        11: Vector((0.225,  0.572, -0.320)),
        12: Vector((0.000,  0.598, -0.318)),  # Rear skirt bell flare
    }

    t_cheek = [0.16, 0.46, 0.76, 1.00]
    t_skirt = [0.26, 0.54, 0.78, 1.00]

    for r_offset in range(4):
        r_idx = 9 + r_offset
        tc = t_cheek[r_offset]
        ts = t_skirt[r_offset]
        jaw_curve = math.sin(tc * math.pi) * 0.012
        for j in range(0, 3):
            grid[r_idx][j] = Vector((0.0, -0.49, -0.160 - 0.372 * tc))
        for j in range(3, ncols):
            p_top = grid[8][j]
            p_bot = bottom_pts[j]
            if j <= 6:
                pt = p_top.lerp(p_bot, tc)
                pt.x += jaw_curve * (1.0 - 0.15 * (j - 3))
            else:
                flare_xy = ts ** 1.45
                pt = Vector((
                    p_top.x + (p_bot.x - p_top.x) * flare_xy,
                    p_top.y + (p_bot.y - p_top.y) * flare_xy,
                    p_top.z + (p_bot.z - p_top.z) * ts,
                ))
            grid[r_idx][j] = pt

    # Final clearance check across all rows
    shells.clear_host(grid, ctx.host_bvh(FIT_HOST), 0.054, CLEAR_CENTER)

    bm = bmesh.new()
    v_right = [[None for _ in range(ncols)] for _ in range(nrows)]
    v_left = [[None for _ in range(ncols)] for _ in range(nrows)]
    brow_cutout_verts: set[bmesh.types.BMVert] = set()

    for i in range(nrows):
        for j in range(ncols):
            if i >= 7 and j < 3:
                continue
            if i == 7 and 3 <= j < 6:
                continue
            co = grid[i][j]
            vr = bm.verts.new(co)
            v_right[i][j] = vr
            if i == 6 and j <= 6:
                brow_cutout_verts.add(vr)
            if j in (0, ncols - 1):
                v_left[i][j] = vr
            else:
                vl = bm.verts.new(Vector((-co.x, co.y, co.z)))
                v_left[i][j] = vl
                if i == 6 and j <= 6:
                    brow_cutout_verts.add(vl)

    f_right: dict[tuple[int, int], bmesh.types.BMFace] = {}
    f_left: dict[tuple[int, int], bmesh.types.BMFace] = {}

    for i in range(nrows - 1):
        for j in range(ncols - 1):
            if i >= 6 and j < 3:
                continue
            if 6 <= i < 8 and 3 <= j < 6:
                continue

            v00 = v_right[i][j]
            v10 = v_right[i + 1][j]
            v11 = v_right[i + 1][j + 1]
            v01 = v_right[i][j + 1]
            if v00 and v10 and v11 and v01:
                f_r = bm.faces.new((v00, v10, v11, v01))
                f_r.material_index = 0
                f_r.smooth = True
                f_right[(i, j)] = f_r

            l00 = v_left[i][j]
            l10 = v_left[i + 1][j]
            l11 = v_left[i + 1][j + 1]
            l01 = v_left[i][j + 1]
            if l00 and l10 and l11 and l01:
                f_l = bm.faces.new((l00, l01, l11, l10))
                f_l.material_index = 0
                f_l.smooth = True
                f_left[(i, j)] = f_l

    # Mark the 3 vertical dome flute edges (j=2, 3, 5 for i=0..6) explicitly sharp
    for i in range(0, 6):
        for j_flute in (2, 3, 5):
            for v_arr in (v_right, v_left):
                va, vb = v_arr[i][j_flute], v_arr[i + 1][j_flute]
                if va and vb:
                    e = bm.edges.get((va, vb))
                    if e:
                        e.smooth = False

    shells.solidify(bm, thickness=0.048, inner_index=1, center=SOLID_CENTER, level=brow_cutout_verts)

    # Create the 3 recessed vertical cheek ventilation slots on each cheek guard directly in BMesh
    for side_faces in (f_right, f_left):
        for j_slot in (3, 4, 5):
            pair = [side_faces[(9, j_slot)], side_faces[(10, j_slot)]]
            res_border = bmesh.ops.inset_region(
                bm,
                faces=pair,
                use_boundary=True,
                use_even_offset=True,
                thickness=0.019,
                depth=0.0,
            )
            for f_b in res_border.get("faces", []):
                f_b.material_index = 0
                f_b.smooth = True

            slot_verts_outer = {v for f in pair for v in f.verts}
            for e in bm.edges:
                if e.verts[0] in slot_verts_outer and e.verts[1] in slot_verts_outer:
                    if pair[0] in e.link_faces and pair[1] in e.link_faces:
                        e.smooth = True
                    else:
                        e.smooth = False

            res_pocket = bmesh.ops.inset_region(
                bm,
                faces=pair,
                use_boundary=True,
                use_even_offset=True,
                thickness=0.003,
                depth=-0.028,
            )
            for f_wall in res_pocket.get("faces", []):
                f_wall.material_index = 1
                f_wall.smooth = True
            for f_floor in pair:
                f_floor.material_index = 1
                f_floor.smooth = True

            slot_verts_inner = {v for f in pair for v in f.verts}
            for e in bm.edges:
                if e.verts[0] in slot_verts_inner and e.verts[1] in slot_verts_inner:
                    if pair[0] in e.link_faces and pair[1] in e.link_faces:
                        e.smooth = True
                    else:
                        e.smooth = False

    ctx.add("HelmetShell", bm, ["armor", "dark"], sharp_angle=32.0)

    # Build SM_DK_GoldTrim with uniform-width temple bar docking cleanly into SM_DK_EarMedallion
    inner_loop_r = [
        Vector((0.000, -0.608, -0.072)),  # 0: Center nose bottom V-point
        Vector((0.075, -0.588,  0.008)),  # 1: Central V-chevron inner
        Vector((0.155, -0.550,  0.088)),  # 2: Brow shoulder turn inner
        Vector((0.295, -0.448,  0.132)),  # 3: Horizontal-arched brow inner
        Vector((0.418, -0.285,  0.152)),  # 4: Top-outer corner of eye opening (aligned under medallion front rim!)
        Vector((0.402, -0.308,  0.068)),  # 5: Mid-outer temple edge
        Vector((0.376, -0.348, -0.008)),  # 6: Lower-outer corner of eye opening
        Vector((0.285, -0.438, -0.078)),  # 7: Cheekbone diagonal inner
        Vector((0.192, -0.514, -0.155)),  # 8: Inner cheek corner
        Vector((0.178, -0.490, -0.285)),  # 9: Upper-mid vertical cheek inner
        Vector((0.162, -0.464, -0.412)),  # 10: Lower-mid vertical cheek inner
        Vector((0.144, -0.438, -0.534)),  # 11: Bottom cheek tip inner
    ]

    outer_loop_r = [
        Vector((0.000, -0.606,  0.092)),  # 0: Center top V-notch overlapping bottom tip of gem bezel
        Vector((0.076, -0.588,  0.152)),  # 1: Parallels lower edge of hexagonal gem bezel
        Vector((0.154, -0.550,  0.214)),  # 2: Crisp brow shoulder corner outside gem bezel
        Vector((0.312, -0.432,  0.236)),  # 3: Broad horizontal-arched brow top edge
        Vector((0.478, -0.195,  0.246)),  # 4: Docks squarely into the front cylinder barrel of SM_DK_EarMedallion!
        Vector((0.466, -0.218,  0.125)),  # 5: Docks squarely into the lower-front barrel of SM_DK_EarMedallion!
        Vector((0.438, -0.282, -0.028)),  # 6: Lower temple outer corner
        Vector((0.348, -0.378, -0.120)),  # 7: Cheekbone diagonal outer
        Vector((0.258, -0.454, -0.198)),  # 8: Inner cheek corner outer
        Vector((0.244, -0.434, -0.315)),  # 9: Upper-mid vertical cheek outer
        Vector((0.226, -0.412, -0.425)),  # 10: Lower-mid vertical cheek outer
        Vector((0.208, -0.392, -0.528)),  # 11: Bottom cheek tip outer
    ]

    tbm = bmesh.new()
    n_trim = len(inner_loop_r)

    def make_trim_stations(sign_x: float) -> list[list[bmesh.types.BMVert]]:
        stations = []
        for k in range(n_trim):
            p_in = Vector((sign_x * inner_loop_r[k].x, inner_loop_r[k].y, inner_loop_r[k].z))
            p_out = Vector((sign_x * outer_loop_r[k].x, outer_loop_r[k].y, outer_loop_r[k].z))
            out_dir = (0.5 * (p_in + p_out) - Vector((0.0, -0.05, 0.02)))
            out_dir.z *= 0.25
            out_dir.normalize()
            p_back_in = p_in - out_dir * 0.042
            p_back_out = p_out - out_dir * 0.042
            p_front_in = p_in.lerp(p_out, 0.14) + out_dir * 0.028
            p_front_out = p_out.lerp(p_in, 0.14) + out_dir * 0.028
            if k == 0:
                p_back_in.x = 0.0
                p_back_out.x = 0.0
                p_front_in.x = 0.0
                p_front_out.x = 0.0
            stations.append([
                tbm.verts.new(p_back_in),
                tbm.verts.new(p_front_in),
                tbm.verts.new(p_front_out),
                tbm.verts.new(p_back_out),
            ])
        return stations

    r_stat = make_trim_stations(1.0)
    l_stat = make_trim_stations(-1.0)
    l_stat[0] = r_stat[0]

    solids.rail_loft(
        tbm, r_stat, flip=True, cap_start=False, cap_end=True, sharp_stations=(0, 2, 4, 6, 8, n_trim - 1)
    )
    solids.rail_loft(
        tbm, l_stat, flip=False, cap_start=False, cap_end=True, sharp_stations=(0, 2, 4, 6, 8, n_trim - 1)
    )

    ctx.add("GoldTrim", tbm, ["gold"], sharp_angle=None)


def gem_and_bezel(ctx):
    """The hexagonal gold bezel on the forehead and the faceted ruby set in it."""
    zc = 0.288
    yc = -0.592
    tilt = math.radians(13.5)
    rot = Matrix.Rotation(tilt, 4, "X")

    def to_world(p_local: Vector) -> Vector:
        pw = Vector((0.0, yc, zc)) + (rot @ p_local)
        pw.y += 0.14 * (pw.x * pw.x)
        return pw

    def hex_ring(rx: float, rz_top: float, rz_mid: float, rz_bot: float, y_off: float) -> list[Vector]:
        pts_local = [
            Vector((0.0, y_off,  rz_top)),
            Vector(( rx, y_off,  rz_mid)),
            Vector(( rx, y_off, -rz_mid)),
            Vector((0.0, y_off, -rz_bot)),
            Vector((-rx, y_off, -rz_mid)),
            Vector((-rx, y_off,  rz_mid)),
        ]
        return [to_world(p) for p in pts_local]

    bbm = bmesh.new()
    b_back_out = [bbm.verts.new(p) for p in hex_ring(0.148, 0.206, 0.096, 0.206,  0.082)]
    b_front_out = [bbm.verts.new(p) for p in hex_ring(0.138, 0.196, 0.092, 0.196, -0.024)]
    b_front_in = [bbm.verts.new(p) for p in hex_ring(0.098, 0.144, 0.066, 0.144, -0.032)]
    b_back_in = [bbm.verts.new(p) for p in hex_ring(0.094, 0.140, 0.064, 0.140,  0.020)]

    rings = [b_back_out, b_front_out, b_front_in, b_back_in]
    for k in range(6):
        kn = (k + 1) % 6
        for r_i in range(4):
            r_next = (r_i + 1) % 4
            f = bbm.faces.new((rings[r_i][k], rings[r_i][kn], rings[r_next][kn], rings[r_next][k]))
            f.smooth = True
    for e in bbm.edges:
        e.smooth = False

    ctx.add("GemBezel", bbm, ["gold"], sharp_angle=None)

    gbm = bmesh.new()
    g_back = [gbm.verts.new(p) for p in hex_ring(0.096, 0.142, 0.065, 0.142,  0.010)]
    g_girdle = [gbm.verts.new(p) for p in hex_ring(0.096, 0.142, 0.065, 0.142, -0.030)]
    g_table = [gbm.verts.new(p) for p in hex_ring(0.048, 0.082, 0.036, 0.082, -0.064)]
    v_ridge_top = gbm.verts.new(to_world(Vector((0.0, -0.078,  0.030))))
    v_ridge_bot = gbm.verts.new(to_world(Vector((0.0, -0.078, -0.030))))

    for k in range(6):
        kn = (k + 1) % 6
        gbm.faces.new((g_back[k], g_back[kn], g_girdle[kn], g_girdle[k]))
        gbm.faces.new((g_girdle[k], g_girdle[kn], g_table[kn], g_table[k]))

    gbm.faces.new((g_table[0], g_table[1], v_ridge_top))
    gbm.faces.new((g_table[1], g_table[2], v_ridge_bot, v_ridge_top))
    gbm.faces.new((g_table[2], g_table[3], v_ridge_bot))
    gbm.faces.new((g_table[3], g_table[4], v_ridge_bot))
    gbm.faces.new((g_table[4], g_table[5], v_ridge_top, v_ridge_bot))
    gbm.faces.new((g_table[5], g_table[0], v_ridge_top))
    gbm.faces.new(list(reversed(g_back)))
    for e in gbm.edges:
        e.smooth = False

    ctx.add("RubyGem", gbm, ["gem"], sharp_angle=None)


def central_crest(ctx):
    """The beveled crest over the crown, tapering down into the rear skirt flare."""
    stations = [
        (-0.535,  0.432,  0.348, 0.064, 0.030),
        (-0.485,  0.576,  0.448, 0.076, 0.040),
        (-0.375,  0.716,  0.568, 0.082, 0.044),
        (-0.215,  0.804,  0.648, 0.084, 0.045),
        (-0.040,  0.828,  0.672, 0.084, 0.045),
        ( 0.135,  0.794,  0.638, 0.082, 0.044),
        ( 0.295,  0.696,  0.548, 0.078, 0.042),
        ( 0.432,  0.532,  0.388, 0.074, 0.038),
        ( 0.515,  0.325,  0.195, 0.070, 0.036),
        ( 0.542,  0.095, -0.005, 0.064, 0.032),
        ( 0.535, -0.055, -0.105, 0.056, 0.028),
        ( 0.532, -0.155, -0.185, 0.046, 0.022),
        ( 0.565, -0.255, -0.268, 0.030, 0.014),
    ]

    cbm = bmesh.new()
    rings = []
    for y, z_out, z_in, wb_h, wt_h in stations:
        rad_dir = Vector((0.0, y + 0.02, z_out - 0.14)).normalized()
        p_tip = Vector((0.0, y, z_out))
        p_shld = p_tip - rad_dir * 0.020
        p_root = Vector((0.0, y, z_in)) - rad_dir * 0.042
        ring = [
            cbm.verts.new(Vector((-wb_h, p_root.y, p_root.z))),
            cbm.verts.new(Vector((-wb_h * 0.86, p_shld.y, p_shld.z))),
            cbm.verts.new(Vector((-wt_h, p_tip.y, p_tip.z))),
            cbm.verts.new(Vector(( wt_h, p_tip.y, p_tip.z))),
            cbm.verts.new(Vector(( wb_h * 0.86, p_shld.y, p_shld.z))),
            cbm.verts.new(Vector(( wb_h, p_root.y, p_root.z))),
        ]
        rings.append(ring)

    solids.rail_loft(cbm, rings, flip=False, cap_start=True, cap_end=True)
    ctx.add("CentralCrest", cbm, ["metal"], sharp_angle=None)


def medallions_and_wings(ctx):
    """Per side: a round ear medallion and a wing of four swept feather blades whose roots start inside the
    medallion's barrel, behind its raised rim, so no twisted collar shows."""
    for side_name, sx in (("R", 1.0), ("L", -1.0)):
        # 1. Circular Ear Medallion (centered at Y = -0.065, Z = +0.262)
        mbm = bmesh.new()
        yc_m, zc_m = -0.065, 0.262
        tilt_y = math.radians(-10.0 * sx)
        tilt_z = math.radians(7.0 * sx)
        rot_m = Matrix.Rotation(tilt_z, 4, "Z") @ Matrix.Rotation(tilt_y, 4, "Y")
        center_m = Vector((sx * 0.486, yc_m, zc_m))

        nseg = 32
        prof = [
            (-0.075, 0.150),
            ( 0.046, 0.154),
            ( 0.072, 0.138),
            ( 0.072, 0.098),
            ( 0.048, 0.088),
        ]
        m_rings = []
        for x_loc, rad in prof:
            ring = []
            for k in range(nseg):
                ang = 2.0 * math.pi * k / nseg
                p_loc = Vector((x_loc * sx, rad * math.cos(ang), rad * math.sin(ang)))
                ring.append(mbm.verts.new(center_m + (rot_m @ p_loc)))
            m_rings.append(ring)

        for r_i in range(len(m_rings) - 1):
            r0, r1 = m_rings[r_i], m_rings[r_i + 1]
            for k in range(nseg):
                kn = (k + 1) % nseg
                verts = (r0[k], r0[kn], r1[kn], r1[k]) if sx > 0 else (r0[k], r1[k], r1[kn], r0[kn])
                f = mbm.faces.new(verts)
                f.smooth = True

        ring_edges = set()
        for r in m_rings:
            for k in range(nseg):
                e = mbm.edges.get((r[k], r[(k + 1) % nseg]))
                if e:
                    ring_edges.add(e)

        cap_in = list(reversed(m_rings[0])) if sx > 0 else list(m_rings[0])
        cap_out = list(m_rings[-1]) if sx > 0 else list(reversed(m_rings[-1]))
        mbm.faces.new(cap_in).smooth = True
        mbm.faces.new(cap_out).smooth = True

        for e in mbm.edges:
            e.smooth = (e not in ring_edges)

        ctx.add(f"EarMedallion{side_name}", mbm, ["metal"], sharp_angle=None)

        # 2. Unified 4-Tier Sculpted Wing (SM_DK_Wing_L/R)
        # Each feather blade starts strictly inside the medallion barrel (X in [0.460, 0.500], well behind the raised bezel ring at X=0.532..0.558)
        # and emerges radially upward/backward without any twisted collar tube.
        wbm = bmesh.new()

        def add_feather_blade(stations: list[tuple[Vector, Vector, float, float]]) -> None:
            f_rings = []
            for p_lead, p_trail, t_lead, t_trail in stations:
                n_dir = Vector((sx * 0.56, -0.81, 0.16)).normalized()
                p_ridge = p_lead.lerp(p_trail, 0.28)
                p_bev_trail = p_lead.lerp(p_trail, 0.85)

                ring = [
                    wbm.verts.new(p_lead + n_dir * (0.20 * t_lead)),
                    wbm.verts.new(p_ridge + n_dir * t_lead),
                    wbm.verts.new(p_bev_trail + n_dir * t_trail),
                    wbm.verts.new(p_trail),
                    wbm.verts.new(p_bev_trail - n_dir * (0.85 * t_trail)),
                    wbm.verts.new(p_ridge - n_dir * (0.85 * t_lead)),
                ]
                f_rings.append(ring)

            solids.rail_loft(wbm, f_rings, flip=(sx < 0), cap_start=True, cap_end=True)

        # Feather 1 (Primary tall blade — swept backward in +Y to align with Side concept, tucked in X for Front/TQ)
        f1_stations = [
            (Vector((sx * 0.462, -0.115, 0.340)), Vector((sx * 0.495,  0.025, 0.310)), 0.024, 0.016),
            (Vector((sx * 0.478, -0.075, 0.505)), Vector((sx * 0.605,  0.145, 0.475)), 0.044, 0.022),
            (Vector((sx * 0.505,  0.015, 0.705)), Vector((sx * 0.695,  0.275, 0.675)), 0.046, 0.022),
            (Vector((sx * 0.548,  0.135, 0.905)), Vector((sx * 0.755,  0.405, 0.895)), 0.044, 0.020),
            (Vector((sx * 0.598,  0.265, 1.055)), Vector((sx * 0.768,  0.455, 1.015)), 0.038, 0.016),
            (Vector((sx * 0.635,  0.345, 1.135)), Vector((sx * 0.752,  0.448, 1.105)), 0.028, 0.014),
            (Vector((sx * 0.658,  0.382, 1.152)), Vector((sx * 0.718,  0.422, 1.142)), 0.018, 0.010),
        ]
        add_feather_blade(f1_stations)

        # Feather 2 (Second feather tier)
        f2_stations = [
            (Vector((sx * 0.465, -0.035, 0.305)), Vector((sx * 0.496,  0.075, 0.275)), 0.022, 0.014),
            (Vector((sx * 0.525,  0.075, 0.505)), Vector((sx * 0.668,  0.265, 0.455)), 0.040, 0.018),
            (Vector((sx * 0.588,  0.195, 0.700)), Vector((sx * 0.758,  0.425, 0.680)), 0.038, 0.018),
            (Vector((sx * 0.632,  0.285, 0.835)), Vector((sx * 0.772,  0.452, 0.815)), 0.032, 0.014),
            (Vector((sx * 0.658,  0.335, 0.865)), Vector((sx * 0.752,  0.435, 0.848)), 0.020, 0.010),
        ]
        add_feather_blade(f2_stations)

        # Feather 3 (Third feather tier)
        f3_stations = [
            (Vector((sx * 0.468,  0.005, 0.260)), Vector((sx * 0.496,  0.095, 0.235)), 0.020, 0.014),
            (Vector((sx * 0.535,  0.105, 0.405)), Vector((sx * 0.668,  0.305, 0.365)), 0.036, 0.016),
            (Vector((sx * 0.588,  0.195, 0.530)), Vector((sx * 0.718,  0.385, 0.480)), 0.032, 0.015),
            (Vector((sx * 0.622,  0.255, 0.595)), Vector((sx * 0.735,  0.412, 0.572)), 0.024, 0.012),
        ]
        add_feather_blade(f3_stations)

        # Feather 4 (Lowest feather tier — starts inside barrel at Z=0.215 and sweeps cleanly upward/backward)
        f4_stations = [
            (Vector((sx * 0.468,  0.025, 0.215)), Vector((sx * 0.496,  0.095, 0.198)), 0.018, 0.012),
            (Vector((sx * 0.525,  0.115, 0.295)), Vector((sx * 0.648,  0.305, 0.255)), 0.032, 0.015),
            (Vector((sx * 0.562,  0.175, 0.372)), Vector((sx * 0.678,  0.358, 0.352)), 0.022, 0.012),
        ]
        add_feather_blade(f4_stations)

        ctx.add(f"Wing{side_name}", wbm, ["metal"], sharp_angle=None)


def build(ctx):
    ctx.origin = HEAD
    helmet_shell_and_trim(ctx)
    gem_and_bezel(ctx)
    central_crest(ctx)
    medallions_and_wings(ctx)
