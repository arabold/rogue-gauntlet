"""
Sculpted pieces: organic forms that read better reconstructed than scripted (swept or spiky hair, dreadlocks),
harvested once from image-to-3D builds into a small library (`LIBRARY`, one `.npz` each) and reused: recolored by
material slot and refitted to any head.

Every sculpt keeps the head it was made on as a fitted `heads.Head` (`skull`, for its centre and proportions) and,
for hair, each point's height over that head's real surface (measured by a ray from the centre). Hair is refitted by
moving each point to the same direction (in each head's proportions) and the same height over the new head
(`refit`), so it hugs the new skull the way it hugged the old one. The old head's scalp was painted in the hair's
color; that area comes along (`scalp`, directions in the head's proportions) and is painted on the new cranium
(`paint_scalp`), so the gaps between locks show hair, never a second, scripted cap.

New hairdos come from the monster pipeline: build a throwaway character whose head part is `image3d` with the
wanted `hair` (separate hair mode), then `harvest` its `<Name>_HeadDetails` mesh.

    python accessories/catalog.py sculpts -o sculpts.png --yaws 0,-40,-90,180
"""

import glob
import io
import json
import os
from pathlib import Path

import numpy as np
from PIL import Image

from accessories import heads, kit

LIBRARY = Path(__file__).resolve().parents[2] / "sculpts"


def save(name, piece, kind, skull, source="", height=None, scalp=None, **arrays):
    LIBRARY.mkdir(exist_ok=True)
    meta = dict(kind=kind, skull=skull.params, source=source)
    extra = {k: np.asarray(v, np.float32) for k, v in (("height", height), ("scalp", scalp), *arrays.items())
             if v is not None}
    np.savez_compressed(LIBRARY / f"{name}.npz", verts=piece.verts.astype(np.float32),
                        faces=piece.faces.astype(np.int32), slots=piece.slots.astype(str), meta=json.dumps(meta),
                        **extra)


def load(name):
    """`(piece, skull, meta)` of a library sculpt; for hair, `meta["height"]` holds its heights over its old head
    and `meta["scalp"]` the painted scalp's directions."""
    d = np.load(LIBRARY / f"{name}.npz")
    meta = json.loads(str(d["meta"]))
    for key in d.files:
        if key not in ("verts", "faces", "slots", "meta"):
            meta[key] = d[key].astype(float)
    piece = kit.Piece(d["verts"].astype(float), d["faces"].astype(np.int64), d["slots"].astype(object))
    return piece, heads.Head(**meta["skull"]), meta


# A sculpted head's material slots and the drawing colors each one stands for (the drawing's palette is matched to
# these); recipes color the slots.
HEAD_SLOTS = {"skin": (132, 166, 68), "skin_dark": (78, 100, 58), "accent": (128, 98, 74), "tusk": (225, 206, 160),
              "socket": (242, 242, 226)}


def classify(rgb, dark=0.45):
    """A drawn color's slot by hue, not by RGB distance (olive greens sit nearer brown than bright green does):
    whites and ivories `socket`, browns `accent`, reds (eyes) `skin_dark`, everything else skin, `skin_dark`
    when its value is under `dark`."""
    import colorsys
    h, sat, v = colorsys.rgb_to_hsv(*(np.asarray(rgb, float) / 255.0))
    hue = h * 360
    if sat < 0.3 and v > 0.75:
        return "socket"
    if 8 <= hue < 50 and sat > 0.15:
        return "accent"
    if (hue < 8 or hue > 330) and sat > 0.4:
        return "skin_dark"
    return "skin_dark" if v < dark else "skin"


def make_head(name, glb, drawing, tris=2600, smooth=1.5, slots=HEAD_SLOTS, source="", dark=0.45):
    """
    A sculpted head for the library from an image-to-3D reconstruction (`glb`, TRELLIS) of a flat-colored
    `drawing` (front view left, side view right): rebuilt as one clean solid smoothed by `smooth` voxels, turned to
    face +z, decimated to `tris`, and colored by projecting the drawing onto it (front faces from the front view,
    side faces from the side view, the undrawn back in plain skin), its palette matched to `slots`, speckles
    cleaned. Stored normalized: top at y = 0, height 1, skull centred on x = z = 0, with the eyes' centres (the
    `socket` faces) for placing kit eyes on it. Shape from the model, colors and details from the kit.
    """
    import monster_image3d as m3
    import monster_mesh as mm

    verts, faces = m3.weld(*m3.load_glb_mesh(glb))
    verts, faces = m3.remesh_solid(verts, faces, smooth=smooth)
    verts, faces = m3.simplify(verts, faces, 8000)
    front, side = mm.split_views(Image.open(drawing).convert("RGB"), 2, max_h=10 ** 6)
    V, _, _, _ = m3.orient(verts, faces, front, m3.load_glb_textured(glb))
    V, F = m3.simplify(V, faces, tris)
    tri = V[F]
    cent = tri.mean(1)
    n = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-12
    kf, cf, rf, _ = m3._fit_view(V[:, :2], F, front.mask, scales=[1.0])
    ks, cs, rs, _ = m3._fit_view(np.column_stack([V[:, 2], V[:, 1]]), F, side.mask)

    def sample(view, col, row):
        c = np.clip(np.rint(col).astype(int), 0, view.w - 1)
        r = np.clip(np.rint(row).astype(int), 0, view.h - 1)
        return view.rgb[r, c].astype(float), view.mask[r, c] > 0

    rgb_f, ok_f = sample(front, cf + kf * cent[:, 0], rf - kf * cent[:, 1])
    rgb_s, ok_s = sample(side, cs + ks * cent[:, 2], rs - ks * cent[:, 1])
    sideways = np.abs(n[:, 0]) > 1.2 * np.abs(n[:, 2])
    rgb = np.where((sideways & ok_s)[:, None], rgb_s, np.where(ok_f[:, None], rgb_f, rgb_s))
    names_ = list(slots)
    lab = np.array([names_.index(classify(c, dark)) for c in rgb])
    lab[(n[:, 2] < -0.3) & ~sideways] = names_.index("skin")  # the back, never drawn
    nb = [[] for _ in range(len(F))]
    edges = {}
    for f, t in enumerate(F):
        for a, b in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
            edges.setdefault((min(a, b), max(a, b)), []).append(f)
    for fs in edges.values():
        for x in fs:
            nb[x] += [y for y in fs if y != x]
    for _ in range(3):  # a face unlike all its neighbours takes theirs
        new = lab.copy()
        for f in range(len(F)):
            ns = lab[nb[f]]
            if len(ns) and not (ns == lab[f]).any():
                new[f] = np.bincount(ns).argmax()
        lab = new
    for _ in range(2):  # then a majority vote smooths the ragged edges between colors
        lab = np.array([np.bincount(np.r_[lab[f], lab[f], lab[nb[f]]]).argmax() for f in range(len(F))])
    top, height = V[:, 1].max(), np.ptp(V[:, 1])
    upper = V[:, 1] > top - 0.5 * height
    c = np.array([np.median(V[upper, 0]), top, np.median(V[upper, 2])])
    V = (V - c) / height
    piece = kit.Piece(V, F, np.array([names_[i] for i in lab], object))
    sock = piece.slots == "socket"
    eye_c = [V[F[sock & (cent[:, 0] * sgn > c[0] * sgn)]].reshape(-1, 3).mean(0) if (sock & (
        cent[:, 0] * sgn > c[0] * sgn)).any() else np.full(3, np.nan) for sgn in (-1, 1)]
    skull = heads.Head.fit(V[V[:, 1] > -0.55])
    save(name, piece, "head", skull, source=source, eyes=np.array(eye_c))
    return piece


def head(name, top, height, center=(0.0, 0.0)):
    """A library head scaled to `height` rig units with its top at `top`, centred on (x, z) = `center`: `(piece,
    fitted Head, meta)` (the Head is the host crowns, helmets and kit features fit)."""
    piece, _, meta = load(name)
    off = np.array([center[0], top, center[1]])
    V = piece.verts * height + off
    placed = kit.Piece(V, piece.faces, piece.slots)
    host = _skull_host(V, top, height)
    if "eyes" in meta:
        meta["eyes"] = meta["eyes"] * height + off
    return placed, host, meta


def _skull_host(V, top, height):
    """The kit `Head` hugging a sculpted head's cranium (what crowns and helmets fit around, and kit features are
    built on): its half height 0.38 of the head's, its width and depth measured across the band just above its
    middle, where a crown sits."""
    Hh = 0.38 * height
    cy = top - Hh
    band = V[(V[:, 1] > cy) & (V[:, 1] < cy + 0.6 * Hh)]
    lo, hi = band.min(0), band.max(0)
    return heads.Head(width=hi[0] - lo[0], height=2 * Hh, depth=hi[2] - lo[2],
                      center=(0.5 * (lo[0] + hi[0]), cy, 0.5 * (lo[2] + hi[2])))


def onto(piece, host, surface):
    """`piece`, built on the kit `host` (a feature conformed to its front), moved along z onto `surface`: each point
    keeps its height over the host's front, now over the sculpt's."""
    V = piece.verts.copy()
    zh = host.surface(*host.front_angles(V[:, 0], V[:, 1]))[:, 2]
    start = np.column_stack([V[:, :2], np.full(len(V), surface.verts[:, 2].max() + 1.0)])
    d = kit.ray_distance(start, np.tile([0.0, 0.0, -1.0], (len(V), 1)), surface.verts, surface.faces)
    zs = start[:, 2] - d
    V[:, 2] += np.where(np.isfinite(zs), zs - zh, 0.0)
    return kit.Piece(V, piece.faces, piece.slots)


def lip_top(surface, x, below):
    """The height of the top of the `accent`-colored lip on a sculpted head near `x` (the highest lip face centre
    below `below`, so the nose isn't counted), or None."""
    tri = surface.verts[surface.faces]
    c = tri.mean(1)
    lip = (surface.slots == "accent") & (c[:, 1] < below)
    if not lip.any():
        return None
    near = lip & (np.abs(c[:, 0] - x) < 0.08)
    if not near.any():  # past the lip's corner: the corner itself
        near = lip & (np.abs(c[:, 0] - x) <= np.abs(c[lip, 0] - x).min() + 0.03)
    return float(c[near, 1].max())


def attach(piece, host, surface, axis="z", embed=0.01, to_y=None):
    """
    `piece`, built on the kit `host` (ears, tusks, a nose), moved rigidly onto the sculpted `surface`, each side
    (left and right of the host's centre) on its own: its anchor (the point nearest the head along `axis`, "x" for
    ears, the lowest point for "z" features like tusks) keeps its height over the host's surface, now over the
    sculpt's, sunk `embed` in so no seam shows. Rigid: the part keeps its shape.
    """
    host_mesh = host.mesh()
    out = []
    for side in (piece.verts[:, 0] >= host.cx, piece.verts[:, 0] < host.cx):
        keep = side[piece.faces].all(1)
        if not keep.any():
            continue
        used = np.unique(piece.faces[keep])
        remap = -np.ones(len(piece.verts), np.int64)
        remap[used] = np.arange(len(used))
        V, F, S = piece.verts[used].copy(), remap[piece.faces[keep]], piece.slots[keep]
        if axis == "x":
            sgn = 1.0 if V[:, 0].mean() >= host.cx else -1.0
            anchor = V[np.argmin(np.abs(V[:, 0] - host.cx))]
            d = np.array([-sgn, 0.0, 0.0])
        else:
            anchor = V[np.argmin(V[:, 1])]
            d = np.array([0.0, 0.0, -1.0])
        # its height over the host where it was built ...
        start = anchor - d * 2.0
        hit_h = kit.ray_distance(start[None], d[None], host_mesh.verts, host_mesh.faces)[0]
        lift = (2.0 - hit_h) if np.isfinite(hit_h) else 0.0
        target_y = to_y(anchor[0]) if callable(to_y) else to_y
        if target_y is not None:  # ... moved to its place (a tusk down onto the lip) ...
            V[:, 1] += target_y - anchor[1]
            anchor = anchor + [0.0, target_y - anchor[1], 0.0]
        start = anchor - d * 2.0  # ... and kept over the sculpt's surface there
        hit_s = kit.ray_distance(start[None], d[None], surface.verts, surface.faces)[0]
        if np.isfinite(hit_s):
            V += ((2.0 - hit_s) - lift - embed) * -d
        out.append(kit.Piece(V, F, S))
    return kit.Piece.merge(*out)


def earrings(surface, host, side="right", count=3, size=0.04, slot="gold", spacing=0.22):
    """Rings pierced through the lower edge of a sculpted ear (`side` "left", "right" or "both"): the ear is what
    reaches beyond the skull; the rings hang from its lower edge, from near the root outward, each in the plane
    across the edge."""
    out = []
    V = np.unique(surface.verts, axis=0)
    for sgn in {"left": (1,), "right": (-1,), "both": (-1, 1)}[side]:
        ear = V[(V[:, 0] - host.cx) * sgn > 1.05 * host.A]
        if len(ear) < 8:
            continue
        tip = ear[np.argmax((ear[:, 0] - host.cx) * sgn)]
        near = ear[(ear[:, 0] - host.cx) * sgn < 1.2 * host.A]
        root = near[np.argmin(near[:, 1])] if len(near) else ear[np.argmin(ear[:, 1])]
        _, _, vt = np.linalg.svd(ear - ear.mean(0))
        normal = vt[2]
        edge = (tip - root) / np.linalg.norm(tip - root)
        away = np.cross(edge, normal)
        if away[1] > 0:
            away = -away  # hanging down off the edge
        t = 2 * np.pi * np.arange(12) / 12
        for k in range(count):
            at = root + (0.12 + spacing * k) * (tip - root)
            c = at + 0.55 * size * away
            ring = c + size * (np.outer(np.cos(t), away) + np.outer(np.sin(t), normal))
            out.append(kit.sweep(ring, kit.ring_profile(0.17 * size, 0.17 * size, 6), slot, closed=True,
                                 up=tuple(edge)))
    return kit.Piece.merge(*out)


def names(kind=None):
    return sorted(p.stem for p in LIBRARY.glob("*.npz")
                  if kind is None or json.loads(str(np.load(p)["meta"]))["kind"] == kind)


def refit(P, skull, head, thickness=1.0, height=None):
    """Points around `skull` moved to sit the same way around `head`: same direction in each head's proportions,
    same height over the skin (`height`, else measured on `skull`; scaled with the head's size, times
    `thickness`)."""
    d = np.asarray(P, float) - skull.centre
    r = np.linalg.norm(d, axis=1)
    dirs = d / np.maximum(r, 1e-12)[:, None]
    if height is None:
        height = r - skull.radius(dirs)
    u = _directions(P, skull)
    n = u * head.proportions(u)
    n /= np.linalg.norm(n, axis=1, keepdims=True)
    k = thickness * head.size / skull.size
    return head.centre + n * (head.radius(n) + k * height)[:, None]


def hair(head, name, thickness=1.0, slot="hair"):
    """The library hair `name` refitted to `head`, its main slot renamed to `slot`."""
    piece, skull, meta = load(name)
    out = kit.Piece(refit(piece.verts, skull, head, thickness, meta.get("height")), piece.faces, piece.slots)
    return out.recolored({"hair": slot})


def _directions(P, skull):
    """Unit directions of points from a head's centre, in its proportions (so they carry over between heads)."""
    d = np.asarray(P, float) - skull.centre
    d = d / skull.proportions(d)
    return d / np.maximum(np.linalg.norm(d, axis=1, keepdims=True), 1e-12)


def paint_scalp(cranium, head, name, hair, slot="hair", spread=0.13, reach=0.35):
    """
    `cranium` (of `head`) with the library hair `name`'s scalp area painted `slot`: faces whose direction lies
    within `spread` (radians, about) of a painted face of the old head, and within twice that where the refitted
    `hair` covers them (along their normal, within `reach` half widths), which closes gaps between locks without
    spilling onto the cheeks.
    """
    from scipy.spatial import cKDTree

    _, _, meta = load(name)
    if "scalp" not in meta:
        return cranium
    tri = cranium.verts[cranium.faces]
    d = cKDTree(meta["scalp"]).query(_directions(tri.mean(1), head))[0]
    paint = d < spread
    edge = np.flatnonzero(~paint & (d < 2 * spread))
    if len(edge):
        n = np.cross(tri[edge, 1] - tri[edge, 0], tri[edge, 2] - tri[edge, 0])
        n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)
        hit = kit.ray_distance(tri[edge].mean(1), n, hair.verts, hair.faces)
        paint[edge[np.nan_to_num(hit, nan=np.inf) < reach * head.A]] = True
    slots = cranium.slots.copy()
    slots[paint] = slot
    return kit.Piece(cranium.verts, cranium.faces, slots)


# ---------------------------------------------------------------------------
# Harvesting from built GLBs
# ---------------------------------------------------------------------------
def _node_worlds(g):
    parent = {c: i for i, nd in enumerate(g["nodes"]) for c in nd.get("children", [])}
    W = [None] * len(g["nodes"])

    def local(nd):
        if "matrix" in nd:
            return np.array(nd["matrix"], float).reshape(4, 4).T
        x, y, z, w = nd.get("rotation", [0, 0, 0, 1])
        T = np.eye(4)
        T[:3, :3] = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                              [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                              [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]]) @ np.diag(
            nd.get("scale", [1, 1, 1]))
        T[:3, 3] = nd.get("translation", [0, 0, 0])
        return T

    def world(i):
        if W[i] is None:
            W[i] = local(g["nodes"][i]) if i not in parent else world(parent[i]) @ local(g["nodes"][i])
        return W[i]

    return [world(i) for i in range(len(g["nodes"]))]


def glb_pieces(path):
    """Every mesh of a (rigged) GLB in its bind pose: `{name: (verts, faces, face_rgb)}`, face colors sampled from the
    texture at each face's UV centroid (flat-colored builds have one color per face)."""
    import monster_mesh as mm

    g, binary, acc = mm.read_glb(path)
    tex = None
    if g.get("images"):
        bv = g["bufferViews"][g["images"][0]["bufferView"]]
        tex = np.asarray(Image.open(io.BytesIO(binary[bv.get("byteOffset", 0):bv.get("byteOffset", 0) + bv["byteLength"]])).convert("RGB"))
    else:
        found = glob.glob(os.path.splitext(path)[0] + "_*texture.png")
        tex = np.asarray(Image.open(found[0]).convert("RGB")) if found else None
    W = _node_worlds(g)
    out = {}
    for i, nd in enumerate(g["nodes"]):
        if "mesh" not in nd:
            continue
        m = g["meshes"][nd["mesh"]]
        pr = m["primitives"][0]
        P = acc(pr["attributes"]["POSITION"]).astype(float)
        F = acc(pr["indices"]).reshape(-1, 3).astype(np.int64)
        Ph = np.column_stack([P, np.ones(len(P))])
        if "skin" in nd:  # bind pose: sum of weight x joint world x inverse bind
            sk = g["skins"][nd["skin"]]
            ibm = acc(sk["inverseBindMatrices"]).reshape(-1, 4, 4).transpose(0, 2, 1).astype(float)
            J = acc(pr["attributes"]["JOINTS_0"]).astype(int)
            wa = pr["attributes"]["WEIGHTS_0"]
            Wt = acc(wa).astype(float) / {5121: 255.0, 5123: 65535.0}.get(g["accessors"][wa]["componentType"], 1.0)
            M = np.stack([W[j] @ ibm[k] for k, j in enumerate(sk["joints"])])
            P = sum(Wt[:, c:c + 1] * np.einsum("nij,nj->ni", M[J[:, c]], Ph) for c in range(4))[:, :3]
        else:
            P = (W[i] @ Ph.T).T[:, :3]
        col = np.full((len(F), 3), 180, np.uint8)
        if tex is not None and "TEXCOORD_0" in pr["attributes"]:
            c = acc(pr["attributes"]["TEXCOORD_0"]).astype(float)[F].mean(1)
            h, w = tex.shape[:2]
            col = tex[np.clip((c[:, 1] * h).astype(int), 0, h - 1), np.clip((c[:, 0] * w).astype(int), 0, w - 1)]
        out[m.get("name") or nd.get("name")] = (P, F, col)
    return out


def harvest(glb, mesh, skull_mesh, slots, name, kind, min_faces=40, reach=None):
    """
    Cuts a sculpt out of a built GLB and saves it to the library: the faces of `mesh` whose color is a key of
    `slots` (an RGB tuple -> slot name; other colors are dropped, e.g. a crown merged into the hair), welded, minus
    connected bits smaller than `min_faces`, with the skull fitted to `skull_mesh` (the head it was made on).
    `reach` drops faces reaching further sideways than that many skull half widths (leftovers of the old head's
    ears) and closes the cuts. A hair keeps the old head's biggest patch painted in its color as its scalp. A hair's
    height over `skull_mesh` is measured per point, so the fitted skull only has to get the head's
    centre and proportions right.
    """
    import monster_image3d as m3
    from scipy.sparse import coo_matrix
    from scipy.sparse.csgraph import connected_components

    pieces = glb_pieces(glb)
    P, F, col = pieces[mesh]
    skull = heads.Head.fit(pieces[skull_mesh][0])
    keys = {tuple(int(v) for v in k): s for k, s in slots.items()}
    slot = np.array([keys.get(tuple(int(v) for v in c)) for c in col], object)
    _, first, inv = np.unique(np.round(P / 1e-6).astype(np.int64), axis=0, return_index=True, return_inverse=True)
    P, F = P[first], inv.reshape(-1)[F]  # welded across the texture seams
    _, once = np.unique(np.sort(F, 1), axis=0, return_index=True)
    keep = np.array([s is not None for s in slot], bool) & np.isin(np.arange(len(F)), once)
    keep &= (F[:, 0] != F[:, 1]) & (F[:, 1] != F[:, 2]) & (F[:, 0] != F[:, 2])  # welding collapsed it
    if reach:
        keep &= (np.abs(P[F][:, :, 0] - skull.cx) <= reach * skull.A).all(1)
    F, slot = F[keep], slot[keep]
    e = np.vstack([F[:, [0, 1]], F[:, [1, 2]]])
    _, lab = connected_components(coo_matrix((np.ones(len(e)), (e[:, 0], e[:, 1])), shape=(len(P),) * 2),
                                  directed=False)
    big = np.bincount(lab[F[:, 0]])[lab[F[:, 0]]] >= min_faces
    F, slot = F[big], slot[big]
    used, F = np.unique(F, return_inverse=True)
    P, F = P[used], F.reshape(-1, 3)
    if reach:  # close what the cut opened, each fan face in the slot of the face across its border edge
        P, capped = m3.cap_holes(P, F)
        owner = {(int(a), int(b)): i for i, f in enumerate(F) for a, b in ((f[0], f[1]), (f[1], f[2]), (f[2], f[0]))}
        slot = np.concatenate([slot, [slot[owner[(int(f[1]), int(f[0]))]] for f in capped[len(F):]]])
        F = capped
    piece = kit.Piece(P, F, slot)
    height = scalp = None
    if kind == "hair":
        d = P - skull.centre
        r = np.linalg.norm(d, axis=1)
        dirs = d / r[:, None]
        SP, SF, scol = pieces[skull_mesh]
        hit = kit.ray_distance(skull.centre, dirs, SP, SF)
        height = r - np.where(np.isnan(hit), skull.radius(dirs), hit)  # a ray out through the neck: the fit
        painted = np.flatnonzero([keys.get(tuple(int(v) for v in c)) == "hair" for c in scol])
        _, sinv = np.unique(np.round(SP / 1e-6).astype(np.int64), axis=0, return_inverse=True)
        G = sinv.reshape(-1)[SF[painted]]
        e = np.vstack([G[:, [0, 1]], G[:, [1, 2]]])
        _, lab = connected_components(coo_matrix((np.ones(len(e)), (e[:, 0], e[:, 1])), shape=(sinv.max() + 1,) * 2),
                                      directed=False)
        fl = lab[G[:, 0]]
        painted = painted[fl == np.bincount(fl).argmax()]  # the scalp; brows or lips in the hair's color stay out
        scalp = _directions(SP[SF[painted]].mean(1), skull)
    save(name, piece, kind, skull, source=f"{os.path.basename(glb)}:{mesh}", height=height, scalp=scalp)
    return piece, skull


def catalog():
    """Each library hair on a plain scripted head, in black and in blonde."""
    for name in names("hair"):
        _, piece = heads.build(dict(form=heads.FORMS["human"], eyes=dict(), brows=dict(), nose=dict(style="button"),
                                    mouth=dict(curve=0.15), ears=dict(style="round"), hair=dict(style="sculpt", name=name)))
        for color in ((40, 36, 40), (236, 200, 110)):
            yield name, piece, None, {**heads.COLORS, "hair": color}
