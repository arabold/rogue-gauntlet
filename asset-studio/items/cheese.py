"""Preserve the original cheese wedge while replacing raised hole spots with real cavities."""

from collections import defaultdict
import numpy as np
import manifold3d as mf
from core import kit
from items.solid import Assembly, welded, inspect


def carve_holes(piece):
    """Keep the largest authored wedge; use the four original spots as carving guides."""
    p, f = welded(piece.verts, piece.faces)
    edges = defaultdict(list)
    for i, face in enumerate(f):
        for a, b in zip(face, np.roll(face, -1)):
            edges[tuple(sorted((int(a), int(b))))].append(i)
    neighbors = defaultdict(set)
    for ids in edges.values():
        for i in ids:
            neighbors[i].update(ids)
    unseen, groups = set(range(len(f))), []
    while unseen:
        queue, group = [unseen.pop()], []
        while queue:
            i = queue.pop()
            group.append(i)
            for j in neighbors[i]:
                if j in unseen:
                    unseen.remove(j)
                    queue.append(j)
        groups.append(group)
    groups.sort(key=lambda ids: inspect(p, f[ids])["signed_volume"], reverse=True)
    if len(groups) != 5:
        raise ValueError("Cheese carving requires the known wedge and four authored spot guides")
    main = kit.Piece(p, f[groups[0]], piece.slots[groups[0]])
    assembly = Assembly().add(main)
    body = assembly.parts[0]
    before = body.volume()
    centers = []
    for ids in groups[1:]:
        tri = p[f[ids]]
        center = (tri.min((0, 1)) + tri.max((0, 1))) / 2
        radius = max(float(np.ptp(tri.reshape(-1, 3), axis=0).max()) * .58, .035)
        top = center[1] > .25
        center[1 if top else 2] -= .012
        cutter = mf.Manifold.sphere(radius, 12).scale((1, .85, 1)).translate(center)
        assembly.add(cutter, "surface_rust")
        body = body - assembly.parts[-1]
        centers.append(center.tolist())
    # One top recess continues through the wedge, making a true cheese opening.
    center = min(centers, key=lambda xyz: xyz[0] if xyz[1] > .25 else 10)
    cutter = mf.Manifold.cylinder(.40, .019, circular_segments=12).rotate((-90, 0, 0)).translate((center[0], -.05, center[2]))
    assembly.add(cutter, "surface_rust")
    body = body - assembly.parts[-1]
    assembly.parts = [body]
    result = assembly.build()
    return result, {"removed_spot_triangles": sum(len(ids) for ids in groups[1:]),
                    "carved_volume": before - body.volume(), "recesses": 4, "through_holes": 1,
                    "geometry_policy": "Original wedge silhouette retained; raised color spots replaced by subtractive cavities"}
