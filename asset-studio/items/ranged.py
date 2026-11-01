"""KayKit-style crossbows: a chunky wooden stock, a heavy prod with a thick string, a loaded bolt.

Built from the shared weapon construction in `items.forge`. Authored in the crossbow scenes' model frame:
+Y runs forward to the muzzle, +X is up and Z spans the prod; the scene turns it into the held pose.
The origin is the hand on the stock.
"""

from core import kit
from items.forge import plate, tube, collar, gem, pyramid, dispatch
from items.solid import Assembly, box


def arbalest():
    """Tier-five gilded siege arbalest: heavy steel prod with gold tips, front stirrup, windlass wheels, a bolt."""
    a = Assembly()
    plate(a, [(-.06, -.10), (.07, -.10), (.07, 1.06), (-.06, 1.06)], .07, 'wood', .02)
    plate(a, [(.06, -.06), (.03, -.42), (-.15, -.40), (-.06, -.02)], .065, 'wood', .02)
    plate(a, [(-.05, .04), (-.05, .20), (-.24, .12), (-.24, .00)], .055, 'wood', .018)
    for y0, y1 in ((.98, 1.12), (.40, .50)):
        plate(a, [(-.075, y0), (.085, y0), (.085, y1), (-.075, y1)], .085, 'gold', .015)
    gem(a, (.005, 1.05), .035, .085)
    a.add(box((.15, .14, .22), (0, .93, 0), .025), 'gold')
    for m in (1, -1):
        a.add(tube([(0, .97, 0), (0, .965, .26*m), (0, .90, .50*m), (0, .80, .68*m)], [.06, .052, .042, .032], 'steel'))
        a.add(box((.08, .08, .08), (0, .80, .685*m), .02), 'gold')
        a.add(collar((0, .02, .085*m), (0, 0, 1), .085, .035, 'gold'))
        a.add(pyramid((0, .02, .10*m), (0, 0, m), .05, .05, 'gold'))
    a.add(tube([(.02, .80, .66), (.075, .52, .02), (.075, .52, -.02), (.02, .80, -.66)], [.014]*4, 'string'))
    a.add(tube([(0, 1.08, -.07), (0, 1.27, -.10), (0, 1.35, 0), (0, 1.27, .10), (0, 1.08, .07)], [.026]*5, 'iron'))
    a.add(tube([(.085, .52, 0), (.085, 1.18, 0)], [.02, .02], 'birch'))
    a.add(pyramid((.085, 1.16, 0), (0, 1, 0), .05, .09, 'steel'))
    for turn in (0, 90, 180):
        vane = box((.07, .16, .022), (.035, .62, 0), .008)
        vane.verts = vane.verts @ kit.rotation([0, 1, 0], turn).T + (.085, 0, 0)
        a.add(vane, 'fletch')
    return a.build()


KINDS = {'arbalest': arbalest}
ranged = dispatch(KINDS, 'ranged')
