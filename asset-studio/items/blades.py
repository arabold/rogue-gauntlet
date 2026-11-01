"""KayKit-style swords and daggers: thick bevelled blades with angular points, chunky guards and
pommels, short banded grips, and edge chips that scale inversely with tier.

Built from the shared weapon construction in `items.forge`. Blades point up +Y from a guard
just above the grip; the origin is the hand grip.
"""

import numpy as np
from core import kit
from items.forge import Haft, Blade, plate, disc, ring, gem, dispatch
from items.solid import Assembly


def grip(a, lo, hi, radius, slot='leather'):
    """A straight eight-sided grip with chamfered ends, overlapping its pommel and guard."""
    return Haft(a, [(0, lo-.012, radius*.8), (0, lo, radius), (0, hi, radius*.96), (0, hi+.012, radius*.8)], slot)


def straight(a, stations, tip, thickness, bevel, edge_thickness=.014, slot='steel', edge_slot='edge'):
    """A double-edged blade: right-side (y, half width) stations from the guard up, mirrored, to `tip` (x, y).

    Both sides and the point form one convex cutting edge, so the bevels meet in a ridge at the point.
    """
    right = [(w, y) for y, w in stations]
    outline = [*right, tuple(tip), *[(-w, y) for w, y in right[::-1]]]
    return Blade(a, outline, outline, thickness, bevel, edge_thickness, slot, edge_slot)


def curved(a, stations, tip, thickness, bevel, edge_thickness=.014, slot='steel', edge_slot='edge'):
    """A single-edged blade bending toward +X: (y, center x, half width) stations from the guard up.

    The convex -X side and the point carry the cutting edge; the concave spine stays square.
    """
    spine = [(x+w, y) for y, x, w in stations]
    edge = [tuple(tip), *[(x-w, y) for y, x, w in stations[::-1]]]
    return Blade(a, [*spine, *edge], edge, thickness, bevel, edge_thickness, slot, edge_slot)


def bow(a, path, slot, radius=.026):
    """A chunky knuckle bow swept from the guard down to the pommel."""
    a.add(kit.sweep(path, kit.ring_profile(radius, radius*.85, 6), slot, up=(0, 0, 1)))


def knight():
    """Tier-three one-handed broadblade: plain steel, angled bar guard, round pommel, two chips."""
    a = Assembly()
    disc(a, (0, -.28), .09, .058, 'iron')
    g = grip(a, -.235, .19, .058)
    for y in (-.13, .07):
        g.band(y, .06, 'wrap', .014)
    plate(a, [(-.26, .26), (.26, .26), (.30, .21), (.26, .16), (-.26, .16), (-.30, .21)], .065, 'iron')
    blade = straight(a, [(.16, .135), (1.20, .125)], (0, 1.46), .075, .05)
    blade.chip(.22, .045, .07, .2)
    blade.chip(.84, .035, .055, -.2)
    blade.finish()
    return a.build()


def sunblade():
    """Tier-five gilded sword: flared steel blade, winged gold guard and pommel with gems, cream grip."""
    a = Assembly()
    disc(a, (0, -.28), .085, .058, 'gold')
    gem(a, (0, -.28), .04, .058)
    g = grip(a, -.235, .19, .058, 'cloth')
    for y in (-.17, .13):
        g.band(y, .045, 'gold', .014)
    plate(a, [(-.10, .28), (.10, .28), (.10, .15), (-.10, .15)], .066, 'gold')
    for m in (1, -1):
        plate(a, np.array([(.05, .16), (.05, .27), (.30, .38), (.25, .19)])*(m, 1), .056, 'gold')
    gem(a, (0, .215), .04, .066)
    blade = straight(a, [(.16, .12), (.72, .145), (1.18, .13)], (0, 1.47), .075, .05)
    blade.finish()
    plate(a, [(0, .22), (.075, .32), (0, .42), (-.075, .32)], .05, 'gold')
    return a.build()


def claymore():
    """Tier-two iron claymore: long broad blade, forward-angled quillons, heavy wear and a broken point."""
    a = Assembly()
    disc(a, (0, -.41), .10, .064, 'iron')
    g = grip(a, -.35, .25, .064)
    for y in (-.24, -.04, .14):
        g.band(y, .06, 'wrap', .016)
    plate(a, [(-.12, .31), (.12, .31), (.12, .20), (-.12, .20)], .068, 'iron')
    for m in (1, -1):
        plate(a, np.array([(.08, .20), (.08, .31), (.38, .47), (.44, .38)])*(m, 1), .06, 'iron')
    blade = straight(a, [(.22, .17), (1.62, .155)], (0, 1.96), .09, .065, .016)
    blade.chip(.16, .055, .08, .3)
    blade.chip(.38, .045, .065, -.2)
    blade.chip(.5, .07, .2)
    blade.chip(.80, .06, .08, .2)
    blade.finish()
    return a.build()


def winged():
    """Tier-five greatsword: long steel blade, gold wings swept up from the guard, gems, cream grip."""
    a = Assembly()
    disc(a, (0, -.41), .095, .064, 'gold')
    gem(a, (0, -.41), .045, .064)
    g = grip(a, -.35, .25, .064, 'cloth')
    for y in (-.25, -.05, .15):
        g.band(y, .045, 'gold', .014)
    plate(a, [(-.11, .33), (.11, .33), (.11, .18), (-.11, .18)], .07, 'gold')
    for m in (1, -1):
        plate(a, np.array([(.06, .19), (.06, .31), (.46, .62), (.40, .30)])*(m, 1), .058, 'gold')
    gem(a, (0, .255), .045, .07)
    blade = straight(a, [(.24, .16), (1.30, .155), (1.62, .13)], (0, 1.96), .09, .065, .016)
    blade.finish()
    return a.build()


def broadsword():
    """Tier-three steel two-hander: KayKit horned guard, banded grip, a couple of chips."""
    a = Assembly()
    disc(a, (0, -.31), .09, .062, 'iron')
    g = grip(a, -.255, .22, .062)
    for y in (-.13, .07):
        g.band(y, .06, 'wrap', .015)
    plate(a, [(-.11, .30), (.11, .30), (.11, .17), (-.11, .17)], .066, 'iron')
    for m in (1, -1):
        plate(a, np.array([(.06, .17), (.06, .28), (.27, .30), (.33, .42)])*(m, 1), .058, 'iron')
    blade = straight(a, [(.20, .15), (1.12, .14)], (0, 1.42), .08, .055)
    blade.chip(.30, .045, .065, .2)
    blade.chip(.70, .04, .055, -.2)
    blade.finish()
    return a.build()


def greatsword():
    """Tier-four steel greatsword: tapering blade, an S-curved crossguard with one quillon up and one down, one nick."""
    a = Assembly()
    disc(a, (0, -.45), .10, .064, 'iron')
    g = grip(a, -.385, .22, .064)
    for y in (-.28, -.08, .12):
        g.band(y, .06, 'wrap', .015)
    plate(a, [(-.10, .31), (.10, .31), (.10, .19), (-.10, .19)], .07, 'iron')
    a.add(kit.sweep([(-.40, .44, 0), (-.33, .35, 0), (-.20, .28, 0), (0, .25, 0), (.20, .22, 0), (.33, .15, 0),
                     (.40, .06, 0)], kit.ring_profile(1, 1.15, 6), 'iron', up=(0, 0, 1),
                    scale=[.022, .04, .052, .058, .052, .04, .022]))
    blade = straight(a, [(.24, .16), (1.40, .12), (1.80, .09)], (0, 2.0), .085, .06, .016)
    blade.chip(.66, .03, .05, .2)
    blade.finish()
    return a.build()


def rapier():
    """Tier-two rapier: narrow ridged thrusting blade, round shell, chunky brass knuckle bow, one nick."""
    a = Assembly()
    disc(a, (0, -.26), .075, .055, 'brass')
    g = grip(a, -.21, .17, .055)
    for y in (-.11, .06):
        g.band(y, .05, 'wrap', .012)
    plate(a, [(-.17, .225), (.17, .225), (.20, .19), (.17, .155), (-.17, .155), (-.20, .19)], .06, 'brass')
    disc(a, (0, .23), .09, .03, 'brass', .01)
    bow(a, [(.17, .18, 0), (.21, .08, 0), (.20, -.08, 0), (.13, -.20, 0), (.03, -.26, 0)], 'brass')
    blade = straight(a, [(.18, .05), (1.20, .04)], (0, 1.50), .068, .06, .012)
    blade.chip(.3, .022, .035, .2)
    blade.finish()
    return a.build()


def saber():
    """Tier-two curved saber: single edge on the convex side, brass guard and knuckle bow, two chips."""
    a = Assembly()
    disc(a, (0, -.25), .072, .055, 'brass')
    g = grip(a, -.21, .155, .055)
    for y in (-.10, .05):
        g.band(y, .05, 'wrap', .012)
    plate(a, [(-.15, .215), (.12, .215), (.15, .18), (.12, .145), (-.15, .145), (-.18, .18)], .06, 'brass')
    bow(a, [(-.14, .16, 0), (-.19, .06, 0), (-.19, -.10, 0), (-.12, -.22, 0), (-.03, -.25, 0)], 'brass')
    blade = curved(a, [(.17, 0, .065), (.45, .01, .07), (.75, .04, .068), (1.05, .10, .06), (1.30, .18, .045)],
                   (.29, 1.48), .065, .04)
    blade.chip(.35, .04, .06, .2)
    blade.chip(.70, .035, .05, -.2)
    blade.finish()
    return a.build()


def dirk():
    """Tier-three heavy hunting dirk: broad leaf blade, block guard, wooden grip, one chip."""
    a = Assembly()
    disc(a, (0, -.22), .08, .056, 'iron')
    g = grip(a, -.175, .11, .056, 'wood')
    g.band(-.03, .05, 'wrap', .013)
    plate(a, [(-.15, .155), (.15, .155), (.18, .12), (.15, .085), (-.15, .085), (-.18, .12)], .062, 'iron')
    blade = straight(a, [(.11, .085), (.45, .11), (.75, .085)], (0, .99), .07, .045)
    blade.chip(.30, .035, .055, .2)
    blade.finish()
    return a.build()


def fang():
    """Tier-five obsidian fang: curved black glass blade, small winged gold guard, a violet gem."""
    a = Assembly()
    disc(a, (0, -.22), .078, .056, 'gold')
    gem(a, (0, -.22), .032, .056)
    g = grip(a, -.175, .11, .056)
    g.band(-.03, .05, 'gold', .012)
    plate(a, [(-.07, .17), (.07, .17), (.07, .07), (-.07, .07)], .062, 'gold')
    for m in (1, -1):
        plate(a, np.array([(.04, .08), (.04, .16), (.20, .24), (.17, .09)])*(m, 1), .046, 'gold')
    gem(a, (0, .12), .032, .062)
    blade = curved(a, [(.12, 0, .085), (.40, .015, .08), (.68, .05, .062), (.88, .10, .038)], (.17, 1.0),
                   .07, .045, .014, 'obsidian', 'obsidian_edge')
    blade.finish()
    return a.build()


def short():
    """Tier-one short dagger: plain steel blade, small block guard, wooden grip, two chips."""
    a = Assembly()
    disc(a, (0, -.21), .07, .054, 'iron')
    g = grip(a, -.175, .08, .054, 'wood')
    g.band(-.05, .045, 'wrap', .012)
    plate(a, [(-.11, .125), (.11, .125), (.14, .09), (.11, .055), (-.11, .055), (-.14, .09)], .06, 'iron')
    blade = straight(a, [(.09, .07), (.48, .06)], (0, .69), .065, .04)
    blade.chip(.25, .035, .05, .2)
    blade.chip(.80, .03, .045, -.2)
    blade.finish()
    return a.build()


def practice():
    """Tier-one wooden practice greatsword: blunt pale-wood blade, wooden guard and pommel, dents from drills."""
    a = Assembly()
    disc(a, (0, -.41), .09, .062, 'wood')
    g = grip(a, -.36, .23, .062)
    for y in (-.25, -.05, .13):
        g.band(y, .06, 'wrap', .015)
    plate(a, [(-.24, .29), (.24, .29), (.27, .235), (.24, .18), (-.24, .18), (-.27, .235)], .07, 'wood')
    blade = straight(a, [(.21, .12), (1.36, .115)], (0, 1.66), .075, .035, .04, 'birch', 'birch')
    blade.chip(.20, .04, .07, .3)
    blade.chip(.62, .035, .06, -.3)
    blade.chip(.85, .045, .08, .2)
    blade.finish()
    return a.build()


def crescent():
    """Tier-three crescent longblade: long ridged blade, a guard drooping toward the hands, ring pommel."""
    a = Assembly()
    ring(a, (0, -.47), .095, .045, .062, 'iron')
    g = grip(a, -.40, .24, .062)
    g.wraps(-.36, .18)
    a.add(kit.sweep([(-.33, .05, 0), (-.27, .16, 0), (-.15, .245, 0), (0, .27, 0), (.15, .245, 0), (.27, .16, 0),
                     (.33, .05, 0)], kit.ring_profile(1, 1.1, 6), 'iron', up=(0, 0, 1),
                    scale=[.02, .046, .06, .066, .06, .046, .02]))
    plate(a, [(-.075, .31), (.075, .31), (.075, .21), (-.075, .21)], .068, 'iron')
    blade = straight(a, [(.24, .10), (1.30, .09), (1.70, .068)], (0, 2.04), .08, .12, .016)
    blade.chip(.25, .04, .06, .2)
    blade.chip(.72, .035, .05, -.2)
    blade.finish()
    return a.build()


def curved_great():
    """Tier-four curved greatblade: long single-edged blade, round guard, long wrapped grip, one nick."""
    a = Assembly()
    g = grip(a, -.42, .19, .062, 'cloth')
    g.wraps(-.36, .10, swell=.016)
    g.band(-.42, .05, 'iron', .016)
    g.band(.21, .04, 'iron', .085)
    plate(a, [(-.075, .30), (.075, .30), (.075, .21), (-.075, .21)], .068, 'iron')
    blade = curved(a, [(.22, 0, .075), (.70, .015, .075), (1.20, .05, .07), (1.60, .10, .062), (1.90, .16, .05)],
                   (.25, 2.08), .075, .05, .016)
    blade.chip(.55, .025, .04, .2)
    blade.finish()
    return a.build()


KINDS = {'knight': knight, 'sunblade': sunblade, 'claymore': claymore, 'winged': winged, 'broadsword': broadsword,
         'greatsword': greatsword, 'rapier': rapier, 'saber': saber, 'dirk': dirk, 'fang': fang, 'short': short,
         'practice': practice, 'crescent': crescent, 'curved_great': curved_great}
blade = dispatch(KINDS, 'blade')
