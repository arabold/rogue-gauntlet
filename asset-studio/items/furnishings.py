"""Room furnishings: altars, statues, vessels, racks and wall decor for generated rooms.

Built from the chunky vocabulary in `furnishing_kit`: chunky chamfered boards with
grooves and chipped corners, stacked stone blocks, square posts, oversized rivets, candles
and heads. Units are Godot metres at the dungeon pack's scale (4-unit tiles, 1-unit table
tops). Floor props stand on y = 0 centered on the origin with their front toward +Z, so
back-to-wall props keep a flat back on their -Z side. Wall props hang with a flat back on
z = 0 at their final height above the floor (their room Prop mounts at height 0).

Candle-bearing designs with `flames` build only their flame tips, a separate emissive model
the game's lit scene shows (and hides on dark floors) together with its light. Fire bowls
and hearths hold dead coals; their lit scenes add the fire. Recipes export with
`color_mode: "world"`, so every prop wears the dungeon pack's own texture and material.
"""

import zlib
from pathlib import Path

import numpy as np
import manifold3d as mf
from core import kit
from items import foliage
from items.solid import Assembly, lathe, loop, welded
from items.furnishing_kit import (block, cushion, post, rod, ball, bend, framed, board, planks, square_post, stones,
                                  rivet, candle, lit, outline, rect, star, disc, facade, floor_plan, jag, painted, bands,
                                  turned, moved, grounded, ring, chain, torus, log)

# Colors come from the dungeon texture (`world_surface.SWATCHES`); slots only choose how hard their edges shade:
# boards, stone and metal keep crisp facets, cast pots, bowls and soft goods shade smooth.
CREASE = {
    **{slot: 35 for slot in ('wood', 'wood_dark', 'wood_light', 'endgrain', 'bark', 'stone', 'stone_dark', 'marble',
                             'marble_dark', 'rock', 'rock_dark')},
    **{slot: 40 for slot in ('iron', 'iron_dark', 'steel', 'gold', 'silver', 'trim')},
    **{slot: 75 for slot in ('pot', 'pot_inner', 'stone_light')},
    **{slot: 60 for slot in ('flame', 'liquid')},
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


# Statues are the pack's own adventurers carved in the plinth's stone: posed in Blender (`items/pose_kaykit.py`).
STATUES = {
    'knight': ('knight_idle', ('Knight_Body', 'Knight_Head', 'Knight_Helmet', 'Knight_Cape', 'Knight_ArmLeft', 'Knight_ArmRight',
                               'Knight_LegLeft', 'Knight_LegRight')),
    'mage': ('mage_idle', ('Mage_Body', 'Mage_Head', 'Mage_Hat', 'Mage_Cape', 'Mage_ArmLeft', 'Mage_ArmRight', 'Mage_LegLeft',
                           'Mage_LegRight')),
}
PLINTH_TOP = .87


def standing(character, scale=1.0):
    """A pack character in stone, standing on the plinth top, facing +Z."""
    name, parts = STATUES[character]
    body = figure(name, parts).scale((scale,) * 3)
    lo, hi = np.asarray(body.bounding_box()[:3]), np.asarray(body.bounding_box()[3:])
    return body.translate((-(lo[0] + hi[0]) / 2, PLINTH_TOP - .02 - lo[1], -(lo[2] + hi[2]) / 2))


def statue(character):
    a = Assembly()
    plinth(a, random('plinth'))
    a.add(standing(character), 'stone')
    # The figure's eyes sit on its face as small bodies of their own, as the pack models them.
    return a.build(loose=True)


def statue_broken():
    """The knight statue snapped at the knees: its stump stands on the plinth, the rest lies beside it."""
    break_at = PLINTH_TOP + .65
    stump = Assembly()
    plinth(stump, random('plinth'))
    stump.add(standing('knight'), 'stone')
    stump.cut(jag(break_at, 1.6, 1.6, above=True).rotate((0, 30, 0)), 'stone_dark')
    upper = Assembly()
    upper.add(standing('knight'), 'stone')
    upper.cut(jag(break_at, 1.6, 1.6, above=False).rotate((0, 30, 0)), 'stone_dark')
    fallen = turned(turned(upper.build(loose=True), -86, 'z'), -12, 'y')
    fallen = moved(fallen, (.86 - fallen.verts[:, 0].min(), -fallen.verts[:, 1].min(), .1 - fallen.verts[:, 2].mean()))
    return kit.Piece.merge(stump.build(loose=True), fallen)


def brazier():
    """A smooth cast fire bowl with a rolled rim on three square legs, full of dead coals; the lit scene adds the fire."""
    a, rng = Assembly(), random('brazier')
    a.add(lathe([(.2, .7), (.46, .76), (.64, .88), (.72, 1.0), (.7, 1.06), (.01, 1.06)], 20), 'pot')
    a.add(torus(.72, .075, 1.06, 24, 10), 'pot')
    a.cut(lathe([(.66, 1.3), (.63, 1.02), (.5, .92), (.3, .88), (.01, .87)], 20), 'pot_inner')
    a.add(ring(.62, .7, .82, .91, 20), 'iron_dark')
    for d in around(8, 0):
        a.add(ball(tuple(d * .695 + (0, .865, 0)), (.045, .045, .045), 6), 'steel')
    for d in around(3):
        a.add(bend([tuple(d * .4 + (0, .76, 0)), tuple(d * .5 + (0, .5, 0)), tuple(d * .56 + (0, .22, 0)),
                    tuple(d * .62 + (0, .08, 0))], [.1, .095, .09, .085], 4), 'iron')
        a.add(ball(tuple(d * .64 + (0, .07, 0)), (.14, .08, .14), 8), 'iron_dark')
    a.add(torus(.53, .05, .3, 20, 8), 'iron')
    for i in range(8):
        t, r = rng.uniform(0, 2 * np.pi), rng.uniform(0, .38)
        size = rng.uniform(.2, .28)
        a.add(cushion((size, size * .55, size * .85), (np.cos(t) * r, .88 + .05 * (1 - r / .38), np.sin(t) * r), .6)
              .rotate((0, rng.uniform(0, 180), 0)), 'coal')
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
    """A smooth round-bellied pot with a rolled rim, side handles on lugs and stubby copper legs, brimming with brew."""
    a, rng = Assembly(), random('cauldron')
    a.add(lathe([(.3, .22), (.46, .26), (.6, .36), (.68, .52), (.69, .68), (.64, .84), (.56, .94), (.52, .98), (.01, .98)], 20), 'pot')
    a.add(torus(.54, .085, .98, 24, 10), 'pot')
    a.cut(lathe([(.48, 1.4), (.47, .96), (.4, .8), (.01, .74)], 20), 'pot_inner')
    a.add(post(.5, .78, .86, sides=20), 'liquid')
    for _ in range(4):
        t, r = rng.uniform(0, 2 * np.pi), rng.uniform(.06, .32)
        size = rng.uniform(.06, .1)
        a.add(ball((np.cos(t) * r, .86, np.sin(t) * r), (size, size * .7, size), 8), 'liquid')
    for s in (-1, 1):
        a.add(block((.12, .14, .16), (s * .68, .82, 0), .03), 'pot')
        a.add(loop((0, 0, 0), (.16, .13), .055, 12).rotate((0, 90, 0)).translate((s * .76, .74, 0)), 'pot')
    for d in around(3):
        a.add(bend([tuple(d * .26 + (0, .3, 0)), tuple(d * .38 + (0, .12, 0)), tuple(d * .44 + (0, .02, 0))],
                   [.1, .09, .08], 8), 'copper')
    return a.build()


def mortar():
    """A stone mortar with a pour notch and its club pestle leaning inside, for alchemist tables."""
    a = Assembly()
    a.add(lathe([(.26, 0), (.31, .04), (.39, .18), (.4, .3), (.37, .34), (.01, .34)], 18), 'stone_light')
    a.add(torus(.37, .045, .34, 20, 8), 'stone_light')
    a.cut(lathe([(.31, .6), (.3, .32), (.2, .16), (.01, .14)], 18), 'stone_dark')
    a.cut(block((.14, .14, .2), (0, .38, .38), .03), 'stone_dark')
    pestle = lathe([(.06, 0), (.11, .06), (.1, .16), (.075, .36), (.06, .5), (.07, .56), (.01, .58)], 10)
    a.add(pestle.rotate((0, 0, -38)).translate((-.06, .16, -.04)), 'stone_light')
    a.add(ball((-.06, .2, -.04), (.11, .1, .11), 10), 'stone_light')
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
                paper = 'parchment' if rng.random() < .7 else 'linen'
                roll = a.label(rod((x + sx, cy, -.26), (x + sx, cy, front), r, 9), paper)
                a.join(roll - a.label(rod((x + sx, cy, front - .05), (x + sx, cy, front + .05), r * .42, 7), 'parchment_dark'))
                if rng.random() < .45:
                    a.add(rod((x + sx, cy, .02), (x + sx, cy, .1), r * 1.14, 9), 'cloth_blue' if rng.random() < .6 else 'cloth_red')
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

def hearth_log(a, rng, center, length, radius, yaw, tilt=0.0):
    """A chunky log lying along its yaw, for fireplaces."""
    log(a, rng, length, radius, [((0, 0, 0), (0, -length / 2, 0)), ((0, 0, 90 + tilt), (0, 0, 0)), ((0, yaw, 0), tuple(center))])


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
    hearth_log(a, rng, (0, .44, -.2), 1.4, .18, 12)
    hearth_log(a, rng, (.02, .46, -.38), 1.36, .17, -14)
    hearth_log(a, rng, (0, .74, -.28), .9, .16, 4)
    for _ in range(9):
        a.add(ball((rng.uniform(-.75, .75), .27, rng.uniform(-.55, .15)), (.16, .08, .16), 7), 'coal')
    end = chain(a, 0, 2.02, 4, -.34)
    a.add(loop((0, 0, 0), (.32, .14), .045, 12).translate((0, end - .2, -.34)), 'iron')
    a.add(lathe([(.26, .9), (.36, .98), (.4, 1.14), (.36, 1.28), (.38, 1.34)], 12).translate((0, 0, -.34)), 'iron')
    return a.build()


def chopping_block():
    """A chunky log stump with ringed end grain in a rim of chunky bark and an old axe cut in its top.
    The game scene sinks a real axe into it."""
    a, rng = Assembly(), random('chopping_block')
    log(a, rng, .82, .5, stub=True)
    for x, turn in ((-.18, 32), (.16, -20)):
        a.cut(block((.06, .1, .46), (0, 0, 0), .01).rotate((0, turn, 0)).translate((x, .84, .06)), 'wood_light')
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
    """A chunky roast leg: a fat glazed drumstick with a two-knobbed bone."""
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
    """Two stacked chunky bunk frames along Z: capped posts, slatted plank platforms (tops at .55 and 1.55),
    headboards at -Z against the wall, a ladder on the side. The game scene lays the dungeon pack's own bedding on both decks."""
    a, rng = Assembly(), random('bunk_bed')
    for x in (-.76, .76):
        for z in (-1.48, 1.48):
            square_post(a, rng, .26, 0, 2.3, x, z, cap=.1)
    for y in (.42, 1.42):
        for x in (-.76, .76):
            board(a, rng, (.16, .26, 2.9), (x, y, 0), 'wood_dark', chips=2)
        for z in (-1.48, 1.48):
            board(a, rng, (1.36, .26, .14), (0, y, z), 'wood_dark', chips=1)
        planks(a, rng, 1.36, 2.8, .1, ((0, 0, 0), (0, y + .08, 0)), count=7)
        board(a, rng, (1.36, .3, .12), (0, y + .36, -1.48), chips=2)
    for z in (.78, 1.3):
        board(a, rng, (.12, 2.1, .12), (.87, 1.05, z), chips=1)
    for y in (.55, .95, 1.35, 1.75):
        board(a, rng, (.1, .1, .62), (.88, y, 1.04), chips=0)
    return a.build()


def curl_profile(half, radius, turns=1.2, thickness=.035, sag=.012, samples=40):
    """Side view of a sheet lying on the floor whose ends roll up and back over themselves: one closed outline,
    the paper's centre line offset by half its thickness either way, each curl tightening as it winds inward
    and tucking its free end inside the roll."""
    path = [(x, thickness / 2 + sag * (1 - (x / half) ** 2)) for x in np.linspace(-half, half, 9)]
    for side in (1, -1):
        curl = []
        for t in np.linspace(0, turns * 2 * np.pi, samples)[1:]:
            r = radius * (1 - .3 * t / (2 * np.pi))
            # Rolls start at the sheet's end and wind up and back over the sheet toward its middle.
            curl.append((side * (half + r * np.sin(t)), thickness / 2 + radius - r * np.cos(t)))
        path = path + curl if side > 0 else curl[::-1] + path
    path = np.array(path)
    tangent = np.gradient(path, axis=0)
    normal = np.column_stack([-tangent[:, 1], tangent[:, 0]]) / np.linalg.norm(tangent, axis=1, keepdims=True)
    return outline(np.vstack([path + normal * thickness / 2, (path - normal * thickness / 2)[::-1]]))


def war_map():
    """A map spread: one parchment sheet curling into loose, slightly conical rolls at both ends, an inked
    coast and island, a red route to an X, and a blue wax seal."""
    half, width, radius, thickness = .5, .92, .14, .035
    sheet = mf.Manifold.extrude(curl_profile(half, radius, thickness=thickness), width, 12).translate((0, 0, -width / 2))

    def taper(points):
        # Each roll narrows toward one side, the two in opposite directions, like paper rolled by hand. A whole roll
        # scales about its own axis, blending to nothing at the floor so it still meets the flat sheet smoothly.
        p = np.array(points)
        side = np.where(p[:, 0] > 0, 1.0, -1.0)
        roll = (np.abs(p[:, 0]) > half) | (p[:, 1] > thickness + .02)
        factor = 1 - .22 * np.where(side > 0, p[:, 2] / width + .5, .5 - p[:, 2] / width)
        weight = np.clip(p[:, 1] / (.7 * radius), 0, 1) * roll
        cx, cy = side * half, thickness / 2 + radius
        p[:, 0] += ((cx + (p[:, 0] - cx) * factor) - p[:, 0]) * weight
        p[:, 1] += ((cy + (p[:, 1] - cy) * factor) - p[:, 1]) * weight
        # Old paper never lies flat: a soft wave across the sheet.
        p[:, 1] += .008 * np.sin(p[:, 2] * 7 + p[:, 0] * 3) * (p[:, 1] < .06)
        return p
    a = Assembly()
    a.add(sheet.warp_batch(taper), 'parchment')
    coast = outline([(-.36, -.3), (-.16, -.24), (-.05, -.09), (.12, -.16), (.28, -.02), (.22, .22), (.03, .28), (-.09, .15), (-.28, .2), (-.38, .05)])
    for shape in (coast, disc(.07, (.34, .28), 9)):
        a.cut(floor_plan(shape - shape.offset(-.032, mf.JoinType.Round), .04, .022), 'ink')
    for x, z in [(-.27, .0), (-.18, .04), (-.09, .07), (0, .05), (.09, .01)]:
        a.cut(floor_plan(disc(.024, (x, z), 6), .04, .022), 'cloth_red')
    a.cut(floor_plan(rect(.15, .035, (0, 0)) + rect(.035, .15, (0, 0)), .04, .022).rotate((0, 45, 0)).translate((.19, 0, -.05)), 'cloth_red')
    a.cut(floor_plan(star(4, .09, .025, (-.3, -.33)), .04, .022), 'ink')
    # Age: two fold creases and small tears along the flat sheet's free edges, clear of the rolls.
    a.cut(block((.014, .03, width + .1), (-.02, .05, 0), .004), 'parchment_dark')
    a.cut(block((2 * half - .2, .03, .014), (0, .05, .04), .004), 'parchment_dark')
    rng = random('war_map')
    for z in (-width / 2, width / 2):
        for x in rng.uniform(-half * .7, half * .7, 3):
            n = rng.uniform(.035, .06)
            a.cut(block((n, .2, n), (0, 0, 0), .005).rotate((0, 45, 0)).translate((x, .05, z)), 'parchment_dark')
    a.add(cushion((.15, .06, .14), (.32, .05, -.27), .6), 'cloth_blue')
    for turn in (-25, 20):
        a.add(block((.045, .015, .15), (0, 0, 0), .005).rotate((0, turn, 0)).translate((.33 + turn / 400, .045, -.17)), 'cloth_blue')
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


def skull(a, x, z, turn, y=0.0, slot='bone', socket='socket', tilt=0.0, size=1.0):
    """A chunky skull with real eye sockets, added to `a` as one solid; `slot` carves it in stone instead."""
    head = Assembly()
    head.slots = a.slots
    head.add(ball((0, .24, 0), (.24, .21, .26), 10), slot)
    head.add(block((.28, .12, .16), (0, .1, .14), .04), slot)
    for s in (-1, 1):
        head.cut(ball((s * .09, .24, .22), (.065, .07, .07), 7), socket)
    head.cut(block((.06, .06, .08), (0, .16, .25), .015), socket)
    a.join(head.build_solid().scale((size,) * 3).rotate((tilt, turn, 0)).translate((x, y, z)))


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


# ---------------------------------------------------------------------------- crypt

# The chest runs along X with its front to +Z, a little bigger than the pack's bed (1.5 x 3) so the stone
# reads heavier than a bed beside it. The lid seats on its top band.
CHEST = {'length': 2.9, 'width': 1.5, 'top': .9}


def sarcophagus_chest(a, rng, hollow=False):
    """A stone chest on a broad plinth: three sunk panels on each long face, one on each end, a carved skull in the
    middle front panel and a band under the lid. With `hollow` it is cut out down to a floor at y = .32."""
    length, width, top = CHEST['length'], CHEST['width'], CHEST['top']
    board(a, rng, (length + .36, .2, width + .36), (0, .1, 0), 'stone_dark', chips=3, wobble=0, chip_slot='stone_dark')
    # The chest sinks into the plinth so the two stay one solid when it is hollowed out.
    board(a, rng, (length, top - .26, width), (0, (top + .06) / 2, 0), 'stone', chips=2, wobble=0, chip_slot='stone_dark')
    board(a, rng, (length + .14, .14, width + .14), (0, top - .07, 0), 'stone', chips=2, wobble=0, chip_slot='stone_dark')
    panel = (top - .12 + .2) / 2
    for z in (-1, 1):
        for x in (-length / 3, 0, length / 3):
            a.cut(block((length / 3 - .24, top - .54, .14), (x, panel, z * width / 2), .03), 'stone_dark')
    for x in (-1, 1):
        a.cut(block((.14, top - .54, width - .3), (x * length / 2, panel, 0), .03), 'stone_dark')
    skull(a, 0, width / 2 - .17, 0, panel - .14, 'marble', 'stone_dark', size=.58)
    if hollow:
        a.cut(block((length - .36, 2, width - .36), (0, 1.32, 0), .04), 'stone_dark')


def sarcophagus_lid(a, rng, y):
    """The heavy two-step lid seated at height y; its top is at y + .34."""
    length, width = CHEST['length'], CHEST['width']
    board(a, rng, (length + .3, .2, width + .3), (0, y + .1, 0), 'stone', chips=3, wobble=0, chip_slot='stone_dark')
    board(a, rng, (length + .02, .16, width - .08), (0, y + .26, 0), 'stone', chips=2, wobble=0, chip_slot='stone_dark')


FIGURES = Path(__file__).resolve().parent / 'meshes'
KNIGHT = ('Knight_Body', 'Knight_Head', 'Knight_Helmet', 'Knight_ArmLeft', 'Knight_ArmRight', 'Knight_LegLeft', 'Knight_LegRight')


def figure(name, parts):
    """A posed KayKit character (`items/pose_kaykit.py`) as one solid: its closed parts unioned. Small features such as
    the eyes sit on the face as bodies of their own; slivers with no volume are dropped."""
    data = np.load(FIGURES / f'{name}.npz')
    solids = []
    for part in parts:
        p, f = welded(data[part + '__v'], data[part + '__f'])
        solids.append(mf.Manifold(mf.Mesh(np.asarray(p, np.float32), np.asarray(f, np.uint32))))
    union = mf.Manifold.batch_boolean(solids, mf.OpType.Add)
    return mf.Manifold.compose([body for body in union.decompose() if abs(body.volume()) > 1e-5])


def effigy(a, y):
    """The pack's knight carved in the lid's own stone, lying in state on a lid top at y with its head toward -X:
    laid on its back with only its back sunk into the lid, so its whole front stands clear."""
    knight = figure('knight_idle', KNIGHT).rotate((-90, 0, 0)).rotate((0, 90, 0)).scale((.9, .9, .9)).simplify(.004)
    lo, hi = np.asarray(knight.bounding_box()[:3]), np.asarray(knight.bounding_box()[3:])
    cut = lo[1] + .2 * (hi[1] - lo[1])
    knight = knight.trim_by_plane((0, 1, 0), cut)
    a.add(knight.translate((-(lo[0] + hi[0]) / 2, y - .02 - cut, -(lo[2] + hi[2]) / 2)), 'stone')


def sarcophagus():
    """A stone sarcophagus with a knight's effigy on its lid, for crypts and shrines."""
    a, rng = Assembly(), random('sarcophagus')
    sarcophagus_chest(a, rng)
    sarcophagus_lid(a, rng, CHEST['top'] - .01)
    effigy(a, CHEST['top'] + .33)
    # The knight's eyes sit on its face as small bodies of their own, as the pack models them.
    return a.build(loose=True)


def sarcophagus_open():
    """The same chest robbed: its lid shoved aside on the chest and cracked, the broken corner on the floor,
    a skull and bones in the open end."""
    a, rng = Assembly(), random('sarcophagus_open')
    sarcophagus_chest(a, rng, hollow=True)
    skull(a, -.95, .05, 15, .3 + .25, tilt=-90)
    for start, end in [((-.7, .39, -.3), (-.1, .41, -.12)), ((-.55, .39, .32), (.05, .4, .2)), ((-1.1, .39, -.45), (-.6, .4, -.5))]:
        bone(a, start, end, .06)
    chest = a.build()
    cover, rng = Assembly(), random('sarcophagus_lid')
    sarcophagus_lid(cover, rng, 0)
    corner = np.array([CHEST['length'] / 2 + .15, 0, CHEST['width'] / 2 + .15])
    out = np.array([1, 0, 1]) / np.sqrt(2)
    crack = jag(0, 4, 1, amplitude=.06, teeth=7).rotate((90, 0, 0)).rotate((0, 45, 0)).translate(tuple(corner - out * .62 + (0, .17, 0)))
    cover.cut(crack, 'stone_dark')
    lid = moved(turned(cover.build(), 20, 'y'), (1.05, CHEST['top'] + .005, .3))
    # The broken-off corner is the lid's own corner beyond the crack, fallen flat beside the chest.
    chunk, rng = Assembly(), random('sarcophagus_lid')
    sarcophagus_lid(chunk, rng, 0)
    chunk.cut(jag(0, 8, 1, above=False, amplitude=.06, teeth=7).rotate((90, 0, 0)).rotate((0, 45, 0))
              .translate(tuple(corner - out * .62 + (0, .17, 0))), 'stone_dark')
    broken = chunk.build()
    broken = moved(broken, -broken.verts.mean(0))
    broken = moved(grounded(turned(turned(broken, 160, 'y'), 8, 'z')), (2.3, 0, 1.25))
    return kit.Piece.merge(chest, lid, broken)


# ---------------------------------------------------------------------------- sewer
# Pipes match the drain pipe run through the slimy sewer wall: a 0.4 radius body, flanges 0.48, bolted.

PIPE = .4


def flange(a, center, axis, radius=PIPE + .08, depth=.12, bolts=6):
    """A bolted collar around a pipe on an axis-aligned `axis`, oversized bolt heads on its outer face."""
    center, axis = np.asarray(center, float), np.asarray(axis, float)
    a.add(rod(tuple(center - axis * depth / 2), tuple(center + axis * depth / 2), radius, 14), 'pipe')
    t1 = np.cross(axis, (0, 1, 0) if abs(axis[1]) < .9 else (1, 0, 0))
    t2 = np.cross(axis, t1)
    for k in range(bolts):
        angle = 2 * np.pi * (k + .5) / bolts
        point = center + axis * depth / 2 + (np.cos(angle) * t1 + np.sin(angle) * t2) * (radius - .09)
        a.add(rivet(tuple(point), .05, tuple(axis)), 'iron')


def elbow(a, start, corner, end, radius=PIPE, steps=7):
    """A pipe turning from `start` through a rounded bend at `corner` to `end` (both legs straight)."""
    start, corner, end = (np.asarray(p, float) for p in (start, corner, end))
    bend_radius = radius * 1.4
    d0 = (corner - start) / np.linalg.norm(corner - start)
    d1 = (end - corner) / np.linalg.norm(end - corner)
    p0, p1 = corner - d0 * bend_radius, corner + d1 * bend_radius
    t = np.linspace(0, 1, steps)[:, None]
    arc = (1 - t) ** 2 * p0 + 2 * (1 - t) * t * corner + t ** 2 * p1
    path = np.vstack([start, arc, end])
    a.add(bend(path, [radius] * len(path), 12), 'pipe')


def wheel(a, center, radius=.42, spokes=5, flat=False):
    """A cast hand wheel facing +Z (or lying `flat`, facing up): rolled rim, spokes, a fat hub nut."""
    center = np.asarray(center, float)
    turn = (0, 0, 0) if flat else (90, 0, 0)
    a.add(torus(radius, .065, 0, 18, 8).rotate(turn).translate(tuple(center)), 'iron')
    for k in range(spokes):
        angle = 2 * np.pi * k / spokes + np.pi / 2
        spoke = (np.cos(angle) * radius, 0, np.sin(angle) * radius) if flat else (np.cos(angle) * radius, np.sin(angle) * radius, 0)
        a.add(rod(tuple(center), tuple(center + spoke), .045, 6), 'iron')
    a.add(lathe([(.13, -.08), (.14, .04), (.08, .1)], 6).rotate(turn).translate(tuple(center)), 'iron_dark')


def valve():
    """A valve on a pipe rising out of the floor and bending back into the wall, its hand wheel to the front."""
    a, rng = Assembly(), random('valve')
    z = .1
    flange(a, (0, .04, z), (0, 1, 0), PIPE + .14, .08, 8)
    a.add(rod((0, 0, z), (0, 1.05, z), PIPE, 12), 'pipe')
    elbow(a, (0, 1.0, z), (0, 1.62, z), (0, 1.62, -.55))
    flange(a, (0, 1.62, -.5), (0, 0, 1), PIPE + .14, .1, 8)
    a.add(lathe([(.44, .5), (.52, .58), (.54, .8), (.44, .9)], 12).translate((0, 0, z)), 'pipe')
    for y in (.5, .92):
        flange(a, (0, y, z), (0, 1, 0))
    a.add(block((.3, .28, .3), (0, .72, z + .5), .05), 'pipe')
    a.add(rod((0, .72, z + .5), (0, .72, z + .78), .07, 8), 'iron')
    wheel(a, (0, .72, z + .78))
    return a.build()


def pipe_outlet():
    """A big outfall pipe out of the wall on a stone saddle, barred across its mouth, sewage spilling to a puddle."""
    a, rng = Assembly(), random('pipe_outlet')
    y, mouth = .66, .55
    stones(a, rng, -.6, .6, 0, .32, .9, -.05, 1, 'stone', span=(.55, .65))
    a.add(rod((0, y, -.55), (0, y, mouth), .52, 14), 'pipe')
    flange(a, (0, y, -.47), (0, 0, 1), .66, .14, 8)
    flange(a, (0, y, mouth - .06), (0, 0, 1), .62, .14, 8)
    a.cut(rod((0, y, -.3), (0, y, mouth + .1), .4, 14), 'socket')
    # Sludge lies in the bottom of the bore, held back by a riveted weir plate across the mouth.
    a.add(block((.6, .14, .8), (0, y - .34, .1), .02) ^ rod((0, y, -.3), (0, y, mouth - .14), .41, 14), 'slime')
    a.add(rod((0, y, mouth - .16), (0, y, mouth - .08), .43, 14) ^ block((1, .26, .3), (0, y - .33, mouth - .12), 0), 'iron')
    for x in (-.22, 0, .22):
        a.add(rivet((x, y - .3, mouth - .08), .04), 'steel')
    for x in (-.2, 0, .2):
        a.add(rod((x, y - .5, mouth - .2), (x, y + .5, mouth - .2), .045, 6), 'iron')
    return a.build()


def pipe_run():
    """Pipes along a wall: one rises out of the floor, runs level on wall brackets past a joint and turns into the wall."""
    a, rng = Assembly(), random('pipe_run')
    y, z, r = .9, -.05, .3
    flange(a, (-1.3, .04, z), (0, 1, 0), r + .14, .08, 6)
    elbow(a, (-1.3, 0, z), (-1.3, y, z), (.9, y, z), r)
    elbow(a, (.6, y, z), (1.3, y, z), (1.3, y, -.48), r)
    flange(a, (1.3, y, -.43), (0, 0, 1), r + .14, .1, 6)
    flange(a, (-.1, y, z), (1, 0, 0), r + .08, .12, 6)
    for x in (-.75, .55):
        a.add(block((.16, .16, .3), (x, y, -.4), .03), 'iron_dark')
        a.add(torus(r + .03, .045, 0, 14, 6).rotate((0, 0, 90)).translate((x, y, z)), 'iron')
        a.add(rivet((x, y + .1, -.25), .045, (0, 0, 1)), 'steel')
    a.add(torus(r + .03, .045, 0, 14, 6).translate((-1.3, .45, z)), 'iron')
    return a.build()


def sluice_gate():
    """A culvert sluice against the wall: stone jambs and lintel, a plank gate raised a little in iron runners
    over the dark culvert, its lifting stem rising through the lintel to a hand wheel lying on a housing on top."""
    a, rng = Assembly(), random('sluice_gate')
    for s in (-1, 1):
        stones(a, rng, s * 1.12 - .28, s * 1.12 + .28, 0, 2.4, .72, -.04, 4, 'stone', span=(.56, .56))
    board(a, rng, (2.9, .36, .82), (0, 2.56, -.04), 'stone', chips=3, wobble=0, chip_slot='stone_dark')
    a.add(block((1.72, 2.4, .12), (0, 1.2, -.36), .02), 'socket')
    for s in (-1, 1):
        a.add(block((.12, 2.4, .22), (s * .86, 1.2, -.12), .03), 'iron_dark')
    edges = np.linspace(-.84, .84, 5)
    for x0, x1 in zip(edges[:-1], edges[1:]):
        board(a, rng, (x1 - x0 + .006, 1.72, .14), ((x0 + x1) / 2, 1.3, -.12), 'wood_dark', chips=1, wobble=.4)
    for y in (.72, 1.9):
        a.add(block((1.76, .16, .06), (0, y, -.03), .02), 'iron')
        for x in (-.6, -.2, .2, .6):
            a.add(rivet((x, y, 0), .045), 'steel')
    a.add(rod((0, 2.1, -.12), (0, 2.5, -.12), .08, 8), 'iron')
    a.add(block((.62, .3, .62), (0, 2.86, -.04), .05), 'iron_dark')
    for x in (-.22, .22):
        a.add(rivet((x, 2.86, .27), .045), 'steel')
    a.add(rod((0, 2.9, -.04), (0, 3.3, -.04), .07, 8), 'iron')
    wheel(a, (0, 3.24, -.04), .46, 6, flat=True)
    return a.build()


def goo_barrel():
    """An open barrel brimming with sewer goo: bulging staves, steel hoops, bubbles swelling on the surface and
    one fat drip running over the rim."""
    a, rng = Assembly(), random('goo_barrel')
    profile = [(.42, 0), (.47, .1), (.52, .35), (.54, .55), (.52, .78), (.47, 1.0), (.45, 1.06)]
    a.add(lathe(profile, 16), 'wood')
    for k in range(12):
        angle = 360 * k / 12 + 15
        a.cut(block((.03, 1.3, .12), (0, .53, 0), .005).translate((0, 0, .54)).rotate((0, angle, 0)), 'wood_dark')
    a.cut(post(.39, .84, 1.4, sides=16), 'wood_dark')
    for y0 in (.12, .86):
        radius = np.interp(y0 + .04, [y for _, y in profile], [r for r, _ in profile])
        a.add(ring(radius - .03, radius + .035, y0, y0 + .09, 16), 'steel')
    a.add(post(.4, .82, .94, sides=16), 'slime')
    for x, z, r in [(.12, -.1, .09), (-.16, .08, .07), (.02, .2, .05)]:
        a.add(ball((x, .93, z), (r, r * .8, r), 8), 'slime')
    rim = np.array([0, 1.02, .44])
    drip = [rim + (0, .04, -.06), rim + (0, .02, .05), (0, .86, .51), (0, .66, .55), (0, .5, .56)]
    a.add(bend(np.array(drip, float), [.07, .08, .075, .065, .06], 7), 'slime')
    a.add(ball((0, .46, .56), (.09, .11, .08), 8), 'slime')
    return a.build()


def goo_puddle(width=1.6, depth=1.1, lobes=4, height=.16, seed='goo_puddle'):
    """A slick of thick sewer goo heaped on the floor: overlapping soft lobes melted into one, domed up to `height`
    (goo stands proud where water would run flat), flat underneath, a few bubbles swelling on top."""
    rng = random(seed)
    blobs = [(0.0, 0.0, width * .3, depth * .34, height)]
    for _ in range(lobes):
        blobs.append((rng.uniform(-.3, .3) * width, rng.uniform(-.24, .24) * depth, rng.uniform(.16, .26) * width,
                      rng.uniform(.18, .28) * depth, height * rng.uniform(.55, .9)))

    def field(x, y, z):
        # A smooth union of flattened ellipsoids: positive inside, as the level set expects.
        total = 0.0
        for cx, cz, rx, rz, h in blobs:
            total += np.exp(-6 * (((x - cx) / rx) ** 2 + (y / h) ** 2 + ((z - cz) / rz) ** 2))
        # Zero where a lone lobe's ellipsoid surface would be; neighbours swell into one another.
        return float(np.log(max(total, 1e-12)) / 6 + 1)
    reach = width * .62, height * 1.3, depth * .62
    goo = mf.Manifold.level_set(field, [-reach[0], -.05, -reach[2], reach[0], reach[1], reach[2]], .05)
    goo = goo.trim_by_plane((0, 1, 0), 0.0).simplify(.004)
    a = Assembly()
    a.add(goo, 'slime')
    for _ in range(3):
        x, z = rng.uniform(-.2, .2) * width, rng.uniform(-.15, .15) * depth
        top = max(y for y in np.linspace(0, reach[1], 40) if field(x, y, z) > 0)
        r = rng.uniform(.05, .09)
        a.add(ball((x, top - r * .2, z), (r, r * .8, r), 8), 'slime')
    return a.build()


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
    'globe': globe, 'cauldron': cauldron, 'scroll_rack': scroll_rack, 'herb_rack': herb_rack,
    'hearth': hearth, 'chopping_block': chopping_block, 'mortar': mortar, 'pot_rack': pot_rack, 'cheese_wheel': cheese_wheel,
    'ham': ham, 'bread': bread, 'weapon_rack': weapon_rack, 'training_dummy': training_dummy, 'bunk_bed': bunk_bed,
    'war_map': war_map, 'shackles': shackles, 'straw_pile': straw_pile, 'bucket': bucket, 'bone_pile': bone_pile,
    'fallen_beam': fallen_beam, 'tapestry': tapestry, 'heraldic_shield': heraldic_shield, 'sconce': sconce,
    'sarcophagus': sarcophagus, 'sarcophagus_open': sarcophagus_open, 'valve': valve, 'pipe_outlet': pipe_outlet,
    'pipe_run': pipe_run, 'sluice_gate': sluice_gate, 'goo_barrel': goo_barrel, 'goo_puddle': goo_puddle, **foliage.KINDS,
}


def furnishing(design):
    """Build one explicit furnishing; unknown kinds fail instead of falling back. Other design keys are arguments."""
    kind = design.get('kind')
    if kind not in KINDS:
        raise ValueError(f'Unknown furnishing design {kind}')
    return KINDS[kind](**{key: value for key, value in design.items() if key != 'kind'})
