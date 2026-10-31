"""Shared KayKit-style weapon construction: hafts and grips, bands, bevelled blades, wear and fittings.

Follows the weapon style guide (objects/weapons/README.md). Metres, +Y along the weapon,
origin at the hand grip, blades lying in the XY plane. Every part is a real solid joined to
its support, and faces a boolean cut creates keep the material of the cutter that made them.
"""

import numpy as np
import manifold3d as mf
from core import kit

OCTAGON = kit.ring_profile(1, 1, 8)


def tag(a, shape, slot):
    """Label a solid with an assembly slot, so faces a boolean creates keep the cutter's material."""
    if slot not in a.slots:
        a.slots.append(slot)
    return shape.set_properties(1, lambda position, old, label=a.slots.index(slot): [label])


def winding(points):
    p = np.asarray(points, float)
    return np.sign(np.sum(p[:, 0]*np.roll(p[:, 1], -1) - p[:, 1]*np.roll(p[:, 0], -1)))


def section(points):
    p = np.asarray(points, float)
    return mf.CrossSection([p[::-1] if winding(p) < 0 else p])


def prism(points, depth):
    """A silhouette extruded through Z, centered on the blade plane."""
    return mf.Manifold.extrude(section(points), depth).translate((0, 0, -depth/2))


def cut(a, normal, origin, length=4., slot='steel', along=(0, 1, 0)):
    """A slot-labelled cutter filling the half-space beyond the plane through `origin` facing `normal`,
    bounded to `length` along the plane's `along` direction."""
    normal = np.asarray(normal, float)/np.linalg.norm(normal)
    e1 = np.asarray(along, float) - (np.asarray(along, float) @ normal)*normal
    e1 /= np.linalg.norm(e1)
    frame = np.column_stack([e1, np.cross(normal, e1), normal, origin])
    return tag(a, mf.Manifold.cube((length, 4, 4), center=True).translate((0, 0, 2)).transform(frame), slot)


class Haft:
    """An eight-sided haft or grip swept through (x, y, radius) stations; end stations chamfer its ends."""

    def __init__(self, a, stations, slot='wood'):
        self.s = np.asarray(stations, float)
        path = np.column_stack([self.s[:, 0], self.s[:, 1], np.zeros(len(self.s))])
        a.add(kit.sweep(path, OCTAGON, slot, up=(0, 0, 1), scale=self.s[:, 2]))

    def at(self, y):
        return np.interp(y, self.s[:, 1], self.s[:, 0]), np.interp(y, self.s[:, 1], self.s[:, 2])

    def band(self, a, y, height, slot, proud=.022, tilt=0.):
        """A chunky wrap standing proud of the haft; `tilt` (degrees) winds it aslant like a strap."""
        x, r = self.at(y)
        r += proud
        c = min(proud*.5, height*.2)
        piece = kit.sweep([(0, -height/2, 0), (0, -height/2+c, 0), (0, height/2-c, 0), (0, height/2, 0)],
                          OCTAGON, slot, up=(0, 0, 1), scale=[r-c, r, r, r-c])
        piece.verts = piece.verts @ kit.rotation([0, 0, 1], tilt).T + (x, y, 0)
        a.add(piece)


class Blade:
    """A flat slab blade whose cutting-edge facets are bevelled front and back.

    `outline` is the silhouette in XY, overlapping whatever holds it. `edge` is the run of its
    corners, in outline order, that forms the cutting edge; other sides stay square. Each straight
    edge facet gets a front and a back plane down to the thin edge, so neighbouring facets meet in
    crisp mitres and a point gets a ridge. `taper` = (thin, start_x) first thins the slab along X
    from `thickness` at `start_x` to `thin` at the edge's reach, like KayKit's wedge axe heads.
    """

    def __init__(self, a, outline, edge, thickness, bevel, edge_thickness=.016, slot='steel', edge_slot='edge',
                 taper=None):
        self.a, self.edge, self.edge_slot = a, np.asarray(edge, float), edge_slot
        self.winding = winding(outline)
        self.shape = tag(a, prism(outline, thickness), slot)
        face = thickness
        if taper:
            face, start = taper
            reach = self.edge[:, 0][np.argmax(np.abs(self.edge[:, 0]))]
            for side in (1, -1):
                normal = np.array([(face-thickness)/2, 0, -(reach-start)])*np.sign(reach)*-1
                normal[2] = side*abs(normal[2])
                self.shape = self.shape - cut(a, normal, (start, 0, side*thickness/2), slot=slot)
        rise = (face-edge_thickness)/2
        for p, q in zip(self.edge[:-1], self.edge[1:]):
            d, n = self._axes(p, q)
            for side in (1, -1):
                origin = np.array([*(p+q)/2, side*edge_thickness/2])
                self.shape = self.shape - cut(a, (n[0]*rise, n[1]*rise, side*bevel), origin,
                                              np.linalg.norm(q-p)+.5, edge_slot, (d[0], d[1], 0))

    def _axes(self, p, q):
        """The facet's direction and its outward normal, from the outline's winding."""
        d = (q-p)/np.linalg.norm(q-p)
        return d, np.array([d[1], -d[0]])*self.winding

    def chip(self, at, depth, width, lean=0.):
        """Bite a V-notch out of the cutting edge `at` (0..1 along its length); 0 or 1 clips a corner."""
        lengths = np.r_[0, np.cumsum(np.linalg.norm(np.diff(self.edge, axis=0), axis=1))]
        u = at*lengths[-1]
        i = min(np.searchsorted(lengths, u, side='right')-1, len(self.edge)-2)
        p, q = self.edge[i], self.edge[i+1]
        d, n = self._axes(p, q)
        point = p + d*(u-lengths[i])
        notch = [point - d*width/2 + n*.06, point + d*width/2 + n*.06, point - n*depth + d*lean*width]
        self.shape = self.shape - tag(self.a, prism(notch, .6), self.edge_slot)

    def finish(self):
        self.a.parts.append(self.shape)


def plate(a, points, half, slot='gold', chamfer=.012, runes=()):
    """A chunky chamfered block from a convex XY outline, `half` deep on both sides of the blade plane:
    cheek plates, guards and their wings.

    `runes` are (start, end) XY strokes carved into both faces as chunky grooves.
    """
    p = np.asarray(points, float)
    c = p.mean(0)
    inner = c + (p-c)*(1 - chamfer/np.linalg.norm(p-c, axis=1).min())
    layers = [(p, half-chamfer), (inner, half), (p, -(half-chamfer)), (inner, -half)]
    shape = tag(a, mf.Manifold.hull_points(np.vstack([np.column_stack([q, np.full(len(q), z)]) for q, z in layers])), slot)
    for start, end in runes:
        start, end = np.asarray(start, float), np.asarray(end, float)
        length = np.linalg.norm(end-start)
        angle = np.degrees(np.arctan2(*(end-start)[::-1]))
        for side in (1, -1):
            groove = mf.Manifold.cube((length+.024, .024, .022), center=True).rotate((0, 0, angle))
            shape = shape - tag(a, groove.translate((*(start+end)/2, side*half)), f'{slot}_dark')
    a.parts.append(shape)


def disc(a, center, radius, half, slot, chamfer=.014, sides=8):
    """A chunky faceted coin facing the viewer, for pommels and guard bosses."""
    t = 2*np.pi*(np.arange(sides)+.5)/sides
    ring = np.column_stack([np.cos(t), np.sin(t)])
    plate(a, center + ring*radius, half, slot, chamfer)


def ring(a, center, outer, inner, half, slot, sides=8):
    """A chunky faceted ring facing the viewer, for ring pommels."""
    t = 2*np.pi*(np.arange(sides)+.5)/sides
    loop = np.column_stack([np.cos(t), np.sin(t)])
    shape = mf.Manifold.extrude(mf.CrossSection([center + loop*outer, (center + loop*inner)[::-1]]), 2*half)
    a.parts.append(tag(a, shape.translate((0, 0, -half)), slot))


def gem(a, center, size, face, slot='gem'):
    """A diamond-cut gem set into both faces of a part whose faces lie at Z = +-`face`."""
    for turn in (0, 180):
        g = kit.gem(size, slot, 'diamond')
        g.verts = (g.verts + (0, 0, face-.012)) @ kit.rotation([0, 1, 0], turn).T + (center[0], center[1], 0)
        a.add(g)


def butt_cap(a, haft, slot='gold', gem_slot='gem'):
    """A metal cap over the haft's butt with a gem in its end."""
    y = haft.s[0, 1]
    x, r = haft.at(y+.04)
    a.add(kit.sweep([(x, y-.012, 0), (x, y, 0), (x, y+.07, 0), (x, y+.08, 0)], OCTAGON, slot, up=(0, 0, 1),
                    scale=[r*.82, r+.014, r+.014, r*.9]))
    g = kit.gem(.03, gem_slot, 'diamond')
    g.verts = (g.verts + (0, 0, -.004)) @ kit.rotation([1, 0, 0], 90).T + (x, y-.012, 0)
    a.add(g)
