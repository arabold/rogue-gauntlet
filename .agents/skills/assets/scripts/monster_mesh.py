#!/usr/bin/env python3
"""
Image -> part geometry for the monster pipeline (`build_monster.py`).

Building blocks, all generic (nothing here knows which monster it is building):
  - `View` / `split_views`: one orthographic panel of a part image (silhouette mask, per-row spans, labels).
    A part image holds its views side by side (front | side | back).
  - `segment_body`: cuts a bare-body T-pose turnaround into head / torso / arm / leg views by landmarks
    (neck, armpits, crotch) and mirrors the side view when it faces left.
  - `build_palette` / `quantize_view`: the flat KayKit look. A small palette is clustered from all part images
    (Lab k-means), then every view becomes a label map (nearest palette color, majority despeckle, small-island
    merge), so painted noise and shading collapse into a few flat color regions.
  - `LoftPart`: the default primitive build, superellipse rings sized per row from the front and side views,
    kept only where the outline bends (low-poly by construction).
  - `SolidPart`: a sculpted volume for shapes rings can't capture: silhouette extrusions intersected with a
    per-row superellipse cross-section, polygonized with surface nets and decimated.
  - `WrapPart`: a band (belt, collar, cuff, headband) around a host part's cross-section whose edges follow the
    band image's silhouette, so scalloped hems, crests and hanging aprons survive.
  - `Atlas` / `flat_colored`: one flat palette color per face, through a strip of palette swatches.

Part-local frame: y in [0, length] bottom -> top, +x = front-view image right, +z = toward the viewer of the
front view (the character's front). `build_monster.py` maps this frame onto the rig.
"""

import json
import struct

import numpy as np
from PIL import Image
from scipy import ndimage

import adaptive_color_subdivision as acs

BG = -1


# ---------------------------------------------------------------------------
# Views
# ---------------------------------------------------------------------------
def _runs(row):
    """`[(start, end_exclusive), ...]` of consecutive True values."""
    d = np.diff(np.concatenate([[0], row.astype(np.int8), [0]]))
    return list(zip(np.flatnonzero(d == 1).tolist(), np.flatnonzero(d == -1).tolist()))


def foreground_mask(rgb, white=228, chroma=28, min_frac=0.002):
    """
    Everything but the near-white background, minus tiny specks: near-white regions connected to the image border,
    and enclosed ones that are mostly paper white, the background seen through a hole (a chain link, a crown's
    opening). Characters are drawn without pure white (`build_monster.FLAT`), so tinted eye whites and teeth stay.
    """
    rgb = np.asarray(rgb, np.int16)
    spread = rgb.max(-1) - rgb.min(-1)
    whiteish = (rgb.min(-1) >= white) & (spread <= chroma)
    lab, n = ndimage.label(whiteish)
    border = np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))
    paper = ndimage.mean((rgb.min(-1) >= 250) & (spread <= 6), lab, np.arange(1, n + 1)) if n else np.zeros(0)
    holes = 1 + np.flatnonzero(paper >= 0.5)
    fg = ~np.isin(lab, np.union1d(border[border > 0], holes))
    fg = ndimage.binary_opening(fg, iterations=1)
    comp, n = ndimage.label(fg)
    if n:
        sizes = ndimage.sum(fg, comp, range(1, n + 1))
        fg = np.isin(comp, 1 + np.flatnonzero(sizes >= min_frac * sizes.max()))
    return fg


def _view_masks_by_columns(fg, n):
    """Views separated by empty columns; merges the narrowest gaps until `n` groups remain (None if < n)."""
    runs = _runs(fg.any(0))
    if len(runs) < n:
        return None
    while len(runs) > n:
        gaps = [runs[i + 1][0] - runs[i][1] for i in range(len(runs) - 1)]
        i = int(np.argmin(gaps))
        runs[i] = (runs[i][0], runs[i + 1][1])
        del runs[i + 1]
    masks = []
    for c0, c1 in runs:
        m = np.zeros_like(fg)
        m[:, c0:c1] = fg[:, c0:c1]
        masks.append(m)
    return masks


def _view_masks_by_components(fg, n):
    """
    Views that overlap in x (a T-pose fist reaching past the next view's edge): the `n` largest connected
    components are the views; smaller pieces (a detached strap, a gap-separated eye) join the nearest one.
    """
    comp, k = ndimage.label(fg)
    if k < n:
        return None
    sizes = ndimage.sum(fg, comp, range(1, k + 1))
    big = 1 + np.argsort(-sizes)[:n]
    if sizes[big[-1] - 1] < 0.15 * sizes[big[0] - 1]:
        return None  # the n-th piece is a fragment, not a view
    owner = np.where(np.isin(comp, big), comp, 0)
    near = ndimage.distance_transform_edt(owner == 0, return_distances=False, return_indices=True)
    owner = np.where(fg, owner[near[0], near[1]], 0)
    big = sorted(big, key=lambda c: np.flatnonzero((owner == c).any(0)).mean())
    return [owner == c for c in big]


def _drop_strays(m, rel_gap=0.05):
    """
    Keeps a view's largest piece plus pieces within `rel_gap` x its diagonal (a strap hanging off an arm);
    drops anything further away, such as captions the image model wrote under a view despite being told not to.
    """
    comp, n = ndimage.label(m)
    if n <= 1:
        return m
    sizes = ndimage.sum(m, comp, range(1, n + 1))
    main = comp == 1 + int(np.argmax(sizes))
    rows, cols = np.flatnonzero(main.any(1)), np.flatnonzero(main.any(0))
    limit = max(8.0, rel_gap * np.hypot(rows[-1] - rows[0], cols[-1] - cols[0]))
    dist = ndimage.distance_transform_edt(~main)
    keep = main.copy()
    big = 0.15 * main.sum()
    for i in range(1, n + 1):
        piece = comp == i
        if piece[main].any() or dist[piece].min() > limit:
            continue
        pr = np.flatnonzero(piece.any(1))
        beside = pr[-1] >= rows[0] and pr[0] <= rows[-1]  # captions sit wholly below (or above) the figure
        if beside or piece.sum() > big:
            keep |= piece
    return keep


def split_views(img, n, max_h=512):
    """Splits an image holding `n` views side by side into `View`s, left -> right."""
    rgb = np.asarray(img.convert("RGB"))
    fg = foreground_mask(rgb)
    masks = _view_masks_by_columns(fg, n) or _view_masks_by_components(fg, n)
    if masks is None:
        raise ValueError(f"could not find {n} separate views")
    views = []
    for m in (_drop_strays(m) for m in masks):
        rows, cols = np.flatnonzero(m.any(1)), np.flatnonzero(m.any(0))
        r0, r1, c0, c1 = rows[0], rows[-1] + 1, cols[0], cols[-1] + 1
        v = View(rgb[r0:r1, c0:c1], m[r0:r1, c0:c1], max_h=max_h)
        v.src_box = (int(c0), int(r0), int(c1), int(r1))  # crop rectangle in the source image
        views.append(v)
    return views


class View:
    """
    One orthographic panel: RGB crop, silhouette mask and per-row silhouette spans. Pixel column `c` covers
    `[c, c + 1)`; `axis` is the column (in the same edge units) the part's bone runs through, estimated as the
    median span centre of the upper 70% of rows so feet, fists or noses don't pull it sideways. `src_h`/`src_w`
    keep the crop's size before downscaling, for measuring it against other crops of the same drawing.
    """

    def __init__(self, rgb, mask, max_h=512):
        self.src_h, self.src_w = mask.shape
        if mask.shape[0] > max_h:
            f = max_h / mask.shape[0]
            size = (max(2, round(mask.shape[1] * f)), max_h)
            rgb = np.asarray(Image.fromarray(np.ascontiguousarray(rgb)).resize(size, Image.LANCZOS))
            mask = np.asarray(Image.fromarray(mask.astype(np.uint8) * 255).resize(size, Image.NEAREST)) > 127
        self.rgb, self.mask = np.ascontiguousarray(rgb), mask
        self.h, self.w = mask.shape
        self.labels = self.fill = None
        self._spans()

    def _spans(self):
        h, w = self.h, self.w
        cols = np.arange(w)
        self.span_c = np.zeros((h, w))  # centre of the run nearest to each pixel
        self.span_h = np.zeros((h, w))  # half width of that run
        self.span_main = np.zeros((h, w), bool)  # that run is the widest of its row
        lo, hi = np.full(h, np.nan), np.full(h, np.nan)
        main_c, main_h = np.full(h, np.nan), np.full(h, np.nan)
        for j in range(h):
            runs = _runs(self.mask[j])
            if not runs:
                continue
            st = np.array([r[0] for r in runs], float)
            en = np.array([r[1] for r in runs], float)
            lo[j], hi[j] = st[0], en[-1]
            d = np.maximum(st[None] - cols[:, None], 0) + np.maximum(cols[:, None] + 1 - en[None], 0)
            k = d.argmin(1)
            widest = int(np.argmax(en - st))
            self.span_c[j] = 0.5 * (st[k] + en[k])
            self.span_h[j] = 0.5 * (en[k] - st[k])
            self.span_main[j] = k == widest
            main_c[j], main_h[j] = 0.5 * (st[widest] + en[widest]), 0.5 * (en[widest] - st[widest])
        filled = np.flatnonzero(~np.isnan(lo))
        for arr in (lo, hi, main_c, main_h):
            arr[:] = np.interp(np.arange(h), filled, arr[filled])
        for j in np.flatnonzero(~self.mask.any(1)):
            src = filled[np.abs(filled - j).argmin()]
            self.span_c[j], self.span_h[j], self.span_main[j] = self.span_c[src], self.span_h[src], self.span_main[src]
        self.row_lo, self.row_hi, self.main_c, self.main_h = lo, hi, main_c, main_h
        upper = filled[filled < max(1, int(0.7 * h))]
        self.axis = float(np.median(0.5 * (lo[upper] + hi[upper])))

    @property
    def secondary_fraction(self):
        """Share of the silhouette outside each row's widest run (ears, straps, horns beside the main shape)."""
        return 1.0 - float((self.mask & self.span_main).sum()) / max(1, int(self.mask.sum()))

    def sdf(self, sy, sx):
        """Signed distance to the silhouette in world units (`sy`, `sx` = world size of a pixel), padded."""
        pad = max(6, int(0.08 * max(self.h, self.w)))
        m = np.pad(self.mask, pad)
        d = ndimage.distance_transform_edt(~m, sampling=(sy, sx)) - ndimage.distance_transform_edt(m, sampling=(sy, sx))
        return d, pad

    def set_labels(self, labels):
        self.labels = labels
        idx = ndimage.distance_transform_edt(labels < 0, return_distances=False, return_indices=True)
        self.fill = labels[idx[0], idx[1]]  # nearest foreground label everywhere (for lookups just off the edge)

    def label_at(self, r, c, cut=False):
        """Labels at fractional pixel coordinates (row, col); `cut` returns BG outside the silhouette."""
        ri = np.clip(np.floor(r).astype(int), 0, self.h - 1)
        ci = np.clip(np.floor(c).astype(int), 0, self.w - 1)
        if not cut:
            return self.fill[ri, ci]
        inside = (r >= 0) & (r < self.h) & (c >= 0) & (c < self.w)
        return np.where(inside, self.labels[ri, ci], BG)

    def clean_image(self):
        """The crop with everything outside this view's silhouette painted white."""
        return Image.fromarray(np.where(self.mask[..., None], self.rgb, 255).astype(np.uint8))

    def label_image(self, palette):
        rgb = np.full((self.h, self.w, 3), 255, np.uint8)
        fg = self.labels >= 0
        rgb[fg] = np.asarray(palette, np.uint8)[self.labels[fg]]
        return Image.fromarray(rgb)


# ---------------------------------------------------------------------------
# Body segmentation (bare-body T-pose turnaround -> head / torso / arm / leg views)
# ---------------------------------------------------------------------------
def _row_runs(mask):
    return [_runs(mask[j]) for j in range(mask.shape[0])]


def body_landmarks(mask):
    """
    T-pose landmarks of a front (or back) silhouette, in pixels of that crop:
      - arm rows: rows whose widest run spans >70% of the widest row (both arms joined to the torso),
      - neck: the narrowest row between the head's widest row and the arm rows,
      - torso edges `tx0..tx1`: the body's extent just below the armpits,
      - crotch: top of the block of rows (walking up from the feet) where two leg runs sit inside the torso
        edges, and `gap` the column between them. Without separated legs (robes) the crotch falls back to
        78% of the height and the legs are cut down the middle.
    """
    h, w = mask.shape
    runs = _row_runs(mask)
    main_w = np.array([max((e - s for s, e in r), default=0) for r in runs], float)
    arm_rows = np.flatnonzero(main_w > 0.7 * main_w.max())
    arm_top, arm_bot = int(arm_rows.min()), int(arm_rows.max())
    # The neck is the narrowest row between the head's widest row (cheeks or ears) and the arms: chibis have a
    # thin neck there, brutes a chin sitting on the shoulders. Searching from the top instead would find the
    # narrow crown of the skull on characters without a visible neck.
    # Concretely: the bottom of the first dip after the head's first width peak (cheeks or ears), since a brute's
    # skull can be narrower than its chin.
    top = int(np.flatnonzero(mask.any(1))[0])
    sw = ndimage.gaussian_filter1d(main_w.astype(float), max(1.0, 0.008 * h))
    neck = None
    for p in range(top + 2, arm_top - 2):
        if sw[p] >= sw[p - 1] and sw[p] >= sw[p + 1]:  # a local maximum ...
            seg = sw[p:arm_top]
            q = p + int(np.argmin(seg))
            if q < arm_top - 1 and seg.min() < 0.95 * sw[p]:  # ... followed by a real dip before the shoulders
                mins = np.flatnonzero(main_w[p:arm_top] <= main_w[q] + 1)
                neck = p + int(mins[np.abs(p + mins - q).argmin()])
                break
    if neck is None:
        j0 = int(0.35 * arm_top)
        seg = main_w[j0:arm_top]
        mins = np.flatnonzero(seg <= seg.min() + 1)
        neck = j0 + int(mins[len(mins) // 2])
    below = range(arm_bot + 1, min(h, arm_bot + 1 + max(3, int(0.04 * h))))
    starts, ends = [], []
    for j in below:
        if runs[j]:
            s, e = max(runs[j], key=lambda r: r[1] - r[0])
            starts.append(s)
            ends.append(e)
    tx0, tx1 = (int(np.median(starts)), int(np.median(ends))) if starts else (int(0.3 * w), int(0.7 * w))

    def legs_in(j):
        return [r for r in runs[j] if r[1] > tx0 and r[0] < tx1 and r[1] - r[0] > 0.03 * w]

    bottom = int(np.flatnonzero(mask.any(1)).max())
    j = bottom
    while j > arm_bot and len(legs_in(j)) >= 2:
        j -= 1
    if bottom - j > 0.08 * h:
        crotch = j + 1
        a, b = legs_in(min(crotch + 2, bottom))[:2]
        gap = 0.5 * (a[1] + b[0])
    else:
        crotch, gap = int(0.78 * h), 0.5 * (tx0 + tx1)
    arm_cols = np.flatnonzero(mask[arm_top:arm_bot + 1].any(0))
    return {"neck": neck, "arm_top": arm_top, "arm_bot": arm_bot, "tx0": tx0, "tx1": tx1,
            "arm_x0": int(arm_cols.min()), "arm_x1": int(arm_cols.max()) + 1,
            "crotch": crotch, "gap": gap, "h": h, "w": w}


def _crop(rgb, mask):
    rows, cols = np.flatnonzero(mask.any(1)), np.flatnonzero(mask.any(0))
    sl = (slice(rows[0], rows[-1] + 1), slice(cols[0], cols[-1] + 1))
    return rgb[sl], mask[sl]


def _extend_top(rgb, mask, frac):
    """Repeats the top row so a limb reaches into the part it joins (no gaps when joints bend)."""
    k = max(1, int(round(frac * mask.shape[0])))
    return (np.concatenate([np.repeat(rgb[:1], k, 0), rgb]), np.concatenate([np.repeat(mask[:1], k, 0), mask]), k)


def _arm_mask(m, lm, edge):
    """Keeps pieces touching the arm rows, or hanging clear of the torso edge (straps); drops hip flare."""
    comp, n = ndimage.label(m)
    keep = np.zeros_like(m)
    for i, sl in enumerate(ndimage.find_objects(comp), start=1):
        rows_hit = sl[0].start <= lm["arm_bot"] and sl[0].stop > lm["arm_top"]
        clear = sl[1].stop < edge - 2 or sl[1].start > edge + 2
        if rows_hit or clear:
            keep |= comp == i
    return keep


def side_faces_right(mask):
    """
    Which way a full-body side view faces, judged by the feet: toes stick out toward the front, past the shin
    line. Returns True (facing right), False (facing left) or None when the feet don't tell (robes).
    """
    h, w = mask.shape
    runs = _row_runs(mask)
    shins = [0.5 * (r[0][0] + r[-1][1]) for r in runs[int(0.78 * h):int(0.88 * h)] if r]
    feet = [r for r in runs[int(0.93 * h):int(0.99 * h)] if r]
    if not shins or not feet:
        return None
    c = float(np.median(shins))
    left = c - min(r[0][0] for r in feet)
    right = max(r[-1][1] for r in feet) - c
    if abs(right - left) < 0.03 * w:
        return None
    return right > left


def _chest_depth_upward(m, r1):
    """
    A torso side-view mask whose rows above `r1` (the bottom of the T-pose arms) repeat row `r1`: the arm seen
    end-on (a fist in front of the chest) and a jaw jutting over the chest are not torso depth. Rows are
    replaced, not dropped, so the view keeps its height and stays aligned with the front view.
    """
    out = m.copy()
    rows = np.flatnonzero(m.any(1))
    if len(rows) and rows[0] < r1 <= rows[-1]:
        out[rows[0]:r1] = m[r1]
    return out


def segment_body(img, max_h=640, ext=None, neck=None):
    """
    Cuts a bare-body T-pose turnaround (front | right side | back) into per-part views. Returns
    `(parts, metrics)`: `parts[role] = (front, side, back)` Views (arm: no side view, drawn rotated to hang
    shoulder-up like a standing part; the drawn limb is the character's right one), and `metrics[role]` with
    the front crop's `h`/`w` in source pixels, the extension rows added on top (`ext`), plus the front
    landmarks. `ext` = fraction of height extruded upward per role so limbs and torso tuck into their parent.
    `neck` overrides the neck row of the front crop (found by placing a head-only drawing, `place_head`).
    """
    ext = ext or {"torso": 0.08, "arm": 0.12, "leg": 0.22}
    rgb = np.asarray(img.convert("RGB"))
    fg = foreground_mask(rgb)
    masks = _view_masks_by_columns(fg, 3) or _view_masks_by_components(fg, 3)
    if masks is None:
        raise ValueError("body image: could not find front, side and back views")
    (fr, fm), (sr, sm), (br, bm) = [_crop(rgb, _drop_strays(m)) for m in masks]
    # The local frame wants the side view facing right (+z = image right). Image models often draw it facing
    # left whatever the prompt says, which would put the face and toes on the back.
    flipped = side_faces_right(sm) is False
    if flipped:
        sr, sm = sr[:, ::-1], sm[:, ::-1]
    lf, lb = body_landmarks(fm), body_landmarks(bm)
    if neck is not None:
        lf["neck"] = int(neck)
        lb["neck"] = int(round(neck * lb["h"] / lf["h"]))
    hs = sm.shape[0]

    def rows(m, a, b):
        out = np.zeros_like(m)
        out[a:b] = m[a:b]
        return out

    def cols(m, a, b):
        out = np.zeros_like(m)
        out[:, int(a):int(b)] = m[:, int(a):int(b)]
        return out

    def side_rows(a, b):  # same relative heights in the side view (all views share scale and baseline)
        return rows(sm, int(a / lf["h"] * hs), int(b / lf["h"] * hs))

    cuts = {
        "head": (rows(fm, 0, lf["neck"]), side_rows(0, lf["neck"]), rows(bm, 0, lb["neck"])),
        "torso": (cols(rows(fm, lf["neck"], lf["crotch"]), lf["tx0"], lf["tx1"]),
                  _chest_depth_upward(side_rows(lf["neck"], lf["crotch"]), int((lf["arm_bot"] + 1) / lf["h"] * hs)),
                  cols(rows(bm, lb["neck"], lb["crotch"]), lb["tx0"], lb["tx1"])),
        # Image-left in the front view and image-right in the back view are both the character's right side.
        "arm": (_arm_mask(cols(rows(fm, lf["neck"], lf["crotch"]), 0, lf["tx0"]), lf, lf["tx0"]), None,
                _arm_mask(cols(rows(bm, lb["neck"], lb["crotch"]), lb["tx1"], lb["w"]), lb, lb["tx1"])),
        "leg": (cols(rows(fm, lf["crotch"], lf["h"]), 0, lf["gap"]), side_rows(lf["crotch"], lf["h"]),
                cols(rows(bm, lb["crotch"], lb["h"]), lb["gap"], lb["w"])),
    }
    parts, metrics = {}, {}
    for role, (f, s, b) in cuts.items():
        views, meta = [], {}
        for which, (src, m) in (("front", (fr, f)), ("side", (sr, s)), ("back", (br, b))):
            if m is None or not m.any():
                views.append(None)
                continue
            r, mk = _crop(src, m)
            if role == "arm":  # T-pose arm -> hanging arm, shoulder at the top
                k = 1 if which == "front" else -1
                r, mk = np.rot90(r, k), np.rot90(mk, k)
            if which == "front":
                meta = {"h": mk.shape[0], "w": mk.shape[1], "ext": 0}
            if role in ext:
                r, mk, k_rows = _extend_top(r, mk, ext[role])
                if which == "front":
                    meta["ext"] = k_rows
            views.append(View(np.ascontiguousarray(r), np.ascontiguousarray(mk), max_h=max_h))
        parts[role] = tuple(views)
        metrics[role] = meta
    metrics["landmarks"], metrics["back_landmarks"] = lf, lb
    metrics["side_flipped"] = flipped
    metrics["crops"] = {"front": (fr, fm), "side": (sr, sm), "back": (br, bm)}  # full resolution
    return parts, metrics


# ---------------------------------------------------------------------------
# Head-only drawing (`parts/head.png`): the whole head, jaw included, which the body drawing often hides
# ---------------------------------------------------------------------------
def head_chin_row(view, frac=0.5):
    """
    Row of a head-only front view where the head ends: scanning down from its widest row, the first row narrower
    than `frac` x the round outline's width (ears ignored), so a neck stump or collar drawn below the jaw is cut.
    """
    w = view.mask.sum(1).astype(float)
    _, _, _, half = fit_round_outline(view.mask)
    rows = np.flatnonzero(view.mask.any(1))
    start = int(rows[0] + np.argmax(ndimage.uniform_filter1d(w[rows[0]:rows[-1] + 1], 5)))
    below = np.flatnonzero(w[start:] < frac * 2 * half)
    return int(start + below[0]) if len(below) else int(rows[-1] + 1)


def head_views(img, max_h=512):
    """
    Front / side / back `View`s of a head-only turnaround, each cut at the front view's chin (the views share the
    top line and scale), plus the uncut full-resolution front view for placing the head on the body drawing.
    Raises ValueError when the image doesn't hold three views.
    """
    rgb = np.asarray(img.convert("RGB"))
    full = split_views(img, 3, max_h=10 ** 6)
    front = full[0]
    chin_abs = front.src_box[1] + head_chin_row(front)
    out = []
    for v in full:
        k = int(np.clip(round(chin_abs - v.src_box[1]), 2, v.h))
        c0, r0, c1, _ = v.src_box
        nv = View(rgb[r0:r0 + k, c0:c1], v.mask[:k], max_h=max_h)
        nv.src_box = (c0, r0, c1, r0 + k)
        out.append(nv)
    return out, front, chin_abs - front.src_box[1]


def _run_at_col(row, c):
    """`(start, end)` of the run of `row` containing column `c`, else `(0, 0)`."""
    for s_, e in _runs(row):
        if s_ <= c < e:
            return s_, e
    return 0, 0


def _gray(rgb, mask):
    g = np.asarray(rgb, float) @ [0.299, 0.587, 0.114]
    return np.where(mask, g, 255.0)


def place_head(body_rgb, body_mask, lm, head_front, chin_row, work_h=220):
    """
    Where a head-only front view sits on the body's front view: the scale and offset with the best masked
    normalized cross-correlation of their gray images (face features line up whatever the colors), searched
    around top-aligned and centred placements at the scale that matches their widest rows (both include the
    ears). Returns `(scale, top_row, chin_row_on_body, left_col, score)` in body-crop pixels.
    """
    rows = int(min(lm["arm_bot"], body_mask.shape[0]))
    f = work_h / max(rows, 1)
    size = lambda a: (max(2, round(a.shape[1] * f)), max(2, round(a.shape[0] * f)))
    B = np.asarray(Image.fromarray(_gray(body_rgb[:rows], body_mask[:rows]).astype(np.uint8)).resize(size(body_mask[:rows]), Image.BILINEAR), float)
    # The head's widest row (ears) above the geometric neck, which lies below the ears even when it is wrong.
    ax = int(0.5 * (lm["tx0"] + lm["tx1"]))
    bw = max((e - s_ for s_, e in (_run_at_col(body_mask[r], ax) for r in range(lm["neck"])) if e > s_), default=1)
    hm, hg = head_front.mask, _gray(head_front.rgb, head_front.mask)
    s0 = bw / max(hm.sum(1).max(), 1)
    axis = 0.5 * (lm["tx0"] + lm["tx1"]) * f
    best = (-2.0, s0, 0, 0)
    for s in s0 * np.linspace(0.8, 1.25, 19):
        tw, th = max(4, round(hm.shape[1] * s * f)), max(4, round(hm.shape[0] * s * f))
        T = np.asarray(Image.fromarray(hg.astype(np.uint8)).resize((tw, th), Image.BILINEAR), float)
        M = np.asarray(Image.fromarray(hm.astype(np.uint8) * 255).resize((tw, th), Image.NEAREST)) > 127
        t = T[M] - T[M].mean()
        tn = np.sqrt((t * t).sum()) + 1e-9
        for dy in range(-int(0.06 * th), int(0.06 * th) + 1, 2):
            for dx in range(-int(0.05 * tw), int(0.05 * tw) + 1, 2):
                y0, x0 = dy, int(round(axis - 0.5 * tw)) + dx
                P = np.full((th, tw), 255.0)
                ys, xs = max(y0, 0), max(x0, 0)
                ye, xe = min(y0 + th, B.shape[0]), min(x0 + tw, B.shape[1])
                if ye <= ys or xe <= xs:
                    continue
                P[ys - y0:ye - y0, xs - x0:xe - x0] = B[ys:ye, xs:xe]
                p = P[M] - P[M].mean()
                score = float((p * t).sum() / (np.sqrt((p * p).sum()) * tn + 1e-9))
                if score > best[0]:
                    best = (score, s, y0, x0)
    score, s, y0, x0 = best
    top = y0 / f
    return s, top, top + chin_row * s, x0 / f, score


def align_redraw(ref_view, img, dark_max=0.35):
    """
    Where a view of an edited redraw (`img`, the same framing, e.g. hair added to a head) sits relative to the
    original `ref_view` (a `View` with `src_box`): the scale and full-image offset with the best masked normalized
    cross-correlation of the original's gray face against the redraw, searched near identity. Pixels the edit
    painted dark (new hair) are left out of the score. Returns `(scale, dx, dy)`: a pixel `(c, r)` of the
    original's crop lands at `(c0 + dx + c * scale, r0 + dy + r * scale)` in `img`.
    """
    g = _gray(np.asarray(img.convert("RGB")), np.ones(img.size[::-1], bool))
    T, M = _gray(ref_view.rgb, ref_view.mask), ref_view.mask.copy()
    c0, r0 = ref_view.src_box[:2]
    f = min(1.0, 260.0 / max(T.shape))
    small = lambda a, mode=Image.BILINEAR: np.asarray(Image.fromarray(a.astype(np.uint8)).resize(
        (max(2, round(a.shape[1] * f)), max(2, round(a.shape[0] * f))), mode), float)
    G = small(g)
    best = (-2.0, 1.0, 0, 0)
    for sc in np.linspace(0.94, 1.06, 7):
        tw, th = max(4, round(T.shape[1] * sc * f)), max(4, round(T.shape[0] * sc * f))
        Ts = np.asarray(Image.fromarray(T.astype(np.uint8)).resize((tw, th), Image.BILINEAR), float)
        Ms = np.asarray(Image.fromarray(M.astype(np.uint8) * 255).resize((tw, th), Image.NEAREST)) > 127
        for dy in range(-12, 13, 2):
            for dx in range(-12, 13, 2):
                y0, x0 = int(round(r0 * f)) + dy, int(round(c0 * f)) + dx
                if y0 < 0 or x0 < 0 or y0 + th > G.shape[0] or x0 + tw > G.shape[1]:
                    continue
                P = G[y0:y0 + th, x0:x0 + tw]
                use = Ms & (P > 255 * dark_max)
                if use.sum() < 50:
                    continue
                p, t = P[use] - P[use].mean(), Ts[use] - Ts[use].mean()
                score = float((p * t).sum() / (np.sqrt((p * p).sum() * (t * t).sum()) + 1e-9))
                if score > best[0]:
                    best = (score, sc, x0 / f - c0, y0 / f - r0)
    _, sc, dx, dy = best
    return sc, dx, dy


def torso_top_row(body_mask, lm, head_front, scale, top, left, chin):
    """
    First body row (between the head's widest row and its placed chin) where the body is clearly wider than the placed
    head: the shoulders or trapezius rising beside the jaw belong to the torso even where the head overlaps them.
    The placed chin when nothing emerges (a thin neck).
    """
    hm = head_front.mask
    ax = int(0.5 * (lm["tx0"] + lm["tx1"]))
    run = 0
    widest = top + scale * float(np.argmax(hm.sum(1)))  # start below the ears: shoulders never rise above them
    for r in range(max(int(widest), 0), int(chin)):
        hr = int((r - top) / scale)
        if not 0 <= hr < hm.shape[0]:
            continue
        cols = np.flatnonzero(hm[hr])
        head_w = (cols[-1] - cols[0] + 1) * scale if len(cols) else 0.0
        s_, e = _run_at_col(body_mask[r], ax)
        run = run + 1 if (e - s_) > 1.1 * head_w + 4 else 0
        if run >= 3:
            return r - 2
    return int(chin)


# ---------------------------------------------------------------------------
# Flat palette
# ---------------------------------------------------------------------------
def _weighted_lab(rgb, l_weight):
    return acs.srgb_to_lab(rgb) * np.array([l_weight, 1.0, 1.0])


def dominant_label(views):
    """Most common palette label over the views' silhouettes."""
    labs = [v.labels[v.labels >= 0] for v in views if v is not None]
    labs = np.concatenate(labs) if labs else np.zeros(0, int)
    return int(np.bincount(labs).argmax()) if len(labs) else 0


def build_palette(views, k=12, l_weight=0.75, merge_de=8.0, min_share=0.004, keep_de=30.0, seed=0):
    """
    Clusters the foreground pixels of all views into at most `k` flat colors (k-means++ in Lab with lightness
    down-weighted, so painted shading folds into its material while dark/light materials stay apart). Clusters
    closer than `merge_de` are merged; clusters rarer than `min_share` are dropped only when another color is
    within `keep_de`, so small but distinct accents (glowing eyes) survive. Returns RGB tuples, dark -> light.
    """
    rng = np.random.default_rng(seed)
    px = np.concatenate([v.rgb[v.mask] for v in views]).astype(float)
    if len(px) > 80000:
        px = px[rng.choice(len(px), 80000, replace=False)]
    x = _weighted_lab(px, l_weight)
    centers = [x[rng.integers(len(x))]]
    for _ in range(k - 1):
        d = ((x[:, None] - np.array(centers)[None]) ** 2).sum(-1).min(1)
        if d.sum() <= 1e-9:  # fewer distinct colors than k
            break
        centers.append(x[rng.choice(len(x), p=d / d.sum())])
    c = np.array(centers)
    for _ in range(30):
        a = ((x[:, None] - c[None]) ** 2).sum(-1).argmin(1)
        c = np.array([x[a == i].mean(0) if (a == i).any() else c[i] for i in range(len(c))])
    while True:
        a = ((x[:, None] - c[None]) ** 2).sum(-1).argmin(1)
        share = np.bincount(a, minlength=len(c)) / len(x)
        d = np.sqrt(((c[:, None] - c[None]) ** 2).sum(-1)) + np.eye(len(c)) * 1e9
        i, j = np.unravel_index(d.argmin(), d.shape)
        rare = [i for i in np.argsort(share) if share[i] < min_share and d[i].min() < keep_de]
        if d[i, j] < merge_de:
            c[i] = (c[i] * share[i] + c[j] * share[j]) / max(share[i] + share[j], 1e-9)
            c = np.delete(c, j, 0)
        elif rare and len(c) > 2:
            c = np.delete(c, int(rare[0]), 0)
        else:
            break
    a = ((x[:, None] - c[None]) ** 2).sum(-1).argmin(1)
    pal = [tuple(int(v) for v in np.median(px[a == i], 0).round()) for i in range(len(c))]
    return sorted(pal, key=lambda p: 0.299 * p[0] + 0.587 * p[1] + 0.114 * p[2])


class _ColorList:
    """Adapter so `build_palette` can cluster a plain list of colors (one pixel each)."""

    def __init__(self, colors):
        self.rgb = np.asarray(colors, np.uint8).reshape(-1, 1, 3)
        self.mask = np.ones(self.rgb.shape[:2], bool)


def cluster_colors(colors, k=10, l_weight=0.6):
    """Reduces a list of RGB colors to at most `k` flat colors: `(palette, label per input color)`."""
    pal = build_palette([_ColorList(colors)], k=k, l_weight=l_weight, min_share=0.0)
    lab = lambda c: _weighted_lab(np.asarray(c, float), l_weight)
    d = ((lab(colors)[:, None] - lab(pal)[None]) ** 2).sum(-1)
    return pal, d.argmin(1)


def merge_palettes(base, extra, min_de=6.0):
    """`base` plus the colors of `extra` that are perceptually new (Lab distance >= `min_de`), dark -> light."""
    out = list(base)
    lab = lambda c: acs.srgb_to_lab(np.array(c, float))
    for c in extra:
        if min(np.linalg.norm(lab(c) - lab(b)) for b in out) >= min_de:
            out.append(c)
    return sorted(out, key=lambda p: 0.299 * p[0] + 0.587 * p[1] + 0.114 * p[2])


def _mode_filter(lab, k, size, passes):
    for _ in range(passes):
        counts = np.stack([ndimage.uniform_filter((lab == c).astype(np.float32), size) for c in range(k)])
        counts += 0.01 * np.stack([(lab == c) for c in range(k)])  # ties keep the current label
        lab = np.where(lab >= 0, counts.argmax(0), BG)
    return lab


def _merge_islands(lab, min_px):
    """Merges label islands smaller than `min_px` into their most common neighbouring label."""
    out = lab.copy()
    for c in np.unique(lab[lab >= 0]):
        comp, n = ndimage.label(lab == c)
        if not n:
            continue
        sizes = ndimage.sum(np.ones_like(comp), comp, range(1, n + 1))
        slices = ndimage.find_objects(comp)
        for ci in np.flatnonzero(sizes < min_px):
            sl = tuple(slice(max(s.start - 1, 0), s.stop + 1) for s in slices[ci])
            m = comp[sl] == ci + 1
            ring = ndimage.binary_dilation(m) & ~m
            neigh = out[sl][ring]
            neigh = neigh[(neigh >= 0) & (neigh != c)]
            if len(neigh):
                out[sl][m] = np.bincount(neigh).argmax()
    return out


def quantize_view(view, palette, l_weight=0.75, simplify=2, min_island_frac=0.002):
    """Snaps a view to the palette and cleans it into flat regions (`simplify` = despeckle strength 0-3)."""
    pal = _weighted_lab(np.array(palette, float), l_weight)
    px = _weighted_lab(view.rgb.astype(float), l_weight)
    lab = ((px[:, :, None] - pal[None, None]) ** 2).sum(-1).argmin(-1)
    lab = np.where(view.mask, lab, BG)
    if simplify:
        lab = _mode_filter(lab, len(palette), 3 + 2 * (simplify - 1), passes=2)
        lab = _merge_islands(lab, max(6, int(min_island_frac * simplify * view.mask.sum())))
    view.set_labels(lab)


def merge_shades(view, palette, max_ab=10.0, max_dl=20.0, min_width=0.012, min_l=35.0, keep=None, creases=False):
    """
    Flattens painted lighting: a patch drawn in a lighter or darker shade of a more common color (same hue and
    chroma, `max_ab`; lightness within `max_dl`; or the same hue at proportionate chroma, a rendered facet's
    shadow) becomes that color where it is broad, like the highlight on a
    chest or the shadow under a belly. Parts narrower than `min_width` x the view height (bandage edges, seams,
    muscle lines) are drawn design and stay, as does anything under `keep`; with `creases`, soft drawn creases
    come back as lines in the material's shade (`_paint_creases`). Very dark colors (lightness under `min_l`: eye shadow,
    brows, hair), which are makeup and lines rather than shadow.
    """
    lab = acs.srgb_to_lab(np.asarray(palette, float))
    labels = view.labels.copy()
    counts = np.bincount(labels[labels >= 0], minlength=len(palette))
    order = [int(l) for l in np.argsort(-counts) if counts[l] > 0]
    chroma = np.hypot(lab[:, 1], lab[:, 2])
    hue = np.degrees(np.arctan2(lab[:, 2], lab[:, 1]))

    def shadow(l, m):
        """A shadowed tone of the same material: same hue, proportionate chroma, DARKER (a lighter tone of a
        similar hue is often a material of its own: tusks, teeth or bone against olive skin). Colors too grey to
        have a hue (brows, eye shadow, black hair) never qualify, however close."""
        if min(chroma[l], chroma[m]) < 8.0 or lab[l, 0] >= lab[m, 0]:
            return False
        dh = abs((hue[l] - hue[m] + 180.0) % 360.0 - 180.0)
        return dh <= 15.0 and 0.45 <= chroma[l] / chroma[m] <= 2.2 and abs(lab[l, 0] - lab[m, 0]) <= 30.0

    close = lambda l, m, k=1.0: shadow(l, m) or (
        np.linalg.norm(lab[l, 1:] - lab[m, 1:]) <= k * max_ab and abs(lab[l, 0] - lab[m, 0]) <= k * max_dl
        and min(lab[l, 0], lab[m, 0]) >= min_l)
    # Shades group transitively (a highlight next to a mid tone next to a shadow), each staying near its group's
    # most common color so chains can't drift from one material into another.
    target, heads = {}, set()
    for m in order:
        if m in target:
            continue
        heads.add(m)  # a material in its own right: never merged into a later, rarer group
        group, grew = [m], True
        while grew:  # until no shade joins: a shadow may only connect through a mid tone found later
            grew = False
            for l in order:
                if (l not in target and l not in heads and l not in group and any(close(l, g) for g in group)
                        and close(l, m, 1.6)):
                    group.append(l)
                    target[l] = m
                    grew = True
    # Broad is judged per pixel, not per region: a network of drawn lines often touches a shaded rim, and only the
    # rim should go. An opening with a disc as wide as the thinnest broad patch keeps exactly the broad parts.
    d = max(3, int(round(min_width * view.h))) | 1
    yy, xx = np.mgrid[:d, :d] - d // 2
    disc = xx * xx + yy * yy <= (d // 2) ** 2
    for l, m in target.items():
        mask = labels == l
        if keep is not None:
            mask &= ~keep  # protected paint (drawn teeth) is never merged away
        if not mask.any():
            continue
        broad = ndimage.binary_opening(mask, structure=disc)
        labels[broad] = m
        rest = mask & ~broad  # slivers left along a merged patch's corners are specks, not lines
        comp, n = ndimage.label(rest)
        if n:
            sizes = ndimage.sum(rest, comp, np.arange(1, n + 1))
            labels[np.isin(comp, 1 + np.flatnonzero(sizes < d * d))] = m
    if creases:
        _paint_creases(view, labels, lab, target)
    view.set_labels(labels)


def _paint_creases(view, labels, lab, target, depth=3.0, min_len=0.03):
    """
    Paints a material's drawn creases (the line under a pec, a belly fold: soft shading too subtle to earn a palette
    color of its own) in that material's darker merged shade, the way KayKit paints muscle lines. A crease is a thin
    valley in the drawing's lightness: darker than its surroundings by `depth` (Lab L) at a small scale, and long
    (at least `min_len` x the view height) rather than a speck.
    """
    L = acs.srgb_to_lab(view.rgb.reshape(-1, 3).astype(float))[:, 0].reshape(view.h, view.w)
    L = np.where(view.mask, L, np.nan)
    fill = np.where(view.mask, L, np.nanmean(L))
    sig = max(1.0, 0.002 * view.h)
    dog = ndimage.gaussian_filter(fill, sig) - ndimage.gaussian_filter(fill, 4 * sig)
    inner = ndimage.binary_erosion(view.mask, iterations=max(2, int(0.01 * view.h)))  # not the outline's own shade
    for m in set(target.values()):
        shades = [l for l, t in target.items() if t == m and lab[l, 0] < lab[m, 0]]
        if not shades:
            continue
        shade = max(shades, key=lambda l: lab[l, 0])  # the lightest of its shadows: lines stay subtle
        valley = (dog < -depth) & inner & (labels == m)
        comp, n = ndimage.label(valley, structure=np.ones((3, 3)))
        lines = np.zeros_like(valley)
        for i, sl in enumerate(ndimage.find_objects(comp), start=1):
            if max(sl[0].stop - sl[0].start, sl[1].stop - sl[1].start) >= min_len * view.h:
                lines[sl] |= comp[sl] == i
        # A little bolder than drawn, so the per-face coloring can follow them as unbroken lines.
        lines = ndimage.binary_dilation(lines, iterations=max(1, int(0.003 * view.h))) & (labels == m)
        labels[lines] = shade


# ---------------------------------------------------------------------------
# Surface nets
# ---------------------------------------------------------------------------
_CORNERS = np.array([(i, j, k) for k in (0, 1) for j in (0, 1) for i in (0, 1)], float)
_EDGES = [(0, 1), (2, 3), (4, 5), (6, 7), (0, 2), (1, 3), (4, 6), (5, 7), (0, 4), (1, 5), (2, 6), (3, 7)]


def surface_nets(field, origin, step):
    """Indexed outward-facing mesh of `field < 0` (`field[i, j, k]` sampled at `origin + (i, j, k) * step`)."""
    nx, ny, nz = (s - 1 for s in field.shape)
    corners = np.stack([field[int(o[0]):int(o[0]) + nx, int(o[1]):int(o[1]) + ny, int(o[2]):int(o[2]) + nz]
                        for o in _CORNERS], -1)
    neg = corners < 0
    active = neg.any(-1) & ~neg.all(-1)
    acc = np.zeros(active.shape + (3,))
    cnt = np.zeros(active.shape)
    for a, b in _EDGES:
        fa, fb = corners[..., a], corners[..., b]
        cross = (fa < 0) != (fb < 0)
        t = np.where(cross, fa / np.where(cross, fa - fb, 1.0), 0.0)
        acc += np.where(cross[..., None], _CORNERS[a] + t[..., None] * (_CORNERS[b] - _CORNERS[a]), 0.0)
        cnt += cross
    cells = np.argwhere(active)
    verts = (cells + acc[active] / cnt[active][:, None]) * step + np.asarray(origin)
    vid = -np.ones(active.shape, np.int64)
    vid[active] = np.arange(len(cells))

    quads, flips = [], []
    inside = field < 0
    # Each grid edge with a sign change becomes a quad joining the four cells around it.
    i, j, k = np.nonzero(inside[:-1, 1:-1, 1:-1] != inside[1:, 1:-1, 1:-1])
    j, k = j + 1, k + 1
    quads.append(np.stack([vid[i, j - 1, k - 1], vid[i, j, k - 1], vid[i, j, k], vid[i, j - 1, k]], 1))
    flips.append(np.stack([inside[i, j, k], np.zeros_like(i)], 1))
    i, j, k = np.nonzero(inside[1:-1, :-1, 1:-1] != inside[1:-1, 1:, 1:-1])
    i, k = i + 1, k + 1
    quads.append(np.stack([vid[i - 1, j, k - 1], vid[i, j, k - 1], vid[i, j, k], vid[i - 1, j, k]], 1))
    flips.append(np.stack([inside[i, j, k], np.ones_like(i)], 1))
    i, j, k = np.nonzero(inside[1:-1, 1:-1, :-1] != inside[1:-1, 1:-1, 1:])
    i, j = i + 1, j + 1
    quads.append(np.stack([vid[i - 1, j - 1, k], vid[i, j - 1, k], vid[i, j, k], vid[i - 1, j, k]], 1))
    flips.append(np.stack([inside[i, j, k], np.full_like(i, 2)], 1))
    q = np.concatenate(quads)
    info = np.concatenate(flips)
    # Orient: the outward normal points along +axis when the lower grid point is inside.
    v = verts[q]
    n = np.cross(v[:, 2] - v[:, 0], v[:, 3] - v[:, 1])
    want = np.where(info[:, 0].astype(bool), 1.0, -1.0)
    flip = n[np.arange(len(q)), info[:, 1]] * want < 0
    q[flip] = q[flip][:, ::-1]
    # Split each quad along its shorter diagonal.
    d02 = np.linalg.norm(verts[q[:, 0]] - verts[q[:, 2]], axis=1)
    d13 = np.linalg.norm(verts[q[:, 1]] - verts[q[:, 3]], axis=1)
    a = d02 <= d13
    faces = np.concatenate([
        np.stack([q[a, 0], q[a, 1], q[a, 2]], 1), np.stack([q[a, 0], q[a, 2], q[a, 3]], 1),
        np.stack([q[~a, 0], q[~a, 1], q[~a, 3]], 1), np.stack([q[~a, 1], q[~a, 2], q[~a, 3]], 1),
    ])
    return verts, faces


def relax_and_project(verts, faces, fn, step, iters=3):
    """Laplacian-smooths surface-nets stair steps, then pulls every vertex back onto `fn == 0`."""
    n = len(verts)
    e = np.concatenate([faces[:, [0, 1]], faces[:, [1, 2]], faces[:, [2, 0]]])
    e = np.concatenate([e, e[:, ::-1]])
    deg = np.bincount(e[:, 0], minlength=n).astype(float)[:, None]
    h = 0.25 * step
    offs = np.eye(3) * h
    for _ in range(iters):
        nb = np.zeros_like(verts)
        np.add.at(nb, e[:, 0], verts[e[:, 1]])
        verts = verts + 0.5 * (nb / np.maximum(deg, 1) - verts)
        for _ in range(2):
            d = fn(verts)
            g = np.stack([(fn(verts + o) - fn(verts - o)) / (2 * h) for o in offs], 1)
            g2 = np.maximum((g * g).sum(1, keepdims=True), 1e-9)
            verts = verts - np.clip(d, -step, step)[:, None] * g / g2
    return verts


# ---------------------------------------------------------------------------
# Parts
# ---------------------------------------------------------------------------
ROUNDNESS = {"round": 2.2, "boxy": 3.2, "flat": 4.0}


def _interp(arr, r, c):
    return ndimage.map_coordinates(arr, [r, c], order=1, mode="nearest")


def _superellipse(theta, p):
    """Unit superellipse directions `(x, z)` at angles `theta` (exponent 2 = ellipse, higher = boxier)."""
    c, s = np.cos(theta), np.sin(theta)
    return np.sign(c) * np.abs(c) ** (2 / p), np.sign(s) * np.abs(s) ** (2 / p)


def superellipse_dirs(n, p):
    """`n` unit superellipse directions `(x, z)` evenly spaced in angle."""
    return _superellipse(2 * np.pi * np.arange(n) / n, p)


def _rdp(P, tol):
    """Indices of the Ramer-Douglas-Peucker simplification of polyline `P` (n, d)."""
    keep = np.zeros(len(P), bool)
    keep[[0, -1]] = True
    stack = [(0, len(P) - 1)]
    while stack:
        i, j = stack.pop()
        if j <= i + 1:
            continue
        seg = P[j] - P[i]
        t = np.clip(((P[i + 1:j] - P[i]) @ seg) / max(seg @ seg, 1e-12), 0, 1)
        d = np.linalg.norm(P[i + 1:j] - (P[i] + t[:, None] * seg), axis=1)
        k = int(np.argmax(d))
        if d[k] > tol:
            m = i + 1 + k
            keep[m] = True
            stack += [(i, m), (m, j)]
    return np.flatnonzero(keep)


class ProjectedPart:
    """
    Shared frame of every part rebuilt from orthographic views (local frame: see the module docstring):
    pixel <-> local mappings, which view colors which face, and palette label sampling. `length` is the world
    height of the front view; `width` (optional) the world width of its silhouette, otherwise the drawing's
    aspect is kept. Side and back views are rescaled to the front view's height (drawn at the same scale).
    Subclasses provide `section(y)`, `_section_params(x, y)`, `mesh()` and `surface_z(x, y, front)`.
    """

    def __init__(self, front, side=None, back=None, length=1.0, width=None, roundness="round", depth_ratio=0.8):
        self.front, self.side, self.back = front, side, back
        self.L = float(length)
        self.ky = self.L / front.h
        fw = float(np.nanmax(front.row_hi) - np.nanmin(front.row_lo))
        self.kx = (width / fw) if width else self.ky
        self.p = ROUNDNESS.get(roundness, 2.6) if isinstance(roundness, str) else float(roundness)
        self.depth_ratio = depth_ratio
        if side is not None:
            self.kys, self.kzs = self.L / side.h, self.kx * front.h / side.h
        if back is not None:
            self.kyb, self.kxb = self.L / back.h, self.kx * front.h / back.h

    def _front_rc(self, x, y):
        return (self.L - y) / self.ky - 0.5, x / self.kx + self.front.axis - 0.5

    def _side_rc(self, z, y):
        return (self.L - y) / self.kys - 0.5, z / self.kzs + self.side.axis - 0.5

    @property
    def views(self):
        """Projection sources by index: 0 front, 1 side, 2 back (the front's edge band when undrawn)."""
        return [self.front, self.side, self.back if self.back is not None else self.front]

    def map_view(self, k, P):
        """Fractional pixel coordinates `(row, col)` of points `P` in view `k` (see `views`)."""
        x, y, z = P[:, 0], P[:, 1], P[:, 2]
        if k == 0:
            return self._front_rc(x, y)
        if k == 1:
            return self._side_rc(z, y)
        if self.back is not None:
            return (self.L - y) / self.kyb - 0.5, -x / self.kxb + self.back.axis - 0.5
        # No back drawing: continue the material at the silhouette edge instead of mirroring the front
        # (which would paint a face onto the back of the head).
        f = self.front
        r, c = self._front_rc(x, y)
        cx = self._section_params(x, y)[0]
        ri = np.clip(np.rint(r).astype(int), 0, f.h - 1)
        ci = np.clip(np.rint(c).astype(int), 0, f.w - 1)
        return r, f.span_c[ri, ci] + np.sign(x - cx) * f.span_h[ri, ci] * 0.82 - 0.5

    def face_views(self, verts, faces):
        """
        Projection source per face: front view for forward-facing faces, back view otherwise. The side view shapes
        the part (its depth profile) but never colors it: it repeats the eyes, nose and mouth at its front edge,
        which painted offset second copies beside the front view's, and the flat look loses nothing when the sides
        continue the front and back colors.
        """
        v = verts[faces]
        n = np.cross(v[:, 1] - v[:, 0], v[:, 2] - v[:, 0])
        return np.where(n[:, 2] >= 0, 0, 2)


def _depth_extents(side, frac=0.07):
    """
    Per-row front/back extent of a side view with small protrusions (a nose, a chin tip) opened away. Each
    ring spans its whole row, so a nose sticking out for a few rows would push the entire face forward there and
    leave a horizontal shelf across it; big forms like a foot pointing forward survive the opening.
    """
    k = max(3, int(frac * side.h)) | 1
    m = ndimage.binary_opening(np.pad(side.mask, k), structure=np.ones((k, k), bool))[k:-k, k:-k]
    rows = np.arange(side.h)
    has = m.any(1)
    if has.sum() < 2:
        return side.row_lo, side.row_hi
    lo = np.where(has, m.argmax(1), np.nan)
    hi = np.where(has, side.w - m[:, ::-1].argmax(1), np.nan)
    return (np.interp(rows, rows[has], lo[has]), np.interp(rows, rows[has], hi[has]))


class LoftPart(ProjectedPart):
    """
    A part made of primitives fitted to its drawings: superellipse rings stacked along the part, each spanning
    the widest silhouette run of its row in the front view (x) and the side view's extent at that height (z).
    The profiles are smoothed and a ring is kept only where they bend (Ramer-Douglas-Peucker, `tol` relative to
    the part size) or where joints need vertices to bend (`max_gap`), giving KayKit-like low-poly meshes. Thin
    dangling bits beside the main silhouette (straps) are ignored; use `SolidPart` when they matter.
    """

    def __init__(self, front, side=None, back=None, sides=16, tol=0.012, max_gap=0.08, fill=0.0, **kw):
        """`fill` (0-1) widens every row toward a rounded-box envelope of the part's own size: KayKit heads are
        rounded boxes, while a drawn chin tapering to a point would loft into a cone."""
        super().__init__(front, side, back, **kw)
        self.sides, self.tol, self.max_gap = sides, tol, max_gap
        f = self.front
        y = self.L - (np.arange(f.h) + 0.5) * self.ky
        a = f.main_h * self.kx
        cx = (f.main_c - f.axis) * self.kx
        if side is not None:
            rs = (self.L - y) / self.kys - 0.5
            rows = np.arange(side.h)
            lo, hi = _depth_extents(side)
            lo, hi = np.interp(rs, rows, lo), np.interp(rs, rows, hi)
            cz, b = (0.5 * (lo + hi) - side.axis) * self.kzs, 0.5 * (hi - lo) * self.kzs
        else:
            cz, b = np.zeros_like(a), a * self.depth_ratio
        sig = max(1.0, 0.012 * f.h)
        smooth = lambda v: ndimage.gaussian_filter1d(v, sig, mode="nearest")
        if fill > 0:
            yn = np.clip((y - 0.5 * self.L) / (0.5 * self.L), -1, 1)
            env = (1 - np.abs(yn) ** 3.0) ** (1 / 3.0)  # superellipse: flat sides, rounded top and bottom
            a = np.maximum(a, fill * float(a.max()) * env)
            b = np.maximum(b, fill * float(b.max()) * env)
            cx = np.where(a > 0, cx, 0.0)
        order = np.argsort(y)
        self.py = y[order]
        self.pa, self.pcx = np.maximum(smooth(a), 1e-3)[order], smooth(cx)[order]
        self.pb, self.pcz = np.maximum(smooth(b), 1e-3)[order], smooth(cz)[order]

    def section(self, y):
        y = np.atleast_1d(np.asarray(y, float))
        return (np.interp(y, self.py, self.pcx), np.interp(y, self.py, self.pa),
                np.interp(y, self.py, self.pcz), np.interp(y, self.py, self.pb))

    def _section_params(self, x, y):
        return self.section(y)

    def mesh(self):
        size = max(self.L, 2 * float(self.pa.max()), 2 * float(self.pb.max()))
        prof = np.column_stack([self.py, self.pa, self.pcx, self.pb, self.pcz]) / size
        keep = list(_rdp(prof, self.tol))
        rows = [keep[0]]
        for i, j in zip(keep[:-1], keep[1:]):
            n = int(np.ceil((self.py[j] - self.py[i]) / self.max_gap))
            rows += [int(round(i + (j - i) * t / n)) for t in range(1, n)] + [j]
        th = 2 * np.pi * np.arange(self.sides) / self.sides
        ux, uz = _superellipse(th, self.p)
        rings = [np.column_stack([self.pcx[i] + self.pa[i] * ux, np.full(self.sides, self.py[i]),
                                  self.pcz[i] + self.pb[i] * uz]) for i in rows]
        return acs.loft_mesh(rings)

    def surface_z(self, x, y, front=True):
        cx, a, cz, b = (float(v[0]) for v in self.section([y]))
        xn = min(abs(x - cx) / a, 1.0)
        dz = b * (1.0 - xn ** self.p) ** (1.0 / self.p)
        return cz + dz if front else cz - dz


def fit_round_outline(mask, q=2.6):
    """
    Fits a superellipse outline (vertical exponent `q`) to a head-like silhouette: rows give the top and bottom,
    and the half-width and centre are medians over rows of width / profile, so protrusions confined to a few rows
    (ears, a nose in profile, a chin tip) can't widen it. Returns `(row0, row1, centre_col, half_width_px)`.
    """
    rows = np.flatnonzero(mask.any(1))
    r0, r1 = rows[0], rows[-1] + 1
    yc, H = 0.5 * (r0 + r1), 0.5 * (r1 - r0)
    rr = np.arange(r0, r1)
    t = np.clip((rr + 0.5 - yc) / H, -1, 1)
    prof = (1.0 - np.abs(t) ** q) ** (1.0 / q)
    lo = np.array([np.flatnonzero(mask[r])[0] if mask[r].any() else np.nan for r in rr], float)
    hi = np.array([np.flatnonzero(mask[r])[-1] + 1 if mask[r].any() else np.nan for r in rr], float)
    ok = (prof > 0.6) & ~np.isnan(lo)
    # A low quantile, not the median: ears only ever widen rows, and big ears can cover half of them.
    half = float(np.quantile(0.5 * (hi - lo)[ok] / prof[ok], 0.3))
    centre = float(np.median(0.5 * (hi + lo)[ok]))
    return int(r0), int(r1), centre, half


def round_core_mask(mask, q=2.6, margin=0.97):
    """The silhouette clipped to its fitted round outline (`fit_round_outline`): ears and similar removed."""
    r0, r1, c, half = fit_round_outline(mask, q)
    yc, H = 0.5 * (r0 + r1), 0.5 * (r1 - r0)
    rr = np.arange(mask.shape[0])[:, None] + 0.5
    t = np.clip((rr - yc) / H, -1, 1)
    w = half * margin * (1.0 - np.abs(t) ** q) ** (1.0 / q)
    cols = np.arange(mask.shape[1])[None, :] + 0.5
    return mask & (np.abs(cols - c) <= w)


def ear_residue(view, q=2.6, min_frac=0.004):
    """
    What sticks out of a head view's round outline on its image-left side between 25% and 85% of its height
    (ears; hair or horns above are ignored). Returns `(row0, row1, col0, col1, label)` of the largest such piece
    in the view's pixels, or None when there is nothing ear-sized.
    """
    out = view.mask & ~round_core_mask(view.mask, q, margin=1.06)  # looser than the texture trim: real protrusions
    h = view.h
    out[: int(0.25 * h)] = False
    out[int(0.85 * h):] = False
    out[:, view.w // 2:] = False
    comp, n = ndimage.label(out)
    if not n:
        return None
    sizes = ndimage.sum(out, comp, range(1, n + 1))
    i = int(np.argmax(sizes))
    if sizes[i] < min_frac * view.mask.sum():
        return None
    piece = comp == i + 1
    rows, cols = np.flatnonzero(piece.any(1)), np.flatnonzero(piece.any(0))
    labs = view.labels[piece]
    labs = labs[labs >= 0]
    label = int(np.bincount(labs).argmax()) if len(labs) else 0
    return int(rows[0]), int(rows[-1]) + 1, int(cols[0]), int(cols[-1]) + 1, label


def drawn_teeth(view, palette, min_frac=0.0015, max_frac=0.03):
    """
    Tusks and fangs drawn on a head's front view: small, light, pointed regions in the lower half of the face in
    a color unlike the skin (the view's most common color). Each is `(base_rc, tip_rc, width_px, label, mask)` in
    the view's pixels, the tip at its narrower end (orc tusks point up, fangs down), so they can be rebuilt as
    separate parts anchored in the jaw instead of being painted on (img2threejs's head construction).
    """
    lab = acs.srgb_to_lab(np.asarray(palette, float))
    labels = view.labels
    counts = np.bincount(labels[labels >= 0], minlength=len(palette))
    skin = int(counts.argmax())
    rows = np.flatnonzero(view.mask.any(1))
    lower = np.zeros_like(view.mask)
    lower[int(rows[0] + 0.55 * (rows[-1] - rows[0])):] = True
    area = view.mask.sum()
    out = []
    for t in range(len(palette)):
        chroma = np.hypot(lab[t, 1], lab[t, 2])
        if t == skin or lab[t, 0] < 65 or chroma > 35 or np.linalg.norm(lab[t] - lab[skin]) < 15:
            continue
        comp, n = ndimage.label((labels == t) & lower)
        for i, sl in enumerate(ndimage.find_objects(comp), start=1):
            m = comp == i
            a = m.sum()
            h, w = sl[0].stop - sl[0].start, sl[1].stop - sl[1].start
            if not (min_frac * area <= a <= max_frac * area) or h < 0.7 * w:
                continue
            rr = np.flatnonzero(m.any(1))
            k = max(1, len(rr) // 4)
            top_w, bot_w = m[rr[:k]].sum(1).mean(), m[rr[-k:]].sum(1).mean()
            if min(top_w, bot_w) > 0.7 * max(top_w, bot_w):  # a tooth tapers to a point; a blob doesn't
                continue
            cols = lambda r: np.flatnonzero(m[r])
            top_r, bot_r = rr[0], rr[-1]
            top_c, bot_c = cols(top_r).mean(), cols(bot_r).mean()
            base, tip = ((bot_r, bot_c), (top_r, top_c)) if top_w < bot_w else ((top_r, top_c), (bot_r, bot_c))
            out.append((base, tip, float(max(top_w, bot_w)), t, m))
    return out


def erase_regions(view, mask):
    """Repaints `mask` in a view's label map with the nearest other label (a part rebuilt in 3D, not painted)."""
    labels = view.labels.copy()
    keep = (labels >= 0) & ~mask
    if keep.any():
        idx = ndimage.distance_transform_edt(~keep, return_distances=False, return_indices=True)
        labels[mask] = labels[idx[0], idx[1]][mask]
    view.set_labels(labels)


def trim_to_core(view, q=2.6):
    """
    Drops a head view's protrusions (ears) from its label map: pixels outside its round outline become
    background, so the texture continues the nearest head color there. Pairs with `HeadPart`.
    """
    view.set_labels(np.where(round_core_mask(view.mask, q), view.labels, BG))


class HeadPart(ProjectedPart):
    """
    A head as one simple form: a superellipsoid (rounded box, like KayKit heads) sized from the drawings with
    ears, noses and chin tips opened away. Width and height come from the front view, depth from the side view
    (else `depth_ratio` x width). The drawings only color it, so drawn anatomy never bends the shape.
    """

    def __init__(self, front, side=None, back=None, sides=20, rings=12, vertical_p=2.6, **kw):
        super().__init__(front, side, back, **kw)
        self.p = max(self.p, 2.4)
        self.sides, self.rings, self.q = sides, rings, vertical_p
        _, _, c, half = fit_round_outline(front.mask, vertical_p)
        self.A, self.cx = half * self.kx, (c - front.axis) * self.kx
        if side is not None:
            _, _, sc, shalf = fit_round_outline(side.mask, vertical_p)
            self.B, self.cz = shalf * self.kzs, (sc - side.axis) * self.kzs
        else:
            self.B, self.cz = self.A * self.depth_ratio, 0.0
        self.yc, self.H = 0.5 * self.L, 0.5 * self.L

    def _scale(self, y):
        t = np.clip((np.asarray(y, float) - self.yc) / self.H, -1.0, 1.0)
        return np.maximum((1.0 - np.abs(t) ** self.q) ** (1.0 / self.q), 1e-3)

    def section(self, y):
        k = self._scale(np.atleast_1d(y))
        return np.full_like(k, self.cx), self.A * k, np.full_like(k, self.cz), self.B * k

    def _section_params(self, x, y):
        return self.section(y)

    def mesh(self):
        ux, uz = superellipse_dirs(self.sides, self.p)
        phi = np.linspace(-0.5 * np.pi, 0.5 * np.pi, self.rings + 2)[1:-1]
        ys = self.yc + self.H * np.sign(np.sin(phi)) * np.abs(np.sin(phi)) ** (2 / self.q)
        rings = []
        for y in ys:
            cx, a, cz, b = (float(v[0]) for v in self.section([y]))
            rings.append(np.column_stack([cx + a * ux, np.full(self.sides, y), cz + b * uz]))
        return acs.loft_mesh(rings)

    def surface_z(self, x, y, front=True):
        cx, a, cz, b = (float(v[0]) for v in self.section([y]))
        xn = min(abs(x - cx) / a, 1.0)
        dz = b * (1.0 - xn ** self.p) ** (1.0 / self.p)
        return cz + dz if front else cz - dz

    def front_to_local(self, r, c):
        """Local `(x, y)` of front-view pixel `(row, col)`."""
        return (c + 0.5 - self.front.axis) * self.kx, self.L - (r + 0.5) * self.ky


class SolidPart(ProjectedPart):
    """
    A sculpted volume for shapes rings can't capture (ears, horns, several runs per row): the intersection of
    the front and side silhouette extrusions with a per-row superellipse cross-section, polygonized with
    surface nets and decimated to `max_tris`.
    """

    def __init__(self, front, side=None, back=None, cells=26, max_tris=1500, **kw):
        super().__init__(front, side, back, **kw)
        self.cells, self.max_tris = cells, max_tris
        self.f_sdf, self.f_pad = front.sdf(self.ky, self.kx)
        if side is not None:
            self.s_sdf, self.s_pad = side.sdf(self.kys, self.kzs)
    def _section_params(self, x, y):
        """Per point: centre/half-extent of the cross-section ellipse in x (`cx`, `a`) and z (`cz`, `b`)."""
        f = self.front
        r, c = self._front_rc(x, y)
        ri = np.clip(np.rint(r).astype(int), 0, f.h - 1)
        ci = np.clip(np.rint(c).astype(int), 0, f.w - 1)
        a = np.maximum(f.span_h[ri, ci] * self.kx, 1e-3)
        cx = (f.span_c[ri, ci] - f.axis) * self.kx
        main = f.span_main[ri, ci]
        if self.side is not None:
            rs = np.clip(np.rint(self._side_rc(np.zeros_like(y), y)[0]).astype(int), 0, self.side.h - 1)
            lo, hi = self.side.row_lo[rs], self.side.row_hi[rs]
            cz = (0.5 * (lo + hi) - self.side.axis) * self.kzs
            b = 0.5 * (hi - lo) * self.kzs
        else:
            cz = np.zeros_like(x)
            b = f.main_h[ri] * self.kx * self.depth_ratio
        # Secondary runs (a dangling strap beside the head) get a depth matching their own width.
        b = np.maximum(np.where(main, b, np.minimum(b, 1.2 * a)), 1e-3)
        return cx, a, cz, b

    def section(self, y):
        """Main cross-section `(cx, a, cz, b)` at local heights `y` (used to fit wraps around this part)."""
        y = np.atleast_1d(np.asarray(y, float))
        f = self.front
        ri = np.clip(np.rint(self._front_rc(np.zeros_like(y), y)[0]).astype(int), 0, f.h - 1)
        a = f.main_h[ri] * self.kx
        cx = (f.main_c[ri] - f.axis) * self.kx
        if self.side is not None:
            rs = np.clip(np.rint(self._side_rc(np.zeros_like(y), y)[0]).astype(int), 0, self.side.h - 1)
            lo, hi = self.side.row_lo[rs], self.side.row_hi[rs]
            return cx, a, (0.5 * (lo + hi) - self.side.axis) * self.kzs, 0.5 * (hi - lo) * self.kzs
        return cx, a, np.zeros_like(a), a * self.depth_ratio

    # -- geometry -------------------------------------------------------------
    def field(self, P):
        P = np.asarray(P, float).reshape(-1, 3)
        x, y, z = P[:, 0], P[:, 1], P[:, 2]
        r, c = self._front_rc(x, y)
        d = _interp(self.f_sdf, r + self.f_pad, c + self.f_pad)
        if self.side is not None:
            rs, cs = self._side_rc(z, y)
            d = np.maximum(d, _interp(self.s_sdf, rs + self.s_pad, cs + self.s_pad))
        cx, a, cz, b = self._section_params(x, y)
        p = self.p
        rad = ((np.abs(x - cx) / a) ** p + (np.abs(z - cz) / b) ** p) ** (1.0 / p)
        return np.maximum(d, (rad - 1.0) * np.minimum(a, b))

    def bounds(self):
        f = self.front
        x0 = (np.nanmin(f.row_lo) - f.axis) * self.kx
        x1 = (np.nanmax(f.row_hi) - f.axis) * self.kx
        if self.side is not None:
            z0 = (np.nanmin(self.side.row_lo) - self.side.axis) * self.kzs
            z1 = (np.nanmax(self.side.row_hi) - self.side.axis) * self.kzs
        else:
            z1 = float(np.nanmax(f.main_h)) * self.kx * self.depth_ratio
            z0 = -z1
        m = 0.04 * max(self.L, x1 - x0)
        return np.array([x0 - m, -m, z0 - m]), np.array([x1 + m, self.L + m, z1 + m])

    def mesh(self):
        lo, hi = self.bounds()
        step = float((hi - lo).max()) / self.cells
        n = np.ceil((hi - lo) / step).astype(int) + 1
        g = np.stack(np.meshgrid(*[lo[i] + np.arange(n[i]) * step for i in range(3)], indexing="ij"), -1)
        field = self.field(g.reshape(-1, 3)).reshape(g.shape[:3])
        verts, faces = surface_nets(field, lo, step)
        verts = relax_and_project(verts, faces, self.field, step)
        if self.max_tris and len(faces) > self.max_tris:
            import fast_simplification

            verts, faces = fast_simplification.simplify(verts, faces, 1.0 - self.max_tris / len(faces))
        return verts, np.asarray(faces, np.int64)

    def surface_z(self, x, y, front=True):
        """Z of the front-most (or back-most) surface at local `(x, y)`, for mounting things onto this part."""
        lo, hi = self.bounds()
        zs = np.linspace(hi[2], lo[2], 160) if front else np.linspace(lo[2], hi[2], 160)
        d = self.field(np.column_stack([np.full_like(zs, x), np.full_like(zs, y), zs]))
        hit = np.flatnonzero(d < 0)
        return float(zs[hit[0]]) if len(hit) else 0.0


class WrapPart:
    """
    A band around `host` between local heights `y0 < y1` (host frame): belts, collars, cuffs, headbands, skirts.
    Each image row is either a *full* row (spanning most of the band's width: the band wraps around the body
    there) or a *partial* row (a crest such as a cobra, an apron, a buckle hanging below). At full rows the ring
    follows the host's cross-section, widened by how much wider that row is drawn than the band's narrowest full
    row (skirts flare, up to `max_flare`; only ratios are used, since an isolated drawing's absolute width is not
    reliable); partial rows keep the radius of the nearest full row, so crests stand up and aprons hang straight.
    Points are projected onto the drawing (front view for the front half; back view, else the mirrored front,
    for the back half), and per angle the band spans exactly the rows where that projection hits the silhouette,
    so scalloped hems, neck openings and front-only crests come out right. Double-walled with rims.
    """

    def __init__(self, host, front, back, y0, y1, thickness=0.03, n_theta=32, row_step=0.05, max_flare=1.6,
                 max_half_width=None, front_cap=None, flare_below=None):
        """`front_cap`: highest the band may reach at the front (blending to no limit at the sides), so a high
        collar rises behind the head instead of in front of the chin. `flare_below`: no flare above this height. The front view can't tell the two apart:
        a collar's back half, seen over its front, is part of the same silhouette."""
        self.host, self.front, self.back = host, front, back
        self.max_half_width, self.front_cap, self.flare_below = max_half_width, front_cap, flare_below
        self.y0, self.y1 = float(min(y0, y1)), float(max(y0, y1))
        self.H = self.y1 - self.y0
        self.t, self.n_theta, self.row_step, self.max_flare = thickness, n_theta, row_step, max_flare
        self._model = [self._row_model(v) for v in self.views]

    @property
    def views(self):
        return [self.front, self.back if self.back is not None else self.front]

    def _row_model(self, v):
        half = 0.5 * (v.row_hi - v.row_lo)
        filled = v.mask.sum(1) / np.maximum(2 * half, 1)
        # A row wraps around the body only if it is wide AND mostly filled: horn tips far apart span the full
        # width too, but with a gap between them.
        full = np.flatnonzero((half >= 0.8 * half.max()) & (filled >= 0.7))
        if not len(full):
            full = np.array([int(np.argmax(half * filled))])
        near = full[np.abs(np.arange(v.h)[:, None] - full[None]).argmin(1)]
        ref = full[np.argmin(half[full])]  # attachment line: the narrowest row that wraps around
        return {"half": half, "cen": 0.5 * (v.row_lo + v.row_hi), "near": near, "k": self.H / v.h, "ref": ref}

    def _row(self, k, y):
        v = self.views[k]
        return np.clip(np.rint((self.y1 - y) / self.H * v.h - 0.5).astype(int), 0, v.h - 1)

    def ring(self, y):
        """Ring `(cx, a, cz, b)` at local heights `y` (outer surface, thickness included)."""
        y = np.atleast_1d(np.asarray(y, float))
        m = self._model[0]
        rn = m["near"][self._row(0, y)]
        yn = self.y1 - (rn + 0.5) * m["k"]  # partial rows use their nearest full row
        cx, a, cz, b = self.host.section(yn)
        a_out = a + self.t
        a_ref = self.host.section([self.y1 - (m["ref"] + 0.5) * m["k"]])[1][0] + self.t
        flare = m["half"][rn] / max(m["half"][m["ref"]], 1e-6)
        A = np.clip(a_ref * flare, a_out, self.max_flare * a_out)
        if self.max_half_width is not None:
            A = np.maximum(np.minimum(A, self.max_half_width), a_out)
        if self.flare_below is not None:  # e.g. collars: straight around the neck, flaring only below it
            A = np.where(y > self.flare_below, a_out, A)
        B = (b + self.t) * A / a_out
        return cx, A, cz, B

    def map_view(self, k, P):
        """Each row's x maps onto its nearest full row's span, so crests keep the band's scale."""
        cx, A, cz, B = self.ring(P[:, 1])
        u = np.clip((P[:, 0] - cx) / A, -1.0, 1.0)
        m, r = self._model[k], self._row(k, P[:, 1])
        rn = m["near"][r]
        sign = 1.0 if k == 0 else -1.0
        return r.astype(float), m["cen"][rn] + sign * u * np.maximum(m["half"][rn] - 0.5, 0.5) - 0.5

    def face_views(self, verts, faces):
        c = verts[faces].mean(1)
        return np.where(c[:, 2] >= self.ring(c[:, 1])[2], 0, 1)

    def mesh(self):
        n = self.n_theta
        th = 2 * np.pi * np.arange(n) / n
        ux, uz = _superellipse(th, self.host.p)
        ys = np.linspace(self.y0, self.y1, max(8, int(self.H / 0.008)))
        cx, A, cz, B = self.ring(ys)
        y_top, y_bot = np.full(n, np.nan), np.full(n, np.nan)
        for i in range(n):
            P = np.column_stack([cx + A * ux[i], ys, cz + B * uz[i]])
            k = 0 if uz[i] >= 0 else 1
            hit = self.views[k].label_at(*self.map_view(k, P), cut=True) >= 0
            if hit.any():
                y_top[i], y_bot[i] = ys[hit].max(), ys[hit].min()
        ok = ~np.isnan(y_top)
        if not ok.any():
            return np.zeros((0, 3)), np.zeros((0, 3), np.int64)
        idx = np.arange(n)
        for arr in (y_top, y_bot):  # angles that miss the drawing collapse onto their neighbours
            arr[~ok] = np.interp(idx[~ok], idx[ok], arr[ok], period=n)
            arr[:] = ndimage.median_filter(arr, 3, mode="wrap")
        mid = 0.5 * (y_top + y_bot)
        y_top[~ok] = y_bot[~ok] = mid[~ok]
        if self.front_cap is not None:
            w = np.clip(uz, 0.0, 1.0) ** 0.5  # 1 at the front, 0 at the sides and back
            cap = self.front_cap + (1 - w) * np.maximum(y_top - self.front_cap, 0)
            y_top = np.maximum(np.minimum(y_top, cap), y_bot + 0.01)
        m = max(1, int(np.ceil(float((y_top - y_bot).max()) / self.row_step)))
        Y = y_bot[:, None] + np.linspace(0, 1, m + 1)[None] * (y_top - y_bot)[:, None]
        cx, A, cz, B = (v.reshape(n, m + 1) for v in self.ring(Y.ravel()))
        # Solid from just inside the body out to the band's surface: a flared collar cut low at the front would
        # otherwise show a dark hollow inside.
        hcx, ha, hcz, hb = (v.reshape(n, m + 1) for v in self.host.section(Y.ravel()))
        a_in = np.maximum(np.minimum(ha - 0.25 * self.t, A - 1.25 * self.t), 1e-3)
        b_in = np.maximum(np.minimum(hb - 0.25 * self.t, B - 1.25 * self.t), 1e-3)
        outer = np.stack([cx + A * ux[:, None], Y, cz + B * uz[:, None]], -1).reshape(-1, 3)
        inner = np.stack([hcx + a_in * ux[:, None], Y, hcz + b_in * uz[:, None]], -1).reshape(-1, 3)
        verts = np.vstack([outer, inner])
        o = np.arange(n * (m + 1)).reshape(n, m + 1)
        q_in = o + n * (m + 1)
        k2 = np.roll(np.arange(n), -1)
        quads = []
        for i in range(m):  # outer and inner walls
            quads.append(np.stack([o[:, i], o[k2, i], o[k2, i + 1], o[:, i + 1]], 1))
            quads.append(np.stack([q_in[:, i + 1], q_in[k2, i + 1], q_in[k2, i], q_in[:, i]], 1))
        for i, flip in ((0, False), (m, True)):  # bottom and top rims
            q = np.stack([o[:, i], q_in[:, i], q_in[k2, i], o[k2, i]], 1)
            quads.append(q[:, ::-1] if flip else q)
        q = np.concatenate(quads)
        faces = np.concatenate([q[:, [0, 1, 2]], q[:, [0, 2, 3]]])
        tri = verts[faces]
        area = np.linalg.norm(np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0]), axis=1)
        faces = faces[area > 1e-9]
        if acs.signed_volume(verts, faces) < 0:
            faces = faces[:, ::-1].copy()
        return verts, faces



# ---------------------------------------------------------------------------
# GLB reading
# ---------------------------------------------------------------------------
_COMPONENTS = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
_WIDTHS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


def read_glb(path):
    """
    A GLB's JSON header, its binary chunk and an accessor reader `acc(i)`: a read-only `(count, components)` view
    of accessor `i`, strided buffer views included (normalized integers stay raw).
    """
    data = open(path, "rb").read()
    n = struct.unpack_from("<I", data, 12)[0]
    g = json.loads(data[20:20 + n])
    binary = data[20 + n + 8:]

    def acc(i):
        a = g["accessors"][i]
        bv = g["bufferViews"][a["bufferView"]]
        dtype, k = np.dtype(_COMPONENTS[a["componentType"]]), _WIDTHS[a["type"]]
        return np.ndarray((a["count"], k), dtype, binary, bv.get("byteOffset", 0) + a.get("byteOffset", 0),
                          (bv.get("byteStride") or k * dtype.itemsize, dtype.itemsize))

    return g, binary, acc

# ---------------------------------------------------------------------------
# Color transfer: flat palette swatches
# ---------------------------------------------------------------------------
class Atlas:
    """
    The palette as one texture: vertical gradient swatches per color slot, edge-padded so texture
    filtering never bleeds a neighbour in.
    Supports:
      - `flat_uv(label)`: center midtone UV coordinate of the swatch (solid base color).
      - `gradient_uv(label, t)`: vertical gradient UV coordinate where t in [0, 1] (0=top/light, 1=bottom/shadow).
    """

    def __init__(self, palette, size=None, pad=4, col_w=16, col_h=128, looks=None, slot_names=None):
        self.pad, self.col_w, self.col_h = pad, col_w, col_h
        pal = np.asarray(palette, float)
        n_slots = len(pal)
        total_w = n_slots * (col_w + pad) + pad
        total_h = col_h + 2 * pad
        self.size_w = 1 << int(np.ceil(np.log2(max(128, total_w))))
        self.size_h = 1 << int(np.ceil(np.log2(max(128, total_h))))
        if size is not None:
            self.size_w = max(self.size_w, size)
            self.size_h = max(self.size_h, size)
        self.size = max(self.size_w, self.size_h)

        img_arr = np.zeros((self.size_h, self.size_w, 3), dtype=np.uint8)
        self.u_coords = np.zeros(n_slots, float)
        self.v_top = np.zeros(n_slots, float)
        self.v_mid = np.zeros(n_slots, float)
        self.v_bot = np.zeros(n_slots, float)

        ts = np.linspace(0.0, 1.0, col_h)
        m1 = ts <= 0.5
        k1 = ts[m1] / 0.5
        k1 = k1 * k1 * (3.0 - 2.0 * k1)
        m2 = ts > 0.5
        k2 = (ts[m2] - 0.5) / 0.5
        k2 = k2 * k2 * (3.0 - 2.0 * k2)

        for idx in range(n_slots):
            c_base = pal[idx]
            s_name = slot_names[idx] if slot_names and idx < len(slot_names) else str(idx)
            spec = (looks or {}).get(s_name, {})
            if spec.get("ramp") is not None:
                c_light = np.asarray(spec["ramp"][0], float)
                c_shadow = np.asarray(spec["ramp"][1], float)
                c_base = np.asarray(spec["ramp"][2], float) if len(spec["ramp"]) > 2 else 0.5 * (c_light + c_shadow)
            elif spec.get("emissive"):
                c_light = c_base.copy()
                c_shadow = c_base.copy()
            elif "metallic" in spec:
                c_light = np.clip(c_base * np.array([1.28, 1.25, 1.20]) + 18.0, 0, 255)
                c_shadow = np.clip(c_base * np.array([0.55, 0.50, 0.45]), 0, 255)
            else:
                c_light = np.clip(c_base * np.array([1.18, 1.16, 1.12]) + 12.0, 0, 255)
                c_shadow = np.clip(c_base * np.array([0.72, 0.68, 0.62]), 0, 255)

            ramp = np.zeros((col_h, 3), float)
            ramp[m1] = (1.0 - k1)[:, None] * c_light + k1[:, None] * c_base
            ramp[m2] = (1.0 - k2)[:, None] * c_base + k2[:, None] * c_shadow

            ramp_2d = np.repeat(ramp[:, None, :], col_w, axis=1)
            padded_ramp = np.pad(ramp_2d, ((pad, pad), (pad, pad), (0, 0)), mode="edge")

            x0 = pad + idx * (col_w + pad)
            y0 = pad
            x1 = x0 + col_w
            y1 = y0 + col_h

            img_arr[y0 - pad:y1 + pad, x0 - pad:x1 + pad, :3] = np.round(padded_ramp).astype(np.uint8)

            self.u_coords[idx] = (x0 + x1) * 0.5 / self.size_w
            self.v_top[idx] = (y0 + 2) / self.size_h
            self.v_mid[idx] = (y0 + y1) * 0.5 / self.size_h
            self.v_bot[idx] = (y1 - 2) / self.size_h

        self.image = Image.fromarray(img_arr)

    def flat_uv(self, label):
        """UV of the flat swatch (midtone / base color) for palette `label`."""
        if isinstance(label, (int, np.integer)):
            return np.array([self.u_coords[label], self.v_mid[label]])
        label = np.asarray(label)
        return np.column_stack([self.u_coords[label], self.v_mid[label]])

    def gradient_uv(self, label, t):
        """
        UV coordinate(s) along the vertical shading ramp.
        t: 0.0 (top / highlight) to 1.0 (bottom / shadow).
        Can accept scalar or arrays for label and t.
        """
        label = np.asarray(label)
        t = np.clip(np.asarray(t, float), 0.05, 0.95)
        u = self.u_coords[label]
        v_top = self.v_top[label]
        v_bot = self.v_bot[label]
        v = v_top + t * (v_bot - v_top)
        if u.ndim == 0 and v.ndim == 0:
            return np.array([u, v])
        if u.shape != v.shape:
            u = np.broadcast_to(u, v.shape)
        return np.column_stack([u.ravel(), v.ravel()])

    def save(self, path):
        self.image.save(path)


def flat_colored(verts, faces, labels, atlas):
    """
    Unwelds a mesh into `(verts, faces, uvs)` with one flat palette color per face (`labels`), each face's UVs on
    that color's swatch in `atlas`: the vertex-color look, with geometry alone carrying every shape and boundary.
    """
    swatch = np.array([atlas.flat_uv(l) for l in range(int(labels.max()) + 1)])
    n = len(faces)
    return verts[faces].reshape(-1, 3), np.arange(3 * n).reshape(n, 3), np.repeat(swatch[labels], 3, axis=0)

