"""Edge shading for finished parts: smooth faces, sharp edges where faces turn more than an angle."""

import math

import bmesh


def close_open(bm, index):
    """Close every open rim of `bm` with a cap taking material index `index`: a relief modeled as a front surface
    (a skull on a band, a plate on a shell) becomes a closed solid whose back sits hidden against its support."""
    caps = bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)["faces"]
    for face in caps:
        face.material_index = index
    bmesh.ops.triangulate(bm, faces=caps, quad_method="BEAUTY", ngon_method="BEAUTY")


def mark_sharp(bm, angle_deg):
    """Smooth-shade every face and mark edges whose faces turn more than `angle_deg` as sharp.

    Edges a builder already marked sharp (rims, rails) stay sharp; open and non-manifold edges are always sharp. The
    glTF exporter splits normals at sharp edges, so a small angle (1 degree) gives crisp low-poly facets and a larger
    one keeps gentle curves smooth while bevels stay hard. `angle_deg=None` keeps only the edges marked already.
    """
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for face in bm.faces:
        face.smooth = True
    for edge in bm.edges:
        if not edge.smooth:
            continue
        if len(edge.link_faces) != 2:
            edge.smooth = False
            continue
        if angle_deg is None:
            continue
        limit = math.radians(angle_deg)
        a, b = edge.link_faces
        if a.normal.length > 1e-6 and b.normal.length > 1e-6:
            edge.smooth = a.normal.angle(b.normal) < limit
