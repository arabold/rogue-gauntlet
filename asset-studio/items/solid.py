"""Real solid assembly and topology checks for item meshes.

Boolean union removes internal faces and creates shared intersection topology.
Neither object joining nor overlapping bounding boxes establishes connectivity.
Export seams are welded by position for inspection; authoring meshes are untouched.
"""

import numpy as np
import manifold3d as mf
from core import kit
from core.mesh import signed_volume
from collections import defaultdict
from scipy.spatial import cKDTree


def welded(vertices, faces, tolerance=1e-7):
    """Recover position topology across normal/UV/material seams, at an explicit geometric tolerance."""
    p = np.asarray(vertices, float)
    _, first, inverse = np.unique(np.round(p / tolerance).astype(np.int64), axis=0, return_index=True, return_inverse=True)
    return p[first], inverse[np.asarray(faces, int)]


def inspect(vertices, faces):
    """Report closed edge-manifold connectivity, orientation, duplicate faces, and signed volume."""
    p, f = welded(vertices, faces)
    parent = np.arange(len(f))

    def root(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    edges = {}
    for index, (a, b, c) in enumerate(f):
        for start, end in [(a, b), (b, c), (c, a)]:
            key = (min(start, end), max(start, end))
            edges.setdefault(key, []).append((index, 1 if start < end else -1))
    for adjacent in edges.values():
        for face, _ in adjacent[1:]:
            parent[root(face)] = root(adjacent[0][0])
    components = len({root(i) for i in range(len(f))})
    boundary = sum(len(a) == 1 for a in edges.values())
    nonmanifold = sum(len(a) > 2 for a in edges.values())
    inconsistent = sum(len(a) == 2 and a[0][1] == a[1][1] for a in edges.values())
    duplicates = len(f) - len(np.unique(np.sort(f, axis=1), axis=0))
    volume = signed_volume(p, f)
    return {"components": components, "boundary_edges": boundary, "nonmanifold_edges": nonmanifold,
            "inconsistent_edges": inconsistent, "duplicate_faces": duplicates, "signed_volume": volume}


class Assembly:
    """Union colored solids; material slots survive intersections as vertex properties."""

    def __init__(self):
        self.parts, self.slots = [], []

    def add(self, shape, slot=None):
        if isinstance(shape, kit.Piece):
            if slot is None:
                # Preserve face slots through solid unions, including flush painted details.
                for name in shape.slots:
                    if str(name) not in self.slots:
                        self.slots.append(str(name))
                p = shape.verts[shape.faces].reshape(-1, 3)
                labels = np.repeat([self.slots.index(str(s)) for s in shape.slots], 3)
                properties = np.column_stack([p, labels]).astype(np.float32)
                _, first, inverse = np.unique(np.round(p / 1e-7).astype(np.int64), axis=0, return_index=True, return_inverse=True)
                indices = np.arange(len(p), dtype=np.uint32)
                target = first[inverse].astype(np.uint32)
                seams = indices != target
                face_labels = labels.reshape(-1, 3)[:, 0]
                order = np.argsort(face_labels, kind="stable")
                unique, counts = np.unique(face_labels[order], return_counts=True)
                runs = np.r_[0, np.cumsum(counts) * 3].astype(np.uint32)
                first_id = mf.Manifold.reserve_ids(len(unique))
                # Separate material provenance prevents coplanar simplification from
                # interpolating an ink slot across the surrounding parchment.
                ids = np.arange(first_id, first_id + len(unique), dtype=np.uint32)
                shape = mf.Manifold(mf.Mesh(properties, indices.reshape(-1, 3)[order], indices[seams], target[seams],
                                           run_index=runs, run_original_id=ids))
                if shape.status() != mf.Error.NoError:
                    raise ValueError(f"Invalid colored solid: {shape.status()}")
                self.parts.append(shape)
                return self
            p, f = welded(shape.verts, shape.faces)
            shape = mf.Manifold(mf.Mesh(np.asarray(p, np.float32), np.asarray(f, np.uint32)))
        if shape.status() != mf.Error.NoError or shape.is_empty():
            raise ValueError(f"Invalid solid for slot {slot}: {shape.status()}")
        if slot is None:
            raise ValueError("Solid primitives require a material slot")
        if slot not in self.slots:
            self.slots.append(slot)
        index = self.slots.index(slot)
        self.parts.append(shape.set_properties(1, lambda position, old, label=index: [label]))
        return self

    def build(self):
        # Simplifying a coplanar region can erase its painted slot boundary.
        result = mf.Manifold.batch_boolean(self.parts, mf.OpType.Add)
        if result.status() != mf.Error.NoError:
            raise ValueError(f"Solid union failed: {result.status()}")
        components = result.decompose()
        # Manifold returns an enclosed cavity as a separate, negatively wound
        # surface. It is not an additional disconnected physical part.
        bodies = sum(component.volume() > 0 for component in components)
        if bodies != 1:
            raise ValueError(f"Assembly has {bodies} disconnected solids; model actual connecting geometry")
        mesh = result.to_mesh()
        p, f = np.asarray(mesh.vert_properties[:, :3]), np.asarray(mesh.tri_verts)
        labels = np.rint(mesh.vert_properties[:, 3][f].mean(axis=1)).astype(int)
        # Float32 GLB positions can collapse microscopic intersection slivers.
        # Weld and remove only collapsed faces, then require the full topology gate.
        p, f = welded(p, f)
        keep = (f[:, 0] != f[:, 1]) & (f[:, 0] != f[:, 2]) & (f[:, 1] != f[:, 2])
        f, labels = f[keep], labels[keep]
        return kit.Piece(p, f, np.array(self.slots, dtype=object)[labels])


def box(size, center=(0, 0, 0), bevel=.015):
    """Chamfered solid box in project coordinates."""
    return kit.bevel_box(size, bevel, "base", center=center)


def tube(start, end, radius, sides=10):
    """Closed cylinder along a world-space segment."""
    return kit.sweep([start, end], kit.ring_profile(radius, radius, sides), "base")


def lathe(profile, sides=12):
    """Closed radius/Y profile revolved around Y, with geometric rather than overlapping bands."""
    polygon = np.array([(0, profile[0][1]), *profile, (0, profile[-1][1])], float)
    # CrossSection uses positive winding; radius/Y silhouettes often arrive clockwise.
    if np.sum(polygon[:, 0] * np.roll(polygon[:, 1], -1) - polygon[:, 1] * np.roll(polygon[:, 0], -1)) < 0:
        polygon = polygon[::-1]
    return mf.Manifold.revolve(mf.CrossSection([polygon]), sides).rotate((-90, 0, 0))


def polygon(points, thickness):
    """Solid extruded silhouette in XY, centered across Z."""
    points = np.asarray(points, float)
    if np.sum(points[:, 0] * np.roll(points[:, 1], -1) - points[:, 1] * np.roll(points[:, 0], -1)) < 0:
        points = points[::-1]
    return mf.Manifold.extrude(mf.CrossSection([points]), thickness).translate((0, 0, -thickness / 2))


def loop(center, radii, thickness, sides=16):
    """A real hollow loop in XY, closed around Z; usable for handles, keys, and rings."""
    angles = np.arange(sides) * 2 * np.pi / sides
    outer = np.column_stack([np.cos(angles) * radii[0], np.sin(angles) * radii[1]])
    inner = np.column_stack([np.cos(angles) * (radii[0] - thickness), np.sin(angles) * (radii[1] - thickness)])
    section = mf.CrossSection([outer, inner[::-1]])
    return mf.Manifold.extrude(section, thickness).translate((center[0], center[1], center[2] - thickness / 2))


def fuse_closed(piece):
    """Union a compound part's original indexed islands, preserving colors and requiring real contact."""
    from core.rig import _islands
    labels = _islands(piece.faces,len(piece.verts))[piece.faces[:,0]]
    assembly = Assembly()
    for label in np.unique(labels):
        keep = labels==label
        assembly.add(kit.Piece(piece.verts,piece.faces[keep],piece.slots[keep]))
    return assembly.build()


def conform_surface(piece):
    """Split neighboring edges at new surface vertices, avoiding surface T-junctions."""
    p, f = welded(piece.verts, piece.faces)
    # Clipped vertices can straddle the quantized weld bins while differing
    # by less than float32 precision. Merge by distance before triangulation.
    parent = np.arange(len(p))
    def root(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    for a, b in sorted(cKDTree(p).query_pairs(1e-7)):
        a, b = root(a), root(b)
        parent[max(a, b)] = min(a, b)
    f = np.array([root(i) for i in range(len(p))])[f]
    used, inverse = np.unique(f, return_inverse=True)
    p, f = p[used], inverse.reshape(-1, 3)
    tri = p[f]
    keep = np.linalg.norm(np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0]), axis=1) > 1e-12
    # Boolean clipping can leave slivers that collapse at the topology weld
    # tolerance. Discard these before finding edges or splitting neighbors.
    f = f[keep]
    source_slots = piece.slots[keep]
    uses = defaultdict(int)
    for face in f:
        for a, b in zip(face, np.roll(face, -1)):
            uses[tuple(sorted((int(a), int(b))))] += 1
    split = {}
    for (a, b), count in uses.items():
        if count != 1:
            continue
        edge = p[b] - p[a]
        length = float(edge @ edge)
        t = ((p - p[a]) @ edge) / length
        distance = np.linalg.norm(p - (p[a] + t[:, None] * edge), axis=1)
        candidates = np.flatnonzero((t > 1e-6) & (t < 1 - 1e-6) & (distance < 1e-7))
        if len(candidates):
            split[(a, b)] = candidates[np.argsort(t[candidates])].tolist()
    vertices, faces, slots = p.tolist(), [], []
    for i, face in enumerate(f):
        loop = []
        for a, b in zip(face, np.roll(face, -1)):
            loop.append(int(a))
            key = tuple(sorted((int(a), int(b))))
            if key in split:
                loop.extend(split[key] if a < b else split[key][::-1])
        if len(loop) == 3:
            faces.append(face.tolist())
            slots.append(source_slots[i])
        else:
            center = len(vertices)
            vertices.append(p[face].mean(0).tolist())
            for a, b in zip(loop, loop[1:] + loop[:1]):
                faces.append([center, a, b])
                slots.append(source_slots[i])
    return kit.Piece(np.asarray(vertices), np.asarray(faces), np.asarray(slots, object))

