"""Score a model against its concept turnaround: silhouette overlap per view, a diff sheet, an optional vision audit.

`item compare` renders the staged build (with its host) at the concept's view angles (`VIEWS`) and passes the
renders here. Concept panels are split at their source resolution, so all panels share one pixel scale; with
`shared_scale` a worn item keeps one model scale across every view, so a helmet modeled 20% too large scores badly
instead of being rescaled per view. Each view may still shift.
"""

from __future__ import annotations

import json
import os
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

from core import mesh

# Concept turnaround panels, left to right, as `catalog.render` yaws: front, three-quarter with the subject turned to
# the viewer's left, and the profile facing left (the layout `prompts.TURNAROUND` asks for).
VIEWS = (("Front", 0.0), ("Three-Quarter", -36.0), ("Side", -90.0))
# Generated three-quarter panels are the least precise, so they count less when one scale is fitted to all views.
SCALE_WEIGHTS = (1.0, 0.5, 1.0)
PAD = 36


def _load_font(size: int) -> ImageFont.ImageFont | ImageFont.FreeTypeFont:
    for candidate in (
        "/System/Library/Fonts/SFNSMono.ttf",
        "/System/Library/Fonts/Menlo.ttc",
        "/Library/Fonts/Arial.ttf",
    ):
        if os.path.exists(candidate):
            try:
                return ImageFont.truetype(candidate, size)
            except OSError:
                pass
    return ImageFont.load_default()


def trim_pedestal(mask: np.ndarray) -> tuple[np.ndarray, int | None]:
    """Detect and exclude a flared mannequin pedestal below a narrow neck pinch.

    Scans the lower third of the foreground mask for the narrowest smoothed row
    (neck waist) followed below by a flared pedestal base. If none is found
    (for example, a long hood or cloak covers the base), returns the mask intact.
    """
    rows = np.flatnonzero(mask.any(1))
    if len(rows) < 20:
        return mask.copy(), None
    r0, r1 = int(rows[0]), int(rows[-1]) + 1
    h = r1 - r0
    widths = mask[r0:r1].sum(1).astype(float)
    smooth = ndimage.uniform_filter1d(widths, size=max(3, h // 40))
    max_head_w = float(smooth[: max(1, int(0.72 * h))].max())

    search_start = int(0.62 * h)
    search_end = int(0.95 * h)
    if search_end <= search_start + 3:
        return mask.copy(), None

    sub = smooth[search_start:search_end]
    neck_rel = search_start + int(np.argmin(sub))
    neck_w = float(smooth[neck_rel])
    below = smooth[neck_rel + 2 :]
    if len(below) < max(4, h // 25):
        return mask.copy(), None

    below_max = float(below.max())
    if neck_w < 0.56 * max_head_w and below_max >= 1.18 * max(neck_w, 1.0):
        cut_row = r0 + neck_rel + 1
        trimmed = mask.copy()
        trimmed[cut_row:, :] = False
        return trimmed, cut_row
    return mask.copy(), None


def _mask_bbox(mask: np.ndarray) -> tuple[int, int, int, int]:
    rows, cols = np.flatnonzero(mask.any(1)), np.flatnonzero(mask.any(0))
    return int(rows[0]), int(rows[-1]) + 1, int(cols[0]), int(cols[-1]) + 1


def _scaled(src: np.ndarray, scale: float) -> np.ndarray:
    """`src` scaled by `scale` (at least 2 px a side): a bool mask by nearest neighbour, an RGBA image by Lanczos."""
    sh, sw = src.shape[:2]
    size = (max(2, int(round(sw * scale))), max(2, int(round(sh * scale))))
    if src.dtype == bool:
        return np.asarray(Image.fromarray(src.astype(np.uint8) * 255).resize(size, Image.NEAREST)) > 127
    return np.asarray(Image.fromarray(src).resize(size, Image.LANCZOS))


def _window(src_shape: tuple[int, ...], out_shape: tuple[int, ...], cx: float, cy: float):
    """Source and destination slices pasting an image of `src_shape` centred at `(cx, cy)` into `out_shape`, clipped
    to the canvas; None when it lands wholly outside."""
    nh, nw = src_shape[:2]
    c0 = int(round(cx - nw * 0.5))
    r0 = int(round(cy - nh * 0.5))
    sc0, sr0 = max(0, -c0), max(0, -r0)
    dc0, dr0 = max(0, c0), max(0, r0)
    cw = min(nw - sc0, out_shape[1] - dc0)
    ch = min(nh - sr0, out_shape[0] - dr0)
    if cw <= 0 or ch <= 0:
        return None
    return (slice(sr0, sr0 + ch), slice(sc0, sc0 + cw)), (slice(dr0, dr0 + ch), slice(dc0, dc0 + cw))


def _paste(src: np.ndarray, out_shape: tuple[int, int], scale: float, cx: float, cy: float) -> np.ndarray:
    """`src` (bool mask or RGBA) scaled by `scale` and pasted centred at `(cx, cy)` on a blank `out_shape` canvas."""
    resized = _scaled(src, scale)
    canvas = np.zeros(tuple(out_shape) + resized.shape[2:], dtype=resized.dtype)
    win = _window(resized.shape, out_shape, cx, cy)
    if win is not None:
        canvas[win[1]] = resized[win[0]]
    return canvas


def _iou_at(resized: np.ndarray, target: np.ndarray, target_area: int, cx: float, cy: float) -> float | None:
    """IoU of the mask `resized` pasted centred at `(cx, cy)` against `target`, counted inside the overlap window
    only (no canvas per try); None when the paste lands wholly outside."""
    win = _window(resized.shape, target.shape, cx, cy)
    if win is None:
        return None
    placed = resized[win[0]]
    inter = int(np.count_nonzero(placed & target[win[1]]))
    return inter / max(int(np.count_nonzero(placed)) + target_area - inter, 1)


def align(
    model_rgba: np.ndarray,
    model_mask: np.ndarray,
    concept_fit_mask: np.ndarray,
    pad: int = 32,
    scale: float | None = None,
) -> tuple[np.ndarray, np.ndarray, np.ndarray, dict]:
    """Pad canvas, search `(scale, dx, dy)` maximizing silhouette IoU, and return aligned layers.

    A given `scale` (concept pixels per render pixel) is kept and only the offset is searched.

    Returns `(aligned_rgba, aligned_mask, padded_concept_fit_mask, alignment_info)` on a
    common padded canvas of size `(H + 2*pad, W + 2*pad)`.
    """
    ch, cw = concept_fit_mask.shape
    mh, mw = model_mask.shape

    padded_concept = np.zeros((ch + 2 * pad, cw + 2 * pad), dtype=bool)
    padded_concept[pad : pad + ch, pad : pad + cw] = concept_fit_mask

    cr0, cr1, cc0, cc1 = _mask_bbox(padded_concept)
    c_w = cc1 - cc0
    c_h = cr1 - cr0
    cx0 = 0.5 * (cc0 + cc1)
    cy0 = 0.5 * (cr0 + cr1)

    # Initial scale from geometric mean of width & height ratios so outliers (tall horn) don't over-bias.
    k_w = c_w / max(mw, 1)
    k_h = c_h / max(mh, 1)
    k_init = float(np.sqrt(k_w * k_h))

    # Coarse search on ~180px downsampled masks for speed.
    ds = max(1.0, max(padded_concept.shape) / 180.0)
    c_ds_h = max(16, int(round(padded_concept.shape[0] / ds)))
    c_ds_w = max(16, int(round(padded_concept.shape[1] / ds)))
    c_small = np.asarray(
        Image.fromarray(padded_concept.astype(np.uint8) * 255).resize((c_ds_w, c_ds_h), Image.NEAREST)
    ) > 127
    c_small_area = int(np.count_nonzero(c_small))
    cx_ds = cx0 / ds
    cy_ds = cy0 / ds

    best = (-1.0, k_init, 0, 0)
    step_xy = max(1, int(round(0.025 * max(c_ds_w, c_ds_h))))
    range_x = range(-int(round(0.14 * c_ds_w)), int(round(0.14 * c_ds_w)) + 1, step_xy)
    range_y = range(-int(round(0.14 * c_ds_h)), int(round(0.14 * c_ds_h)) + 1, step_xy)

    for k_try in ([scale] if scale else k_init * np.linspace(0.76, 1.24, 17)):
        k_try = float(k_try)
        resized = _scaled(model_mask, k_try / ds)
        for dx_ds in range_x:
            for dy_ds in range_y:
                score = _iou_at(resized, c_small, c_small_area, cx_ds + dx_ds, cy_ds + dy_ds)
                if score is not None and score > best[0]:
                    best = (score, k_try, int(round(dx_ds * ds)), int(round(dy_ds * ds)))

    # Fine refinement at full working resolution around coarse winner, then a final 1-pixel nudge.
    concept_area = int(np.count_nonzero(padded_concept))

    def iou(resized: np.ndarray, dx: int, dy: int) -> float:
        return _iou_at(resized, padded_concept, concept_area, cx0 + dx, cy0 + dy) or 0.0

    _, k_c, dx_c, dy_c = best
    fine_best = (-1.0, k_c, dx_c, dy_c)
    for ks in ([1.0] if scale else np.linspace(0.95, 1.05, 7)):
        k_try = k_c * float(ks)
        resized = _scaled(model_mask, k_try)
        for ddx in range(-8, 9, 2):
            for ddy in range(-8, 9, 2):
                score = iou(resized, dx_c + ddx, dy_c + ddy)
                if score > fine_best[0]:
                    fine_best = (score, k_try, dx_c + ddx, dy_c + ddy)

    score, k_opt, dx_opt, dy_opt = fine_best
    resized = _scaled(model_mask, k_opt)
    for ddx in (-1, 0, 1):
        for ddy in (-1, 0, 1):
            s = iou(resized, dx_opt + ddx, dy_opt + ddy)
            if s > score:
                score, dx_opt, dy_opt = s, dx_opt + ddx, dy_opt + ddy

    oh, ow = padded_concept.shape
    aligned_mask = _paste(model_mask, (oh, ow), k_opt, cx0 + dx_opt, cy0 + dy_opt)
    aligned_rgba = _paste(model_rgba, (oh, ow), k_opt, cx0 + dx_opt, cy0 + dy_opt)
    info = {
        "iou": float(score),
        "scale": float(k_opt),
        "dx": int(dx_opt),
        "dy": int(dy_opt),
        "center_x": float(cx0),
        "center_y": float(cy0),
    }
    return aligned_rgba, aligned_mask, padded_concept, info


def compute_metrics(concept_mask: np.ndarray, model_mask: np.ndarray) -> dict:
    """Compute overall and 3x3 regional divergence stats between concept & aligned model masks."""
    inter = int(np.count_nonzero(concept_mask & model_mask))
    union_mask = concept_mask | model_mask
    union = max(int(np.count_nonzero(union_mask)), 1)
    c_area = max(int(np.count_nonzero(concept_mask)), 1)
    m_area = max(int(np.count_nonzero(model_mask)), 1)

    missing = concept_mask & ~model_mask
    extra = model_mask & ~concept_mask

    ur0, ur1, uc0, uc1 = _mask_bbox(union_mask)
    cr0, cr1, cc0, cc1 = _mask_bbox(concept_mask)
    mr0, mr1, mc0, mc1 = _mask_bbox(model_mask)

    # 3x3 zone divergence percentages relative to total union area
    r_edges = np.linspace(ur0, ur1, 4, dtype=int)
    c_edges = np.linspace(uc0, uc1, 4, dtype=int)
    row_names = ("Top", "Mid", "Bot")
    col_names = ("L", "C", "R")
    zones = {}
    for ri in range(3):
        for ci in range(3):
            name = f"{row_names[ri]}-{col_names[ci]}"
            rs = slice(r_edges[ri], r_edges[ri + 1])
            cs = slice(c_edges[ci], c_edges[ci + 1])
            miss_pct = 100.0 * float(np.count_nonzero(missing[rs, cs])) / union
            ext_pct = 100.0 * float(np.count_nonzero(extra[rs, cs])) / union
            zones[name] = {
                "missing_pct": round(miss_pct, 2),
                "extra_pct": round(ext_pct, 2),
                "total_diff_pct": round(miss_pct + ext_pct, 2),
            }

    ch, cw = cr1 - cr0, cc1 - cc0
    mh, mw = mr1 - mr0, mc1 - mc0
    return {
        "iou_pct": round(100.0 * inter / union, 2),
        "precision_pct": round(100.0 * inter / m_area, 2),
        "recall_pct": round(100.0 * inter / c_area, 2),
        "missing_pct": round(100.0 * float(np.count_nonzero(missing)) / union, 2),
        "extra_pct": round(100.0 * float(np.count_nonzero(extra)) / union, 2),
        "concept_wh": [cw, ch],
        "model_wh": [mw, mh],
        "width_ratio": round(mw / max(cw, 1), 3),
        "height_ratio": round(mh / max(ch, 1), 3),
        "edge_deltas_px": {
            "top_concept_minus_model": int(mr0 - cr0),      # >0 means model is lower than concept crown/horn tip
            "bottom_model_minus_concept": int(mr1 - cr1),   # >0 means model extends lower than concept
            "left_concept_minus_model": int(mc0 - cc0),     # >0 means model is narrower on image left
            "right_model_minus_concept": int(mc1 - cc1),    # >0 means model extends wider on image right
        },
        "zones": zones,
    }


def _sobel_edges(rgb: np.ndarray, mask: np.ndarray, thresh: float = 18.0) -> np.ndarray:
    """Return crisp internal + silhouette edge mask for overlay visualization."""
    gray = (rgb.astype(np.float32) @ np.array([0.299, 0.587, 0.114]))
    # Fill background near mean interior tone before gradient so white-edge jump doesn't swamp internal seams
    if mask.any():
        gray = np.where(mask, gray, float(np.median(gray[mask])))
    gx = ndimage.sobel(gray, axis=1)
    gy = ndimage.sobel(gray, axis=0)
    mag = np.hypot(gx, gy) / 4.0
    outline = mask ^ ndimage.binary_erosion(mask, iterations=2)
    return ((mag >= thresh) & mask) | outline


def view_row(
    concept_rgb_full: np.ndarray,
    concept_mask_full: np.ndarray,
    cut_row: int | None,
    aligned_rgba: np.ndarray,
    aligned_mask: np.ndarray,
    padded_concept_fit: np.ndarray,
    metrics: dict,
    label: str,
    pad: int = 32,
    cell_h: int = 440,
) -> Image.Image:
    """Build a 4-column visual comparison row for one view."""
    oh, ow = aligned_mask.shape
    ch, cw = concept_rgb_full.shape[:2]

    # Padded full concept canvas (preserving dimmed pedestal if cut)
    c_canvas = np.full((oh, ow, 3), 245, dtype=np.uint8)
    c_canvas[pad : pad + ch, pad : pad + cw] = concept_rgb_full
    c_full_mask = np.zeros((oh, ow), dtype=bool)
    c_full_mask[pad : pad + ch, pad : pad + cw] = concept_mask_full
    pedestal_mask = c_full_mask & ~padded_concept_fit

    # Col 1: Concept (dim pedestal slightly if cut)
    col1 = c_canvas.copy()
    if pedestal_mask.any():
        col1[pedestal_mask] = (col1[pedestal_mask].astype(float) * 0.45 + 135).astype(np.uint8)

    # Col 2: Blender Model on matching light background
    bg = np.full((oh, ow, 3), 245, dtype=np.uint8)
    alpha = aligned_rgba[..., 3:4].astype(np.float32) / 255.0
    col2 = (aligned_rgba[..., :3].astype(np.float32) * alpha + bg * (1.0 - alpha)).astype(np.uint8)

    # Col 3: 50/50 blend with the concept's edges (magenta), the model's (cyan) and shared edges (yellow).
    col3 = (0.50 * c_canvas.astype(np.float32) + 0.50 * col2.astype(np.float32)).astype(np.uint8)
    c_edges = _sobel_edges(c_canvas, padded_concept_fit, thresh=20.0)
    m_edges = _sobel_edges(col2, aligned_mask, thresh=18.0)
    col3[c_edges] = np.array([230, 55, 175], dtype=np.uint8)
    col3[m_edges] = np.array([25, 200, 235], dtype=np.uint8)
    col3[c_edges & m_edges] = np.array([250, 240, 70], dtype=np.uint8)
    col3_title = "Blend + Edges (Mag=Concept, Cyn=Model)"

    # Col 4: Divergence Map on dark slate ground
    col4 = np.full((oh, ow, 3), 28, dtype=np.uint8)
    if pedestal_mask.any():
        col4[pedestal_mask] = np.array([62, 64, 74], dtype=np.uint8)
    match_m = padded_concept_fit & aligned_mask
    miss_m = padded_concept_fit & ~aligned_mask
    ext_m = aligned_mask & ~padded_concept_fit
    col4[match_m] = np.array([72, 208, 112], dtype=np.uint8)   # Green = match
    col4[miss_m] = np.array([242, 68, 86], dtype=np.uint8)     # Red = concept missing in model
    col4[ext_m] = np.array([48, 198, 248], dtype=np.uint8)     # Cyan = model protrudes past concept

    # Draw 3x3 union grid hairlines onto Col 4 so zone coordinates are readable
    union_mask = padded_concept_fit | aligned_mask
    ur0, ur1, uc0, uc1 = _mask_bbox(union_mask)
    r_edges = np.linspace(ur0, ur1, 4, dtype=int)
    c_edges_grid = np.linspace(uc0, uc1, 4, dtype=int)
    for r in r_edges[1:-1]:
        col4[r : r + 1, uc0:uc1] = np.array([210, 210, 210], dtype=np.uint8)
    for c in c_edges_grid[1:-1]:
        col4[ur0:ur1, c : c + 1] = np.array([210, 210, 210], dtype=np.uint8)

    # Scale each panel uniformly to height = cell_h
    scale = cell_h / max(oh, 1)
    cell_w = max(120, int(round(ow * scale)))
    gap = 10
    header_h = 54
    total_w = 4 * cell_w + 5 * gap
    total_h = header_h + cell_h + 14

    row_img = Image.new("RGB", (total_w, total_h), (20, 22, 28))
    draw = ImageDraw.Draw(row_img)
    font_title = _load_font(16)
    font_sub = _load_font(12)

    top_zones = sorted(
        metrics["zones"].items(),
        key=lambda kv: kv[1]["total_diff_pct"],
        reverse=True,
    )[:3]
    zone_txt = "  |  ".join(
        f"{z}: -{d['missing_pct']:.1f}% +{d['extra_pct']:.1f}%"
        for z, d in top_zones
    )
    ed = metrics["edge_deltas_px"]
    title = (
        f"[{label}]  IoU {metrics['iou_pct']:.1f}%   "
        f"Miss(Red) {metrics['missing_pct']:.1f}%   Extra(Cyan) {metrics['extra_pct']:.1f}%   "
        f"W_ratio {metrics['width_ratio']:.2f}  H_ratio {metrics['height_ratio']:.2f}"
    )
    sub = (
        f"Top diff zones: {zone_txt}   |   "
        f"TopGap:{ed['top_concept_minus_model']:+d}px  BotGap:{ed['bottom_model_minus_concept']:+d}px  "
        f"L_Gap:{ed['left_concept_minus_model']:+d}px  R_Gap:{ed['right_model_minus_concept']:+d}px"
    )
    draw.text((gap, 6), title, font=font_title, fill=(235, 240, 248))
    draw.text((gap, 28), sub, font=font_sub, fill=(175, 185, 200))

    cols_rgb = [
        ("Concept", col1),
        ("Model", col2),
        (col3_title, col3),
        ("Diff (Grn=Match, Red=Miss, Cyn=Extra)", col4),
    ]
    for idx, (col_label, arr) in enumerate(cols_rgb):
        x0 = gap + idx * (cell_w + gap)
        y0 = header_h
        tile = Image.fromarray(arr).resize((cell_w, cell_h), Image.LANCZOS)
        row_img.paste(tile, (x0, y0))
        if idx == 0 and cut_row is not None:
            cut_y = y0 + int(round((pad + cut_row) * scale))
            draw.line([(x0, cut_y), (x0 + cell_w, cut_y)], fill=(0, 195, 225), width=2)
        # Small caption pill at top-left of tile
        draw.rectangle([(x0, y0), (x0 + cell_w, y0 + 20)], fill=(14, 16, 22))
        draw.text((x0 + 6, y0 + 3), col_label, font=font_sub, fill=(215, 222, 232))

    return row_img


AUDIT_SCHEMA = {
    "type": "object",
    "properties": {
        "verdict": {"type": "string", "enum": ["PASS", "NEEDS_REVISION"]},
        "overall_score_10": {"type": "number"},
        "summary": {"type": "string"},
        "junction_issues": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "location": {"type": "string"},
                    "severity": {"type": "string", "enum": ["critical", "major", "minor"]},
                    "issue": {"type": "string"},
                    "fix": {"type": "string"},
                },
                "required": ["location", "severity", "issue", "fix"],
            },
        },
        "shape_proportion_mismatches": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "feature": {"type": "string"},
                    "severity": {"type": "string", "enum": ["critical", "major", "minor"]},
                    "concept_vs_model": {"type": "string"},
                    "fix": {"type": "string"},
                },
                "required": ["feature", "severity", "concept_vs_model", "fix"],
            },
        },
        "shading_feedback": {"type": "string"},
    },
    "required": [
        "verdict",
        "overall_score_10",
        "summary",
        "junction_issues",
        "shape_proportion_mismatches",
        "shading_feedback",
    ],
}



def score(concept_path, renders, out_png, labels=None, out_json=None, shared_scale=True) -> dict:
    """Compare `renders` (`(rgb, mask)` per concept panel, left to right, rendered at one pixel scale) with the
    concept's panels; write the diff sheet `out_png` (and `out_json`) and return the metrics report."""
    labels = labels or [name for name, _ in VIEWS][: len(renders)]
    with Image.open(concept_path) as image:
        panels = mesh.split_views(image, n=len(renders), max_h=10**6)
    fits = [trim_pedestal(view.mask) for view in panels]
    crops = []
    for rgb, covered in renders:
        if not covered.any():
            raise ValueError("A render has no coverage; check the model frame and camera")
        r0, r1, c0, c1 = _mask_bbox(covered)
        alpha = covered[r0:r1, c0:c1].astype(np.uint8) * 255
        crops.append((np.dstack([np.asarray(rgb)[r0:r1, c0:c1], alpha]), covered[r0:r1, c0:c1]))
    scale = None
    if shared_scale:
        free = [align(rgba, m, fit, pad=PAD)[3]["scale"] for (rgba, m), (fit, _) in zip(crops, fits)]
        weights = np.array(SCALE_WEIGHTS[: len(free)], float)
        scale = float(np.dot(weights, free) / weights.sum())
    report = {"concept": str(concept_path), "shared_scale": scale, "views": {}}
    rows = []
    for view, (rgba, m), (fit, cut_row), label in zip(panels, crops, fits, labels):
        aligned_rgba, aligned_mask, padded_fit, info = align(rgba, m, fit, pad=PAD, scale=scale)
        metrics = compute_metrics(padded_fit, aligned_mask)
        metrics["alignment"] = info
        metrics["pedestal_cut_row"] = cut_row
        report["views"][label] = metrics
        rows.append(view_row(view.rgb, view.mask, cut_row, aligned_rgba, aligned_mask, padded_fit, metrics,
                             label=label, pad=PAD))
    report["mean_iou_pct"] = round(float(np.mean([v["iou_pct"] for v in report["views"].values()])), 2)
    banner = 44
    sheet = Image.new("RGB", (max(r.width for r in rows), banner + sum(r.height for r in rows)), (14, 16, 21))
    ImageDraw.Draw(sheet).text((12, 11), f"Concept compare | {Path(concept_path).name} | Mean IoU {report['mean_iou_pct']:.1f}%"
                               + (f" | shared scale {scale:.3f}" if scale else ""), font=_load_font(18), fill=(245, 248, 255))
    y = banner
    for row in rows:
        sheet.paste(row, (0, y))
        y += row.height
    Path(out_png).parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out_png)
    if out_json:
        Path(out_json).write_text(json.dumps(report, indent=2))
    return report


def audit(concept_path, sheet_path, asset_name, brief=None, provider="gemini", out_json=None) -> dict:
    """Ask a vision model to review the comparison sheet against the concept (through `core.ai`'s provider)."""
    from core.ai import get_provider

    prompt = f"""You are a senior 3D art director auditing a low-poly stylized game asset, '{asset_name}', against its concept.
Image 1: the concept turnaround. Image 2: a comparison sheet, one row per view: Concept | Model render | Blend + edges
(magenta = concept, cyan = model) | Silhouette diff (green = match, red = missing in the model, cyan = model extends past
the concept). Grey surfaces in the model render are the host the asset is worn on.
{f"Design brief: {brief}" if brief else ""}
Inspect strictly. Small craftsmanship errors make a model look unfinished even when the overlap is high. Check:
1. Silhouette and proportions in every view: sizes, angles and positions of the major forms.
2. Structural junctions: parts seated on their supports, no floating parts, no clipping, no host showing through.
3. Detail scale and shape language: bevels, facets, insets, cutouts and ornaments versus the concept.
4. Color regions versus the concept.
Respect asymmetry that the concept shows on purpose. Return verdict NEEDS_REVISION if any critical or major issue
remains, PASS only when the model reads like the concept at close inspection."""
    images = [Image.open(concept_path).convert("RGB"), Image.open(sheet_path).convert("RGB")]
    result = get_provider(provider).analyze_json(prompt, images, AUDIT_SCHEMA)
    if out_json:
        Path(out_json).write_text(json.dumps(result, indent=2))
    return result
