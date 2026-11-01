"""Pools: water sunk into the floor, laid like rugs, with KayKit quay walls toward the floor around it.

A pool is a rectangle of whole 4x4 tiles cut out of the floor and filled with these pieces, turned the way
rug pieces turn (`overlays.BORDERS`): open water in the middle, a quay wall along every side that meets
floor. The walls are KayKit's floor foundation (`floor_foundation_front`, CC0) at pool depth: its coping
and plinth lips standing proud of a recessed face, its own accent bricks, and its wall swatch with the
same gradient from a light coping to a dark foot. The water's surface lies `drop` under the floor top
over a bed `depth` further down. The game draws the surface with its water shader, which turns murky
with depth; the exported material is only a translucent stand-in.
"""

import manifold3d as mf
import numpy as np

from core import kit
from tiles import dressing, kaykit, overlays
from tiles.geometry import box, rng

HALF = 2.0
TOP = overlays.FLOOR_TOP
SOURCE = "floor_foundation_front"
WALL = "kaykit_00"
# The foundation's front, measured: plinth (0-.2) and coping (top .2) stand .1 proud of a face recessed between
# .1 bevels, and its swatch runs from .72 at the foot to .28 at the coping's top.
HEIGHTS = (0, .2, .3, -.3, -.2, 0)
SHADES = (.72, .68, .65, .35, .32, .28)


def section(height, thick):
    """The wall's cross-section in (out from its back, up from its foot)."""
    lip = thick + .1
    return [(0, 0), (lip, 0), (lip, .2), (thick, .3), (thick, height - .3), (lip, height - .2), (lip, height), (0, height)]


def turn(side):
    """Degrees about Y that bring a wall authored on the -z side to `side` (an outward x, z normal)."""
    return float(np.degrees(np.arctan2(-side[0], -side[1])))


def turned(piece, degrees):
    """A piece turned about Y, its authored corner normals with it."""
    rotation = kit.rotation((0, 1, 0), degrees)
    normals = None if piece.normals is None else np.asarray(piece.normals) @ rotation.T
    return kit.Piece(piece.verts @ rotation.T, piece.faces, piece.slots, normals=normals, uv_t=piece.uv_t)


def bricks():
    """KayKit's accent bricks off the foundation's face, each closed at its back, their backs at z = 0."""
    mesh, _ = kaykit.load(SOURCE)
    labels = dressing.label_of_faces(mesh)
    out = []
    for label, (lo, hi) in dressing.accents(mesh, labels).items():
        keep = labels == label
        used, faces = np.unique(mesh.faces[keep], return_inverse=True)
        brick = dressing.capped(kit.Piece(mesh.verts[used], faces.reshape(-1, 3), mesh.slots[keep], normals=mesh.normals[keep],
                                          uv_t=mesh.uv_t[keep]))
        center = (lo + hi) / 2
        out.append(kit.Piece(brick.verts - [center[0], center[1], lo[2]], brick.faces, brick.slots, normals=brick.normals,
                             uv_t=brick.uv_t))
    return out


def quay(design, piece, variant, family):
    """One pool piece: bed and water across the tile, a quay wall on each of its bordered sides."""
    drop, depth, thick = design.get("drop", .6), design.get("depth", 1.0), design.get("thickness", .3)
    level = TOP - drop
    floor = level - depth
    # The walls' foot sinks a little into the bed, so no wall edge lies on a bed edge.
    foot = floor - .05
    height = TOP - foot
    r = rng(family, variant, piece, "quay")
    parts = [box(-HALF, floor - .1, -HALF, HALF, floor, HALF, "bed"), box(-HALF, level - .02, -HALF, HALF, level, HALF, "water")]
    sides = overlays.BORDERS[piece]
    if sides:
        prism = mf.Manifold.extrude(mf.CrossSection([np.array(section(height, thick))]), 2 * HALF)
        # Extruded along +z from (out, up); turned so the run lies along x and the back on z = -HALF.
        wall = prism.rotate((0, -90, 0)).translate((HALF, foot, -HALF))
        walls = mf.Manifold.batch_boolean([wall.rotate((0, turn(side), 0)) for side in sides], mf.OpType.Add)
        mesh = walls.to_mesh()
        stone = kit.Piece(np.asarray(mesh.vert_properties[:, :3], float), np.asarray(mesh.tri_verts), WALL)
        marks = [h if h >= 0 else height + h for h in HEIGHTS]
        stone.uv_t = np.interp(stone.verts[stone.faces][:, :, 1] - foot, marks, SHADES)
        parts.append(stone)
        shapes = bricks()
        rows = [level + .14, level - .32, level - .7]
        for side in sides:
            for _ in range(int(r.integers(4, 7))):
                along, y = r.uniform(-HALF + .55, HALF - .55), rows[int(r.integers(len(rows)))]
                brick = shapes[int(r.integers(len(shapes)))]
                at = np.array([along, y, -HALF + thick - .01])
                parts.append(turned(kit.Piece(brick.verts + at, brick.faces, brick.slots, normals=brick.normals, uv_t=brick.uv_t),
                                    turn(side)))
    return kit.Piece.merge(*parts)


STYLES = {"quay": quay}
