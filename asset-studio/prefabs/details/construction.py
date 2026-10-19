"""Reusable shaped timber and forged repair fittings for shields, doors and props.

These are closed physical parts. Callers must intersect/union them with their
supports; merging render meshes alone does not establish an attachment.
"""

import numpy as np
from core import kit


def timber_plank(width=.14, length=1.5, thickness=.055, bow=.005, phase=0., slot='wood'):
    """Chamfered plank with a gentle twist and uneven edges, without texture noise."""
    # Subdivide a long board into transverse sections so the bow is geometric.
    levels=np.linspace(-length/2,length/2,7)
    rings=[]
    for y in levels:
        x=bow*np.sin(y*5+phase)
        z=bow*.65*np.cos(y*4+phase)
        w=width/2*(1+.025*np.sin(y*6+phase))
        b=min(.008,width*.06);t=thickness/2
        rings.append(np.array([[-w+b,y,-t], [w-b,y,-t], [w,y,-t+b], [w,y,t-b],
                               [w-b,y,t],[-w+b,y,t],[-w,y,t-b],[-w,y,-t+b]])+[x,0,z])
    v,f=kit._loft(rings);n=8
    v,f=kit._cap_loops(v,f,[list(range(n)),list(range(len(v)-n,len(v)))])
    return kit.Piece(v,kit._orient_closed(v,f),slot)


def repair_plate(width=.27, height=.12, thickness=.025, slot='iron'):
    """Forged patch with clipped corners and a subtly uneven outline."""
    points=np.array([[-.50,-.30],[-.35,-.5],[.38,-.46],[.5,-.20],[.47,.35],[.26,.5],[-.40,.45],[-.5,.18]])*[width,height]
    lower=np.column_stack([points,np.zeros(8)])
    upper=np.column_stack([points*.90,np.full(8,thickness)])
    v,f=kit._loft([lower,upper]);v,f=kit._cap_loops(v,f,[list(range(8)),list(range(8,16))])
    return kit.Piece(v,kit._orient_closed(v,f),slot)


def corner_bracket(size=.11, thickness=.024, slot='iron'):
    """Chamfered corner reinforcement with a genuine L-shaped silhouette."""
    xy=np.array([[-.5,-.5],[.5,-.5],[.5,-.10],[-.10,-.10],[-.10,.5],[-.5,.5]])*size
    # Concave outline is triangulated by the solid helper rather than a fan cap.
    from items.solid import polygon
    mesh=polygon(xy,thickness).to_mesh()
    return kit.Piece(mesh.vert_properties[:,:3],mesh.tri_verts,slot)


def rim_clamp(width=.18,height=.16,depth=.19,slot='iron'):
    """Solid C-shaped rim clamp with front/back jaws and an outer bridge.

    Local +X points outward across the rim, +Y follows it and +Z is the front.
    The jaws overlap a .115-thick frame when centered at its outer edge.
    """
    from items.solid import polygon
    xz=np.array([(-.55,-.5),(.5,-.5),(.5,.5),(-.55,.5),
                 (-.55,.23),(.18,.23),(.18,-.23),(-.55,-.23)])*[width,depth]
    shape=polygon(xz,height).rotate((90,0,0))
    # Broad chamfers keep the exaggerated clamp readable without box-sharp ears.
    import manifold3d as mf
    from items.solid import welded
    envelope=kit.bevel_box((width*1.05,height,depth),min(.020,height*.14),slot,center=(-.025*width,0,0))
    v,f=welded(envelope.verts,envelope.faces)
    shape=shape ^ mf.Manifold(mf.Mesh(np.asarray(v,np.float32),np.asarray(f,np.uint32)))
    mesh=shape.to_mesh()
    return kit.Piece(mesh.vert_properties[:,:3],mesh.tri_verts,slot)


def horn_socket(radius=.13,length=.15,slot='iron'):
    """Faceted tapered mounting boot with a reinforced foot and raised mouth lip.

    Local +Y follows the horn root. The closed interior is buried in the horn;
    the visible exterior defines a thick load-bearing metal socket.
    """
    from items.solid import lathe
    profile=[(radius*.90,-.025),(radius*1.14,-.010),(radius*1.14,.025),
             (radius*.97,.040),(radius*.97,length-.030),
             (radius*1.06,length-.018),(radius*.94,length)]
    mesh=lathe(profile,10).to_mesh()
    return kit.Piece(mesh.vert_properties[:,:3],mesh.tri_verts,slot)


CONSTRUCTION={'timber_plank':timber_plank,'repair_plate':repair_plate,'corner_bracket':corner_bracket,'rim_clamp':rim_clamp,'horn_socket':horn_socket}


def catalog():
    colors={'wood':(120,77,43),'iron':(82,95,105)}
    yield 'shaped timber plank',timber_plank(length=.55),None,colors
    yield 'forged repair plate',repair_plate(),None,colors
    yield 'corner bracket',corner_bracket(),None,colors
    yield 'rim clamp',rim_clamp(),None,colors
    yield 'horn mounting socket',horn_socket(),None,colors
