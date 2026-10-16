#!/usr/bin/env python3
"""
Image-to-3D parts for the monster pipeline (`build_monster.py`): forms too organic for simple shapes (a head
with its ears, tusks and hair) are reconstructed by TRELLIS (`monster_ai.image_to_3d`) from the part's own
turnaround drawing, then made a KayKit-style part: oriented and scaled onto that drawing, decimated, and
colored from the drawing's flat label maps seen from the front and back, one flat palette color per face
(`color_faces`: the mesh is first split along the color boundaries, so geometry carries every shape, like vertex
colors). The drawing is first registered to the reconstruction's own painted features (`align_view`), since the
outline fit alone leaves the drawn eyes, nose and mouth off the sculpted ones.

`MeshHead` is the result in the head's local frame (`build_monster.to_world`: y = 0 at the chin) with the
interface accessories expect from a head (`section`, `surface_z`, `L`, `p`), so crowns still fit around it.
"""

import os

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage, optimize

import adaptive_color_subdivision as acs
import monster_mesh as mm


def _glb_texture(g, binary):
    """The first image of a GLB as an RGB array, or None."""
    if not g.get("images"):
        return None
    import io

    bv = g["bufferViews"][g["images"][0]["bufferView"]]
    return np.asarray(Image.open(io.BytesIO(binary[bv.get("byteOffset", 0):bv.get("byteOffset", 0) + bv["byteLength"]])).convert("RGB"))


def load_glb_mesh(path):
    """Positions and triangle indices of every mesh primitive in a GLB (TRELLIS nodes carry no transforms)."""
    g, binary, acc = mm.read_glb(path)
    verts, faces = [], []
    for m in g["meshes"]:
        for pr in m["primitives"]:
            faces.append(acc(pr["indices"]).reshape(-1, 3).astype(np.int64) + sum(len(x) for x in verts))
            verts.append(acc(pr["attributes"]["POSITION"]).astype(float))
    return np.vstack(verts), np.vstack(faces)


def _vertex_rgb(acc, pr):
    """A primitive's `COLOR_0` as sRGB 0..255 (glTF stores vertex colors linear, floats or normalized ints)."""
    raw = acc(pr["attributes"]["COLOR_0"])[:, :3]
    c = raw.astype(float) / ({np.uint8: 255.0, np.uint16: 65535.0}.get(raw.dtype.type, 1.0))
    c = np.clip(c, 0.0, 1.0)
    return 255.0 * np.where(c <= 0.0031308, 12.92 * c, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def load_glb_textured(path):
    """
    A GLB's first primitive as it is stored, vertices split along UV seams: `(verts, faces, uv, texture)`, or None
    when it has no UVs or texture. `load_glb_mesh` samples one texel per vertex, too coarse to see small painted
    features (an eye, a nostril) that `align_view` needs; one sample per vertex is too coarse for those. A dense reconstruction with vertex colors instead (the
    local TRELLIS.2 runner's) gives `(verts, faces, rgb, None)`: its colors are already as fine as its vertices.
    """
    g, binary, acc = mm.read_glb(path)
    tex = _glb_texture(g, binary)
    pr = g["meshes"][0]["primitives"][0]
    if "COLOR_0" in pr["attributes"]:
        return (acc(pr["attributes"]["POSITION"]).astype(float), acc(pr["indices"]).reshape(-1, 3).astype(np.int64),
                _vertex_rgb(acc, pr), None)
    if tex is None or "TEXCOORD_0" not in pr["attributes"]:
        return None
    return (acc(pr["attributes"]["POSITION"]).astype(float), acc(pr["indices"]).reshape(-1, 3).astype(np.int64),
            acc(pr["attributes"]["TEXCOORD_0"]).astype(float), tex)


def weld(verts, faces, tol=1e-5):
    """Merges vertices closer than `tol` (TRELLIS splits them along texture seams, which a decimator would tear
    open) and drops faces that collapse."""
    key = np.round(verts / tol).astype(np.int64)
    _, first, inverse = np.unique(key, axis=0, return_index=True, return_inverse=True)
    f = inverse.reshape(-1)[faces]
    f = f[(f[:, 0] != f[:, 1]) & (f[:, 1] != f[:, 2]) & (f[:, 0] != f[:, 2])]
    return verts[first], f


def solid_voxels(verts, faces, origin, step, shape):
    """
    Occupancy of a (possibly leaky) mesh's solid on a voxel grid (`origin`, `step`, `shape`): the surface
    rasterized by dense samples over every triangle, holes closed by the narrowest dilation that lets the inside
    fill, then undone. A big opening (an open neck stump) leaks through any closing: then every slice along each
    axis is filled on its own and a voxel is inside when two of its three slices say so, since a hole breaks only
    the slices through it.
    """
    occ = np.zeros(shape, bool)
    tri = np.asarray(verts, float)[np.asarray(faces, np.int64)]
    longest = np.max([np.linalg.norm(tri[:, i] - tri[:, (i + 1) % 3], axis=1) for i in range(3)], 0)
    # Barycentric samples finer than half a voxel, as many as each triangle needs (one dense lattice for all would
    # sample a 200k-face reconstruction for its single longest edge: minutes and gigabytes).
    counts = np.clip(np.ceil(longest / (0.5 * step)), 1, 64).astype(int)
    hi = np.asarray(shape) - 1
    for k in np.unique(counts):
        a, b = np.meshgrid(np.arange(k + 1), np.arange(k + 1), indexing="ij")
        keep = a + b <= k
        w = np.stack([a[keep], b[keep], k - a[keep] - b[keep]], 1) / k
        sel = np.flatnonzero(counts == k)
        chunk = max(1, 2_000_000 // len(w))
        for i in range(0, len(sel), chunk):
            pts = np.einsum("sk,fkd->fsd", w, tri[sel[i:i + chunk]]).reshape(-1, 3)
            idx = np.clip(np.rint((pts - origin) / step).astype(np.int32), 0, hi)
            occ[idx[:, 0], idx[:, 1], idx[:, 2]] = True

    def close(k):  # (shell, filled solid, whether the closing sealed it)
        shell = ndimage.binary_dilation(occ, iterations=k)
        solid = ndimage.binary_fill_holes(shell)
        return shell, solid, solid.sum() > 1.6 * shell.sum()

    shell, solid, sealed = close(3)  # a leak the widest closing can't seal won't seal narrower: skip those
    if sealed:
        k = 3
        for narrow in (1, 2):  # the narrowest closing that seals keeps the most detail
            attempt = close(narrow)
            if attempt[2]:
                (shell, solid, _), k = attempt, narrow
                break
    else:
        k = 1
        shell = ndimage.binary_dilation(occ, iterations=k)
        votes = np.zeros(shell.shape, np.uint8)
        for axis in range(3):
            st = np.zeros((3, 3, 3), bool)
            idx = [slice(None)] * 3
            idx[axis] = 1
            st[tuple(idx)] = ndimage.generate_binary_structure(2, 1)  # connected within the slice only
            votes += ndimage.binary_fill_holes(shell, structure=st)
        solid = (votes >= 2) | shell
    return ndimage.binary_erosion(solid, iterations=k) | occ


def cap_holes(verts, faces):
    """
    The mesh with every open boundary closed by a fan around its loop's centroid (one new vertex per loop), so a
    cut-open solid (a head with its neck stump cut away) is closed again. Voxelizing an open mesh has to fall back
    on `solid_voxels`' slice votes, which leave tunnels through the whole solid above a big opening.
    """
    from scipy.sparse import coo_matrix
    from scipy.sparse.csgraph import connected_components

    verts, faces = np.asarray(verts, float), np.asarray(faces, np.int64)
    edges = np.vstack([faces[:, [0, 1]], faces[:, [1, 2]], faces[:, [2, 0]]])  # directed as the faces wind them
    _, inv, cnt = np.unique(np.sort(edges, 1), axis=0, return_inverse=True, return_counts=True)
    border = edges[cnt[inv.reshape(-1)] == 1]
    if not len(border):
        return verts, faces
    n = len(verts)
    _, comp = connected_components(coo_matrix((np.ones(len(border)), (border[:, 0], border[:, 1])), shape=(n, n)),
                                   directed=False)
    ids = np.unique(comp[border[:, 0]])
    ring = np.unique(border)
    loop = np.searchsorted(ids, comp[ring])
    centers = np.stack([np.bincount(loop, verts[ring, d], len(ids)) for d in range(3)], 1)
    centers /= np.bincount(loop, minlength=len(ids))[:, None]
    cap = np.column_stack([border[:, 1], border[:, 0], n + np.searchsorted(ids, comp[border[:, 0]])])  # reversed
    return np.vstack([verts, centers]), np.vstack([faces, cap])


def polygonize(occ, origin, step, smooth=1.0):
    """
    The surface of a voxel solid, blurred by `smooth` voxels (higher irons out surface noise), by marching cubes
    (Lewiner): manifold, which the decimator needs (surface nets' few non-manifold edges at thin spots made it
    collapse whole heads into slivers).
    """
    from skimage.measure import marching_cubes

    field = ndimage.gaussian_filter(occ.astype(np.float32), max(smooth, 0.6))
    v, f, _, _ = marching_cubes(np.pad(field, 1), level=0.5, spacing=(step, step, step))
    return (v - step + origin).astype(float), f[:, ::-1].astype(np.int64)  # outward winding


def _grid(lo, hi, res):
    step = float((hi - lo).max()) / (res - 8)
    origin = lo - 4 * step
    return origin, step, np.ceil((hi - origin) / step).astype(int) + 5


def remesh_solid(verts, faces, res=320, smooth=1.0):
    """
    A clean, watertight version of a reconstruction (`solid_voxels` on a `res`-voxel grid, then `polygonize`).
    TRELLIS.2's decoder output is non-manifold with holes, which no decimator can bring down to a low-poly budget.
    """
    verts = np.asarray(verts, float)
    origin, step, shape = _grid(verts.min(0), verts.max(0), res)
    return polygonize(solid_voxels(verts, faces, origin, step, shape), origin, step, smooth)


def _yaw(verts, k):
    """Rotates by `k` quarter turns about +y."""
    a = k * np.pi / 2
    c, s = np.cos(a), np.sin(a)
    return verts @ np.array([[c, 0, -s], [0, 1, 0], [s, 0, c]])


def _pitch(verts, deg):
    """Tilts by `deg` degrees about +x (positive turns the face down, toward -y)."""
    a = np.radians(deg)
    c, s_ = np.cos(a), np.sin(a)
    return verts @ np.array([[1.0, 0.0, 0.0], [0.0, c, s_], [0.0, -s_, c]])


def _silhouette(P2, faces, shape, to_px):
    """Filled projection of triangles (2D points `P2`) into a mask of `shape`, through `to_px(P2) -> (col, row)`."""
    im = Image.new("L", (shape[1], shape[0]), 0)
    d = ImageDraw.Draw(im)
    px = to_px(P2)
    for t in px[faces]:
        d.polygon([tuple(p) for p in t], fill=1)
    return np.asarray(im, bool)


def _fit_view(P2, faces, mask, scales=np.linspace(0.92, 1.08, 9)):
    """
    Uniform scale and offset (`k`, `c0`, `r0`: col = c0 + k x, row = r0 - k y) placing projected points `P2` onto
    a view's silhouette `mask`: tops aligned and centres matched, then refined by silhouette overlap. Returns
    `(k, c0, r0, iou)`.
    """
    rows, cols = np.flatnonzero(mask.any(1)), np.flatnonzero(mask.any(0))
    lo, hi = P2.min(0), P2.max(0)
    k0 = (rows[-1] + 1 - rows[0]) / max(hi[1] - lo[1], 1e-9)
    best = (-1.0, k0, 0.0, 0.0)
    for ks in scales:
        k = k0 * ks
        c0 = 0.5 * (cols[0] + cols[-1] + 1) - k * 0.5 * (lo[0] + hi[0])
        r0 = rows[0] + k * hi[1]
        sil = _silhouette(P2, faces, mask.shape, lambda p: np.column_stack([c0 + k * p[:, 0], r0 - k * p[:, 1]]))
        for dx in np.rint(np.linspace(-0.03, 0.03, 5) * mask.shape[1]).astype(int):  # sideways: shift, don't redraw
            moved = np.zeros_like(sil)
            if dx >= 0:
                moved[:, dx:] = sil[:, :sil.shape[1] - dx]
            else:
                moved[:, :dx] = sil[:, -dx:]
            iou = (moved & mask).sum() / max((moved | mask).sum(), 1)
            if iou > best[0]:
                best = (iou, k, c0 + dx, r0)
    iou, k, c0, r0 = best
    return k, c0, r0, iou


def orient(verts, faces, front, textured=None, tilts=np.arange(-30, 31, 6)):
    """
    The rotation that makes a TRELLIS reconstruction face its drawing (+z = the character's front, +y up):
      - a quarter-turn about +y: the front silhouette picks the axis (a head is wider across the ears than deep);
        TRELLIS puts the face of the first conditioning view toward its +x, which picks the sign,
      - then a tilt about +x: TRELLIS returns heads tilted back by some 15 degrees, which keeps the outline but
        lifts every sculpted feature off its drawn place (the face looks up). The tilt whose render of the
        reconstruction's own baked texture best matches the drawing wins (`textured`, see `load_glb_textured`;
        the outline decides when the bake shows nothing).
    `faces` should be decimated (all are rasterized). Returns `(verts, front fit (k, c0, r0, iou), turns, tilt)`.
    """
    fits = [_fit_view(_yaw(verts, q)[:, :2], faces, front.mask, scales=[1.0])[3] for q in (1, 3)]
    across = max(fits) > _fit_view(_yaw(verts, 0)[:, :2], faces, front.mask, scales=[1.0])[3] + 0.01  # ties: keep
    q = 3 if across else 0  # quarter-turns taking TRELLIS +x to +z, or keeping +z when it already faces there

    def score(deg):
        V = _pitch(_yaw(verts, q), deg)
        k, c0, r0, iou = _fit_view(V[:, :2], faces, front.mask, scales=[1.0])
        feat = None
        if textured is not None:  # half the registration's resolution is plenty to rank tilts
            tv = _pitch(_yaw(textured[0], q), deg)
            pair = _render_pair((tv, *textured[1:]), front, lambda Q: np.column_stack([c0 + k * Q[:, 0], r0 - k * Q[:, 1]]),
                                work=WORK_SIZE // 2)
            feat = _score(*pair) if pair is not None else None
        return feat, iou

    scores = {int(d): score(d) for d in tilts}
    use_feat = all(f is not None for f, _ in scores.values()) and max(f for f, _ in scores.values()) >= 0.2
    best = max(scores, key=lambda d: scores[d][0] if use_feat else scores[d][1])
    for d in range(best - 4, best + 5):  # refine to a degree around the coarse best
        if d not in scores:
            scores[d] = score(d)
    best = max(scores, key=lambda d: scores[d][0] if use_feat else scores[d][1])
    V = _pitch(_yaw(verts, q), best)
    return V, _fit_view(V[:, :2], faces, front.mask), q, best


# ---------------------------------------------------------------------------
# Feature alignment
# ---------------------------------------------------------------------------
# `_fit_view` registers the reconstruction to its drawing by OUTLINE only, but the two disagree about where the
# face is: TRELLIS keeps the drawing's silhouette while sculpting the brows, nose and mouth a tenth of the head's
# height off their drawn places (the lower face is longer, the brow ridge higher), so painted eye shadow lands on
# the brow ridge and the nose paint under the nose. The reconstruction's own baked texture, however, is painted
# with the same features exactly where the mesh sculpts them (dark brows over the brow ridge, white eyes in the
# sockets, tusks, nose). Rendering that texture into a drawing's pixel grid and registering the drawing onto it
# gives a smooth warp of the projection that lands each drawn feature on its sculpted one.
WORK_SIZE = 270  # long side, in pixels, of the working images the registration runs on
# (control grid size, blur sigma as a share of the long side): coarse-to-fine, blurring less as the grid refines
ALIGN_LEVELS = ((4, 0.052), (5, 0.044), (7, 0.037), (10, 0.030), (14, 0.022), (20, 0.015))
ALIGN_SMOOTH = 0.5  # weight of the control grid's bending energy against the correlation (1 = perfect match)
ALIGN_ITERS = 60  # L-BFGS iterations per level


def _rasterize(px, depth, attrs, faces, shape):
    """
    Z-buffered triangles seen from the front: `px` (n, 2) screen points as (col, row) in continuous pixel
    coordinates, `depth` (n,) with larger = nearer, `attrs` (n, c) interpolated per pixel. Returns the depth buffer
    (-inf where nothing is drawn) and the (h, w, c) attribute image. Splats a barycentric lattice finer than half
    a pixel over every triangle and keeps the nearest sample in each pixel: no loop over the 30k small triangles
    of a reconstruction, and no holes, since the lattice is denser than the pixels. Faces smaller than a pixel are
    splatted by their corners and centres alone.
    """
    h, w = shape
    tri = px[faces]  # (f, 3, 2)
    edge = np.linalg.norm(tri - np.roll(tri, 1, axis=1), axis=2).max(1)
    pix, zs, vals = [], [], []
    small = edge < 1.5
    if small.any():  # faces smaller than the pixels: their corners and centres cover them
        corners = np.unique(faces[small])
        at = np.vstack([px[corners], tri[small].mean(1)])
        col, row = np.floor(at[:, 0]).astype(int), np.floor(at[:, 1]).astype(int)
        inside = (col >= 0) & (col < w) & (row >= 0) & (row < h)
        pix.append((row * w + col)[inside])
        zs.append(np.concatenate([depth[corners], depth[faces[small]].mean(1)])[inside])
        vals.append(np.vstack([attrs[corners], attrs[faces[small]].mean(1)])[inside])
    steps = np.where(small, 0, np.clip(np.ceil(2 * edge), 2, 400)).astype(int)
    for n in np.unique(steps[steps > 0]):
        sel = np.flatnonzero(steps == n)
        i, j = (a.ravel() for a in np.meshgrid(np.arange(n + 1), np.arange(n + 1), indexing="ij"))
        keep = i + j <= n
        bary = np.column_stack([1 - (i[keep] + j[keep]) / n, i[keep] / n, j[keep] / n])  # (k, 3)
        at = np.einsum("kc,fcd->fkd", bary, tri[sel]).reshape(-1, 2)
        col, row = np.floor(at[:, 0]).astype(int), np.floor(at[:, 1]).astype(int)
        inside = (col >= 0) & (col < w) & (row >= 0) & (row < h)
        pix.append((row * w + col)[inside])
        zs.append(np.einsum("kc,fc->fk", bary, depth[faces[sel]]).ravel()[inside])
        vals.append(np.einsum("kc,fcd->fkd", bary, attrs[faces[sel]]).reshape(-1, attrs.shape[1])[inside])
    pix, zs, vals = np.concatenate(pix), np.concatenate(zs), np.concatenate(vals)
    order = np.argsort(zs, kind="stable")  # farthest first, so the nearest sample is written last and stays
    zbuf = np.full(h * w, -np.inf)
    out = np.zeros((h * w, attrs.shape[1]))
    zbuf[pix[order]], out[pix[order]] = zs[order], vals[order]
    return zbuf.reshape(h, w), out.reshape(h, w, -1)


def _blur_valid(a, valid, sigma):
    """Gaussian blur of the (c, h, w) image `a` over the pixels where `valid`, which extends its colors smoothly
    past the valid region instead of mixing in whatever lies outside (white paper, black void)."""
    den = ndimage.gaussian_filter(valid.astype(float), sigma) + 1e-6
    return np.stack([ndimage.gaussian_filter(x * valid, sigma) / den for x in a])


def _bspline_basis(n, g):
    """(n, g) uniform cubic B-spline weights of `g` control points spanning `n` pixels."""
    u = np.arange(n) * (g - 3) / (n - 1)
    i = np.clip(np.floor(u).astype(int), 0, g - 4)
    t = u - i
    w = np.stack([(1 - t) ** 3 / 6, (3 * t ** 3 - 6 * t ** 2 + 4) / 6, (-3 * t ** 3 + 3 * t ** 2 + 3 * t + 1) / 6, t ** 3 / 6], 1)
    B = np.zeros((n, g))
    for j in range(4):
        B[np.arange(n), i + j] = w[:, j]
    return B


def _register(R, D, wgt, rvalid, dvalid, smooth=ALIGN_SMOOTH, limit=0.25):
    """
    Displacement field `(dy, dx)` (pixels of the working grid) with `D(p + d(p)) ~ R(p)`: the colors of the
    reference `R` and the drawing `D` (both (3, h, w)), compared by per-channel normalized cross-correlation over
    the weights `wgt` (invariant to the reconstruction's dark, off-hue bake). The field is a sum of cubic B-spline
    grids, coarse to fine, each optimized by L-BFGS with an analytic gradient and a bending penalty. Coarse levels
    blur the images so that features a tenth of the head apart still attract each other. Gives up (None) once the
    field's 90th-percentile shift over the weights passes `limit` of the long side: a drawing with no features to
    register on drifts off instead of settling.
    """
    C, h, w = R.shape
    yy, xx = np.mgrid[0:h, 0:w].astype(float)
    dy, dx = np.zeros((h, w)), np.zeros((h, w))
    size, total = max(h, w), wgt.sum()
    for g, share in ALIGN_LEVELS:
        sigma = share * size
        Rb, Db = _blur_valid(R, rvalid, sigma), _blur_valid(D, dvalid, sigma)
        mean = (wgt * Rb).sum((1, 2), keepdims=True) / total
        Rz = (Rb - mean) / np.sqrt((wgt * (Rb - mean) ** 2).sum((1, 2), keepdims=True) / total + 1e-9)
        grads = [np.gradient(d) for d in Db]
        By, Bx = _bspline_basis(h, g), _bspline_basis(w, g)

        def energy(theta):
            cy, cx = theta[:g * g].reshape(g, g), theta[g * g:].reshape(g, g)
            at = [yy + dy + By @ cy @ Bx.T, xx + dx + By @ cx @ Bx.T]
            Wc = np.stack([ndimage.map_coordinates(d, at, order=1, mode="nearest") for d in Db])
            wc = Wc - (wgt * Wc).sum((1, 2), keepdims=True) / total
            var = (wgt * wc * wc).sum((1, 2), keepdims=True) / total + 1e-9
            ncc = (wgt * Rz * wc).sum((1, 2), keepdims=True) / (total * np.sqrt(var))
            gw = wgt / total * (Rz / np.sqrt(var) - ncc * wc / var)  # d ncc / d Wc
            gy, gx = (sum(gw[c] * ndimage.map_coordinates(grads[c][a], at, order=1, mode="nearest") for c in range(C))
                      for a in (0, 1))
            grad = [-By.T @ gy @ Bx / C, -By.T @ gx @ Bx / C]
            e = -ncc.mean()
            for c, gc in zip((cy, cx), grad):  # bending energy: second differences of the control grid
                for ax in (0, 1):
                    dd = np.diff(c, 2, axis=ax) / size
                    e += smooth * (dd ** 2).sum()
                    gg = np.zeros_like(c)
                    lo, mid, hi = ((slice(None, -2), slice(1, -1), slice(2, None)) if ax == 0 else
                                   ((slice(None), slice(None, -2)), (slice(None), slice(1, -1)), (slice(None), slice(2, None))))
                    gg[lo] += dd
                    gg[mid] -= 2 * dd
                    gg[hi] += dd
                    gc += smooth * 2 * gg / size
            return e, np.concatenate([grad[0].ravel(), grad[1].ravel()])

        cell = size / (g - 3)
        res = optimize.minimize(energy, np.zeros(2 * g * g), jac=True, method="L-BFGS-B",
                                bounds=[(-0.5 * cell, 0.5 * cell)] * (2 * g * g), options=dict(maxiter=ALIGN_ITERS))
        dy = dy + By @ res.x[:g * g].reshape(g, g) @ Bx.T
        dx = dx + By @ res.x[g * g:].reshape(g, g) @ Bx.T
        if np.percentile(np.hypot(dy, dx)[wgt > 0], 90) > limit * size:
            return None
    return dy, dx


def _score(R, D, wgt, rvalid, dvalid, dy=None, dx=None):
    """Mean per-channel normalized cross-correlation of reference `R` and drawing `D` warped by `(dy, dx)`, both
    lightly blurred (the drawing's painted edges and the mesh's baked ones never agree to the pixel)."""
    sigma = 0.008 * max(R.shape[1:])
    Rb, Db = _blur_valid(R, rvalid, sigma), _blur_valid(D, dvalid, sigma)
    if dy is not None:
        yy, xx = np.mgrid[0:R.shape[1], 0:R.shape[2]].astype(float)
        Db = np.stack([ndimage.map_coordinates(d, [yy + dy, xx + dx], order=1, mode="nearest") for d in Db])
    m = wgt > 0
    return float(np.mean([np.corrcoef(r[m], d[m])[0, 1] for r, d in zip(Rb, Db)]))


class FeatureWarp:
    """
    A smooth displacement of a view's pixels, from `_register`: the drawing pixel that colors the mesh point shown
    at view pixel `p` is `p + d(p)`. `n` = full-resolution pixels per working pixel of the field.
    """

    def __init__(self, dy, dx, n):
        self.dy, self.dx, self.n = dy, dx, n
        self._inv = None

    def _d(self, col, row):
        at = [np.atleast_1d(np.asarray(row, float)) / self.n - 0.5, np.atleast_1d(np.asarray(col, float)) / self.n - 0.5]
        return [ndimage.map_coordinates(a, at, order=1, mode="nearest").reshape(np.shape(col)) * self.n for a in (self.dy, self.dx)]

    def __call__(self, col, row):
        """The drawing's `(col, row)` for the mesh point at view `(col, row)`."""
        dy, dx = self._d(col, row)
        return col + dx, row + dy

    def inverse(self, col, row):
        """
        The view `(col, row)` whose drawing point is the given one. The field stretches the lower face by a factor
        of two, far too steeply for a fixed-point iteration, so this interpolates the forward map's samples (a
        triangulation of where each view pixel lands) the other way round; outside them, the nearest sample's shift.
        """
        if self._inv is None:
            from scipy.interpolate import LinearNDInterpolator, NearestNDInterpolator

            h, w = self.dy.shape
            yy, xx = np.mgrid[0:h:2, 0:w:2]
            src = np.column_stack([(xx.ravel() + 0.5) * self.n, (yy.ravel() + 0.5) * self.n])
            dst = src + np.column_stack([self.dx[yy, xx].ravel(), self.dy[yy, xx].ravel()]) * self.n
            self._inv = (LinearNDInterpolator(dst, src), NearestNDInterpolator(dst, src))
        q = np.column_stack([np.ravel(col), np.ravel(row)]).astype(float)
        p = self._inv[0](q)
        bad = np.isnan(p[:, 0])
        if bad.any():
            p[bad] = self._inv[1](q[bad])
        return p[:, 0].reshape(np.shape(col)), p[:, 1].reshape(np.shape(row))


def _render_pair(textured, view, to_px, side=1, work=None):
    """
    The reconstruction's baked texture rendered into `view`'s pixel grid (`to_px` places vertices; `side` -1
    looks from the back), next to the drawing, both block-averaged to `WORK_SIZE`: `(R, D, weight, covered,
    drawn)` with only the bake's informative channels (its blue is empty on olive skin), or None when the bake
    covers too little or shows nothing.
    """
    verts, faces, uv, tex = textured
    H, W = view.mask.shape
    n = max(1, round(max(H, W) / (work or WORK_SIZE)))
    h, w = -(-H // n), -(-W // n)
    zbuf, at = _rasterize(to_px(verts) / n, side * verts[:, 2], uv, faces, (h, w))
    cov = np.isfinite(zbuf)
    if cov.sum() < 0.05 * h * w:
        return None
    R = np.zeros((3, h, w))
    if tex is None:  # vertex colors, interpolated by the rasterizer
        R[:, cov] = at[cov].T
    else:
        u, v = at[cov, 0] * tex.shape[1] - 0.5, at[cov, 1] * tex.shape[0] - 0.5
        for c in range(3):
            R[c][cov] = ndimage.map_coordinates(tex[..., c].astype(float), [v, u], order=1, mode="nearest")
    pad = ((0, h * n - H), (0, w * n - W))
    m = np.pad(view.mask.astype(float), pad)
    rgb = np.pad(view.rgb.astype(float), pad + ((0, 0),), mode="edge") * m[..., None]
    block = lambda a: a.reshape((h, n, w, n) + a.shape[2:]).mean((1, 3))
    dvalid = block(m) > 0.5
    D = np.stack([block(rgb[..., c]) / np.maximum(block(m), 1e-6) for c in range(3)])
    wgt = ndimage.binary_erosion(cov, iterations=2).astype(float)
    spread = R[:, wgt > 0].std(1) if wgt.sum() >= 100 else np.zeros(3)
    keep = spread >= max(0.25 * spread.max(), 0.5)
    if not keep.any():
        return None
    return R[keep], D[keep], wgt, cov, dvalid


def align_view(textured, view, to_px, side=1, name="front"):
    """
    The `FeatureWarp` registering `view` (a full-resolution drawing `View`) to the reconstruction's baked texture
    (`load_glb_textured`, vertices turned into the view's frame): the texture is rendered as seen from the front
    (`side` = 1) or back (-1) with `to_px(verts)` placing vertices in the drawing's pixels, then `_register`ed
    against the drawing. Returns None, so the plain outline fit stays, when the bake shows nothing the drawing
    shares (a flat or black texture) or the warp doesn't clearly improve the correlation.
    """
    pair = _render_pair(textured, view, to_px, side)
    if pair is None:  # nothing to register on
        return None
    R, D, wgt, cov, dvalid = pair
    n = max(1, round(max(view.mask.shape) / WORK_SIZE))
    before = _score(R, D, wgt, cov, dvalid)
    if before >= 0.8:
        print(f"  {name} features: already in place (correlation {before:.2f})")
        return None
    # A flexible warp can also fit noise when the bake has little to show: demand a clear gain, and no folds. A
    # field that folds is retried stiffer before only its global part is kept.
    for stiff in (1.0, 4.0, 16.0):
        field = _register(R, D, wgt, cov, dvalid, smooth=ALIGN_SMOOTH * stiff)
        if field is None:
            print(f"  {name} features: nothing to register on (the field drifted)")
            return None
        dy, dx = field
        after = _score(R, D, wgt, cov, dvalid, dy, dx)
        jac = (1 + np.gradient(dy, axis=0)) * (1 + np.gradient(dx, axis=1)) - np.gradient(dy, axis=1) * np.gradient(dx, axis=0)
        folded = float((jac[wgt > 0] < 0.05).mean())
        ok = after >= 0.4 and after >= before + 0.15 and folded <= 0.01
        print(f"  {name} features (stiffness x{stiff:g}): correlation {before:.2f} -> {after:.2f}; "
              f"{_describe(dy, dx, wgt)}; {100 * folded:.1f}% folded -> {'kept' if ok else 'rejected'}")
        if ok:
            return FeatureWarp(dy, dx, n)
        if folded <= 0.01:  # it didn't fold, it just didn't help: stiffer won't either
            break
    # The detailed field folded or fit noise, but its global part (shift and scale per axis) is usually still
    # right: TRELLIS sculpts the whole face a few percent off. Keep that when it clearly helps on its own.
    ay, ax = _affine_part(dy, dx, wgt)
    after_aff = _score(R, D, wgt, cov, dvalid, ay, ax)
    if after_aff >= 0.35 and after_aff >= before + 0.08:
        print(f"  {name} features: kept the field's global fit instead (correlation {before:.2f} -> {after_aff:.2f})")
        return FeatureWarp(ay, ax, n)
    print(f"  WARNING: {name} features not registered (global fit alone: {after_aff:.2f}); "
          f"the drawing is placed by its outline only")
    return None


def _affine_part(dy, dx, wgt):
    """The weighted least-squares affine fit `(dy, dx)` of a displacement field over the weights."""
    h, w = dy.shape
    yy, xx = np.mgrid[0:h, 0:w].astype(float)
    m = wgt > 0
    A = np.column_stack([np.ones(m.sum()), yy[m], xx[m]])
    full = np.stack([np.ones(h * w), yy.ravel(), xx.ravel()], 1)
    out = []
    for d in (dy, dx):
        coef = np.linalg.lstsq(A, d[m], rcond=None)[0]
        out.append((full @ coef).reshape(h, w))
    return out


def _describe(dy, dx, wgt):
    """
    How the drawn features sit against the mesh's, from the field's best global fit over the weights, per axis:
    a shift (as a share of the mesh's extent, at its centre; positive = the drawing's features lie lower / to the
    right) and a scale (> 1: the drawn face is longer / wider), plus the non-rigid residual left once that is
    taken out.
    """
    rows, cols = np.nonzero(wgt)
    out = []
    for d, pos in ((dy, rows), (dx, cols)):
        extent = pos.max() - pos.min() + 1
        m, t = np.polyfit(pos - pos.mean(), d[rows, cols], 1)
        resid = d[rows, cols] - (m * (pos - pos.mean()) + t)
        out.append((t / extent, 1 + m, np.sqrt((resid ** 2).mean()) / extent))
    (ty, sy, ry), (tx, sx, rx) = out
    return (f"drawn features {100 * ty:+.1f}% vertically x{sy:.2f}, {100 * tx:+.1f}% horizontally x{sx:.2f}, "
            f"non-rigid residual {100 * ry:.1f}% / {100 * rx:.1f}%")


def simplify(verts, faces, target, preserve_border=False):
    """
    Quadric decimation to about `target` faces, in steps of at most 4x with a gentle aggressiveness: one big jump
    (or the default aggressiveness) collapses dense, smooth remeshes into slivers.
    """
    import fast_simplification

    v, f = np.asarray(verts, float), np.asarray(faces, np.int64)
    while len(f) > target * 1.05:
        step = max(target, len(f) // 4)
        v2, f2 = fast_simplification.simplify(v, f, target_count=int(step), agg=3.0, preserve_border=preserve_border)
        f2 = np.asarray(f2, np.int64)
        if len(f2) >= len(f) * 0.98:  # stalled
            break
        v, f = np.asarray(v2, float), f2
    return v, f


def decimate_face_first(verts, faces, max_tris, face_share=0.5):
    """
    Simplifies a head (local frame: y up from the chin, +z forward) to about `max_tris`, giving the face patch
    (front-facing, inside the skull's width, chin to brow) `face_share` of the budget. Uniform decimation removes
    small forms first, which on a head are exactly the eyelids, lips and nostrils. The two patches are simplified
    with their shared border kept, so they weld back without cracks.
    """
    verts, faces = np.asarray(verts, float), np.asarray(faces, np.int64)
    if len(faces) > 8 * max_tris:  # the patches' kept border would otherwise stall the budget on a dense mesh
        verts, faces = simplify(verts, faces, 8 * max_tris)
    tri = verts[faces]
    cen = tri.mean(1)
    n = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    nz = n[:, 2] / np.maximum(np.linalg.norm(n, axis=1), 1e-12)
    y0, y1 = verts[:, 1].min(), verts[:, 1].max()
    upper = verts[verts[:, 1] > y0 + 0.5 * (y1 - y0)]
    half = 0.5 * np.ptp(upper[:, 0]) if len(upper) else 0.5 * np.ptp(verts[:, 0])
    t = (cen[:, 1] - y0) / max(y1 - y0, 1e-9)
    face = (nz > 0.2) & (np.abs(cen[:, 0]) < 0.75 * half) & (t > 0.05) & (t < 0.8) & (cen[:, 2] > np.median(verts[:, 2]))
    if face.sum() < 50 or (~face).sum() < 50:
        return simplify(verts, faces, max_tris)
    parts = []
    for sel, budget in ((face, face_share * max_tris), (~face, (1.0 - face_share) * max_tris)):
        f = faces[sel]
        used = np.unique(f)
        remap = -np.ones(len(verts), np.int64)
        remap[used] = np.arange(len(used))
        v, f = verts[used], remap[f]
        if len(f) > budget:
            v, f = simplify(v, f, budget, preserve_border=True)
        parts.append((np.asarray(v, float), np.asarray(f, np.int64)))
    v = np.vstack([parts[0][0], parts[1][0]])
    f = np.vstack([parts[0][1], parts[1][1] + len(parts[0][0])])
    v, f = weld(v, f)
    # A collapse across the patches' shared border can leave a needle reaching far across the head: faces that are
    # both long and sliver-thin (big flat faces on the back of a low-poly skull are fine).
    t = v[f]
    longest = np.max([np.linalg.norm(t[:, i] - t[:, (i + 1) % 3], axis=1) for i in range(3)], 0)
    area = 0.5 * np.linalg.norm(np.cross(t[:, 1] - t[:, 0], t[:, 2] - t[:, 0]), axis=1)
    needle = (longest > 0.15 * np.ptp(v, 0).max()) & (longest ** 2 > 40.0 * np.maximum(area, 1e-12))
    # Deleting them would open slits: collapse each one's shortest edge instead (both ends meet at its middle), which
    # removes it and its neighbour across that edge and keeps the surface closed.
    e = np.stack([t[:, (i + 1) % 3] - t[:, i] for i in range(3)], 1)
    short = np.linalg.norm(e, axis=2).argmin(1)
    target = np.arange(len(v))
    for i in np.flatnonzero(needle):
        a, b = f[i, short[i]], f[i, (short[i] + 1) % 3]
        while target[a] != a:
            a = target[a]
        while target[b] != b:
            b = target[b]
        if a != b:
            v[a] = 0.5 * (v[a] + v[b])
            target[b] = a
    for _ in range(len(v)):  # resolve chains of merges
        nxt = target[target]
        if np.array_equal(nxt, target):
            break
        target = nxt
    f = target[f]
    f = f[(f[:, 0] != f[:, 1]) & (f[:, 1] != f[:, 2]) & (f[:, 0] != f[:, 2])]
    used = np.unique(f)
    remap = -np.ones(len(v), np.int64)
    remap[used] = np.arange(len(used))
    return v[used], remap[f]


class MeshHead:
    """
    A reconstructed head in the head's local frame (y = 0 at the chin, +z forward), colored from its drawing's
    front and back views. `to_view_front(P)` / `to_view_back(P)` map local points to those views' pixels.
    """

    p = 2.2

    def __init__(self, verts, faces, views, front_map, back_map, max_tris=2400):
        if max_tris and len(faces) > max_tris:
            verts, faces = decimate_face_first(verts, faces, max_tris)
        self.verts, self.faces = np.asarray(verts, float), np.asarray(faces, np.int64)
        self.front, self.side, self.back = views
        self._fm, self._bm = front_map, back_map
        self.L = self.visible_L = float(self.verts[:, 1].max())
        ys = np.linspace(self.verts[:, 1].min(), self.L, 48)
        secs = [self._slice(y) for y in ys]
        secs = np.array(secs)
        ok = ~np.isnan(secs[:, 0])
        for i in range(4):
            secs[:, i] = np.interp(ys, ys[ok], secs[ok, i])
        self._ys, self._secs = ys, secs
        mid = self.section([0.5 * self.L])
        self.cx, self.A, self.cz, self.B = (float(v[0]) for v in mid)

    def _slice(self, y, n=96):
        """
        Cross-section `(cx, a, cz, b)` of the mesh at height `y` without narrow protrusions: the triangles cut by
        the plane drawn as an outline top-down, filled, then opened with a disc a third of its size, so big ears
        sticking out sideways don't widen a crown into a brim while the skull and hair keep their full outline.
        """
        tri = self.verts[self.faces]
        d = tri[:, :, 1] - y
        cut = (d.min(1) < 0) & (d.max(1) > 0)
        t, dd = tri[cut], d[cut]
        segs = []
        for a, b in ((0, 1), (1, 2), (2, 0)):  # each crossing edge gives one end of a triangle's segment
            hit = (dd[:, a] < 0) != (dd[:, b] < 0)
            w = np.where(hit, dd[:, a] / np.where(hit, dd[:, a] - dd[:, b], 1.0), 0.0)
            pts = t[:, a][:, [0, 2]] + w[:, None] * (t[:, b][:, [0, 2]] - t[:, a][:, [0, 2]])
            segs.append(np.where(hit[:, None], pts, np.nan))
        segs = np.stack(segs, 1)  # (n, 3 edges, 2): exactly two edges hold points
        ok = ~np.isnan(segs[..., 0])
        keep = ok.sum(1) == 2
        segs = segs[keep][ok[keep]].reshape(-1, 2, 2)
        if len(segs) < 3:
            return [np.nan] * 4
        segs = np.array(segs)
        lo, hi = segs.reshape(-1, 2).min(0), segs.reshape(-1, 2).max(0)
        size = float((hi - lo).max()) * 1.1 + 1e-6
        o = 0.5 * (lo + hi) - 0.5 * size
        im = Image.new("L", (n, n), 0)
        draw = ImageDraw.Draw(im)
        for p, q in (segs - o) / size * n:
            draw.line([tuple(p), tuple(q)], fill=1, width=2)
        occ = ndimage.binary_fill_holes(np.asarray(im, bool))
        r = max(2, n // 6)  # opened with a disc of radius r, by distance transforms (a 33-px disc is slow)
        pad = np.pad(occ, r + 1)  # beyond the image counts as outside, as in `binary_opening`
        core = (ndimage.distance_transform_edt(ndimage.distance_transform_edt(pad) <= r) <= r)[r + 1:-r - 1, r + 1:-r - 1]
        if not core.any():
            core = occ
        zs, xs = np.flatnonzero(core.any(1)), np.flatnonzero(core.any(0))  # image rows = z, columns = x
        x0, x1 = o[0] + xs[0] / n * size, o[0] + (xs[-1] + 1) / n * size
        z0, z1 = o[1] + zs[0] / n * size, o[1] + (zs[-1] + 1) / n * size
        return [0.5 * (x0 + x1), 0.5 * (x1 - x0), 0.5 * (z0 + z1), 0.5 * (z1 - z0)]

    def mesh(self):
        return self.verts, self.faces

    def section(self, y):
        y = np.atleast_1d(np.asarray(y, float))
        return tuple(np.interp(y, self._ys, self._secs[:, i]) for i in range(4))

    def surface_z(self, x, y, front=True):
        r = 0.08 * self.L
        near = self.verts[(np.abs(self.verts[:, 0] - x) < r) & (np.abs(self.verts[:, 1] - y) < r)]
        if not len(near):
            return float(self.section([y])[2][0])
        return float(near[:, 2].max() if front else near[:, 2].min())

    @property
    def views(self):
        return [self.front, self.side, self.back]

    def face_views(self, verts, faces):
        v = verts[faces]
        n = np.cross(v[:, 1] - v[:, 0], v[:, 2] - v[:, 0])
        return np.where(n[:, 2] >= 0, 0, 2)

    def map_view(self, k, P):
        """View pixel coordinates `(row, col)` of local points in the front (0) or back (2) drawing."""
        col, row = (self._fm if k == 0 else self._bm)(P)
        view = self.front if k == 0 else self.back
        f = view.h / view.src_h
        return row * f, col * f


def build_head(glb_path, views, head_scale, head_top_row, head_left_col, chin_rig_y, torso_y, s, axis_col,
               tex_views=None, max_tris=2400, smooth=1.5):
    """
    A `MeshHead` from a TRELLIS reconstruction of the head drawing (`views` = its front, side, back `View`s at
    full resolution, the drawing the head was placed on the body by). `head_scale` maps head-drawing pixels to
    body-drawing pixels with the head's top at body row `head_top_row` and its front crop's left edge at body
    column `head_left_col`; `torso_y(row)`, `s` (rig units per body pixel) and `axis_col` map the body drawing to
    the rig, and `chin_rig_y` is the head's base there. `tex_views` (smaller views of the same drawing, packed in
    the atlas) color it. The neck stump an image model draws below the jaw is cut away; hair hanging there stays.
    The projection is warped so the drawn face lands on the mesh's sculpted face (`align_view`).
    `smooth` (voxels of blur in `remesh_solid`) irons out the reconstruction's surface noise.
    """
    verts, faces = weld(*load_glb_mesh(glb_path))
    # Rebuilt as one clean solid (TRELLIS.2's decoder output has holes and non-manifold seams no decimator can bring
    # down to a low-poly budget), its surface noise smoothed away by `smooth`.
    verts, faces = remesh_solid(verts, faces, smooth=smooth)
    if len(faces) > 8000:  # plenty for fitting silhouettes; the part is decimated further below
        verts, faces = simplify(verts, faces, 8000)
    front, side, back = views
    textured = load_glb_textured(glb_path)
    V, (k, c0, r0, iou), turns, tilt = orient(verts, faces, front, textured)
    print(f"  {os.path.basename(glb_path)}: turned {90 * turns} degrees, tilted {tilt:+d} degrees (outline {iou:.2f})")
    # mesh -> head-drawing front px -> body px -> rig, all uniform scales: one similarity transform.
    def to_rig(P):
        col = c0 + k * P[:, 0]
        row = r0 - k * P[:, 1]
        bcol = head_left_col + col * head_scale
        brow = head_top_row + row * head_scale
        return np.column_stack([(bcol - axis_col) * s, torso_y(brow)])

    scale = k * head_scale * s  # rig units per mesh unit
    upper = V[:, 1] > V[:, 1].min() + 0.55 * (V[:, 1].max() - V[:, 1].min())
    z_mid = float(np.median(V[upper, 2]))  # the skull's centre lands on the rig's z = 0

    def to_local(P):
        xy = to_rig(P)
        return np.column_stack([xy[:, 0], xy[:, 1] - chin_rig_y, (P[:, 2] - z_mid) * scale])

    local = to_local(V)
    # Cut the neck stump: faces below the chin inside a neck-sized column under the skull.
    cen = local[faces].mean(1)
    top = local[:, 1].max()
    skull = local[local[:, 1] > 0.5 * top]
    hx = 0.5 * (skull[:, 0].max() - skull[:, 0].min())
    hz = 0.5 * (skull[:, 2].max() - skull[:, 2].min())
    neck = (cen[:, 1] < 0.02 * top) & ((cen[:, 0] / (0.5 * hx)) ** 2 + (cen[:, 2] / (0.5 * hz)) ** 2 < 1.0)
    faces = faces[~neck]
    used = np.unique(faces)
    remap = -np.ones(len(local), np.int64)
    remap[used] = np.arange(len(used))
    local, faces = cap_holes(local[used], remap[faces])

    # The back view shares the front's scale and top line (one turnaround): mirror about the drawing's centre.
    fcols = np.flatnonzero(front.mask.any(0))
    bcols = np.flatnonzero(back.mask.any(0))
    fc, bc = 0.5 * (fcols[0] + fcols[-1] + 1), 0.5 * (bcols[0] + bcols[-1] + 1)
    ftop, btop = np.flatnonzero(front.mask.any(1))[0], np.flatnonzero(back.mask.any(1))[0]

    def plain_front(P):  # local -> head-drawing front px (the inverse of the chain above)
        col = (P[:, 0] / s + axis_col - head_left_col) / head_scale
        brow = _inverse_torso_y(torso_y, P[:, 1] + chin_rig_y)
        return col, (brow - head_top_row) / head_scale

    def plain_back(P):
        col, row = plain_front(P)
        return bc - (col - fc), row - ftop + btop

    # The outline fit leaves the drawn face off the sculpted one: warp each view's projection onto the mesh's own
    # baked features, rendered in the view's pixels through the same maps (before any warp).
    warp_f = warp_b = None
    if textured is not None:
        tv = _pitch(_yaw(textured[0], turns), tilt)
        textured = (tv, *textured[1:])
        fpx = lambda Q: np.column_stack([c0 + k * Q[:, 0], r0 - k * Q[:, 1]])
        bpx = lambda Q: np.column_stack([bc - (c0 + k * Q[:, 0] - fc), r0 - k * Q[:, 1] - ftop + btop])
        warp_f = align_view(textured, front, fpx, 1, "front")
        warp_b = align_view(textured, back, bpx, -1, "back")

    def front_map(P):
        col, row = plain_front(P)
        return warp_f(col, row) if warp_f else (col, row)

    def back_map(P):
        col, row = plain_back(P)
        return warp_b(col, row) if warp_b else (col, row)

    def front_to_local(r, c):  # texture-view pixel -> local (x, y), the inverse of front_map
        f = head.front.h / head.front.src_h
        col, row = c / f, r / f
        if warp_f:
            col, row = (float(v) for v in warp_f.inverse(col, row))
        return (col * head_scale + head_left_col - axis_col) * s, float(torso_y(head_top_row + row * head_scale)) - chin_rig_y

    head = MeshHead(local, faces, tuple(tex_views or views), front_map, back_map, max_tris=max_tris)
    head.front_to_local = front_to_local
    return head


def _front_depth(head, xs, ys, r):
    """Front-most z of a head's vertices near each grid point `(xs[i], ys[j])` (NaN where none)."""
    out = np.full((len(ys), len(xs)), np.nan)
    v = head.verts
    for j, y in enumerate(ys):
        band = v[np.abs(v[:, 1] - y) < r]
        for i, x in enumerate(xs):
            near = band[np.abs(band[:, 0] - x) < r]
            if len(near):
                out[j, i] = near[:, 2].max()
    return out


def face_labels(part, verts, faces):
    """Palette label of each face: its centroid looked up in the front or back drawing (`part.face_views`)."""
    return _label_at(part, verts[faces].mean(1), part.face_views(verts, faces))


def _label_at(part, P, k):
    """Palette labels of points `P` read from the front (`k` = 0) or back (2) drawing, `k` per point."""
    out = np.zeros(len(P), np.int64)
    for kk in (0, 2):
        sel = k == kk
        if sel.any():
            out[sel] = part.views[kk].label_at(*part.map_view(kk, P[sel]))
    return out


def _edge_table(faces):
    """
    Edges of a triangle mesh: `(uniq, inv, n)` with each undirected edge keyed as `lo * n + hi`, so
    `uniq // n`, `uniq % n` are its vertices and `inv` (faces x 3: v0v1, v1v2, v2v0) maps a face's edges to them.
    """
    n = int(faces.max()) + 1
    nxt = np.roll(faces, -1, axis=1)
    keys = (np.minimum(faces, nxt) * n + np.maximum(faces, nxt)).reshape(-1)
    uniq, inv = np.unique(keys, return_inverse=True)
    return uniq, inv.reshape(-1, 3), n


def densify_by_color(part, verts, faces, edge, max_faces, levels=7, res=4):
    """
    Splits every face whose drawing colors disagree at its edge midpoints (conforming, in the face's own plane, so the
    shape is untouched) until its edges are shorter than `edge`: the color boundaries then run through faces small
    enough to follow their curves. Faces of one color keep their size. Past `max_faces` (a busy pattern), only the
    largest mixed faces are split.
    """
    grid = np.vstack([acs._stencil(res, shrink=0.85), [[1 / 3, 1 / 3, 1 / 3]]])
    n_labels = 1 + max(int(v.labels.max()) for v in (part.front, part.back))
    for _ in range(levels):
        tri = verts[faces]
        cross = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
        lab = _label_at(part, acs._face_points(verts, faces, grid).reshape(-1, 3),
                        np.repeat(np.where(cross[:, 2] >= 0, 0, 2), len(grid))).reshape(len(faces), -1)
        _, minority = acs._mode_rows(lab, n_labels)
        cand = np.flatnonzero((minority > 0) & (acs._edge_lengths(verts, faces).max(1) > edge))
        room = (max_faces - len(faces)) // 6  # a split face becomes 4, and each neighbour gains one
        if not len(cand) or room <= 0:
            break
        mixed = np.zeros(len(faces), bool)
        mixed[cand[np.argsort(-np.linalg.norm(cross[cand], axis=1))[:room]]] = True
        base = verts
        verts, faces = acs._split_edges(verts, faces, np.repeat(mixed[:, None], 3, 1),
                                        lambda ia, ib: 0.5 * (base[ia] + base[ib]))
    return verts, faces


def snap_to_colors(part, verts, faces, labels, sigma=1.0, iters=5, move=0.35):
    """
    Slides every vertex between two colors onto the drawn boundary between them: the zero of the difference of the
    two colors' masks, blurred by `sigma` pixels so it runs smoothly between pixels, read through the vertex's
    view and stepped along the surface (Newton, with finite differences in the vertex's tangent plane). The face
    colors then meet along the drawn curve instead of in steps along the face edges. The slide is capped at `move` x
    the vertex's shortest edge, and the vertices of any face that still turns over slide back;
    vertices where three colors meet stay.
    """
    nl = 1 + max(int(labels.max()), *(int(v.labels.max()) for v in (part.front, part.back)))
    counts = np.zeros((len(verts), nl))
    for k in range(3):
        np.add.at(counts, (faces[:, k], labels), 1.0)
    idx = np.flatnonzero((counts > 0).sum(1) == 2)
    if not len(idx):
        return verts
    pair = np.argsort(-counts[idx], 1)[:, :2]
    tri = verts[faces]
    fn = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    normal = np.zeros_like(verts)
    for k in range(3):
        np.add.at(normal, faces[:, k], fn)
    N = normal[idx] / np.maximum(np.linalg.norm(normal[idx], axis=1, keepdims=True), 1e-12)
    t1 = np.cross(N, np.where(np.abs(N[:, 2:3]) < 0.9, [[0.0, 0.0, 1.0]], [[1.0, 0.0, 0.0]]))
    t1 /= np.maximum(np.linalg.norm(t1, axis=1, keepdims=True), 1e-12)
    t2 = np.cross(N, t1)
    el = acs._edge_lengths(verts, faces)
    shortest = np.full(len(verts), np.inf)
    for k in range(3):
        np.minimum.at(shortest, faces[:, k], np.minimum(el[:, k], el[:, (k + 2) % 3]))
    cap, eps = move * shortest[idx], 0.15 * shortest[idx]
    view = np.where(N[:, 2] >= 0, 0, 2)
    masks = {k: np.stack([ndimage.gaussian_filter((part.views[k].labels == l).astype(np.float32), sigma)
                          for l in range(nl)]) for k in (0, 2)}
    rows = np.arange(len(idx))

    def field(P):  # first color's blurred mask minus the second's: positive on the first color's side
        out = np.zeros(len(P))
        for k in (0, 2):
            sel = view == k
            if sel.any():
                r, c = part.map_view(k, P[sel])
                coords = [np.asarray(r, float) - 0.5, np.asarray(c, float) - 0.5]
                m = np.array([ndimage.map_coordinates(masks[k][l], coords, order=1, mode="nearest") for l in range(nl)])
                out[sel] = m[pair[sel, 0], rows[:sel.sum()]] - m[pair[sel, 1], rows[:sel.sum()]]
        return out

    P0 = verts[idx].copy()
    P = P0.copy()
    for _ in range(iters):
        d0 = field(P)
        g1 = (field(P + eps[:, None] * t1) - d0) / eps
        g2 = (field(P + eps[:, None] * t2) - d0) / eps
        gg = g1 * g1 + g2 * g2
        s = np.where(gg > 1e-12, -d0 / np.maximum(gg, 1e-12), 0.0)
        P = P + s[:, None] * (g1[:, None] * t1 + g2[:, None] * t2)
        moved = P - P0  # the cap holds for the whole slide, not per step
        P = P0 + moved * np.minimum(1.0, cap / np.maximum(np.linalg.norm(moved, axis=1), 1e-12))[:, None]
    # Neighbours slide at once: the vertices of a face that turned over go back halfway, then home.
    out = verts.copy()
    out[idx] = P
    before = _face_normals(verts, faces)
    real = np.linalg.norm(before, axis=1) > 0.5  # faces that had an orientation to lose
    slot = np.full(len(verts), -1)
    slot[idx] = np.arange(len(idx))
    for frac in (0.5, 0.0, 0.0, 0.0):  # sending one home can turn a neighbour's face over: repeat
        flipped = real & ((_face_normals(out, faces) * before).sum(1) < 0.2)
        if not flipped.any():
            break
        v = np.unique(faces[flipped])
        v = v[slot[v] >= 0]
        out[v] = P0[slot[v]] + frac * (out[v] - P0[slot[v]])
    return out


def _face_normals(verts, faces):
    n = np.cross(verts[faces[:, 1]] - verts[faces[:, 0]], verts[faces[:, 2]] - verts[faces[:, 0]])
    return n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)


def face_colors(part, verts, faces, samples=5):
    """
    One palette label per face: the most common color on a grid of points over the face (the centroid breaks ties),
    read from the front or back drawing by `part.face_views`.
    """
    grid = np.vstack([acs._stencil(samples, shrink=0.8), [[1 / 3, 1 / 3, 1 / 3]]])
    which = part.face_views(verts, faces)
    lab = _label_at(part, acs._face_points(verts, faces, grid).reshape(-1, 3), np.repeat(which, len(grid)))
    lab = lab.reshape(len(faces), -1)
    n_labels = 1 + int(lab.max())
    votes = np.stack([(lab == c).sum(1) for c in range(n_labels)], 1).astype(float)
    votes[np.arange(len(faces)), lab[:, -1]] += 0.5
    return votes.argmax(1)


def _face_adjacency(verts, faces):
    """Pairs of faces sharing an edge, with that edge's length: `(face a, face b, length)`."""
    uniq, inv, n = _edge_table(faces)
    flat = inv.reshape(-1)
    order = np.argsort(flat, kind="stable")
    e = flat[order]
    pair = e[1:] == e[:-1]
    k = uniq[e[1:][pair]]
    return order[:-1][pair] // 3, order[1:][pair] // 3, np.linalg.norm(verts[k // n] - verts[k % n], axis=1)


def inherit_labels(labels, known, adjacency):
    """
    Colors the faces that are not `known` from the ones that are: round by round, each takes the color it shares
    the most edge length with among its known neighbours, so the colors spread inward from the reliable faces.
    Faces cut off from every known one keep their own.
    """
    labels, known = labels.copy(), known.copy()
    fa, fb, w = adjacency
    for _ in range(len(labels)):
        votes = np.zeros((len(labels), int(labels.max()) + 1))
        for x, y in ((fa, fb), (fb, fa)):
            m = known[y] & ~known[x]
            np.add.at(votes, (x[m], labels[y[m]]), w[m])
        has = votes.sum(1) > 0
        if not has.any():
            break
        labels[has] = votes[has].argmax(1)
        known |= has
    return labels


def merge_specks(labels, adjacency, min_faces=2):
    """
    Cleans a per-face coloring into flat regions: every connected patch of one color with at most `min_faces`
    faces takes the color it shares the most edge length with, so lone triangles (a sample landing off a thin
    stripe) don't speckle a surface while strips of a thin feature, which stay connected, survive. Repeats a few
    times for patches that only become small once their neighbours merge.
    """
    from scipy.sparse import coo_matrix
    from scipy.sparse.csgraph import connected_components

    labels = labels.copy()
    fa, fb, w = adjacency
    for _ in range(4):
        same = labels[fa] == labels[fb]
        _, comp = connected_components(coo_matrix((np.ones(same.sum()), (fa[same], fb[same])), shape=(len(labels),) * 2),
                                       directed=False)
        small = np.bincount(comp)[comp] <= min_faces
        if not small.any():
            break
        votes = np.zeros((comp.max() + 1, int(labels.max()) + 1))
        for x, y in ((fa, fb), (fb, fa)):
            m = ~same & small[x]
            np.add.at(votes, (comp[x[m]], labels[y[m]]), w[m])
        moves = small & (votes[comp].max(1) > 0)
        labels[moves] = votes[comp[moves]].argmax(1)
    return labels


def color_faces(part, max_tris=7000, edge=0.02, min_facing=0.35):
    """
    A part (`MeshHead`, `FlatPart`) with one flat palette label per face, its color boundaries following the
    drawing's curves:
      1. faces the drawing colors split until they are `edge` x the part's size small, within 1.25 x the budget
         (`densify_by_color`),
      2. each face takes its most common color (`face_colors`), except on faces seen too obliquely, which the
         orthographic drawings squeeze into a sliver of pixels (`min_facing` = smallest cosine between normal and
         view): those inherit their neighbours' colors (`inherit_labels`); lone faces merge away (`merge_specks`),
      3. the vertices between two colors slide onto the drawn boundary (`snap_to_colors`), smoothing its steps,
      4. the regions of one color are simplified back toward `max_tris` faces, boundaries kept
         (`simplify_within_colors`).
    Returns `(verts, faces, labels)`.
    """
    verts, faces = part.mesh()
    verts, faces = densify_by_color(part, np.asarray(verts, float), np.asarray(faces, np.int64), edge * part.L,
                                    int(1.25 * max_tris))  # boundaries can't be simplified away: they set the count
    tri = verts[faces]
    n = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    facing = np.abs(n[:, 2]) / np.maximum(np.linalg.norm(n, axis=1), 1e-12)
    adjacency = _face_adjacency(verts, faces)
    labels = merge_specks(inherit_labels(face_colors(part, verts, faces), facing >= min_facing, adjacency), adjacency)
    verts = snap_to_colors(part, verts, faces, labels)
    return simplify_within_colors(verts, faces, labels, max_tris, adjacency)


def simplify_within_colors(verts, faces, labels, max_tris, adjacency=None):
    """
    Decimates a mesh colored per face inside its regions of one color only: every connected patch is simplified with
    its border kept, so the color boundaries stay exactly where they are while the faces between them grow large
    again, the budget `max_tris` shared out by area (a patch keeps at least what its border needs). Returns
    `(verts, faces, labels)`.
    """
    from scipy.sparse import coo_matrix
    from scipy.sparse.csgraph import connected_components

    fa, fb, _ = adjacency if adjacency is not None else _face_adjacency(verts, faces)
    same = labels[fa] == labels[fb]
    _, patch = connected_components(coo_matrix((np.ones(same.sum()), (fa[same], fb[same])), shape=(len(faces),) * 2),
                                    directed=False)
    tri = verts[faces]
    area = 0.5 * np.linalg.norm(np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0]), axis=1)
    share = np.bincount(patch, area) / max(area.sum(), 1e-12)
    out_v, out_f, out_l, offset = [], [], [], 0
    for p in range(patch.max() + 1):
        sel = np.flatnonzero(patch == p)
        used = np.unique(faces[sel])
        remap = np.zeros(len(verts), np.int64)
        remap[used] = np.arange(len(used))
        v, f = verts[used], remap[faces[sel]]
        target = max(2, int(round(share[p] * max_tris)))
        if len(f) > 2 * target:
            v, f = simplify(v, f, target, preserve_border=True)
        out_v.append(v)
        out_f.append(np.asarray(f, np.int64) + offset)
        out_l.append(np.full(len(f), labels[sel[0]]))
        offset += len(v)
    v, f, lab = np.vstack(out_v), np.vstack(out_f), np.concatenate(out_l)
    key = np.round(v / 1e-7).astype(np.int64)  # kept borders are bit-identical: weld the patches back together
    _, first, inverse = np.unique(key, axis=0, return_index=True, return_inverse=True)
    f = inverse.reshape(-1)[f]
    ok = (f[:, 0] != f[:, 1]) & (f[:, 1] != f[:, 2]) & (f[:, 0] != f[:, 2])
    return v[first], f[ok], lab[ok]


class FlatPart:
    """
    Any part colored from drawings (its `views`, `map_view`, `face_views`: the smooth body, wraps, solids, sphere
    heads) over a given mesh, with the interface `color_faces` needs (`mesh`, `front`, `back`, `L`), so every part
    can take one flat palette color per face. Wraps list their views as (front, back); here back is index 2.
    """

    def __init__(self, part, verts, faces, size=None):
        self.part = part
        self._v, self._f = np.asarray(verts, float), np.asarray(faces, np.int64)
        pv = list(part.views)
        self._two = len(pv) == 2
        self.front, self.back = pv[0], pv[-1]
        self.views = [self.front, None, self.back]
        self.L = float(size or np.ptp(self._v, axis=0).max())

    def mesh(self):
        return self._v, self._f

    def map_view(self, k, P):
        return self.part.map_view(1 if (self._two and k == 2) else k, P)

    def face_views(self, verts, faces):
        w = np.asarray(self.part.face_views(verts, faces))
        return np.where(w == 1, 2, w) if self._two else w


def split_hair(with_hair, bald, hair_labels, res=200, margin=0.025, smooth=1.0, max_tris=3000):
    """
    The hair of `with_hair` (a `MeshHead` of the head drawn with hair) as its own solid over `bald` (the same head
    reconstructed without hair): both are voxelized in one grid, after matching them in depth on the face (which
    hair never covers), and the hair is what the head with hair has beyond the bald head grown by `margin` x its
    height (the two reconstructions' skulls never agree exactly). Small crumbs are dropped and the rest is
    polygonized as chunky solid locks, colored from the hair drawing (hair colors only). Returns
    `(MeshHead, face labels)` or None. Picking the drawn hair's triangles instead gave scattered shards when hair
    and skin share a hue.
    """
    L, A = bald.L, bald.A
    xs = np.linspace(-0.4 * A, 0.4 * A, 7)
    ys = np.linspace(0.2 * L, 0.6 * L, 7)
    fb, fh = _front_depth(bald, xs, ys, 0.04 * L), _front_depth(with_hair, xs, ys, 0.04 * L)
    ok = ~np.isnan(fb) & ~np.isnan(fh)
    dz = float(np.median((fb - fh)[ok])) if ok.any() else 0.0
    Vh = with_hair.verts + [0.0, 0.0, dz]
    lo = np.minimum(Vh.min(0), bald.verts.min(0))
    hi = np.maximum(Vh.max(0), bald.verts.max(0))
    origin, step, shape = _grid(lo, hi, res)
    occ_h = solid_voxels(Vh, with_hair.faces, origin, step, shape)
    occ_b = solid_voxels(bald.verts, bald.faces, origin, step, shape)
    grow = max(1, int(round(margin * L / step)))
    hair = occ_h & ~ndimage.binary_dilation(occ_b, iterations=grow)
    zc = float(np.median(bald.verts[:, 2]))
    # The two reconstructions' ears never coincide either, which leaves a shell around each bald ear that would read
    # as a hair-colored ear. Close to the bald head, drop what the hair drawing paints in a color other than hair
    # (front view before the skull's middle, back view behind it; off the drawing counts as unknown and stays).
    near = ndimage.distance_transform_edt(~occ_b) * step < 0.08 * L
    idx = np.argwhere(hair & near)
    if len(idx):
        P = origin + step * idx
        drawn = np.full(len(P), -1)
        for k in (0, 2):  # off the drawing (cut) counts as unknown
            sel = (P[:, 2] >= zc) if k == 0 else (P[:, 2] < zc)
            if sel.any():
                drawn[sel] = with_hair.views[k].label_at(*with_hair.map_view(k, P[sel] - [0.0, 0.0, dz]), cut=True)
        hair[tuple(idx[(drawn >= 0) & ~np.isin(drawn, list(hair_labels))].T)] = False
    hair = ndimage.binary_opening(hair, iterations=1)  # slivers where the two surfaces merely disagree
    comp, n = ndimage.label(hair)
    if not n:
        return None
    sizes = ndimage.sum(hair, comp, range(1, n + 1))
    hair = np.isin(comp, 1 + np.flatnonzero(sizes >= 0.02 * sizes.max()))
    # Never on the face (where the two reconstructions' cheeks and brows merely disagree): the front of the head
    # below the brow and inside the skull's width. The scalp under the locks is painted in the hair's colors.
    gx, gy, gz = (origin[i] + step * np.arange(shape[i]) for i in range(3))
    face = ((np.abs(gx - bald.cx) < 0.75 * A)[:, None, None] & (gy < 0.72 * L)[None, :, None]
            & (gz > zc)[None, None, :])
    hair &= ~face
    v, f = polygonize(hair, origin, step, smooth)
    if len(f) < 12:
        return None
    v, f = simplify(v, f, max_tris)
    labels = face_labels(with_hair, v - [0.0, 0.0, dz], f)
    hl = [l for l in labels if l in hair_labels]
    dominant = int(np.bincount(hl).argmax()) if hl else int(sorted(hair_labels)[0])
    labels = np.where(np.isin(labels, list(hair_labels)), labels, dominant)
    part = MeshHead(v, f, tuple(with_hair.views), with_hair._fm, with_hair._bm, max_tris=None)
    return part, labels


def paint_scalp(with_hair, verts, faces, labels, hair_labels, bald):
    """
    Labels of the bald head's faces (`verts`, `faces`, `labels`) with the scalp under the hair painted in the hair's
    colors: faces the hair drawing (`with_hair`'s views, through its own aligned mapping) paints in a hair color,
    away from the face and throat, take that color. Gaps between locks then read as hair, not as bare scalp.
    Testing coverage by the hair's own volume instead misses hair a reconstruction leaves flat on the skull (only
    paint) and catches ear tips under hanging locks.
    """
    tri = verts[faces]
    cen = tri.mean(1)
    n = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    nz = n[:, 2] / np.maximum(np.linalg.norm(n, axis=1), 1e-12)
    L, A = bald.L, bald.A
    face = (np.abs(cen[:, 0]) < 0.7 * A) & (cen[:, 1] < 0.78 * L) & (nz > 0.3)
    throat = (np.abs(cen[:, 0]) < 0.6 * A) & (cen[:, 1] < 0.12 * L) & (cen[:, 2] > 0.0)
    sel = np.flatnonzero(~face & ~throat)
    drawn = _label_at(with_hair, cen[sel], np.where(nz[sel] >= 0, 0, 2))
    hit = np.isin(drawn, list(hair_labels))
    out = labels.copy()
    out[sel[hit]] = drawn[hit]
    return out


def combined(*parts):
    """One `MeshHead` over several parts' geometry (e.g. head and hair), for fitting accessories around them."""
    verts, faces = [], []
    for p in parts:
        faces.append(p.faces + sum(len(v) for v in verts))
        verts.append(p.verts)
    first = parts[0]
    return MeshHead(np.vstack(verts), np.vstack(faces), tuple(first.views), first._fm, first._bm, max_tris=None)


def _inverse_torso_y(torso_y, y):
    """Body row of rig height `y` for the (affine) torso map."""
    y0, y1 = float(torso_y(0.0)), float(torso_y(1000.0))
    return (np.asarray(y, float) - y0) / (y1 - y0) * 1000.0
