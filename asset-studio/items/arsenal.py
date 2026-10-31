"""Distinct weapon silhouettes with continuous bevels and physically joined fittings.

All dimensions use metres, +Y follows the weapon, and the origin is the hand grip.
Color regions describe materials, not baked lighting or decorative mesh shells.
"""

import numpy as np
import manifold3d as mf
from core import kit
from items.solid import Assembly, lathe, tube, polygon, box
from prefabs import library

WANDS = ('crooked_wand', 'iron_wand', 'bone_wand', 'carved_wand')
STAVES = ('crooked_staff', 'iron_staff', 'bone_staff', 'horned_staff')
# Swords and daggers moved to items.blades; the executioner blade stays for the Forsaken Warden boss.
KINDS = ('wooden_club', 'steel_club', 'morning_star', 'executioner', 'scythe', 'halberd', 'spear') + WANDS + STAVES
COLORS = {'steel': [134, 153, 166], 'edge': [181, 194, 203], 'iron': [65, 76, 83],
          'wood': [119, 78, 46], 'endgrain': [148, 104, 62], 'leather': [79, 47, 32],
          'brass': [165, 119, 48], 'bark': [92, 62, 40], 'ebony': [96, 58, 42],
          'silver': [196, 204, 214], 'bone': [228, 216, 186], 'horn': [170, 140, 104],
          'moonstone': [214, 222, 236]}
MATERIALS = {name: {'metallic': .55, 'roughness': .36, 'crease': 48}
             for name in ('steel', 'edge', 'iron', 'brass', 'silver')}
MATERIALS.update({name: {'metallic': 0, 'roughness': .9, 'crease': 55}
                  for name in ('wood', 'endgrain', 'leather', 'bark', 'ebony', 'bone', 'horn')})
MATERIALS['moonstone'] = {'metallic': 0, 'roughness': .3, 'crease': 40}


def loft(rings, slot):
    """Cap a continuous ring loft, retaining explicit cross-section bevels."""
    v, f = kit._loft(rings)
    n = len(rings[0])
    v, f = kit._cap_loops(v, f, [list(range(n)), list(range(len(v)-n, len(v)))])
    return kit.Piece(v, kit._orient_closed(v, f), slot)


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


def shaft(a, lo, hi, radius=.041):
    a.add(lathe([(radius*.92, lo), (radius, lo+.06), (radius, hi-.08), (radius*.88, hi)], 12), 'wood')
    collar(a, lo+.035, radius*1.3, .06)
    collar(a, hi-.07, radius*1.45, .10)
    # Two separated hand positions remain continuous parts of the real shaft.
    for y in [0., .48]:
        grip(a, y-.12, y+.12, radius*1.12)


def spike(start, end, radius):
    """Tapered forged spike aligned to a direction, with its base buried in its head."""
    start, end = np.asarray(start), np.asarray(end)
    direction = end-start
    length = np.linalg.norm(direction)
    direction /= length
    ref = np.array([0., 0., 1.]) if abs(direction[2]) < .9 else np.array([1., 0., 0.])
    u = np.cross(direction, ref); u /= np.linalg.norm(u)
    v = np.cross(direction, u)
    theta = np.arange(6)*np.pi/3
    profile = np.cos(theta)[:, None]*u + np.sin(theta)[:, None]*v
    return loft([start+profile*radius, start+direction*length*.24+profile*radius*.86,
                 end+profile*.0015], 'steel')


def club(kind):
    a = Assembly()
    grip(a, -.20, .13, .047)
    pommel(a, -.225, .061, 'iron')
    if kind == 'wooden_club':
        angles = np.arange(12)*np.pi/6
        stations = [(.015, .10, .049), (.015, .24, .067), (.007, .45, .105),
                    (-.006, .68, .143), (.006, .89, .154), (.025, 1.02, .132), (.029, 1.06, .100)]
        rings=[]
        for x,y,r in stations:
            variation=1+.06*np.sin(angles*3+.8)+.035*np.cos(angles*5)
            rings.append(np.column_stack([x+np.cos(angles)*r*variation, np.full(12,y), np.sin(angles)*r*variation*.92]))
        body=loft(rings,'wood')
        # Real chips in the striking end, not dark paint or attached lumps.
        wood = Assembly().add(body,'wood').build()
        v,f = wood.verts,wood.faces
        shape=mf.Manifold(mf.Mesh(np.asarray(v,np.float32),np.asarray(f,np.uint32)))
        shape=shape-mf.Manifold.cube((.11,.11,.14),center=True).rotate((0,15,28)).translate((.127,1.04,.03))
        a.add(shape,'wood')
        collar(a, .22, .075, .085, 'iron')
    elif kind == 'steel_club':
        a.add(lathe([(.044,.10),(.047,.32),(.075,.39),(.106,.43),(.125,.58),
                     (.125,.94),(.11,1.03),(.075,1.06)],12),'steel')
        collar(a,.40,.102,.08)
        collar(a,.99,.122,.08)
        # Six longitudinal forged ribs are fused into the striking barrel.
        for theta in np.arange(6)*np.pi/3:
            x,z=np.cos(theta)*.105,np.sin(theta)*.105
            rib=box((.045,.46,.055),(0,0,0),.013)
            rib.verts= rib.verts @ np.array([[np.cos(theta),0,np.sin(theta)],[0,1,0],[-np.sin(theta),0,np.cos(theta)]])
            rib.verts += [x,.73,z]
            a.add(rib,'iron')
    else:
        a.add(lathe([(.043,.10),(.048,.56),(.069,.62),(.06,.70)],12),'steel')
        center=np.array([0.,.83,0.])
        a.add(mf.Manifold.sphere(.173,16).scale((1,1.12,1)).translate(center),'iron')
        collar(a,.66,.085,.08)
        directions=[np.array([np.cos(t), .22 if row else -.25,np.sin(t)])
                    for row in [0,1] for t in np.arange(5)*2*np.pi/5+row*np.pi/5]
        directions.append(np.array([0.,1.,0.]))
        for d in directions:
            d=d/np.linalg.norm(d)
            a.add(spike(center+d*.13,center+d*.31,.047),'steel')
    return a.build()


def sword(kind):
    a=Assembly()
    grip(a,-.36,.22,.075);pommel(a,-.43,.11,'brass');guard(a,.47,.28,.025,'brass')
    a.add(blade([(0,.25,.21,.065),(0,.44,.30,.070),(0,2.38,.30,.060),(0,2.50,.23,.045)]))
    return a.build()


def polearm(kind):
    a=Assembly()
    shaft(a,-.62,1.43,.042 if kind!='halberd' else .048)
    if kind=='spear':
        collar(a,1.39,.073,.15)
        a.add(blade([(0,1.40,.044,.027),(0,1.52,.125,.038),(0,1.70,.133,.036),
                     (0,1.94,.072,.023),(0,2.13,.002,.002)]))
        a.add(spike((0,-.57,0),(0,-.80,0),.045),'iron')
    elif kind=='halberd':
        # Long central spike, socketed axe and rear beak, all overlap the head.
        a.add(lathe([(.058,1.22),(.073,1.31),(.068,1.61),(.040,1.70)],12),'iron')
        a.add(blade([(0,1.54,.054,.030),(0,1.79,.064,.030),(0,2.08,.002,.002)]))
        axe=blade([(0,1.27,.055,.032),(.19,1.32,.22,.038),(.27,1.49,.28,.036),
                   (.20,1.69,.22,.032),(0,1.76,.06,.025)])
        a.add(axe)
        a.add(polygon([(-.04,1.40),(-.31,1.39),(-.45,1.28),(-.35,1.57),(-.04,1.62)],.075),'steel')
        for y in [1.34,1.64]:collar(a,y,.085,.070,'brass')
        for z in [-.05,.05]:
            a.add(mf.Manifold.sphere(.037,12).scale((1,1,.6)).translate((0,1.51,z)),'brass')
    else:
        # Angular tang and curved crescent leave the shaft through a real socket.
        a.add(box((.15,.27,.13),(0,1.42,0),.022),'iron')
        scythe=blade([(0,0,.075,.040),(.08,.18,.11,.038),(.09,.43,.13,.033),
                      (0,.69,.13,.028),(-.17,.92,.10,.021),(-.39,1.10,.0018,.0018)])
        # Loft along the crescent's span, then rotate the whole solid onto its socket.
        x, y = scythe.verts[:, 0].copy(), scythe.verts[:, 1].copy()
        scythe.verts[:, 0], scythe.verts[:, 1] = -y, x+1.40
        a.add(scythe)
        a.add(tube((.005,.34,0),(.24,.30,0),.034,10),'wood')
        a.add(tube((.005,.87,0),(.17,.83,.025),.033,10),'wood')
        collar(a,1.34,.086,.085,'brass')
    return a.build()


def ellipsoid(center, radii, segments=10):
    """A solid ellipsoid for knots, knuckles and finials; it must overlap the part it grows from."""
    return mf.Manifold.sphere(1, segments).scale(radii).translate(center)


def branch(points, radii, sides=8, seed=0):
    """A gnarled limb swept along a crooked path: the radius tapers per point and wobbles
    slightly around the ring, so the bark reads as grown wood rather than a turned dowel."""
    points = np.asarray(points, float)
    rng = np.random.default_rng(seed)
    t = 2*np.pi*np.arange(sides)/sides
    profile = np.column_stack([np.cos(t), np.sin(t)]) * (1 + .09*rng.standard_normal(sides))[:, None]
    return kit.sweep(points, profile, 'bark', scale=np.asarray(radii, float))


def spiral(center, start, turns, r0, r1, rise, steps=28):
    """Points of a clockwise spiral in XY that shrinks from `r0` to `r1` and drifts along Z, for horns and crooks."""
    u = np.linspace(0, 1, steps)
    phi = start - turns*2*np.pi*u
    r = r0 + (r1-r0)*u
    return np.column_stack([center[0] + r*np.cos(phi), center[1] + r*np.sin(phi), center[2] + rise*u])


def wraps(a, lo, hi, radius, bands=4, slot='leather'):
    """Raised binding bands around a straight section: real geometry, not painted stripes."""
    for y in np.linspace(lo, hi, bands):
        a.add(lathe([(radius*.9, y-.016), (radius*1.08, y-.008), (radius*1.08, y+.008), (radius*.9, y+.016)], 8), slot)


def twisted_bar(lo, hi, half, turns, steps=24):
    """A square forged bar twisted along Y, lofted from rotated square sections."""
    rings = []
    for i in range(steps):
        u = i/(steps-1)
        a = turns*2*np.pi*u
        square = np.array([[1, 0], [0, 1], [-1, 0], [0, -1]], float)*half*1.35
        c, s = np.cos(a), np.sin(a)
        rot = square @ np.array([[c, -s], [s, c]])
        rings.append(np.column_stack([rot[:, 0], np.full(4, lo+(hi-lo)*u), rot[:, 1]]))
    return loft(rings, 'iron')


def long_bone(a, lo, hi, radius, knuckle, axis=(1, 0, 0)):
    """A bone with a waisted shaft and two-lobed knuckles at both ends."""
    a.add(lathe([(radius*1.1, lo+knuckle*.6), (radius, lo+knuckle*1.4), (radius*.86, (lo+hi)/2),
                 (radius, hi-knuckle*1.4), (radius*1.1, hi-knuckle*.6)], 10), 'bone')
    ax = np.asarray(axis, float)
    for y in (lo+knuckle*.7, hi-knuckle*.7):
        for side in (-1, 1):
            a.add(ellipsoid(ax*side*knuckle*.55 + (0, y, 0), (knuckle*.75, knuckle*.7, knuckle*.75), 7), 'bone')


def vertebra(a, y, radius):
    """A vertebra: a thick disc with a rim and two side processes, threaded on the shaft."""
    a.add(lathe([(radius*.55, y-.035), (radius, y-.022), (radius*1.05, y), (radius, y+.022), (radius*.55, y+.035)], 8), 'bone')
    for side in (-1, 1):
        a.add(tube((0, y, 0), (side*radius*1.75, y+.02, -.01), radius*.22, 8), 'bone')
        a.add(ellipsoid((side*radius*1.75, y+.02, -.01), (radius*.3, radius*.26, radius*.26), 6), 'bone')


def wand(kind):
    """One-handed spell focus about one unit long; the origin is the grip, the tip points up +Y."""
    a = Assembly()
    if kind == 'crooked_wand':
        path = [(0, -.25, 0), (.01, -.08, 0), (-.012, .10, .006), (.035, .27, -.012), (-.025, .43, .02),
                (.045, .58, -.012), (-.004, .72, .02), (.05, .86, 0)]
        a.add(branch(path, [.05, .049, .046, .041, .035, .029, .022, .010], seed=3))
        # Gnarls at the bends, a root knob at the butt and a split twig near the tip.
        for p, r in [((.03, .27, -.012), .034), ((-.02, .43, .02), .03), ((.04, .58, -.012), .026)]:
            a.add(ellipsoid(p, (r, r*1.25, r)), 'bark')
        a.add(ellipsoid((0, -.25, 0), (.062, .05, .058)), 'bark')
        a.add(branch([(.0, .68, .015), (-.04, .76, .03), (-.075, .82, .04)], [.016, .011, .005], 6, seed=5))
        wraps(a, -.17, .05, .049, 4, 'leather')
    elif kind == 'iron_wand':
        a.add(lathe([(.05, -.24), (.054, -.20), (.054, .08), (.05, .12)], 10), 'leather')
        wraps(a, -.19, .06, .054, 5, 'leather')
        a.add(ellipsoid((0, -.28, 0), (.07, .06, .07), 10), 'steel')
        collar(a, .13, .074, .06, 'steel')
        a.add(twisted_bar(.10, .64, .032, 1.75))
        collar(a, .66, .068, .06, 'steel')
        # Three forged claws close over a pale stone.
        a.add(mf.Manifold.sphere(.062, 14).translate((0, .78, 0)), 'moonstone')
        for theta in np.arange(3)*2*np.pi/3:
            d = np.array([np.cos(theta), 0, np.sin(theta)])
            claw = [(0, .66, 0), tuple(d*.06 + (0, .71, 0)), tuple(d*.075 + (0, .79, 0)), tuple(d*.03 + (0, .86, 0))]
            a.add(kit.sweep(claw, kit.ring_profile(1, 1, 6), 'iron', scale=[.02, .018, .015, .006]))
    elif kind == 'bone_wand':
        long_bone(a, -.27, .62, .044, .06)
        wraps(a, -.16, .06, .048, 4, 'leather')
        # A small skull sits on the upper knuckle, facing forward.
        a.add(library.load('skull_cranium', 'bone', scale=.19, offset=(0, .66, .01)))
    elif kind == 'carved_wand':
        # A turned handle: knob pommel, swelling carved grip and beads, then a long taper to a rounded tip.
        profile = [(.020, -.33), (.050, -.32), (.066, -.29), (.058, -.26), (.044, -.245), (.054, -.22)]
        for y, r in [(-.17, .060), (-.10, .066), (-.03, .062), (.03, .056)]:
            profile.extend([(r, float(y-.022)), (r*.86, float(y)), (r, float(y+.022))])
        profile.extend([(.050, .07), (.040, .10), (.058, .125), (.040, .15), (.036, .30), (.028, .55),
                        (.020, .80), (.022, .86), (.014, .90), (.004, .915)])
        a.add(lathe(profile, 12), 'ebony')
        for y, r in [(-.245, .052), (.125, .062)]:
            collar(a, y, r, .03, 'silver')
    else:
        raise ValueError(f'Unknown wand design {kind}')
    return a.build()


def staff(kind):
    """Two-handed spell focus about two units long; the origin is the upper grip, the head points up +Y."""
    a = Assembly()
    if kind == 'crooked_staff':
        path = [(0, -.86, 0), (.012, -.55, .01), (-.015, -.25, -.008), (.01, .05, .012), (-.02, .38, -.01),
                (.025, .70, .015), (-.01, .98, .0), (.03, 1.14, .01)]
        a.add(branch(path, [.058, .06, .058, .057, .055, .053, .051, .05], seed=7))
        # The top curls back over itself into a crook, thinning to a twig.
        crook = spiral((.17, 1.22, .01), np.pi, .78, .14, .075, .0, 18)
        crook = np.vstack([[(.03, 1.14, .01)], crook[1:]])
        a.add(branch(crook, np.linspace(.05, .018, len(crook)), seed=8))
        for p, r in [((.012, -.55, .01), .044), ((-.02, .38, -.01), .046), ((.025, .70, .015), .044),
                     ((.03, 1.14, .01), .05)]:
            a.add(ellipsoid(p, (r, r*1.3, r)), 'bark')
        wraps(a, -.12, .12, .05, 4, 'leather')
    elif kind == 'iron_staff':
        a.add(lathe([(.04, -.80), (.043, -.70), (.043, 1.02), (.04, 1.06)], 8), 'iron')
        a.add(lathe([(.001, -.95), (.05, -.80), (.055, -.76)], 6), 'steel')
        for y in (-.66, .58):
            collar(a, y, .06, .07, 'steel')
        wraps(a, -.12, .12, .046, 5, 'leather')
        # An open cage of four forged prongs around a pale stone.
        collar(a, 1.06, .085, .08, 'steel')
        a.add(tube((0, 1.06, 0), (0, 1.21, 0), .024, 8), 'iron')
        a.add(mf.Manifold.sphere(.115, 16).translate((0, 1.29, 0)), 'moonstone')
        for theta in np.arange(4)*np.pi/2 + np.pi/4:
            d = np.array([np.cos(theta), 0, np.sin(theta)])
            prong = [(0, 1.08, 0), tuple(d*.14 + (0, 1.14, 0)), tuple(d*.19 + (0, 1.29, 0)),
                     tuple(d*.13 + (0, 1.45, 0)), (0, 1.51, 0)]
            a.add(kit.sweep(prong, kit.ring_profile(.022, .022, 6), 'iron'), 'iron')
        a.add(lathe([(.035, 1.48), (.042, 1.52), (.0015, 1.65)], 6), 'steel')
    elif kind == 'bone_staff':
        # Long bones joined knuckle to knuckle, lashed at every joint.
        joints = [-.86, -.30, .26, .82]
        for lo, hi in zip(joints, joints[1:]):
            long_bone(a, lo-.03, hi+.03, .044, .066, axis=(1, 0, 0) if lo < 0 else (0, 0, 1))
        for y in joints[1:-1]:
            wraps(a, y-.03, y+.03, .062, 2, 'leather')
        # A short spine of vertebrae, then three rib bones curling in to cradle the top.
        for i, y in enumerate([.90, 1.0, 1.10]):
            vertebra(a, y, .066 - .006*i)
        a.add(tube((0, .80, 0), (0, 1.24, 0), .03, 8), 'bone')
        a.add(mf.Manifold.sphere(.1, 10).translate((0, 1.31, 0)), 'moonstone')
        for theta in np.arange(3)*2*np.pi/3 + np.pi/2:
            d = np.array([np.cos(theta), 0, np.sin(theta)])
            rib = [(0, 1.12, 0), tuple(d*.12 + (0, 1.17, 0)), tuple(d*.19 + (0, 1.31, 0)),
                   tuple(d*.14 + (0, 1.46, 0)), tuple(d*.05 + (0, 1.51, 0))]
            a.add(kit.sweep(rib, kit.ring_profile(1, 1, 6), 'bone', scale=[.034, .031, .027, .02, .008]))
            a.add(ellipsoid(tuple(d*.12 + (0, 1.17, 0)), (.04, .04, .04), 8), 'bone')
    elif kind == 'horned_staff':
        a.add(lathe([(.038, -.86), (.044, -.80), (.044, 1.04), (.05, 1.08)], 10), 'wood')
        a.add(lathe([(.001, -.93), (.046, -.86), (.05, -.82)], 8), 'brass')
        for y in (-.70, .62):
            collar(a, y, .058, .06, 'brass')
        wraps(a, -.12, .12, .046, 4, 'leather')
        # A carved head block sprouts two ridged ram horns that curl outward and down.
        a.add(lathe([(.05, 1.06), (.095, 1.12), (.105, 1.24), (.08, 1.31), (.045, 1.35)], 10), 'wood')
        collar(a, 1.10, .10, .05, 'brass')
        horn = spiral((.21, 1.26, 0), np.pi, 1.0, .17, .07, .1, 34)
        horn[0] = (.05, 1.26, 0)
        u = np.linspace(0, 1, len(horn))
        radius = (.082 - .07*u) * (1 + .07*np.sin(u*46))
        for mirror in (1, -1):
            a.add(kit.sweep(horn * (mirror, 1, 1), kit.ring_profile(1, .85, 9), 'horn', scale=radius))
        a.add(lathe([(.04, 1.33), (.05, 1.38), (.0015, 1.52)], 6), 'brass')
    else:
        raise ValueError(f'Unknown staff design {kind}')
    return a.build()


def arsenal(design):
    """Build one explicit weapon design; unknown kinds fail instead of falling back."""
    kind=design.get('kind')
    if kind not in KINDS:
        raise ValueError(f'Unknown arsenal design {kind}')
    if kind in KINDS[:3]:return club(kind)
    if kind in ('scythe','halberd','spear'):return polearm(kind)
    if kind in WANDS:return wand(kind)
    if kind in STAVES:return staff(kind)
    return sword(kind)
