"""Overlay tiles that lie on a finished floor: rugs assembled from 4x4 pieces, quarter-turned in place.

Pieces are authored with their border on local -z (`edge`), -z and -x (`corner`), +-z (`runner`, which
runs along x) and +-z plus -x (`runner_end`). A rug is corners, edges and centers turned to face out;
a runner is two ends around any number of runner pieces. Because pieces turn, the field motif is
symmetric under quarter turns and repeats every seam, and border motifs are symmetric along their
band. The pattern is the carpet's own top faces cut and recolored, never shapes laid on top.
"""

import numpy as np

from core import kit
from items.solid import conform_surface
from tiles.geometry import block, box, rect

FLOOR_TOP = 0.05
# Which sides carry a border, as outward unit normals (x, z) in the piece's own frame.
BORDERS = {"center": [], "edge": [(0, -1)], "corner": [(0, -1), (-1, 0)],
           "runner": [(0, -1), (0, 1)], "runner_end": [(0, -1), (0, 1), (-1, 0)]}
FRINGED = {"runner_end": [(-1, 0)]}


def diamond(cx, cz, radius):
    return np.array([[cx + radius, cz], [cx, cz + radius], [cx - radius, cz], [cx, cz - radius]], float)


def edge_rect(side, a, b, lo, hi, half):
    """A band rectangle hugging border `side`, `a`..`b` in from that edge, spanning `lo`..`hi` along it."""
    nx, nz = side
    if nz:
        z0, z1 = sorted([nz * (half - a), nz * (half - b)])
        return rect(lo, z0, hi, z1)
    x0, x1 = sorted([nx * (half - a), nx * (half - b)])
    return rect(x0, lo, x1, hi)


def carpet(design, piece, variant, family):
    """One rug piece: a thin slab whose top is cut into field, band, stripe and motif regions."""
    half = 2.0
    inset, band, stripe = design.get("inset", .15), design.get("band", .6), design.get("stripe", .06)
    thick, motif = design.get("thickness", .025), design.get("motif", .3)
    sides = BORDERS[piece]
    lo = [-half + (inset if (-1, 0) in sides else 0), -half + (inset if (0, -1) in sides else 0)]
    hi = [half - (inset if (1, 0) in sides else 0), half - (inset if (0, 1) in sides else 0)]
    outline = rect(lo[0], lo[1], hi[0], hi[1])
    ends = [(-1, 0) in sides, (0, -1) in sides, (1, 0) in sides, (0, 1) in sides]
    # rect edges run bottom (-z), right (+x), top (+z), left (-x); only rug ends are beveled.
    slab = block(outline, [ends[1], ends[2], ends[3], ends[0]], FLOOR_TOP + thick, FLOOR_TOP - .01, "carpet_edge",
                 bevel=design.get("bevel", .012), gap=0.0)
    top = up(slab)

    def paint(piece_, polygons, slot, paintable):
        return kit.paint_cut(piece_, polygons, slot, paintable) if polygons else piece_

    # Field: diamonds on even lattice points and dots on odd ones, symmetric under quarter turns.
    lattice = [(i, j) for i in range(-2, 3) for j in range(-2, 3)]
    top = paint(top, [outline], "carpet", ["carpet_edge"])
    top = paint(top, [diamond(i, j, motif) for i, j in lattice if (i + j) % 2 == 0], "carpet_motif", ["carpet"])
    top = paint(top, [diamond(i, j, motif * .35) for i, j in lattice if (i + j) % 2], "carpet_dot", ["carpet"])
    if sides:
        inner = band + inset
        bands = [edge_rect(s, inset, inner, -half, half, half) for s in sides]
        top = paint(top, bands, "band", ["carpet", "carpet_motif", "carpet_dot"])
        stripes = []
        for s in sides:
            stripes += [edge_rect(s, inset, inset + stripe, -half, half, half),
                        edge_rect(s, inner - stripe, inner, -half, half, half)]
        top = paint(top, stripes, "stripe", ["band"])
        # Stripes cross the perpendicular band; the corner rosette below repaints that square.
        # Band motif: small diamonds every unit on half-units, so the spacing holds across seams.
        marks = []
        for s in sides:
            middle = half - inset - band / 2
            for t in np.arange(-half + .5, half, 1.0):
                cx, cz = (t, s[1] * middle) if s[1] else (s[0] * middle, t)
                marks.append(diamond(cx, cz, band * .22))
        top = paint(top, marks, "band_motif", ["band"])
        # Where two borders meet, the corner square is marked by its own rosette.
        for a in sides:
            for b in sides:
                if a[0] == 0 and b[1] == 0:
                    cx, cz = b[0] * (half - inset - band / 2), a[1] * (half - inset - band / 2)
                    top = paint(top, [rect(cx - band / 2 + stripe, cz - band / 2 + stripe, cx + band / 2 - stripe, cz + band / 2 - stripe)],
                                "band", ["band_motif", "stripe"])
                    top = paint(top, [diamond(cx, cz, band * .3)], "stripe", ["band"])
    pieces = [down(conform_surface(top))]
    for side in FRINGED.get(piece, []):
        pieces += fringe(side, lo, hi, design)
    return kit.Piece.merge(*pieces)


def fringe(side, lo, hi, design):
    """Tassels along a runner's end: short strands from under the carpet out to the tile edge."""
    strands, spacing, width = [], design.get("fringe_spacing", .09), design.get("fringe_width", .04)
    x_end = lo[0]
    for z in np.arange(lo[1] + spacing / 2, hi[1] - spacing / 4, spacing):
        strand = box(-2.0 + .005, FLOOR_TOP, z - width / 2, x_end + .05, FLOOR_TOP + .012, z + width / 2, "fringe")
        strands.append(strand)
    return strands


# paint_cut works in a front view (x, y) facing +z; carpets face +y, so paint with y and z swapped.
def up(piece):
    return kit.Piece(piece.verts[:, [0, 2, 1]], piece.faces[:, ::-1], piece.slots)


def down(piece):
    normals = None if piece.normals is None else piece.normals[:, ::-1][:, :, [0, 2, 1]]
    return kit.Piece(piece.verts[:, [0, 2, 1]], piece.faces[:, ::-1], piece.slots, normals=normals)


STYLES = {"carpet": carpet}
