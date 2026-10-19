"""Distinct weapon silhouettes with continuous bevels and physically joined fittings.

All dimensions use metres, +Y follows the weapon, and the origin is the hand grip.
Color regions describe materials, not baked lighting or decorative mesh shells.
"""

import numpy as np
import manifold3d as mf
from core import kit
from items.solid import Assembly, lathe, tube, polygon, loop, box

KINDS = ('wooden_club', 'steel_club', 'morning_star', 'short_dagger', 'saber',
         'rapier', 'greatsword', 'broadsword', 'scythe', 'halberd', 'spear')
COLORS = {'steel': [134, 153, 166], 'edge': [181, 194, 203], 'iron': [65, 76, 83],
          'wood': [119, 78, 46], 'endgrain': [148, 104, 62], 'leather': [79, 47, 32],
          'brass': [165, 119, 48]}
MATERIALS = {name: {'metallic': .55, 'roughness': .36, 'crease': 48}
             for name in ('steel', 'edge', 'iron', 'brass')}
MATERIALS.update({name: {'metallic': 0, 'roughness': .9, 'crease': 55}
                  for name in ('wood', 'endgrain', 'leather')})


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
    if kind=='greatsword':
        grip(a,-.34,.19,.051);pommel(a,-.395,.086)
        guard(a,.34,.25,.07)
        stations=[(0,.22,.075,.035),(0,.40,.083,.037),(0,.52,.115,.036),
                  (0,1.55,.093,.028),(0,1.88,.048,.018),(0,2.00,.0018,.002)]
    elif kind=='broadsword':
        grip(a);pommel(a)
        guard(a,.25,.23,-.035)
        stations=[(0,.20,.105,.035),(0,.34,.135,.038),(0,1.01,.125,.030),
                  (0,1.25,.075,.023),(0,1.39,.002,.002)]
    elif kind=='short_dagger':
        grip(a,-.15,.11,.040);pommel(a,-.197,.056)
        guard(a,.145,.15,.025)
        stations=[(0,.13,.063,.025),(0,.24,.075,.028),(0,.52,.049,.019),(0,.69,.0018,.0018)]
    elif kind=='rapier':
        grip(a,-.18,.13,.041);pommel(a,-.225,.067,'brass')
        guard(a,.21,.20,.015,'brass')
        # Pierced swept basket, anchored at both guard and pommel.
        a.add(kit.sweep([(0,-.20,0),(.17,-.12,.015),(.21,.04,.015),(.17,.18,0),(.07,.22,0)],
                       kit.ring_profile(.022,.022,8),'brass'),'brass')
        a.add(loop((0,0,0),(.15,.10),.022,20).rotate((65,0,0)).translate((0,.21,0)), 'brass')
        stations=[(0,.18,.055,.025),(0,.31,.042,.021),(0,1.18,.025,.014),(0,1.51,.0015,.0015)]
    else:
        grip(a,-.18,.14,.043);pommel(a,-.22,.062,'brass')
        guard(a,.18,.20,-.025,'brass')
        a.add(kit.sweep([(.15,.19,0),(.20,.07,0),(.18,-.13,0),(.06,-.22,0),(0,-.22,0)],
                       kit.ring_profile(.024,.026,8),'brass'),'brass')
        stations=[(0,.18,.065,.030),(0,.34,.071,.030),(.025,.68,.074,.027),
                  (.095,1.00,.072,.025),(.19,1.24,.055,.021),(.26,1.40,.002,.002)]
    a.add(blade(stations))
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


def arsenal(design):
    """Build one explicit weapon design; unknown kinds fail instead of falling back."""
    kind=design.get('kind')
    if kind not in KINDS:
        raise ValueError(f'Unknown arsenal design {kind}')
    if kind in KINDS[:3]:return club(kind)
    if kind in ('scythe','halberd','spear'):return polearm(kind)
    return sword(kind)
