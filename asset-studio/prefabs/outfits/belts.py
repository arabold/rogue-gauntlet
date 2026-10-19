"""
Belts: a band around the waist with a buckle at the front, in variants (`VARIANTS`). Built around a host's
cross-sections (`build(host, y, ...)`), so the band follows the waist's taper and sits flush on it.

Material slots: `band` (the strap, usually leather), `buckle` (metal, gold or bone), plus `stud`, `gem` and `trim`.
A variant maps them to `kit.SLOTS` names; the monster build recolors them from its drawing.
"""

import numpy as np

from core import kit

FRONT = np.pi / 2  # angle of the front (+z) on a host ring


def _band(host, y, height, thickness, slot, sides=32, swell=0.0):
    """The leather band: rounded edges, and with `swell` taller at the front (stylized's barbarian belt)."""
    profile=None
    if getattr(host,"conforming",False):
        # Sample the tunic's taper through the band's height, including its wider front.
        c=.6*thickness
        dt=min(c/height,.45)
        profile=[(0.,-.01),(0.,thickness-c)]
        profile += [(t,thickness) for t in np.linspace(dt,1-dt,9)]
        profile += [(1.,thickness-c),(1.,-.01)]
    return kit.host_band(host, y - height / 2, y + height / 2, thickness, slot, bevel=0.6, sides=sides,
                         swell=swell * height,profile=profile)


def _fur_edges(host, y, height, thickness, swell, slot, count=26, length=0.055, top=True, bottom=True):
    """Fur teeth along the band's top edge pointing up and its bottom edge pointing down, rooted on the band's
    surface so no gap opens between them, following its `swell` toward the front."""
    out = []
    cx, a, cz, b = (float(v[0]) for v in host.section([y]))
    w = np.pi * (3 * (a + b) - np.sqrt((3 * a + b) * (a + 3 * b))) / count
    for k in range(count):
        theta = 2 * np.pi * (k + 0.5) / count
        grow = swell * max(np.cos(theta - FRONT), 0.0) ** 2
        for sgn in [sign for sign,enabled in ((1,top),(-1,bottom)) if enabled]:
            edge = y + sgn * (height / 2 + grow / 2 - 0.012)
            tooth = kit.prism([(-0.55 * w, 0.0), (0.55 * w, 0.0), (0.0, sgn * length)][::sgn], 0.022, slot,
                              bevel=0.004)
            out.append(tooth.moved(_at(host, edge, theta, thickness - 0.012)))
    return out


def _fur_lining(host,y,height,thickness,swell,slot,count=20,length=.027,sign=-1):
    """A continuous pelt backing tucked into either leather edge, with broad uneven tufts.

    Its entire root loop overlaps the corresponding leather bevel. Solid union removes the
    hidden interface so the visible fringe is part of the belt's exterior surface.
    """
    from prefabs.details import fur
    samples=count*4
    theta=np.arange(samples)*2*np.pi/samples
    grow=swell*height*np.clip(np.sin(theta),0,1)**2
    root_y=y+sign*(height*.5+grow*.5-.007)
    path=kit.ring_points(host,root_y,thickness-.012,samples,theta)
    tangent=np.roll(path,-1,axis=0)-np.roll(path,1,axis=0)
    outward=np.column_stack([tangent[:,2],np.zeros(samples),-tangent[:,0]])
    return fur.edge(path,outward,fall=(0,sign,0),length=length,thickness=.018,
                    slot=slot,variation=.004)


def _at(host, y, theta, out):
    return kit.band_frame(host, y, theta, out)


def frame_buckle(height, slot):
    """Bold closed frame and attached tongue; one solid avoids coincident corner bars."""
    import manifold3d as mf
    from items import solid
    w, h, d, bar = height*1.3, height*1.15, max(.018,height*.24), height*.20
    body = solid.box((w,h,d),(0,0,d/2),bevel=min(.004,height*.10))
    hole = mf.Manifold.cube((w-2*bar,h-2*bar,d*3),True).translate((0,0,d/2))
    assembly = solid.Assembly().add(body,slot)
    assembly.parts = [assembly.parts[0]-hole]
    assembly.add(solid.box((w*.62,bar*.35,d*.6),(-w*.20,0,d*.65),bevel=min(.002,height*.05)),slot)
    return assembly.build()


def plate_buckle(height, slot, rivet_slot):
    """A big chamfered plate with a rivet in each corner."""
    w, h, d = 1.45 * height, 1.25 * height, 0.04
    plate = kit.bevel_box((w, h, d), 0.012, slot, center=(0, 0, d / 2))
    rivets = [kit.stud(0.11 * height, 0.012, rivet_slot).moved(kit.Frame((sx * 0.36 * w, sy * 0.34 * h, d),
                                                                           [1, 0, 0], [0, 1, 0], [0, 0, 1]))
              for sx in (-1, 1) for sy in (-1, 1)]
    return kit.Piece.merge(plate, *rivets)


def _chiseled_incisor(w, L, d, slot="bone"):
    """A crisp, faceted low-poly chiseled tooth with sharp planar faces."""
    w_top, w_bot = 0.5 * w, 0.44 * w
    d_top = 0.5 * d
    verts = np.array([
        [-w_top, 0.0, d_top],
        [ w_top, 0.0, d_top],
        [ w_top, 0.0, -d_top],
        [-w_top, 0.0, -d_top],
        [-w_bot, -L, 0.05 * d],
        [ w_bot, -L, 0.05 * d],
    ])
    faces = np.array([
        [0, 1, 5], [0, 5, 4],  # front
        [2, 3, 4], [2, 4, 5],  # back
        [0, 4, 3],             # left
        [1, 2, 5],             # right
        [0, 3, 2], [0, 2, 1],  # top cap
    ])
    return kit.Piece(verts, kit._orient_closed(verts, faces), slot)


def _chiseled_fang(w, L, d, slot="bone", tip_tilt=0.15):
    """A crisp, sharp faceted low-poly canine/fang."""
    w_top, d_top = 0.5 * w, 0.5 * d
    verts = np.array([
        [-w_top, 0.0, d_top],
        [ w_top, 0.0, d_top],
        [ w_top, 0.0, -d_top],
        [-w_top, 0.0, -d_top],
        [tip_tilt * w, -L, 0.0],
    ])
    faces = np.array([
        [0, 1, 4],  # front
        [1, 2, 4],  # right
        [2, 3, 4],  # back
        [3, 0, 4],  # left
        [0, 3, 2], [0, 2, 1],  # top cap
    ])
    return kit.Piece(verts, kit._orient_closed(verts, faces), slot)


def skull(height, slot="bone", eye_slot="dark", patina_slot="bone_dark"):
    """
    A stylized skull boss designed for armor and belt mounts.
    Features an organic continuous curved dental band with chiseled teeth, inset eye sockets,
    nasal aperture, suture patina, and a solid planar back suitable for sinking into armor and belts.
    """
    s = 1.35 * height
    n_pts = 16
    t = np.linspace(0, 2 * np.pi, n_pts, endpoint=False)
    ct, st = np.cos(t), np.sin(t)

    # Rings for cranium and maxilla:
    # Front profile (st > 0) has bold brow, cheekbones, and upper snout.
    # Back profile (st <= 0) extends back to a solid base (z ~ 0.10*s to 0.20*s)
    # so that when mounted/sunk into armor or belts, the skull merges seamlessly
    # with the host surface with ZERO air gap.
    rings = []

    # 0. Crown dome top
    r0 = 0.25 * s
    rings.append(np.column_stack([0.85 * r0 * ct, np.full(n_pts, 0.54 * s), np.where(st > 0, 0.72 * r0 * st, 0.50 * r0 * st)]))

    # 1. Upper Forehead (tapered dome)
    r1 = 0.42 * s
    rings.append(np.column_stack([0.88 * r1 * ct, np.full(n_pts, 0.36 * s), np.where(st > 0, 0.80 * r1 * st, 0.50 * r1 * st)]))

    # 2. Forehead & Brow ridge (forward ledge at front, broad at temples)
    zf2 = np.where(st > 0, 0.44 * s * st, 0.20 * s * st)
    rings.append(np.column_stack([0.46 * s * ct, np.full(n_pts, 0.16 * s), zf2]))

    # 3. Cheekbones (bold flare at sides ct ~ +-1, sunken under eyes)
    xf3 = 0.48 * s * ct
    zf3 = np.where(st > 0, 0.38 * s * st, 0.18 * s * st)
    rings.append(np.column_stack([xf3, np.full(n_pts, -0.04 * s), zf3]))

    # 4. Maxilla bone (upper jaw snout, terminating cleanly above teeth)
    xf4 = np.where(st > 0, 0.31 * s * ct, 0.30 * s * ct)
    zf4 = np.where(st > 0, 0.38 * s * st + 0.02 * s, 0.16 * s * st)
    rings.append(np.column_stack([xf4, np.full(n_pts, -0.16 * s), zf4]))

    # 5. Palate shelf closing under the maxilla
    xf5 = np.where(st > 0, 0.24 * s * ct, 0.20 * s * ct)
    zf5 = np.where(st > 0, 0.24 * s * st, 0.10 * s * st)
    rings.append(np.column_stack([xf5, np.full(n_pts, -0.18 * s), zf5]))

    verts, faces = kit._loft(rings)
    verts, faces = kit._cap_loops(verts, faces, [list(range(n_pts)), list(range((len(rings) - 1) * n_pts, len(rings) * n_pts))],
                                  centers=[[0, 0.58 * s, 0], [0, -0.19 * s, 0.10 * s]])
    base_mesh = kit.Piece(verts, kit._orient_closed(verts, faces), slot)

    # Eyes & Nose: angled stylized ovals and triangular nasal cavity
    t_eye = np.linspace(0, 2 * np.pi, 12, endpoint=False)
    we, he = 0.11 * s, 0.13 * s
    R_eye = kit.rotation([0, 0, 1], 14.0)
    e_pts = np.column_stack([we * np.cos(t_eye), he * np.sin(t_eye), np.zeros(len(t_eye))])
    left_eye = (e_pts @ R_eye.T)[:, :2] + [-0.165 * s, 0.08 * s]
    right_eye = (e_pts @ R_eye)[:, :2] * [-1, 1] + [0.165 * s, 0.08 * s]
    nose = np.array([[0.0, 0.01 * s], [-0.055 * s, -0.09 * s], [0.055 * s, -0.09 * s]])

    # Patina: aged suture crack across crown and dark gumline across maxilla
    suture = np.array([
        [-0.012 * s, 0.56 * s], [0.012 * s, 0.56 * s],
        [0.018 * s, 0.26 * s], [-0.008 * s, 0.26 * s]
    ])
    gumline = np.array([
        [-0.26 * s, -0.13 * s], [0.26 * s, -0.13 * s],
        [0.26 * s, -0.18 * s], [-0.26 * s, -0.18 * s]
    ])

    carved = kit.paint_cut(base_mesh, [suture, gumline], patina_slot, {slot})
    carved = kit.paint_cut(carved, [left_eye, right_eye, nose], eye_slot, {slot, patina_slot})

    V = carved.verts.copy()
    F = carved.faces
    S = carved.slots
    cut_mask = (S == eye_slot)
    cut_vert_indices = np.unique(F[cut_mask].ravel())
    V[cut_vert_indices, 2] -= 0.095 * s
    cranium_piece = kit.Piece(V, F, S)

    # Dental arch: continuous organic curved horseshoe band embedded up into maxilla
    n_pts_t = 17
    u = np.linspace(-np.pi * 0.42, np.pi * 0.42, n_pts_t)
    su, cu = np.sin(u), np.cos(u)

    y_tips = np.zeros(n_pts_t)
    for i, ui in enumerate(u):
        if ui < -np.pi * 0.21:
            t_rel = (ui - (-np.pi * 0.42)) / (np.pi * 0.21)
            y_tips[i] = -0.26 * s - 0.07 * s * np.sin(t_rel * np.pi)
        elif ui < 0.0:
            t_rel = (ui - (-np.pi * 0.21)) / (np.pi * 0.21)
            y_tips[i] = -0.285 * s - 0.015 * s * t_rel
        elif ui < np.pi * 0.21:
            t_rel = ui / (np.pi * 0.21)
            y_tips[i] = -0.295 * s + 0.02 * s * t_rel
        else:
            t_rel = (ui - np.pi * 0.21) / (np.pi * 0.21)
            y_tips[i] = -0.26 * s - 0.075 * s * np.sin(t_rel * np.pi)

    # Front outer face lofts through organic rounded convex belly
    r0 = np.column_stack([0.28 * s * su, np.full(n_pts_t, -0.13 * s), 0.37 * s * cu])
    r1 = np.column_stack([0.29 * s * su, np.full(n_pts_t, -0.17 * s), 0.39 * s * cu])
    r2 = np.column_stack([0.285 * s * su, np.full(n_pts_t, -0.23 * s), 0.40 * s * cu])
    r3 = np.column_stack([0.28 * s * su, y_tips, 0.385 * s * cu])

    verts_f, faces_f = kit._loft([r0, r1, r2, r3])

    # Back of teeth and solid backing extending into oral cavity and mounting plane
    r3_b = np.column_stack([0.26 * s * su, y_tips, 0.20 * s * cu])
    r2_b = np.column_stack([0.26 * s * su, np.full(n_pts_t, -0.23 * s), 0.18 * s * cu])
    r1_b = np.column_stack([0.26 * s * su, np.full(n_pts_t, -0.17 * s), 0.16 * s * cu])
    r0_b = np.column_stack([0.25 * s * su, np.full(n_pts_t, -0.13 * s), 0.15 * s * cu])

    verts_b, faces_b = kit._loft([r3_b, r2_b, r1_b, r0_b])

    nvf = len(verts_f)
    all_v = np.vstack([verts_f, verts_b])
    all_f = list(faces_f) + [f + nvf for f in faces_b]

    # Bottom biting edge caps
    for j in range(n_pts_t - 1):
        f0, f1 = 3 * n_pts_t + j, 3 * n_pts_t + j + 1
        b0, b1 = nvf + j, nvf + j + 1
        all_f.append([f0, f1, b1])
        all_f.append([f0, b1, b0])
    # Top root caps embedded inside maxilla
    for j in range(n_pts_t - 1):
        f0, f1 = j, j + 1
        b0, b1 = nvf + 3 * n_pts_t + j, nvf + 3 * n_pts_t + j + 1
        all_f.append([f1, f0, b0])
        all_f.append([f1, b0, b1])
    # Side caps
    for k in range(3):
        f0, f1 = k * n_pts_t, (k + 1) * n_pts_t
        b0, b1 = nvf + (3 - k) * n_pts_t, nvf + (2 - k) * n_pts_t
        all_f.append([f0, b0, b1])
        all_f.append([f0, b1, f1])
    for k in range(3):
        f0, f1 = k * n_pts_t + (n_pts_t - 1), (k + 1) * n_pts_t + (n_pts_t - 1)
        b0, b1 = nvf + (3 - k) * n_pts_t + (n_pts_t - 1), nvf + (2 - k) * n_pts_t + (n_pts_t - 1)
        all_f.append([f0, f1, b1])
        all_f.append([f0, b1, b0])

    band = kit.Piece(all_v, kit._orient_closed(all_v, np.array(all_f)), slot)

    # Carve 3D grooves separating the 4 teeth
    notch_polys = []
    for split_idx in [4, 8, 12]:
        xc_n = 0.285 * s * su[split_idx]
        w_n = 0.016 * s
        poly = np.array([
            [xc_n - w_n, -0.16 * s],
            [xc_n + w_n, -0.16 * s],
            [xc_n + w_n, -0.32 * s],
            [xc_n - w_n, -0.32 * s]
        ])
        notch_polys.append(poly)

    band_carved = kit.paint_cut(band, notch_polys, eye_slot, {slot})
    V_b = band_carved.verts.copy()
    F_b = band_carved.faces
    S_b = band_carved.slots
    cut_mask = (S_b == eye_slot)
    cut_verts = np.unique(F_b[cut_mask].ravel())
    V_b[cut_verts, 2] -= 0.035 * s
    band_piece = kit.Piece(V_b, F_b, S_b)

    # Recessed oral cavity backing behind teeth
    cav_pts = np.array([
        [-0.20 * s, -0.16 * s, 0.28 * s],
        [ 0.20 * s, -0.16 * s, 0.28 * s],
        [ 0.16 * s, -0.28 * s, 0.28 * s],
        [-0.16 * s, -0.28 * s, 0.28 * s],
        [-0.18 * s, -0.16 * s, 0.08 * s],
        [ 0.18 * s, -0.16 * s, 0.08 * s],
        [ 0.14 * s, -0.28 * s, 0.08 * s],
        [-0.14 * s, -0.28 * s, 0.08 * s],
    ])
    cav_faces = np.array([
        [0, 1, 2], [0, 2, 3],
        [4, 5, 1], [4, 1, 0],
        [7, 3, 2], [7, 2, 6],
        [4, 0, 3], [4, 3, 7],
        [1, 5, 6], [1, 6, 2],
        [5, 4, 7], [5, 7, 6],
    ])
    cavity = kit.Piece(cav_pts, cav_faces, eye_slot)

    merged = kit.Piece.merge(cranium_piece, band_piece, cavity)
    mv = merged.verts.copy()
    # Normalize Z so the base plane sits at z = 0
    mv[:, 2] -= mv[:, 2].min()
    return kit.Piece(mv, merged.faces, merged.slots)


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


def build(host, y, height=0.13, thickness=0.04, variant="leather", **overrides):
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
        band_p = _band(host, y, height, thickness, v["band"],
                       sides=v.get("fur_count",20)*4 if v.get("fur_style")=="lining" else 32,
                       swell=v.get("swell", 0.0))
        d_cnt, d_scale = kit.parse_dents(v.get("dents"), max_allowed=2)
        if v.get("patina") or d_cnt:
            from core.export import corner_normals
            b_slot = v["band"]
            band_p.normals = corner_normals(band_p.verts, band_p.faces, crease=55.0)
            if v.get("patina"):
                band_p = kit.weather(band_p, b_slot, light_slot=f"{b_slot}_light", worn_slot=f"{b_slot}_worn",
                                     dark_slot=f"{b_slot}_dark", rust_slot="grime", seed=53)
            if d_cnt >= 1:
                f_bn = _at(host, y, FRONT + 0.58, thickness).rotated(24.0, "z")
                band_p = kit.gash(band_p, f_bn, length=0.90 * height * d_scale, width=0.28 * height * d_scale,
                                  depth=0.50 * thickness * d_scale, slot=b_slot, shadow_slot=f"{b_slot}_dark")
            if d_cnt >= 2:
                f_bn2 = _at(host, y, FRONT - 0.58, thickness).rotated(-24.0, "z")
                band_p = kit.gash(band_p, f_bn2, length=0.80 * height * d_scale, width=0.25 * height * d_scale,
                                  depth=0.45 * thickness * d_scale, slot=b_slot, shadow_slot=f"{b_slot}_dark")
        parts.append(band_p)
    if v.get("trim"):  # thin rims along both edges
        for dy in (-0.5, 0.5):
            parts.append(kit.host_band(host, y + dy * height - 0.01, y + dy * height + 0.01, thickness + 0.006,
                                       v["trim"], bevel=0.3, sides=20))
    if v.get("chain"):
        parts.append(_chain(host, y, height, v["chain"]))
    front = _at(host, y, FRONT, thickness)
    buckle, big = v.get("buckle"), height * v.get("buckle_scale", 1.0)
    if buckle == "frame":
        bk = frame_buckle(big, v["buckle_slot"])
        if v.get("patina"):
            bs = v["buckle_slot"]
            bk = kit.weather(bk, bs, light_slot=f"{bs}_light", worn_slot=f"{bs}_worn", dark_slot=f"{bs}_dark",
                             rust_slot="tarnish", seed=59)
        parts.append(bk.moved(front))
    elif buckle == "plate":
        bk = plate_buckle(big, v["buckle_slot"], "dark")
        if v.get("patina"):
            bs = v["buckle_slot"]
            bk = kit.weather(bk, bs, light_slot=f"{bs}_light", worn_slot=f"{bs}_worn", dark_slot=f"{bs}_dark",
                             rust_slot="tarnish", seed=61)
        parts.append(bk.moved(front))
    elif buckle == "skull":
        parts.append(skull(big, v["buckle_slot"]).moved(_at(host, y, FRONT, thickness - 0.20 * big)))
    elif buckle == "boss":  # stylized's barbarian: a big octagonal steel boss in a dark ring, stepped up to its middle
        parts.append(kit.Piece.merge(kit.stud(0.62 * big, 0.03, v.get("boss_rim", "dark"), sides=8),
                                     kit.stud(0.54 * big, 0.055, v["buckle_slot"], sides=8),
                                     kit.stud(0.3 * big, 0.08, v["buckle_slot"], sides=8)).moved(front))
    elif buckle == "scarab":
        parts.append(scarab(big, v["buckle_slot"], "gold").moved(front))
    elif buckle == "knot":
        parts.append(sash_knot(height, v["band"]).moved(_at(host, y, FRONT - 0.7, thickness)))
    if v.get("studs"):
        parts += _studs(host, y, thickness, v.get("stud_slot", "metal"), v["studs"], radius=0.026)
    if v.get("fur"):
        if v.get("fur_style")=="lining":
            from prefabs.details import fur
            linings=[_fur_lining(host,y,height,thickness,v.get("swell",0),v["fur"],
                                count=v.get("fur_count",20),length=v.get("fur_length",.027),sign=sign)
                     for sign,enabled in ((1,v.get("fur_top",True)),(-1,v.get("fur_bottom",True)))
                     if enabled]
            # Replace the band itself; retain separate hardware's existing material/weight rules.
            parts[0]=fur.attach(parts[0],*linings)
        else:
            parts += _fur_edges(host, y, height, thickness, v.get("swell", 0.0) * height, v["fur"],
                                count=v.get("fur_count",26),length=v.get("fur_length",.055),
                                top=v.get("fur_top",True),bottom=v.get("fur_bottom",True))
    if v.get("pouches"):
        for theta in (FRONT - 0.8, FRONT + 0.8):
            parts.append(pouch(height, "leather", "dark", "gold").moved(_at(host, y, theta, thickness)))
    return kit.Piece.merge(*parts)



def build_fitted(host, supports, y, height=.13, thickness=.04, variant="leather",
                 fit_slot=None, **overrides):
    """Build a belt against actual armor triangles, with integrated fur when requested.

    Fit to the completed load-bearing garment, excluding decorative straps that
    tuck beneath the belt. Select a material slot when only that surface supports
    the band. Sparse/open armor requires an authored continuous backing first.
    """
    from prefabs.outfits.clothes import SurfaceFitted
    supports=list(supports)
    if fit_slot is not None:
        supports=[kit.Piece(p.verts,p.faces[p.slots==fit_slot],fit_slot)
                  for p in supports if np.any(p.slots==fit_slot)]
    if not supports:
        raise ValueError("Fitted belt requires supporting armor faces")
    overrides.setdefault("fur_style","lining")
    return build(SurfaceFitted(host,supports),y,height,thickness,variant,**overrides)

def catalog():
    """Every variant on the mannequin waist (`catalog.WAIST`)."""
    from core.catalog import WAIST
    host = WAIST.mesh(0.62, 1.25)
    for name in VARIANTS:
        yield name, build(WAIST, 0.9, variant=name), host, {"fur_trim": (110, 86, 70)}
