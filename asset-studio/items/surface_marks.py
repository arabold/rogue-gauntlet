"""Project scroll markings into parchment face colors instead of raised color shells."""

from collections import defaultdict, Counter
import numpy as np
import manifold3d as mf
from core import kit
from items.solid import welded, conform_surface


def scroll_ink(piece):
    """Retain the authored glyph silhouette, cutting color regions into supporting faces.

    Only the known scroll layout is supported: separate marks in front of the
    parchment, inside the frame. New vertices interpolate the supporting triangle;
    they never move off its surface. Functional rolls, frame and paper stay intact.
    """
    p, f = welded(piece.verts, piece.faces)
    edges = defaultdict(list)
    for i, face in enumerate(f):
        for a, b in zip(face, np.roll(face, -1)):
            edges[tuple(sorted((int(a), int(b))))].append(i)
    neighbors = defaultdict(set)
    for faces in edges.values():
        for i in faces:
            neighbors[i].update(faces)
    glyph = set()
    unseen = set(range(len(f)))
    while unseen:
        queue, group = [unseen.pop()], []
        while queue:
            i = queue.pop()
            group.append(i)
            for j in neighbors[i]:
                if j in unseen:
                    unseen.remove(j)
                    queue.append(j)
        xyz = p[f[group]]
        if xyz[:, :, 2].min() > .029 and xyz[:, :, 1].min() > .08 and xyz[:, :, 1].max() < .56:
            glyph.update(group)
    if not glyph:
        raise ValueError("Scroll ink conversion found no isolated authored marks")
    contours, weights = [], Counter()
    for i in sorted(glyph):
        xy = p[f[i], :2]
        a, b = xy[1] - xy[0], xy[2] - xy[0]
        area = float(a[0] * b[1] - a[1] * b[0])
        if abs(area) > 1e-10:
            contours.append(mf.CrossSection([xy if area > 0 else xy[::-1]]))
            weights[str(piece.slots[i])] += abs(area)
    ink = mf.CrossSection.batch_boolean(contours, mf.OpType.Add)
    ink_slot = weights.most_common(1)[0][0]
    vertices, faces, slots = [], [], []
    painted_area = 0.
    painted_regions = []

    def add(points, triangles, slot):
        offset = len(vertices)
        vertices.extend(points)
        faces.extend(np.asarray(triangles) + offset)
        slots.extend([slot] * len(triangles))

    for i, face in enumerate(f):
        if i in glyph:
            continue
        tri = p[face]
        normal = np.cross(tri[1] - tri[0], tri[2] - tri[0])
        if normal[2] <= .8 * np.linalg.norm(normal):
            add(tri, [[0, 1, 2]], piece.slots[i])
            continue
        section = mf.CrossSection([tri[:, :2]])
        painted = section ^ ink
        if painted.is_empty() or painted.area() < 1e-10:
            add(tri, [[0, 1, 2]], piece.slots[i])
            continue
        # Parchment bows toward the viewer in the center. A fixed Z cutoff
        # clips the middle of its rune; interpolate on every supporting face.
        painted_area += painted.area()
        painted_regions.append(painted)
        basis = np.column_stack([tri[1, :2] - tri[0, :2], tri[2, :2] - tri[0, :2]])
        for region, slot in [(section - ink, piece.slots[i]), (painted, ink_slot)]:
            if region.is_empty():
                continue
            polygons = region.to_polygons()
            xy = np.concatenate(polygons)
            indices = np.asarray(mf.triangulate(polygons, allow_convex=False), int)
            bary = np.linalg.solve(basis, (xy - tri[0, :2]).T).T
            xyz = tri[0] + bary[:, :1] * (tri[1] - tri[0]) + bary[:, 1:] * (tri[2] - tri[0])
            add(xyz, indices, slot)
    if painted_area <= 0:
        raise ValueError("Scroll marks did not project onto parchment")
    coverage = mf.CrossSection.batch_boolean(painted_regions, mf.OpType.Add)
    missing_area = (ink - coverage).area()
    if missing_area > max(1e-9, ink.area() * 1e-6):
        raise ValueError(f"Scroll ink conversion lost part of its symbol: {missing_area:g} square units")
    return conform_surface(kit.Piece(np.asarray(vertices), np.asarray(faces), np.asarray(slots, object))), {
        "removed_mark_triangles": len(glyph), "painted_area": painted_area,
        "ink_slot": ink_slot, "symbol_area": ink.area(), "missing_symbol_area": missing_area,
        "geometry_policy": "Glyph outlines retained as face colors on the original parchment surface"}
