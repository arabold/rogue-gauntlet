"""Dungeon fountains: stone fonts and basins holding a pool of glowing liquid.

All dimensions use metres; the origin is the floor center and +Y is up. Each design
unions its stonework and trim into one closed basin solid, hollowed down to a stone
floor, so a drained fountain is genuinely empty. The liquid is a separate closed
solid that fills those hollows to the pool level, together with any stream from a
spout or overflow lip: a real body of water resting in the basin, exported as its own
`Liquid` node the game hides once the fountain is drunk dry.

The liquid slot is emissive in the recipes; health and mana variants differ only in
its color. Rising sparkles and surface shimmer are game-side effects placed on the
pools listed in `POOLS`, so the art stays a static, gameplay-free mesh.
"""

import numpy as np
import manifold3d as mf
from core import kit
from items.solid import Assembly, lathe, tube

# Liquid level and the largest circle inside each pool, for placing game-side effects.
POOLS = {'font': [(1.02, .48)], 'chalice': [(1.12, .45)], 'spring': [(.44, .60)],
         'tiered': [(.66, .80), (1.52, .30)]}


def stone(size, bevel=.03, radius=None):
    """A chamfered block centered on the origin. With `radius`, its sides taper toward the
    ring center so blocks laid around a circle meet along radial joints; +Z faces outward."""
    p = kit.bevel_box(size, bevel, 'stone').verts
    if radius:
        p[:, 0] *= (radius + p[:, 2]) / radius
    return mf.Manifold.hull_points(p.astype(np.float32))


def turned(a, profile, sides, slot, offset=0.):
    """A lathe turned `offset` degrees about Y, so flat polygon faces can face the camera."""
    a.add(lathe(profile, sides).rotate((0, offset, 0)), slot)


def ring(a, radius, depth, lo, hi, count, offset=0.):
    """One course of tapered blocks around a circle; slight overlap leaves chamfered joints."""
    width = 2*np.pi*radius/count + .006
    for k in range(count):
        yaw = k*360/count + offset
        theta = np.radians(yaw)
        a.add(stone((width, hi - lo, depth), .045, radius).rotate((0, yaw, 0))
              .translate((np.sin(theta)*radius, (lo + hi)/2, np.cos(theta)*radius)), 'stone')


def band(profile, inner, sides):
    """A lathe ring rather than a disc: `lathe` closes on the axis, so bore out its middle."""
    lo, hi = profile[0][1] - .01, profile[-1][1] + .01
    return lathe(profile, sides) - lathe([(inner, lo), (inner, hi)], sides)


def stream(a, path, width, depth):
    """A flattened falling ribbon of liquid that narrows from its lip into the pool below."""
    u = np.linspace(0, 1, len(path))
    a.add(kit.sweep(np.asarray(path, float), kit.ring_profile(width, depth, 8), 'liquid', scale=1 - .3*u), 'liquid')


def font(a, water):
    """Octagonal baptismal font on a two-course flagstone plinth."""
    pitch = .39
    for i in range(4):
        for j in range(4):
            a.add(stone((pitch + .004, .14, pitch + .004)).translate(((i - 1.5)*pitch, .07, (j - 1.5)*pitch)), 'stone_dark')
    for i in range(2):
        for j in range(2):
            a.add(stone((.564, .13, .564)).translate(((i - .5)*.56, .195, (j - .5)*.56)), 'stone_dark')
    # Foot, shaft and capital, then the flaring bowl with a rolled rim and a short inner wall.
    turned(a, [(.37, .25), (.37, .32), (.29, .37), (.25, .38), (.24, .55), (.31, .58), (.31, .63)], 8, 'stone', 22.5)
    turned(a, [(.27, .60), (.36, .66), (.50, .76), (.60, .87), (.64, .95), (.70, .97), (.71, 1.05),
               (.66, 1.09), (.56, 1.09), (.53, 1.06), (.52, 1.02), (.46, .93), (.36, .87), (.20, .85)], 8, 'stone', 22.5)
    # The pool follows the hollow a little inside its walls, from just below the floor to the surface.
    turned(water, [(.21, .845), (.37, .865), (.47, .93), (.53, 1.02)], 8, 'liquid', 22.5)


def chalice(a, water):
    """Marble chalice on a fluted column, with gilded collars and a gilded band below the rim."""
    a.add(lathe([(.50, 0), (.50, .07), (.45, .09), (.41, .10), (.41, .16), (.36, .18)], 16), 'stone')
    column = lathe([(.15, .17), (.15, .80)], 16)
    for theta in np.arange(8)*np.pi/4 + np.pi/8:
        flute = mf.Manifold.cylinder(.50, .03, .03, 8).rotate((-90, 0, 0))
        column = column - flute.translate((np.cos(theta)*.16, .24, np.sin(theta)*.16))
    a.add(column, 'stone')
    for y in (.20, .76):
        a.add(lathe([(.17, y - .045), (.20, y - .02), (.20, y + .02), (.17, y + .045)], 16), 'gold')
    a.add(lathe([(.15, .78), (.22, .84), (.40, .93), (.53, 1.03), (.58, 1.10), (.60, 1.15),
                 (.55, 1.17), (.49, 1.17), (.47, 1.15), (.465, 1.12), (.44, 1.05), (.36, .99), (.22, .95)], 16), 'stone')
    water.add(lathe([(.23, .945), (.37, .985), (.45, 1.05), (.475, 1.12)], 16), 'liquid')
    a.add(band([(.585, 1.05), (.615, 1.07), (.62, 1.12), (.60, 1.14)], .55, 16), 'gold')


def spring(a, water):
    """Low ring of fitted blocks around a pool, fed by a spout set into an arched niche."""
    # The pool floor fills the ring's lower inner joints, so no void is sealed behind the blocks.
    a.add(lathe([(.84, 0), (.84, .22)], 16), 'stone_dark')
    water.add(lathe([(.645, .215), (.645, .44)], 16), 'liquid')
    ring(a, .86, .48, 0, .29, 10)
    ring(a, .86, .44, .27, .56, 10, 18)
    # The pillar stands in the ring diagonally behind the pool from the gameplay camera.
    yaw = 225
    theta = np.radians(yaw)
    out = np.array([np.sin(theta), 0, np.cos(theta)])
    arch = np.array([[-.11, -.16], [.11, -.16], [.11, .06], [.07, .13], [0, .155], [-.07, .13], [-.11, .06]])
    niche = mf.Manifold.extrude(mf.CrossSection([arch]), .15).translate((0, .24, -.25))
    pillar = stone((.40, 1.16, .34), .04) - niche
    a.add(pillar.rotate((0, yaw, 0)).translate(out*.92 + (0, .58, 0)), 'stone')
    a.add(stone((.50, .10, .44), .035).rotate((0, yaw, 0)).translate(out*.92 + (0, 1.19, 0)), 'stone')
    mouth = out*.62 + (0, .80, 0)
    a.add(tube(out*.84 + (0, .82, 0), mouth, .05, 8), 'brass')
    a.add(tube(out*.665 + (0, .808, 0), mouth - out*.015, .066, 8), 'brass')
    stream(water, [mouth - out*.01 + (0, -.015, 0), out*.53 + (0, .72, 0), out*.48 + (0, .58, 0), out*.46 + (0, .40, 0)], .05, .026)


def tiered(a, water):
    """Broad lower basin with a central column and an overflowing upper bowl."""
    turned(a, [(.50, 0), (.50, .14), (.40, .18), (.36, .22), (.36, .30)], 8, 'stone_dark', 22.5)
    turned(a, [(.42, .28), (.76, .32), (.90, .38), (.96, .48), (.98, .62), (1.02, .64), (1.02, .72),
               (.88, .72), (.86, .70), (.85, .66), (.80, .57), (.66, .51), (.40, .49)], 12, 'stone', 15)
    turned(water, [(.41, .485), (.67, .505), (.81, .57), (.86, .66)], 12, 'liquid', 15)
    # The column stands on the basin floor and rises through the pool.
    turned(a, [(.20, .47), (.20, .70), (.16, .74), (.15, 1.22), (.19, 1.26), (.19, 1.30)], 8, 'stone', 22.5)
    turned(a, [(.18, .96), (.21, .99), (.21, 1.03), (.18, 1.06)], 8, 'stone', 22.5)
    bowl = lathe([(.17, 1.28), (.30, 1.36), (.40, 1.46), (.44, 1.54), (.42, 1.58), (.36, 1.58),
                  (.34, 1.56), (.335, 1.52), (.30, 1.46), (.20, 1.42)], 12).rotate((0, 15, 0))
    # An overflow notch in the rim feeds the stream.
    a.add(bowl - mf.Manifold.cube((.16, .08, .10), center=True).translate((.41, 1.585, 0)), 'stone')
    turned(water, [(.21, 1.415), (.31, 1.465), (.345, 1.52)], 12, 'liquid', 15)
    # The overflow leaves the upper pool through the notch and plunges into the lower one.
    stream(water, [(.28, 1.48, 0), (.40, 1.535, 0), (.47, 1.50, 0), (.52, 1.36, 0), (.55, 1.05, 0), (.56, .80, 0),
                   (.565, .62, 0)], .055, .022)


DESIGNS = {'font': font, 'chalice': chalice, 'spring': spring, 'tiered': tiered}
KINDS = tuple(DESIGNS)


def fountain(design):
    """Build one explicit fountain design as its basin and a separate `Liquid` part; unknown kinds fail."""
    kind = design.get('kind')
    if kind not in DESIGNS:
        raise ValueError(f'Unknown fountain design {kind}')
    basin, water = Assembly(), Assembly()
    DESIGNS[kind](basin, water)
    return basin.build(), {'Liquid': water.build()}
