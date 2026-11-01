"""KayKit-style spell staffs and wands: thick shafts with chunky collars and wraps, big faceted crystals
held in claws or crescents, bone, horn and gnarled-wood heads.

Built from the shared weapon construction in `items.forge`. Staffs are two-handed, about 2.3 long, with
the origin at the upper grip; wands are about one long with the origin at the grip. Heads point up +Y.
"""

import numpy as np
import manifold3d as mf
from core import kit
from prefabs import library
from items.forge import Haft, collar, tube, crystal, branch, spiral, butt_cap, dispatch
from items.solid import Assembly, ellipsoid

STAFF = [(0, -.94, .048), (0, -.90, .062), (0, -.40, .060), (0, .40, .058), (0, 1.00, .056), (0, 1.06, .045)]
WAND = [(0, -.27, .042), (0, -.25, .058), (0, -.05, .056), (0, .30, .046), (0, .62, .040), (0, .66, .03)]


def claws(a, base, top, radius, count, thickness, slot, turn=0.):
    """Chunky prongs rising from a collar at `base`, bowing out to `radius` and closing in at `top`."""
    h = top - base
    for k in range(count):
        t = np.radians(360*k/count + turn)
        d = np.array([np.cos(t), 0, np.sin(t)])
        path = [d*radius*.35 + (0, base, 0), d*radius + (0, base + h*.35, 0), d*radius*.95 + (0, base + h*.75, 0),
                d*radius*.4 + (0, top, 0)]
        a.add(tube(path, [thickness, thickness, thickness*.8, thickness*.45], slot))


def crescent(a, y, radius, thickness, slot):
    """A chunky upturned crescent facing the viewer, its belly resting on the collar at `y`."""
    t = np.radians(np.linspace(200, 340, 7))
    path = np.column_stack([np.cos(t)*radius, y + radius + np.sin(t)*radius, np.zeros(7)])
    a.add(tube(path, thickness*np.array([.35, .75, .95, 1, .95, .75, .35]), slot))


def archmage():
    """Tier-five archmage staff: four gold claws round a big blue crystal, cream grip, gold butt cap."""
    a = Assembly()
    haft = Haft(a, STAFF)
    a.add(collar((0, 0, 0), (0, 1, 0), .076, .40, 'cloth', .012))
    for y in (-.22, .22, -.62, .62):
        haft.band(y, .05, 'gold', .02)
    butt_cap(a, haft)
    a.add(collar((0, 1.04, 0), (0, 1, 0), .09, .12, 'gold'))
    claws(a, 1.06, 1.66, .22, 4, .04, 'gold', 45)
    a.add(crystal((0, 1.28, 0), .16, .50, 'crystal'))
    return a.build()


def emerald():
    """Tier-three emerald staff: a gold crescent cradling a green crystal, leather wraps."""
    a = Assembly()
    haft = Haft(a, STAFF)
    for y in (-.06, .06):
        haft.band(y, .07, 'leather', .016)
    a.add(collar((0, 1.04, 0), (0, 1, 0), .085, .10, 'gold'))
    crescent(a, 1.06, .24, .07, 'gold')
    a.add(crystal((0, 1.26, 0), .13, .40, 'emerald'))
    return a.build()


def bone():
    """Tier-three bone staff: knuckled bone shaft lashed at its joints, three ribs round a pale stone."""
    a = Assembly()
    haft = Haft(a, STAFF, 'bone')
    for y in (-.90, -.35, .30, 1.0):
        a.add(collar((0, y, 0), (0, 1, 0), .085, .10, 'bone', .02))
    for y in (-.22, -.48, .17, .43):
        haft.band(y, .05, 'leather', .016)
    claws(a, 1.04, 1.52, .22, 3, .045, 'bone', 90)
    a.add(mf.Manifold.sphere(.14, 10).translate((0, 1.17, 0)), 'moonstone')
    return a.build()


def crooked():
    """Tier-one crooked staff: a gnarled branch curling into a crook, knots and leather wraps."""
    a = Assembly()
    path = [(0, -.94, 0), (.014, -.55, .01), (-.016, -.25, -.008), (.01, .05, .012), (-.02, .38, -.01),
            (.026, .70, .015), (-.01, .98, 0), (.03, 1.14, .01)]
    a.add(branch(path, [.06, .064, .062, .06, .058, .056, .055, .054], seed=7))
    crook = spiral((.19, 1.24, .01), np.pi, .78, .16, .085, 0, 18)
    crook = np.vstack([[(.03, 1.14, .01)], crook[1:]])
    a.add(branch(crook, np.linspace(.054, .024, len(crook)), seed=8))
    for p, r in [((.014, -.55, .01), .05), ((-.02, .38, -.01), .052), ((.026, .70, .015), .05), ((.03, 1.14, .01), .056)]:
        a.add(ellipsoid(p, (r, r*1.3, r), 10), 'wood')
    for y in (-.06, .06):
        a.add(collar((0, y, 0), (0, 1, 0), .06 + .012, .07, 'leather', min(.012*.5, .07*.2)))
    return a.build()


def horned():
    """Tier-four horned staff: a carved head block sprouting two chunky ram horns, brass fittings."""
    a = Assembly()
    haft = Haft(a, STAFF)
    for y in (-.06, .06):
        haft.band(y, .07, 'leather', .016)
    for y in (-.66, .56):
        haft.band(y, .06, 'brass', .02)
    a.add(collar((0, 1.16, 0), (0, 1, 0), .13, .28, 'wood'))
    a.add(collar((0, 1.03, 0), (0, 1, 0), .138, .06, 'brass'))
    a.add(collar((0, 1.31, 0), (0, 1, 0), .105, .06, 'brass'))
    horn = spiral((.24, 1.20, 0), np.pi, 1.0, .19, .08, .1, 30)
    horn[0] = (.06, 1.20, 0)
    u = np.linspace(0, 1, len(horn))
    for mirror in (1, -1):
        a.add(kit.sweep(horn*(mirror, 1, 1), kit.ring_profile(1, .85, 8), 'horn', scale=.10 - .075*u))
    return a.build()


def iron():
    """Tier-two iron staff: dark iron shaft with steel collars, a four-prong cage round a pale stone."""
    a = Assembly()
    haft = Haft(a, STAFF, 'iron')
    a.add(collar((0, 0, 0), (0, 1, 0), .074, .34, 'leather', .012))
    for y in (-.62, .52):
        haft.band(y, .07, 'steel', .02)
    a.add(collar((0, -.92, 0), (0, 1, 0), .08, .08, 'steel'))
    a.add(collar((0, 1.04, 0), (0, 1, 0), .085, .12, 'steel'))
    claws(a, 1.06, 1.56, .22, 4, .04, 'steel', 45)
    a.add(mf.Manifold.sphere(.15, 10).translate((0, 1.22, 0)), 'moonstone')
    return a.build()


def bone_wand():
    """Tier-three bone wand: a knuckled bone with a small skull on top, leather grip."""
    a = Assembly()
    haft = Haft(a, WAND, 'bone')
    a.add(collar((0, -.24, 0), (0, 1, 0), .07, .08, 'bone', .02))
    for y in (-.12, .0):
        haft.band(y, .06, 'leather', .014)
    a.add(library.load('skull_cranium', 'bone', scale=.26, offset=(0, .70, .01)))
    return a.build()


def carved_wand():
    """Tier-four scholar's wand: a turned ebony wand with carved beads and silver ferrules."""
    a = Assembly()
    Haft(a, [(0, -.27, .045), (0, -.25, .066), (0, -.05, .068), (0, .10, .056), (0, .40, .046), (0, .70, .034),
             (0, .82, .026), (0, .86, .012)], 'ebony')
    for y in (-.15, -.06):
        a.add(collar((0, y, 0), (0, 1, 0), .08, .05, 'ebony', .015))
    for y in (-.24, .12):
        a.add(collar((0, y, 0), (0, 1, 0), .075, .05, 'silver'))
    return a.build()


def celestial_wand():
    """Tier-five celestial wand: three gold claws round a blue crystal, cream grip, gold butt cap."""
    a = Assembly()
    haft = Haft(a, WAND)
    a.add(collar((0, -.09, 0), (0, 1, 0), .068, .26, 'cloth', .012))
    butt_cap(a, haft)
    a.add(collar((0, .62, 0), (0, 1, 0), .062, .08, 'gold'))
    claws(a, .64, .94, .11, 3, .024, 'gold', 90)
    a.add(crystal((0, .74, 0), .075, .26, 'crystal'))
    return a.build()


def crooked_wand():
    """Tier-one crooked wand: a bent twig with a forked tip and a leather-wrapped grip."""
    a = Assembly()
    path = [(0, -.27, 0), (.01, -.08, 0), (-.012, .10, .006), (.035, .27, -.012), (-.02, .43, .02),
            (.045, .58, -.012), (0, .72, .02), (.05, .86, 0)]
    a.add(branch(path, [.058, .057, .054, .048, .042, .036, .028, .016], seed=3))
    for p, r in [((.035, .27, -.012), .04), ((-.02, .43, .02), .036)]:
        a.add(ellipsoid(p, (r, r*1.25, r), 10), 'wood')
    a.add(branch([(0, .68, .015), (-.05, .77, .03), (-.09, .84, .04)], [.022, .016, .008], sides=6, seed=5))
    for y in (-.15, -.04):
        r = np.interp(y, (-.27, .1), (.058, .055))
        a.add(collar((0, y, 0), (0, 1, 0), r + .012, .06, 'leather', min(.012*.5, .06*.2)))
    return a.build()


def iron_wand():
    """Tier-two iron wand: a plain iron rod with steel collars, three claws round a pale stone."""
    a = Assembly()
    haft = Haft(a, WAND, 'iron')
    a.add(collar((0, -.09, 0), (0, 1, 0), .066, .26, 'leather', .012))
    a.add(collar((0, -.26, 0), (0, 1, 0), .07, .06, 'steel'))
    a.add(collar((0, .62, 0), (0, 1, 0), .058, .08, 'steel'))
    claws(a, .64, .88, .10, 3, .022, 'iron', 90)
    a.add(mf.Manifold.sphere(.065, 10).translate((0, .71, 0)), 'moonstone')
    return a.build()


def ruby_wand():
    """Tier-three ruby scepter: a flared gold cup holding a big ruby, red leather grip, gold butt cap."""
    a = Assembly()
    haft = Haft(a, WAND)
    a.add(collar((0, -.09, 0), (0, 1, 0), .066, .26, 'leather', .012))
    butt_cap(a, haft)
    a.add(tube([(0, .58, 0), (0, .60, 0), (0, .68, 0), (0, .74, 0)], [.04, .055, .085, .095], 'gold', 8))
    a.add(crystal((0, .80, 0), .09, .26, 'gem'))
    return a.build()


KINDS = {'archmage_staff': archmage, 'emerald_staff': emerald, 'bone_staff': bone, 'crooked_staff': crooked,
         'horned_staff': horned, 'iron_staff': iron, 'bone_wand': bone_wand, 'carved_wand': carved_wand,
         'celestial_wand': celestial_wand, 'crooked_wand': crooked_wand, 'iron_wand': iron_wand,
         'ruby_wand': ruby_wand}
staff = dispatch(KINDS, 'staff')
