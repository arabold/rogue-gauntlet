"""Room furnishings: altars, statues, vessels, racks, rugs and wall decor for generated rooms.

Built from the KayKit-style vocabulary in `furnishing_kit`: chunky chamfered boards with
grooves and chipped corners, stacked stone blocks, square posts, oversized rivets, candles
and heads. Units are Godot metres at the KayKit dungeon scale (4-unit tiles, 1-unit table
tops). Floor props stand on y = 0 centered on the origin with their front toward +Z, so
back-to-wall props keep a flat back on their -Z side. Wall props hang with a flat back on
z = 0 at their final height above the floor (their room Prop mounts at height 0).

Candle-bearing designs with `flames` build only their flame tips, a separate emissive model
the game's lit scene shows (and hides on dark floors) together with its light. Fire bowls
and hearths hold dead coals; their lit scenes add the fire.
"""

import zlib

import numpy as np
import manifold3d as mf
from core import kit
from items.solid import Assembly, lathe, loop
from items.furnishing_kit import (block, cushion, post, rod, ball, bend, framed, board, planks, square_post, stones,
                                  rivet, candle, lit, outline, rect, star, disc, facade, floor_plan, jag, painted, bands,
                                  turned, moved, grounded, ring, chain)

COLORS = {
    'stone': (128, 136, 141), 'stone_dark': (88, 94, 99), 'marble': (178, 183, 186), 'marble_dark': (128, 133, 137),
    'rock': (112, 98, 90), 'rock_dark': (84, 72, 66),
    'wood': (159, 94, 71), 'wood_dark': (126, 66, 48), 'wood_light': (204, 138, 98), 'endgrain': (210, 154, 106),
    'bark': (106, 66, 46),
    'iron': (98, 106, 112), 'iron_dark': (66, 72, 78), 'steel': (154, 166, 172), 'gold': (244, 172, 64),
    'silver': (208, 214, 218),
    'wax': (238, 230, 216), 'wick': (52, 44, 40), 'flame': (255, 204, 96), 'coal': (60, 52, 50),
    'cloth_red': (196, 46, 50), 'cloth_blue': (76, 132, 196), 'cloth_green': (66, 132, 84), 'cloth_purple': (120, 72, 150),
    'cloth_yellow': (244, 180, 66), 'trim': (240, 186, 76), 'sheet': (238, 232, 222), 'linen': (218, 192, 150),
    'leather': (118, 70, 46), 'parchment': (234, 214, 168), 'ink': (96, 64, 50), 'straw': (222, 184, 98),
    'straw_dark': (180, 140, 74), 'rope': (184, 150, 102), 'bone': (232, 222, 194), 'socket': (70, 58, 52),
    'clay': (194, 110, 72), 'clay_dark': (96, 56, 44), 'glaze': (70, 128, 166), 'glaze_light': (222, 204, 160),
    'liquid': (130, 218, 98), 'ocean': (76, 134, 182), 'land': (214, 186, 116),
    'crust': (198, 116, 58), 'crumb': (244, 220, 164), 'cheese': (250, 208, 92), 'rind': (222, 156, 56),
    'meat': (202, 98, 82), 'herb': (102, 150, 74), 'lavender': (156, 114, 190),
}
_MATTE = {'metallic': 0, 'roughness': .9, 'crease': 45}
MATERIALS = {
    **{slot: {'metallic': .5, 'roughness': .38, 'crease': 40} for slot in ('steel', 'gold', 'silver')},
    **{slot: {'metallic': .35, 'roughness': .6, 'crease': 40} for slot in ('iron', 'iron_dark')},
    **{slot: {'metallic': 0, 'roughness': .95, 'crease': 35} for slot in ('stone', 'stone_dark', 'marble', 'marble_dark',
                                                                          'rock', 'rock_dark')},
    **{slot: {'metallic': 0, 'roughness': .9, 'crease': 35} for slot in ('wood', 'wood_dark', 'wood_light', 'endgrain', 'bark')},
    **{slot: _MATTE for slot in ('wax', 'wick', 'coal', 'cloth_red', 'cloth_blue', 'cloth_green', 'cloth_purple',
                                 'cloth_yellow', 'trim', 'sheet', 'linen', 'leather', 'parchment', 'ink', 'straw',
                                 'straw_dark', 'rope', 'bone', 'socket', 'clay', 'clay_dark', 'land', 'crust', 'crumb',
                                 'cheese', 'rind', 'meat', 'herb', 'lavender')},
    **{slot: {'metallic': .05, 'roughness': .35, 'crease': 50} for slot in ('glaze', 'glaze_light', 'ocean')},
    **{slot: {'emissive': True, 'crease': 60} for slot in ('flame', 'liquid')},
}


def random(name):
    """Each design's own reproducible generator."""
    return np.random.default_rng(zlib.crc32(name.encode()))


def around(count, phase=np.pi / 2):
    """Unit directions spread around the Y axis, the first toward +Z."""
    return [np.array([np.cos(phase + k * 2 * np.pi / count), 0, np.sin(phase + k * 2 * np.pi / count)]) for k in range(count)]


# ---------------------------------------------------------------------------- chapel

def altar():
    a, rng = Assembly(), random('altar')
    stones(a, rng, -1.46, 1.46, 0, .2, 1.56, 0, 1, 'stone_dark', span=(.7, 1.1))
    stones(a, rng, -1.2, 1.2, .2, 1.04, 1.2, 0, 2, 'stone', span=(.55, .9))
    for x in (-.74, .74):
        board(a, rng, (1.49, .24, 1.62), (x, 1.15, 0), 'stone', chips=2, wobble=0, chip_slot='stone_dark')
    a.cut(block((1.36, .52, .3), (0, .62, .68), .03), 'stone_dark')
    a.add(facade(star(8, .27, .15, (0, .62)), .09, .5), 'gold')
    a.add(facade(disc(.12, (0, .62), 12), .12, .5), 'gold')
    # The runner lies in a shallow channel, flush with the top, so candles on the slab stand level with it.
    a.cut(block((.96, .1, 1.8), (0, 1.27, 0), .01), 'stone_dark')
    a.add(block((.92, .07, 1.7), (0, 1.235, 0), .02), 'cloth_red')
    a.add(block((.92, .78, .07), (0, .88, .84), .025), 'cloth_red')
    a.add(block((.98, .12, .1), (0, .5, .84), .03), 'trim')
    for x in (-.3, 0, .3):
        a.add(lathe([(.04, .28), (.08, .34), (.06, .46), (.045, .5)], 6).translate((x, 0, .84)), 'trim')
    return a.build()


def candelabra(flames=False):
    """A stubby wrought-iron floor candelabra: claw tripod, knobbed stem, three fat candles."""
    a, tips = Assembly(), []
    for d in around(3):
        a.add(bend([(0, .5, 0), tuple(d * .26 + (0, .4, 0)), tuple(d * .44 + (0, .18, 0)), tuple(d * .5 + (0, .08, 0))],
                   [.12, .11, .1, .09], 6), 'iron')
        a.add(ball(tuple(d * .54 + (0, .08, 0)), (.16, .09, .16), 8), 'iron_dark')
    a.add(lathe([(.24, .34), (.3, .44), (.27, .58), (.12, .7)], 10), 'iron')
    a.add(post(.1, .6, 1.72, sides=8), 'iron')
    for y in (.95, 1.32):
        a.add(lathe([(.1, y - .11), (.22, y - .03), (.22, y + .03), (.1, y + .11)], 8), 'gold')
    for s in (-1, 1):
        a.add(bend([(0, 1.44, 0), (s * .26, 1.44, 0), (s * .46, 1.52, 0), (s * .52, 1.64, 0)], [.085] * 4, 6), 'iron')
    cups = [(-.52, 1.64), (.52, 1.64), (0, 1.76)]
    for x, y in cups:
        a.add(lathe([(.07, y - .09), (.25, y), (.28, y + .06), (.16, y + .08)], 10).translate((x, 0, 0)), 'gold')
    for i, (x, y) in enumerate(cups):
        candle(a, tips, x, 0, y + .06, .32 + .1 * (i == 2), .14, 2, i)
    return lit(a, tips, flames)


def candle_cluster(flames=False):
    """Three fat candles on a puddle of their own wax, for tables and altars."""
    a, tips = Assembly(), []
    a.add(lathe([(.42, 0), (.44, .025), (.38, .06), (.2, .08), (.01, .085)], 14).scale((1, 1, .85)), 'wax')
    for i, (x, z, h, r) in enumerate([(-.1, -.06, .62, .16), (.17, .04, .44, .13), (-.03, .2, .3, .12)]):
        candle(a, tips, x, z, .03, h, r, 2, i)
    return lit(a, tips, flames)


def candle_pile(flames=False):
    """A shrine heap of candles of every height on a mound of melted wax."""
    a, tips = Assembly(), []
    a.add(lathe([(.8, 0), (.78, .07), (.62, .16), (.36, .22), (.01, .24)], 16).scale((1, 1, .85)), 'wax')
    spots = [(0, -.06, .95, .18), (-.36, -.12, .68, .15), (.34, -.16, .78, .15), (-.24, .26, .48, .14),
             (.27, .24, .4, .13), (.58, .06, .3, .12), (-.6, .06, .34, .12), (.02, .4, .26, .12)]
    for i, (x, z, h, r) in enumerate(spots):
        candle(a, tips, x, z, max(.02, .2 - .24 * np.hypot(x, z / .85)), h, r, 2, i)
    return lit(a, tips, flames)


def lectern():
    a, rng = Assembly(), random('lectern')
    board(a, rng, (1.1, .16, .3), (0, .08, 0), 'wood_dark', chips=2)
    board(a, rng, (.3, .16, .9), (0, .09, 0), 'wood_dark', chips=2)
    square_post(a, rng, .28, .12, 1.12)
    board(a, rng, (.46, .2, .46), (0, 1.17, 0), 'wood_dark')
    desk = ((22, 0, 0), (0, 1.3, 0))
    planks(a, rng, 1.16, .8, .11, desk, count=3)
    board(a, rng, (1.2, .12, .1), (0, .08, .42), 'wood_dark', desk)
    a.add(framed(block((1.0, .06, .66), (0, .08, -.02), .02), desk), 'leather')
    for s in (-1, 1):
        a.add(framed(block((.46, .11, .58), (0, 0, 0), .035).rotate((0, 0, s * 6)).translate((s * .235, .14, -.02)), desk),
              'parchment')
    for s in (-1, 1):
        for z in (-.18, -.08, .02, .12):
            a.cut(framed(block((.28, .05, .022), (s * .25, .2, z), .005), desk), 'ink')
    a.add(framed(block((.07, .03, .36), (.03, .17, .28), .008), desk), 'cloth_red')
    return a.build()


def plinth(a, rng):
    """Stepped stone statue base; its top is at y = .87."""
    stones(a, rng, -.78, .78, 0, .2, 1.56, 0, 1, 'stone_dark', span=(.7, 1.0))
    stones(a, rng, -.62, .62, .2, .7, 1.24, 0, 1, 'stone', span=(.55, .7))
    for x in (-.36, .36):
        board(a, rng, (.74, .18, 1.46), (x, .78, 0), 'stone', chips=2, chip_slot='stone_dark')


def figure(a, pose):
    """A KayKit-proportioned robed figure on a plinth: big head, short body. `praying` (hooded) or `sword` (helmed)."""
    base, head = .84, 2.3
    a.add(lathe([(.6, base), (.58, base + .1), (.5, base + .5), (.43, base + .85), (.46, base + 1.0), (.36, base + 1.1),
                 (.16, base + 1.16)], 12).scale((1, 1, .86)), 'marble')
    a.add(ring(.5, .66, base, base + .1, 12).scale((1, 1, .88)), 'marble')
    a.add(ring(.4, .51, base + .52, base + .62, 12).scale((1, 1, .88)), 'marble_dark')
    if pose == 'praying':
        a.add(lathe([(.46, base + 1.02), (.48, base + 1.4), (.4, base + 1.72), (.22, base + 1.9), (.05, base + 1.96)], 12), 'marble')
        a.cut(ball((0, head, .32), (.29, .32, .27), 12), 'marble_dark')
        a.add(ball((0, head - .02, .04), (.32, .34, .32), 12), 'marble')
        for s in (-1, 1):
            a.add(bend([(s * .42, base + 1.0, 0), (s * .5, base + .78, .12), (s * .3, base + .62, .42), (s * .1, base + .7, .52)],
                       [.17, .18, .2, .18]), 'marble')
        a.add(block((.32, .34, .18), (0, 0, 0), .05).rotate((-15, 0, 0)).translate((0, base + .8, .54)), 'marble')
    else:
        a.add(ball((0, head, .02), (.3, .32, .3), 12), 'marble')
        a.add(lathe([(.36, head - .1), (.38, head + .08), (.32, head + .26), (.16, head + .38), (.02, head + .41)], 12), 'marble')
        a.cut(block((.42, .07, .3), (0, head + .02, .33), .02), 'marble_dark')
        a.add(block((.07, .3, .5), (0, head + .36, 0), .03), 'marble')
        for s in (-1, 1):
            a.add(ball((s * .44, base + 1.02, 0), (.27, .21, .27), 12), 'marble')
            a.add(bend([(s * .44, base + .98, 0), (s * .5, base + .72, .14), (s * .3, base + .6, .44), (s * .14, base + .68, .58)],
                       [.15, .14, .14, .13]), 'marble')
        a.add(block((.24, .68, .08), (0, base + .33, .66), .03), 'marble')
        a.add(block((.7, .12, .16), (0, base + .72, .66), .04), 'marble')
        a.add(post(.07, base + .72, base + .92, 0, .66, 8), 'marble')
        a.add(ball((0, base + .96, .66), (.12, .12, .12), 10), 'marble')
        for s in (-1, 1):
            a.add(ball((s * .12, base + .82, .64), (.15, .13, .15), 10), 'marble')


def statue(pose):
    a = Assembly()
    plinth(a, random('plinth'))
    figure(a, pose)
    return a.build()


def statue_broken():
    """The praying statue snapped at the knees: its stump stands, the rest lies beside the plinth."""
    stump = Assembly()
    plinth(stump, random('plinth'))
    figure(stump, 'praying')
    stump.cut(jag(1.38, 1.6, 1.6, above=True).rotate((0, 30, 0)), 'marble_dark')
    upper = Assembly()
    figure(upper, 'praying')
    upper.cut(jag(1.38, 1.6, 1.6, above=False).rotate((0, 30, 0)), 'marble_dark')
    fallen = turned(turned(upper.build(), -86, 'z'), -12, 'y')
    fallen = moved(fallen, (.86 - fallen.verts[:, 0].min(), -fallen.verts[:, 1].min(), .1 - fallen.verts[:, 2].mean()))
    return kit.Piece.merge(stump.build(), fallen)


def brazier():
    """An iron fire bowl of dead coals; the lit scene adds the fire and its light."""
    a, rng = Assembly(), random('brazier')
    a.add(lathe([(.22, .72), (.52, .78), (.7, .92), (.76, 1.06), (.66, 1.08), (.58, .98), (.4, .9), (.01, .89)], 12), 'iron')
    a.add(ring(.7, .8, .98, 1.1, 12), 'iron_dark')
    for d in around(8, 0):
        a.add(ball(tuple(d * .8 + (0, 1.04, 0)), (.045, .045, .045), 6), 'steel')
    for d in around(3):
        a.add(bend([tuple(d * .4 + (0, .82, 0)), tuple(d * .52 + (0, .5, 0)), tuple(d * .58 + (0, .2, 0)),
                    tuple(d * .62 + (0, .06, 0))], [.09, .085, .08, .08], 4), 'iron')
        a.add(block((.24, .08, .24), tuple(d * .64 + (0, .04, 0))), 'iron_dark')
    a.add(ring(.46, .58, .26, .34, 12), 'iron')
    for i in range(10):
        t, r = rng.uniform(0, 2 * np.pi), rng.uniform(0, .42)
        size = rng.uniform(.15, .22)
        a.add(ball((np.cos(t) * r, .93 + .1 * (1 - r / .42), np.sin(t) * r), (size, size * .7, size), 7), 'coal')
    return a.build()


# ---------------------------------------------------------------------------- hall

def vase_tall():
    a = Assembly()
    a.add(lathe([(.3, 0), (.33, .04), (.31, .1), (.24, .12)], 12), 'glaze')
    body = lathe([(.24, .1), (.36, .3), (.46, .56), (.47, .66), (.44, .86), (.3, 1.04), (.22, 1.16), (.21, 1.24)], 12)
    a.add(bands(body, 'clay', [(.56, .86, 'glaze')]))
    a.add(lathe([(.23, 1.2), (.33, 1.27), (.34, 1.37), (.27, 1.42)], 12), 'glaze')
    a.cut(post(.17, 1.28, 1.6), 'clay_dark')
    for s in (-1, 1):
        a.add(bend([(s * .38, .92, 0), (s * .54, 1.08, 0), (s * .48, 1.26, 0), (s * .24, 1.28, 0)], [.075] * 4, 6), 'clay')
    return a.build()


URN = [(.36, .08), (.54, .28), (.62, .52), (.58, .76), (.44, .92), (.38, .98)]


def urn_wide():
    a = Assembly()
    a.add(lathe([(.38, 0), (.41, .05), (.36, .1)], 14), 'glaze_light')
    a.add(bands(lathe(URN, 14), 'glaze', [(.28, .52, 'glaze_light')]))
    a.add(lathe([(.38, .94), (.47, .99), (.47, 1.07), (.4, 1.1)], 14), 'glaze_light')
    a.add(lathe([(.44, 1.06), (.46, 1.12), (.32, 1.22), (.12, 1.26), (.01, 1.27)], 14), 'glaze')
    a.add(lathe([(.08, 1.24), (.14, 1.32), (.11, 1.4), (.01, 1.42)], 8), 'glaze_light')
    for s in (-1, 1):
        a.add(loop((0, 0, 0), (.13, .11), .06, 10).rotate((0, 90, 0)).translate((s * .62, .7, 0)), 'glaze')
    return a.build()


def vase_broken():
    """The lower half of the wide urn with a jagged rim, and shards of its shell around it."""
    shell = lathe(URN, 14) - lathe([(.3, .16), (.48, .3), (.54, .52), (.5, .76), (.36, .9), (.3, 1.2)], 14)
    paint = lambda shape: bands(shape, 'glaze', [(.28, .52, 'glaze_light')])
    a = Assembly()
    a.add(lathe([(.38, 0), (.41, .05), (.36, .1)], 14), 'glaze_light')
    a.add(paint(shell))
    a.cut(jag(.6, 1.6, 1.6, above=True, amplitude=.09, teeth=4).rotate((0, 20, 0)), 'clay')
    pieces = [a.build()]
    for i, (t, y, x, z, turn) in enumerate([(.6, .72, .86, .42, 30), (2.5, .8, -.8, .5, -50), (4.2, .68, .14, .92, 80)]):
        shard = Assembly()
        shard.add(paint(shell ^ block((.4, .3, .4), (np.cos(t) * .56, y, np.sin(t) * .56), .01)))
        piece = turned(turned(moved(shard.build(), -np.array([np.cos(t) * .56, y, np.sin(t) * .56])), 70 + 15 * i, 'x'), turn, 'y')
        pieces.append(grounded(piece, x, z))
    return kit.Piece.merge(*pieces)


def bust():
    a, rng = Assembly(), random('bust')
    stones(a, rng, -.5, .5, 0, .18, 1.0, 0, 1, 'stone_dark', span=(.5, .6))
    square_post(a, rng, .56, .18, 1.25, slot='stone')
    board(a, rng, (.86, .16, .86), (0, 1.3, 0), 'stone', chips=2, chip_slot='stone_dark')
    a.add(cushion((1.0, .44, .58), (0, 1.56, 0), .5), 'marble')
    a.add(post(.15, 1.7, 1.86, sides=10), 'marble')
    a.add(ball((0, 2.08, .02), (.32, .34, .32), 12), 'marble')
    a.add(block((.1, .14, .12), (0, 2.04, .33), .03), 'marble')
    a.add(ball((0, 2.16, -.04), (.33, .3, .32), 12), 'marble')
    a.add(loop((0, 0, 0), (.34, .36), .07, 16).rotate((90, 0, 0)).translate((0, 2.2, -.02)), 'gold')
    for s in (-1, 1):
        a.add(ball((s * .3, 2.22, .1), (.08, .05, .1), 6), 'gold')
    return a.build()


def armor_stand():
    a, rng = Assembly(), random('armor_stand')
    board(a, rng, (1.1, .16, .3), (0, .08, 0), 'wood_dark', chips=2)
    board(a, rng, (.3, .16, .8), (0, .09, 0), 'wood_dark', chips=2)
    square_post(a, rng, .18, .16, 2.05)
    board(a, rng, (1.0, .14, .16), (0, 1.82, 0))
    a.add(lathe([(.36, 1.12), (.42, 1.3), (.45, 1.55), (.47, 1.76), (.38, 1.9), (.18, 1.97)], 12).scale((1, 1, .74)), 'steel')
    a.add(ring(.3, .44, 1.13, 1.25, 12).scale((1, 1, .78)), 'leather')
    a.add(block((.18, .15, .06), (0, 1.19, .34), .02), 'gold')
    for y0 in (.92, 1.02):
        a.add(ring(.28, .44, y0, y0 + .12, 12).scale((1, 1, .78)), 'steel')
    for s in (-1, 1):
        a.add(ball((s * .48, 1.86, 0), (.25, .17, .25), 10), 'steel')
        for k in (-1, 0, 1):
            a.add(ball((s * (.48 + .1 * k * s * 0), 2.02, k * .12), (.04, .04, .04), 6), 'iron_dark')
    a.add(lathe([(.24, 2.0), (.27, 2.14), (.26, 2.34), (.18, 2.46), (.02, 2.5)], 12), 'steel')
    a.cut(block((.34, .05, .2), (0, 2.24, .25), .015), 'iron_dark')
    a.add(cushion((.12, .3, .5), (0, 2.56, -.04), .4), 'cloth_red')
    return a.build()


def rug(width, length, field, border, accent, pattern):
    """A thick woven rug: border, field and motifs partition one layer; chunky tassels on the short ends."""
    a = Assembly()
    thickness = .05
    whole, inner = rect(width, length), rect(width - .36, length - .36)
    motifs = mf.CrossSection()
    if pattern == 'medallion':
        motifs = star(4, min(width, length) * .28, min(width, length) * .16)
        for z in (-length * .32, length * .32):
            motifs = motifs + star(4, .24, .13, (0, z))
    elif pattern == 'runner':
        for z in np.arange(-length / 2 + .9, length / 2 - .5, 1.2):
            motifs = motifs + star(4, .36, .19, (0, z))
    elif pattern == 'rings':
        r = min(width, length)
        motifs = disc(r * .3) - disc(r * .2) + disc(r * .1)
    weave = lambda section, slot: a.add(floor_plan(section.offset(.003, mf.JoinType.Miter), thickness), slot)
    weave(whole - inner, border)
    weave(inner - rect(width - .5, length - .5), accent)
    weave(rect(width - .5, length - .5) - motifs, field)
    if not motifs.is_empty():
        weave(motifs, accent)
    for z in (-length / 2, length / 2):
        for x in np.arange(-width / 2 + .12, width / 2 - .05, .17):
            a.add(block((.08, .035, .22), (x, .018, z + np.sign(z) * .07), .012), 'sheet')
    return a.build()


# ---------------------------------------------------------------------------- library and alchemist

def globe():
    a, rng = Assembly(), random('globe')
    board(a, rng, (1.0, .14, .24), (0, .07, 0), 'wood_dark', chips=2)
    board(a, rng, (.24, .14, 1.0), (0, .08, 0), 'wood_dark', chips=2)
    a.add(lathe([(.2, .14), (.14, .24), (.11, .4), (.16, .55), (.1, .7), (.12, .82), (.2, .9)], 10), 'wood')
    center = np.array([0, 1.34, 0])

    def land(c, n):
        d = c - center
        d /= np.linalg.norm(d, axis=1, keepdims=True)
        field = (np.sin(d[:, 0] * 4.1 + 1.3) * np.cos(d[:, 2] * 3.3) + .7 * np.sin(d[:, 1] * 5.2 + d[:, 0] * 2.1)
                 + .5 * np.cos(d[:, 2] * 6.7 - d[:, 1] * 3.1))
        return [(field > .55, 'land')]
    a.add(painted(mf.Manifold.sphere(.44, 24).translate(tuple(center)), 'ocean', land))
    a.add(loop((0, 0, 0), (.54, .54), .08, 24).translate(tuple(center)), 'gold')
    a.add(lathe([(.07, 1.84), (.1, 1.9), (.06, 1.97), (.01, 1.99)], 8), 'gold')
    return a.build()


def cauldron():
    a = Assembly()
    a.add(lathe([(.36, .26), (.58, .36), (.7, .6), (.68, .86), (.58, 1.0)], 14), 'iron')
    a.add(ring(.52, .72, .96, 1.1, 14), 'iron_dark')
    a.add(ring(.6, .73, .56, .66, 14), 'iron_dark')
    for d in around(8, 0):
        a.add(ball(tuple(d * .73 + (0, .61, 0)), (.045, .045, .045), 6), 'steel')
    a.cut(post(.54, .84, 1.4, sides=14), 'iron_dark')
    a.add(post(.56, .8, .86, sides=14), 'liquid')
    rng = random('cauldron')
    for _ in range(5):
        t, r = rng.uniform(0, 2 * np.pi), rng.uniform(.08, .4)
        size = rng.uniform(.07, .11)
        a.add(ball((np.cos(t) * r, .86, np.sin(t) * r), (size, size * .8, size), 7), 'liquid')
    for d in around(3):
        a.add(lathe([(.12, 0), (.1, .2), (.15, .38)], 8).translate(tuple(d * .42)), 'iron')
    for s in (-1, 1):
        a.add(loop((0, 0, 0), (.15, .13), .065, 10).rotate((0, 90, 0)).translate((s * .74, .92, 0)), 'iron')
    return a.build()


def scroll_rack():
    """A plank cubby case stuffed with scroll rolls, back to the wall."""
    a, rng = Assembly(), random('scroll_rack')
    width, height, depth = 2.1, 2.3, .66
    for s in (-1, 1):
        board(a, rng, (.13, height, depth), (s * (width / 2 - .065), height / 2, 0), chips=2)
    board(a, rng, (width + .1, .14, depth + .06), (0, height + .03, 0), 'wood_dark', chips=3)
    board(a, rng, (width, .16, depth), (0, .08, 0), 'wood_dark', chips=2)
    floors = [.16, .67, 1.19, 1.71]
    for y in floors[1:]:
        board(a, rng, (width - .2, .1, depth - .04), (0, y - .05, .01), chips=2)
    for x in (-.35, .35):
        board(a, rng, (.1, height - .2, depth - .06), (x, height / 2 + .02, .01), chips=1)
    edges = np.linspace(-width / 2 + .06, width / 2 - .06, 5)
    for x0, x1 in zip(edges[:-1], edges[1:]):
        board(a, rng, (x1 - x0 + .006, height - .1, .07), ((x0 + x1) / 2, height / 2, -depth / 2 + .045), 'wood_dark', chips=0)
    for x in (-.64, 0, .64):
        for y in floors:
            count = int(rng.integers(2, 6))
            for sx, level in [(-.11, 0), (.11, 0), (0, 1), (-.22, 0), (.22, 0)][:count]:
                r = .1
                if abs(x + sx) > .9:
                    continue
                cy = y + r - .01 + level * .15
                front = .3 + rng.uniform(-.05, .03)
                a.add(rod((x + sx, cy, -.26), (x + sx, cy, front), r, 9), 'parchment' if rng.random() < .7 else 'linen')
                a.add(rod((x + sx, cy, front - .01), (x + sx, cy, front + .04), .055, 7), 'wood_dark')
                if rng.random() < .35:
                    a.add(rod((x + sx, cy, .06), (x + sx, cy, .12), r * 1.12, 8), 'cloth_red')
    return a.build()


def herb_rack():
    a, rng = Assembly(), random('herb_rack')
    board(a, rng, (1.9, .22, .12), (0, 2.52, .06), chips=3, wobble=.3)
    herbs = ['herb', 'lavender', 'straw_dark', 'herb', 'lavender']
    for i, x in enumerate(np.linspace(-.7, .7, 5)):
        a.add(rod((x, 2.5, .1), (x, 2.53, .32), .05), 'wood_dark')
        a.add(rod((x, 2.52, .27), (x, 2.22, .27), .02, 5), 'rope')
        top = 2.26 - .05 * (i % 2)
        a.add(lathe([(.06, top - .5), (.2, top - .42), (.17, top - .18), (.08, top)], 8).translate((x, 0, .27)), herbs[i])
        a.add(ring(.06, .11, top - .1, top - .02, 8).translate((x, 0, .27)), 'rope')
    return a.build()


# ---------------------------------------------------------------------------- kitchen

def log(a, start, end, radius):
    """A split log with pale end grain."""
    start, end = np.asarray(start, float), np.asarray(end, float)
    axis = (end - start) / np.linalg.norm(end - start)
    a.add(painted(rod(tuple(start), tuple(end), radius, 8), 'bark', lambda c, n: [(np.abs(n @ axis) > .8, 'endgrain')]))


def hearth():
    """A stone fireplace with a timber mantel, logs and a hanging pot, back to the wall; the lit scene adds the fire."""
    a, rng = Assembly(), random('hearth')
    stones(a, rng, -1.7, 1.7, 0, .22, 1.6, -.02, 1, 'stone_dark', span=(.7, 1.1))
    for s in (-1, 1):
        stones(a, rng, s * 1.18 - .34, s * 1.18 + .34, .22, 2.1, 1.3, -.15, 4, 'stone', span=(.68, .7))
    stones(a, rng, -1.62, 1.62, 2.1, 2.48, 1.44, -.08, 1, 'stone', span=(.6, .9))
    board(a, rng, (3.5, .16, .5), (0, 2.56, .55), 'wood_dark', chips=3)
    for i, (width, depth) in enumerate([(3.0, 1.2), (2.6, 1.0), (2.2, .85), (1.8, .72)]):
        y0 = 2.48 + .3 * i
        stones(a, rng, -width / 2, width / 2, y0, y0 + .3, depth, -.8 + depth / 2, 1, 'stone', span=(.5, .8))
    a.add(block((1.8, 1.9, .25), (0, 1.15, -.68)), 'stone_dark')
    for s in (-1, 1):
        a.add(block((.1, .34, .56), (s * .55, .38, -.22), .03), 'iron_dark')
    log(a, (-.7, .44, -.1), (.66, .5, -.34), .17)
    log(a, (-.62, .48, -.42), (.7, .44, -.06), .16)
    log(a, (-.3, .74, -.28), (.4, .72, -.2), .15)
    for _ in range(9):
        a.add(ball((rng.uniform(-.75, .75), .27, rng.uniform(-.55, .15)), (.16, .08, .16), 7), 'coal')
    end = chain(a, 0, 2.02, 4, -.34)
    a.add(loop((0, 0, 0), (.32, .14), .045, 12).translate((0, end - .2, -.34)), 'iron')
    a.add(lathe([(.26, .9), (.36, .98), (.4, 1.14), (.36, 1.28), (.38, 1.34)], 12).translate((0, 0, -.34)), 'iron')
    return a.build()


def chopping_block():
    a, rng = Assembly(), random('chopping_block')
    stump = lathe([(.56, 0), (.5, .08), (.46, .3), (.47, .72), (.5, .8)], 10)
    a.add(painted(stump, 'bark', lambda c, n: [(n[:, 1] > .7, 'endgrain')]))
    for k, d in enumerate(around(4, .4)):
        a.add(ball(tuple(d * .5 + (0, .13, 0)), (.22, .13, .22), 8), 'bark')
    for r in (.2, .36):
        a.cut(ring(r, r + .03, .785, .9, 12), 'wood_dark')
    a.add(block((.08, .38, .64), (0, 0, 0), .02).rotate((0, 0, 10)).translate((.08, .94, 0)), 'steel')
    a.add(rod((.1, 1.06, .3), (.15, 1.1, .7), .06), 'wood_dark')
    for z in (.42, .58):
        a.add(ball((.1, 1.08, z), (.075, .03, .03), 6), 'steel')
    return a.build()


def pot_rack():
    a = Assembly()
    for s in (-1, 1):
        a.add(block((.14, .16, .5), (s * .86, 2.64, .25)), 'iron_dark')
        a.add(rivet((s * .86, 2.73, .12), .05, (0, 1, 0)), 'steel')
    a.add(rod((-1.0, 2.64, .42), (1.0, 2.64, .42), .06), 'iron')
    for x in (-.55, 0, .55):
        a.add(rod((x, 2.66, .42), (x, 2.38, .42), .03, 6), 'iron')
    a.add(block((.1, .5, .06), (-.55, 2.16, .42), .02), 'iron')
    a.add(lathe([(.32, 0), (.34, .08), (.3, .1), (.01, .05)], 14).rotate((90, 0, 0)).translate((-.55, 1.62, .42)), 'iron')
    a.add(loop((0, 0, 0), (.17, .15), .04, 12).translate((0, 2.26, .42)), 'iron')
    a.add(lathe([(.18, 1.8), (.27, 1.88), (.29, 2.04), (.25, 2.14), (.28, 2.18)], 12).translate((0, 0, .42)), 'iron')
    a.add(rod((.55, 2.4, .42), (.55, 1.86, .42), .04, 6), 'wood')
    a.add(ball((.55, 1.8, .44), (.14, .09, .14), 8), 'wood')
    return a.build()


def cheese_wheel():
    a = Assembly()
    a.add(lathe([(.42, 0), (.45, .04), (.45, .26), (.42, .3), (.01, .31)], 16), 'rind')
    a.cut(floor_plan(outline([(0, 0), (.6, -.27), (.6, .27)]), .5, -.1), 'cheese')
    for x, y, z in [(.25, .16, -.08), (.34, .08, .1), (.16, .22, .05)]:
        a.cut(ball((x, y, z), (.05, .05, .05), 8), 'rind')
    return a.build()


def ham():
    """A KayKit-style roast leg: a fat glazed drumstick with a two-knobbed bone."""
    a = Assembly()
    a.add(lathe([(.1, 0), (.22, .1), (.34, .32), (.34, .5), (.22, .66), (.05, .7)], 12).rotate((0, 0, -90)).scale((1, .86, .9))
          .translate((-.24, .3, 0)), 'crust')
    a.add(rod((-.18, .3, 0), (-.56, .33, 0), .07), 'bone')
    for dz in (-.07, .07):
        a.add(ball((-.6, .34, dz), (.1, .1, .1), 8), 'bone')
    return a.build()


def bread():
    a = Assembly()
    for x, z, sx in [(-.14, -.06, .36), (.22, .14, .3)]:
        a.add(ball((x, .17, z), (sx, .17, .2), 12), 'crust')
    for x in (-.32, -.14, .04):
        a.cut(block((.05, .08, .3), (0, 0, 0), .015).rotate((0, 30, 0)).translate((x, .34, -.06)), 'crumb')
    a.add(ball((-.04, .12, .26), (.16, .12, .16), 10), 'crust')
    return a.build()


# ---------------------------------------------------------------------------- barracks and guardroom

def weapon_rack():
    a, rng = Assembly(), random('weapon_rack')
    board(a, rng, (2.3, .18, .76), (0, .09, 0), 'wood_dark', chips=2)
    for s in (-1, 1):
        square_post(a, rng, .2, .18, 2.3, s * 1.03, -.24, cap=.08)
    board(a, rng, (2.3, .16, .2), (0, 2.2, -.24), chips=2)
    board(a, rng, (2.1, .14, .36), (0, 1.08, -.14), chips=2)
    for x in (-.68, .68):
        a.add(post(.06, .18, 2.25, x, -.12, 8), 'wood_dark')
        a.add(ring(.04, .09, 2.18, 2.28, 8).translate((x, 0, -.12)), 'iron')
        a.add(lathe([(.08, 2.24), (.11, 2.32), (.17, 2.44), (.13, 2.62), (.01, 2.8)], 4).scale((1, 1, .45)).translate((x, 0, -.12)), 'steel')
    for x in (-.28, .28):
        a.add(block((.18, 1.06, .06), (x, .7, -.12), .025), 'steel')
        a.add(block((.5, .1, .12), (x, 1.24, -.12), .035), 'iron')
        a.add(post(.06, 1.28, 1.52, x, -.12, 8), 'leather')
        a.add(ball((x, 1.56, -.12), (.1, .1, .1), 8), 'iron')
    a.add(post(.055, .18, 1.94, 0, -.12, 8), 'wood_dark')
    a.add(facade(outline([(0, 1.5), (.36, 1.4), (.46, 1.66), (.36, 1.92), (0, 1.82)]), .07, -.155), 'steel')
    a.add(facade(outline([(0, 1.58), (-.18, 1.66), (0, 1.76)]), .06, -.15), 'iron')
    return a.build()


def training_dummy():
    a, rng = Assembly(), random('training_dummy')
    board(a, rng, (1.1, .14, .26), (0, .07, 0), 'wood_dark', chips=2)
    board(a, rng, (.26, .14, 1.1), (0, .08, 0), 'wood_dark', chips=2)
    square_post(a, rng, .2, .14, 2.0)
    a.add(lathe([(.3, .86), (.4, 1.0), (.42, 1.5), (.34, 1.74), (.16, 1.84)], 10), 'straw')
    for y in (1.06, 1.42):
        a.add(ring(.36, .46, y, y + .09, 10), 'rope')
    board(a, rng, (1.5, .16, .16), (0, 1.6, 0), chips=2)
    for s in (-1, 1):
        a.add(rod((s * .5, 1.6, 0), (s * .72, 1.6, 0), .15, 8), 'straw')
        a.add(rod((s * .58, 1.6, 0), (s * .64, 1.6, 0), .17, 8), 'rope')
    a.add(ball((0, 2.12, 0), (.3, .32, .29), 10), 'linen')
    a.add(ring(.1, .2, 1.82, 1.92, 8), 'rope')
    for s in (-1, 1):
        for turn in (45, -45):
            a.cut(block((.14, .035, .1), (0, 0, 0), .01).rotate((0, 0, turn)).translate((s * .12, 2.16, .27)), 'wick')
    a.cut(block((.22, .03, .1), (0, 1.98, .27), .01), 'wick')
    return a.build()


def bunk_bed():
    """Two stacked bunks along Z: capped posts, plank rails and headboards at -Z against the wall, a side ladder."""
    a, rng = Assembly(), random('bunk_bed')
    for x in (-.74, .74):
        for z in (-1.46, 1.46):
            square_post(a, rng, .22, 0, 2.56, x, z, cap=.08)
    for level, y in enumerate((.5, 1.7)):
        for x in (-.74, .74):
            board(a, rng, (.14, .24, 2.8), (x, y, 0), 'wood_dark', chips=2)
        for z in (-1.46, 1.46):
            board(a, rng, (1.3, .24, .12), (0, y, z), 'wood_dark', chips=1)
        board(a, rng, (1.3, .2, .1), (0, y + .34, -1.46), chips=1)
        a.add(cushion((1.46, .26, 2.84), (0, y + .2, 0), .3), 'sheet')
        a.add(cushion((1.5, .16, 1.9), (0, y + .32, .46), .3), 'cloth_yellow' if level else 'cloth_blue')
        a.add(cushion((.86, .22, .46), (0, y + .42, -1.08), .55), 'sheet')
    for z in (.72, 1.22):
        board(a, rng, (.1, 2.3, .1), (.86, 1.15, z), chips=1)
    for y in (.45, .85, 1.25, 1.65, 2.05):
        board(a, rng, (.08, .08, .56), (.88, y, .97), chips=0)
    return a.build()


def war_map():
    a = Assembly()
    a.add(block((1.4, .04, .96), (0, .02, 0), .012), 'parchment')
    for s in (-1, 1):
        a.add(rod((s * .7, .07, -.48), (s * .7, .07, .48), .075), 'parchment')
    coast = outline([(-.5, -.38), (-.2, -.3), (-.05, -.12), (.18, -.2), (.38, .02), (.3, .3), (.05, .36), (-.12, .2), (-.38, .26), (-.52, .1)])
    a.cut(floor_plan(coast - coast.offset(-.035), .03, .025), 'ink')
    for x, z in [(-.36, .02), (-.22, .08), (-.08, .12), (.06, .06), (.18, -.02)]:
        a.cut(floor_plan(rect(.08, .035, (x, z)), .03, .025), 'ink')
    a.cut(floor_plan(star(4, .12, .04, (.44, -.32)), .03, .025), 'ink')
    for x, z, slot in [(-.36, .02, 'cloth_red'), (.2, -.04, 'cloth_blue'), (.02, .22, 'cloth_blue')]:
        a.add(lathe([(.06, .03), (.05, .08), (.02, .22)], 6).translate((x, 0, z)), 'iron')
        a.add(ball((x, .24, z), (.06, .06, .06), 7), slot)
    return a.build()


# ---------------------------------------------------------------------------- prison and ruins

def shackles():
    a = Assembly()
    a.add(block((1.4, .34, .1), (0, 2.5, .05)), 'iron_dark')
    for x in (-.6, -.2, .2, .6):
        a.add(rivet((x, 2.5, .1), .055), 'steel')
    for x, links in ((-.4, 5), (.4, 4)):
        a.add(rod((x, 2.4, .06), (x, 2.4, .2), .045), 'iron')
        end = chain(a, x, 2.34, links, .16)
        a.add(loop((0, 0, 0), (.15, .15), .06, 12).rotate((0, 90 * (links % 2), 0)).translate((x, end - .22, .16)), 'iron')
    return a.build()


def straw_pile():
    a, rng = Assembly(), random('straw_pile')
    a.add(lathe([(.95, 0), (.86, .09), (.6, .24), (.3, .34), (.01, .37)], 11).scale((1, 1, .72)), 'straw')
    for i in range(20):
        t, r = rng.uniform(0, 2 * np.pi), rng.uniform(.1, .8)
        y = .37 * (1 - (r / .95) ** 1.6) - .04
        stalk = block((.055, .035, rng.uniform(.45, .75)), (0, 0, 0), .01)
        stalk = stalk.rotate((rng.uniform(-14, 6), rng.uniform(0, 180), 0)).translate((np.cos(t) * r, max(y, .025), np.sin(t) * r * .72))
        a.add(stalk, 'straw_dark' if i % 3 == 0 else 'straw')
    return a.build()


def bucket():
    a = Assembly()
    a.add(lathe([(.3, 0), (.31, .05), (.37, .62), (.38, .68)], 14), 'wood')
    for d in around(10, .2):
        angle = np.degrees(np.arctan2(d[0], d[2]))
        a.cut(block((.025, .8, .12), (0, 0, 0), .005).rotate((0, angle, 0)).translate(tuple(d * .37 + (0, .4, 0))), 'wood_dark')
    a.cut(post(.3, .12, .9, sides=14), 'wood_dark')
    for y0 in (.1, .48):
        r = .31 + .06 * y0 / .62
        a.add(ring(r - .02, r + .05, y0, y0 + .09, 14), 'iron')
    a.add(bend([(np.cos(t) * .4, .62 + np.sin(t) * .34, 0) for t in np.linspace(0, np.pi, 9)], [.03] * 9, 5), 'iron')
    for s in (-1, 1):
        a.add(block((.06, .14, .1), (s * .39, .62, 0), .02), 'iron')
    return a.build()


def bone(a, start, end, radius=.07):
    start, end = np.asarray(start, float), np.asarray(end, float)
    a.add(rod(tuple(start), tuple(end), radius, 6), 'bone')
    side = np.cross(end - start, (0, 1, 0))
    side = side / max(np.linalg.norm(side), 1e-6) * radius * .9
    for p in (start, end):
        for s in (-1, 1):
            a.add(ball(tuple(p + s * side), (radius * 1.35,) * 3, 6), 'bone')


def skull(a, x, z, turn, y=0.0):
    """A chunky skull with real eye sockets, added to `a` as one solid."""
    head = Assembly()
    head.slots = a.slots
    head.add(ball((0, .24, 0), (.24, .21, .26), 10), 'bone')
    head.add(block((.28, .12, .16), (0, .1, .14), .04), 'bone')
    for s in (-1, 1):
        head.cut(ball((s * .09, .24, .22), (.065, .07, .07), 7), 'socket')
    head.cut(block((.06, .06, .08), (0, .16, .25), .015), 'socket')
    a.join(head.build_solid().rotate((0, turn, 0)).translate((x, y, z)))


def bone_pile():
    a, rng = Assembly(), random('bone_pile')
    skull(a, -.2, .12, 20)
    skull(a, .3, -.12, -35, .02)
    for _ in range(8):
        t = rng.uniform(0, np.pi)
        length = rng.uniform(.5, .72)
        cx, cz = rng.uniform(-.42, .42), rng.uniform(-.34, .34)
        d = np.array([np.cos(t), 0, np.sin(t)]) * length / 2
        y = rng.uniform(.07, .18)
        bone(a, (cx - d[0], y, cz - d[2]), (cx + d[0], y + rng.uniform(-.04, .06), cz + d[2]))
    return a.build(loose=True)


def fallen_beam():
    """A collapsed ceiling beam, one end propped on fallen stones, the other splintered."""
    a, rng = Assembly(), random('fallen_beam')
    board(a, rng, (4.0, .64, .64), (0, 0, 0), chips=4, wobble=0)
    a.add(block((.22, .72, .72), (1.1, 0, 0), .04), 'iron_dark')
    for y in (-.2, .2):
        a.add(rivet((1.1, y, .36), .065), 'steel')
    tip = [(1.55 + .22 * (i % 2) + .1 * (i % 3), -.36 + .72 * i / 6) for i in range(7)]
    a.cut(facade(outline(tip + [(2.4, .36), (2.4, -.36)]), .9, -.45), 'endgrain')
    beam = turned(a.build(), 9, 'z')
    beam = moved(beam, (0, -beam.verts[:, 1].min(), 0))
    rubble = Assembly()
    for x, y, z, size in [(1.35, .2, .02, (.7, .4, .6)), (1.82, .16, -.3, (.5, .32, .44)), (1.0, .14, .42, (.46, .28, .4))]:
        board(rubble, rng, size, (x, y, z), 'stone', chips=2, wobble=12, chip_slot='stone_dark')
    return kit.Piece.merge(beam, grounded(rubble.build(loose=True)))


# ---------------------------------------------------------------------------- wall decor

def tapestry(field, border, emblem, motif):
    a = Assembly()
    shape = outline([(-.8, 3.5), (.8, 3.5), (.8, 1.3), (0, .95), (-.8, 1.3)])
    inner = shape.offset(-.14, mf.JoinType.Miter)
    if motif == 'sun':
        mark = star(12, .44, .31, (0, 2.45)) - star(12, .25, .2, (0, 2.45), 0)
    elif motif == 'cross':
        mark = rect(.22, 1.1, (0, 2.4)) + rect(.7, .22, (0, 2.6))
    else:
        mark = outline([(0, 2.95), (.42, 2.55), (0, 2.15), (-.42, 2.55)]) - outline([(0, 2.72), (.18, 2.55), (0, 2.38), (-.18, 2.55)])
    weave = lambda section, slot: a.add(facade(section.offset(.003, mf.JoinType.Miter), .08, .1), slot)
    weave(shape - inner, border)
    weave(inner - mark, field)
    weave(mark, emblem)
    a.add(rod((-.98, 3.52, .14), (.98, 3.52, .14), .07), 'wood_dark')
    for s in (-1, 1):
        a.add(ball((s * 1.02, 3.52, .14), (.1, .1, .1), 8), 'gold')
        a.add(block((.12, .12, .2), (s * .88, 3.52, .1), .03), 'iron_dark')
    for x, y in [(0, .95), (-.8, 1.3), (.8, 1.3)]:
        a.add(lathe([(.04, y - .26), (.1, y - .2), (.07, y - .04), (.04, y + .03)], 8).translate((x, 0, .14)), border)
    return a.build()


def heraldic_shield(division, first, second):
    a = Assembly()
    angles = np.linspace(0, np.pi, 9)
    points = [(-.55, 3.3), (.55, 3.3), (.55, 2.75)] + [(.55 * np.cos(t), 2.75 - .68 * np.sin(t)) for t in angles[1:-1]] + [(-.55, 2.75)]
    shape = outline(points)
    inner = shape.offset(-.1, mf.JoinType.Miter)
    if division == 'quartered':
        split = rect(1.2, 1.2, (-.6, 3.3)) + rect(1.2, 1.2, (.6, 1.9))
    elif division == 'chevron':
        split = outline([(-.7, 2.1), (0, 2.9), (.7, 2.1), (.7, 2.42), (0, 3.22), (-.7, 2.42)])
    else:
        split = outline([(-.7, 3.42), (-.26, 3.42), (.7, 2.32), (.7, 1.88)])
    a.add(facade((shape - inner).offset(.003, mf.JoinType.Miter), .16, .02), 'steel')
    # The second tincture stands a hair proud of the first, so the two never share a face.
    a.add(facade(inner - split, .12, .04), first)
    a.add(facade((inner ^ split).offset(.004, mf.JoinType.Miter), .13, .045), second)
    a.add(block((.34, .34, .05), (0, 2.9, .025), .01), 'iron_dark')
    for x, y in [(-.48, 3.24), (.48, 3.24), (-.48, 2.8), (.48, 2.8), (0, 2.12), (-.3, 2.3), (.3, 2.3)]:
        a.add(rivet((x, y, .18), .04), 'iron_dark')
    if division != 'quartered':
        a.add(ball((0, 2.82, .16), (.15, .15, .09), 10), 'steel')
    return a.build()


def sconce(flames=False):
    a, tips = Assembly(), []
    a.add(facade(outline([(-.16, 2.5), (.16, 2.5), (.16, 2.1), (0, 1.92), (-.16, 2.1)]), .07, 0), 'iron_dark')
    for y in (2.4, 2.08):
        a.add(rivet((0, y, .07), .04), 'steel')
    a.add(bend([(0, 2.06, .04), (0, 2.03, .18), (0, 2.08, .32), (0, 2.2, .38)], [.045] * 4, 6), 'iron')
    a.add(lathe([(.04, 2.17), (.17, 2.22), (.19, 2.27), (.08, 2.28)], 10).translate((0, 0, .38)), 'iron')
    candle(a, tips, 0, .38, 2.26, .32, .11, 2, 1)
    return lit(a, tips, flames)


# ---------------------------------------------------------------------------- dispatch

KINDS = {
    'altar': altar, 'candelabra': candelabra, 'candle_cluster': candle_cluster, 'candle_pile': candle_pile,
    'lectern': lectern, 'statue': statue, 'statue_broken': statue_broken, 'brazier': brazier,
    'vase_tall': vase_tall, 'urn_wide': urn_wide, 'vase_broken': vase_broken, 'bust': bust, 'armor_stand': armor_stand,
    'rug': rug, 'globe': globe, 'cauldron': cauldron, 'scroll_rack': scroll_rack, 'herb_rack': herb_rack,
    'hearth': hearth, 'chopping_block': chopping_block, 'pot_rack': pot_rack, 'cheese_wheel': cheese_wheel,
    'ham': ham, 'bread': bread, 'weapon_rack': weapon_rack, 'training_dummy': training_dummy, 'bunk_bed': bunk_bed,
    'war_map': war_map, 'shackles': shackles, 'straw_pile': straw_pile, 'bucket': bucket, 'bone_pile': bone_pile,
    'fallen_beam': fallen_beam, 'tapestry': tapestry, 'heraldic_shield': heraldic_shield, 'sconce': sconce,
}


def furnishing(design):
    """Build one explicit furnishing; unknown kinds fail instead of falling back. Other design keys are arguments."""
    kind = design.get('kind')
    if kind not in KINDS:
        raise ValueError(f'Unknown furnishing design {kind}')
    return KINDS[kind](**{key: value for key, value in design.items() if key != 'kind'})
