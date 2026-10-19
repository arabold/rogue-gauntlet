"""One-piece cloth capes with a continuous chest bib, shoulder yoke and back drape."""
import numpy as np
from core import kit
from prefabs.outfits import clothes


def build(torso, hem=.2, width=62., slot="cloak", trim=None, flare=.055,
          front_drop=.15, pieces=(), back_clearance=0, front_hem=None):
    """Loft one closed garment from its open neckline to front bib and back hem.

    The shoulder and chest section belongs to the cape, not a separate gorget.
    Fit its upper surface over existing armor; the back flares clear of the hips.
    A chest attachment keeps the whole garment together without arm weighting.
    """
    from prefabs.outfits.costumes import _bound
    sides,rows=32,14
    theta=np.arange(sides)*2*np.pi/sides
    front=np.clip(np.sin(theta),0,1)
    back=np.clip(-np.sin(theta),0,1)
    # A smooth angular transition leaves short side openings and a broad back hem.
    blend=np.clip((back-.12)/.48,0,1)
    blend=blend*blend*(3-2*blend)
    bottom=(1.125-front_drop*front**2)*(1-blend)+hem*blend
    if front_hem is not None:
        front_blend=np.clip((front-.12)/.48,0,1)
        front_blend=front_blend*front_blend*(3-2*front_blend)
        bottom=bottom*(1-front_blend)+front_hem*front_blend
    top=np.full(sides,1.235)
    upper=torso
    a,b=(float(upper.section([.68])[i][0]) for i in (1,3))
    host=clothes._Bell(upper,.72,hem,a+.045+flare,b+.06+flare*.5)
    # Keep bevels a constant physical width even when front/back hems differ.
    # A shared fractional bevel would turn the long front's neckline into a wide bib.
    c=.035*.30
    dt=np.minimum(c/(top-bottom),.45)
    profile=[(np.zeros(sides),.012),(np.zeros(sides),.035-c)]
    profile += [(t,.035) for t in np.linspace(dt,1-dt,rows)]
    profile += [(np.ones(sides),.035-c),(np.ones(sides),.012)]
    piece=kit.host_band(host,bottom,top,.035,slot,sides=sides,profile=profile)
    v=piece.verts
    if pieces:
        # The flared host supplies the drape; project its upper shell to actual
        # triangles, since an analytical torso cannot describe raised armor.
        support=kit.Piece.merge(*pieces)
        cx,_,cz,_=torso.section(v[:,1])
        center=np.column_stack([cx,v[:,1],cz])
        radial=v-center
        radius=np.linalg.norm(radial,axis=1)
        direction=radial/np.maximum(radius[:,None],1e-9)
        hit=kit.ray_distance(center+2*direction,-direction,support.verts,support.faces,chunk=16)
        c=.035*.30
        offset=np.repeat([.012,.035-c,*([.035]*rows),.035-c,.012],sides)
        target=np.where(np.isfinite(hit),2-hit+offset,radius)
        v[:]=center+direction*np.maximum(radius,target)[:,None]
    # Widen only the drape; the neck opening and shoulder fit retain body dimensions.
    low=np.clip((1.05-v[:,1])/.45,0,1)
    v[:,0]*=1+(width/62.-1)*low
    # Arch over the same shoulder sleeve envelope as the approved neckwear.
    x,z=v[:,0],v[:,2]
    high=np.clip((v[:,1]-1.04)/.12,0,1)
    across=np.clip((np.abs(x)-.12)/.18,0,1)
    v[:,1]+=.125*np.sqrt(across)*np.exp(-(z/.24)**4)*high
    along=np.clip((np.abs(x)-.112)/.24,0,1)
    depth=.215*(1-along)+.140*along
    height=.130+.015*np.sin(np.pi*along)
    over=(np.abs(x)>.10)&(np.abs(x)<.352)&(np.abs(z)<depth)
    shoulder=1.107+height*np.sqrt(np.maximum(1-(z/depth)**2,0))+.035
    current=v[over,1]
    shoulder_top=shoulder[over]+.018*np.logaddexp(0,(current-shoulder[over])/.018)
    blend=np.clip((current-1.03)/.16,0,1)
    blend=blend*blend*(3-2*blend)
    v[over,1]+=blend*(shoulder_top-current)
    v[:,1]+=.007*high
    if back_clearance:
        # Rigid back drape needs an explicit allowance over raised harness fittings.
        theta_v=np.arctan2(v[:,2],v[:,0])
        v[:,2]-=back_clearance*np.clip(-np.sin(theta_v),0,1)**2
    if trim:
        # Paint the hem and neckline in the same shell, without extra trim objects.
        row=np.arange(len(piece.faces))//(sides*2)
        piece.slots[np.isin(row,(0,1,rows,rows+1,rows+2))]=trim
    piece.cover_skin=False  # A moving open cape must not remove the body behind it.
    piece.garment_type="cape"
    # A full tabard must share torso/hips skinning at the waist beneath its belt.
    return _bound(piece,"chest") if front_hem is None else piece
