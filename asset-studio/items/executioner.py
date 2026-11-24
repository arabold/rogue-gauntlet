"""The Forsaken Warden boss's executioner blade and sun shield: continuous bevels and physically joined fittings.

All dimensions use metres, +Y follows the weapon, and the origin is the hand grip.
Color regions describe materials, not baked lighting or decorative mesh shells.
Catalog weapons are built by the KayKit-style families (`items.forge` and its builders).
"""

import numpy as np
import manifold3d as mf
from core import kit
from items.forge import loft
from items.solid import Assembly, lathe, tube, box

def blade(stations):
    """Eight-sided diamond blade with a broad face, central ridge and honed edges.

    Stations contain center X, Y, half-width and half-thickness. The changing
    center creates an actual curved saber/scythe, rather than a rectangular slab.
    """
    section = np.array([[-1, 0], [-.68, -.78], [0, -1], [.68, -.78],
                        [1, 0], [.68, .78], [0, 1], [-.68, .78]])
    rings = [np.column_stack([x + section[:, 0]*w, np.full(8, y), section[:, 1]*t])
             for x, y, w, t in stations]
    result = loft(rings, 'steel')
    # Only the narrow bevel faces use the edge material; the center stays one albedo.
    sides = (len(rings)-1)*16
    for i in range(sides):
        if (i//2) % 8 in (0, 3, 4, 7):
            result.slots[i] = 'edge'
    return result


def collar(a, y, radius=.065, height=.065, slot='iron'):
    """A chamfered ferrule overlapping its shaft, with no floating trim."""
    a.add(lathe([(radius*.86, y-height/2), (radius, y-height*.30),
                 (radius, y+height*.30), (radius*.86, y+height/2)], 12), slot)


def grip(a, lo=-.17, hi=.17, radius=.047, slot='leather'):
    """One profiled grip with shallow wrap grooves in its exterior geometry."""
    profile = [(radius*.82, lo-.012), (radius, lo)]
    for y in np.linspace(lo+.014, hi-.014, 8):
        profile.extend([(radius*.97, float(y-.005)), (radius*.90, float(y)), (radius*.97, float(y+.005))])
    profile.extend([(radius, hi), (radius*.82, hi+.012)])
    a.add(lathe(profile, 12), slot)
    collar(a, lo, radius*1.16, .045)
    collar(a, hi, radius*1.16, .045)


def pommel(a, y=-.23, radius=.073, slot='steel'):
    a.add(lathe([(radius*.43, y-radius*.55), (radius*.88, y-radius*.4),
                 (radius, y), (radius*.88, y+radius*.35), (radius*.4, y+radius*.6)], 12), slot)
    a.add(tube((0, y, 0), (0, -.12, 0), .031, 10), 'iron')


def guard(a, width=.26, y=.22, curve=.04, slot='steel'):
    """Swept quillons, flattened front/back, joined through the blade's tang."""
    path = [(-width, y+curve, 0), (-width*.78, y+.012, 0),
            (-width*.35, y, 0), (0, y, 0), (width*.35, y, 0),
            (width*.78, y+.012, 0), (width, y+curve, 0)]
    a.add(kit.sweep(path, kit.ring_profile(.045, .036, 8), slot), slot)
    for sign in [-1, 1]:
        a.add(mf.Manifold.sphere(.051, 12).scale((.8, 1.05, .72)).translate((sign*width, y+curve, 0)), slot)
    a.add(box((.13, .095, .09), (0, y, 0), .018), 'iron')


def executioner_sword():
    """The warden's broad two-handed executioner blade with a brass guard and pommel."""
    a=Assembly()
    grip(a,-.36,.22,.075);pommel(a,-.43,.11,'brass');guard(a,.47,.28,.025,'brass')
    a.add(blade([(0,.25,.21,.065),(0,.44,.30,.070),(0,2.38,.30,.060),(0,2.50,.23,.045)]))
    return a.build()


def fit_shield(piece, yaw=-12., standoff=.27):
    """Stand the board off a large forearm at its real hand grip and lengthen its rear supports to reach it.

    The leather grip vertices stay fixed while the existing metal posts extend to the board, so the shield still sits
    on the hand slot's verified frame; `yaw` opens the board slightly away from the body.
    """
    v = piece.verts.copy()
    blend = np.clip((v[:, 2]-.065)/.11, 0, 1)
    blend = blend*blend*(3-2*blend)
    board = (np.abs(v[:, 0]) > .42) | (np.abs(v[:, 1]) > .11)
    blend[board] = 1
    blend[np.unique(piece.faces[piece.slots == 'leather'])] = 0
    v[:, 2] += standoff*blend
    return kit.Piece(v@kit.rotation((0, 1, 0), yaw).T, piece.faces, piece.slots)


def warden_shield(size=1.9, fold_degrees=12., yaw=-12., standoff=.27):
    """The warden's red heater shield with its brass sun and center gem, fitted to stand off his forearm armor.

    Same conventions as the blade: metres, +Y up the board, +Z out of its face, origin at the hand grip.
    """
    from items import shieldcraft
    shield = shieldcraft.shieldcraft({'kind': 'heroic_sun', 'details': False, 'fold_degrees': fold_degrees})
    gem = kit.gem(.16, 'gem', cut='oval', depth=.055)
    gem.verts += [0, 0, .243]
    shield = kit.Piece.merge(shield, gem)
    return fit_shield(kit.Piece(shield.verts*size, shield.faces, shield.slots), yaw, standoff)
