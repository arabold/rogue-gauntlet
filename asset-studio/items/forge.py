"""Shared KayKit-style weapon construction: the weapon palette, hafts and grips, bands, bevelled blades, wear and
fittings.

Follows the weapon style guide (objects/weapons/README.md). Metres, +Y along the weapon,
origin at the hand grip, blades lying in the XY plane. Every part is a real solid joined to
its support, and faces a boolean cut creates keep the material of the cutter that made them.
"""

import numpy as np
import manifold3d as mf
from core import kit
from items.solid import polygon, winding

OCTAGON = kit.ring_profile(1, 1, 8)

METAL = {'metallic': .1, 'roughness': .55, 'crease': 45}
MATTE = {'metallic': 0, 'roughness': .9, 'crease': 50}
GLASS = {'metallic': 0, 'roughness': .3, 'crease': 40}
# Shaded slots as (light, shadow, base). KayKit's metal swatches stay grey-blue at the top, while the
# atlas's default metal ramp would wash up-facing bevels to white.
RAMPS = {'steel': ((176, 188, 198), (92, 102, 112), (150, 164, 176)),
         'edge': ((188, 200, 210), (112, 122, 132), (166, 180, 192)),
         'iron': ((146, 156, 166), (70, 78, 86), (118, 128, 138)),
         'gold': ((246, 214, 110), (176, 112, 36), (228, 176, 68)),
         'brass': ((226, 180, 104), (132, 88, 40), (200, 146, 70)),
         'silver': ((226, 232, 238), (120, 128, 140), (200, 208, 218)),
         'obsidian': ((96, 80, 116), (26, 20, 34), (58, 46, 70)),
         'obsidian_edge': ((160, 136, 196), (70, 54, 96), (128, 104, 160))}
MATTES = {'wood': (176, 98, 66), 'wood_cut': (222, 170, 120), 'birch': (214, 174, 120), 'ebony': (96, 58, 42),
          'leather': (128, 74, 50), 'wrap': (222, 206, 176), 'cloth': (236, 228, 212), 'bone': (232, 220, 192),
          'horn': (200, 166, 118), 'string': (230, 220, 200), 'fletch': (190, 60, 50)}
STONES = {'gem': (222, 42, 78), 'crystal': (70, 170, 240), 'emerald': (70, 200, 120), 'moonstone': (214, 222, 236)}
# The weapon palette every weapon recipe starts from; a recipe lists only the slots it changes.
COLORS = {**{slot: [*ramp[2]] for slot, ramp in RAMPS.items()}, **{slot: [*c] for slot, c in {**MATTES, **STONES}.items()}}
MATERIALS = {**{slot: {**(GLASS if slot.startswith('obsidian') else METAL), 'ramp': [[*c] for c in ramp]}
                for slot, ramp in RAMPS.items()},
             **{slot: dict(MATTE) for slot in MATTES}, **{slot: dict(GLASS) for slot in STONES}}


def dispatch(kinds, family):
    """A family's builder: `design['kind']` picks one of `kinds`; unknown kinds fail instead of falling back."""
    def build(design):
        kind = design.get('kind')
        if kind not in kinds:
            raise ValueError(f'Unknown {family} design {kind}')
        return kinds[kind]()
    build.__name__ = family
    return build


def collar(center, axis, radius, height, slot, chamfer=None):
    """A chunky eight-sided band or barrel centered on `center` along `axis`, with chamfered ends."""
    c = min(radius*.25, height*.2) if chamfer is None else chamfer
    center, axis = np.asarray(center, float), np.asarray(axis, float)/np.linalg.norm(axis)
    path = [center + axis*t for t in (-height/2, -height/2+c, height/2-c, height/2)]
    up = (0, 0, 1) if abs(axis[2]) < .9 else (1, 0, 0)
    return kit.sweep(path, OCTAGON, slot, up=up, scale=[radius-c, radius, radius, radius-c])


def pyramid(base, direction, size, height, slot):
    """A chunky four-sided pyramid rising `height` from `base` along `direction`; its square base block is buried
    in the support. For maul studs, morning-star spikes and tassel tufts."""
    base, d = np.asarray(base, float), np.asarray(direction, float)/np.linalg.norm(direction)
    ref = np.array([0., 1., 0.]) if abs(d[1]) < .9 else np.array([1., 0., 0.])
    u = np.cross(d, ref); u /= np.linalg.norm(u)
    v = np.cross(d, u)
    corners = [base - d*size*.5 + (u*i + v*j)*size/2 for i, j in ((1, 1), (1, -1), (-1, -1), (-1, 1))]
    return kit.hull([*corners, *(c + d*size*.5 for c in corners), base + d*height], slot)


def tube(points, radii, slot, sides=6):
    """A tapering chunky tube along `points`: hafts, prongs, claws, ribs and crescents."""
    return kit.sweep(points, kit.ring_profile(1, 1, sides), slot, up=(0, 0, 1), scale=np.asarray(radii, float))


def loft(rings, slot):
    """Cap a continuous ring loft, retaining explicit cross-section bevels."""
    v, f = kit._loft(rings)
    n = len(rings[0])
    v, f = kit._cap_loops(v, f, [list(range(n)), list(range(len(v)-n, len(v)))])
    return kit.Piece(v, kit._orient_closed(v, f), slot)


def cut(a, normal, origin, length=4., slot='steel', along=(0, 1, 0)):
    """A slot-labelled cutter filling the half-space beyond the plane through `origin` facing `normal`,
    bounded to `length` along the plane's `along` direction."""
    normal = np.asarray(normal, float)/np.linalg.norm(normal)
    e1 = np.asarray(along, float) - (np.asarray(along, float) @ normal)*normal
    e1 /= np.linalg.norm(e1)
    frame = np.column_stack([e1, np.cross(normal, e1), normal, origin])
    return a.label(mf.Manifold.cube((length, 4, 4), center=True).translate((0, 0, 2)).transform(frame), slot)


class Haft:
    """An eight-sided haft or grip swept through (x, y, radius) stations; end stations chamfer its ends."""

    def __init__(self, a, stations, slot='wood', add=True):
        """`add=False` keeps the swept `piece` out of the assembly, for bodies that are carved first."""
        self.a, self.s = a, np.asarray(stations, float)
        self.piece = tube(np.column_stack([self.s[:, 0], self.s[:, 1], np.zeros(len(self.s))]), self.s[:, 2], slot, 8)
        if add:
            a.add(self.piece)

    def at(self, y):
        return np.interp(y, self.s[:, 1], self.s[:, 0]), np.interp(y, self.s[:, 1], self.s[:, 2])

    def band(self, y, height, slot, proud=.022, tilt=0.):
        """A chunky wrap standing proud of the haft; `tilt` (degrees) winds it aslant like a strap."""
        x, r = self.at(y)
        self.a.add(collar((x, y, 0), kit.rotation([0, 0, 1], tilt) @ (0, 1, 0), r + proud, height, slot,
                          min(proud*.5, height*.2)))


class Blade:
    """A flat slab blade whose cutting-edge facets are bevelled front and back.

    `outline` is the silhouette in XY, overlapping whatever holds it. `edge` is the run of its
    corners, in outline order, that forms the cutting edge; other sides stay square. Each straight
    edge facet gets a front and a back plane down to the thin edge, so neighbouring facets meet in
    crisp mitres and a point gets a ridge. `taper` = (thin, start_x) first thins the slab along X
    from `thickness` at `start_x` to `thin` at the edge's reach, like KayKit's wedge axe heads.
    Carve it with `chip`, then `finish` joins it to the assembly.
    """

    def __init__(self, a, outline, edge, thickness, bevel, edge_thickness=.016, slot='steel', edge_slot='edge',
                 taper=None):
        self.a, self.edge, self.edge_slot = a, np.asarray(edge, float), edge_slot
        self.winding = winding(outline)
        self.shape = a.label(polygon(outline, thickness), slot)
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
        self.shape = self.shape - self.a.label(polygon(notch, .6), self.edge_slot)

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
    shape = a.label(mf.Manifold.hull_points(np.vstack([np.column_stack([q, np.full(len(q), z)]) for q, z in layers])), slot)
    for start, end in runes:
        start, end = np.asarray(start, float), np.asarray(end, float)
        length = np.linalg.norm(end-start)
        angle = np.degrees(np.arctan2(*(end-start)[::-1]))
        for side in (1, -1):
            groove = mf.Manifold.cube((length+.024, .024, .022), center=True).rotate((0, 0, angle))
            shape = shape - a.label(groove.translate((*(start+end)/2, side*half)), f'{slot}_dark')
    a.parts.append(shape)


def disc(a, center, radius, half, slot, chamfer=.014):
    """A chunky eight-faceted coin facing the viewer, for pommels and guard bosses."""
    t = 2*np.pi*(np.arange(8)+.5)/8
    plate(a, center + np.column_stack([np.cos(t), np.sin(t)])*radius, half, slot, chamfer)


def ring(a, center, outer, inner, half, slot):
    """A chunky eight-faceted ring facing the viewer, for ring pommels."""
    t = 2*np.pi*(np.arange(8)+.5)/8
    loop = np.column_stack([np.cos(t), np.sin(t)])
    shape = mf.Manifold.extrude(mf.CrossSection([center + loop*outer, (center + loop*inner)[::-1]]), 2*half)
    a.add(shape.translate((0, 0, -half)), slot)


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
    a.add(tube([(x, y-.012, 0), (x, y, 0), (x, y+.07, 0), (x, y+.08, 0)], [r*.82, r+.014, r+.014, r*.9], slot, 8))
    g = kit.gem(.03, gem_slot, 'diamond')
    g.verts = (g.verts + (0, 0, -.004)) @ kit.rotation([1, 0, 0], 90).T + (x, y-.012, 0)
    a.add(g)


def branch(points, radii, slot='wood', sides=8, seed=0):
    """A gnarled limb swept along a crooked path: the radius tapers per point and wobbles
    slightly around the ring, so the bark reads as grown wood rather than a turned dowel."""
    points = np.asarray(points, float)
    rng = np.random.default_rng(seed)
    t = 2*np.pi*np.arange(sides)/sides
    profile = np.column_stack([np.cos(t), np.sin(t)]) * (1 + .09*rng.standard_normal(sides))[:, None]
    return kit.sweep(points, profile, slot, scale=np.asarray(radii, float))


def spiral(center, start, turns, r0, r1, rise, steps=28):
    """Points of a clockwise spiral in XY that shrinks from `r0` to `r1` and drifts along Z, for horns and crooks."""
    u = np.linspace(0, 1, steps)
    phi = start - turns*2*np.pi*u
    r = r0 + (r1-r0)*u
    return np.column_stack([center[0] + r*np.cos(phi), center[1] + r*np.sin(phi), center[2] + rise*u])


def crystal(center, radius, height, slot):
    """A faceted crystal: a six-cornered girdle between a tall upper point and a short lower one."""
    t = 2*np.pi*np.arange(6)/6
    ring = np.column_stack([np.cos(t), np.zeros(6), np.sin(t)])
    c = np.asarray(center, float)
    return kit.hull(np.vstack([c + ring*radius, c + ring*radius*.6 + (0, height*.3, 0),
                               [c + (0, height*.6, 0), c - (0, height*.4, 0)]]), slot)
