"""
Inspiration by image generation, held to our silhouettes: render a kit character (or some of its pieces) from the
front and the side on white, have the image model redraw it as fantasy game art with the same camera and framing,
redesigning only the part named in the brief, then compare the outlines view by view. The overlay shows red where
only the drawing has shape (ours should grow there) and blue where only ours does (ours should shrink there); the
overlap (IoU) says how close the scripted piece is. Tune the piece's parameters and compare again until they
overlap; the drawing is the target, the script stays the source.

    python asset-studio/studio.py reference heroes/knight --brief "a horned viking helmet"
    python asset-studio/core/match.py heroes/knight --pieces Head,Helmet --reference out/reference.png -o out/

The first run makes `reference.png` (one paid image call); later runs reuse it with `--reference`. `tune()` then
searches a piece's numeric parameters for the best overlap (e.g. a helmet's horn height, thickness and spread).
"""

import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from core import mesh as mm  # noqa: E402
from core import catalog, kit
from prefabs.creatures import characters  # noqa: E402

WHITE = (255, 255, 255)
VIEWS = (0.0, -90.0)  # front, then the character's left side

PROMPT = (
    "These are two views of one low-poly 3D game character in the stylized style: the FRONT view on the left and the "
    "SIDE view on the right (the character's left side, face pointing to the left of the image). Redraw exactly this "
    "image as polished fantasy game art in the same chunky low-poly stylized style. Keep everything about the image "
    "fixed: the same two views side by side, the right one still a side profile (never a back view), the same "
    "camera, framing, size and position, and the same T-pose with both arms held straight out horizontally exactly "
    "as in the input, the same character, face and colors, on a plain white background, flat colors without "
    "shading or outlines. Change only {brief}: redesign it so it looks great and reads clearly, exaggerated like "
    "stylized game art, but keep it on the same body parts."
)


def ours(pieces, colors, size=512, shading=True):
    """Front and side renders of `pieces` on white, side by side, framed alike."""
    whole = kit.Piece.merge(*pieces)
    views = [catalog.render(whole, None, size, yaw=y, pitch=0.0, colors=colors, background=WHITE, shading=shading)
             for y in VIEWS]
    out = Image.new("RGB", (size * len(views), size), WHITE)
    for i, v in enumerate(views):
        out.paste(v, (i * size, 0))
    return out


# For a new creature drawn over the standard mannequin in the rig's idle pose (`rig.mannequin`, `rig.skin`): drawings
# never hold a T-pose, so the build is compared in the idle pose instead (see `golems.py`).
CREATURE_PROMPT = (
    "These are two views of one grey low-poly 3D mannequin in the stylized style, standing in its idle pose: the FRONT "
    "view on the left and the SIDE view on the right (the character's left side, face pointing to the left of the "
    "image). Redraw exactly this image as polished fantasy game art in the chunky low-poly stylized style: {brief}. "
    "Keep the two views side by side, the right one a true side profile, the same camera, framing, size and "
    "position, every body part where the mannequin has it and the same idle pose, on a plain white background, flat "
    "colors without outlines."
)


# For one reusable part (a head, a breastplate) rendered on its own, front and side: a design to rebuild closely.
PART_PROMPT = (
    "These are two views of one part of a low-poly 3D game character in the stylized style: the FRONT view on the left "
    "and the SIDE view on the right (facing left). Redraw it as polished stylized fantasy game art in the chunky "
    "low-poly stylized style, as a clear design to model from: {brief}. Keep the two views side by side, the right one "
    "a true side profile, the same camera, framing, size and position, on a plain white background, flat colors "
    "without outlines. Bold, readable, exaggerated shapes built from distinct chunky pieces, never realistic."
)


def inspire(image, brief, provider="gemini", prompt=PROMPT):
    """A drawing of `image` redesigned by `brief` (one paid image call), with `prompt` (`PROMPT` for a part of a kit
    character in the T-pose, `CREATURE_PROMPT` for a whole new creature on the idle mannequin)."""
    import monster_ai

    return monster_ai.get_provider(provider).edit_image(prompt.format(brief=brief), [image], aspect="16:9")


def _views(mask, n):
    """The view masks of a composite, left to right, each cropped to its silhouette."""
    out = []
    for m in mm._view_masks_by_columns(mask, n):
        ys, xs = np.nonzero(m)
        out.append(m[ys.min():ys.max() + 1, xs.min():xs.max() + 1] if len(ys) else m)
    return out


def _place(mask, scale, dx, dy, size, cx, base):
    """`mask` resized by `scale`, its bottom middle put at (`cx` + `dx`, `base` + `dy`) on a `size` canvas."""
    h, w = mask.shape
    H, W = max(1, int(round(h * scale))), max(1, int(round(w * scale)))
    m = np.asarray(Image.fromarray(mask.astype(np.uint8) * 255).resize((W, H), Image.NEAREST)) > 127
    canvas = np.zeros((size, size), bool)
    x0, y0 = int(round(cx + dx - W / 2)), int(round(base + dy - H))
    xs, ys = slice(max(0, x0), min(size, x0 + W)), slice(max(0, y0), min(size, y0 + H))
    canvas[ys, xs] = m[ys.start - y0:ys.stop - y0, xs.start - x0:xs.stop - x0]
    return canvas


def _fit(r, m, size=400):
    """The scale and shift putting the drawing's view mask `r` best over ours `m` (both cropped), and our scale."""
    k = 0.8 * size / max(m.shape)
    mine_c = _place(m, k, 0, 0, size, size / 2, 0.9 * size)
    best = (-1, None)
    for s in k * m.shape[0] / r.shape[0] * np.linspace(0.85, 1.15, 13):
        for dx in range(-24, 25, 6):
            for dy in range(-24, 25, 6):
                c = _place(r, s, dx, dy, size, size / 2, 0.9 * size)
                iou = (c & mine_c).sum() / max((c | mine_c).sum(), 1)
                if iou > best[0]:
                    best = (iou, (s, dx, dy), c, mine_c)
    return best, k


def compare(reference, mine, size=400):
    """Per view: the IoU of the drawing's silhouette against ours after the best similarity fit, an overlay, and the
    fit itself (for `regions`)."""
    ref_views = _views(mm.foreground_mask(np.asarray(reference.convert("RGB"))), len(VIEWS))
    my_views = _views((np.asarray(mine.convert("RGB")) != WHITE).any(-1), len(VIEWS))
    results = []
    for r, m in zip(ref_views, my_views):
        (iou, fit, ref_c, mine_c), k = _fit(r, m, size)
        over = np.full((size, size, 3), 255, np.uint8)
        over[ref_c & mine_c] = (150, 150, 160)
        over[ref_c & ~mine_c] = (220, 60, 60)
        over[mine_c & ~ref_c] = (60, 110, 220)
        results.append((iou, Image.fromarray(over), (fit, k)))
    return results


def _crops(mask, n):
    """Each view's bounding box in a composite: (rows, cols) slices, left to right."""
    out = []
    for v in mm._view_masks_by_columns(mask, n):
        ys, xs = np.nonzero(v)
        out.append((slice(ys.min(), ys.max() + 1), slice(xs.min(), xs.max() + 1)))
    return out


def _place_labels(lab, scale, dx, dy, size, cx, base):
    """`_place` for a label image (-1 = nothing)."""
    h, w = lab.shape
    H, W = max(1, int(round(h * scale))), max(1, int(round(w * scale)))
    small = np.asarray(Image.fromarray((lab + 1).astype(np.uint8)).resize((W, H), Image.NEAREST)).astype(int) - 1
    canvas = np.full((size, size), -1)
    x0, y0 = int(round(cx + dx - W / 2)), int(round(base + dy - H))
    xs, ys = slice(max(0, x0), min(size, x0 + W)), slice(max(0, y0), min(size, y0 + H))
    canvas[ys, xs] = small[ys.start - y0:ys.stop - y0, xs.start - x0:xs.stop - x0]
    return canvas


def regions(reference, pieces, colors, fits, clusters=10, size=400):
    """
    How well the inside matches: the drawing's flat colors clustered into regions, each of our material slots
    paired with the region it overlaps most (after the silhouette fit). Returns per-slot IoU (summed over the views,
    weighted by area), the area-weighted mean, and per view an image: our slots beside the drawing's regions,
    painted in the colors of the slots they matched.
    """
    from scipy.cluster.vq import kmeans2

    whole = kit.Piece.merge(*pieces)
    names = sorted(set(whole.slots))
    palette = np.array([((37 * i) % 251, (91 * i + 40) % 251, (53 * i + 120) % 251) for i in range(len(names))], float)
    id_img = np.asarray(ours([whole], {n: tuple(palette[i]) for i, n in enumerate(names)}, shading=False)).astype(float)
    my_lab = np.full(id_img.shape[:2], -1)
    fg_mine = (id_img != WHITE).any(-1)  # each pixel the slot of the nearest id color (edges blend a little)
    my_lab[fg_mine] = np.argmin(((id_img[fg_mine][:, None, :] - palette[None]) ** 2).sum(-1), 1)
    ref = np.asarray(reference.convert("RGB"))
    fg = mm.foreground_mask(ref)
    pix = ref[fg].astype(float)
    rng = np.random.default_rng(0)
    centres, _ = kmeans2(pix[rng.choice(len(pix), min(20000, len(pix)), replace=False)], clusters, seed=1, minit="++")
    ref_lab = np.full(fg.shape, -1)
    ref_lab[fg] = np.argmin(((pix[:, None, :] - centres[None]) ** 2).sum(-1), 1)
    views = []
    for (rs, cs), (ms, mcs), (fit, k) in zip(_crops(fg, len(VIEWS)), _crops(my_lab >= 0, len(VIEWS)), fits):
        views.append((_place_labels(my_lab[ms, mcs], k, 0, 0, size, size / 2, 0.9 * size),
                      _place_labels(ref_lab[rs, cs], fit[0], fit[1], fit[2], size, size / 2, 0.9 * size)))
    # each drawn color region joins the slot covering most of it (shading splits a material into several)
    cover = np.zeros((clusters, len(names) + 1))
    for M, R in views:
        np.add.at(cover, (R[R >= 0], M[R >= 0] + 1), 1)
    owner = cover[:, 1:].argmax(1)
    inter, union, area = np.zeros(len(names)), np.zeros(len(names)), np.zeros(len(names))
    pictures = []
    for M, R in views:
        S = np.where(R >= 0, owner[np.maximum(R, 0)], -1)  # the drawing as our slots
        for i in range(len(names)):
            mi, si = M == i, S == i
            inter[i] += (mi & si).sum()
            union[i] += (mi | si).sum()
            area[i] += mi.sum()
        a_ = np.full((size, size, 3), 255, np.uint8)
        b_ = np.full((size, size, 3), 255, np.uint8)
        for i, n in enumerate(names):
            a_[M == i] = colors.get(n, (128, 128, 128))
            b_[S == i] = colors.get(n, (128, 128, 128))
        pictures.append(Image.fromarray(np.hstack([a_, b_])))
    keep = area > 0.003 * area.sum()
    per_slot = {n: float(inter[i] / max(union[i], 1)) for i, n in enumerate(names) if keep[i]}
    mean = float(sum(inter[keep]) / max(sum(union[keep]), 1))
    return per_slot, mean, pictures


def tune(reference, render, params, steps, rounds=3, size=400):
    """
    Coordinate descent on a piece's numeric `params` (`steps` = how far to try each one) for the best mean
    silhouette overlap with the drawing. `render(params)` returns our side-by-side views (`ours`); the drawing's
    alignment is fitted once, on the starting parameters, so the tuning moves the piece, not the drawing.
    Returns `(params, overlap per view)`.
    """
    ref_views = _views(mm.foreground_mask(np.asarray(reference.convert("RGB"))), len(VIEWS))

    def my_views(p):
        return _views((np.asarray(render(p).convert("RGB")) != WHITE).any(-1), len(VIEWS))

    fixed = []
    for r, m in zip(ref_views, my_views(params)):
        k = 0.8 * size / max(m.shape)
        mine_c = _place(m, k, 0, 0, size, size / 2, 0.9 * size)
        best = (-1, None)
        for s in k * m.shape[0] / r.shape[0] * np.linspace(0.85, 1.15, 13):
            for dx in range(-24, 25, 6):
                for dy in range(-24, 25, 6):
                    c = _place(r, s, dx, dy, size, size / 2, 0.9 * size)
                    iou = (c & mine_c).sum() / max((c | mine_c).sum(), 1)
                    if iou > best[0]:
                        best = (iou, c)
        fixed.append((best[1], k))

    def score(p):
        ious = []
        for (ref_c, k), m in zip(fixed, my_views(p)):
            mine_c = _place(m, k, 0, 0, size, size / 2, 0.9 * size)
            ious.append((ref_c & mine_c).sum() / max((ref_c | mine_c).sum(), 1))
        return float(np.mean(ious)), ious

    p = dict(params)
    best, per = score(p)
    for _ in range(rounds):
        for key, st in steps.items():
            for d in (st, -st, st / 2, -st / 2):
                q = dict(p, **{key: p[key] + d})
                sc, pv = score(q)
                if sc > best + 1e-4:
                    p, best, per = q, sc, pv
    return p, per


def sheet(reference, mine, results, path, size=400):
    """The drawing, ours and the overlays in one image."""
    out = Image.new("RGB", (size * (2 + len(results)), size + 30), (20, 22, 28))
    d = ImageDraw.Draw(out)
    for i, (title, im) in enumerate((("drawing", reference), ("ours", mine))):
        im = im.copy()
        im.thumbnail((size, size))
        out.paste(im, (i * size, 30 + (size - im.height) // 2))
        d.text((i * size + 8, 6), title, fill=(230, 230, 230), font_size=16)
    for j, (iou, over, _) in enumerate(results):
        out.paste(over, ((2 + j) * size, 30))
        d.text(((2 + j) * size + 8, 6), f"view {j}: overlap {iou:.0%}  (red: drawing only, blue: ours only)",
               fill=(230, 230, 230), font_size=14)
    out.save(path)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("character", help="a character as family/name (see `characters.keys()`)")
    ap.add_argument("--pieces", help="comma-separated piece name prefixes to keep (default: all)")
    ap.add_argument("--brief", default="nothing", help="what the drawing should redesign")
    ap.add_argument("--reference", help="reuse this drawing instead of generating one")
    ap.add_argument("--provider", default="gemini")
    ap.add_argument("--regions", action="store_true", help="also match the drawing's color regions to our slots")
    ap.add_argument("-o", "--out", default=".")
    args = ap.parse_args()
    recipe = characters.load(args.character)
    pieces, colors = characters.build(recipe)
    keep = args.pieces.split(",") if args.pieces else None
    chosen = [p for name, p in pieces.items() if keep is None or any(name.startswith(k) for k in keep)]
    os.makedirs(args.out, exist_ok=True)
    mine = ours(chosen, colors)
    mine.save(os.path.join(args.out, "ours.png"))
    if args.reference:
        reference = Image.open(args.reference).convert("RGB")
    else:
        reference = inspire(mine, args.brief, args.provider)
        reference.save(os.path.join(args.out, "reference.png"))
    results = compare(reference, mine)
    sheet(reference, mine, results, os.path.join(args.out, "match.png"))
    print("overlap per view:", ", ".join(f"{iou:.0%}" for iou, _, _ in results))
    if args.regions:
        per_slot, mean, pictures = regions(reference, chosen, colors, [fit for _, _, fit in results])
        for j, p in enumerate(pictures):
            p.save(os.path.join(args.out, f"regions{j}.png"))
        print(f"region overlap {mean:.0%}:", ", ".join(f"{n} {v:.0%}" for n, v in sorted(per_slot.items(), key=lambda kv: kv[1])))


if __name__ == "__main__":
    main()
