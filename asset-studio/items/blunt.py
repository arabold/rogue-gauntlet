"""KayKit-style blunt weapons: chamfered block and barrel heads, flanged maces, studded mauls and clubs on
thick hafts, with chipped corners on the low tiers.

Built from the shared weapon construction in `items.forge`. Heads sit on +Y above the grip origin;
their striking faces point along X.
"""

import numpy as np
import manifold3d as mf
from core import kit
from prefabs.details import hilts
from items.forge import Haft, collar, pyramid, gem, groove, dispatch
from items.solid import Assembly, box

ONE_HANDED = [(.012, -.30, .044), (.014, -.28, .062), (.010, -.20, .058), (.004, 0, .054), (0, .40, .052),
              (0, .80, .050), (0, 1.04, .048), (0, 1.07, .036)]
TWO_HANDED = [(.018, -.455, .066), (.02, -.43, .092), (.014, -.30, .090), (.006, -.10, .087), (0, .20, .085),
              (0, .60, .083), (0, 1.00, .080), (0, 1.40, .078), (0, 1.47, .060)]


def corner_bite(a, shape, corner, size, slot):
    """Knock a chunk off a block's corner: a cube turned onto its point, centered on the corner."""
    bite = mf.Manifold.cube((size, size, size), center=True).rotate((45, 35, 0)).translate(corner)
    return shape - a.label(bite, slot)


def mallet():
    """Tier-one wooden mallet: an iron-hooped barrel head on a plain haft, its rims chipped."""
    a = Assembly()
    haft = Haft(a, ONE_HANDED)
    haft.wraps(-.24, .08)
    head = a.label(collar((0, .88, 0), (1, 0, 0), .18, .66, 'wood', .035), 'wood')
    for corner in ((-.33, 1.04, .09), (.33, .74, -.10)):
        head = corner_bite(a, head, corner, .11, 'wood_cut')
    a.parts.append(head)
    for x in (-.22, .22):
        a.add(collar((x, .88, 0), (1, 0, 0), .198, .06, 'iron'))
    return a.build()


def mace():
    """Tier-five gilded paladin mace: six chunky gold flanges round a core, gem finial, cream grip."""
    a = Assembly()
    haft = Haft(a, [(0, -.30, .044), (0, -.28, .058), (0, .66, .050), (0, .70, .040)])
    a.add(collar((0, -.03, 0), (0, 1, 0), .068, .34, 'cloth', .012))
    for y in (-.20, .14):
        haft.band(y, .045, 'gold', .02)
    haft.band(.60, .07, 'gold', .035)
    haft.knob(hilts.gem_cap)
    a.add(collar((0, .88, 0), (0, 1, 0), .11, .42, 'gold'))
    for k in range(6):
        flange = box((.20, .38, .055), (.16, .88, 0), .02)
        flange.verts = flange.verts @ kit.rotation([0, 1, 0], 60*k).T
        a.add(flange, 'gold')
    a.add(kit.hull([(.06, 1.08, 0), (-.06, 1.08, 0), (0, 1.08, .06), (0, 1.08, -.06), (0, 1.22, 0), (0, 1.04, 0)], 'gem'))
    gem(a, (0, .88), .045, .11)
    return a.build()


# The sledge's chipped face corners, (y, z) per side: the leading -X face took the most blows.
BITES = {-1: ((1.50, .26), (.98, -.26)), 1: ((1.50, .26),)}


def maul(spiked):
    """Two-handed iron maul: an oversized three-part block head with steel faces on a rope grip and spiked butt;
    studded or plain, chipped by tier."""
    a = Assembly()
    haft = Haft(a, TWO_HANDED)
    haft.wraps(-.32, .14)
    haft.band(.94, .08, 'leather')
    haft.knob(hilts.spiked_cap)
    a.add(box((.70, .46, .46), (0, 1.24, 0), .055), 'iron')
    for side in (-1, 1):
        face = a.label(box((.13, .52, .52), (side*.39, 1.24, 0), .06), 'steel')
        if not spiked:
            for corner in BITES[side]:
                face = corner_bite(a, face, (side*.455, *corner), .14, 'edge')
        a.parts.append(face)
        if spiked:
            for y in (1.12, 1.36):
                for z in (-.12, .12):
                    a.add(pyramid((side*.455, y, z), (side, 0, 0), .11, .12, 'steel'))
    if spiked:
        for z in (-.23, .23):
            a.add(pyramid((0, 1.24, z), (0, 0, np.sign(z)), .12, .11, 'steel'))
    return a.build()


def cudgel():
    """Tier-two steel cudgel: a wooden club shod with iron bands and a ring of chunky iron studs."""
    a = Assembly()
    haft = Haft(a, [(0, -.30, .040), (0, -.28, .056), (0, .10, .052), (0, .40, .062), (0, .70, .10), (0, .95, .125),
                    (0, 1.04, .115), (0, 1.07, .075)], add=False)
    a.parts.append(a.label(haft.piece, 'wood'))
    haft.wraps(-.22, .06)
    haft.band(.99, .09, 'iron', .018)
    haft.band(.55, .06, 'iron', .016)
    for k in range(6):
        t = np.radians(60*k)
        d = np.array([np.cos(t), 0, np.sin(t)])
        a.add(pyramid(d*.11 + (0, .80, 0), d, .08, .09, 'iron'))
    return a.build()


def club():
    """Tier-one old wooden club: a knobby, slightly bent body with chunks knocked out of it."""
    a = Assembly()
    haft = Haft(a, [(0, -.30, .040), (0, -.28, .056), (.005, .10, .052), (0, .40, .066), (-.012, .65, .10),
                    (0, .85, .135), (.012, .98, .128), (.012, 1.04, .095), (.01, 1.06, .055)], add=False)
    body = a.label(haft.piece, 'wood')
    for corner, size in (((-.115, 1.02, .07), .11), ((.13, .80, -.08), .09), ((-.06, .62, -.09), .08)):
        body = corner_bite(a, body, corner, size, 'wood_cut')
    a.parts.append(body)
    haft.band(-.07, .07, 'leather', .014)
    return a.build()


def morning_star():
    """Tier-three morning star: a faceted iron ball bristling with chunky spikes on a wrapped haft."""
    a = Assembly()
    haft = Haft(a, ONE_HANDED[:5] + [(0, .74, .050), (0, .78, .04)])
    haft.wraps(-.22, .16)
    haft.knob(hilts.spiked_cap, length=.10)
    haft.band(.70, .10, 'iron', .022)
    center = np.array([0, .94, 0])
    a.add(mf.Manifold.sphere(.19, 10).translate(center), 'iron')
    golden = np.pi*(3 - np.sqrt(5))
    for k in range(12):
        y = 1 - 2*(k+.5)/12
        r = np.sqrt(1 - y*y)
        d = np.array([np.cos(golden*k)*r, y, np.sin(golden*k)*r])
        if d[1] > -.75:
            a.add(pyramid(center + d*.17, d, .09, .15, 'steel'))
    return a.build()


# A square spiral glyph, as strokes winding out from the center of a face.
SPIRAL = np.array([(0, 0), (.06, 0), (.06, .06), (-.06, .06), (-.06, -.06), (.12, -.06), (.12, .12), (-.12, .12),
                   (-.12, -.12)])


def hewn(size, center, bevel, slot, seed, rough=(.06, .06, .03)):
    """A rough-hewn block: a chamfered box whose eight corners are each knocked a little off square, by up to
    `rough` of its size per axis."""
    block = box(size, center, bevel)
    corner = ((block.verts - center) > 0) @ (4, 2, 1)
    offsets = np.random.default_rng(seed).uniform(-1, 1, (8, 3))*np.multiply(size, rough)
    return kit.hull(block.verts + offsets[corner], slot)


def stone_maul():
    """Tier-four earth maul: a rough-hewn sandstone block, chunks knocked off its corners and a glowing spiral glyph
    carved into each face, held by iron bands on a rope grip."""
    a = Assembly()
    haft = Haft(a, TWO_HANDED)
    haft.wraps(-.32, .14)
    haft.band(.94, .08, 'leather')
    haft.knob(hilts.spiked_cap)
    head = a.label(hewn((.80, .52, .52), (0, 1.24, 0), .05, 'stone', 3), 'stone')
    for corner, size in (((-.40, 1.50, .26), .24), ((.40, .98, -.26), .20), ((.40, 1.50, -.26), .16),
                         ((-.40, .98, .26), .15), ((.40, 1.50, .26), .12), ((-.40, 1.50, -.26), .10)):
        head = corner_bite(a, head, corner, size, 'stone_cut')
    for side in (1, -1):
        for start, end in zip(SPIRAL[:-1], SPIRAL[1:]):
            head = head - a.label(groove(start + (0, 1.24), end + (0, 1.24), side*.26, .032, .06), 'emerald')
    a.parts.append(head)
    for x in (-.28, .28):
        a.add(box((.07, .56, .56), (x, 1.24, 0), .02), 'iron')
    return a.build()


KINDS = {'mallet': mallet, 'mace': mace, 'sledge': lambda: maul(False), 'spiked_maul': lambda: maul(True),
         'cudgel': cudgel, 'club': club, 'morning_star': morning_star, 'stone_maul': stone_maul}
blunt = dispatch(KINDS, 'blunt')
