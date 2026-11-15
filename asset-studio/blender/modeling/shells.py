"""Shells worn over a host: keep them clear of it, then give them thickness."""

from mathutils import Vector


def clear_host(grid, bvh, clearance, center):
    """Push every point of `grid` (rows of `Vector`s, edited in place) out along its ray from `center` until it lies
    at least `clearance` outside the host surface `bvh` (`Context.host_bvh`) on that ray.

    Rays that miss the host leave their point alone, but a grid that misses it entirely is an error: the shell and
    host are in different frames.
    """
    hits = 0
    for row in grid:
        for i, point in enumerate(row):
            offset = point - center
            reach = offset.length
            if reach < 1e-6:
                continue
            direction = offset / reach
            location, _normal, _index, _distance = bvh.ray_cast(center, direction, 4.0)
            if location is None:
                continue
            hits += 1
            needed = (location - center).length + clearance
            if reach < needed:
                row[i] = center + direction * needed
    if not hits:
        raise ValueError("Shell grid never meets its host; check the frame and the ray center")


def solidify(bm, thickness, inner_index, center, rim_index=0, level=()):
    """Give the open surface `bm` an inner skin `thickness` inward and close its open rims.

    Inner faces take material index `inner_index` and rim faces `rim_index`; rim edges are marked sharp. Points move
    inward along a blend of their normal and the direction toward `center`, flattened vertically so a dome thickens
    evenly; vertices in `level` (e.g. a brow cutout's rim) never move down, so the rim cannot dip below the trim.
    """
    bm.verts.ensure_lookup_table()
    bm.normal_update()
    original_faces = list(bm.faces)
    rims = [e for e in bm.edges if e.is_boundary]
    inner = {}
    for vert in list(bm.verts):
        radial = vert.co - center
        radial.z *= 0.30
        if radial.length > 1e-6:
            radial.normalize()
        normal = vert.normal.copy()
        inward = -(0.60 * normal + 0.40 * radial).normalized() if normal.length > 1e-6 else -radial
        if vert in level:
            inward.z = max(inward.z, 0.02)
            inward.normalize()
        moved = bm.verts.new(vert.co + inward * thickness)
        if abs(vert.co.x) < 1e-6:
            moved.co.x = 0.0
        inner[vert] = moved
    for face in original_faces:
        new = bm.faces.new([inner[v] for v in reversed(face.verts)])
        new.material_index = inner_index
    for edge in rims:
        a, b = edge.verts
        loop = edge.link_loops[0]
        quad = (a, inner[a], inner[b], b) if loop.vert == a else (b, inner[b], inner[a], a)
        face = bm.faces.new(quad)
        face.material_index = rim_index
        edge.smooth = False
        inner_edge = bm.edges.get((inner[a], inner[b]))
        if inner_edge:
            inner_edge.smooth = False


def grid_points(rows):
    """Plain `Vector` copies of nested coordinates, for `clear_host` to edit."""
    return [[Vector(p) for p in row] for row in rows]
