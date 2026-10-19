"""Reusable shaped shoulder collars in cloth and steel, fitted to the existing neck transition."""

import numpy as np

from core import kit
from prefabs.outfits import clothes


def torso_supports(pieces):
    """Actual upper garments, excluding sleeve roots, long capes and the body's neck envelope."""
    return [p for p in pieces if getattr(p,"garment_type",None)!="cape" and (
        getattr(p,"bone",None)=="chest" or (getattr(p,"bone",None) is None and
        p.verts[:,1].min()>.55 and 1.08<p.verts[:,1].max()<=1.20))]


class _ArmorSurface:
    """Radial hits on the actual chest plates, without hulls spanning gaps at the neckline."""

    def __init__(self,torso,pieces):
        self.torso = torso
        self.piece = kit.Piece.merge(*pieces)

    def __getattr__(self,name):
        return getattr(self.torso,name)

    def outline(self,y,theta,out):
        theta = np.asarray(theta,float)
        y = np.broadcast_to(np.asarray(y,float),theta.shape)
        base = kit.ring_points(self.torso,y,0,len(theta),theta)
        cx,_,cz,_ = self.torso.section(y)
        center = np.column_stack([cx,y,cz])
        radial = base-center
        radius = np.linalg.norm(radial,axis=1)
        direction = radial/np.maximum(radius[:,None],1e-9)
        hit = kit.ray_distance(center+direction,-direction,self.piece.verts,self.piece.faces,reach=1.)
        support = np.maximum(radius,np.where(np.isfinite(hit),1-hit,0))
        return center+direction*(support+np.asarray(out))[:,None]


def collar(torso, style="scarf", slot="wrap", trim="wrap_dark", front_drop=.105):
    """One closed collar with a neck opening, a shaped bib and integrated edge/fold colors."""
    if style not in ("scarf", "cowl", "gorget"):
        raise ValueError(f"Unknown collar style: {style}")
    theta = np.arange(32)*2*np.pi/32
    front = np.clip(np.sin(theta),0,1)
    back = np.clip(-np.sin(theta),0,1)
    metal = style=="gorget"
    front_angle = np.abs((theta-np.pi/2+np.pi)%(2*np.pi)-np.pi)
    bib = np.maximum(1-front_angle/1.25,0)
    bottom = 1.135-(front_drop*front**2 if metal else .165*bib)-.045*back**2
    top = np.full(32,1.245 if metal else 1.235)
    thickness = .041 if metal else .048
    # Broad shoulder roots narrow with the actual body into the collar's open throat.
    piece = clothes.shell(torso,bottom,top,thickness,slot,inset=.008 if metal else -.015,
                          sides=32,rows=9,bevel=.30,neck=None)
    if style=="cowl":
        # A soft folded roll, shaped on the same vertices rather than stacked colored bands.
        v = piece.verts.copy()
        angle = np.arctan2(v[:,2],v[:,0])
        front_angle = np.abs((angle-np.pi/2+np.pi)%(2*np.pi)-np.pi)
        low = 1.135-.165*np.maximum(1-front_angle/1.25,0)-.045*np.clip(-np.sin(angle),0,1)**2
        t = np.clip((v[:,1]-low)/(1.235-low),0,1)
        radial = .014*np.sin(np.pi*t)**2
        v[:,0] += radial*np.cos(angle)
        v[:,2] += radial*np.sin(angle)
        piece = kit.Piece(v,piece.faces,piece.slots)
    # Follow the constructed ring rows: thresholding triangle centers against polar heights
    # makes a sawtooth color seam on a superellipse's changing cross-section.
    row = np.arange(len(piece.faces))//(32*2)
    piece.slots[np.isin(row,(0,1,10,11,12))] = trim
    if style=="cowl":
        piece.slots[row==6] = trim
    # The torso narrows into the neck, but the shoulder sleeves rise higher than its
    # side section. Arch the collar over that volume instead of cutting across it.
    x,z = piece.verts[:,0],piece.verts[:,2]
    across = np.clip((np.abs(x)-.12)/.18,0,1)
    bend = .125*np.sqrt(across)*np.exp(-(z/.24)**4)
    piece.verts[:,1] += bend
    # Keep the arch's underside above the actual sleeve-root envelope. A smooth
    # maximum preserves thickness; a hard clamp would collapse the inner/outer faces.
    along = np.clip((np.abs(x)-.112)/.24,0,1)
    depth = .215*(1-along)+.140*along
    height = .130+.015*np.sin(np.pi*along)
    over = (np.abs(x)>.10)&(np.abs(x)<.352)&(np.abs(z)<depth)
    shoulder = 1.107+height*np.sqrt(np.maximum(1-(z/depth)**2,0))+.035
    current = piece.verts[over,1]
    piece.verts[over,1] = shoulder[over]+.018*np.logaddexp(0,(current-shoulder[over])/.018)
    piece.verts[:,1] += .007  # Clearance for the triangulated spans between the curved shoulder samples.
    return piece


def gorget(torso, slot="armor", trim="trim", pieces=(), front_drop=.105):
    """A continuous practical steel neck guard; the first study leaves existing costumes unchanged."""
    from prefabs.outfits.costumes import _bound
    host = _ArmorSurface(torso,pieces) if pieces else torso
    return _bound(collar(host,"gorget",slot,trim,front_drop),"chest")


def baldric(torso, y0=.595, y1=1.12, width=.08, thickness=.022, spread=48, shoulder_y=1.265, slot="strap", tuck=0):
    """One continuous shoulder strap with a smooth crown, avoiding the torso's neck taper.

    The front/back runs follow the clothed torso below the shoulder. A cubic crown
    passes over the shoulder instead of following its rapidly narrowing neck rings.
    """
    from scipy.interpolate import make_interp_spline
    ys=np.array([y0,y0+(y1-y0)*.36,y0+(y1-y0)*.74,y1])
    angles=np.pi/2+np.radians(spread)*np.array([1,.40,-.40,-1])
    front=kit.ring_points(torso,ys,thickness*.35,len(ys),angles)
    back=kit.ring_points(torso,ys,thickness*.35,len(ys),2*np.pi-angles)
    shoulder_x=max(float(front[-1,0]),.235)
    controls=np.vstack([front,[shoulder_x,shoulder_y-.055,.10],
                        [shoulder_x,shoulder_y,0],[shoulder_x,shoulder_y-.055,-.10],back[::-1]])
    distance=np.r_[0,np.cumsum(np.linalg.norm(np.diff(controls,axis=0),axis=1))]
    path=make_interp_spline(distance,controls,k=3)(np.linspace(0,distance[-1],80))
    # Keep the low runs flush with the actual tunic, rather than a free spline cutting through it.
    low=(path[:,1]<y1-.005)
    y=path[low,1]
    cx,a,cz,b=torso.section(y)
    exponent=getattr(torso,"p",2.)
    across=np.clip(np.abs((path[low,0]-cx)/(a+thickness*.35)),0,.999)
    path[low,2]=cz+np.sign(path[low,2])*(b+thickness*.35)*np.maximum(1-across**exponent,0)**(1/exponent)
    if tuck:
        # Tuck both tails beneath a fitted waist band without moving the shoulder crown.
        center_x,_,center_z,_=torso.section(path[:,1])
        radial=path-np.column_stack([center_x,path[:,1],center_z])
        radial/=np.maximum(np.linalg.norm(radial,axis=1)[:,None],1e-9)
        weight=np.clip((y0+.17-path[:,1])/.17,0,1)**2
        path-=radial*(tuck*weight)[:,None]
    return kit.sweep(path,kit.rect_profile(width,thickness,.004),slot,up=(0,0,1))
