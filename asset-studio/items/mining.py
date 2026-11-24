"""Mining props for the Mines realm: carts, rail tracks, ore veins, ore piles, racks, lanterns, timber frames, winch, and powder kegs.

All geometry uses the chunky, weathered construction vocabulary in `furnishing_kit` and exports through
`world_surface` onto the shared dungeon texture. Units are Godot metres:
- Floor props stand on y = 0, front toward +Z, back toward -Z.
- Wall props keep a flat back on z = 0 at their natural hanging height above y = 0.
- Rail tracks fit one 4 x 4 m tile at 1.0 m gauge and stay under 0.15 m tall (except buffer stop).
- Timber frames span one 4 m tunnel tile (4 m wide x 4 m tall) with >= 2.7 m clear opening.
"""

import numpy as np
import manifold3d as mf
from core import kit
from items.solid import Assembly, lathe, loop
from items.furnishing_kit import (
    ball, bend, block, board, disc, facade, framed, grounded, jag,
    moved, outline, post, random, rect, rivet, ring, rod, solid, square_post, torus, turned,
)


def _sub(parent):
    """Child assembly sharing `parent.slots` so `build_solid()` preserves material slot indices."""
    sub = Assembly()
    sub.slots = parent.slots
    return sub


def _rock_chunk(center, size, rng, facets=6, tilt_deg=0, tilt_axis=(1, 0, 0), top_scale=0.38, slot='rock'):
    """Chunky faceted rock or ore nugget matching the dungeon rubble silhouette."""
    sx, sy, sz = size
    angles = np.sort(rng.uniform(0, 2 * np.pi, facets))
    rx = sx * 0.5 * rng.uniform(0.80, 1.08, facets)
    rz = sz * 0.5 * rng.uniform(0.80, 1.08, facets)
    ring_y = sy * rng.uniform(-0.10, 0.10, facets)
    mid_ring = np.column_stack([np.cos(angles) * rx, ring_y, np.sin(angles) * rz])
    top_n = max(3, facets - 2)
    t_ang = np.sort(rng.uniform(0, 2 * np.pi, top_n))
    top_ring = np.column_stack([
        np.cos(t_ang) * sx * top_scale * rng.uniform(0.75, 1.05),
        np.full(top_n, sy * 0.5 * rng.uniform(0.86, 1.05)),
        np.sin(t_ang) * sz * top_scale * rng.uniform(0.75, 1.05),
    ])
    bot_n = max(3, facets - 2)
    b_ang = np.sort(rng.uniform(0, 2 * np.pi, bot_n))
    bot_ring = np.column_stack([
        np.cos(b_ang) * sx * 0.45 * rng.uniform(0.75, 1.05),
        np.full(bot_n, -sy * 0.5 * rng.uniform(0.86, 1.05)),
        np.sin(b_ang) * sz * 0.45 * rng.uniform(0.75, 1.05),
    ])
    pts = np.vstack([mid_ring, top_ring, bot_ring])
    if tilt_deg != 0:
        pts = pts @ kit.rotation(tilt_axis, tilt_deg).T
    return kit.hull(pts + np.asarray(center, float), slot)


def _rock_chip(center, size, rng, slot='rock'):
    """Low pebble or ore shard for base scatter."""
    sx, sy, sz = size
    n = int(rng.integers(5, 8))
    angles = np.sort(rng.uniform(0, 2 * np.pi, n))
    rx = sx * 0.5 * rng.uniform(0.76, 1.08, n)
    rz = sz * 0.5 * rng.uniform(0.76, 1.08, n)
    pts = np.vstack([
        np.column_stack([np.cos(angles) * rx, rng.uniform(-0.015, 0.015, n), np.sin(angles) * rz]),
        [[rng.uniform(-0.02, 0.02), sy * 0.5, rng.uniform(-0.02, 0.02)], [0, -sy * 0.5, 0]],
    ])
    return kit.hull(pts + np.asarray(center, float), slot)


def _crystal_spire(radius, height, tilt=(0, 0, 0), pos=(0, 0, 0)):
    """Chunky hexagonal pointed crystal prism."""
    spire = lathe([
        (radius, 0.0),
        (radius * 0.94, height * 0.66),
        (radius * 0.50, height * 0.88),
        (0.01, height),
    ], sides=6)
    return spire.rotate(tuple(tilt)).translate(tuple(pos))


def _trim_wall_back(a):
    """Trim every component behind z = 0 cleanly while preserving Assembly material slots."""
    return _sub(a).join(a.build_solid().trim_by_plane((0, 0, 1), 0.0)).build()


# ---------------------------------------------------------------------------- tools (shared by rack & loose pickaxe)

def _add_pickaxe(a, frame=None):
    """Chunky mining pickaxe: curved wooden haft (~0.98 m along Y), iron eye socket, steel pick + adze head."""
    # Wooden haft with subtle grip curve and pommel swell
    haft_path = [
        (0.0, 0.00, 0.00),
        (0.0, 0.25, 0.006),
        (0.0, 0.55, 0.010),
        (0.0, 0.82, 0.004),
        (0.0, 0.94, 0.000),
    ]
    a.join(framed(a.label(
        mf.Manifold.batch_boolean([
            solid(kit.sweep(haft_path, kit.ring_profile(1, 1, 8), 'wood', scale=[0.044, 0.036, 0.038, 0.042, 0.046])),
            block((0.095, 0.075, 0.092), (0, 0.035, 0), 0.020),
            block((0.105, 0.140, 0.115), (0, 0.86, 0), 0.024),
        ], mf.OpType.Add),
        'wood',
    ), frame))
    # Iron eye collar
    a.join(framed(a.label(block((0.125, 0.125, 0.135), (0, 0.84, 0), 0.022), 'iron_dark'), frame))
    # Steel pick spike (-X) and flared adze/chisel (-/+ X)
    pick_pts = [
        (-0.29, 0.77), (-0.17, 0.81), (-0.04, 0.82), (0.04, 0.82), (0.21, 0.80), (0.28, 0.77),
        (0.28, 0.84), (0.17, 0.88), (0.04, 0.90), (-0.04, 0.90), (-0.17, 0.88), (-0.29, 0.80),
    ]
    head = facade(outline(pick_pts), 0.088, -0.044)
    # Flare adze blade wider in Z while tapering pick point narrower in Z
    def _taper_head(verts):
        v = np.array(verts)
        left = np.clip(-v[:, 0] / 0.29, 0.0, 1.0)
        right = np.clip(v[:, 0] / 0.28, 0.0, 1.0)
        scale_z = np.where(v[:, 0] < 0, 1.0 - 0.72 * left, 1.0 + 0.35 * right)
        v[:, 2] *= scale_z
        return v
    head = head.warp_batch(_taper_head)
    a.join(framed(a.label(head, 'steel'), frame))
    # Top haft wedge peaking through eye
    a.join(framed(a.label(block((0.068, 0.045, 0.072), (0, 0.92, 0), 0.010), 'wood_light'), frame))


def _add_shovel(a, frame=None):
    """Chunky mining shovel: D-grip, straight shaft, dished iron/steel spade blade (~0.96 m along Y)."""
    # Shaft
    a.join(framed(a.label(post(0.034, 0.24, 0.86, sides=8), 'wood'), frame))
    # D-grip top handle
    a.join(framed(a.label(block((0.15, 0.048, 0.055), (0, 0.955, 0), 0.014), 'wood_dark'), frame))
    for sx in (-0.058, 0.058):
        a.join(framed(a.label(block((0.036, 0.115, 0.048), (sx, 0.90, 0), 0.010), 'wood_dark'), frame))
    a.join(framed(a.label(block((0.15, 0.042, 0.064), (0, 0.845, 0), 0.012), 'iron_dark'), frame))
    # Blade socket collar
    a.join(framed(a.label(lathe([(0.052, 0.22), (0.046, 0.33)], 8), 'iron_dark'), frame))
    # Chunky spade blade (XY silhouette extruded in Z, slight concave dish)
    blade_pts = [
        (-0.115, 0.26), (-0.110, 0.08), (0.0, 0.00), (0.110, 0.08), (0.115, 0.26),
        (0.045, 0.275), (-0.045, 0.275),
    ]
    blade = facade(outline(blade_pts), 0.038, -0.019)
    def _dish(verts):
        v = np.array(verts)
        # Taper tip thickness and dish center gently back (-Z)
        tip = np.clip((0.26 - v[:, 1]) / 0.26, 0.0, 1.0)
        v[:, 2] *= (1.0 - 0.55 * tip)
        v[:, 2] -= 0.018 * (1.0 - (v[:, 0] / 0.115) ** 2) * (1.0 - 0.5 * tip)
        return v
    blade = blade.warp_batch(_dish)
    a.join(framed(a.label(blade, 'steel'), frame))
    # Center iron rib down upper blade
    a.join(framed(a.label(block((0.050, 0.14, 0.045), (0, 0.19, 0.005), 0.010), 'iron_dark'), frame))


def pickaxe_loose():
    """Loose weathered pickaxe lying flat on the floor (~1.0 m long, <= 0.14 m tall)."""
    a = Assembly()
    _add_pickaxe(a)
    p = turned(turned(a.build(), 90, 'x'), 18, 'y')
    return grounded(p, 0.0, 0.0)


def tool_rack():
    """Wall-mounted timber tool rack (~1.62 m wide) holding two pickaxes, two shovels, and one empty peg slot."""
    a, rng = Assembly(), random('tool_rack')
    # Backboard vertical cleats at z = 0..0.06
    for sx in (-0.64, 0.64):
        board(a, rng, (0.14, 1.02, 0.06), (sx, 1.72, 0.03), 'wood_dark', chips=1, wobble=0.2)
    # Two horizontal rails
    for y in (1.42, 2.02):
        board(a, rng, (1.60, 0.16, 0.07), (0, y, 0.08), 'wood', chips=2, wobble=0.2)
        for sx in (-0.64, 0.64):
            a.add(block((0.12, 0.19, 0.05), (sx, y, 0.11), 0.015), 'iron_dark')
            a.add(rivet((sx, y, 0.135), 0.028), 'steel')
    # Five tool slots along X; slot index 2 (+0.03) is deliberately empty!
    slots = [
        (-0.58, 'pick', 0.0),
        (-0.27, 'shovel', 0.0),
        (+0.03, 'empty', 0.0),
        (+0.31, 'shovel', -3.0),
        (+0.60, 'pick', 4.0),
    ]
    for x, kind, tilt in slots:
        # Pair of sturdy pegs at the upper rail
        for dx in (-0.065, 0.065):
            a.add(rod((x + dx, 2.02, 0.09), (x + dx, 2.05, 0.27), 0.024, 6), 'wood_dark')
            a.add(ball((x + dx, 2.05, 0.27), (0.030, 0.030, 0.030), 6), 'iron_dark')
        # Lower rest peg
        a.add(rod((x, 1.42, 0.09), (x, 1.44, 0.22), 0.022, 6), 'wood_dark')
        if kind == 'pick':
            frame = [((0, 0, tilt), (0, -0.84, 0)), ((0, 0, 0), (x, 2.04, 0.18))]
            _add_pickaxe(a, frame=frame)
        elif kind == 'shovel':
            frame = [((0, 0, tilt), (0, 0, 0)), ((0, 0, 0), (x, 1.24, 0.17))]
            _add_shovel(a, frame=frame)
    return _trim_wall_back(a)


# ---------------------------------------------------------------------------- mine carts

def _add_flanged_wheel(a, x_center, y_center, z_center, side_sign):
    """Chunky flanged mine-cart wheel: tread at x_center, inner rail flange inward (-side_sign)."""
    r_tread, r_flange = 0.195, 0.235
    # Tread + hub lathe profile revolved around Y, then turned onto X axis
    # Inner face toward center (side_sign < 0 -> +X, side_sign > 0 -> -X)
    tread = lathe([(0.02, -0.05), (r_tread, -0.05), (r_tread, 0.05), (0.02, 0.05)], 14)
    flange = lathe([(r_tread - 0.02, -0.065), (r_flange, -0.065), (r_flange, -0.035), (r_tread - 0.02, -0.035)], 14)
    hub = lathe([(0.01, -0.07), (0.075, -0.07), (0.075, 0.08), (0.01, 0.08)], 8)
    # Orient so flange (-Y local) points inward toward cart centerline (x = 0)
    rot = (0, 0, -90 * side_sign)
    frame = [((0, 0, 0), (x_center, y_center, z_center))]
    a.join(framed(a.label(tread.rotate(rot), 'iron'), frame))
    a.join(framed(a.label(flange.rotate(rot), 'iron_dark'), frame))
    a.join(framed(a.label(hub.rotate(rot), 'steel'), frame))


def _build_mine_cart(seed_name, reinforced=False):
    """Build the core mine cart assembly (~1.22 m W x 2.08 m L x 1.18 m H, 1.00 m wheel gauge)."""
    a, rng = Assembly(), random(seed_name)
    # Flared solid tub shell: bottom 0.90 x 1.62 at y = 0.44, top 1.18 x 1.88 at y = 1.16
    w0, l0, y0 = 0.44, 0.78, 0.44
    w1, l1, y1 = 0.56, 0.90, 1.15
    outer_pts = [
        (-w0, y0, -l0), (w0, y0, -l0), (w0, y0, l0), (-w0, y0, l0),
        (-w1, y1, -l1), (w1, y1, -l1), (w1, y1, l1), (-w1, y1, l1),
    ]
    a.add(kit.hull(np.array(outer_pts, float), 'wood'), 'wood')
    # Real carved hollow interior
    iw0, il0, iy0 = 0.34, 0.68, 0.54
    iw1, il1, iy1 = 0.48, 0.82, 1.32
    inner_pts = [
        (-iw0, iy0, -il0), (iw0, iy0, -il0), (iw0, iy0, il0), (-iw0, iy0, il0),
        (-iw1, iy1, -il1), (iw1, iy1, -il1), (iw1, iy1, il1), (-iw1, iy1, il1),
    ]
    a.cut(kit.hull(np.array(inner_pts, float), 'wood_dark'), 'wood_dark')

    # Exterior weathered horizontal plank relief slats on sides and ends (tilted about each slat's own center)
    for y_band, t_w, t_l in [(0.62, 0.47, 0.80), (0.83, 0.51, 0.84), (1.02, 0.545, 0.875)]:
        for s in (-1, 1):
            board(a, rng, (0.055, 0.165, t_l * 1.92), (0, 0, 0), 'wood',
                  frame=[((0, 0, -s * 9.5), (s * (t_w + 0.004), y_band, 0))], chips=1, wobble=0.3)
            board(a, rng, (t_w * 1.92, 0.165, 0.055), (0, 0, 0), 'wood',
                  frame=[((s * 9.5, 0, 0), (0, y_band, s * (t_l + 0.004)))], chips=1, wobble=0.3)

    # Heavy iron upper rim band around tub mouth (y = 1.11..1.18)
    for s in (-1, 1):
        a.add(block((0.085, 0.075, 1.88), (s * 0.555, 1.14, 0), 0.018), 'iron_dark')
        a.add(block((1.19, 0.075, 0.085), (0, 1.14, s * 0.895), 0.018), 'iron_dark')
    # Corner vertical iron straps + rivets
    for sx in (-1, 1):
        for sz in (-1, 1):
            a.add(rod((sx * 0.44, 0.44, sz * 0.78), (sx * 0.57, 1.15, sz * 0.90), 0.048, 6), 'iron_dark')
            for y_riv in (0.64, 0.94, 1.14):
                frac = (y_riv - 0.44) / 0.71
                rx = sx * (0.44 + 0.13 * frac)
                rz = sz * (0.78 + 0.12 * frac)
                a.add(rivet((rx + sx * 0.025, y_riv, rz), 0.026, (sx, 0, 0)), 'steel')
                a.add(rivet((rx, y_riv, rz + sz * 0.025), 0.026, (0, 0, sz)), 'steel')

    # Optional extra vertical iron ribs for heavy ore cart
    if reinforced:
        for sx in (-1, 1):
            for z_rib in (-0.32, 0.32):
                a.add(rod((sx * 0.46, 0.44, z_rib), (sx * 0.57, 1.14, z_rib), 0.042, 6), 'iron')
                a.add(rivet((sx * 0.53, 0.82, z_rib), 0.028, (sx, 0, 0)), 'steel')
                a.add(rivet((sx * 0.57, 1.14, z_rib), 0.028, (sx, 0, 0)), 'steel')

    # Chassis rails, cross bumpers, and forged coupling hooks at z = +-1.0
    for sx in (-0.42, 0.42):
        board(a, rng, (0.14, 0.16, 1.84), (sx, 0.38, 0), 'wood_dark', chips=1, wobble=0.2)
    for sz in (-0.90, 0.90):
        board(a, rng, (1.00, 0.16, 0.14), (0, 0.38, sz), 'wood_dark', chips=1, wobble=0.2)
        s = 1 if sz > 0 else -1
        # Iron bumper plate + drawbar eye + solid forged coupling C-hook
        a.add(block((0.30, 0.16, 0.05), (0, 0.38, sz + s * 0.065), 0.012), 'iron_dark')
        a.add(rod((0, 0.38, sz + s * 0.055), (0, 0.38, sz + s * 0.11), 0.034, 6), 'iron')
        a.add(block((0.075, 0.075, 0.055), (0, 0.38, sz + s * 0.115), 0.012), 'iron_dark')
        hook_path = [
            (0.0, 0.38, sz + s * 0.105),
            (0.0, 0.37, sz + s * 0.155),
            (0.0, 0.32, sz + s * 0.185),
            (0.0, 0.275, sz + s * 0.165),
            (0.0, 0.285, sz + s * 0.125),
        ]
        a.add(bend(hook_path, [0.023, 0.021, 0.019, 0.017, 0.014], 6), 'iron')
        for rx in (-0.10, 0.10):
            a.add(rivet((rx, 0.38, sz + s * 0.095), 0.024, (0, 0, s)), 'steel')

    # Two axles (z = +-0.54) and 4 flanged wheels at 1.00 m gauge (x = +-0.50, y = 0.235)
    for sz in (-0.54, 0.54):
        a.add(rod((-0.56, 0.235, sz), (0.56, 0.235, sz), 0.042, 8), 'iron_dark')
        for sx in (-0.42, 0.42):
            a.add(block((0.12, 0.16, 0.14), (sx, 0.28, sz), 0.02), 'iron_dark')
        for side in (-1, 1):
            _add_flanged_wheel(a, side * 0.50, 0.235, sz, side)
    return a, rng


def mine_cart_empty():
    """Empty weathered wood-and-iron mine cart (1.2 m W x 2.1 m L x 1.2 m H, 1.0 m gauge)."""
    a, rng = _build_mine_cart('mine_cart_empty', reinforced=False)
    # A couple tiny leftover pebbles seated on the tub bottom so it reads hollow & used
    a.add(_rock_chip((-0.12, 0.545, 0.18), (0.14, 0.05, 0.12), rng, 'rock_dark'), 'rock_dark')
    a.add(_rock_chip((0.16, 0.545, -0.22), (0.12, 0.04, 0.10), rng, 'rock'), 'rock')
    return grounded(a.build())


def mine_cart_ore():
    """Ore-filled iron-ribbed mine cart heaped high with raw rock and glinting gold ore."""
    a, rng = _build_mine_cart('mine_cart_ore', reinforced=True)
    # Solid rock bed filling the interior up over the rim
    bed = kit.hull(np.array([
        (-0.44, 0.76, -0.78), (0.44, 0.76, -0.78), (0.44, 0.76, 0.78), (-0.44, 0.76, 0.78),
        (-0.34, 1.18, -0.55), (0.34, 1.18, -0.55), (0.34, 1.18, 0.55), (-0.34, 1.18, 0.55),
        (0.0, 1.30, 0.0),
    ], float), 'rock_dark')
    a.add(bed, 'rock_dark')
    # Chunky heaped rocks and prominent gold ore chunks
    chunks = [
        ((0.0, 1.22, 0.05), (0.58, 0.32, 0.62), 'rock', 10),
        ((-0.18, 1.15, -0.34), (0.46, 0.28, 0.48), 'rock_dark', -12),
        ((0.18, 1.16, 0.38), (0.48, 0.28, 0.46), 'rock', 14),
        ((-0.22, 1.10, 0.44), (0.38, 0.22, 0.38), 'rock_dark', 8),
        ((0.20, 1.12, -0.44), (0.40, 0.23, 0.36), 'rock', -15),
        # Glinting ore chunks nestled high across the mound
        ((0.04, 1.31, -0.04), (0.28, 0.20, 0.28), 'ore_gold', 18),
        ((-0.24, 1.21, 0.14), (0.26, 0.19, 0.24), 'ore_gold', -20),
        ((0.24, 1.20, -0.18), (0.24, 0.18, 0.26), 'ore_gold', 24),
        ((-0.06, 1.18, 0.48), (0.24, 0.17, 0.22), 'ore_gold', -10),
        ((0.08, 1.16, -0.48), (0.22, 0.16, 0.22), 'ore_gold', 15),
        ((0.26, 1.14, 0.24), (0.20, 0.15, 0.20), 'ore_gold', -14),
        ((-0.24, 1.12, -0.32), (0.21, 0.15, 0.20), 'ore_gold', 12),
    ]
    for pos, size, slot, tilt in chunks:
        a.add(_rock_chunk(pos, size, rng, facets=6, tilt_deg=tilt, slot=slot), slot)
    return grounded(a.build())


def mine_cart_tipped():
    """Derailled mine cart rolled onto its side with spilled rock and ore fanned across the floor."""
    a, rng = _build_mine_cart('mine_cart_tipped', reinforced=False)
    cart_solid = a.build_solid().rotate((0, 0, -84)).rotate((0, -12, 0))
    c_lo = cart_solid.bounding_box()
    cart_solid = cart_solid.translate((-0.25 - (c_lo[0] + c_lo[3]) * 0.12, -c_lo[1], 0.0))
    c_hi = cart_solid.bounding_box()
    mouth_x = float(c_hi[3]) - 0.10

    whole = _sub(a)
    whole.join(cart_solid)
    spill_items = [
        ((mouth_x - 0.04, 0.14, -0.24), (0.42, 0.28, 0.36), 'rock_dark', 12),
        ((mouth_x + 0.14, 0.13, 0.18), (0.36, 0.26, 0.34), 'rock', -14),
        ((mouth_x + 0.26, 0.10, -0.02), (0.28, 0.20, 0.26), 'ore_gold', 20),
        ((mouth_x + 0.04, 0.11, 0.46), (0.30, 0.22, 0.28), 'rock_dark', 8),
        ((mouth_x + 0.34, 0.09, 0.32), (0.24, 0.17, 0.22), 'ore_gold', -16),
        ((mouth_x + 0.28, 0.10, -0.36), (0.26, 0.18, 0.24), 'rock', 15),
        ((mouth_x + 0.46, 0.07, 0.08), (0.20, 0.14, 0.18), 'ore_gold', 22),
    ]
    for pos, size, slot, tilt in spill_items:
        whole.add(_rock_chunk(pos, size, rng, facets=6, tilt_deg=tilt, slot=slot), slot)
    for dx, dz, sz_chip, slot in [
        (0.56, -0.18, 0.16, 'rock'),
        (0.58, 0.24, 0.14, 'ore_gold'),
        (0.40, 0.62, 0.15, 'rock_dark'),
        (0.36, -0.58, 0.15, 'ore_gold'),
        (0.18, 0.74, 0.14, 'rock'),
    ]:
        whole.add(_rock_chip((mouth_x + dx, sz_chip * 0.24, dz), (sz_chip, sz_chip * 0.48, sz_chip * 0.9), rng, slot), slot)
    return grounded(whole.build(loose=True), 0.0, 0.0)


# ---------------------------------------------------------------------------- rail tracks (1.0 m gauge, < 0.15 m tall)

_RAIL_PROFILE = np.array([
    (-0.055, 0.076), (0.055, 0.076),
    (0.055, 0.094), (0.028, 0.102),
    (0.028, 0.114), (0.044, 0.120),
    (0.042, 0.138), (-0.042, 0.138),
    (-0.044, 0.120), (-0.028, 0.114),
    (-0.028, 0.102), (-0.055, 0.094),
], float)


def _rail_profile_section():
    """Chunky beveled rail cross section (X across rail, Y height 0.076..0.138)."""
    return outline(_RAIL_PROFILE)


def _add_sleeper(a, rng, frame, cracked=False):
    """Weathered wooden sleeper (y = 0.00..0.084) with optional crack/split and iron tie-plates."""
    if not cracked:
        board(a, rng, (1.56, 0.082, 0.22), (0, 0.0425, 0), 'wood', frame=frame, chips=1, wobble=0.0)
    else:
        # Split sleeper: two slightly misaligned halves with exposed endgrain at break
        board(a, rng, (0.74, 0.082, 0.22), (-0.43, 0.0425, 0.015), 'wood',
              frame=[((0, -2.5, 0), (0, 0, 0))] + frame, chips=1, wobble=0.0)
        board(a, rng, (0.72, 0.082, 0.22), (0.43, 0.0425, -0.015), 'wood_dark',
              frame=[((0, 3.0, 0), (0, 0, 0))] + frame, chips=1, wobble=0.0)
        # Jagged fracture nibs showing pale endgrain
        a.join(framed(a.label(block((0.06, 0.072, 0.16), (-0.07, 0.038, 0.01), 0.01), 'endgrain'), frame))
        a.join(framed(a.label(block((0.06, 0.072, 0.16), (0.08, 0.038, -0.01), 0.01), 'endgrain'), frame))
    # Iron tie-plates and spike lugs at gauge +-0.50
    for rx in (-0.50, 0.50):
        a.join(framed(a.label(block((0.15, 0.024, 0.24), (rx, 0.076, 0), 0.006), 'iron_dark'), frame))
        for dx in (-0.052, 0.052):
            a.join(framed(a.label(block((0.026, 0.026, 0.036), (rx + dx, 0.095, 0.065), 0.005), 'steel'), frame))
            a.join(framed(a.label(block((0.026, 0.026, 0.036), (rx + dx, 0.095, -0.065), 0.005), 'steel'), frame))


def rail_track_straight():
    """One 4 x 4 m tile of straight 1.0 m gauge mine track (max y <= 0.14 m, walkable)."""
    a, rng = Assembly(), random('rail_track_straight')
    # 7 sleeper positions along Z; #2 missing, #4 cracked
    for idx, z in enumerate(np.linspace(-1.72, 1.72, 7)):
        if idx == 2:
            continue
        yaw = float(rng.uniform(-1.5, 1.5))
        _add_sleeper(a, rng, [((0, yaw, 0), (0, 0, z))], cracked=(idx == 4))
    # Two straight iron rails spanning z = -2.0..2.0 at x = +-0.50
    sec = _rail_profile_section()
    for rx in (-0.50, 0.50):
        rail = mf.Manifold.extrude(sec, 4.0).translate((rx, 0.0, -2.0))
        a.add(rail, 'iron')
    return grounded(a.build())


def rail_track_curve():
    """One 4 x 4 m tile of 90-degree curved 1.0 m gauge mine track (+Z edge to +X edge, max y <= 0.14 m)."""
    a, rng = Assembly(), random('rail_track_curve')
    cx, cz = 2.0, 2.0
    angles = np.linspace(np.pi + 0.14, 1.5 * np.pi - 0.14, 7)
    for idx, th in enumerate(angles):
        if idx == 1:
            continue
        px = cx + 2.0 * np.cos(th)
        pz = cz + 2.0 * np.sin(th)
        yaw_deg = -np.degrees(th) + float(rng.uniform(-1.5, 1.5))
        _add_sleeper(a, rng, [((0, yaw_deg, 0), (px, 0.0, pz))], cracked=(idx == 4))

    for r in (1.50, 2.50):
        sec = outline(np.column_stack([r + _RAIL_PROFILE[:, 0], _RAIL_PROFILE[:, 1]]))
        arc = mf.Manifold.revolve(sec, 18, 90.0).rotate((-90, 0, 0)).rotate((0, 90, 0)).translate((cx, 0.0, cz))
        a.add(arc, 'iron')
    return grounded(a.build())


def rail_track_end():
    """End piece of 1.0 m gauge track with a heavy timber-and-iron buffer stop at -Z."""
    a, rng = Assembly(), random('rail_track_end')
    # Walkable track sleepers from z = +1.72 down to -1.58
    for idx, z in enumerate([1.72, 1.15, 0.58, 0.00, -0.58, -1.16, -1.68]):
        _add_sleeper(a, rng, [((0, float(rng.uniform(-1.2, 1.2)), 0), (0, 0, z))], cracked=(idx == 2))
    # Iron rails from z = +2.00 to z = -1.86 (cleanly past rear clamp shoe at -1.80)
    sec = _rail_profile_section()
    for rx in (-0.50, 0.50):
        a.add(mf.Manifold.extrude(sec, 3.86).translate((rx, 0.0, -1.86)), 'iron')

    # Heavy timber & iron buffer stop (z in [-1.80, -0.92], y up to 0.88)
    for rx in (-0.50, 0.50):
        # Rail clamp shoes
        a.add(block((0.18, 0.16, 0.24), (rx, 0.125, -1.14), 0.018), 'iron_dark')
        a.add(block((0.18, 0.16, 0.24), (rx, 0.125, -1.68), 0.018), 'iron_dark')
        # Upright angled front posts rising toward -Z bumper
        a.add(mf.Manifold.batch_hull([
            block((0.16, 0.08, 0.16), (rx, 0.13, -1.14), 0.02),
            block((0.16, 0.08, 0.16), (rx, 0.72, -1.18), 0.02),
        ]), 'wood_dark')
        # Diagonal rear struts bracing against end sleeper
        strut = mf.Manifold.batch_hull([
            block((0.15, 0.08, 0.15), (rx, 0.13, -1.68), 0.02),
            block((0.15, 0.08, 0.15), (rx, 0.72, -1.24), 0.02),
        ])
        a.add(strut, 'wood_dark')
        # Heavy iron gusset collar at top junction
        a.add(block((0.20, 0.22, 0.22), (rx, 0.70, -1.18), 0.022), 'iron_dark')
        a.add(rivet((rx + (0.10 if rx > 0 else -0.10), 0.70, -1.18), 0.026, (1 if rx > 0 else -1, 0, 0)), 'steel')

    # Main heavy weathered bumper crossbeam facing +Z
    board(a, rng, (1.58, 0.26, 0.22), (0, 0.70, -1.04), 'wood', chips=2, wobble=0.3)
    # Iron end caps & twin buffer pads on bumper front (+Z face)
    for rx in (-0.76, 0.76):
        a.add(block((0.08, 0.29, 0.25), (rx, 0.70, -1.04), 0.018), 'iron_dark')
    for rx in (-0.42, 0.42):
        a.add(block((0.26, 0.20, 0.07), (rx, 0.70, -0.92), 0.016), 'iron_dark')
        for dx in (-0.07, 0.07):
            a.add(rivet((rx + dx, 0.70, -0.885), 0.024, (0, 0, 1)), 'steel')
    return a.build()


# ---------------------------------------------------------------------------- ore veins (wall mounted, z in [0, 0.32])

def ore_vein_a():
    """Diagonal rock outcrop wall vein (~1.25 m wide, z <= 0.28) with carved seam and gold ore."""
    a, rng = Assembly(), random('ore_vein_a')
    # Layered rock outcrop overlapping behind z = 0 (trimmed cleanly at z = 0)
    rocks = [
        ((-0.24, 1.15, 0.04), (0.74, 0.66, 0.46), 'rock', 12),
        ((0.02, 1.56, 0.05), (0.78, 0.72, 0.48), 'rock_dark', -10),
        ((0.26, 1.96, 0.04), (0.70, 0.60, 0.44), 'rock', 14),
        ((-0.32, 1.56, 0.03), (0.50, 0.52, 0.40), 'rock_dark', -8),
        ((0.22, 1.34, 0.04), (0.56, 0.52, 0.42), 'rock', 9),
    ]
    for pos, size, slot, tilt in rocks:
        a.add(_rock_chunk(pos, size, rng, facets=6, tilt_deg=tilt, slot=slot), slot)

    # Real carved diagonal seam pocket into the front face of the rock bulge
    seam = block((0.26, 0.96, 0.17), (0, 0, 0), 0.025).rotate((0, 0, -28)).translate((0.02, 1.56, 0.25))
    a.cut(seam, 'rock_dark')

    # Angular gold ore chunks seated deep back to z = 0 and protruding crisply inside the seam (z_max <= 0.31)
    ores = [
        ((-0.18, 1.24, 0.13), (0.25, 0.25, 0.30), 16),
        ((-0.03, 1.47, 0.14), (0.29, 0.27, 0.31), -14),
        ((0.09, 1.69, 0.13), (0.26, 0.25, 0.30), 22),
        ((0.22, 1.91, 0.12), (0.23, 0.23, 0.28), -12),
        ((-0.24, 1.62, 0.11), (0.19, 0.18, 0.26), 18),
        ((0.18, 1.35, 0.11), (0.19, 0.18, 0.26), -18),
    ]
    for pos, size, tilt in ores:
        a.add(_rock_chunk(pos, size, rng, facets=5, tilt_deg=tilt, slot='ore_gold'), 'ore_gold')
    return _trim_wall_back(a)


def ore_vein_b():
    """Wide horizontal tiered wall vein (~1.92 m wide, z <= 0.31) with carved pocket and gold nuggets."""
    a, rng = Assembly(), random('ore_vein_b')
    rocks = [
        ((-0.56, 1.12, 0.05), (0.76, 0.60, 0.44), 'rock_dark', 8),
        ((-0.06, 1.24, 0.06), (0.88, 0.72, 0.48), 'rock', -9),
        ((0.48, 1.16, 0.05), (0.82, 0.64, 0.44), 'rock_dark', 11),
        ((-0.34, 1.56, 0.04), (0.64, 0.46, 0.36), 'rock', -12),
        ((0.24, 1.52, 0.04), (0.66, 0.48, 0.38), 'rock_dark', 10),
        ((0.68, 0.94, 0.03), (0.48, 0.42, 0.32), 'rock', -6),
        ((-0.72, 0.92, 0.03), (0.46, 0.40, 0.30), 'rock', 12),
    ]
    for pos, size, slot, tilt in rocks:
        a.add(_rock_chunk(pos, size, rng, facets=6, tilt_deg=tilt, slot=slot), slot)

    # Carved horizontal shelf seam across the vein
    a.cut(block((1.35, 0.24, 0.26), (-0.02, 1.24, 0.24), 0.03).rotate((0, 0, 5)), 'rock_dark')

    ores = [
        ((-0.58, 1.18, 0.15), (0.26, 0.22, 0.24), 14),
        ((-0.32, 1.22, 0.17), (0.28, 0.24, 0.26), -16),
        ((-0.04, 1.26, 0.18), (0.30, 0.25, 0.26), 12),
        ((0.26, 1.24, 0.16), (0.26, 0.22, 0.24), -18),
        ((0.56, 1.20, 0.15), (0.24, 0.21, 0.24), 20),
        ((-0.18, 1.52, 0.13), (0.20, 0.18, 0.20), -10),
        ((0.36, 0.98, 0.13), (0.20, 0.17, 0.20), 15),
    ]
    for pos, size, tilt in ores:
        a.add(_rock_chunk(pos, size, rng, facets=5, tilt_deg=tilt, slot='ore_gold'), 'ore_gold')
    return _trim_wall_back(a)


def ore_vein_crystal():
    """Geode wall outcrop (~1.62 m wide, z <= 0.32) with carved hollow and protruding crystal cluster."""
    a, rng = Assembly(), random('ore_vein_crystal')
    rocks = [
        ((-0.36, 1.28, 0.05), (0.78, 0.76, 0.44), 'rock_dark', 10),
        ((0.26, 1.36, 0.05), (0.82, 0.82, 0.46), 'rock', -12),
        ((0.00, 1.72, 0.05), (0.76, 0.68, 0.42), 'rock_dark', 8),
        ((-0.54, 1.02, 0.03), (0.52, 0.48, 0.34), 'rock', -8),
        ((0.54, 1.12, 0.03), (0.50, 0.46, 0.32), 'rock_dark', 14),
    ]
    for pos, size, slot, tilt in rocks:
        a.add(_rock_chunk(pos, size, rng, facets=6, tilt_deg=tilt, slot=slot), slot)

    # Carve deep geode hollow in center of rock mound
    a.cut(ball((0.0, 1.42, 0.25), (0.44, 0.38, 0.22), 10), 'rock_dark')

    # Large angled hexagonal turquoise crystal spires fanning along wall face (z_max <= 0.31 m)
    crystals = [
        (0.110, 0.48, (22, 10, -12), (0.02, 1.42, 0.04)),
        (0.100, 0.44, (18, -18, 28), (-0.14, 1.38, 0.04)),
        (0.095, 0.43, (20, 22, -34), (0.16, 1.44, 0.04)),
        (0.085, 0.39, (12, 0, 46), (-0.24, 1.50, 0.03)),
        (0.085, 0.40, (14, 14, -50), (0.26, 1.54, 0.03)),
        (0.075, 0.32, (32, -8, 14), (-0.04, 1.24, 0.06)),
        (0.070, 0.30, (28, 16, -18), (0.12, 1.24, 0.06)),
    ]
    for r, h, tilt, pos in crystals:
        a.add(_crystal_spire(r, h, tilt, pos), 'ore_crystal')
    # Small gold flecks along side edge
    for pos, size, tilt in [
        ((-0.44, 1.10, 0.13), (0.18, 0.16, 0.18), 16),
        ((0.46, 1.18, 0.13), (0.17, 0.15, 0.18), -14),
    ]:
        a.add(_rock_chunk(pos, size, rng, facets=5, tilt_deg=tilt, slot='ore_gold'), 'ore_gold')
    return _trim_wall_back(a)


# ---------------------------------------------------------------------------- ore piles (floor, 1.0 - 1.5 m across)

def ore_pile_small():
    """Low heap (~1.05 m across, ~0.48 m tall) of loose mined rock and gold ore chunks."""
    a, rng = Assembly(), random('ore_pile_small')
    chunks = [
        ((-0.04, 0.20, -0.02), (0.58, 0.38, 0.54), 'rock_dark', 8),
        ((0.20, 0.15, 0.12), (0.44, 0.28, 0.40), 'rock', -12),
        ((-0.24, 0.14, 0.14), (0.42, 0.26, 0.38), 'rock', 10),
        ((0.06, 0.13, -0.24), (0.40, 0.24, 0.38), 'rock_dark', -8),
        ((-0.22, 0.11, -0.20), (0.34, 0.20, 0.32), 'rock', 14),
        # Ore chunks wedged prominently into heap
        ((0.02, 0.34, 0.04), (0.26, 0.20, 0.24), 'ore_gold', 16),
        ((-0.16, 0.22, 0.22), (0.22, 0.18, 0.22), 'ore_gold', -14),
        ((0.24, 0.20, -0.10), (0.22, 0.17, 0.20), 'ore_gold', 20),
        ((-0.30, 0.10, -0.02), (0.18, 0.14, 0.18), 'ore_gold', -18),
    ]
    for pos, size, slot, tilt in chunks:
        a.add(_rock_chunk(pos, size, rng, facets=6, tilt_deg=tilt, slot=slot), slot)
    for x, z, sz, slot in [
        (0.42, 0.08, 0.16, 'rock'), (-0.42, 0.20, 0.15, 'rock_dark'),
        (0.14, 0.40, 0.14, 'ore_gold'), (-0.12, -0.42, 0.15, 'rock'),
        (0.34, -0.30, 0.14, 'rock_dark'),
    ]:
        a.add(_rock_chip((x, sz * 0.24, z), (sz, sz * 0.48, sz * 0.85), rng, slot), slot)
    return grounded(a.build(loose=True))


def ore_pile_large():
    """Larger asymmetric ore heap (~1.48 m across, ~0.76 m tall) with rock, gold ore, and small crystals."""
    a, rng = Assembly(), random('ore_pile_large')
    chunks = [
        ((-0.08, 0.30, -0.06), (0.82, 0.56, 0.76), 'rock_dark', 6),
        ((0.32, 0.22, 0.14), (0.64, 0.42, 0.58), 'rock', -12),
        ((-0.36, 0.20, 0.22), (0.58, 0.38, 0.54), 'rock', 14),
        ((0.12, 0.20, -0.36), (0.56, 0.36, 0.50), 'rock_dark', -10),
        ((-0.34, 0.17, -0.32), (0.48, 0.30, 0.44), 'rock', 12),
        ((0.40, 0.15, -0.28), (0.42, 0.26, 0.40), 'rock_dark', -14),
        ((0.02, 0.14, 0.42), (0.46, 0.28, 0.42), 'rock', 9),
        # Stepped crown & prominent gold chunks
        ((-0.04, 0.56, -0.04), (0.42, 0.30, 0.38), 'rock', -8),
        ((0.04, 0.62, 0.08), (0.28, 0.22, 0.26), 'ore_gold', 22),
        ((-0.24, 0.42, 0.14), (0.28, 0.22, 0.26), 'ore_gold', -18),
        ((0.28, 0.38, -0.06), (0.26, 0.20, 0.24), 'ore_gold', 15),
        ((-0.14, 0.30, -0.34), (0.24, 0.18, 0.22), 'ore_gold', -12),
        ((0.46, 0.20, 0.26), (0.22, 0.17, 0.20), 'ore_gold', 20),
        ((-0.44, 0.16, 0.02), (0.20, 0.16, 0.20), 'ore_gold', -24),
        ((0.14, 0.26, 0.40), (0.22, 0.18, 0.22), 'ore_gold', 12),
    ]
    for pos, size, slot, tilt in chunks:
        a.add(_rock_chunk(pos, size, rng, facets=6, tilt_deg=tilt, slot=slot), slot)
    # Two small crystal spikes poking out of one corner
    a.add(_crystal_spire(0.065, 0.32, (28, 12, -32), (-0.20, 0.44, -0.18)), 'ore_crystal')
    a.add(_crystal_spire(0.055, 0.26, (38, -18, -22), (-0.28, 0.36, -0.10)), 'ore_crystal')
    for x, z, sz, slot in [
        (0.64, 0.12, 0.18, 'rock'), (-0.64, 0.22, 0.17, 'rock_dark'),
        (0.24, 0.62, 0.16, 'ore_gold'), (-0.26, -0.60, 0.18, 'rock'),
        (0.56, -0.44, 0.16, 'rock_dark'), (-0.54, -0.36, 0.15, 'ore_gold'),
    ]:
        a.add(_rock_chip((x, sz * 0.24, z), (sz, sz * 0.48, sz * 0.85), rng, slot), slot)
    return grounded(a.build(loose=True))


# ---------------------------------------------------------------------------- mine lanterns (hanging + post, ~0.5 m cage)

def _add_lantern_cage(a, center, glow=False):
    """Hanging octagonal iron cage lantern (~0.50 m tall, ~0.28 m wide) centered at `center`."""
    cx, cy, cz = center
    if glow:
        # Warm glowing inner lantern core centered cleanly inside the cage
        core = lathe([
            (0.055, -0.11),
            (0.082, -0.07),
            (0.088, 0.02),
            (0.072, 0.09),
            (0.010, 0.105),
        ], sides=8).translate((cx, cy - 0.01, cz))
        a.add(core, 'lantern_glow')
        return

    # Bottom oil pan + drop finial
    pan = lathe([
        (0.010, -0.24),
        (0.045, -0.20),
        (0.115, -0.18),
        (0.135, -0.14),
        (0.125, -0.11),
        (0.010, -0.11),
    ], sides=8).translate((cx, cy, cz))
    a.add(pan, 'iron_dark')

    # Domed top cap with chimney collar (recessed exterior vent pockets leaving inner core solid)
    cap = lathe([
        (0.010, 0.09),
        (0.138, 0.09),
        (0.145, 0.12),
        (0.105, 0.155),
        (0.088, 0.19),
        (0.094, 0.215),
        (0.010, 0.22),
    ], sides=8).translate((cx, cy, cz))
    # Carve four exterior vent recesses into chimney collar without cutting through center stem
    for angle in (0, 90, 180, 270):
        slot_cut = block((0.038, 0.028, 0.040), (0, 0, 0.082), 0.004).rotate((0, angle, 0)).translate((cx, cy + 0.162, cz))
        cap = cap - a.label(slot_cut, 'iron_dark')
    a.join(a.label(cap, 'iron_dark'))

    # Bail suspension ring at top
    a.add(loop((cx, cy + 0.24, cz), (0.048, 0.045), 0.018, 10), 'iron')
    # Middle guard ring around the cage
    a.add(torus(0.132, 0.018, cy - 0.01, 12, 6).translate((cx, 0, cz)), 'iron')
    # Four corner cage bars connecting bottom pan to top cap
    for k in range(4):
        ang = (k + 0.5) * np.pi / 2
        dx, dz = np.cos(ang) * 0.122, np.sin(ang) * 0.122
        a.add(rod((cx + dx, cy - 0.13, cz + dz), (cx + dx * 1.04, cy + 0.11, cz + dz * 1.04), 0.016, 6), 'iron')


def mine_lantern_wall(glow=False):
    """Hanging iron cage mine lantern on a forged wall bracket (flat back at z = 0)."""
    a = Assembly()
    lantern_center = (0.0, 2.04, 0.36)
    _add_lantern_cage(a, lantern_center, glow=glow)
    if glow:
        return a.build()

    # Forged wall backplate at z = 0..0.06
    a.add(facade(outline([(-0.11, 2.52), (0.11, 2.52), (0.11, 2.12), (0.0, 1.98), (-0.11, 2.12)]), 0.06, 0.0), 'iron_dark')
    for y_riv in (2.43, 2.11):
        a.add(rivet((0.0, y_riv, 0.06), 0.028), 'steel')
    # Horizontal top arm & curved scroll bracket out to hook at z = 0.36
    a.add(block((0.065, 0.065, 0.38), (0.0, 2.40, 0.21), 0.014), 'iron')
    a.add(bend([(0.0, 2.16, 0.05), (0.0, 2.22, 0.16), (0.0, 2.31, 0.26), (0.0, 2.38, 0.34)], [0.024] * 4, 6), 'iron_dark')
    # Forged hook stem & eye solidly connecting arm (2.40) into top of lantern bail ring (2.325)
    a.add(rod((0.0, 2.38, 0.36), (0.0, 2.31, 0.36), 0.022, 6), 'iron_dark')
    a.add(loop((0, 0, 0), (0.036, 0.045), 0.018, 10).rotate((0, 90, 0)).translate((0.0, 2.33, 0.36)), 'iron')
    return _trim_wall_back(a)


def mine_lantern_post(glow=False):
    """Floor-standing weathered timber post (~1.82 m tall) with iron bracket and hanging cage lantern."""
    a, rng = Assembly(), random('mine_lantern_post')
    lantern_center = (0.0, 1.32, 0.36)
    _add_lantern_cage(a, lantern_center, glow=glow)
    if glow:
        return a.build()

    # Sturdy cross-foot base on y = 0
    board(a, rng, (0.64, 0.11, 0.16), (0, 0.055, 0), 'wood_dark', chips=1, wobble=0.3)
    board(a, rng, (0.16, 0.11, 0.64), (0, 0.065, 0), 'wood_dark', chips=1, wobble=0.3)
    # Main timber post (~1.82 m tall)
    square_post(a, rng, 0.20, 0.08, 1.82, 0.0, 0.0, slot='wood', cap=0.05)
    # Overhanging timber arm toward +Z at y = 1.66
    board(a, rng, (0.14, 0.14, 0.56), (0.0, 1.66, 0.22), 'wood_dark', chips=1, wobble=0.3)
    # Diagonal knee brace from post to arm
    a.add(mf.Manifold.batch_hull([
        block((0.11, 0.08, 0.09), (0.0, 1.28, 0.08), 0.016),
        block((0.11, 0.08, 0.09), (0.0, 1.64, 0.32), 0.016),
    ]), 'wood')
    # Iron collar & tip band on arm + hanging hook solidly connecting to lantern bail (1.56)
    a.add(block((0.17, 0.17, 0.10), (0.0, 1.66, 0.36), 0.016), 'iron_dark')
    a.add(rivet((0.085, 1.66, 0.36), 0.024, (1, 0, 0)), 'steel')
    a.add(rivet((-0.085, 1.66, 0.36), 0.024, (-1, 0, 0)), 'steel')
    a.add(rod((0.0, 1.63, 0.36), (0.0, 1.59, 0.36), 0.022, 6), 'iron_dark')
    a.add(loop((0, 0, 0), (0.036, 0.042), 0.018, 10).rotate((0, 90, 0)).translate((0.0, 1.59, 0.36)), 'iron')
    return grounded(a.build())


# ---------------------------------------------------------------------------- timber frames (4 m wide x 4 m tall tunnel)

def timber_frame_intact():
    """Intact heavy timber mine passageway frame (4.0 m W x 4.0 m H, >= 2.8 m clear opening)."""
    a, rng = Assembly(), random('timber_frame_intact')
    # Left & right heavy posts centered at x = +-1.68 (inner faces at +-1.42 -> 2.84 m clear opening!)
    for sx in (-1, 1):
        px = sx * 1.68
        # Timber foot sill block & iron shoe
        board(a, rng, (0.62, 0.18, 0.72), (px, 0.09, 0), 'wood_dark', chips=1, wobble=0.2)
        square_post(a, rng, 0.52, 0.16, 3.44, px, 0.0, slot='wood')
        # Mid-post iron reinforcing collar + rivets
        a.add(block((0.57, 0.14, 0.57), (px, 1.68, 0), 0.02), 'iron_dark')
        for sz in (-0.285, 0.285):
            a.add(rivet((px, 1.68, sz), 0.032, (0, 0, 1 if sz > 0 else -1)), 'steel')
        # High corner knee brace (y >= 2.62, well above head height)
        brace = mf.Manifold.batch_hull([
            block((0.26, 0.22, 0.36), (px - sx * 0.24, 2.72, 0), 0.025),
            block((0.26, 0.22, 0.36), (px - sx * 0.92, 3.42, 0), 0.025),
        ])
        a.add(brace, 'wood_dark')
        # Corner iron L-bracket & bolts
        a.add(block((0.60, 0.30, 0.60), (px, 3.36, 0), 0.025), 'iron_dark')
        for sz in (-0.30, 0.30):
            for dx in (-0.14, 0.14):
                a.add(rivet((px + dx, 3.36, sz), 0.032, (0, 0, 1 if sz > 0 else -1)), 'steel')

    # Heavy horizontal lintel beam across full 4 m tunnel width
    board(a, rng, (3.94, 0.48, 0.62), (0, 3.66, 0), 'wood', chips=3, wobble=0.2)
    # Top cribbing blocks seated cleanly into lintel top up to y = 4.00
    for cx in (-1.50, -0.50, 0.50, 1.50):
        board(a, rng, (0.48, 0.16, 0.58), (cx, 3.92, 0), 'wood_dark', chips=1, wobble=0.3)
    # Center iron splice strap on lintel
    a.add(block((0.28, 0.53, 0.67), (0, 3.66, 0), 0.02), 'iron_dark')
    for sz in (-0.335, 0.335):
        for dy in (-0.12, 0.12):
            a.add(rivet((0, 3.66 + dy, sz), 0.032, (0, 0, 1 if sz > 0 else -1)), 'steel')
    return grounded(a.build())


def timber_frame_cracked():
    """Damaged mine timber frame: split right post, sagging broken lintel, snapped right knee brace."""
    a, rng = Assembly(), random('timber_frame_cracked')
    # Left post intact (slight lean)
    board(a, rng, (0.60, 0.18, 0.70), (-1.68, 0.09, 0), 'wood_dark', chips=1, wobble=0.2)
    square_post(a, rng, 0.52, 0.16, 3.44, -1.68, 0.0, slot='wood')
    a.add(block((0.57, 0.14, 0.57), (-1.68, 1.65, 0), 0.02), 'iron_dark')
    for sz in (-0.285, 0.285):
        a.add(rivet((-1.68, 1.65, sz), 0.032, (0, 0, 1 if sz > 0 else -1)), 'steel')
    # Left knee brace intact
    a.add(mf.Manifold.batch_hull([
        block((0.26, 0.22, 0.36), (-1.44, 2.72, 0), 0.025),
        block((0.26, 0.22, 0.36), (-0.76, 3.42, 0), 0.025),
    ]), 'wood_dark')
    a.add(block((0.60, 0.30, 0.60), (-1.68, 3.36, 0), 0.025), 'iron_dark')

    # Right post vertically cracked & slightly buckled around y ~ 1.85
    board(a, rng, (0.60, 0.18, 0.70), (1.68, 0.09, 0), 'wood_dark', chips=2, wobble=0.2)
    lower_post = _sub(a)
    square_post(lower_post, rng, 0.52, 0.16, 2.08, 1.68, 0.0, slot='wood')
    lower_post.cut(jag(1.88, 1.2, 1.2, above=True, amplitude=0.09, teeth=4).translate((1.68, 0, 0)), 'endgrain')
    a.join(lower_post.build_solid())
    upper_post = _sub(a)
    square_post(upper_post, rng, 0.50, -0.10, 1.56, 0.0, 0.0, slot='wood')
    upper_post.cut(jag(0.0, 1.2, 1.2, above=False, amplitude=0.09, teeth=4), 'endgrain')
    upper_post.cut(block((0.065, 1.05, 0.18), (-0.03, 0.75, 0.23), 0.008), 'endgrain')
    a.join(upper_post.build_solid().rotate((0, 0, 1.8)).translate((1.68, 1.88, 0)))
    # Askew iron collar rotated around its own center near fracture
    a.add(block((0.58, 0.14, 0.58), (0, 0, 0), 0.02).rotate((0, 0, -11)).translate((1.68, 1.86, 0)), 'iron_dark')
    # Snapped short right knee brace stub extending inward from right post
    stub = _sub(a)
    stub.add(mf.Manifold.batch_hull([
        block((0.25, 0.22, 0.34), (1.46, 2.72, 0), 0.025),
        block((0.25, 0.22, 0.34), (1.02, 3.16, 0), 0.025),
    ]), 'wood_dark')
    stub.cut(jag(0.0, 1.0, 1.0, above=True, amplitude=0.06, teeth=3).rotate((0, 0, 45)).translate((1.14, 3.04, 0)), 'endgrain')
    a.join(stub.build_solid())
    a.add(block((0.60, 0.30, 0.60), (0, 0, 0), 0.025).rotate((0, 0, 4)).translate((1.66, 3.34, 0)), 'iron_dark')

    # Broken lintel: two sagging halves anchored within x in [-1.96, +1.96] meeting in a jagged break at x ~ +0.04
    l_half = _sub(a)
    board(l_half, rng, (2.14, 0.46, 0.60), (1.03, 0.0, 0.0), 'wood', chips=2, wobble=0.1)
    l_half.cut(jag(0.0, 1.4, 1.4, above=False, amplitude=0.08, teeth=4).rotate((0, 0, 90)).translate((2.00, 0, 0)), 'endgrain')
    a.join(l_half.build_solid().rotate((0, 0, -6.0)).translate((-1.92, 3.64, 0)))

    r_half = _sub(a)
    board(r_half, rng, (2.10, 0.46, 0.60), (-1.01, 0.0, 0.0), 'wood_dark', chips=2, wobble=0.1)
    r_half.cut(jag(0.0, 1.4, 1.4, above=True, amplitude=0.08, teeth=4).rotate((0, 0, 90)).translate((-1.92, 0, 0)), 'endgrain')
    a.join(r_half.build_solid().rotate((0, 0, 8.0)).translate((1.92, 3.58, 0)))

    # Twisted hanging iron strap rotated about its own center at the sagging break (x ~ 0.04, y ~ 3.38)
    a.add(block((0.28, 0.40, 0.66), (0, 0, 0), 0.018).rotate((4, 8, -14)).translate((0.04, 3.38, 0)), 'iron_dark')
    # Only left cribbing blocks remain seated cleanly onto sagging left lintel
    for cx, cy in ((-1.52, 3.84), (-0.78, 3.76)):
        board(a, rng, (0.46, 0.16, 0.56), (cx, cy, 0), 'wood_dark', chips=1, wobble=0.4)
    return grounded(a.build())


# ---------------------------------------------------------------------------- winch (~1.5 m)

def winch():
    """Timber A-frame mining winch (~1.54 m W x 1.24 m D x 1.62 m H) with rope drum, crank, and hanging bucket."""
    a, rng = Assembly(), random('winch')
    # Heavy timber base skids & cross sills on y = 0
    for sx in (-0.54, 0.54):
        board(a, rng, (0.18, 0.16, 1.24), (sx, 0.08, 0.0), 'wood_dark', chips=1, wobble=0.3)
    for sz in (-0.48, 0.48):
        board(a, rng, (1.28, 0.14, 0.18), (0.0, 0.08, sz), 'wood_dark', chips=1, wobble=0.3)
    # Iron corner plates on skid corners
    for sx in (-0.54, 0.54):
        for sz in (-0.48, 0.48):
            a.add(block((0.21, 0.17, 0.21), (sx, 0.09, sz), 0.018), 'iron_dark')
            a.add(rivet((sx, 0.175, sz), 0.026, (0, 1, 0)), 'steel')

    # Twin timber A-frame trestles at x = +-0.54 rising to apex y = 1.56
    for sx in (-0.54, 0.54):
        for sz in (-0.42, 0.42):
            leg = mf.Manifold.batch_hull([
                block((0.15, 0.10, 0.15), (sx, 0.14, sz), 0.02),
                block((0.15, 0.10, 0.15), (sx, 1.52, 0.0), 0.02),
            ])
            a.add(leg, 'wood')
        # Mid horizontal crossbar on A-frame & top cap
        board(a, rng, (0.14, 0.14, 0.62), (sx, 0.78, 0.0), 'wood_dark', chips=1, wobble=0.3)
        board(a, rng, (0.20, 0.18, 0.26), (sx, 1.52, 0.0), 'wood_dark', chips=1, wobble=0.2)
        # Iron bearing collars at drum axle y = 1.18
        a.add(block((0.19, 0.22, 0.24), (sx, 1.18, 0.0), 0.02), 'iron_dark')
        a.add(rivet((sx, 1.24, 0.12), 0.024, (0, 0, 1)), 'steel')
        a.add(rivet((sx, 1.24, -0.12), 0.024, (0, 0, -1)), 'steel')

    # Top tie beam joining both A-frame peaks
    board(a, rng, (1.36, 0.16, 0.18), (0.0, 1.54, 0.0), 'wood', chips=2, wobble=0.2)

    # Horizontal axle, wooden drum barrel & iron flanges at y = 1.18, z = 0
    a.add(rod((-0.74, 1.18, 0.0), (0.76, 1.18, 0.0), 0.048, 8), 'iron')
    a.add(rod((-0.43, 1.18, 0.0), (0.43, 1.18, 0.0), 0.150, 12), 'wood')
    for sx in (-0.42, 0.42):
        flange = lathe([(0.02, -0.025), (0.23, -0.025), (0.23, 0.025), (0.02, 0.025)], 14)
        a.add(flange.rotate((0, 0, 90)).translate((sx, 1.18, 0.0)), 'iron_dark')
    # Coiled rope wraps along the drum core (x in -0.37..+0.37)
    base_coil = torus(0.165, 0.032, 0.0, 14, 6).rotate((0, 0, 90))
    for x_coil in np.linspace(-0.36, 0.36, 13):
        a.add(base_coil.translate((float(x_coil), 1.18, 0.0)), 'rope')

    # Ratchet gear wheel on -X end (x = -0.70)
    gear = facade(outline([
        (r * np.cos(a_i), r * np.sin(a_i))
        for i in range(16)
        for a_i, r in [(2 * np.pi * i / 16, 0.20 if i % 2 == 0 else 0.155)]
    ]), 0.055, -0.0275).rotate((0, 90, 0)).translate((-0.70, 1.18, 0.0))
    a.add(gear, 'iron')
    a.add(lathe([(0.01, -0.04), (0.08, -0.04), (0.08, 0.04), (0.01, 0.04)], 8)
          .rotate((0, 0, 90)).translate((-0.71, 1.18, 0.0)), 'steel')

    # Forged S-crank arm & light wood handle on +X end (x = +0.72..0.90)
    a.add(rod((0.72, 1.18, 0.0), (0.72, 0.90, 0.14), 0.036, 6), 'iron_dark')
    a.add(rod((0.72, 0.90, 0.14), (0.94, 0.90, 0.14), 0.034, 8), 'wood_light')
    a.add(ball((0.95, 0.90, 0.14), (0.042, 0.042, 0.042), 6), 'iron')

    # Taut vertical rope descending from drum front tangent (0, 1.18, 0.18) to hanging bucket
    a.add(rod((0.0, 1.18, 0.175), (0.0, 0.68, 0.175), 0.026, 7), 'rope')
    a.add(rod((0.0, 0.69, 0.175), (0.0, 0.60, 0.175), 0.020, 6), 'iron')
    a.add(loop((0, 0, 0), (0.040, 0.050), 0.018, 8).rotate((0, 90, 0)).translate((0.0, 0.645, 0.175)), 'iron')

    # Hanging wooden bucket suspended above the floor (y = 0.10..0.52)
    bx, bz, by = 0.0, 0.175, 0.10
    bucket_body = _sub(a)
    bucket_body.add(lathe([(0.20, by), (0.21, by + 0.04), (0.25, by + 0.40), (0.26, by + 0.44)], 14), 'wood')
    for k in range(10):
        ang = 360 * k / 10 + 18
        bucket_body.cut(block((0.02, 0.50, 0.08), (0, by + 0.25, 0.25), 0.004).rotate((0, ang, 0)), 'wood_dark')
    bucket_body.cut(post(0.20, by + 0.12, by + 0.60, 0, 0, 14), 'wood_dark')
    bucket_solid = bucket_body.build_solid().translate((bx, 0.0, bz))
    a.join(bucket_solid)
    # Iron hoops + bail handle connecting bucket rim up to rope hook
    for dy, r_h in ((0.08, 0.21), (0.33, 0.245)):
        a.add(ring(r_h - 0.015, r_h + 0.032, by + dy, by + dy + 0.055, 14).translate((bx, 0.0, bz)), 'iron')
    a.add(bend([
        (bx - 0.26, by + 0.40, bz),
        (bx - 0.18, by + 0.56, bz),
        (bx, by + 0.60, bz),
        (bx + 0.18, by + 0.56, bz),
        (bx + 0.26, by + 0.40, bz),
    ], [0.022] * 5, 6), 'iron')
    for s in (-1, 1):
        a.add(block((0.045, 0.09, 0.065), (bx + s * 0.255, by + 0.40, bz), 0.014), 'iron_dark')
    # Small ore chunks seated cleanly on the suspended bucket floor (by + 0.12 = 0.22)
    a.add(_rock_chunk((bx - 0.05, by + 0.17, bz + 0.02), (0.18, 0.15, 0.18), rng, 5, slot='rock_dark'), 'rock_dark')
    a.add(_rock_chunk((bx + 0.06, by + 0.18, bz - 0.03), (0.16, 0.14, 0.16), rng, 5, slot='ore_gold'), 'ore_gold')
    return grounded(a.build())


# ---------------------------------------------------------------------------- blasting powder kegs (~1.0 m)

def _build_powder_keg(parent, yaw_deg=0.0):
    """One chunky small powder keg (~0.48 m dia x 0.58 m H) with carved staves, iron hoops, and carved hazard X."""
    a = _sub(parent)
    profile = [(0.20, 0.0), (0.225, 0.08), (0.248, 0.29), (0.225, 0.50), (0.20, 0.58)]
    a.add(lathe(profile, 14), 'wood')
    # Real carved stave grooves
    for k in range(10):
        ang = 360 * k / 10 + 18
        a.cut(block((0.018, 0.64, 0.06), (0, 0.29, 0.248), 0.004).rotate((0, ang, 0)), 'wood_dark')
    # Recessed top & bottom barrel heads
    a.cut(post(0.175, 0.545, 0.66, sides=14), 'wood_dark')
    a.cut(post(0.175, -0.05, 0.025, sides=14), 'wood_dark')
    # Real carved hazard mark on front (+Z) stave face: recessed red circular badge + carved gold X
    a.cut(rod((0, 0.29, 0.215), (0, 0.29, 0.28), 0.092, 10), 'cloth_red')
    for turn in (45, -45):
        a.cut(block((0.135, 0.032, 0.06), (0, 0, 0), 0.005).rotate((0, 0, turn)).translate((0, 0.29, 0.245)), 'gold')
    # Dark iron hoops & rivets
    for y0, r in ((0.06, 0.225), (0.45, 0.225)):
        a.add(ring(r - 0.015, r + 0.024, y0, y0 + 0.060, 14), 'iron_dark')
        for k in range(4):
            ang = k * np.pi / 2 + np.pi / 4
            a.add(rivet((np.sin(ang) * (r + 0.024), y0 + 0.030, np.cos(ang) * (r + 0.024)), 0.018,
                        (np.sin(ang), 0, np.cos(ang))), 'steel')
    return a.build_solid().rotate((0, yaw_deg, 0))


def blasting_kegs():
    """Stack of 3 small powder kegs (~1.0 m across x 1.1 m H) with carved hazard emblems and coiled fuse."""
    a = Assembly()
    # Base pair of upright powder kegs staggered in XZ + third upper horizontal keg built from one template
    keg = _build_powder_keg(a)
    a.join(keg.rotate((0, 18, 0)).translate((-0.24, 0.0, 0.11)))
    a.join(keg.rotate((0, -22, 0)).translate((0.24, 0.0, -0.09)))
    # Wooden chock block on top rims cradling the upper horizontal keg
    a.add(block((0.42, 0.06, 0.16), (0, 0, 0), 0.012).rotate((0, -22, 0)).translate((0.0, 0.58, 0.01)), 'wood_dark')
    # Third keg lying horizontally diagonally across the saddle of the two base kegs
    a.join(keg.translate((0.0, -0.29, 0.0)).rotate((0, 0, -90)).rotate((0, -22, 0)).translate((0.0, 0.79, 0.01)))
    # Chunky wooden bung + coiled rope fuse resting on top of the upper keg (y ~ 1.03..1.18)
    a.add(post(0.044, 0.98, 1.06, 0.02, 0.02, 8), 'wood_dark')
    fuse_pts = []
    for t in np.linspace(0.4, 2.35 * np.pi, 18):
        r = 0.032 + 0.015 * t
        fuse_pts.append((0.02 + np.cos(t) * r, 1.045 + 0.008 * np.sin(2 * t), 0.02 + np.sin(t) * r))
    end_x, end_y, end_z = fuse_pts[-1]
    fuse_pts.extend([
        (end_x + 0.02, end_y + 0.06, end_z + 0.02),
        (end_x + 0.04, end_y + 0.11, end_z + 0.04),
    ])
    a.add(bend(fuse_pts, [0.022] * (len(fuse_pts) - 1) + [0.014], 6), 'rope')
    a.add(ball((end_x + 0.04, end_y + 0.115, end_z + 0.04), (0.026, 0.026, 0.026), 6), 'wick')
    return grounded(a.build())


# ---------------------------------------------------------------------------- dispatch

KINDS = {
    'mine_cart_empty': mine_cart_empty,
    'mine_cart_ore': mine_cart_ore,
    'mine_cart_tipped': mine_cart_tipped,
    'rail_track_straight': rail_track_straight,
    'rail_track_curve': rail_track_curve,
    'rail_track_end': rail_track_end,
    'ore_vein_a': ore_vein_a,
    'ore_vein_b': ore_vein_b,
    'ore_vein_crystal': ore_vein_crystal,
    'ore_pile_small': ore_pile_small,
    'ore_pile_large': ore_pile_large,
    'tool_rack': tool_rack,
    'pickaxe_loose': pickaxe_loose,
    'mine_lantern_wall': mine_lantern_wall,
    'mine_lantern_post': mine_lantern_post,
    'timber_frame_intact': timber_frame_intact,
    'timber_frame_cracked': timber_frame_cracked,
    'winch': winch,
    'blasting_kegs': blasting_kegs,
}
