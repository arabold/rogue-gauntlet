"""Reusable attached heraldic emblems and ridged monster armor plates."""

import numpy as np
import manifold3d as mf
from core import kit
from items.solid import polygon

def crown(size=.30,slot='gold'):
    """Solid three-point crown appliqué, attached through its broad lower band."""
    points=np.array([[-.50,-.30],[.50,-.30],[.62,.38],[.23,.10],[0,.60],[-.23,.10],[-.62,.38]])*size
    shape=mf.Manifold.extrude(mf.CrossSection([points]),.036,scale_top=(.88,.88)).translate((0,0,-.018));mesh=shape.to_mesh()
    return kit.Piece(mesh.vert_properties[:,:3],mesh.tri_verts,slot)


def lily(size=.36,slot='gold'):
    """Broad fleur-de-lis silhouette with connected leaves, stem and cross band."""
    points=np.array([(0,.60),(.16,.30),(.08,.02),(.23,.22),(.48,.17),(.48,-.03),(.28,-.10),
                     (.20,.03),(.10,-.12),(.30,-.16),(.28,-.27),(.09,-.25),(.19,-.50),(0,-.38),
                     (-.19,-.50),(-.09,-.25),(-.28,-.27),(-.30,-.16),(-.10,-.12),(-.20,.03),
                     (-.28,-.10),(-.48,-.03),(-.48,.17),(-.23,.22),(-.08,.02),(-.16,.30)])*size
    if np.sum(points[:,0]*np.roll(points[:,1],-1)-points[:,1]*np.roll(points[:,0],-1))<0:points=points[::-1]
    mesh=mf.Manifold.extrude(mf.CrossSection([points]),.036,scale_top=(.88,.88)).translate((0,0,-.018)).to_mesh()
    return kit.Piece(mesh.vert_properties[:,:3],mesh.tri_verts,slot)


def scale_plate(width=.19,height=.22,slot='dragon'):
    """Thick pointed scale with a convex central ridge, suitable for solid union."""
    edge=np.array([(-.50,.35),(-.35,.50),(.35,.50),(.50,.35),(.43,-.12),(0,-.58),(-.43,-.12)])*[width,height]
    back=np.column_stack([edge,np.zeros(len(edge))]);front=np.column_stack([edge,.012+.035*(.5-edge[:,1]/height)])
    v,f=kit._loft([back,front]);v,f=kit._cap_loops(v,f,[list(range(7))])
    peak=len(v);v=np.vstack([v,[0,height*.06,.065]])
    f=np.vstack([f,[[peak,7+i,7+(i+1)%7] for i in range(7)]])
    return kit.Piece(v,kit._orient_closed(v,f),slot)


def horn(start=(0,0,0),middle=(.04,.12,.03),tip=(.08,.26,.10),radius=.055,slot='bone'):
    """Curved tapered horn with a broad buried root and a small closed tip."""
    start,middle,tip=np.asarray(start,float),np.asarray(middle,float),np.asarray(tip,float)
    path=[start,start*.45+middle*.55,middle,middle*.45+tip*.55,tip]
    return kit.sweep(path,kit.ring_profile(radius,radius,8),slot,scale=[1,.86,.58,.28,.016])


def bound_horn(radius=.108,slot='bone',binding_slot='leather'):
    """Large swept shoulder horn with two solid leather collars, rooted at origin.

    Local +Y points upward. The broad root sits on the shield shoulder; the horn
    first sweeps outward, then returns inward toward its narrow tip.
    """
    from items.solid import Assembly
    path=np.array([(0,0,0),(.15,.11,.025),(.27,.30,.05),(.26,.50,.065),(.18,.70,.085),(.08,.90,.11)])
    scales=np.array([1,.95,.80,.60,.33,.014])
    a=Assembly().add(kit.sweep(path,kit.ring_profile(radius,radius,10),slot,scale=scales),slot)
    for i in [1,3]:
        tangent=path[i+1]-path[i-1];tangent/=np.linalg.norm(tangent)
        centers=[path[i]+tangent*t for t in [-.034,-.022,.022,.034]]
        band=kit.sweep(centers,kit.ring_profile(radius*scales[i]*1.08,radius*scales[i]*1.08,10),binding_slot,
                       scale=[.98,1.08,1.08,.98])
        a.add(band,binding_slot)
    return a.build()


HERALDRY={'crown':crown,'lily':lily,'scale_plate':scale_plate,'horn':horn,'bound_horn':bound_horn}


def catalog():
    colors={'gold':(181,133,51),'dragon':(39,96,75)}
    yield 'crown applique',crown(),None,colors
    yield 'lily applique',lily(),None,colors
    yield 'ridged scale',scale_plate(),None,colors
    yield 'curved horn',horn(),None,{**colors,'bone':(211,192,151)}
    yield 'bound shoulder horn',bound_horn(),None,{**colors,'bone':(211,192,151),'leather':(75,47,31)}
