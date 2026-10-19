"""Faceted sliced bread with carved scores and flush crust/crumb color regions."""

import numpy as np
import manifold3d as mf
from core import kit
from items.solid import Assembly

ROLL_RINGS = [(-.40, .16), (-.37, .58), (-.29, .85), (-.20, .96),
              (-.12, 1), (-.035, .99), (.045, .98), (.105, .96), (.105, .80)]
SCORES = [-.205, -.065]


def _score(z_center):
    """A shallow tapered carving follows the oval crown instead of chopping flat wedges."""
    sections = []
    angles = np.linspace(0, np.pi, 9)
    for x in np.linspace(-.185, .185, 11):
        z = z_center - .28 * x
        scale = np.interp(z, *np.array(ROLL_RINGS[:-1]).T)
        crown = np.interp(x, (.215 * scale * np.cos(angles))[::-1],
                          (.15 * (1 + scale * np.sin(angles)))[::-1])
        taper = max(.08, 1 - (abs(x) / .19) ** 4)
        sections.append(np.array([(x, crown + .03, z - .05 * taper),
                                  (x, crown - .018 * taper, z),
                                  (x, crown + .03, z + .05 * taper)]))
    v, f = kit._loft(sections)
    v, f = kit._cap_loops(v, f, [list(range(3)), list(range(30, 33))])
    return mf.Manifold(mf.Mesh(np.asarray(v, np.float32),
                              np.asarray(kit._orient_closed(v, f), np.uint32)))


def _loaf(rings, width, height):
    """Loft closed cross-sections along Z, retaining broad intentional facets."""
    angles = np.arange(16) * 2 * np.pi / 16
    sections = []
    for z, scale in rings:
        x = width * np.cos(angles) * scale
        # A baked roll rests on a broad flat base. Flatten the profile before
        # scaling each ring so the inset cut rim retains a real thickness.
        y = height * (1 + np.maximum(np.sin(angles), -.70) * scale)
        sections.append(np.column_stack([x, y, np.full(16, z)]))
    v, f = kit._loft(sections)
    v, f = kit._cap_loops(v, f, [list(range(16)), list(range((len(rings) - 1) * 16, len(rings) * 16))])
    f = kit._orient_closed(v, f)
    return mf.Manifold(mf.Mesh(np.asarray(v, np.float32), np.asarray(f, np.uint32)))


def _paint(shape, cut_z, grooves=False):
    """Assign colors on the actual exterior, including cut and recessed surfaces."""
    mesh = shape.to_mesh()
    p, f = np.asarray(mesh.vert_properties[:, :3]), np.asarray(mesh.tri_verts)
    tri = p[f]
    center = tri.mean(1)
    normal = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    normal /= np.maximum(np.linalg.norm(normal, axis=1, keepdims=True), 1e-12)
    slots = np.full(len(f), "bread_crust", dtype=object)
    # Exposed crumb is inset within a crust rim on each cut end.
    radial = (center[:, 0] / .215) ** 2 + ((center[:, 1] - .15) / .15) ** 2
    cut = (center[:, 2] > cut_z - .035) & (radial < .82 ** 2)
    slots[cut] = "bread_crumb"
    if grooves:
        for z in SCORES:
            in_score = np.abs(center[:, 2] + .28 * center[:, 0] - z) < .035
            carved = np.abs(normal[:, 2] + .28 * normal[:, 0]) > .50
            slots[in_score & carved & (center[:, 1] > .21)] = "bread_score"
    return kit.Piece(p, f, slots)


def _lean_slice(vertices, footprint_scale, height_scale, ground):
    """Size the slice first, then rigidly rotate it so its rim stays square to its face."""
    dimensions = np.array([footprint_scale, height_scale, footprint_scale])
    vertices = (np.asarray(vertices) - [0, .0492, .105]) * dimensions
    rotation = kit.rotation([1, 0, 0], -25)
    vertices = vertices @ rotation.T
    vertices[:, 1] += ground - vertices[:, 1].min()
    vertices[:, 0] += .14 * footprint_scale
    # Place the rear upper surface against the actual cut face, with a small
    # natural overlap for solid union rather than an extra connecting piece.
    angle = np.radians(25)
    vertices[:, 2] += (.105 * footprint_scale + .035 * footprint_scale * np.cos(angle)
                      + .17 * height_scale * np.sin(angle) - .008 * footprint_scale)
    return vertices


def sliced_roll(footprint_scale=1, height_scale=1):
    """A scored roll and leaning slice meet naturally at their lower contact patch."""
    if not np.isfinite(footprint_scale) or footprint_scale <= 0:
        raise ValueError("Bread footprint scale must be finite and positive")
    if not np.isfinite(height_scale) or height_scale <= 0:
        raise ValueError("Bread height scale must be finite and positive")
    body = _loaf(ROLL_RINGS, .215, .15)
    for z in SCORES:
        body = body - _score(z)
    for x, y, radius in [(-.08, .17, .023), (.065, .205, .029), (.03, .085, .022), (-.10, .105, .014)]:
        body = body - mf.Manifold.sphere(radius, 8).scale((1, .80, .65)).translate((x, y, .108))
    main = _paint(body, .105, grooves=True)

    slice_shape = _loaf([(.07, .89), (.077, .96), (.13, .96), (.14, .89), (.14, .80)], .215, .15)
    for x, y, radius in [(-.06, .18, .025), (.07, .12, .020), (.035, .235, .017)]:
        slice_shape = slice_shape - mf.Manifold.sphere(radius, 8).scale((1, .8, .65)).translate((x, y, .142))
    sliced = _paint(slice_shape, .14)
    # Apply the exaggerated dimensions before posing. Stretching a tilted slice
    # afterwards would skew its rim just as shearing the slice would.
    main.verts *= [footprint_scale, height_scale, footprint_scale]
    sliced.verts = _lean_slice(sliced.verts, footprint_scale, height_scale, main.verts[:, 1].min())
    return Assembly().add(main).add(sliced).build()
