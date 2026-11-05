"""Procedural spider web assets: triangular corner pieces and trapezoid wall pieces.

Matching the KayKit dungeon aesthetic: stylized, chunky 3D polygonal silk strands
with catenary sagging arcs and droplet nodes. Authored with flat back on z = 0
at natural hanging height on the 4-unit dungeon walls, wearing the world's
white silk swatch from dungeon_texture.png. Non-blocking decorative props.
"""

import numpy as np
import manifold3d as mf
from items.solid import Assembly


def _catenary_points(p0, p1, sag, center_pt, num_points=5):
    """Interpolate points along a sagging catenary/parabolic curve between p0 and p1 toward center_pt."""
    t = np.linspace(0, 1, num_points)[:, None]
    straight = (1 - t) * p0 + t * p1
    mid = (p0 + p1) / 2
    to_center = center_pt - mid
    norm = np.linalg.norm(to_center)
    if norm > 1e-6:
        sag_dir = to_center / norm
    else:
        d = p1 - p0
        sag_dir = np.array([-d[1], d[0]]) / max(np.linalg.norm(d), 1e-6)
    dip = 4 * t * (1 - t) * sag
    return straight + dip * sag_dir


def _make_ribbon(p0, p1, width):
    """A 2D rectangle ribbon between p0 and p1 with specified width."""
    d = p1 - p0
    length = np.linalg.norm(d)
    if length < 1e-6:
        return None
    n = np.array([-d[1], d[0]]) * (width / (2.0 * length))
    quad = np.array([p0 - n, p1 - n, p1 + n, p0 + n])
    return mf.CrossSection([quad])


def _cylinder_between(p0, p1, radius, extend=0.003):
    """Solid cylinder between two 3D points p0 and p1, slightly extended to fuse into node spheres."""
    p0 = np.array(p0, dtype=float)
    p1 = np.array(p1, dtype=float)
    v = p1 - p0
    length = np.linalg.norm(v)
    if length < 1e-4:
        return None
    uz = v / length
    if abs(uz[0]) < 0.9:
        ux = np.cross([1.0, 0.0, 0.0], uz)
    else:
        ux = np.cross([0.0, 1.0, 0.0], uz)
    ux /= np.linalg.norm(ux)
    uy = np.cross(uz, ux)

    p_start = p0 - uz * extend
    total_len = length + 2 * extend

    mat = np.column_stack([ux, uy, uz, p_start])
    cyl = mf.Manifold.cylinder(total_len, radius, radius, 8)
    return cyl.transform(mat)


def _build_corner_web_solid(radius, drop, num_spokes, num_rings, strand_r, y_top=3.35, sag_ratio=0.15):
    """Build a 3D corner spiderweb manifold solid fitting a 90-degree corner vertex.

    The apex sits in the 90-degree corner at (0, y_top - drop, 0).
    Spoke 0 runs along the left wall (-45 degrees from local +Z), rising to y_top.
    Spoke N-1 runs along the right wall (+45 degrees from local +Z), rising to y_top.
    Inner spokes fan out in 3D into the room (+Z), rising upward toward the ceiling,
    facing the wide net upward toward the overhead gameplay camera.
    """
    parts = []
    y_apex = y_top - drop
    apex = np.array([0.0, y_apex, 0.0])

    angles = np.linspace(-np.pi / 4, np.pi / 4, num_spokes)
    spoke_tips = []
    for ang in angles:
        x = radius * np.sin(ang)
        z = radius * np.cos(ang)
        # Rim reaches y_top at the walls, with gentle catenary droop in the center
        y = y_top - drop * 0.20 * np.cos(2 * ang)
        spoke_tips.append(np.array([x, y, z]))

    grid = []
    ring_fractions = [(k / (num_rings + 1)) ** 1.15 for k in range(1, num_rings + 1)]
    for i in range(num_spokes):
        col = [apex]
        tip = spoke_tips[i]
        for f in ring_fractions:
            col.append(apex + f * (tip - apex))
        col.append(tip)
        grid.append(col)
        for k in range(len(col) - 1):
            r = strand_r * 1.35 if (i == 0 or i == num_spokes - 1) else strand_r
            cyl = _cylinder_between(col[k], col[k + 1], r)
            if cyl:
                parts.append(cyl)

    for k in range(1, num_rings + 1):
        for i in range(num_spokes - 1):
            p0 = grid[i][k]
            p1 = grid[i + 1][k]
            mid = 0.5 * (p0 + p1) + np.array([0, -sag_ratio * np.linalg.norm(p1 - p0), 0])
            cyl1 = _cylinder_between(p0, mid, strand_r * 0.85)
            cyl2 = _cylinder_between(mid, p1, strand_r * 0.85)
            if cyl1:
                parts.append(cyl1)
            if cyl2:
                parts.append(cyl2)
            parts.append(mf.Manifold.sphere(strand_r * 1.15, 6).translate(tuple(mid)))

    for i in range(num_spokes - 1):
        p0 = spoke_tips[i]
        p1 = spoke_tips[i + 1]
        mid = 0.5 * (p0 + p1) + np.array([0, -drop * 0.12, 0])
        cyl1 = _cylinder_between(p0, mid, strand_r * 1.15)
        cyl2 = _cylinder_between(mid, p1, strand_r * 1.15)
        if cyl1:
            parts.append(cyl1)
        if cyl2:
            parts.append(cyl2)
        parts.append(mf.Manifold.sphere(strand_r * 1.35, 6).translate(tuple(mid)))

    for i in range(num_spokes):
        for k in range(1, num_rings + 2):
            parts.append(mf.Manifold.sphere(strand_r * 1.45, 6).translate(tuple(grid[i][k])))
    parts.append(mf.Manifold.sphere(strand_r * 1.8, 8).translate(tuple(apex)))

    return mf.Manifold.batch_boolean(parts, mf.OpType.Add)


def _build_wall_web_solid(width, height, num_rings, strand_w, thickness, y_center, sag_ratio=0.18):
    """Build a trapezoid wall spiderweb manifold solid with clean, open 8-spoke layout."""
    sections = []
    wt, wb = width * 0.96, width * 0.86
    ht, hb = y_center + height * 0.52, y_center - height * 0.48

    c_tl = np.array([-wt * 0.5, ht])
    c_tr = np.array([wt * 0.52, ht * 0.98])
    c_br = np.array([wb * 0.5, hb])
    c_bl = np.array([-wb * 0.48, hb * 1.02])

    center = np.array([width * 0.02, y_center + height * 0.03])

    # 8 spokes: 4 corner points + 1 middle point on each of the 4 outer edges
    outer_anchors = []
    outer_anchors.append(c_tl)
    top_mid = _catenary_points(c_tl, c_tr, width * 0.07, center, 3)[1]
    outer_anchors.append(top_mid)
    outer_anchors.append(c_tr)
    right_mid = _catenary_points(c_tr, c_br, height * 0.07, center, 3)[1]
    outer_anchors.append(right_mid)
    outer_anchors.append(c_br)
    bot_mid = _catenary_points(c_br, c_bl, width * 0.07, center, 3)[1]
    outer_anchors.append(bot_mid)
    outer_anchors.append(c_bl)
    left_mid = _catenary_points(c_bl, c_tl, height * 0.07, center, 3)[1]
    outer_anchors.append(left_mid)

    num_spokes = len(outer_anchors) # 8 spokes
    hub_r = 0.13 * min(width, height)
    hub_pts = []
    for i in range(num_spokes):
        vec = outer_anchors[i] - center
        ang = np.arctan2(vec[1], vec[0])
        hub_pts.append(center + hub_r * np.array([np.cos(ang), np.sin(ang)]))

    for i in range(num_spokes):
        j = (i + 1) % num_spokes
        r = _make_ribbon(hub_pts[i], hub_pts[j], strand_w * 0.9)
        if r:
            sections.append(r)

    ring_fractions = [(k / (num_rings + 1)) ** 1.15 for k in range(1, num_rings + 1)]
    grid = []
    for i in range(num_spokes):
        col = [hub_pts[i]]
        out = outer_anchors[i]
        for r_frac in ring_fractions:
            col.append(hub_pts[i] + r_frac * (out - hub_pts[i]))
        col.append(out)
        grid.append(col)

        # Corner spokes slightly thicker
        w = strand_w * 1.25 if (i in (0, 2, 4, 6)) else strand_w
        for k in range(len(col) - 1):
            r = _make_ribbon(col[k], col[k + 1], w)
            if r:
                sections.append(r)

    for k in range(1, num_rings + 1):
        for i in range(num_spokes):
            j = (i + 1) % num_spokes
            p0 = grid[i][k]
            p1 = grid[j][k]
            dist = np.linalg.norm(p1 - p0)
            sag = dist * sag_ratio
            pts = _catenary_points(p0, p1, sag, center, 4)
            for m in range(len(pts) - 1):
                r = _make_ribbon(pts[m], pts[m + 1], strand_w * 0.85)
                if r:
                    sections.append(r)

    for i in range(num_spokes):
        j = (i + 1) % num_spokes
        p0 = outer_anchors[i]
        p1 = outer_anchors[j]
        dist = np.linalg.norm(p1 - p0)
        sag = dist * (sag_ratio * 1.25)
        pts = _catenary_points(p0, p1, sag, center, 5)
        for m in range(len(pts) - 1):
            r = _make_ribbon(pts[m], pts[m + 1], strand_w * 1.2)
            if r:
                sections.append(r)

    for i in range(num_spokes):
        for k in range(len(grid[i])):
            rad = strand_w * 1.35 if (k in (1, 2) and i in (1, 3, 5, 7)) else strand_w * 0.95
            sections.append(mf.CrossSection.circle(rad, 6).translate(tuple(grid[i][k])))

    cs = mf.CrossSection.batch_boolean(sections, mf.OpType.Add)
    return cs.extrude(thickness)


def web_corner_small(radius=1.15, drop=0.45, y_top=3.35, **kwargs):
    """Small 3D corner web: 5 spokes, 2 inner rings, 1.15m reach."""
    if "width" in kwargs:
        radius = kwargs["width"] / 2.0 * np.sqrt(2)
    if "height" in kwargs:
        drop = kwargs["height"]
    if "y_apex" in kwargs:
        y_top = kwargs["y_apex"]
    a = Assembly()
    solid = _build_corner_web_solid(radius=radius, drop=drop, num_spokes=5, num_rings=2,
                                   strand_r=0.018, y_top=y_top)
    a.add(solid, 'web')
    return a.build()


def web_corner_large(radius=1.75, drop=0.65, y_top=3.35, **kwargs):
    """Large 3D corner web: 6 spokes, 3 inner rings, 1.75m reach."""
    if "width" in kwargs:
        radius = kwargs["width"] / 2.0 * np.sqrt(2)
    if "height" in kwargs:
        drop = kwargs["height"]
    if "y_apex" in kwargs:
        y_top = kwargs["y_apex"]
    a = Assembly()
    solid = _build_corner_web_solid(radius=radius, drop=drop, num_spokes=6, num_rings=3,
                                   strand_r=0.022, y_top=y_top)
    a.add(solid, 'web')
    return a.build()


def web_wall_small(width=1.25, height=1.05, y_center=2.25):
    """Small trapezoid wall web: 8 spokes, 2 inner rings, 1.25m x 1.05m."""
    a = Assembly()
    solid = _build_wall_web_solid(width=width, height=height, num_rings=2,
                                 strand_w=0.032, thickness=0.035, y_center=y_center)
    a.add(solid, 'web')
    return a.build()


def web_wall_large(width=2.05, height=1.65, y_center=2.20):
    """Large trapezoid wall web: 8 spokes, 3 inner rings, 2.05m x 1.65m."""
    a = Assembly()
    solid = _build_wall_web_solid(width=width, height=height, num_rings=3,
                                 strand_w=0.038, thickness=0.038, y_center=y_center)
    a.add(solid, 'web')
    return a.build()


KINDS = {
    'web_corner_small': web_corner_small,
    'web_corner_large': web_corner_large,
    'web_wall_small': web_wall_small,
    'web_wall_large': web_wall_large,
}
