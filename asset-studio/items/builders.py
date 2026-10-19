"""Small topology-first item builders, with color attached to the exterior surface.

Reserve separate geometry for actual shape changes. Surface markings, fat, and bone
use face/vertex colors on shared topology instead of stacked decorative solids.
"""

import math
import numpy as np


def steak():
    """One closed low-poly steak shell; the bone and fat are flush colored surface regions."""
    count = 20
    vertices, faces, colors = [], [], []
    meat, crust, fat, bone, marrow = (159, 55, 51), (105, 49, 34), (227, 198, 150), (245, 223, 182), (157, 109, 76)

    def ring(y, scale=1, center=(0, 0), radius=None):
        indices = []
        for i in range(count):
            angle = 2 * math.pi * i / count
            wobble = 1 + 0.10 * math.sin(3 * angle) - 0.07 * math.cos(2 * angle)
            x = (radius if radius else .30 * scale * wobble) * math.cos(angle)
            z = (radius if radius else .22 * scale * wobble) * math.sin(angle)
            indices.append(len(vertices))
            vertices.append((center[0] + x, y, center[1] + z))
        return indices

    lower = ring(.015, .90)
    side = ring(.055)
    top = ring(.105, .92)
    flesh = ring(.108, .80)
    bone_outer = ring(.108, center=(-.075, .015), radius=.050)
    bone_inner = ring(.108, center=(-.075, .015), radius=.023)

    def strip(outer, inner, color):
        for i in range(count):
            j = (i + 1) % count
            faces.extend([(outer[i], inner[i], inner[j]), (outer[i], inner[j], outer[j])])
            colors.extend([color, color])

    strip(lower, side, crust)
    strip(side, top, crust)
    strip(top, flesh, fat)
    strip(flesh, bone_outer, meat)
    strip(bone_outer, bone_inner, bone)
    for indices, y, color, reverse in [(bone_inner, .108, marrow, False), (lower, .015, crust, True)]:
        center = len(vertices)
        points = np.array([vertices[i] for i in indices])
        vertices.append((float(points[:, 0].mean()), y, float(points[:, 2].mean())))
        for i in range(count):
            j = (i + 1) % count
            faces.append((center, indices[i], indices[j]) if reverse else (center, indices[j], indices[i]))
            colors.append(color)
    return vertices, faces, colors


BUILDERS = {"steak": steak}
