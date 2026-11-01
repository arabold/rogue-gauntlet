"""Floor inlays: KayKit's own grate tiles (CC0) holding still sewer water in place of their bars.

A channel (4x2, `floor_tile_grate`'s footprint) and a pool (4x4, `floor_tile_big_grate`'s) keep the source
tile's granite rim and shaft exactly, so they sit on the floor grid where the grates do and the game lays
them with the same inlay rules, runs included. The bars are dropped; a still body of water fills the
shaft to just under the rim. The game draws its surface with the water shader, which turns murky down
the shaft and pales where it meets the stone. Variants after the first float a few scum islands and
bubbles, so runs of one piece do not repeat exactly.
"""

import manifold3d as mf
import numpy as np

from core import kit
from items.furnishing_kit import floor_plan
from tiles import dressing, kaykit
from tiles.geometry import rng

SOURCES = {"channel": "floor_tile_grate_open", "pool": "floor_tile_big_grate_open"}


def source(design, piece):
    return design.get("source", {}).get(piece, SOURCES[piece])


def shaft(mesh, level):
    """The opening of a grate's shaft at height `level`: the hull of where its inner walls cross that plane."""
    tri = mesh.verts[mesh.faces]
    points = []
    for a, b, c in tri:
        for p, q in ((a, b), (b, c), (c, a)):
            if (p[1] - level) * (q[1] - level) < 0:
                points.append(p + (q - p) * (level - p[1]) / (q[1] - p[1]))
    points = np.array(points)[:, [0, 2]]
    # The tile's outer skirts cross the plane too; the shaft is the loop nearest the middle.
    half = np.abs(mesh.verts[:, [0, 2]]).max(0)
    inner = points[np.all(np.abs(points) < half - .06, axis=1)]
    return mf.CrossSection([inner]).hull()


def water(design, piece, variant, family):
    """The source grate without its bars, its shaft filled with water to `level`."""
    mesh, _ = kaykit.load(source(design, piece))
    labels = dressing.label_of_faces(mesh)
    areas = np.linalg.norm(np.cross(*(mesh.verts[mesh.faces[:, k]] - mesh.verts[mesh.faces[:, 0]] for k in (1, 2))), axis=1)
    rim = labels == max(np.unique(labels), key=lambda label: areas[labels == label].sum())
    used, faces = np.unique(mesh.faces[rim], return_inverse=True)
    stone = kit.Piece(mesh.verts[used], faces.reshape(-1, 3), mesh.slots[rim], normals=mesh.normals[rim], uv_t=mesh.uv_t[rim])
    level, depth = design.get("level", -.06), design.get("depth", .3)
    # The water tucks a hair into the shaft walls so no gap shows along them.
    opening = shaft(mesh, level).offset(.012, mf.JoinType.Miter)
    bodies = [(floor_plan(opening, depth, level - depth), "water")]
    r = rng(family, variant, piece, "water")
    if variant != "A":
        inner = opening.offset(-.3, mf.JoinType.Round)
        (x0, z0), (x1, z1) = inner.bounds()[:2], inner.bounds()[2:]
        for _ in range(int(r.integers(2, 5))):
            x, z, size = r.uniform(x0, x1), r.uniform(z0, z1), r.uniform(.12, .26)
            blob = mf.CrossSection.circle(size, 9).scale((1, r.uniform(.5, .9))).rotate(r.uniform(0, 180)).translate((x, z))
            bodies.append((floor_plan(blob ^ inner, .032, level - .02), "scum"))
        for _ in range(int(r.integers(2, 4))):
            x, z, size = r.uniform(x0, x1), r.uniform(z0, z1), r.uniform(.04, .07)
            bodies.append((mf.Manifold.sphere(1, 8).scale((size, size * .7, size)).translate((x, level, z)), "scum"))
    pieces = []
    for body, slot in bodies:
        mesh = body.to_mesh()
        pieces.append(kit.Piece(np.asarray(mesh.vert_properties[:, :3], float), np.asarray(mesh.tri_verts), slot))
    return kit.Piece.merge(stone, *pieces)


STYLES = {"water": water}
