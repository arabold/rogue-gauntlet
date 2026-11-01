"""Weapon hilt hardware in KayKit's chunky style: knobs that cap a haft or grip, and handles that wrap,
strap, band, bind or decorate it.

Each part is one closed piece built around the Y axis at the origin and sized from the `radius` of the haft
or grip it fits, so a weapon builder only moves it into place. Knobs hang below the origin (-Y); handles are
centered on it. Overlapping sub-parts are solid-unioned, so every part can carve and join like any other solid.
"""

import numpy as np
import manifold3d as mf
from core import kit
from items.solid import Assembly

OCTAGON = kit.ring_profile(1, 1, 8)


def _sweep_y(stations, slot, profile=OCTAGON):
    """A closed sweep up the Y axis through (y, radius) stations; the end stations chamfer it."""
    s = np.asarray(stations, float)
    return kit.sweep(np.column_stack([np.zeros(len(s)), s[:, 0], np.zeros(len(s))]), profile, slot, up=(0, 0, 1),
                     scale=s[:, 1])


def _piece(shape, slot):
    mesh = shape.to_mesh()
    p, f = np.asarray(mesh.vert_properties[:, :3], float), np.asarray(mesh.tri_verts)
    return kit.Piece(p, f, slot)


def _ring_xy(radius, sides=8):
    t = 2*np.pi*(np.arange(sides)+.5)/sides
    return np.column_stack([np.cos(t), np.sin(t)])*radius


# Knobs ------------------------------------------------------------------------------------------------------------

def disc_pommel(radius=.09, depth=.06, slot="iron", chamfer=.014):
    """A chunky eight-faceted coin facing the viewer: KayKit's round sword pommel."""
    outer = _ring_xy(radius)
    inner = _ring_xy(radius - chamfer)
    half = depth/2
    return kit.hull(np.vstack([np.column_stack([outer, np.full(8, z)]) for z in (half-chamfer, chamfer-half)]
                              + [np.column_stack([inner, np.full(8, z)]) for z in (half, -half)]), slot)


def ring_pommel(outer=.095, inner=.045, depth=.12, slot="iron"):
    """A faceted open ring facing the viewer, for longswords."""
    section = mf.CrossSection([_ring_xy(outer), _ring_xy(inner)[::-1]])
    return _piece(mf.Manifold.extrude(section, depth).translate((0, 0, -depth/2)), slot)


def ball_knob(radius=.06, slot="iron"):
    """A faceted ball pommel or haft end."""
    return _piece(mf.Manifold.sphere(radius, 8).translate((0, -radius*.8, 0)), slot)


def spiked_cap(radius=.06, length=.16, slot="iron"):
    """A metal sleeve over a haft's butt ending in a four-sided point, as on KayKit polearms."""
    t = 2*np.pi*(np.arange(8)+.5)/8
    ring = np.column_stack([np.cos(t), np.zeros(8), np.sin(t)])*(radius + .014)
    return kit.hull(np.vstack([ring + (0, .04, 0), ring*1.06 + (0, -.03, 0), ring*.55 + (0, -.07, 0),
                               [(0, -length, 0)]]), slot)


def flared_knob(radius=.055, slot="wood"):
    """A wooden haft end that swells into a flared knob."""
    return _sweep_y([(.02, radius), (-.03, radius*1.22), (-.07, radius*1.32), (-.095, radius*1.08), (-.11, radius*.7)],
                    slot)


def gem_cap(radius=.06, slot="gold", gem_slot="gem"):
    """A metal cap over a haft's butt with a gem set in its end."""
    a = Assembly()
    a.add(_sweep_y([(-.012, radius*.82), (0, radius + .014), (.07, radius + .014), (.08, radius*.9)], slot))
    g = kit.gem(.03, gem_slot, "diamond")
    g.verts = (g.verts + (0, 0, -.004)) @ kit.rotation([1, 0, 0], 90).T + (0, -.012, 0)
    a.add(g)
    return a.build()


# Handles ----------------------------------------------------------------------------------------------------------

def rope_wraps(radius=.055, length=.30, slot="wrap", swell=.022, turns=None):
    """A stack of bulging rope turns wrapped round a grip, wider than the haft it covers: KayKit's cream grips.

    One closed sweep whose radius waves from a tight seam to a full turn, so the wrap reads as separate turns
    without stacking overlapping rings.
    """
    turns = turns or max(2, int(round(length/.055)))
    pitch = length/turns
    stations = [(-length/2 - .006, radius*.92)]
    for k in range(turns):
        y0 = -length/2 + k*pitch
        stations += [(y0, radius + swell*.35), (y0 + pitch*.25, radius + swell), (y0 + pitch*.75, radius + swell)]
    stations += [(length/2, radius + swell*.35), (length/2 + .006, radius*.92)]
    return _sweep_y(stations, slot)


def strap(radius=.055, height=.075, slot="leather", proud=.022, tilt=0.):
    """A chunky wrap band standing proud of a haft; `tilt` (degrees) winds it aslant like a leather strap."""
    r, c = radius + proud, min(proud*.5, height*.2)
    piece = _sweep_y([(-height/2, r - c), (-height/2 + c, r), (height/2 - c, r), (height/2, r - c)], slot)
    piece.verts = piece.verts @ kit.rotation([0, 0, 1], tilt).T
    return piece


def riveted_band(radius=.055, height=.10, slot="iron", rivet_slot="steel", rivets=2):
    """A metal strap band fastened by domed rivets on its front and back, as on KayKit cleavers."""
    a = Assembly()
    a.add(strap(radius, height, slot, proud=.018))
    for side in (1, -1):
        for k in range(rivets):
            y = (k - (rivets - 1)/2)*height*.45
            a.add(mf.Manifold.sphere(.017, 8).scale((1, 1, .7)).translate((0, y, side*(radius + .018))), rivet_slot)
    return a.build()


def rope_cross(half_width=.08, half_depth=.075, height=.20, slot="wrap", thickness=.018):
    """A rope binding crossed over the front and back of a socket block, its ends wrapped round the sides."""
    a = Assembly()
    w, h, d = half_width, height/2, half_depth - thickness*.3
    profile = kit.ring_profile(thickness, thickness, 6)
    for z in (d, -d):
        for sign in (1, -1):
            a.add(kit.sweep([(-w, -sign*h, z), (0, 0, z*1.04), (w, sign*h, z)], profile, slot, up=(0, 0, 1)))
    for x in (w - thickness*.3, -(w - thickness*.3)):
        for y in (h, -h):
            a.add(kit.sweep([(x, y, d), (x*1.04, y, 0), (x, y, -d)], profile, slot, up=(0, 1, 0)))
    return a.build()


def feather_tuft(radius=.06, count=6, length=.20, slot="feather"):
    """A collar of broad, chunky feathers splaying down and out from a haft, as under KayKit spear heads."""
    a = Assembly()
    a.add(strap(radius, .05, slot, proud=.016))
    for k in range(count):
        t = 2*np.pi*k/count
        d = np.array([np.cos(t), 0, np.sin(t)])
        side = np.cross(d, (0, 1, 0))
        base = d*(radius + .01)
        tip = base + d*length*.6 + (0, -length*.85, 0)
        mid = base*.4 + tip*.6 + d*.025
        points = [base + side*.018, base - side*.018, mid + side*.048, mid - side*.048, tip,
                  base + d*.014, mid + d*.018]
        a.add(kit.hull(np.asarray(points), slot))
    return a.build()


HILTS = {
    "disc_pommel": disc_pommel,
    "ring_pommel": ring_pommel,
    "ball_knob": ball_knob,
    "spiked_cap": spiked_cap,
    "flared_knob": flared_knob,
    "gem_cap": gem_cap,
    "rope_wraps": rope_wraps,
    "strap": strap,
    "riveted_band": riveted_band,
    "rope_cross": rope_cross,
    "feather_tuft": feather_tuft,
}


def catalog():
    """Yield every knob and handle at its default size, as fitted to a 0.055 haft."""
    colors = {"iron": (118, 128, 138), "steel": (150, 164, 176), "gold": (228, 176, 68), "gem": (222, 42, 78),
              "wood": (176, 98, 66), "wrap": (222, 206, 176), "leather": (128, 74, 50), "feather": (70, 120, 200)}
    for name, fn in HILTS.items():
        yield name.replace("_", " "), fn(), None, colors
