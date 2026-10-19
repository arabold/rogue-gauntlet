"""Continuous fur borders integrated into closed armor and clothing surfaces."""
import numpy as np
from core import kit
from items import solid


def edge(path, outward, fall=(0,-1,0), length=.045, thickness=.024, slot="fur", variation=0):
    """A closed continuous pelt border with broad pointed tufts, rather than loose floating spikes.

    The path is a closed root loop embedded into its supporting garment. Every four
    samples form one tuft; the backing stays continuous across the entire border.
    """
    path, outward = np.asarray(path,float), np.asarray(outward,float)
    outward = outward/np.linalg.norm(outward,axis=1)[:,None]
    fall = np.broadcast_to(np.asarray(fall,float),path.shape).copy()
    fall /= np.linalg.norm(fall,axis=1)[:,None]
    amount = np.resize(np.array([.40,.65,1.22,.78]),len(path))*length
    top = path-fall*.015
    bottom = path+fall*amount[:,None]
    if variation:
        phase=np.arange(len(path))//4
        bottom+=fall*(variation*np.sin(phase*1.7))[:,None]
    loops = [top-outward*thickness*.45,top+outward*thickness*.55,
             bottom+outward*thickness*.40,bottom-outward*thickness*.45]
    n=len(path);v=np.vstack(loops);f=[]
    for j in range(4):
        for i in range(n):
            a,b=j*n+i,j*n+(i+1)%n
            c,d=((j+1)%4)*n+(i+1)%n,((j+1)%4)*n+i
            f.extend(((a,b,c),(a,c,d)))
    return kit.Piece(v,kit._orient_closed(v,np.asarray(f)),slot)



def attach(support, *borders):
    """Fuse embedded fur borders into one closed support, preserving colors and attachment.

    Roots must overlap the actual support. Disconnected borders fail the solid
    union; never join parts carried by different bones into a deforming solid.
    """
    bone=getattr(support,"bone",None)
    for border in borders:
        if getattr(border,"bone",bone)!=bone:
            raise ValueError("Fur and support must use the same bone attachment")
    result=solid.fuse_closed(kit.Piece.merge(support,*borders))
    report=solid.inspect(result.verts,result.faces)
    for key in ("boundary_edges","nonmanifold_edges","inconsistent_edges","duplicate_faces"):
        if report[key]:
            raise ValueError(f"Invalid integrated fur surface: {key}={report[key]}")
    if bone is not None:
        result.bone=bone
    return result


def border(support, path, outward, fall=(0,-1,0), length=.045, thickness=.024,
           slot="fur", variation=.004):
    """Create and attach a border along an embedded closed root path on any armor part.

    Outward vectors follow the support surface; fall may be a vector per sample,
    allowing upper hems, shoulder rims and non-horizontal curved borders alike.
    """
    return attach(support,edge(path,outward,fall,length,thickness,slot,variation))


FUR={"edge":edge,"border":border}
