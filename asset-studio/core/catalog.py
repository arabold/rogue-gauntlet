#!/usr/bin/env python3
"""
Render a contact sheet of every variant of a prefab category, on a neutral stand-in host (`WAIST`, `HEAD`,
`shoulder_stand`) so the silhouette and proportions are easy to judge from several angles:

    .venv/bin/python asset-studio/core/catalog.py belts -o belts.png
    .venv/bin/python asset-studio/core/catalog.py all -o accessories.png
"""

import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

STUDIO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if STUDIO_ROOT not in sys.path:
    sys.path.insert(0, STUDIO_ROOT)

from core import image3d as m3  # noqa: E402
from core import kit  # noqa: E402


HOST_COLOR = (92, 96, 108)
BACKGROUND = (30, 32, 40)


class StandIn:
    """
    A piecewise-linear host with `section(y) -> (cx, a, cz, b)` and `p`, plus `mesh(y0, y1)` for a faint grey
    mannequin slice to show the accessory in place.
    """

    def __init__(self, rows, p=2.2):
        rows = np.asarray(rows, float)
        self.ys, self.a, self.b = rows[:, 0], rows[:, 1], rows[:, 2]
        self.p = p

    def section(self, y):
        y = np.atleast_1d(np.asarray(y, float))
        return np.zeros_like(y), np.interp(y, self.ys, self.a), np.zeros_like(y), np.interp(y, self.ys, self.b)

    def mesh(self, y0=None, y1=None, rings=10, sides=24):
        y0 = float(self.ys[0] if y0 is None else y0)
        y1 = float(self.ys[-1] if y1 is None else y1)
        return kit.host_band(self, y0, y1, 0.0, "host", bevel=0.0, sides=sides)


EllipseHost = StandIn


def head_host(cy=1.75, rx=0.42, ry=0.44, rz=0.40):
    """A stylized-proportioned head stand-in: an ellipsoid of radii `rx`, `ry`, `rz` around height `cy`."""
    ys = np.linspace(cy - ry, cy + ry, 25)
    k = np.sqrt(np.clip(1 - ((ys - cy) / ry) ** 2, 0.0, 1.0))
    return StandIn(np.column_stack([ys, np.maximum(rx * k, 1e-3), np.maximum(rz * k, 1e-3)]))


# A mannequin waist (hips at 0.55 to lower chest at 1.25; the narrowest row is the waist at y = 0.90) and a
# round head (neck at 1.38 to crown at 2.10, widest at 1.75).
WAIST = StandIn([(0.55, 0.30, 0.21), (0.72, 0.27, 0.19), (0.90, 0.24, 0.18), (1.08, 0.28, 0.20), (1.25, 0.31, 0.22)])
HEAD = head_host()


def shoulder_stand():
    """A faint half-chest and upper arm under the right shoulder at (-0.38, 1.33, 0), for the pauldron catalog."""
    chest = kit.bevel_box((0.46, 0.48, 0.34), 0.06, "host", center=(-0.15, 1.12, 0.0))
    arm = kit.tube([(-0.36, 1.22, 0.0), (-0.72, 1.22, 0.0)], 0.12, "host", sides=14)
    return kit.Piece.merge(chest, arm)


def render(piece, host=None, yaw=25.0, pitch=14.0, roll=0.0, size=240, extent=None, center=None, colors=None,
           crease=50.0, zoom=1.0, pan=(0.0, 0.0), gradient_strength=0.68, background=(245, 245, 247), shading=True):
    """
    Z-buffered orthographic render of `piece` (and an optional `host` in pale grey) at `(yaw, pitch, roll)` degrees,
    lit from upper-front-left with soft top-down vertical gradient ramps, smooth normals (`crease` degrees; 0 = flat
    facets), and specular highlights on metallic/glossy slots (`eye`, `metal`, `armor`, `gold`, `gem`).
    """
    from core.export import DEFAULT_MATERIALS, corner_normals, resolve_palette
    from core.mesh import Atlas

    # Stand-in hosts are neutral context and need no character palette.
    neutral_host = kit.Piece(host.verts, host.faces, "__catalog_host", normals=host.normals) if host is not None else None
    full = kit.Piece.merge(neutral_host, piece) if neutral_host is not None else piece
    if not len(full.faces):
        return Image.new("RGB", (size, size), tuple(background))
    R = kit.rotation([0, 0, 1], roll) @ kit.rotation([1, 0, 0], pitch) @ kit.rotation([0, 1, 0], yaw)
    v = full.verts @ R.T
    c = full.verts.mean(0) if center is None else np.asarray(center, float)
    vc = v - c @ R.T
    if pan != (0.0, 0.0):
        vc = vc - np.array([pan[0], pan[1], 0.0])
    span = float(np.ptp(vc[:, :2], axis=0).max()) * 1.25 if extent is None else extent
    span = span / max(zoom, 1e-4)
    k = size / max(span, 1e-6)

    N_world = full.normals if getattr(full, "normals", None) is not None else corner_normals(full.verts, full.faces, crease=crease)
    n = N_world @ R.T
    flip = n[..., 2] < 0
    n = np.where(flip[..., None], -n, n)
    light = np.array([-0.35, 0.55, 0.76])
    light /= np.linalg.norm(light)
    ndotl = np.clip(n @ light, 0, 1)
    shade = 0.48 + 0.54 * ndotl

    palette = {**kit.DEMO_COLORS, "__catalog_host": (186, 188, 192), **(colors or {})}
    used = sorted({str(s) for s in full.slots})
    slot_colors, looks = resolve_palette(used, palette)
    atlas = Atlas([slot_colors[s] for s in used], pad=4, col_w=16, col_h=128, looks=looks, slot_names=used)
    atlas_arr = np.asarray(atlas.image, float)
    slot_idx = {s: i for i, s in enumerate(used)}

    pos_world = full.verts[full.faces].reshape(-1, 3)
    nrm_world = N_world.reshape(-1, 3)
    y_min, y_max = float(full.verts[:, 1].min()), float(full.verts[:, 1].max())
    y_span = max(y_max - y_min, 0.15)
    light_world = np.array([0.22, 0.85, 0.48], float)
    light_world /= np.linalg.norm(light_world)

    slot_ids = np.repeat([slot_idx[str(s)] for s in full.slots], 3)
    full_uv_t = getattr(full, "uv_t", None)
    if gradient_strength > 0:
        ndotl_w = np.clip(nrm_world @ light_world, -1.0, 1.0)
        t_norm = 0.5 - 0.36 * ndotl_w
        t_height = 1.0 - np.clip((pos_world[:, 1] - y_min) / y_span, 0.0, 1.0)
        t_grad = 0.5 + gradient_strength * ((0.65 * t_norm + 0.35 * t_height) - 0.5)
        if full_uv_t is not None:
            has_uv = np.isfinite(full_uv_t.ravel())
            if has_uv.any():
                t_grad = np.where(has_uv, full_uv_t.ravel(), t_grad)
        uv = atlas.gradient_uv(slot_ids, t_grad)
    else:
        if full_uv_t is not None and np.isfinite(full_uv_t).any():
            has_uv = np.isfinite(full_uv_t.ravel())
            t_grad = np.where(has_uv, full_uv_t.ravel(), 0.5)
            uv = atlas.gradient_uv(slot_ids, t_grad)
        else:
            uv = np.repeat([atlas.flat_uv(slot_idx[str(s)]) for s in full.slots], 3, axis=0)

    px_x = np.clip((uv[:, 0] * atlas.size_w).astype(int), 0, atlas.size_w - 1)
    px_y = np.clip((uv[:, 1] * atlas.size_h).astype(int), 0, atlas.size_h - 1)
    base = atlas_arr[px_y, px_x].reshape(-1, 3, 3)

    # Specular reflection (Blinn-Phong) so glossy eye beads, gems, and metals catch a crisp light reflection
    half_v = light + np.array([0.0, 0.0, 1.0])
    half_v /= np.linalg.norm(half_v)
    ndoth = np.clip(n @ half_v, 0.0, 1.0)

    rough = np.array([DEFAULT_MATERIALS.get(str(s), {}).get("roughness", 0.85) for s in full.slots])[:, None]
    metal = np.array([DEFAULT_MATERIALS.get(str(s), {}).get("metallic", 0.0) for s in full.slots])[:, None]
    emiss = np.array([DEFAULT_MATERIALS.get(str(s), {}).get("emissive", 0.0) for s in full.slots])[:, None]

    shininess = np.where(rough < 0.2, 42.0, np.where(rough < 0.45, 16.0, 6.0))
    spec_str = np.where(rough < 0.2, 0.60, np.where(rough < 0.45, 0.26 + 0.22 * metal, 0.0))
    spec = (ndoth ** shininess) * spec_str

    if shading:
        lit_col = base * shade[..., None] + 255.0 * spec[..., None]
        col = np.where((emiss > 0.3)[..., None], np.maximum(lit_col, base * np.minimum(emiss[..., None], 1.15)), lit_col)
    else:
        raw = np.array([slot_colors[str(s)] for s in full.slots], float)[:, None, :]
        col = np.repeat(raw, 3, axis=1)
    col = np.clip(col, 0, 255).reshape(-1, 3)

    flat_v = vc[full.faces].reshape(-1, 3)
    faces = np.arange(len(flat_v)).reshape(-1, 3)
    px = np.column_stack([0.5 * size + k * flat_v[:, 0], 0.5 * size - k * flat_v[:, 1]])
    z, img = m3._rasterize(px, flat_v[:, 2], col, faces, (size, size))
    bg = np.asarray(background, dtype=np.uint8)
    out = np.where(np.isfinite(z)[..., None], img, bg).astype(np.uint8)
    return Image.fromarray(out)


def sheet(items, yaws=(0, 32, 90, 180), pitch=14.0, cell=230):
    """Contact sheet: one row per `(name, piece, host[, colors])`, one column per yaw angle."""
    items = [(it[0], it[1], it[2], it[3] if len(it) > 3 else None) for it in items]
    spans = []
    for _, piece, host, _ in items:
        full = kit.Piece.merge(host, piece) if host is not None else piece
        spans.append(np.linalg.norm(np.ptp(full.verts, axis=0)) * 0.85 if len(full.faces) else 1.0)
    extent = max(spans)
    label_w = 130
    w, h = label_w + len(yaws) * cell, len(items) * cell
    canvas = Image.new("RGB", (w, h), (245, 245, 247))
    draw = ImageDraw.Draw(canvas)
    font = ImageFont.load_default()
    for row, (name, piece, host, colors) in enumerate(items):
        c = (kit.Piece.merge(host, piece) if host is not None else piece).verts.mean(0)
        draw.text((12, row * cell + cell // 2 - 6), f"{name}\n({len(piece.faces)} tris)", fill=(40, 42, 48), font=font)
        for col, yaw in enumerate(yaws):
            im = render(piece, host, yaw=yaw, pitch=pitch, size=cell - 8, extent=extent, center=c, colors=colors)
            canvas.paste(im, (label_w + col * cell + 4, row * cell + 4))
    return canvas


def _get_modules():
    from prefabs import anatomy, details, items, library, outfits
    from prefabs.anatomy import appendages, bodies, heads
    from prefabs.creatures import characters, golems
    from prefabs.details import fasteners, trophies, wear
    from prefabs.items import props, shields, weapons
    from prefabs.outfits import armor, belts, clothes, crowns, cuirass, helmets, pauldrons

    return {
        # Domains
        "anatomy": anatomy,
        "outfits": outfits,
        "items": items,
        "details": details,
        # Anatomy sub-parts
        "heads": heads,
        "bodies": bodies,
        "body": bodies,
        "appendages": appendages,
        # Outfits sub-parts
        "helmets": helmets,
        "cuirass": cuirass,
        "armor": armor,
        "pauldrons": pauldrons,
        "belts": belts,
        "crowns": crowns,
        "clothes": clothes,
        # Items sub-parts
        "weapons": weapons,
        "shields": shields,
        "props": props,
        # Details sub-parts
        "fasteners": fasteners,
        "trophies": trophies,
        "wear": wear,
        "trinkets": details,
        # Meshes & creatures
        "library": library,
        "characters": characters,
        "golems": golems,
    }


def main():
    mods = _get_modules()
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("which", choices=[*mods, "all"])
    ap.add_argument("-o", "--out", required=True, help="Output PNG.")
    ap.add_argument("--yaws", default="0,32,90,180", help="Comma-separated yaw angles in degrees.")
    ap.add_argument("--only", help="Comma-separated item names to render (default: all).")
    args = ap.parse_args()
    yaws = [float(a) for a in args.yaws.split(",")]
    only = {s.strip() for s in args.only.split(",")} if args.only else None
    chosen = mods.values() if args.which == "all" else [mods[args.which]]
    items = [it for m in chosen for it in m.catalog() if not only or it[0] in only]
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    sheet(items, yaws=yaws).save(args.out)
    print(f"wrote {args.out} ({len(items)} variants)")


if __name__ == "__main__":
    main()
