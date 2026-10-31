"""KayKit-style axes: thick bent hafts, chunky bands, bevelled slab bits and silhouette wear.

Follows the weapon style guide (objects/weapons/README.md). Metres, +Y along the haft,
origin at the hand grip; one-handed bits point to -X. Every part is a real solid joined
to its support, and cut faces keep the material of the cutter that made them.
"""

import numpy as np
import manifold3d as mf
from core import kit
from items.solid import Assembly, box

OCTAGON = kit.ring_profile(1, 1, 8)


def tag(a, shape, slot):
    """Label a solid with an assembly slot, so faces a boolean creates keep the cutter's material."""
    if slot not in a.slots:
        a.slots.append(slot)
    return shape.set_properties(1, lambda position, old, label=a.slots.index(slot): [label])


def section(points):
    p = np.asarray(points, float)
    if np.sum(p[:, 0]*np.roll(p[:, 1], -1) - p[:, 1]*np.roll(p[:, 0], -1)) < 0:
        p = p[::-1]
    return mf.CrossSection([p])


def prism(points, depth):
    """A silhouette extruded through Z, centered on the blade plane."""
    return mf.Manifold.extrude(section(points), depth).translate((0, 0, -depth/2))


class Haft:
    """An eight-sided haft swept through (x, y, radius) stations; the end stations chamfer butt and top."""

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


def cut(a, normal, origin, length=4., slot='steel', along=(0, 1, 0)):
    """A slot-labelled cutter filling the half-space beyond the plane through `origin` facing `normal`,
    bounded to `length` along the plane's `along` direction."""
    normal = np.asarray(normal, float)/np.linalg.norm(normal)
    e1 = np.asarray(along, float) - (np.asarray(along, float) @ normal)*normal
    e1 /= np.linalg.norm(e1)
    frame = np.column_stack([e1, np.cross(normal, e1), normal, origin])
    return tag(a, mf.Manifold.cube((length, 4, 4), center=True).translate((0, 0, 2)).transform(frame), slot)


class Bit:
    """One cutting bit: a wedge slab whose cutting-edge facets are bevelled front and back.

    `outline` is the bit's silhouette in XY, overlapping the socket. `edge` is the run of its
    corners that forms the cutting edge. The slab thins from `thickness` at the socket to
    `taper` at the edge's reach, like KayKit's wedge heads. Each straight edge facet then gets a
    front and a back plane down to the thin edge, so neighbouring facets meet in crisp mitres.
    """

    def __init__(self, a, outline, edge, inside, thickness, taper, bevel, edge_thickness=.016, slot='steel'):
        self.a, self.edge, self.inside = a, np.asarray(edge, float), np.asarray(inside, float)
        self.shape = tag(a, prism(outline, thickness), slot)
        start, reach = self.inside[0]*.4, self.edge[:, 0][np.argmax(np.abs(self.edge[:, 0]))]
        for side in (1, -1):
            normal = np.array([(taper-thickness)/2, 0, -(reach-start)])*np.sign(reach)*-1
            normal[2] = side*abs(normal[2])
            self.shape = self.shape - cut(a, normal, (start, 0, side*thickness/2), slot=slot)
        rise = (taper-edge_thickness)/2
        for p, q in zip(self.edge[:-1], self.edge[1:]):
            d, n = self._axes(p, q)
            for side in (1, -1):
                origin = np.array([*(p+q)/2, side*edge_thickness/2])
                self.shape = self.shape - cut(a, (n[0]*rise, n[1]*rise, side*bevel), origin,
                                              np.linalg.norm(q-p)+.5, 'edge', (d[0], d[1], 0))

    def _axes(self, p, q):
        d = (q-p)/np.linalg.norm(q-p)
        n = np.array([d[1], -d[0]])
        return d, (n if n @ (p-self.inside) > 0 else -n)

    def chip(self, at, depth, width, lean=0.):
        """Bite a V-notch out of the cutting edge `at` (0..1 along its length); 0 or 1 clips a corner."""
        lengths = np.r_[0, np.cumsum(np.linalg.norm(np.diff(self.edge, axis=0), axis=1))]
        u = at*lengths[-1]
        i = min(np.searchsorted(lengths, u, side='right')-1, len(self.edge)-2)
        p, q = self.edge[i], self.edge[i+1]
        d, n = self._axes(p, q)
        point = p + d*(u-lengths[i])
        notch = [point - d*width/2 + n*.06, point + d*width/2 + n*.06, point - n*depth + d*lean*width]
        self.shape = self.shape - tag(self.a, prism(notch, .6), 'edge')

    def finish(self):
        self.a.parts.append(self.shape)


def plate(a, points, half, slot='gold', chamfer=.012, runes=()):
    """A raised cheek plate through the blade, standing `half` off the center plane on both faces.

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


# One-handed haft stations (x, y, radius): a flared butt bending back from the blade side, then straight under the head.
ONE_HANDED = [(.022, -.285, .048), (.024, -.265, .066), (.020, -.20, .063), (.012, -.10, .059),
              (.004, .05, .057), (0, .30, .055), (0, .60, .053), (0, .99, .050), (0, 1.02, .038)]


def bearded():
    """Tier-three Viking axe: long hooked beard, blue cloth bands, a few chips from use."""
    a = Assembly()
    haft = Haft(a, ONE_HANDED)
    for y, height in [(-.17, .08), (.08, .085), (.60, .07)]:
        haft.band(a, y, height, 'cloth')
    a.add(box((.15, .30, .15), (0, .80, 0), .025), 'steel')
    edge = [(-.47, 1.02), (-.555, .95), (-.60, .82), (-.605, .66), (-.57, .50), (-.50, .36)]
    bit = Bit(a, [(0, .92), (-.18, .90), (-.33, .94), *edge, (-.42, .37), (-.33, .45), (-.22, .56), (-.10, .64), (0, .67)],
              edge, (-.25, .75), .11, .065, .10)
    bit.chip(.38, .045, .06, .3)
    bit.chip(.80, .035, .05, -.2)
    bit.finish()
    return a.build()


def executioner():
    """Tier-five hatchet: broad squared bit, gold socket and cheek, a gem, one small nick."""
    a = Assembly()
    haft = Haft(a, ONE_HANDED)
    haft.band(a, -.01, .30, 'leather', proud=.014)
    haft.band(a, .60, .05, 'gold')
    butt_cap(a, haft)
    a.add(box((.19, .34, .17), (0, .80, 0), .03), 'gold')
    edge = [(-.46, 1.05), (-.56, .99), (-.60, .86), (-.60, .70), (-.56, .57), (-.46, .51)]
    bit = Bit(a, [(0, .93), (-.20, .92), (-.34, .97), *edge, (-.34, .59), (-.20, .66), (0, .67)],
              edge, (-.25, .78), .11, .065, .10)
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
        made.append(Bit(a, outline, edge, (-.3*mirror, 1.15), .13, .075, .13, .018))
    return a, haft, made


CLEAVER = ([(0, 1.34), (-.36, 1.36), (-.64, 1.44), (-.70, 1.32), (-.72, 1.12), (-.70, .94), (-.64, .84),
            (-.36, .92), (0, .96)],
           [(-.64, 1.44), (-.70, 1.32), (-.72, 1.12), (-.70, .94), (-.64, .84)])
CRESCENT = ([(0, 1.32), (-.20, 1.30), (-.40, 1.42), (-.58, 1.60), (-.69, 1.48), (-.75, 1.28), (-.75, 1.02),
             (-.69, .82), (-.58, .70), (-.40, .88), (-.20, 1.00), (0, .98)],
            [(-.58, 1.60), (-.69, 1.48), (-.75, 1.28), (-.75, 1.02), (-.69, .82), (-.58, .70)])


def cleaver_double():
    """Tier-two iron greataxe: squared cleaver bits, leather straps, heavy and uneven wear."""
    a, haft, (left, right) = double(CLEAVER, ((.24, .48, .22), (0, 1.15, 0), .035), 'iron')
    for y in (-.15, -.03, .09):
        haft.band(a, y, .09, 'leather', proud=.028, tilt=18)
    haft.band(a, .86, .08, 'leather')
    left.chip(.30, .06, .075, .3)
    left.chip(.72, .045, .055, -.3)
    right.chip(0, .07, .12)
    right.chip(.55, .05, .06, .2)
    left.finish()
    right.finish()
    return a.build()


def runic_double():
    """Tier-five gilded battleaxe: flared crescent bits, rune-carved gold cheeks, gems, one nick."""
    a, haft, (left, right) = double(CRESCENT, ((.28, .52, .24), (0, 1.15, 0), .04), 'gold')
    haft.band(a, 0, .40, 'cloth', proud=.016)
    for y in (.85, .24):
        haft.band(a, y, .06, 'gold')
    butt_cap(a, haft)
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


def axe(design):
    """Build one explicit axe design; unknown kinds fail instead of falling back."""
    kind = design.get('kind')
    if kind not in KINDS:
        raise ValueError(f'Unknown axe design {kind}')
    return KINDS[kind]()
