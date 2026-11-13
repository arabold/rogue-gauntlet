#!/usr/bin/env python3
"""Compare a multi-view 2D concept sheet against transparent Blender renders.

Splits a white-background turnaround sheet into ordered views via
`core.mesh.split_views`, trims any flared display-bust pedestal below the neck
waist, aligns each transparent RGBA Blender render onto its concept panel by
maximizing silhouette IoU, and outputs both quantitative regional divergence
metrics and a four-column visual diagnostic sheet.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

_STUDIO_DIR = Path(__file__).resolve().parents[1]
if str(_STUDIO_DIR) not in sys.path:
	sys.path.insert(0, str(_STUDIO_DIR))

from core import mesh as mesh_views  # noqa: E402


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


def extract_concept_panels(concept_path: str | Path, n: int = 3, max_h: int = 640) -> list[mesh_views.View]:
	"""Load a white-background turnaround sheet and split it left-to-right into `n` Views."""
	img = Image.open(concept_path).convert("RGB")
	return mesh_views.split_views(img, n=n, max_h=max_h)


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


def load_model_render(png_path: str | Path) -> tuple[np.ndarray, np.ndarray, tuple[int, int, int, int]]:
	"""Load a transparent Blender RGBA render and return `(rgba_crop, alpha_mask_crop, crop_box)`."""
	im = Image.open(png_path).convert("RGBA")
	arr = np.asarray(im)
	mask = arr[..., 3] > 127
	rows, cols = np.flatnonzero(mask.any(1)), np.flatnonzero(mask.any(0))
	if len(rows) == 0 or len(cols) == 0:
		raise ValueError(f"Render has empty alpha mask: {png_path}")
	r0, r1 = int(rows[0]), int(rows[-1]) + 1
	c0, c1 = int(cols[0]), int(cols[-1]) + 1
	return (
		np.ascontiguousarray(arr[r0:r1, c0:c1]),
		np.ascontiguousarray(mask[r0:r1, c0:c1]),
		(r0, r1, c0, c1),
	)


def load_wire_crop(wire_path: str | Path, crop_box: tuple[int, int, int, int]) -> np.ndarray:
	"""Load a transparent wireframe RGBA render and crop to the matching solid render's bbox."""
	arr = np.asarray(Image.open(wire_path).convert("RGBA"))
	r0, r1, c0, c1 = crop_box
	return np.ascontiguousarray(arr[r0:r1, c0:c1])


def _mask_bbox(mask: np.ndarray) -> tuple[int, int, int, int]:
	rows, cols = np.flatnonzero(mask.any(1)), np.flatnonzero(mask.any(0))
	return int(rows[0]), int(rows[-1]) + 1, int(cols[0]), int(cols[-1]) + 1


def _paste_mask(src: np.ndarray, out_h: int, out_w: int, scale: float, dx: int, dy: int, center_x: float, center_y: float) -> np.ndarray:
	"""Scale and place `src` boolean mask into `(out_h, out_w)` centered at `(center_x + dx, center_y + dy)`."""
	sh, sw = src.shape
	nw = max(2, int(round(sw * scale)))
	nh = max(2, int(round(sh * scale)))
	resized = np.asarray(
		Image.fromarray(src.astype(np.uint8) * 255).resize((nw, nh), Image.NEAREST)
	) > 127
	c0 = int(round(center_x + dx - nw * 0.5))
	r0 = int(round(center_y + dy - nh * 0.5))
	canvas = np.zeros((out_h, out_w), dtype=bool)
	sc0, sr0 = max(0, -c0), max(0, -r0)
	dc0, dr0 = max(0, c0), max(0, r0)
	cw = min(nw - sc0, out_w - dc0)
	ch = min(nh - sr0, out_h - dr0)
	if cw > 0 and ch > 0:
		canvas[dr0 : dr0 + ch, dc0 : dc0 + cw] = resized[sr0 : sr0 + ch, sc0 : sc0 + cw]
	return canvas


def _paste_rgba(src_rgba: np.ndarray, out_h: int, out_w: int, scale: float, dx: int, dy: int, center_x: float, center_y: float) -> np.ndarray:
	"""Scale and place `src_rgba` into `(out_h, out_w, 4)` centered at `(center_x + dx, center_y + dy)`."""
	sh, sw = src_rgba.shape[:2]
	nw = max(2, int(round(sw * scale)))
	nh = max(2, int(round(sh * scale)))
	resized = np.asarray(Image.fromarray(src_rgba).resize((nw, nh), Image.LANCZOS))
	c0 = int(round(center_x + dx - nw * 0.5))
	r0 = int(round(center_y + dy - nh * 0.5))
	canvas = np.zeros((out_h, out_w, 4), dtype=np.uint8)
	sc0, sr0 = max(0, -c0), max(0, -r0)
	dc0, dr0 = max(0, c0), max(0, r0)
	cw = min(nw - sc0, out_w - dc0)
	ch = min(nh - sr0, out_h - dr0)
	if cw > 0 and ch > 0:
		canvas[dr0 : dr0 + ch, dc0 : dc0 + cw] = resized[sr0 : sr0 + ch, sc0 : sc0 + cw]
	return canvas


def _iou(a: np.ndarray, b: np.ndarray) -> float:
	inter = int(np.count_nonzero(a & b))
	union = int(np.count_nonzero(a | b))
	return inter / max(union, 1)


def align_model_to_concept(
	model_rgba: np.ndarray,
	model_mask: np.ndarray,
	concept_fit_mask: np.ndarray,
	pad: int = 32,
) -> tuple[np.ndarray, np.ndarray, np.ndarray, dict]:
	"""Pad canvas, search `(scale, dx, dy)` maximizing silhouette IoU, and return aligned layers.

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
	cx_ds = cx0 / ds
	cy_ds = cy0 / ds

	best = (-1.0, k_init, 0, 0)
	step_xy = max(1, int(round(0.025 * max(c_ds_w, c_ds_h))))
	range_x = range(-int(round(0.14 * c_ds_w)), int(round(0.14 * c_ds_w)) + 1, step_xy)
	range_y = range(-int(round(0.14 * c_ds_h)), int(round(0.14 * c_ds_h)) + 1, step_xy)

	for ks in np.linspace(0.76, 1.24, 17):
		k_try = k_init * float(ks)
		k_ds = k_try / ds
		nw = max(2, int(round(mw * k_ds)))
		nh = max(2, int(round(mh * k_ds)))
		resized = np.asarray(
			Image.fromarray(model_mask.astype(np.uint8) * 255).resize((nw, nh), Image.NEAREST)
		) > 127
		for dx_ds in range_x:
			for dy_ds in range_y:
				c0 = int(round(cx_ds + dx_ds - nw * 0.5))
				r0 = int(round(cy_ds + dy_ds - nh * 0.5))
				canvas = np.zeros_like(c_small)
				sc0, sr0 = max(0, -c0), max(0, -r0)
				dc0, dr0 = max(0, c0), max(0, r0)
				bw = min(nw - sc0, c_ds_w - dc0)
				bh = min(nh - sr0, c_ds_h - dr0)
				if bw <= 0 or bh <= 0:
					continue
				canvas[dr0 : dr0 + bh, dc0 : dc0 + bw] = resized[sr0 : sr0 + bh, sc0 : sc0 + bw]
				score = _iou(canvas, c_small)
				if score > best[0]:
					best = (score, k_try, int(round(dx_ds * ds)), int(round(dy_ds * ds)))

	# Fine refinement at full working resolution around coarse winner.
	_, k_c, dx_c, dy_c = best
	oh, ow = padded_concept.shape
	fine_best = (-1.0, k_c, dx_c, dy_c)
	for ks in np.linspace(0.95, 1.05, 7):
		k_try = k_c * float(ks)
		for ddx in range(-8, 9, 2):
			for ddy in range(-8, 9, 2):
				m_try = _paste_mask(model_mask, oh, ow, k_try, dx_c + ddx, dy_c + ddy, cx0, cy0)
				score = _iou(m_try, padded_concept)
				if score > fine_best[0]:
					fine_best = (score, k_try, dx_c + ddx, dy_c + ddy)

	score, k_opt, dx_opt, dy_opt = fine_best
	# Final 1-pixel nudge
	for ddx in (-1, 0, 1):
		for ddy in (-1, 0, 1):
			m_try = _paste_mask(model_mask, oh, ow, k_opt, dx_opt + ddx, dy_opt + ddy, cx0, cy0)
			s = _iou(m_try, padded_concept)
			if s > score:
				score, dx_opt, dy_opt = s, dx_opt + ddx, dy_opt + ddy

	aligned_mask = _paste_mask(model_mask, oh, ow, k_opt, dx_opt, dy_opt, cx0, cy0)
	aligned_rgba = _paste_rgba(model_rgba, oh, ow, k_opt, dx_opt, dy_opt, cx0, cy0)
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


def _build_view_row(
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
	aligned_wire: np.ndarray | None = None,
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

	# Col 3: Either crisp 100% Concept + Hidden-Line 3D Wireframe Overlay (preferred),
	# or 50/50 Alpha Blend + Colored Edges fallback when no wireframe pass is supplied.
	if aligned_wire is not None:
		w_alpha = np.clip((aligned_wire[..., 3:4].astype(np.float32) / 255.0) * 1.4, 0.0, 1.0)
		col3 = (aligned_wire[..., :3].astype(np.float32) * w_alpha + col1.astype(np.float32) * (1.0 - w_alpha)).astype(np.uint8)
		col3_title = "Wireframe on Concept (Cyn=Model, Mag=Head)"
	else:
		col3 = (0.50 * c_canvas.astype(np.float32) + 0.50 * col2.astype(np.float32)).astype(np.uint8)
		c_edges = _sobel_edges(c_canvas, padded_concept_fit, thresh=20.0)
		m_edges = _sobel_edges(col2, aligned_mask, thresh=18.0)
		col3[c_edges] = np.array([230, 55, 175], dtype=np.uint8)
		col3[m_edges] = np.array([25, 200, 235], dtype=np.uint8)
		both_edges = c_edges & m_edges
		col3[both_edges] = np.array([250, 240, 70], dtype=np.uint8)
		col3_title = "50/50 Blend + Edges"

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
		("Blender Model", col2),
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


def compare_views(
	concept_path: str | Path,
	render_paths: list[str | Path],
	out_png: str | Path,
	labels: list[str] | None = None,
	out_json: str | Path | None = None,
	wire_paths: list[str | Path] | None = None,
) -> dict:
	"""Compare a `n`-panel concept turnaround against matching transparent Blender renders."""
	n = len(render_paths)
	labels = labels or [f"View {i}" for i in range(n)]
	if wire_paths is not None and len(wire_paths) != n:
		raise ValueError(f"Expected {n} wire renders, got {len(wire_paths)}")
	panels = extract_concept_panels(concept_path, n=n, max_h=640)

	pad = 36
	rows_imgs = []
	report = {"concept": str(concept_path), "views": {}}

	for idx, (view, render_path, label) in enumerate(zip(panels, render_paths, labels)):
		concept_fit, cut_row = trim_pedestal(view.mask)
		model_rgba, model_mask, crop_box = load_model_render(render_path)
		aligned_rgba, aligned_mask, padded_concept_fit, align_info = align_model_to_concept(
			model_rgba, model_mask, concept_fit, pad=pad
		)
		aligned_wire = None
		if wire_paths is not None:
			wire_crop = load_wire_crop(wire_paths[idx], crop_box)
			oh, ow = aligned_mask.shape
			aligned_wire = _paste_rgba(
				wire_crop,
				oh,
				ow,
				align_info["scale"],
				align_info["dx"],
				align_info["dy"],
				align_info["center_x"],
				align_info["center_y"],
			)
		metrics = compute_metrics(padded_concept_fit, aligned_mask)
		metrics["alignment"] = align_info
		metrics["pedestal_cut_row"] = int(cut_row) if cut_row is not None else None
		report["views"][label] = metrics
		rows_imgs.append(
			_build_view_row(
				view.rgb,
				view.mask,
				cut_row,
				aligned_rgba,
				aligned_mask,
				padded_concept_fit,
				metrics,
				label=label,
				pad=pad,
				aligned_wire=aligned_wire,
			)
		)

	mean_iou = float(np.mean([v["iou_pct"] for v in report["views"].values()]))
	report["mean_iou_pct"] = round(mean_iou, 2)

	max_w = max(im.width for im in rows_imgs)
	banner_h = 44
	total_h = banner_h + sum(im.height for im in rows_imgs)
	sheet = Image.new("RGB", (max_w, total_h), (14, 16, 21))
	draw = ImageDraw.Draw(sheet)
	font_banner = _load_font(18)
	draw.text(
		(12, 11),
		f"Concept vs Blender Compare  |  {Path(concept_path).name}  |  Mean IoU: {mean_iou:.1f}%",
		font=font_banner,
		fill=(245, 248, 255),
	)
	y = banner_h
	for im in rows_imgs:
		sheet.paste(im, (0, y))
		y += im.height

	os.makedirs(os.path.dirname(os.path.abspath(out_png)), exist_ok=True)
	sheet.save(out_png)
	if out_json:
		os.makedirs(os.path.dirname(os.path.abspath(out_json)), exist_ok=True)
		with open(out_json, "w") as f:
			json.dump(report, f, indent=2)
	return report


_AUDIT_SCHEMA = {
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


def build_closeup_sheet(
	closeup_paths: list[str | Path],
	out_png: str | Path,
	labels: list[str] | None = None,
	cell_size: int = 640,
) -> Path:
	"""Composite close-up RGBA junction renders onto a labeled 2x2 studio sheet."""
	default_labels = [
		"1. Front-3/4 Brow, Nasal & Cheek",
		"2. Left Socket / Temple Join",
		"3. Right Socket / Temple Join",
		"4. Rear Crown-to-Band Join",
	]
	use_labels = labels or default_labels[: len(closeup_paths)]
	cols = 2
	rows = (len(closeup_paths) + 1) // 2
	gap = 14
	banner_h = 44
	sheet_w = cols * cell_size + (cols + 1) * gap
	sheet_h = banner_h + rows * (cell_size + gap) + gap
	sheet = Image.new("RGB", (sheet_w, sheet_h), (18, 21, 28))
	draw = ImageDraw.Draw(sheet)
	font_banner = _load_font(18)
	font_sub = _load_font(14)
	draw.text(
		(gap, 11),
		"Macro Seam & Junction Inspection Sheet (Look for Clipping, Cutoffs & Gaps)",
		font=font_banner,
		fill=(242, 246, 255),
	)

	for idx, fp in enumerate(closeup_paths):
		r, c = divmod(idx, cols)
		x0 = gap + c * (cell_size + gap)
		y0 = banner_h + gap + r * (cell_size + gap)
		rgba = Image.open(fp).convert("RGBA")
		bg = Image.new("RGBA", rgba.size, (242, 243, 247, 255))
		comp = Image.alpha_composite(bg, rgba).convert("RGB").resize((cell_size, cell_size), Image.LANCZOS)
		sheet.paste(comp, (x0, y0))
		lbl = use_labels[idx] if idx < len(use_labels) else f"Closeup {idx + 1}"
		draw.rectangle([(x0, y0), (x0 + cell_size, y0 + 24)], fill=(14, 16, 22))
		draw.text((x0 + 8, y0 + 4), lbl, font=font_sub, fill=(220, 228, 240))

	out_path = Path(out_png)
	out_path.parent.mkdir(parents=True, exist_ok=True)
	sheet.save(out_path)
	return out_path


def audit_with_gemini(
	concept_path: str | Path,
	diag_path: str | Path,
	closeup_sheet_path: str | Path | None = None,
	asset_name: str = "headgear",
	text_model: str = "gemini-3.8-flash",
	out_json: str | Path | None = None,
) -> dict:
	"""Programmatically audit Blender renders + wireframe overlays + seam closeups via Gemini Flash."""
	if "GEMINI_API_KEY" not in os.environ and os.environ.get("SKILL_IMAGE_GEN_GEMINI_KEY"):
		os.environ["GEMINI_API_KEY"] = os.environ["SKILL_IMAGE_GEN_GEMINI_KEY"]

	from core.ai import GeminiProvider  # noqa: E402

	images = [
		Image.open(concept_path).convert("RGB"),
		Image.open(diag_path).convert("RGB"),
	]
	has_closeups = closeup_sheet_path is not None and Path(closeup_sheet_path).exists()
	if has_closeups:
		images.append(Image.open(closeup_sheet_path).convert("RGB"))

	closeup_note = (
		"Image 3 is a 2x2 zoomed Macro Seam & Junction sheet showing front-3/4 brow/nasal/cheek, "
		"left socket/temple, right socket/temple, and rear crown-to-band join."
		if has_closeups
		else "Inspect seams carefully across every available angle in Image 2."
	)
	prompt = f"""You are a senior 3D game character/prop art director auditing a low-poly stylized KayKit-style '{asset_name}'.

Images provided:
- Image 1: Canonical 2D concept turnaround reference.
- Image 2: 4-column comparison diagnostic sheet (Concept | Blender Model | Wireframe on Concept [cyan=model, magenta=head] | Silhouette Diff [red=missing, cyan=extra]).
- {closeup_note}

Inspect strictly and skeptically. Small craftsmanship errors make a model look unfinished even when overall IoU is high (~90%). Check ALL four categories explicitly:
1. STRUCTURAL JUNCTIONS & PHYSICAL PLAUSIBILITY (check Image 3 macro views closely):
   - Do sockets/collars/cuffs/mounts seat flush and unbroken around the outside of curved domes, OR does one side clip/sink diagonally into the helmet shell, or twist/pinch where feathers or horns exit a circular boss?
   - Do vertical crests, ridges, or metal straps run continuously and terminate cleanly INTO horizontal bands/rims on BOTH front AND rear, OR does a band stop abruptly mid-helmet leaving a floating stub/gap?
   - Do straps, rivets, cheek/ear plates, and trim hug the underlying curvature cleanly without floating gaps or dummy-head bleed-through (especially around the boxy 135-degree rear-diagonal corners of the KayKit head)?
2. PROPORTIONS, SILHOUETTE & SHAPE LANGUAGE (compare Image 1 vs Image 2 solid & cyan wireframe overlay):
   - Compare small detail scale and canonical cutouts (e.g., recessed ventilation slits/grooves, brass rivet/stud size and spacing, strap thickness, horn/wing base width/taper/sweep, nasal bar bevel/angle vs boxy rectangle, curved cheek/ear guards wrapping the head vs flat slabs, natural lower-in-back helmet rake).
3. INTENTIONAL VS ACCIDENTAL ASYMMETRY:
   - Respect intentional design asymmetry from Image 1 (such as a broken horn tip), while flagging unintentional asymmetrical mounting/clipping.
4. SHADING, VALUE CONTRAST & MATERIAL READ:
   - Evaluate whether the Blender render shows crisp key/fill/rim light contrast and clear separation between steel, brass/gold rivets, leather straps/cuffs, gems, and bone — or looks overly flat/washed-out or has unsplit bevel shading streaks.

Return `verdict: "NEEDS_REVISION"` if ANY critical or major junction defect, mid-surface cutoff, clipping socket, dummy-head bleed-through, or clear shape/proportion mismatch remains; return `"PASS"` only when geometry, seams, proportions, and shading read cleanly at close inspection."""

	provider = GeminiProvider(text_model=text_model)
	result = provider.analyze_json(prompt, images, _AUDIT_SCHEMA)
	result["model"] = text_model
	if out_json:
		Path(out_json).parent.mkdir(parents=True, exist_ok=True)
		with open(out_json, "w") as f:
			json.dump(result, f, indent=2)
	return result


def main() -> None:
	parser = argparse.ArgumentParser(description="Overlay and compare concept turnaround views vs Blender renders.")
	parser.add_argument("concept_pos", nargs="?", help="Path to multi-view white-background concept image")
	parser.add_argument("renders", nargs="*", help="Transparent RGBA Blender render PNGs matching panels left-to-right")
	parser.add_argument("--concept", help="Path to multi-view white-background concept image (optional flag form)")
	parser.add_argument("--render-dir", help="Directory containing `<view>_<prefix>_solid.png` renders (used with --prefix)")
	parser.add_argument("--prefix", help="Render prefix from `render_compare_passes` / `render_junction_closeups`")
	parser.add_argument("--wires", nargs="+", help="Optional hidden-line wireframe RGBA PNGs matching `renders` order")
	parser.add_argument("--closeups", nargs="+", help="Optional closeup RGBA PNGs to stitch into a 2x2 junction sheet")
	parser.add_argument("--closeups-out", help="Output path for 2x2 closeup junction sheet")
	parser.add_argument("--out", required=True, help="Output diagnostic PNG path")
	parser.add_argument("--json", help="Optional JSON metrics path")
	parser.add_argument("--labels", default="", help="Comma-separated panel names")
	parser.add_argument("--audit", action="store_true", help="Run Gemini Flash visual seam/shape audit after compare")
	parser.add_argument("--audit-model", default="gemini-3.8-flash", help="Gemini model ID for --audit")
	parser.add_argument("--asset-name", default="headgear", help="Asset name hint for --audit")
	parser.add_argument("--audit-out", help="Optional JSON path for --audit output")
	args = parser.parse_args()

	concept_path = args.concept or args.concept_pos
	renders = list(args.renders or [])
	if args.concept and args.concept_pos:
		renders = [args.concept_pos] + renders
	if not concept_path:
		parser.error("Provide a concept image path either positionally or via --concept.")

	wires = args.wires
	closeups = args.closeups
	if args.render_dir and args.prefix:
		rdir = Path(args.render_dir)
		pfx = args.prefix
		if not renders:
			renders = [str(rdir / f"{v}_{pfx}_solid.png") for v in ("front", "tq", "side")]
		if not wires:
			cand_wires = [rdir / f"{v}_{pfx}_wire.png" for v in ("front", "tq", "side")]
			if all(p.exists() for p in cand_wires):
				wires = [str(p) for p in cand_wires]
		if not closeups:
			cand_closeups = [
				rdir / f"closeup_{c}_{pfx}_closeup.png" for c in ("brow", "left", "right", "rear")
			]
			if all(p.exists() for p in cand_closeups):
				closeups = [str(p) for p in cand_closeups]

	if not renders:
		parser.error("Provide render PNG paths positionally or via `--render-dir <dir> --prefix <prefix>`.")

	labels = [s.strip() for s in args.labels.split(",") if s.strip()] or ["Front", "Three-Quarter", "Side"][: len(renders)]
	report = compare_views(concept_path, renders, args.out, labels=labels, out_json=args.json, wire_paths=wires)
	print(f"Saved diagnostic sheet: {args.out} (Mean IoU = {report['mean_iou_pct']:.2f}%)")
	for name, m in report["views"].items():
		print(
			f"  {name}: IoU={m['iou_pct']:.1f}%  Miss={m['missing_pct']:.1f}%  "
			f"Extra={m['extra_pct']:.1f}%  W_ratio={m['width_ratio']:.2f}  H_ratio={m['height_ratio']:.2f}"
		)

	views_dict = report["views"]
	if "Front" in views_dict and "Side" in views_dict:
		fw = views_dict["Front"]["width_ratio"]
		sw = views_dict["Side"]["width_ratio"]
		fh = views_dict["Front"]["height_ratio"]
		print(
			f"  [3D Axis Hint] Lateral X width ratio (Front) = {fw:.2f} | "
			f"Front-to-back Y depth/sweep ratio (Side) = {sw:.2f} | "
			f"Vertical Z height ratio = {fh:.2f} "
			"(Note: Three-Quarter horizontal screen axis u_right = +0.81*X + 0.59*Y; decouple X in Front and Y in Side!)"
		)

	closeup_sheet = None
	if closeups:
		out_p = Path(args.out)
		closeup_sheet = Path(args.closeups_out) if args.closeups_out else out_p.with_name(f"{out_p.stem}_closeups.png")
		build_closeup_sheet(closeups, closeup_sheet)
		print(f"Saved closeup junction sheet: {closeup_sheet}")

	if args.audit:
		out_p = Path(args.out)
		audit_json = Path(args.audit_out) if args.audit_out else out_p.with_name(f"{out_p.stem}_audit.json")
		audit = audit_with_gemini(
			concept_path,
			args.out,
			closeup_sheet_path=closeup_sheet,
			asset_name=args.asset_name,
			text_model=args.audit_model,
			out_json=audit_json,
		)
		print(f"\n[Gemini Audit: {audit['model']}] Verdict={audit['verdict']}  Score={audit['overall_score_10']}/10")
		print(f"Summary: {audit['summary']}")
		for issue in audit.get("junction_issues", []):
			print(f"  [JUNCTION:{issue['severity'].upper()}] {issue['location']}: {issue['issue']} -> Fix: {issue['fix']}")
		for mm in audit.get("shape_proportion_mismatches", []):
			print(f"  [SHAPE:{mm['severity'].upper()}] {mm['feature']}: {mm['concept_vs_model']} -> Fix: {mm['fix']}")
		print(f"  [SHADING] {audit.get('shading_feedback', '')}")


def compose_character_showcase(
	render_dir: str | Path,
	prefix: str,
	out_sheet: str | Path,
	out_gif: str | Path | None = None,
	title: str = "KayKit Character Headgear Showcase",
	fps: int = 18,
) -> tuple[Path, Path | None]:
	"""Compose the 6-panel character showcase sheet (2 portraits + 4 full-body views) and optional 360 turntable GIF."""
	rdir = Path(render_dir)
	bg_top = np.array([28, 32, 42], dtype=np.float32)
	bg_bot = np.array([16, 18, 24], dtype=np.float32)

	def load_on_grad(fp: Path, target_w: int, target_h: int) -> Image.Image:
		im = Image.open(fp).convert("RGBA").resize((target_w, target_h), Image.LANCZOS)
		arr = np.asarray(im, dtype=np.float32)
		alpha = arr[..., 3:4] / 255.0
		t = np.linspace(0.0, 1.0, target_h, dtype=np.float32)[:, None, None]
		grad = bg_top * (1.0 - t) + bg_bot * t
		grad = np.broadcast_to(grad, (target_h, target_w, 3))
		comp = arr[..., :3] * alpha + grad * (1.0 - alpha)
		return Image.fromarray(np.clip(comp, 0, 255).astype(np.uint8))

	pad = 16
	header_h = 54
	label_h = 30
	total_w = 1860
	row1_w = (total_w - 3 * pad) // 2
	row1_h = 680
	row2_w = (total_w - 5 * pad) // 4
	row2_h = 520
	total_h = header_h + pad + (row1_h + label_h) + pad + (row2_h + label_h) + pad

	sheet = Image.new("RGB", (total_w, total_h), (13, 15, 20))
	draw = ImageDraw.Draw(sheet)
	font_title = _load_font(22)
	font_lbl = _load_font(15)

	draw.text((pad, 15), title, fill=(235, 240, 248), font=font_title)

	p_specs = [
		(rdir / f"{prefix}_portrait_front.png", "1. Portrait Closeup (Front-Left)"),
		(rdir / f"{prefix}_portrait_tq.png", "2. Portrait Closeup (Three-Quarter)"),
	]
	y1 = header_h + pad
	for idx, (fp, lbl) in enumerate(p_specs):
		x = pad + idx * (row1_w + pad)
		draw.rectangle([x, y1, x + row1_w, y1 + label_h], fill=(24, 28, 36))
		draw.text((x + 10, y1 + 6), lbl, fill=(200, 215, 235), font=font_lbl)
		im_src = Image.open(fp).convert("RGBA")
		w, h = im_src.size
		crop_h = int(w * (row1_h / row1_w))
		top = max(0, (h - crop_h) // 2 - int(0.04 * h))
		im_crop = im_src.crop((0, top, w, min(h, top + crop_h))).resize((row1_w, row1_h), Image.LANCZOS)
		arr = np.asarray(im_crop, dtype=np.float32)
		alpha = arr[..., 3:4] / 255.0
		t = np.linspace(0.0, 1.0, row1_h, dtype=np.float32)[:, None, None]
		grad = np.broadcast_to(bg_top * (1.0 - t) + bg_bot * t, (row1_h, row1_w, 3))
		comp = Image.fromarray(np.clip(arr[..., :3] * alpha + grad * (1.0 - alpha), 0, 255).astype(np.uint8))
		sheet.paste(comp, (x, y1 + label_h))

	f_specs = [
		(rdir / f"{prefix}_full_front.png", "3. Full Character — Front"),
		(rdir / f"{prefix}_full_tq.png", "4. Full Character — Three-Quarter"),
		(rdir / f"{prefix}_full_side.png", "5. Full Character — Side Profile"),
		(rdir / f"{prefix}_full_back.png", "6. Full Character — Rear View"),
	]
	y2 = y1 + label_h + row1_h + pad
	for idx, (fp, lbl) in enumerate(f_specs):
		x = pad + idx * (row2_w + pad)
		draw.rectangle([x, y2, x + row2_w, y2 + label_h], fill=(24, 28, 36))
		draw.text((x + 10, y2 + 6), lbl, fill=(200, 215, 235), font=font_lbl)
		comp = load_on_grad(fp, row2_w, row2_h)
		sheet.paste(comp, (x, y2 + label_h))

	out_sheet_p = Path(out_sheet)
	sheet.save(out_sheet_p)

	out_gif_p = None
	if out_gif is not None:
		turn_files = sorted(rdir.glob(f"{prefix}_turn_*.png"))
		if turn_files:
			frames = [
				load_on_grad(fp, 480, 480).quantize(colors=256, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.FLOYDSTEINBERG)
				for fp in turn_files
			]
			out_gif_p = Path(out_gif)
			frames[0].save(
				out_gif_p,
				save_all=True,
				append_images=frames[1:],
				duration=int(round(1000.0 / fps)),
				loop=0,
				optimize=False,
			)

	return out_sheet_p, out_gif_p


if __name__ == "__main__":
	main()
