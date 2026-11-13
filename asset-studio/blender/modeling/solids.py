"""Closed swept parts: faceted tubes along stations (horns, antlers, collars, wraps, bosses) and bladed solids
(feathers, leaves, fins). Every part is closed, so validation can treat it as a solid."""

import math

import bmesh
from mathutils import Vector


def tapered_tube(bm, stations, sides=6, index=0, pointed=True):
    """A `sides`-gon tube through `stations` (`(center, radius)` pairs), capped at both ends.

    Rings are turned half a facet so a flat faces up, and each ring is square to the path there. `pointed` pokes the
    end cap into a tip. Start a branch inside its parent with a small first radius so its base ring stays hidden.
    """
    rings = []
    count = len(stations)
    for i, (center, radius) in enumerate(stations):
        if i == 0:
            tangent = (stations[1][0] - center).normalized()
        elif i == count - 1:
            tangent = (center - stations[i - 1][0]).normalized()
        else:
            tangent = (stations[i + 1][0] - stations[i - 1][0]).normalized()
        reference = Vector((0.0, 1.0, 0.0)) if abs(tangent.y) < 0.85 else Vector((1.0, 0.0, 0.0))
        across = tangent.cross(reference).normalized()
        up = across.cross(tangent).normalized()
        ring = []
        for s in range(sides):
            angle = (s / sides) * 2.0 * math.pi + math.pi / sides
            ring.append(bm.verts.new(center + (across * math.cos(angle) + up * math.sin(angle)) * radius))
        rings.append(ring)
    for i in range(count - 1):
        for s in range(sides):
            n = (s + 1) % sides
            bm.faces.new((rings[i][s], rings[i][n], rings[i + 1][n], rings[i + 1][s])).material_index = index
    bm.faces.new(list(reversed(rings[0]))).material_index = index
    end = bm.faces.new(rings[-1])
    end.material_index = index
    if pointed:
        bmesh.ops.poke(bm, faces=[end])


def blade(bm, root, tip, up, half_width, thickness=0.018, tip_from=0.70, indices=(0, 1)):
    """A faceted diamond-section blade from `root` to `tip`, arched toward `up` and widest past its middle.

    Faces before `tip_from` (a share of the length) take `indices[0]`, the rest `indices[1]`: a feather's colored
    tip, a leaf's dry edge.
    """
    axis = tip - root
    along = axis.normalized()
    across = along.cross(up).normalized()
    normal = across.cross(along).normalized()
    stations = [0.0, 0.18, 0.44, tip_from, 0.88, 1.0]
    widths = [0.24, 0.72, 0.98, 1.00, 0.65, 0.05]
    depths = [0.65, 0.95, 1.00, 0.90, 0.60, 0.15]
    rings = []
    for t, w, d in zip(stations, widths, depths):
        center = root + axis * t + normal * (math.sin(t * math.pi) * 0.016)
        hw, ht = half_width * w, thickness * d
        rings.append([bm.verts.new(center - across * hw), bm.verts.new(center + normal * ht),
                      bm.verts.new(center + across * hw), bm.verts.new(center - normal * ht)])
    for i in range(len(stations) - 1):
        index = indices[1] if stations[i] >= tip_from - 1e-4 else indices[0]
        for s in range(4):
            n = (s + 1) % 4
            bm.faces.new((rings[i][s], rings[i][n], rings[i + 1][n], rings[i + 1][s])).material_index = index
    bm.faces.new(list(reversed(rings[0]))).material_index = indices[0]
    bm.faces.new(rings[-1]).material_index = indices[1]


def rail_loft(bm, rings, flip=False, cap_start=True, cap_end=True, sharp_stations=()):
    """Loft rings of existing vertices (one profile per station: crests, trims, blades) with sharp lengthwise rails
    and smooth faces between them; `sharp_stations` also sharpens those rings, and the caps' rims are sharp.

    Only the loft's own edges are marked, so several lofts, shells and insets can share one bmesh.
    """
    count = len(rings[0])
    faces, rails = [], set()
    for s in range(len(rings) - 1):
        a, b = rings[s], rings[s + 1]
        for k in range(count):
            n = (k + 1) % count
            quad = (a[k], b[k], b[n], a[n]) if flip else (a[k], a[n], b[n], b[k])
            if len(set(quad)) == 4:
                faces.append(bm.faces.new(quad))
            edge = bm.edges.get((a[k], b[k]))
            if edge is not None:
                rails.add(edge)
    stations = set(i for i in sharp_stations if 0 <= i < len(rings))
    if cap_start:
        faces.append(bm.faces.new(list(rings[0]) if flip else list(reversed(rings[0]))))
        stations.add(0)
    if cap_end:
        faces.append(bm.faces.new(list(reversed(rings[-1])) if flip else list(rings[-1])))
        stations.add(len(rings) - 1)
    for i in stations:
        for k in range(count):
            edge = bm.edges.get((rings[i][k], rings[i][(k + 1) % count]))
            if edge is not None:
                rails.add(edge)
    for face in faces:
        face.smooth = True
        for edge in face.edges:
            edge.smooth = edge not in rails
