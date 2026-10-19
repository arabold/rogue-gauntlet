"""Broad connected fur edging and horned buckle reliefs for primitive fantasy costumes."""
import numpy as np
from core import kit
from items import solid
from prefabs.details.heraldry import horn


# Compatibility for older prefab callers; construction lives in the shared fur module.
from prefabs.details.fur import edge as fur_edge


def horned_skull(size=.27):
    """A flat-backed skull buckle with two attached swept horns; roots belong to the relief."""
    from prefabs.outfits.costumes import _skull_mount
    skull = _skull_mount(np.zeros(3),size,"hips",front_half=True)
    # A genuine shallow backing supports the cheeks and both horn roots.
    backing = kit.bevel_box((size*1.10,size*.56,.040),.012,"bone",center=(0,size*.06,.010))
    horns=[]
    for sign in (-1,1):
        horns.append(horn((sign*size*.36,size*.13,.019),
                          (sign*size*.66,size*.43,.035),
                          (sign*size*.50,size*.72,.047),size*.15,"bone"))
    return solid.fuse_closed(kit.Piece.merge(skull,backing,*horns))


def catalog():
    theta=np.arange(48)*2*np.pi/48
    normals=np.column_stack([np.cos(theta),np.zeros(48),np.sin(theta)])
    path=normals*[.21,0,.16]+[0,.10,0]
    yield "fur border",fur_edge(path,normals),None,{"fur":(220,210,185)}
    yield "horned skull buckle",horned_skull(),None,{"bone":(226,215,184)}


PRIMAL={"fur_edge":fur_edge,"horned_skull":horned_skull}
