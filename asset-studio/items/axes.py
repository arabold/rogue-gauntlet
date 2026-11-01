"""KayKit-style axes: thick bent hafts, rope-wrapped grips, wedge bits, bound sockets and silhouette wear.

Built from the shared weapon construction in `items.forge`. One-handed bits point to -X.
"""

import numpy as np
from prefabs.details import hilts
from items.forge import Haft, Blade, plate, gem, placed, dispatch
from items.solid import Assembly, box

# One-handed haft stations (x, y, radius): a flared butt bending back from the blade side, then straight under the head.
ONE_HANDED = [(.022, -.285, .048), (.024, -.265, .066), (.020, -.20, .063), (.012, -.10, .059),
              (.004, .05, .057), (0, .30, .055), (0, .60, .053), (0, .99, .050), (0, 1.02, .038)]


def bearded():
    """Tier-three Viking axe: long hooked beard, rope-bound socket, rope grip, chips and a notched beard."""
    a = Assembly()
    haft = Haft(a, ONE_HANDED)
    haft.wraps(-.24, .10)
    haft.band(.60, .07, 'cloth')
    haft.knob(hilts.spiked_cap, length=.11)
    a.add(box((.15, .30, .15), (0, .80, 0), .025), 'steel')
    a.add(placed(hilts.rope_cross(.075, .075, .22), (0, .80)))
    edge = [(-.47, 1.02), (-.555, .95), (-.60, .82), (-.605, .66), (-.57, .50), (-.50, .36)]
    bit = Blade(a, [(0, .92), (-.18, .90), (-.33, .94), *edge, (-.42, .37), (-.33, .45), (-.22, .56), (-.10, .64), (0, .67)],
                edge, .11, .10, taper=(.065, -.10))
    bit.chip(.38, .045, .06, .3)
    bit.comb(.74, .92, 3, .03, .035)
    bit.finish()
    return a.build()


def executioner():
    """Tier-five hatchet: broad squared bit, gold socket and cheek, a gem, one small nick."""
    a = Assembly()
    haft = Haft(a, ONE_HANDED)
    haft.band(-.01, .30, 'leather', proud=.014)
    haft.band(.60, .05, 'gold')
    haft.knob(hilts.gem_cap)
    a.add(box((.19, .34, .17), (0, .80, 0), .03), 'gold')
    edge = [(-.46, 1.05), (-.56, .99), (-.60, .86), (-.60, .70), (-.56, .57), (-.46, .51)]
    bit = Blade(a, [(0, .93), (-.20, .92), (-.34, .97), *edge, (-.34, .59), (-.20, .66), (0, .67)],
                edge, .11, .10, taper=(.065, -.10))
    bit.chip(.55, .025, .04, .2)
    bit.finish()
    plate(a, [(-.02, .69), (-.02, .91), (-.18, .90), (-.27, .86), (-.27, .72), (-.18, .68)], .071)
    gem(a, (0, .80), .045, .085)
    return a.build()


def double(bits, socket, socket_slot):
    """Shared two-handed haft and socket; `bits` are (outline, edge) for the -X side, mirrored to +X."""
    a = Assembly()
    haft = Haft(a, [(.018, -.455, .066), (.02, -.43, .092), (.014, -.30, .090), (.006, -.10, .087),
                    (0, .20, .085), (0, .60, .083), (0, 1.00, .080), (0, 1.44, .078), (0, 1.47, .060)])
    a.add(box(*socket), socket_slot)
    made = []
    for mirror in (1, -1):
        outline, edge = (np.asarray(points, float)*(mirror, 1) for points in bits)
        made.append(Blade(a, outline, edge, .13, .13, .018, taper=(.075, -.12*mirror)))
    return a, haft, made


# -X bits as (outline, cutting edge); the edge is a run of the outline.
CLEAVER_EDGE = [(-.64, 1.44), (-.70, 1.32), (-.72, 1.12), (-.70, .94), (-.64, .84)]
CLEAVER = ([(0, 1.34), (-.36, 1.36), *CLEAVER_EDGE, (-.36, .92), (0, .96)], CLEAVER_EDGE)
CRESCENT_EDGE = [(-.58, 1.60), (-.69, 1.48), (-.75, 1.28), (-.75, 1.02), (-.69, .82), (-.58, .70)]
CRESCENT = ([(0, 1.32), (-.20, 1.30), (-.40, 1.42), *CRESCENT_EDGE, (-.40, .88), (-.20, 1.00), (0, .98)], CRESCENT_EDGE)


def cleaver_double():
    """Tier-two iron greataxe: squared cleaver bits on riveted straps, a rope grip, combed and chipped edges."""
    a, haft, (left, right) = double(CLEAVER, ((.24, .48, .22), (0, 1.15, 0), .035), 'iron')
    haft.wraps(-.30, .10)
    for y in (.82, .68):
        haft.fit(hilts.riveted_band, y, height=.09)
    haft.knob(hilts.spiked_cap)
    left.chip(.30, .06, .075, .3)
    left.chip(.72, .045, .055, -.3)
    right.chip(0, .07, .12)
    right.comb(.35, .75, 4, .04, .045)
    left.finish()
    right.finish()
    return a.build()


def runic_double():
    """Tier-five gilded battleaxe: flared crescent bits, rune-carved gold cheeks, gems, one nick."""
    a, haft, (left, right) = double(CRESCENT, ((.28, .52, .24), (0, 1.15, 0), .04), 'gold')
    haft.band(0, .40, 'cloth', proud=.016)
    for y in (.85, .24):
        haft.band(y, .06, 'gold')
    haft.knob(hilts.gem_cap)
    right.chip(.60, .025, .04, -.2)
    left.finish()
    right.finish()
    rune = [((-.23, 1.07), (-.23, 1.23)), ((-.23, 1.15), (-.31, 1.22)), ((-.23, 1.15), (-.15, 1.22))]
    for mirror in (1, -1):
        cheek = np.array([(-.06, 1.03), (-.06, 1.27), (-.24, 1.26), (-.38, 1.20), (-.38, 1.06), (-.24, 1.04)])*(mirror, 1)
        plate(a, cheek, .081, runes=[(np.multiply(p, (mirror, 1)), np.multiply(q, (mirror, 1))) for p, q in rune])
    gem(a, (0, 1.15), .05, .12)
    return a.build()


KINDS = {'bearded': bearded, 'executioner': executioner, 'cleaver_double': cleaver_double, 'runic_double': runic_double}
axe = dispatch(KINDS, 'axe')
