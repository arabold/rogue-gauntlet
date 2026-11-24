"""Cathedral props for its headline rooms: a church bell on its frame, a reliquary cabinet, a bishop's
throne and the vampire's coffins.

The Cathedral is elegant and clerical: dressed stone with gilt trim, carved arches and red cloth, chunky in the pack's
manner beside the 2.2 m hero. Floor props stand on y = 0 facing +Z; back-to-wall props keep a flat back on -Z.
"""

import numpy as np
import manifold3d as mf
from items.solid import Assembly, lathe
from items.furnishing_kit import (ball, bend, block, board, candle, cushion, facade, outline, planks, random, rivet, rod,
                                  skull, square_post, strut, torus)
from items.graveyard import _arch, _chamfered, _chip, _crack, _stack, _sub


def church_bell():
    """A great bell (2 m, as wide as the barrels are tall) in a massive timber frame standing as high as the walls
    (4.6 m): stone footings at either end, square posts braced fore and aft, a stepped headstock with iron straps over the
    bell's yoke, the clapper hanging in its dark mouth and the pull rope coiled on the plank floor."""
    a, rng = Assembly(), random('church_bell')
    span, foot, top = 1.5, .45, 3.9
    for x in (-span, span):
        board(a, rng, (.9, foot, 2.0), (x, foot / 2, 0), 'stone', chips=2, wobble=0, chip_slot='stone_dark')
        square_post(a, rng, .42, foot, top, x, 0)
        for z in (-1, 1):
            strut(a, rng, (x, foot + .02, z * .85), (x, 2.3, z * .18), .24)
    planks(a, rng, 2.3, .9, .14, ((0, 0, 0), (0, .07, 0)), count=3)
    board(a, rng, (3.8, .5, .6), (0, top + .2, 0), 'wood', chips=2)
    board(a, rng, (2.2, .3, .5), (0, top + .58, 0), 'wood', chips=2)
    board(a, rng, (1.0, .36, .56), (0, 3.68, 0), 'wood', chips=1)
    for x in (-.36, .36):
        for z in (-1, 1):
            a.add(block((.16, .92, .06), (x, 3.84, z * .3), .015), 'iron')
            for y in (3.55, 4.1):
                a.add(rivet((x, y, z * .335), .045, (0, 0, z)), 'iron_dark')
    a.add(lathe([(.001, 3.55), (.4, 3.55), (.55, 3.42), (.62, 3.2), (.64, 2.8), (.7, 2.4), (.84, 2.0), (1.0, 1.7),
                 (1.06, 1.56), (1.04, 1.5), (.001, 1.5)], 16), 'copper')
    a.cut(lathe([(.001, 2.0), (.55, 1.9), (.86, 1.62), (.92, 1.45), (.001, 1.45)], 16), 'socket')
    a.add(torus(.63, .04, 3.15, 16, 5), 'gold')
    a.add(torus(1.0, .04, 1.72, 16, 5), 'gold')
    a.add(rod((0, 2.05, 0), (0, 1.3, 0), .07, 6), 'iron_dark')
    a.add(ball((0, 1.22, 0), (.17, .17, .17), 8), 'iron_dark')
    # The pull rope down from the headstock to a coil on the floor.
    a.add(rod((-1.18, top - .02, .5), (-1.18, .25, .5), .045, 6), 'rope')
    a.add(torus(.22, .05, .17, 12, 6).translate((-.92, 0, .3)), 'rope')
    a.add(torus(.15, .05, .25, 10, 6).translate((-.89, 0, .32)), 'rope')
    return a.build()


def reliquary():
    """A reliquary cabinet (1.7 x 2.5 m, back to the wall): dark stone with three arched niches under a gabled top, a
    gilt reliquary casket, skulls of the saints and candles in them, and a gilt cross on the gable."""
    a, rng = Assembly(), random('reliquary')
    depth = .6
    board(a, rng, (1.8, .24, depth + .1), (0, .12, -(depth + .1) / 2), 'stone_dark', chips=2, wobble=0, chip_slot='stone_dark')
    a.add(_chamfered(outline([(-.85, .2), (.85, .2), (.85, 2.05), (0, 2.5), (-.85, 2.05)]), depth, -depth), 'stone')
    for x in (-.55, 0, .55):
        a.cut(facade(_arch(.44, 1.05, .45, 8), depth - .14, -depth + .14).translate((x, 0, 0)), 'socket')
    board(a, rng, (1.86, .14, depth + .12), (0, 2.02, -(depth + .12) / 2), 'stone', chips=2, wobble=0, chip_slot='stone_dark')
    a.add(block((.1, .44, .08), (0, 2.42, .01), .02), 'gold')
    a.add(block((.3, .09, .08), (0, 2.5, .01), .02), 'gold')
    # The casket in the middle niche, skulls either side, candles before them.
    a.add(block((.32, .2, .24), (0, .54, -.22), .03), 'gold')
    a.add(lathe([(.17, .62), (.12, .74), (.001, .78)], 4).rotate((0, 45, 0)).translate((0, 0, -.22)), 'gold')
    for s in (-1, 1):
        skull(a, s * .55, -.3, 0, .46, size=.62)
        candle(a, None, s * .55 + .14, -.08, .45, .16, .06, 2, 1 + s)
    for k, (x, y) in enumerate(((-.55, .4), (0, .4), (.55, .4))):
        a.add(block((.4, .05, depth - .2), (x, y + .03, -(depth - .2) / 2 - .1), .01), 'stone_dark')
    _chip(a, rng, (.85, 1.6, 0), .14)
    _crack(a, rng, (-.85, 1.4), -10, .5, 0, bends=3)
    a.cut(block((4, 4, 1), (0, 1, -depth - .5), 0), 'stone_dark')
    return a.build()


def throne():
    """A bishop's throne (1.7 m wide, 3.1 m tall, back to the wall) on a two-step dais: a high carved back under a pointed
    arch with a gilt finial, deep armrests, a red cushion and gilt trim."""
    a, rng = Assembly(), random('throne')
    board(a, rng, (2.4, .18, 1.9), (0, .09, -.95), 'stone_dark', chips=2, wobble=0, chip_slot='stone_dark')
    board(a, rng, (2.0, .16, 1.5), (0, .25, -.85), 'stone', chips=2, wobble=0, chip_slot='stone_dark')
    seat, back = .9, -1.45
    board(a, rng, (1.5, seat - .3, .95), (0, .3 + (seat - .3) / 2, -.85), 'wood_dark', chips=1, wobble=0)
    a.add(_chamfered(outline([(-.8, .3), (.8, .3), (.8, 2.5), (0, 3.05), (-.8, 2.5)]), .3, back - .15), 'wood_dark')
    a.cut(facade(outline([(-.52, 1.15), (.52, 1.15), (.52, 2.3), (0, 2.72), (-.52, 2.3)]), .12, back + .1), 'cloth_red')
    a.add(facade(outline([(-.08, 1.6), (.08, 1.6), (.08, 2.05), (.22, 2.05), (.22, 2.2), (.08, 2.2), (.08, 2.4), (-.08, 2.4),
                          (-.08, 2.2), (-.22, 2.2), (-.22, 2.05), (-.08, 2.05)]), .08, back + .06), 'gold')
    for s in (-1, 1):
        board(a, rng, (.22, .62, 1.0), (s * .82, seat + .26, -.9), 'wood_dark', chips=1, wobble=0)
        a.add(ball((s * .82, seat + .62, -.4), (.15, .15, .15), 8), 'gold')
        a.add(lathe([(.12, 2.5), (.08, 2.8), (.001, 3.0)], 6).translate((s * .8, 0, back)), 'gold')
    a.add(cushion((1.3, .16, .85), (0, seat + .06, -.8), .5), 'cloth_red')
    a.add(block((1.62, .07, .06), (0, 2.5, back + .16), .02), 'gold')
    a.add(ball((0, 3.08, back), (.12, .12, .12), 8), 'gold')
    a.cut(block((4, 5, 1), (0, 1.5, -1.9 - .5), 0), 'stone_dark')
    return a.build()


COFFIN = [(-.6, -1.3), (.6, -1.3), (.85, .45), (.62, 1.3), (-.62, 1.3), (-.85, .45)]


def _prism(section, y0, height):
    """A floor-plan section (x, z) extruded upward from y0."""
    return mf.Manifold.extrude(section, height).rotate((90, 0, 0)).translate((0, y0 + height, 0))


def _coffin_lid(a, rng, shape):
    """The lid, lying on y = 0: an overhanging slab, a raised panel of three long planks and a big blocky iron cross."""
    lid = _sub(a)
    lid.add(_prism(shape.offset(.05, mf.JoinType.Miter), 0, .16), 'wood')
    lid.add(_prism(shape.offset(-.16, mf.JoinType.Miter), .14, .14), 'wood')
    for x in (-.22, .22):
        lid.cut(block((.035, .1, 2.6), (x, .29, 0), .005), 'wood_dark')
    lid.add(block((.28, .13, 1.4), (0, .33, .05), .03), 'iron')
    lid.add(block((.92, .13, .28), (0, .33, .45), .03), 'iron')
    _chip(lid, rng, (.85, .16, .45), .12, 'wood_light')
    _chip(lid, rng, (-.6, .16, -1.3), .1, 'wood_light')
    return lid


def _corner_caps(a, points, height, offset):
    """Iron caps wrapped round each corner of the box, two rivets on each face they cover."""
    pts = np.asarray(points, float)
    for k, v in enumerate(pts):
        clamp = block((.38, height, .38), (v[0], height / 2, v[1]), 0)
        a.add(_prism(mf.CrossSection([pts]).offset(offset, mf.JoinType.Miter), 0, height) ^ clamp, 'iron')
        for n in (pts[k - 1], pts[(k + 1) % len(pts)]):
            along = (n - v) / np.linalg.norm(n - v)
            out = np.array([along[1], -along[0]])
            out = out if out @ (v + n) > 0 else -out
            for y in (height * .28, height * .7):
                p = v + along * .1 + out * (offset + .005)
                a.add(rivet((p[0], y, p[1]), .045, (out[0], 0, out[1])), 'iron_dark')


def coffin(open=False):
    """A squat, wide coffin in the pack's chunky manner (2.6 x 1.7 m, its box as high as a chest): a fat six-sided box
    of three plank courses, iron caps riveted round its six corners, and a lid of three long planks on an overhanging
    slab under a big blocky iron cross. Open, it shows a red velvet lining and a round satin pillow, its lid leant against
    its side."""
    a, rng = Assembly(), random(f'coffin_{open}')
    shape = mf.CrossSection([np.asarray(COFFIN, float)])
    height, course = .9, .3
    for k in range(3):
        # Each course a plank a little proud of or behind its neighbors, a groove between them.
        a.add(_prism(shape.offset(rng.uniform(-.012, .012), mf.JoinType.Miter), k * course, course + .002), 'wood')
        if k:
            a.cut(_prism(shape.offset(.05, mf.JoinType.Miter), k * course - .018, .036)
                  - _prism(shape.offset(-.03, mf.JoinType.Miter), 0, height), 'wood_dark')
    _chip(a, rng, (-.85, height, .45), .12, 'wood_light')
    _chip(a, rng, (.6, height, -1.3), .1, 'wood_light')
    if not open:
        lid = _coffin_lid(a, rng, shape).build_solid().translate((0, height, 0))
        a.join(lid)
        _corner_caps(a, COFFIN, height + .16, .08)
        return a.build()
    a.cut(_prism(shape.offset(-.14, mf.JoinType.Miter), .2, 1), 'cloth_red')
    a.add(ball((0, .3, .8), (.36, .14, .28), 10), 'linen')
    a.add(rivet((0, .43, .8), .06, (0, 1, 0)), 'gold')
    _corner_caps(a, COFFIN, height, .06)
    # The lid stands on its long edge, its underside resting on the rim at the box's widest point, its cross facing out.
    tilt = np.radians(58)
    lid = _coffin_lid(a, rng, shape).build_solid().rotate((0, 0, -np.degrees(tilt)))
    ground = -lid.bounding_box()[1]
    rim = .88
    lid = lid.translate((rim + (height - ground) / np.tan(tilt), ground, .1))
    a.join(lid)
    return a.build()


KINDS = {
    'church_bell': church_bell, 'reliquary': reliquary, 'throne': throne, 'coffin': coffin,
}
