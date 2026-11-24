"""Burial-ground props: graves with their markers, grave pedestals, an iron fence on stone pillars with its gateway,
and a family mausoleum.

Built from the chunky vocabulary in `furnishing_kit` on the dungeon texture. Units are Godot metres; floor props stand
on y = 0 with their front toward +Z, and a grave keeps its headstone toward -Z so it can stand back to a wall. Like the
pack's own pieces the stones are oversized and thick beside the 2.2 m hero: headstones reach about 1.6 m on a broad base.
Nothing is a plain box: walls and pillars are stacked from uneven, chipped stones, headstones and slabs are cracked,
mounds are lumpy and roof tiles are rounded, worn and broken.

Fence pieces are modules for `FencedAreaStep`: they run along X, exactly 4 m (segment) or 2 m (half) long, and the
corner's legs run 2 m along -X and +Z from a pillar on its origin; each is trimmed to its exact extent, whatever its
stones' wobble. Bars sit on one 0.25 m lattice, so they stay evenly spaced across joins. The gateway fills a 4 m
entrance, its leaves standing open toward -Z, into the fenced plot.
"""

import numpy as np
import manifold3d as mf
from items.solid import Assembly, lathe, loop
from items.furnishing_kit import (ball, bend, block, board, candle, crack, crack_path, cushion, facade, floor_plan,
                                  framed, lit, outline, random, rivet, rod, skull, stones, torus)


def _sub(parent):
    """Child assembly sharing `parent.slots`, for a part that is turned or tilted as a whole."""
    sub = Assembly()
    sub.slots = parent.slots
    return sub


def _chamfered(section, thickness, z=0.0, bevel=.05):
    """A convex stone section extruded `thickness` forward from z, with the pack's broad chamfer all round."""
    core = facade(section, thickness - 2 * bevel, z + bevel)
    face = facade(section.offset(-bevel, mf.JoinType.Miter), thickness, z)
    return mf.Manifold.batch_hull([core, face])


def _chip(a, rng, point, size, slot='stone_dark'):
    """A corner knocked off, showing the darker stone inside."""
    a.cut(block((size,) * 3, (0, 0, 0), size * .12).rotate(tuple(rng.uniform(20, 70, 3))).translate(tuple(point)), slot)


def _crack(a, rng, start, heading, length, z, frame=None, bends=4):
    """A jagged crack cut into the face at z (x across, y up), or turned onto another face by `frame`."""
    a.cut(framed(crack(crack_path(rng, start, heading, length, bends), z), frame), 'stone_dark')


def _top_crack(a, rng, x, z, top, heading, length):
    """A jagged crack cut down into a level top at height `top`, starting at (x, z)."""
    # Drawn on a face at z = top, then laid flat: the face's y runs along -Z.
    a.cut(crack(crack_path(rng, (x, -z), heading, length), top).rotate((-90, 0, 0)), 'stone_dark')


def _trim(a, lo=None, hi=None, axis=0, slot='stone'):
    """Cut away everything beyond lo/hi along `axis`, so a module ends exactly whatever its stones' wobble."""
    # The cutter stays about the prop's size: Manifold's tolerance grows with the bounds, and a huge one would fuse
    # nearly coplanar faces elsewhere.
    for bound, sign in ((lo, -1), (hi, 1)):
        if bound is not None:
            size, center = [8.0, 8.0, 8.0], [0.0, 2.0, 0.0]
            size[axis], center[axis] = 1.0, bound + sign * .5
            a.cut(block(tuple(size), tuple(center), 0), slot)


def _stack(a, rng, x, z, y, heights, width, slot='stone'):
    """Stones stacked into a pillar or shaft, each course a little off line like dry-laid masonry. Returns its top."""
    for h in heights:
        size = (width + rng.uniform(-.03, .03), h + .012, width + rng.uniform(-.03, .03))
        board(a, rng, size, (x + rng.uniform(-.02, .02), y + h / 2, z + rng.uniform(-.02, .02)), slot, chips=1, wobble=1.2,
              chip_slot='stone_dark')
        y += h
    return y


def _courses(rng, total, count):
    """`count` course heights summing to `total`, each a little different."""
    heights = rng.uniform(.8, 1.2, count)
    return heights / heights.sum() * total


def _arch(width, shoulder, y0=0.0, sides=10):
    """A round-topped section: straight sides up to `shoulder`, then a half circle."""
    r = width / 2
    top = [(np.cos(t) * r, shoulder + np.sin(t) * r) for t in np.linspace(0, np.pi, sides + 1)]
    return outline([(-r, y0), (r, y0)] + top)


# ---------------------------------------------------------------------------- graves

# The headstone stands on its base at z = HEAD; the bed runs from BED_Z toward +Z.
HEAD, BED_Z, BED_WIDTH, BED_LENGTH = -1.05, -.85, 1.3, 2.1


def _mound(a, rng, zc, spread):
    """Heaped earth: a low dome with lumps shoved up in it, and stones turned up on its surface."""
    a.add(ball((0, 0, zc), spread, 10).trim_by_plane((0, 1, 0), 0), 'soil')
    for _ in range(3):
        center = (rng.uniform(-.3, .3) * spread[0], 0, zc + rng.uniform(-.4, .4) * spread[2])
        radii = (spread[0] * rng.uniform(.45, .6), spread[1] * rng.uniform(.8, 1.05), spread[2] * rng.uniform(.3, .45))
        a.add(ball(center, radii, 8).trim_by_plane((0, 1, 0), 0), 'soil')


def _bed(a, rng, kind):
    """What lies in front of the marker: a curb of uneven stones around a heaped mound ('earth'), the curb closed by
    a cracked ledger slab with a cross cut in it ('ledger'), or a bare mound ringed with stones ('mound')."""
    w, length = BED_WIDTH, BED_LENGTH
    zc = BED_Z + length / 2
    if kind in ('earth', 'ledger'):
        for s in (-1, 1):
            # The long sides run down the bed: courses turned a quarter, their X along Z.
            stones(a, rng, -length / 2, length / 2, 0, .24, .18, 0, 1, 'stone', frame=((0, 90, 0), (s * (w / 2 - .09), 0, zc)),
                   span=(.45, .75))
        for z in (BED_Z + .09, BED_Z + length - .09):
            stones(a, rng, -w / 2 + .17, w / 2 - .17, 0, .22, .18, z, 1, 'stone', span=(.4, .55))
    if kind == 'ledger':
        board(a, rng, (w - .08, .14, length - .08), (0, .29, zc), 'stone', chips=3, wobble=.8, chip_slot='stone_dark')
        a.cut(block((.14, .08, 1.2), (0, .37, zc + .12), .01), 'stone_dark')
        a.cut(block((.66, .08, .14), (0, .37, zc - .2), .01), 'stone_dark')
        _top_crack(a, rng, .5, zc + .7, .36, 160, .9)
        return
    spread = (w / 2 - .06, .34, length / 2 - .04) if kind == 'earth' else (w / 2 + .02, .4, length / 2 + .02)
    _mound(a, rng, zc, spread)
    for _ in range(4 if kind == 'earth' else 9):
        t = rng.uniform(0, 2 * np.pi)
        r = rng.uniform(.55, .8) if kind == 'earth' else rng.uniform(.84, .94)
        x, z = np.cos(t) * spread[0] * r, zc + np.sin(t) * spread[2] * r
        y = spread[1] * np.sqrt(max(0, 1 - r * r)) * .8
        size = rng.uniform(.09, .15) if kind == 'earth' else rng.uniform(.13, .2)
        a.add(ball((0, 0, 0), (size, size * .7, size), 6).rotate((0, rng.uniform(0, 90), 0)).translate((x, max(y, size * .3), z)),
              'stone_dark')


def _base(a, rng):
    """Two settled stones of different heights under the marker."""
    board(a, rng, (.74, .22, .62), (-.33, .11, HEAD), 'stone_dark', chips=2, wobble=.8, chip_slot='stone_dark')
    board(a, rng, (.68, .19, .6), (.36, .095, HEAD + .01), 'stone_dark', chips=2, wobble=.8, chip_slot='stone_dark')


def _round_stone(a, rng):
    """A round-topped headstone with a skull carved in its arched panel, cracked and chipped about its rim."""
    front = HEAD + .15
    a.add(_chamfered(_arch(1.0, 1.08, .2), .3, HEAD - .15), 'stone')
    a.cut(facade(_arch(.66, 1.08, .5, 8), .2, front - .05), 'stone_dark')
    skull(a, 0, front - .12, 0, .7, 'stone', 'stone_dark', size=.72)
    a.cut(block((.6, .05, .1), (0, .36, front), .01), 'stone_dark')
    for t in rng.uniform(.3, 2.8, 2):
        _chip(a, rng, (np.cos(t) * .5, 1.08 + np.sin(t) * .5, front), rng.uniform(.12, .17))
    _crack(a, rng, (rng.uniform(-.25, .25), 1.58), rng.uniform(-100, -80), .7, front, bends=4)


def _cross_stone(a, rng):
    """A thick stone cross on a stepped plinth, one arm chipped and its shaft cracked."""
    board(a, rng, (.62, .22, .46), (0, .32, HEAD), 'stone', chips=2, wobble=.8, chip_slot='stone_dark')
    board(a, rng, (.34, 1.52, .3), (0, 1.16, HEAD), 'stone', chips=1, wobble=.4, chip_slot='stone_dark')
    board(a, rng, (1.06, .32, .3), (0, 1.44, HEAD), 'stone', chips=1, wobble=.4, chip_slot='stone_dark')
    a.cut(block((.06, .9, .06), (0, 1.3, HEAD + .15), .01), 'stone_dark')
    _chip(a, rng, (.53, 1.6, HEAD + .15), .22)
    _crack(a, rng, (.05, .5), rng.uniform(70, 85), .7, HEAD + .15, bends=3)


def _slab_stone(a, rng):
    """A tall gabled slab with a cross and two lines cut in its face, cracked across and chipped at its corners."""
    front = HEAD + .16
    a.add(_chamfered(outline([(-.48, .2), (.48, .2), (.48, 1.4), (0, 1.72), (-.48, 1.4)]), .32, HEAD - .16), 'stone')
    a.cut(block((.13, .6, .1), (0, 1.16, front), .01), 'stone_dark')
    a.cut(block((.44, .13, .1), (0, 1.3, front), .01), 'stone_dark')
    for y in (.72, .58):
        a.cut(block((.56, .045, .06), (0, y, front), .005), 'stone_dark')
    _chip(a, rng, (-.48, 1.36, front), .15)
    _chip(a, rng, (.48, .32, front), .12)
    _crack(a, rng, (.48, .95), rng.uniform(190, 205), .6, front, bends=3)


def _candles(a, spots, y):
    for i, (x, z, h, r) in enumerate(spots):
        candle(a, None, x, z, y, h, r, 2, i)


def _wood_cross(a, rng):
    """Two weathered boards nailed and lashed with rope, driven into the mound at a lean."""
    cross = _sub(a)
    board(cross, rng, (.2, 1.63, .13), (0, .835, 0), 'wood', chips=1, wobble=0)
    board(cross, rng, (.92, .2, .13), (0, 1.2, .1), 'wood', chips=1, wobble=0)
    for turn in (40, -40):
        cross.add(block((.34, .07, .2), (0, 0, 0), .02).rotate((0, 0, turn)).translate((0, 1.2, .08)), 'rope')
    for x in (-.32, .32):
        cross.add(rivet((x, 1.2, .165), .035), 'iron_dark')
    a.join(cross.build_solid().rotate((-6, 0, 5)).translate((0, 0, BED_Z + .3)))


MARKERS = {'round': _round_stone, 'cross': _cross_stone, 'slab': _slab_stone}


def grave(marker='round', bed='earth', lean=0.0):
    """A grave: its marker at the back (-Z) and its bed toward +Z, 1.4 x 2.6 m. A stone marker stands on two settled
    base stones and may `lean` back (degrees); 'wood' is a lashed wooden cross in a bare mound."""
    a, rng = Assembly(), random(f'grave_{marker}_{bed}_{lean}')
    _bed(a, rng, bed)
    if marker == 'wood':
        _wood_cross(a, rng)
        a.cut(block((8, 2, 8), (0, -1, 0), 0), 'soil')
        return a.build()
    _base(a, rng)
    stone = _sub(a)
    MARKERS[marker](stone, rng)
    pivot = np.array((0, .22, HEAD - .1))
    a.join(stone.build_solid().translate(tuple(-pivot)).rotate((-lean, 0, lean * .45)).translate(tuple(pivot)))
    if marker == 'slab':
        _candles(a, [(-.6, HEAD + .22, .34, .085), (.62, HEAD + .24, .22, .075)], .22)
    # Nothing sinks below the floor: a lean or a mound stone would poke through it.
    a.cut(block((8, 2, 8), (0, -1, 0), 0), 'soil')
    return a.build()


# ---------------------------------------------------------------------------- pedestal

PEDESTAL_TOP = 1.58
PEDESTAL_CANDLES = [(-.2, -.12, .42, .11), (.17, -.16, .3, .1), (.02, .2, .22, .1)]


def grave_pedestal(candles=False, flames=False):
    """A stone grave pedestal: uneven base slabs, a shaft of two stacked stones with a skull in an arched niche, a
    chipped cornice and either a lidded urn or melted candles on top (`flames` builds only their flame tips)."""
    a, rng, tips = Assembly(), random('grave_pedestal'), []
    board(a, rng, (1.12, .24, 1.1), (0, .12, 0), 'stone_dark', chips=3, wobble=.8, chip_slot='stone_dark')
    board(a, rng, (.92, .16, .9), (0, .32, 0), 'stone', chips=2, wobble=.8, chip_slot='stone_dark')
    _stack(a, rng, 0, 0, .39, (.56, .44), .7)
    a.cut(facade(_arch(.4, .74, .5, 8), .2, .27), 'stone_dark')
    skull(a, 0, .2, 0, .58, 'stone', 'stone_dark', size=.46)
    _crack(a, rng, (.35, 1.2), rng.uniform(190, 210), .45, 0, ((0, 90, 0), (0, 0, 0)), bends=3)
    _crack(a, rng, (-.2, 1.38), rng.uniform(-110, -95), .4, .35, bends=3)
    board(a, rng, (.96, .14, .96), (0, 1.43, 0), 'stone', chips=3, wobble=.6, chip_slot='stone_dark')
    board(a, rng, (.8, .1, .8), (0, 1.53, 0), 'stone', chips=2, wobble=.6, chip_slot='stone_dark')
    if candles:
        for i, (x, z, h, r) in enumerate(PEDESTAL_CANDLES):
            candle(a, tips, x, z, PEDESTAL_TOP - .01, h, r, 3, i)
        # Wax pooled on the top and run down over the cornice's edge.
        a.add(cushion((.6, .05, .5), (0, PEDESTAL_TOP, 0), .5), 'wax')
        for x, length in ((-.18, .34), (.22, .24)):
            a.add(ball((x, PEDESTAL_TOP - length * .45, .455), (.036, length * .55, .036), 6), 'wax')
        _trim(a, lo=0, axis=1, slot='stone_dark')
        return lit(a, tips, flames)
    a.add(lathe([(.18, PEDESTAL_TOP - .01), (.26, PEDESTAL_TOP + .08), (.3, PEDESTAL_TOP + .28), (.22, PEDESTAL_TOP + .46),
                 (.14, PEDESTAL_TOP + .52), (.18, PEDESTAL_TOP + .56)], 10), 'stone')
    a.add(lathe([(.2, PEDESTAL_TOP + .55), (.2, PEDESTAL_TOP + .6), (.08, PEDESTAL_TOP + .7), (.01, PEDESTAL_TOP + .78)], 10),
          'stone')
    _chip(a, rng, (.28, PEDESTAL_TOP + .3, .1), .12)
    _trim(a, lo=0, axis=1, slot='stone_dark')
    return a.build()


# ---------------------------------------------------------------------------- iron fence

BAR = .25
FENCE_TOP = 1.5


def _pillar(a, rng, x, height=1.95, width=.56, top=True):
    """A stone fence pillar: an exact base, a shaft of three uneven stacked stones, a chipped cap and a pyramid top
    set a little askew."""
    a.add(block((width + .16, .3, width + .16), (x, .15, 0)), 'stone_dark')
    y = _stack(a, rng, x, 0, .29, _courses(rng, height - .45, 3), width)
    board(a, rng, (width + .1, .16, width + .1), (x, y + .06, 0), 'stone', chips=2, wobble=1.5, chip_slot='stone_dark')
    if top:
        a.add(lathe([(width * .52, y + .12), (.02, y + .38)], 4).rotate((rng.uniform(-3, 3), 45 + rng.uniform(-8, 8), 0))
              .translate((x, 0, 0)), 'stone')
    _crack(a, rng, (x + rng.uniform(-.1, .1), y - .05), rng.uniform(-100, -80), .5, width / 2 + .01, bends=3)


def _spear(x, top, z=0.0):
    return lathe([(.014, top - .04), (.1, top + .07), (.014, top + .28)], 4).rotate((0, 45, 0)).translate((x, 0, z))


def _bars(a, rng, x0, x1, skip=(), gap=None):
    """Iron bars with spear tips between two rails over a curb of uneven stones, from x0 to x1 along X. Across a `gap`
    (lo, hi) the top rail is snapped and its bars bent outward or gone."""
    mid, length = (x0 + x1) / 2, x1 - x0
    stones(a, rng, x0, x1, 0, .26, .4, 0, 1, 'stone', span=(.6, 1.1))
    for y in (.5, 1.28):
        if gap and y > 1:
            for lo, hi in ((x0, gap[0]), (gap[1], x1)):
                a.add(block((hi - lo, .1, .11), ((lo + hi) / 2, y, 0), .02), 'iron_dark')
        else:
            # Rails stand proud of the bars: faces flush with them would fuse badly under later cuts.
            a.add(block((length, .1, .11), (mid, y, 0), .02), 'iron_dark')
    for k in range(int(round(length / BAR))):
        x = x0 + BAR * (k + .5)
        if any(lo <= x <= hi for lo, hi in skip):
            continue
        if gap and gap[0] < x < gap[1]:
            # Bars wrenched out of the broken top rail, bent outward or gone.
            if rng.random() < .6:
                lean = rng.uniform(.3, .6)
                a.add(bend([(x, .22, 0), (x, .55, 0), (x, .95, lean * .3), (x, 1.3, lean)], [.045] * 4, 4), 'iron')
            continue
        a.add(block((.09, FENCE_TOP - .2, .09), (x, (.2 + FENCE_TOP) / 2, 0), .02), 'iron')
        a.add(_spear(x, FENCE_TOP), 'iron')


def graveyard_fence(broken=False):
    """A 4 m fence module: bars over a curb with a stone pillar in its middle; `broken` bends and drops bars under a
    snapped top rail and knocks a corner off the pillar's cap."""
    a, rng = Assembly(), random(f'graveyard_fence_{broken}')
    _bars(a, rng, -2, 2, skip=[(-.36, .36)], gap=(-1.8, -.7) if broken else None)
    _pillar(a, rng, 0)
    if broken:
        _chip(a, rng, (.3, 1.84, .3), .26)
    _trim(a, -2, 2)
    _trim(a, lo=0, axis=1)
    return a.build()


def graveyard_fence_half():
    """A 2 m fence module of bars alone, closing runs that are not a multiple of 4 m."""
    a, rng = Assembly(), random('graveyard_fence_half')
    _bars(a, rng, -1, 1)
    _trim(a, -1, 1)
    _trim(a, lo=0, axis=1)
    return a.build()


def graveyard_fence_corner():
    """A pillar on the origin with 2 m legs of bars along -X and +Z."""
    a, rng = Assembly(), random('graveyard_fence_corner')
    leg = _sub(a)
    _bars(leg, rng, -2, 0, skip=[(-.36, .36)])
    _trim(leg, -2, .36)
    shape = leg.build_solid()
    a.join(shape)
    a.join(shape.rotate((0, 90, 0)))
    _pillar(a, rng, 0)
    _trim(a, lo=0, axis=1)
    return a.build()


def _leaf(a, rng, side, width=1.18, height=1.9):
    """One gate leaf hung from a pillar at x = side * 1.25, swung open toward -Z."""
    leaf = _sub(a)
    for x in (.04, width - .04):
        leaf.add(block((.09, height - .12, .09), (x, .06 + (height - .12) / 2, 0), .02), 'iron_dark')
    for y in (.32, height - .34):
        leaf.add(block((width, .1, .11), (width / 2, y, 0), .02), 'iron_dark')
    for k in range(5):
        x = .14 + k * (width - .28) / 4
        leaf.add(block((.065, height - .2, .065), (x, .1 + (height - .2) / 2, 0), .015), 'iron')
        leaf.add(_spear(x, height - .1), 'iron')
    leaf.add(loop((width / 2, height * .58, 0), (.2, .2), .05, 12), 'iron')
    for y in (.5, height - .5):
        leaf.add(rod((-.1, y, 0), (.12, y, 0), .05, 6), 'iron_dark')
    shape = leaf.build_solid()
    if side > 0:
        shape = shape.mirror((1, 0, 0))
    a.join(shape.rotate((0, 90 * -side, 0)).translate((side * 1.25, 0, 0)))


def graveyard_gate():
    """The plot's 4 m gateway: stacked stone pillars at the entrance's ends carrying a round arch of uneven voussoirs,
    its keystone carved with a skull, and two iron gate leaves standing open toward -Z, into the plot."""
    a, rng = Assembly(), random('graveyard_gate')
    spring, inner, outer = 2.0, 1.28, 1.74
    for s in (-1, 1):
        _pillar(a, rng, s * 1.6, height=spring + .1, width=.64, top=False)
        _leaf(a, rng, s)
    edges = np.linspace(0, np.pi, 10)
    for k, (t0, t1) in enumerate(zip(edges[:-1], edges[1:])):
        # Neighbours overlap a little, so the stones bond into one arch with a chamfered joint between them; each
        # stands a little proud or short of its neighbours.
        t0, t1 = max(t0 - .02, 0), min(t1 + .02, np.pi)
        reach = outer + (.1 if k == 4 else rng.uniform(-.05, .06))
        ring = [(np.cos(t) * inner, spring + np.sin(t) * inner) for t in (t0, t1)]
        ring += [(np.cos(t) * reach, spring + np.sin(t) * reach) for t in (t1, t0)]
        bulge = .1 if k == 4 else rng.uniform(0, .04)
        a.add(_chamfered(outline(ring), .5 + 2 * bulge, -.25 - bulge, .04), 'stone')
    skull(a, 0, .3, 0, spring + (inner + outer) / 2 - .13, 'stone', 'stone_dark', size=.45)
    _chip(a, rng, (-1.45, spring + .45, .25), .16)
    _chip(a, rng, (1.1, spring + 1.25, .25), .14)
    _crack(a, rng, (-1.0, spring + 1.1), rng.uniform(-150, -130), .5, .26, bends=3)
    _trim(a, -2, 2)
    return a.build()


# ---------------------------------------------------------------------------- mausoleum

def _shingle(length, width, thickness=.09, bevel=.025):
    """A roof tile lying flat, chamfered, its butt rounded toward +X."""
    r = width / 2
    arc = [(length / 2 - r + np.cos(t) * r, np.sin(t) * r) for t in np.linspace(-np.pi / 2, np.pi / 2, 7)]
    section = outline([(-length / 2, -r)] + arc + [(-length / 2, r)])
    core = floor_plan(section, thickness - 2 * bevel, bevel)
    face = floor_plan(section.offset(-bevel, mf.JoinType.Miter), thickness, 0)
    return mf.Manifold.batch_hull([core, face])


def _tiles(a, rng, side, eave, ridge, half_span, z0, z1, courses=5):
    """One roof slope from the eave at x = side * half_span up to the ridge on x = 0: round-butted terracotta tiles of
    uneven widths in staggered courses along Z, their butts kicked up over a dark underlay that shows in the joints.
    Some are worn darker, some broken at the butt, a few gone."""
    rise = ridge - eave
    pitch = np.degrees(np.arctan2(rise, half_span))
    slope = np.hypot(half_span, rise)
    a.add(block((slope + .1, .18, z1 - z0), (0, 0, (z0 + z1) / 2), .04).rotate((0, 0, -side * pitch))
          .translate((side * half_span / 2, (eave + ridge) / 2 - .06, 0)), 'clay_dark')
    step = slope / courses
    for c in range(courses):
        u = (c + .5) * step
        z = z0 - .22 * (c % 2)
        while z < z1 - .12:
            width = min(rng.uniform(.36, .5), z1 + .02 - z)
            roll = rng.random()
            if width > .2 and roll > .05:
                tile = _shingle(step + .16, width - .035)
                if roll < .12:
                    corner = (step * .5 + .06, 0, rng.choice((-1, 1)) * width * .42)
                    tile = tile - block((.26, .4, .26), (0, 0, 0), 0).rotate((0, rng.uniform(25, 65), 0)).translate(corner)
                if side < 0:
                    tile = tile.rotate((0, 180, 0))
                tile = tile.rotate((rng.uniform(-2, 2), rng.uniform(-3, 3), side * (5 + rng.uniform(-1.5, 1.5) - pitch)))
                position = (side * (half_span - u * half_span / slope), eave + u * rise / slope + .05, z + width / 2)
                a.add(tile.translate(position), 'clay_worn' if rng.random() < .2 else 'clay')
            z += width


def _flagstones(a, rng, x0, x1, z0, z1, y):
    """Uneven paving: rows of chipped flags of different sizes, a little tilted, laid on a surface at height y."""
    z = z0
    while z < z1 - .1:
        d = min(rng.uniform(.55, .8), z1 - z)
        x = x0
        while x < x1 - .1:
            w = min(rng.uniform(.6, 1.0), x1 - x)
            board(a, rng, (w - .05, .09, d - .05), (x + w / 2, y + .02, z + d / 2), 'stone', chips=1, wobble=1.5,
                  chip_slot='stone_dark')
            x += w
        z += d


def mausoleum():
    """A family tomb, back flat to a wall: a house of staggered stone courses with quoined corners on a paved plinth,
    a sealed and cracked stone door in a dark doorway, a two-column porch under a pediment with a carved skull, and a
    gable roof of worn, round-butted tiles under a ridge of capping tiles and a cross."""
    a, rng = Assembly(), random('mausoleum')
    back, width, depth = -2.45, 3.6, 3.7
    front, zc = back + depth, back + depth / 2
    porch = front + 1.0
    # The plinth: a dark slab, flagstones over the porch and a step of two stones.
    board(a, rng, (4.4, .26, porch - back + .3), (0, .13, (back + porch + .3) / 2), 'stone_dark', chips=4, wobble=0,
          chip_slot='stone_dark')
    _flagstones(a, rng, -2.12, 2.12, front - .1, porch + .24, .26)
    board(a, rng, (1.25, .16, .5), (-.6, .08, porch + .52), 'stone_dark', chips=2, wobble=1.5, chip_slot='stone_dark')
    board(a, rng, (1.1, .13, .46), (.62, .065, porch + .5), 'stone_dark', chips=2, wobble=1.5, chip_slot='stone_dark')
    # The house: a hidden core faced with courses of uneven stones over a dark footing course, quoins at its corners.
    a.add(block((width - .2, 2.3, depth - .1), (0, 1.45, back + (depth - .1) / 2)), 'stone_dark')
    stones(a, rng, -width / 2 + .3, width / 2 - .3, .3, .54, .26, front - .13, 1, 'stone_dark', span=(.6, 1.0))
    stones(a, rng, -width / 2 + .3, width / 2 - .3, .54, 2.48, .22, front - .11, 6, 'stone', span=(.45, .8))
    for s in (-1, 1):
        frame = ((0, 90 * s, 0), (s * (width / 2 - .11), 0, zc))
        stones(a, rng, -depth / 2 + .3, depth / 2 - .3, .3, .54, .26, -.02, 1, 'stone_dark', frame, span=(.6, 1.0))
        stones(a, rng, -depth / 2 + .3, depth / 2 - .3, .54, 2.48, .22, 0, 6, 'stone', frame, span=(.45, .8))
    for x in (-1, 1):
        for z, inward in ((front, -1), (back, 1)):
            y = .3
            for k, h in enumerate(_courses(rng, 2.2, 6)):
                along, across = (.72, .46) if k % 2 == 0 else (.46, .72)
                center = (x * (width / 2 - along / 2 + .03), y + h / 2, z + inward * (across / 2 - .03))
                board(a, rng, (along, h + .012, across), center, 'stone', chips=1, wobble=.8, chip_slot='stone_dark')
                y += h
    board(a, rng, (width + .34, .24, depth + .18), (0, 2.6, back + (depth + .18) / 2), 'stone', chips=4, wobble=0,
          chip_slot='stone_dark')
    _crack(a, rng, (-1.25, 2.4), rng.uniform(-85, -70), 1.0, front)
    _crack(a, rng, (.6, 2.45), rng.uniform(-100, -85), .9, 0, ((0, 90, 0), (width / 2, 0, zc)))
    _crack(a, rng, (-.4, 1.0), rng.uniform(-20, 20), .8, 0, ((0, -90, 0), (-width / 2, 0, zc)))
    # The doorway: a dark recess with the sealed stone door set back in it under a cracked lintel.
    a.cut(block((1.5, 2.0, .6), (0, 1.3, front), .02), 'socket')
    board(a, rng, (1.3, 1.86, .16), (0, 1.25, front - .25), 'stone_dark', chips=2, wobble=.6, chip_slot='stone_dark')
    a.cut(block((.12, .9, .06), (0, 1.4, front - .17), .01), 'socket')
    a.cut(block((.5, .12, .06), (0, 1.62, front - .17), .01), 'socket')
    _crack(a, rng, (-.62, .55), rng.uniform(20, 40), .7, front - .17, bends=3)
    a.add(torus(.11, .025, 0, 10, 6).rotate((90, 0, 0)).translate((.4, 1.1, front - .15)), 'iron')
    a.add(rivet((.4, 1.22, front - .17), .05), 'iron_dark')
    board(a, rng, (1.9, .3, .32), (0, 2.38, front + .04), 'stone', chips=2, wobble=.6, chip_slot='stone_dark')
    _crack(a, rng, (.3, 2.52), rng.uniform(-80, -60), .3, front + .2, bends=2)
    # The porch: two columns under a beam, one chipped at its foot.
    for x in (-1.3, 1.3):
        a.add(lathe([(.34, .29), (.34, .42), (.24, .48), (.22, 1.6), (.2, 2.2), (.28, 2.28), (.34, 2.38),
                     (.34, 2.5)], 10).translate((x, 0, porch - .4)), 'stone')
    _chip(a, rng, (1.6, .42, porch - .2), .18)
    _chip(a, rng, (-1.0, 2.42, porch - .2), .14)
    board(a, rng, (3.4, .3, 1.2), (0, 2.6, front + .4), 'stone', chips=3, wobble=0, chip_slot='stone_dark')
    # Roof: gable along Z over the house and the porch; a pediment closes each end.
    eave, ridge, span = 2.78, 3.85, 2.2
    z0, z1 = back, porch + .15
    for side in (-1, 1):
        _tiles(a, rng, side, eave, ridge, span, z0, z1)
    gable = outline([(-span + .12, eave - .05), (span - .12, eave - .05), (0, ridge - .08)])
    a.add(facade(gable, .3, porch - .45), 'stone')
    a.add(facade(gable, depth + .6, back), 'stone')
    a.cut(facade(outline([(-1.3, eave + .04), (1.3, eave + .04), (0, ridge - .26)]), .1, porch - .2), 'stone_dark')
    skull(a, 0, porch - .26, 0, eave + .02, 'stone', 'stone_dark', size=.52)
    _crack(a, rng, (1.5, eave + .05), rng.uniform(110, 130), .5, porch - .15, bends=3)
    # Capping tiles along the ridge, each set a little off line, and the cross at its front end.
    z = z0
    while z < z1 - .05:
        length = min(rng.uniform(.42, .55), z1 - z)
        cap = rod((0, 0, 0), (0, 0, length + .04), .15, 6).rotate((0, 0, 30)).translate((rng.uniform(-.02, .02), ridge + .03, z))
        a.add(cap, 'clay_worn')
        z += length
    a.add(block((.15, .5, .13), (0, ridge + .26, z1 - .25), .03), 'stone')
    a.add(block((.4, .13, .13), (0, ridge + .35, z1 - .25), .03), 'stone')
    _chip(a, rng, (.2, ridge + .4, z1 - .2), .08)
    # Two tiles slipped off onto the porch.
    for x, turn in ((-1.75, 35), (1.85, -70)):
        a.add(_shingle(.6, .42).rotate((0, turn, 0)).translate((x, .3, porch)), 'clay')
    for x, z in ((-1.5, back + .5), (1.4, front - .6), (-.5, porch + .2)):
        _chip(a, rng, (x, .26, z), .12)
    # The back stays one flat face against the wall.
    a.cut(block((10, 10, 2), (0, 0, back - 1), 0), 'stone_dark')
    _trim(a, lo=0, axis=1, slot='stone_dark')
    return a.build()


KINDS = {
    'grave': grave, 'grave_pedestal': grave_pedestal, 'graveyard_fence': graveyard_fence,
    'graveyard_fence_half': graveyard_fence_half, 'graveyard_fence_corner': graveyard_fence_corner,
    'graveyard_gate': graveyard_gate, 'mausoleum': mausoleum,
}
