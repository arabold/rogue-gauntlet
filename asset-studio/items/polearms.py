"""KayKit-style polearms: long thick shafts with wrapped hand positions, socketed bevelled heads and a
feather tuft under the head.

Built from the shared weapon construction in `items.forge` and the blade shapes in `items.blades`.
The origin is the lower hand; heads sit at the top of +Y, and axe bits point to -X.
"""

import numpy as np
from core import kit
from prefabs.details import hilts
from items.forge import Haft, Blade, collar, pyramid, tube, dispatch
from items.blades import straight, curved
from items.solid import Assembly

SHAFT = [(0, -.66, .062), (0, -.62, .082), (0, .40, .079), (0, 1.40, .076), (0, 1.44, .06)]


def polearm_shaft(a):
    """The shared shaft: rope-wrapped at both hand positions, a spiked iron butt cap."""
    haft = Haft(a, SHAFT)
    for lo, hi in ((-.12, .12), (.36, .60)):
        haft.wraps(lo, hi)
    haft.knob(hilts.spiked_cap)
    return haft


def spear():
    """Tier-two spear: a long ridged leaf head on an iron socket over a feather tuft."""
    a = Assembly()
    haft = polearm_shaft(a)
    a.add(collar((0, 1.48, 0), (0, 1, 0), .095, .18, 'iron'))
    haft.fit(hilts.feather_tuft, 1.38)
    head = straight(a, [(1.52, .06), (1.70, .13), (1.92, .105)], (0, 2.22), .075, .07, .016)
    head.chip(.2, .03, .045, .2)
    head.finish()
    return a.build()


def halberd():
    """Tier-three halberd: a crescent axe bit, a ridged top spike and a rear beak on a long socket."""
    a = Assembly()
    haft = polearm_shaft(a)
    a.add(collar((0, 1.50, 0), (0, 1, 0), .10, .40, 'iron'))
    haft.fit(hilts.feather_tuft, 1.28)
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
    haft = polearm_shaft(a)
    a.add(collar((0, 1.46, 0), (0, 1, 0), .098, .22, 'iron'))
    for z in (-.09, .09):
        a.add(pyramid((0, 1.46, z*1.1), (0, 0, np.sign(z)), .045, .03, 'iron'))
    haft.fit(hilts.feather_tuft, 1.34)
    blade = curved(a, [(1.50, 0, .07), (1.78, .02, .085), (2.04, .07, .08), (2.26, .15, .06)], (.27, 2.42),
                   .075, .06, .016)
    blade.chip(.45, .035, .05, .2)
    blade.finish()
    return a.build()


def bolt(a, points, radii, slot='lightning'):
    """A zig-zag lightning bolt: faceted diamonds at each XY corner, hulled pairwise into chunky segments that taper
    to a point."""
    axes = np.vstack([np.eye(3), -np.eye(3)])
    joints = [np.array([*p, 0]) + axes*r for p, r in zip(points, radii)]
    for lo, hi in zip(joints[:-1], joints[1:]):
        a.add(kit.hull(np.vstack([lo, hi]), slot))


# Lightning prongs as (XY corners, radii); the side prongs rise from the crossbar's upturned ends.
BOLTS = [([(0, 1.56), (.11, 1.84), (-.08, 1.88), (.09, 2.14), (-.05, 2.18), (.02, 2.50)], [.085, .08, .08, .07, .06, 0]),
         ([(-.36, 1.72), (-.27, 1.92), (-.44, 1.96), (-.34, 2.16), (-.39, 2.36)], [.075, .07, .07, .058, 0]),
         ([(.37, 1.72), (.46, 1.90), (.31, 1.95), (.40, 2.14), (.36, 2.34)], [.075, .07, .07, .058, 0])]


def storm_trident():
    """Tier-five storm trident: three glowing zig-zag lightning prongs on an upturned gold crossbar and socket."""
    a = Assembly()
    haft = polearm_shaft(a)
    a.add(collar((0, 1.50, 0), (0, 1, 0), .10, .24, 'gold'))
    haft.fit(hilts.feather_tuft, 1.34)
    a.add(tube([(-.365, 1.78, 0), (-.33, 1.66, 0), (-.18, 1.59, 0), (0, 1.57, 0), (.18, 1.59, 0), (.33, 1.66, 0),
                (.37, 1.78, 0)], [.055, .066, .072, .078, .072, .066, .055], 'gold'))
    for points, radii in BOLTS:
        bolt(a, points, radii)
    return a.build()


KINDS = {'spear': spear, 'halberd': halberd, 'war_scythe': war_scythe, 'storm_trident': storm_trident}
polearm = dispatch(KINDS, 'polearm')
