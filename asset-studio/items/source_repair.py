"""Conservative repairs for authored assemblies: retain surfaces and triangulate boundary loops."""

from collections import Counter, defaultdict, deque
import numpy as np
import manifold3d as mf
from core import kit
from core.mesh import signed_volume
from items.solid import welded, inspect


def repair(piece):
    """Orient unambiguous neighbors, remove duplicate faces, and triangulate boundary loops.

    Existing positions are retained within the weld tolerance. Non-planar caps use
    projection only for triangulation; their vertices remain on the original boundary.
    Unresolved branched boundaries and coincident multipart edges remain reported;
    parts are never bridged or inflated.
    Coplanar nested loops are triangulated together to preserve intended openings.
    """
    p, f = welded(piece.verts, piece.faces)
    slots = np.asarray(piece.slots, object).copy()
    _, first = np.unique(np.sort(f, axis=1), axis=0, return_index=True)
    keep = np.sort(first)
    removed = len(f) - len(keep)
    f, slots = f[keep].copy(), slots[keep]

    def edges():
        result = defaultdict(list)
        for i, face in enumerate(f):
            for a, b in zip(face, np.roll(face, -1)):
                result[tuple(sorted((int(a), int(b))))].append((i, int(a), int(b)))
        return result

    adjacency = defaultdict(list)
    for linked in edges().values():
        if len(linked) == 2:
            (i, a, b), (j, c, d) = linked
            opposite = a == c
            adjacency[i].append((j, opposite))
            adjacency[j].append((i, opposite))
    flips = {}
    conflicts = 0
    for start in range(len(f)):
        if start in flips:
            continue
        flips[start] = False
        queue = deque([start])
        group = []
        while queue:
            i = queue.popleft()
            group.append(i)
            for j, opposite in adjacency[i]:
                value = flips[i] ^ opposite
                if j not in flips:
                    flips[j] = value
                    queue.append(j)
                elif flips[j] != value:
                    conflicts += 1
        # Open patches retain the majority of their authored orientation.
        if sum(flips[i] for i in group) > len(group) / 2:
            for i in group:
                flips[i] = not flips[i]
    inverted = np.array([flips[i] for i in range(len(f))])
    f[inverted] = f[inverted, ::-1]

    boundary = [linked[0] for linked in edges().values() if len(linked) == 1]
    following, incoming = defaultdict(list), defaultdict(list)
    for face, a, b in boundary:
        following[b].append((a, face))  # Cap edges run against the surface edge.
        incoming[a].append(b)
    remaining = {(b, a) for _, a, b in boundary}
    loops, skipped = [], 0
    while remaining:
        start, initial_end = min(remaining)
        vertices, adjacent = [], []
        current = start
        previous_face = next(face for end, face in following[start] if end == initial_end)
        visited = {}
        traversed = []
        while current not in visited:
            visited[current] = len(vertices)
            vertices.append(current)
            choices = [(end, face) for end, face in following[current] if (current, end) in remaining]
            if not choices:
                break
            # Touching authored strips can share a corner. Follow the local
            # source patch by face order rather than joining unrelated parts.
            nxt, face = min(choices, key=lambda entry: abs(entry[1] - previous_face))
            remaining.discard((current, nxt))
            traversed.append((current, nxt))
            adjacent.append(face)
            previous_face = face
            current = nxt
        else:
            cycle_start = visited[current]
            remaining.update(traversed[:cycle_start])
            vertices, adjacent = vertices[cycle_start:], adjacent[cycle_start:]
            if len(vertices) >= 3:
                xyz = p[vertices]
                center = xyz.mean(0)
                _, _, basis = np.linalg.svd(xyz - center, full_matrices=False)
                span = max(float(np.ptp(xyz, axis=0).max()), 1e-6)
                loops.append((vertices, adjacent, center, basis))
                continue
        skipped += 1

    groups = []
    for loop in loops:
        for group in groups:
            base = group[0]
            if abs(loop[3][2] @ base[3][2]) > .99999 and abs((loop[2] - base[2]) @ base[3][2]) < 1e-6:
                group.append(loop)
                break
        else:
            groups.append([loop])
    added_faces, added_slots = [], []
    for group in groups:
        center, basis = group[0][2], group[0][3].copy()
        contours = [(p[loop[0]] - center) @ basis[:2].T for loop in group]
        area = [float(np.sum(xy[:, 0] * np.roll(xy[:, 1], -1) - xy[:, 1] * np.roll(xy[:, 0], -1))) for xy in contours]
        # The largest contour sets the plane's outward direction. Nested holes
        # keep their opposite direction rather than being capped as solid disks.
        if area[int(np.argmax(np.abs(area)))] < 0:
            contours = [xy * [1, -1] for xy in contours]
        try:
            triangles = np.asarray(mf.triangulate(contours, allow_convex=False), int)
        except (ValueError, RuntimeError):
            skipped += len(group)
            continue
        indices = np.concatenate([loop[0] for loop in group])
        if len(triangles) == 0:
            skipped += len(group)
            continue
        cap = indices[triangles]
        tri = p[cap]
        area3 = np.linalg.norm(np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0]), axis=1)
        if np.any(area3 < 1e-12):
            skipped += len(group)
            continue
        adjacent = np.concatenate([loop[1] for loop in group])
        slot = Counter(slots[adjacent]).most_common(1)[0][0]
        added_faces.extend(cap)
        added_slots.extend([slot] * len(cap))
    if added_faces:
        f = np.vstack([f, added_faces])
        slots = np.concatenate([slots, added_slots])
    # Closed source parts sometimes arrived entirely inside-out. Correct those
    # independently; flipping the whole assembly would invert its good parts.
    linked_edges = edges()
    neighbors = defaultdict(set)
    for linked in linked_edges.values():
        indices = [entry[0] for entry in linked]
        for i in indices:
            neighbors[i].update(indices)
    unseen = set(range(len(f)))
    outward_parts = 0
    while unseen:
        queue = [unseen.pop()]
        group = []
        while queue:
            i = queue.pop()
            group.append(i)
            for j in neighbors[i]:
                if j in unseen:
                    unseen.remove(j)
                    queue.append(j)
        group_set = set(group)
        open_part = any(len(linked) == 1 and linked[0][0] in group_set for linked in linked_edges.values())
        volume = signed_volume(p, f[group])
        if not open_part and volume < -1e-12:
            f[group] = f[group, ::-1]
            outward_parts += 1
    result = kit.Piece(p, f, slots)
    report = {"removed_duplicate_faces": removed, "reoriented_faces": int(inverted.sum()),
              "added_cap_triangles": len(added_faces), "skipped_boundary_loops": skipped,
              "orientation_conflicts": conflicts, "outward_oriented_parts": outward_parts, "topology": inspect(p, f)}
    return result, report
