"""KayKit-style polearms: long thick shafts with wrapped hand positions, socketed bevelled heads and a
cloth tassel under the head.

Built from the shared weapon construction in `items.forge` and the blade shapes in `items.blades`.
The origin is the lower hand; heads sit at the top of +Y, and axe bits point to -X.
"""

import numpy as np
from items.forge import Haft, Blade, collar, pyramid, dispatch
from items.blades import straight, curved
from items.solid import Assembly

SHAFT = [(0, -.66, .062), (0, -.62, .082), (0, .40, .079), (0, 1.40, .076), (0, 1.44, .06)]


def polearm_shaft(a):
    """The shared shaft: wraps at both hand positions, an iron butt cap."""
    haft = Haft(a, SHAFT)
    for y in (-.06, .06, .42, .54):
        haft.band(y, .08, 'wrap', .016)
    a.add(collar((0, -.62, 0), (0, 1, 0), .098, .10, 'iron'))
    return haft


def tassel(a, y):
    """A chunky cloth tassel ring under the head with three hanging tufts."""
    a.add(collar((0, y, 0), (0, 1, 0), .10, .06, 'cloth', .015))
    for k in range(3):
        t = np.radians(120*k + 30)
        d = np.array([np.cos(t), 0, np.sin(t)])
        a.add(pyramid(d*.075 + (0, y-.02, 0), (d[0]*.3, -1, d[2]*.3), .05, .16, 'cloth'))


def spear():
    """Tier-two spear: a long ridged leaf head on an iron socket, tasselled."""
    a = Assembly()
    polearm_shaft(a)
    a.add(collar((0, 1.48, 0), (0, 1, 0), .095, .18, 'iron'))
    tassel(a, 1.36)
    head = straight(a, [(1.52, .06), (1.70, .13), (1.92, .105)], (0, 2.22), .075, .07, .016)
    head.chip(.2, .03, .045, .2)
    head.finish()
    return a.build()


def halberd():
    """Tier-three halberd: a crescent axe bit, a ridged top spike and a rear beak on a long socket."""
    a = Assembly()
    polearm_shaft(a)
    a.add(collar((0, 1.50, 0), (0, 1, 0), .10, .40, 'iron'))
    tassel(a, 1.26)
    edge = [(-.38, 1.74), (-.47, 1.66), (-.50, 1.50), (-.47, 1.34), (-.38, 1.26)]
    bit = Blade(a, [(0, 1.62), (-.20, 1.60), *edge, (-.20, 1.38), (0, 1.36)], edge, .10, .10, taper=(.06, -.08))
    bit.chip(.66, .035, .05, .2)
    bit.finish()
    straight(a, [(1.64, .055), (1.86, .05)], (0, 2.10), .07, .06, .016).finish()
    beak = [(.30, 1.44), (.16, 1.42), (0, 1.40), (0, 1.58), (.16, 1.54)]
    Blade(a, beak, beak[3:] + beak[:1], .08, .05).finish()
    return a.build()


def war_scythe():
    """Tier-three war scythe: a curved single-edged blade socketed upright, its edge facing -X."""
    a = Assembly()
    polearm_shaft(a)
    a.add(collar((0, 1.46, 0), (0, 1, 0), .098, .22, 'iron'))
    for z in (-.09, .09):
        a.add(pyramid((0, 1.46, z*1.1), (0, 0, np.sign(z)), .045, .03, 'iron'))
    tassel(a, 1.32)
    blade = curved(a, [(1.50, 0, .07), (1.78, .02, .085), (2.04, .07, .08), (2.26, .15, .06)], (.27, 2.42),
                   .075, .06, .016)
    blade.chip(.45, .035, .05, .2)
    blade.finish()
    return a.build()


KINDS = {'spear': spear, 'halberd': halberd, 'war_scythe': war_scythe}
polearm = dispatch(KINDS, 'polearm')
