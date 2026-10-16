"""
Renders every variant of the accessory scripts on simple mannequin hosts into one contact sheet for review:

    python accessories/catalog.py belts crowns pauldrons -o catalog.png

Each tile shows the accessory flat-shaded in its demo colors (`kit.DEMO_COLORS`) from the front three-quarter view,
on a grey stand-in of the body part it sits on.
"""

import argparse
import importlib
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import monster_image3d as m3  # noqa: E402
from accessories import kit  # noqa: E402

HOST_COLOR = (92, 96, 108)
BACKGROUND = (30, 32, 40)


class EllipseHost:
    """A mannequin body part: elliptic cross-sections whose half widths `a(y)` (across) and `b(y)` (deep) are
    interpolated from `rows` = [(y, a, b), ...], centred on the y axis."""

    def __init__(self, rows, p=2.2):
        self.rows, self.p = np.asarray(rows, float), p

    def section(self, y):
        y = np.atleast_1d(np.asarray(y, float))
        r = self.rows
        return (np.zeros(len(y)), np.interp(y, r[:, 0], r[:, 1]), np.zeros(len(y)), np.interp(y, r[:, 0], r[:, 2]))

    def mesh(self, y0=None, y1=None, sides=24, steps=16):
        lo, hi = self.rows[0, 0] if y0 is None else y0, self.rows[-1, 0] if y1 is None else y1
        rings = [kit.ring_points(self, y, -0.002, sides) for y in np.linspace(lo, hi, steps)]
        verts, faces = kit._loft(rings)
        return kit.Piece(verts, faces, "host")


def head_host(cy=1.75, rx=0.42, ry=0.44, rz=0.40):
    """A KayKit-sized head: an ellipsoid of radii `rx`, `ry`, `rz` around height `cy`."""
    ys = np.linspace(cy - ry, cy + ry, 25)
    k = np.sqrt(np.clip(1 - ((ys - cy) / ry) ** 2, 0.0, 1.0))
    return EllipseHost(np.column_stack([ys, np.maximum(rx * k, 1e-3), np.maximum(rz * k, 1e-3)]))


WAIST = EllipseHost([(0.62, 0.30, 0.22), (0.80, 0.31, 0.23), (0.95, 0.32, 0.24), (1.10, 0.36, 0.26), (1.30, 0.40, 0.27)])
HEAD = head_host()
ARM = EllipseHost([(0.0, 0.12, 0.12), (0.45, 0.11, 0.11)])  # along the arm (rotated onto x in `shoulder_stand`)


def shoulder_stand():
    """The right shoulder region: a torso top and the right arm reaching out along -x (KayKit's T-pose)."""
    torso = EllipseHost([(0.85, 0.34, 0.24), (1.10, 0.38, 0.26), (1.22, 0.30, 0.22)]).mesh()
    arm = ARM.mesh()
    v = arm.verts.copy()
    arm = kit.Piece(np.column_stack([-(0.30 + v[:, 1]), 1.10 + v[:, 0], v[:, 2]]), arm.faces[:, ::-1], "host")
    return kit.Piece.merge(torso, arm)


def render(piece, host=None, size=360, yaw=-30.0, pitch=12.0, colors=None, background=BACKGROUND, shading=True):
    """A flat-shaded image of `piece` (and a grey `host`) from the front, turned by `yaw` and tilted by `pitch`
    (`shading` false: each slot's exact color, for masks)."""
    colors = colors or kit.DEMO_COLORS
    parts = [piece] + ([host] if host is not None else [])
    allp = kit.Piece.merge(*parts)
    a, b = np.radians(yaw), np.radians(pitch)
    Ry = np.array([[np.cos(a), 0, np.sin(a)], [0, 1, 0], [-np.sin(a), 0, np.cos(a)]])
    Rx = np.array([[1, 0, 0], [0, np.cos(b), -np.sin(b)], [0, np.sin(b), np.cos(b)]])
    R = Rx @ Ry
    V = allp.verts @ R.T
    lo, hi = piece.verts.min(0), piece.verts.max(0)  # framed unturned, so every view of a piece has the same scale
    span = (hi - lo).max() * 1.25
    centre = (0.5 * (lo + hi)) @ R.T
    tri = V[allp.faces]
    n = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)
    light = np.array([-0.35, 0.55, 0.75]) / np.linalg.norm([-0.35, 0.55, 0.75])
    shade = 0.55 + 0.45 * np.clip(n @ light, 0.0, 1.0) if shading else np.ones(len(n))
    base = np.array([colors.get(s, HOST_COLOR) if s != "host" else HOST_COLOR for s in allp.slots], float)
    col = np.clip(base * shade[:, None], 0, 255)
    flat_v = tri.reshape(-1, 3)
    px = np.column_stack([(flat_v[:, 0] - centre[0]) / span * size + size / 2,
                          size / 2 - (flat_v[:, 1] - centre[1]) / span * size])
    faces = np.arange(len(flat_v)).reshape(-1, 3)
    z, img = m3._rasterize(px, flat_v[:, 2], np.repeat(col, 3, axis=0), faces, (size, size))
    out = np.where(np.isfinite(z)[..., None], img, background)
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8))


def sheet(tiles, columns=4, size=360):
    """Tiles `[(title, image), ...]` in a grid with their titles."""
    rows = (len(tiles) + columns - 1) // columns
    out = Image.new("RGB", (columns * size, rows * (size + 30)), (20, 22, 28))
    d = ImageDraw.Draw(out)
    for i, (title, im) in enumerate(tiles):
        x, y = (i % columns) * size, (i // columns) * (size + 30)
        d.text((x + 8, y + 6), title, fill=(230, 230, 230), font_size=18)
        out.paste(im, (x, y + 30))
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("types", nargs="+", help="accessory scripts, e.g. belts crowns pauldrons")
    ap.add_argument("-o", "--out", default="catalog.png")
    ap.add_argument("--size", type=int, default=360)
    ap.add_argument("--yaws", default="-30", help="view angles per variant, comma-separated (0 = front)")
    args = ap.parse_args()
    yaws = [float(v) for v in args.yaws.split(",")]
    tiles = []
    for name in args.types:
        mod = importlib.import_module(f"accessories.{name}")
        for variant, piece, host, *colors in mod.catalog():  # a 4th item: the variant's own colors
            for yaw in yaws:
                tiles.append((f"{name}: {variant}", render(piece, host, size=args.size, yaw=yaw,
                                                           colors=colors[0] if colors else None)))
    sheet(tiles, columns=max(4, len(yaws)), size=args.size).save(args.out)
    print(f"{len(tiles)} tiles -> {args.out}")


if __name__ == "__main__":
    main()
