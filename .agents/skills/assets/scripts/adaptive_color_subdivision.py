#!/usr/bin/env python3
"""
Adaptive Color-Boundary Subdivision -- Phase 2 of the two-phase monster-part pipeline.

Phase 1 sculpts *form only*: a clean, colorless low-poly base mesh (lofted squircles, KayKit limbs, ...).
Phase 2 (this module) transfers a high-fidelity reference image onto that form as flat-shaded swatches:

  2A. `quantize_part_cell`   Quantize one sheet cell to the monster's swatch palette: no dithering, a
                             per-part allowed palette subset, majority despeckle and small-island removal,
                             so the result is a crisp, high-contrast label map.
  2B. `RowSpanProjector`     Project label-map colors onto the 3D surface (front/back orthographic). Each
                             image row's silhouette span is fitted to the mesh width at that height, so form
                             and image line up even when their silhouettes differ (ears, flaps).
  2C. `subdivide_adaptive`   Recursively split *only* triangles whose surface crosses a color boundary.
                             Splits are conforming (red/green, no T-junctions). In `snap` mode, split points
                             are binary-searched onto the color boundary, so new edges follow the contour
                             instead of stair-stepping across it.
  2D. `carve_labels`         Optional: push labelled regions (mouth, sockets) inward along vertex normals,
                             i.e. carve the form along the color contours instead of stacking geometry.

Because every vertex 2C inserts lies on an original planar face, Phase-1 form and flat shading are preserved
exactly; only 2D changes geometry, and only where asked.

Requires numpy. Run with the repo venv:
    .venv/bin/python .agents/skills/assets/scripts/adaptive_color_subdivision.py [--out-dir DIR]
"""

import argparse
import math
import os
import sys
import time
from collections import deque

import numpy as np
from PIL import Image, ImageDraw, ImageEnhance, ImageFont

BG = -1  # Label of background pixels (outside the part silhouette).


# ---------------------------------------------------------------------------
# 2A. Palette quantization
# ---------------------------------------------------------------------------
def srgb_to_lab(rgb):
    """Converts sRGB `(..., 3)` 0-255 values to CIE L*a*b* (D65)."""
    c = np.asarray(rgb, dtype=np.float64) / 255.0
    c = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    m = np.array([[0.4124564, 0.3575761, 0.1804375],
                  [0.2126729, 0.7151522, 0.0721750],
                  [0.0193339, 0.1191920, 0.9503041]])
    xyz = (c @ m.T) / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787 * xyz + 16.0 / 116.0)
    return np.stack([116.0 * f[..., 1] - 16.0, 500.0 * (f[..., 0] - f[..., 1]), 200.0 * (f[..., 1] - f[..., 2])], -1)


def _foreground_mask(rgb):
    """Foreground = everything not reachable from the crop border through near-white pixels."""
    whiteish = (rgb.min(-1) > 228) & ((rgb.max(-1) - rgb.min(-1)) < 24)
    h, w = whiteish.shape
    m = Image.new("L", (w + 2, h + 2), 255)
    m.paste(Image.fromarray(np.where(whiteish, 255, 0).astype(np.uint8)), (1, 1))
    ImageDraw.floodfill(m, (0, 0), 128)
    return np.asarray(m)[1:-1, 1:-1] != 128


def _majority_filter(lab, k, passes):
    """3x3 majority vote among foreground labels (centre weighted x2 so ties keep the current label)."""
    h, w = lab.shape
    for _ in range(passes):
        padded = np.pad(lab, 1, constant_values=BG)
        counts = np.zeros((k, h, w), np.int16)
        for dy in range(3):
            for dx in range(3):
                shifted = padded[dy:dy + h, dx:dx + w]
                weight = 2 if (dy == 1 and dx == 1) else 1
                for c in range(k):
                    counts[c] += weight * (shifted == c)
        lab = np.where(lab >= 0, counts.argmax(0), BG)
    return lab


def _remove_small_islands(lab, min_px):
    """Merges 4-connected label islands smaller than `min_px` into their most common neighbouring label."""
    h, w = lab.shape
    seen = np.zeros_like(lab, dtype=bool)
    out = lab.copy()
    for y0 in range(h):
        for x0 in range(w):
            if seen[y0, x0] or lab[y0, x0] < 0:
                continue
            target = lab[y0, x0]
            comp, border = [], {}
            q = deque([(y0, x0)])
            seen[y0, x0] = True
            while q:
                y, x = q.popleft()
                comp.append((y, x))
                for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
                    if 0 <= ny < h and 0 <= nx < w:
                        nl = lab[ny, nx]
                        if nl == target and not seen[ny, nx]:
                            seen[ny, nx] = True
                            q.append((ny, nx))
                        elif nl >= 0 and nl != target:
                            border[nl] = border.get(nl, 0) + 1
            if len(comp) < min_px and border:
                repl = max(border, key=border.get)
                for y, x in comp:
                    out[y, x] = repl
    return out


def _min_filter_1d(a, win):
    pad = win // 2
    return np.lib.stride_tricks.sliding_window_view(np.pad(a, pad, mode="edge"), 2 * pad + 1).min(-1)


def _max_filter_1d(a, win):
    pad = win // 2
    return np.lib.stride_tricks.sliding_window_view(np.pad(a, pad, mode="edge"), 2 * pad + 1).max(-1)


class LabelMap:
    """
    A quantized part cell: `lab[j, i]` holds a palette index or `BG`. Also precomputes:
      - per-row silhouette spans `u0[j]..u1[j]`, morphologically opened along the vertical axis so short
        protrusions (ears, sleeve flaps) do not stretch the projection of the whole row;
      - `lab_fill`, where in-span background pixels take the nearest foreground label along the row (used
        when a part must stay watertight instead of being cut along its silhouette).
    """

    def __init__(self, lab, palette, open_frac=0.0):
        self.lab = lab
        self.palette = palette
        self.h, self.w = lab.shape
        fg = lab >= 0
        rows = np.flatnonzero(fg.any(1))
        jj = np.arange(self.h)
        first = np.array([np.flatnonzero(fg[j])[0] if fg[j].any() else 0 for j in jj], float)
        last = np.array([np.flatnonzero(fg[j])[-1] if fg[j].any() else 0 for j in jj], float)
        u0 = np.interp(jj, rows, first[rows])
        u1 = np.interp(jj, rows, last[rows])
        win = max(1, int(open_frac * self.h)) | 1
        if win > 1:
            u0 = _min_filter_1d(_max_filter_1d(u0, win), win)  # opening of the left boundary
            u1 = _max_filter_1d(_min_filter_1d(u1, win), win)  # opening of the right boundary
        self.u0, self.u1 = u0, u1

        self.lab_fill = lab.copy()
        idx = np.arange(self.w)
        for j in jj:
            src = j if fg[j].any() else rows[np.abs(rows - j).argmin()]
            fg_idx = np.flatnonzero(fg[src])
            pos = np.clip(np.searchsorted(fg_idx, idx), 1, len(fg_idx) - 1) if len(fg_idx) > 1 else np.zeros_like(idx)
            left, right = fg_idx[pos - 1] if len(fg_idx) > 1 else fg_idx[pos], fg_idx[pos]
            nearest = np.where(np.abs(idx - left) <= np.abs(right - idx), left, right)
            self.lab_fill[j] = lab[src, nearest]

    def to_image(self):
        """Renders the label map with palette colours on white for inspection."""
        rgb = np.full((self.h, self.w, 3), 255, np.uint8)
        pal = np.array(self.palette, np.uint8)
        fg = self.lab >= 0
        rgb[fg] = pal[self.lab[fg]]
        return Image.fromarray(rgb)


def quantize_part_cell(img, bbox, palette, allowed=None, l_weight=0.9, contrast=1.0, saturation=1.4,
                       majority_passes=3, min_island_frac=0.002, open_frac=0.0):
    """
    Phase 2A: quantizes one sheet cell to a swatch palette without dithering.

    `allowed` restricts the palette to the colours that may legitimately appear on this part (a shadowed
    tunic should never become "trouser brown"). `l_weight` scales lightness in the Lab distance: values
    well below 1 let pale skin highlights collapse into bone/ivory, so keep it near 1. Saturation is boosted
    so hues separate before snapping. Islands smaller than `min_island_frac` of the part area are merged
    into their surroundings (removes render-shading speckle, keeps teeth, eyes and skull patches).
    """
    crop = img.convert("RGB").crop(bbox)
    raw = np.asarray(crop, np.int16)
    fg = _foreground_mask(raw)
    boosted = ImageEnhance.Color(ImageEnhance.Contrast(crop).enhance(contrast)).enhance(saturation)
    lab_px = srgb_to_lab(np.asarray(boosted))
    allowed = list(range(len(palette))) if allowed is None else list(allowed)
    pal_lab = srgb_to_lab(np.array([palette[i] for i in allowed]))
    wvec = np.array([l_weight, 1.0, 1.0])
    d = (((lab_px[:, :, None, :] - pal_lab[None, None, :, :]) * wvec) ** 2).sum(-1)
    lab = np.array(allowed)[d.argmin(-1)]
    lab = np.where(fg, lab, BG)
    lab = _majority_filter(lab, len(palette), majority_passes)
    lab = _remove_small_islands(lab, max(4, int(min_island_frac * fg.sum())))
    return LabelMap(lab, palette, open_frac=open_frac)


# ---------------------------------------------------------------------------
# 2B. Projection of labels onto the surface
# ---------------------------------------------------------------------------
class RowSpanProjector:
    """
    Maps surface points to palette labels. A point at height `y` maps to image row `v` (linear over
    `y_range`); its `x` is normalised by the mesh half-width at that height and placed inside the row's
    silhouette span. Points in front of the profile centre (`z >= z_center(y)`) sample the front cell,
    others the (mirrored) back cell. With `cut_background=True`, in-span background pixels return `BG`,
    so the subdivision can cut the part along its true silhouette (jagged hems, notches). `cut_labels`
    additionally returns `BG` for palette labels that belong to another layer, e.g. skin seen through a
    tunic's neckline or tears, so the clothing shell is opened there and the body mesh shows through.
    """

    def __init__(self, front, back, y_range, profile, cut_background=False, cut_labels=()):
        self.front, self.back = front, back if back is not None else front
        self.back_sign = -1.0 if back is not None else 1.0
        self.y_min, self.y_max = y_range
        prof = np.asarray(sorted(profile), float)  # rows of (y, half_width, z_center)
        self.py, self.phw, self.pzc = prof[:, 0], prof[:, 1], prof[:, 2]
        self.cut = cut_background
        self.cut_labels = np.array(sorted(cut_labels), np.int64)

    def __call__(self, pts):
        pts = np.asarray(pts, float).reshape(-1, 3)
        x, y, z = pts[:, 0], pts[:, 1], pts[:, 2]
        hw = np.maximum(np.interp(y, self.py, self.phw), 1e-4)
        zc = np.interp(y, self.py, self.pzc)
        v = np.clip((self.y_max - y) / (self.y_max - self.y_min), 0.0, 1.0)
        t = np.clip(x / hw, -0.98, 0.98)
        out = np.empty(len(pts), np.int64)
        is_front = z >= zc
        for sel, lm, sgn in ((is_front, self.front, 1.0), (~is_front, self.back, self.back_sign)):
            if not sel.any():
                continue
            j = np.rint(v[sel] * (lm.h - 1)).astype(int)
            u0, u1 = lm.u0[j], lm.u1[j]
            i = np.clip(np.rint(0.5 * (u0 + u1) + sgn * t[sel] * 0.5 * (u1 - u0)).astype(int), 0, lm.w - 1)
            out[sel] = (lm.lab if self.cut else lm.lab_fill)[j, i]
        if len(self.cut_labels):
            out[np.isin(out, self.cut_labels)] = BG
        return out


# ---------------------------------------------------------------------------
# Phase 1 helpers: indexed lofted base meshes
# ---------------------------------------------------------------------------
def loft_mesh(rings):
    """Lofts closed rings (bottom->top, each `(n, 3)`) into an outward-facing indexed mesh with caps."""
    rings = [np.asarray(r, float) for r in rings]
    nr, n = len(rings), len(rings[0])
    verts = np.vstack(rings + [rings[0].mean(0, keepdims=True), rings[-1].mean(0, keepdims=True)])
    faces = []
    for i in range(nr - 1):
        for k in range(n):
            kn = (k + 1) % n
            a, b, c, d = i * n + k, (i + 1) * n + k, (i + 1) * n + kn, i * n + kn
            faces += [(a, b, c), (a, c, d)]
    cb, ct = nr * n, nr * n + 1
    top = (nr - 1) * n
    for k in range(n):
        kn = (k + 1) % n
        faces.append((cb, k, kn))
        faces.append((ct, top + kn, top + k))
    faces = np.array(faces, np.int64)
    if signed_volume(verts, faces) < 0:
        faces = faces[:, ::-1].copy()
    return verts, faces


def signed_volume(verts, faces):
    a, b, c = verts[faces[:, 0]], verts[faces[:, 1]], verts[faces[:, 2]]
    return float(np.einsum("ij,ij->i", a, np.cross(b, c)).sum() / 6.0)


def ring_profile(rings):
    """`(y, half_width, z_center)` per ring, as needed by `RowSpanProjector`."""
    prof = []
    for r in rings:
        r = np.asarray(r)
        prof.append((float(r[:, 1].mean()), float(np.abs(r[:, 0]).max()), float(0.5 * (r[:, 2].max() + r[:, 2].min()))))
    return prof


# ---------------------------------------------------------------------------
# 2C. Adaptive colour-boundary subdivision
# ---------------------------------------------------------------------------
def _stencil(res, shrink=1.0):
    """Barycentric grid with `res` steps per edge; `shrink < 1` pulls samples off the edges."""
    pts = [(i / res, j / res, 1.0 - i / res - j / res) for i in range(res + 1) for j in range(res + 1 - i)]
    b = np.array(pts)
    return 1.0 / 3.0 + shrink * (b - 1.0 / 3.0)


def _face_points(verts, faces, bary):
    return np.einsum("sk,mkd->msd", bary, verts[faces])


def _mode_rows(labels, n_labels):
    """Row-wise majority of labels in `[-1, n_labels)` -> `(majority, minority_count)`."""
    shifted = labels + 1
    counts = np.stack([(shifted == c).sum(1) for c in range(n_labels + 1)], 1)
    maj = counts.argmax(1)
    return maj - 1, labels.shape[1] - counts.max(1)


def _edge_lengths(verts, faces):
    v = verts[faces]
    return np.linalg.norm(v[:, [1, 2, 0]] - v[:, [0, 1, 2]], axis=-1)


def _triangulate(face, split, mids, verts):
    """Conforming red/green triangulation of one face given which of its edges (v0v1, v1v2, v2v0) split."""
    cnt = sum(split)
    if cnt == 0:
        return [face]
    if cnt == 3:
        v0, v1, v2 = face
        m0, m1, m2 = mids
        return [(v0, m0, m2), (m0, v1, m1), (m2, m1, v2), (m0, m1, m2)]
    for r in range(3):
        f = face[r:] + face[:r]
        s = split[r:] + split[:r]
        m = mids[r:] + mids[:r]
        if cnt == 1 and s == (True, False, False):
            v0, v1, v2 = f
            return [(v0, m[0], v2), (m[0], v1, v2)]
        if cnt == 2 and s == (True, True, False):
            v0, v1, v2 = f
            m0, m1 = m[0], m[1]
            tris = [(m0, v1, m1)]
            if np.linalg.norm(verts[v0] - verts[m1]) <= np.linalg.norm(verts[m0] - verts[v2]):
                tris += [(v0, m0, m1), (v0, m1, v2)]
            else:
                tris += [(v0, m0, v2), (m0, m1, v2)]
            return tris
    raise AssertionError("unreachable split pattern")


def _split_edges(verts, faces, split_mask, positions_fn):
    """Splits the flagged edges once (shared edges split once for both faces) and re-triangulates."""
    n = len(verts)
    ea, eb = faces, np.roll(faces, -1, axis=1)
    keys = np.minimum(ea, eb) * n + np.maximum(ea, eb)
    uniq = np.unique(keys[split_mask])
    if len(uniq) == 0:
        return verts, faces
    new_pts = positions_fn(uniq // n, uniq % n)
    verts = np.vstack([verts, new_pts])
    is_split = np.isin(keys, uniq)
    mid_id = n + np.searchsorted(uniq, keys)
    out = []
    for f, s, m in zip(faces.tolist(), is_split.tolist(), mid_id.tolist()):
        out.extend(_triangulate(tuple(f), tuple(s), tuple(m), verts))
    return verts, np.array(out, np.int64)


def subdivide_uniform(verts, faces, levels):
    """Reference strategy: split every edge at its midpoint `levels` times (x4 triangles per level)."""
    for _ in range(levels):
        verts, faces = _split_edges(verts, faces, np.ones(faces.shape, bool),
                                    lambda ia, ib: 0.5 * (verts[ia] + verts[ib]))
    return verts, faces


def subdivide_adaptive(verts, faces, sampler, n_labels, mode="snap", max_levels=6, min_edge=0.010,
                       max_tris=None, stencil_res=5, snap_iters=8, snap_clamp=0.12):
    """
    Phase 2C. Each level: sample every face on an interior stencil; faces whose samples disagree (and whose
    longest edge exceeds `min_edge`) are refined.
      - `mode="midpoint"`: classic red refinement of mixed faces (all three edges at midpoints).
      - `mode="snap"`: only edges whose endpoint labels differ are split, at the point where the colour
        changes (binary search, clamped to `[snap_clamp, 1 - snap_clamp]` to avoid slivers). Mixed faces
        without such an edge (a feature fully inside one face, e.g. a small eye) get a red midpoint split.
    Neighbouring faces receive green splits automatically because split points are keyed by edge.
    With `max_tris`, candidates are ranked by `minority_fraction * area` (the surface they get wrong) and
    only the worst are refined each level, so the triangle budget is spent where colour error is largest.
    """
    stencil = _stencil(stencil_res, shrink=0.8)
    for _ in range(max_levels):
        pts = _face_points(verts, faces, stencil)
        lab = sampler(pts.reshape(-1, 3)).reshape(len(faces), -1)
        _, minority = _mode_rows(lab, n_labels)
        elen = _edge_lengths(verts, faces)
        cand = (minority > 0) & (elen.max(1) > min_edge)
        if max_tris is not None and cand.any():
            room = (max_tris - len(faces)) // 4  # a refined face plus green neighbours adds ~3-4 triangles
            if room <= 0:
                break
            idx = np.flatnonzero(cand)
            if len(idx) > room:
                v = verts[faces[idx]]
                area = 0.5 * np.linalg.norm(np.cross(v[:, 1] - v[:, 0], v[:, 2] - v[:, 0]), axis=1)
                keep = idx[np.argsort(-(minority[idx] / lab.shape[1]) * area)[:room]]
                cand = np.zeros_like(cand)
                cand[keep] = True
        if not cand.any():
            break
        if mode == "midpoint":
            split_mask = np.zeros(faces.shape, bool)
            split_mask[cand] = True
            positions = lambda ia, ib: 0.5 * (verts[ia] + verts[ib])
        else:
            vlab = sampler(verts)
            ea, eb = faces, np.roll(faces, -1, axis=1)
            boundary = (vlab[ea] != vlab[eb]) & (elen > min_edge) & cand[:, None]
            split_mask = boundary.copy()
            split_mask[cand & ~boundary.any(1)] = True

            def positions(ia, ib, verts=verts, vlab=vlab):
                t = np.full(len(ia), 0.5)
                diff = vlab[ia] != vlab[ib]
                if diff.any():
                    a, b, la = verts[ia[diff]], verts[ib[diff]], vlab[ia[diff]]
                    lo, hi = np.zeros(len(a)), np.ones(len(a))
                    for _ in range(snap_iters):
                        mid = 0.5 * (lo + hi)
                        same = sampler(a + mid[:, None] * (b - a)) == la
                        lo, hi = np.where(same, mid, lo), np.where(same, hi, mid)
                    t[diff] = np.clip(0.5 * (lo + hi), snap_clamp, 1.0 - snap_clamp)
                return verts[ia] + t[:, None] * (verts[ib] - verts[ia])

        verts, faces = _split_edges(verts, faces, split_mask, positions)
    return verts, faces


def face_labels(verts, faces, sampler, n_labels, stencil_res=5):
    """Majority label per face on an interior stencil (centroid-only when `stencil_res == 0`)."""
    stencil = np.array([[1 / 3, 1 / 3, 1 / 3]]) if stencil_res == 0 else _stencil(stencil_res, shrink=0.8)
    lab = sampler(_face_points(verts, faces, stencil).reshape(-1, 3)).reshape(len(faces), -1)
    return _mode_rows(lab, n_labels)[0]


def color_error(verts, faces, labels, sampler, spacing=0.004):
    """Area-weighted fraction of the surface whose flat face colour differs from the projected reference."""
    v = verts[faces]
    area = 0.5 * np.linalg.norm(np.cross(v[:, 1] - v[:, 0], v[:, 2] - v[:, 0]), axis=1)
    res = np.clip(np.ceil(_edge_lengths(verts, faces).max(1) / spacing), 2, 24).astype(int)
    err = 0.0
    for r in np.unique(res):
        idx = np.flatnonzero(res == r)
        lab = sampler(_face_points(verts, faces[idx], _stencil(r)).reshape(-1, 3)).reshape(len(idx), -1)
        err += float((area[idx] * (lab != labels[idx, None]).mean(1)).sum())
    return err / float(area.sum())


# ---------------------------------------------------------------------------
# 2D. Optional carving along colour contours
# ---------------------------------------------------------------------------
def vertex_normals(verts, faces):
    fn = np.cross(verts[faces[:, 1]] - verts[faces[:, 0]], verts[faces[:, 2]] - verts[faces[:, 0]])
    vn = np.zeros_like(verts)
    for j in range(3):
        np.add.at(vn, faces[:, j], fn)
    return vn / np.maximum(np.linalg.norm(vn, axis=1, keepdims=True), 1e-12)


def carve_labels(verts, faces, labels, depths):
    """
    Pushes vertices inward by the *smallest* carve depth among their incident faces (non-carved labels count
    as depth 0). Contour vertices therefore stay on the surface and the walls slope into the recess, while
    features that are not carved (teeth inside a carved mouth) remain proud of the floor.
    """
    fdepth = np.array([depths.get(int(l), 0.0) for l in labels])
    disp = np.full(len(verts), np.inf)
    for j in range(3):
        np.minimum.at(disp, faces[:, j], fdepth)
    disp[~np.isfinite(disp)] = 0.0
    return verts - vertex_normals(verts, faces) * disp[:, None]


# ---------------------------------------------------------------------------
# Inspection rendering (orthographic, z-buffered, flat shaded)
# ---------------------------------------------------------------------------
def render_mesh(verts, faces, face_rgb, size=(260, 330), yaw_deg=0.0, fit=None, wire=False, ss=3,
                bg=(28, 31, 42)):
    """Renders flat-shaded triangles; back faces seen through cut holes are drawn dark (interior)."""
    th = math.radians(yaw_deg)
    rot = np.array([[math.cos(th), 0, math.sin(th)], [0, 1, 0], [-math.sin(th), 0, math.cos(th)]])
    v = verts @ rot.T
    w_px, h_px = size[0] * ss, size[1] * ss
    if fit is None:
        lo, hi = v[:, :2].min(0), v[:, :2].max(0)
        fit = (0.5 * (lo + hi), 0.88 * min(w_px / (hi[0] - lo[0]), h_px / (hi[1] - lo[1])) / ss)
    centre, scale = fit
    sx = (v[:, 0] - centre[0]) * scale * ss + w_px / 2
    sy = (centre[1] - v[:, 1]) * scale * ss + h_px / 2
    fv = v[faces]
    n = np.cross(fv[:, 1] - fv[:, 0], fv[:, 2] - fv[:, 0])
    n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)
    light = np.array([-0.45, 0.55, 0.70])
    light /= np.linalg.norm(light)
    front = n[:, 2] > 0
    shade = np.where(front, 0.58 + 0.48 * np.clip(n @ light, 0, 1), 0.30)
    cols = np.clip(np.asarray(face_rgb, float) * shade[:, None], 0, 255)

    img = np.empty((h_px, w_px, 3), float)
    img[:] = bg
    zbuf = np.full((h_px, w_px), -np.inf)
    visible = np.zeros(len(faces), bool)
    for fi, (a, b, c) in enumerate(faces):
        xs, ys, zs = sx[[a, b, c]], sy[[a, b, c]], v[[a, b, c], 2]
        x0, x1 = max(int(xs.min()), 0), min(int(xs.max()) + 1, w_px - 1)
        y0, y1 = max(int(ys.min()), 0), min(int(ys.max()) + 1, h_px - 1)
        if x1 < x0 or y1 < y0:
            continue
        den = (ys[1] - ys[2]) * (xs[0] - xs[2]) + (xs[2] - xs[1]) * (ys[0] - ys[2])
        if abs(den) < 1e-9:
            continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        w0 = ((ys[1] - ys[2]) * (gx - xs[2]) + (xs[2] - xs[1]) * (gy - ys[2])) / den
        w1 = ((ys[2] - ys[0]) * (gx - xs[2]) + (xs[0] - xs[2]) * (gy - ys[2])) / den
        w2 = 1.0 - w0 - w1
        inside = (w0 >= -1e-6) & (w1 >= -1e-6) & (w2 >= -1e-6)
        if not inside.any():
            continue
        z = w0 * zs[0] + w1 * zs[1] + w2 * zs[2]
        sub = zbuf[y0:y1 + 1, x0:x1 + 1]
        upd = inside & (z > sub)
        if upd.any():
            sub[upd] = z[upd]
            img[y0:y1 + 1, x0:x1 + 1][upd] = cols[fi]
            visible[fi] = front[fi]
    out = Image.fromarray(img.astype(np.uint8))
    if wire:
        d = ImageDraw.Draw(out)
        for fi in np.flatnonzero(visible):
            a, b, c = faces[fi]
            d.line([(sx[a], sy[a]), (sx[b], sy[b]), (sx[c], sy[c]), (sx[a], sy[a])], fill=(14, 14, 18), width=1)
    return out.resize(size, Image.Resampling.LANCZOS), fit


# ---------------------------------------------------------------------------
# Demo on the zombie_walker sheet
# ---------------------------------------------------------------------------
def _squircle_ring(y, rx, rz, z_off, n, p=2.35, front_only=True, sculpt=None):
    pts = []
    for k in range(n):
        ang = 2.0 * math.pi * (k + 0.5) / n
        ca, sa = math.cos(ang), math.sin(ang)
        if (not front_only) or sa > 0.20:
            r = (abs(ca) ** p + abs(sa) ** p) ** (-1.0 / p)
            px, pz = ca * r * rx * (0.96 if front_only else 1.0), z_off + sa * r * rz * (0.92 if front_only else 1.0)
            if sculpt is not None and sa > 0.20:
                pz += sculpt(px, y) * (sa - 0.20) / 0.80
        else:
            px, pz = ca * rx, z_off + sa * rz
        pts.append((px, y, pz))
    return pts


def zombie_head_form():
    """Phase 1 head: smooth, non-oscillating squircle head form matching the 0.74 w/h ratio without ripples."""
    profile = [
        (1.18, 0.200, 0.200, 0.020),
        (1.24, 0.217, 0.214, 0.023),
        (1.30, 0.257, 0.248, 0.029),
        (1.38, 0.324, 0.304, 0.040),
        (1.48, 0.382, 0.352, 0.049),
        (1.58, 0.385, 0.355, 0.050),
        (1.68, 0.385, 0.355, 0.050),
        (1.76, 0.385, 0.355, 0.050),
        (1.84, 0.379, 0.350, 0.043),
        (1.92, 0.350, 0.323, 0.033),
        (1.99, 0.299, 0.275, 0.025),
        (2.05, 0.223, 0.205, 0.017),
        (2.10, 0.095, 0.087, 0.011),
    ]
    return [_squircle_ring(y, rx, rz, zo, 28, p=2.25, front_only=False) for y, rx, rz, zo in profile]


def zombie_torso_body_form():
    """Phase 1 solid torso body: 20-sector squircle lofted hips-to-neck in solid green decaying flesh."""
    profile = [
        (0.38, 0.33, 0.23, 0.000), (0.46, 0.31, 0.22, 0.000), (0.55, 0.27, 0.19, 0.008),
        (0.68, 0.28, 0.20, 0.010), (0.80, 0.30, 0.21, 0.012), (0.92, 0.31, 0.22, 0.016),
        (1.04, 0.26, 0.20, 0.014), (1.10, 0.22, 0.18, 0.010), (1.16, 0.18, 0.16, 0.008),
        (1.22, 0.15, 0.14, 0.006),
    ]
    return [_squircle_ring(y, rx, rz, zo, 20, p=2.4, front_only=False) for y, rx, rz, zo in profile]


def zombie_tunic_form():
    """Phase 1 tunic shell: the zombie torso profile inflated by 1.5 cm, coarse 28-sector loft."""
    profile = [
        (0.38, 0.33, 0.23, 0.000), (0.46, 0.31, 0.22, 0.000), (0.55, 0.27, 0.19, 0.008),
        (0.68, 0.28, 0.20, 0.010), (0.80, 0.30, 0.21, 0.012), (0.92, 0.31, 0.22, 0.016),
        (1.04, 0.26, 0.20, 0.014), (1.10, 0.22, 0.18, 0.010), (1.16, 0.18, 0.16, 0.008),
        (1.22, 0.15, 0.14, 0.006),
    ]
    return [_squircle_ring(y, rx + 0.015, rz + 0.015, zo, 28, p=2.4, front_only=False) for y, rx, rz, zo in profile]



DEMO_PARTS = [
    {
        "name": "Head front/back (cells 0,0 + 0,2)",
        "front": "head", "back": "beard", "form": zombie_head_form,
        "y_range": (1.18, 2.10), "allowed": [0, 1, 2, 4, 7], "cut_background": False, "open_frac": 0.30,
        "cut_labels": (), "max_tris": 6000,
        "carve": {2: 0.018, 1: 0.006, 7: 0.010}, "yaw": 0.0,
    },
    {
        "name": "Tunic shell (cells 1,1 + 1,2: skin/wounds cut out)",
        "front": "torso_back", "back": "pauldron", "form": zombie_tunic_form,
        "y_range": (0.36, 1.20), "allowed": [0, 1, 2, 3, 4], "cut_background": True, "open_frac": 0.08,
        "cut_labels": (0, 1), "max_tris": 5000,
        "carve": None, "yaw": 0.0,
    },
    {
        "name": "Composite Torso (solid green body + cut tunic shell)",
        "front": "torso_back", "back": "pauldron", "form": zombie_tunic_form,
        "y_range": (0.36, 1.20), "allowed": [0, 1, 2, 3, 4], "cut_background": True, "open_frac": 0.08,
        "cut_labels": (0, 1), "max_tris": 5000,
        "carve": None, "yaw": 0.0,
        "underlay": (zombie_torso_body_form, 0),  # Underlay body with swatch 0 (green flesh)
    },
]


def run_demo(out_dir):
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import sdf_sculptor as sdfs
    from build_wave2_monsters import ZOMBIE_WALKER_SWATCHES, ARTIFACT_DIR

    out_dir = out_dir or ARTIFACT_DIR
    palette = ZOMBIE_WALKER_SWATCHES
    k = len(palette)
    sheet = Image.open(os.path.join(ARTIFACT_DIR, "zombie_walker_3x3_google.png")).convert("RGB")
    _, bboxes, _ = sdfs.extract_sheet_quadrant_masks_and_dilated_atlas(sheet, dilate_px=4, layout="3x3")
    pal = np.array(palette, float)

    pw, ph = 250, 320
    cols = ["Source cell", "2A Quantized", "A: centroid colour", "B: uniform x16", "C: adaptive midpoint",
            "D: adaptive + snap", "D wireframe", "E: D + carve / 3D"]
    canvas = Image.new("RGB", (pw * len(cols) + 20, 40 + len(DEMO_PARTS) * (ph + 70)), (20, 22, 30))
    draw = ImageDraw.Draw(canvas)
    font = ImageFont.load_default(size=14)
    small = ImageFont.load_default(size=12)
    stats = []

    for row, part in enumerate(DEMO_PARTS):
        y_off = 40 + row * (ph + 70)
        draw.text((12, y_off - 22), part["name"], fill=(176, 238, 68), font=font)
        lm_f = quantize_part_cell(sheet, bboxes[part["front"]], palette, part["allowed"], open_frac=part["open_frac"])
        lm_b = quantize_part_cell(sheet, bboxes[part["back"]], palette, part["allowed"], open_frac=part["open_frac"])
        rings = part["form"]()
        base_v, base_f = loft_mesh(rings)
        sampler = RowSpanProjector(lm_f, lm_b, part["y_range"], ring_profile(rings), part["cut_background"],
                                   cut_labels=part.get("cut_labels", ()))

        underlay_mesh = None
        if "underlay" in part:
            u_form_fn, u_color_idx = part["underlay"]
            u_rings = u_form_fn()
            u_v, u_f = loft_mesh(u_rings)
            u_rgb = np.full((len(u_f), 3), pal[u_color_idx])
            underlay_mesh = (u_v, u_f, u_rgb)

        def colourise(v, f, lab):
            keep = lab >= 0 if part["cut_background"] else np.ones(len(f), bool)
            fl = np.where(lab >= 0, lab, 0)
            kf = f[keep]
            krgb = pal[fl[keep]]
            if underlay_mesh is not None:
                uv, uf, urgb = underlay_mesh
                comb_v = np.vstack([uv, v])
                comb_f = np.vstack([uf, kf + len(uv)])
                comb_rgb = np.vstack([urgb, krgb])
                return comb_v, comb_f, comb_rgb, lab
            return v, kf, krgb, lab

        results = {}
        max_tris = part.get("max_tris")
        t0 = time.time()
        lab_a = face_labels(base_v, base_f, sampler, k, stencil_res=0)
        results["A"] = (base_v, base_f, lab_a, time.time() - t0)
        t0 = time.time()
        uv_, uf_ = subdivide_uniform(base_v, base_f, 2)
        results["B"] = (uv_, uf_, face_labels(uv_, uf_, sampler, k), time.time() - t0)
        for key, mode in (("C", "midpoint"), ("D", "snap")):
            t0 = time.time()
            av, af = subdivide_adaptive(base_v, base_f, sampler, k, mode=mode, max_tris=max_tris)
            results[key] = (av, af, face_labels(av, af, sampler, k), time.time() - t0)

        _, fit = render_mesh(base_v, base_f, np.full((len(base_f), 3), 128.0), (pw, ph))
        panels = [sheet.crop(bboxes[part["front"]]), lm_f.to_image()]
        captions = ["", f"{len(part['allowed'])} allowed swatches"]
        for key in ("A", "B", "C", "D"):
            v, f, lab, dt = results[key]
            err = color_error(v, f, lab, sampler)
            stats.append((part["name"], key, len(f), err, dt))
            vv, ff, rgb, _ = colourise(v, f, lab)
            panels.append(render_mesh(vv, ff, rgb, (pw, ph), fit=fit)[0])
            captions.append(f"{len(ff):,} tris | colour err {100 * err:.1f}%")
        v, f, lab, _ = results["D"]
        vv, ff, rgb, _ = colourise(v, f, lab)
        panels.append(render_mesh(vv, ff, rgb, (pw, ph), fit=fit, wire=True)[0])
        captions.append("adaptive density follows contours")
        if part["carve"]:
            cv = carve_labels(v, f, lab, part["carve"])
            vv, ff, rgb, _ = colourise(cv, f, lab)
            panels.append(render_mesh(vv, ff, rgb, (pw, ph), yaw_deg=30.0)[0])
            captions.append("mouth/sockets carved along contours")
        elif underlay_mesh is not None:
            panels.append(render_mesh(vv, ff, rgb, (pw, ph), yaw_deg=25.0)[0])
            captions.append("green rotten body shows through cuts")
        else:
            panels.append(render_mesh(vv, ff, rgb, (pw, ph), yaw_deg=30.0)[0])
            captions.append("hem/neck cut along silhouette")

        for ci, (panel, cap) in enumerate(zip(panels, captions)):
            x = 10 + ci * pw
            p = panel.copy()
            p.thumbnail((pw - 10, ph - 10))
            tile = Image.new("RGB", (pw - 6, ph), (28, 31, 42) if ci >= 2 else (255, 255, 255))
            tile.paste(p, ((tile.width - p.width) // 2, (ph - p.height) // 2))
            canvas.paste(tile, (x, y_off))
            if row == 0:
                draw.text((x + 4, 8), cols[ci], fill=(220, 224, 236), font=font)
            draw.text((x + 4, y_off + ph + 6), cap, fill=(200, 204, 216), font=small)

    out_png = os.path.join(out_dir, "adaptive_subdivision_prototype.png")
    canvas.save(out_png)
    print(f"{'part':38s} {'strategy':8s} {'tris':>8s} {'colour err':>11s} {'time':>7s}")
    for name, key, n_tris, err, dt in stats:
        print(f"{name:38s} {key:8s} {n_tris:8d} {100 * err:10.2f}% {dt:6.2f}s")
    print(f"Saved {out_png}")


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--out-dir", default=None, help="Directory for the comparison sheet (default: artifact dir).")
    run_demo(ap.parse_args().out_dir)
