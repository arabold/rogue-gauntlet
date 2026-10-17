"""
the standard rigs from the Character Animations pack (`assets/standard-character-animations`), read as data: bone rest
transforms and clips sampled at a time, so rigidly bound kit parts can be shown in a real animation pose.

    rig = Rig.load("large")
    posed = rig.pose({"chest": piece, "upperarm.l": arm}, "Idle_A")
"""

import functools
import os

import numpy as np

from core import kit
from core import mesh as mm
from core.mesh import _node_worlds

RIGS_DIR = os.path.normpath(os.path.join(os.path.dirname(__file__), "../rigs"))


def _quat_matrix(q):
    x, y, z, w = q / np.linalg.norm(q)
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def _sample(times, values, t, rotation):
    """A LINEAR glTF channel at time `t` (clamped), quaternions by normalized lerp along the short way."""
    i = int(np.clip(np.searchsorted(times, t) - 1, 0, len(times) - 1))
    if i + 1 >= len(times) or t <= times[0]:
        return values[min(i, len(values) - 1)] if t > times[0] else values[0]
    a = (t - times[i]) / max(times[i + 1] - times[i], 1e-9)
    v0, v1 = values[i], values[i + 1]
    if rotation and v0 @ v1 < 0:
        v1 = -v1
    v = (1 - a) * v0 + a * v1
    return v / np.linalg.norm(v) if rotation else v


class Rig:
    """One rig GLB: its nodes, rest world transforms per bone name and its clips."""

    def __init__(self, path):
        self.g, _, self.acc = mm.read_glb(path)
        self.names = {nd.get("name"): i for i, nd in enumerate(self.g["nodes"])}
        self.rest = {nd.get("name"): W for nd, W in zip(self.g["nodes"], _node_worlds(self.g))}
        self.clips = {a["name"]: a for a in self.g.get("animations", [])}

    @staticmethod
    @functools.lru_cache(maxsize=None)
    def load(size="large", file="General"):
        """The rig of `size` ("medium" or "large") from the pack file `Rig_<Size>_<file>.glb`."""
        return Rig(os.path.join(RIGS_DIR, f"rig_{size}.glb"))

    def joint(self, bone):
        """A bone's rest position in world space."""
        return self.rest[bone][:3, 3].copy()

    def worlds(self, clip, t=0.0):
        """Every bone's world transform with `clip` applied at time `t` (seconds)."""
        g = {**self.g, "nodes": [dict(nd) for nd in self.g["nodes"]]}
        anim = self.clips[clip]
        for ch in anim["channels"]:
            node, path = ch["target"].get("node"), ch["target"]["path"]
            if node is None or path == "weights":
                continue
            s = anim["samplers"][ch["sampler"]]
            v = _sample(np.asarray(self.acc(s["input"]))[:, 0], np.asarray(self.acc(s["output"]), float), t,
                        path == "rotation")
            g["nodes"][node][path] = [float(x) for x in v]
            g["nodes"][node].pop("matrix", None)
        return {nd.get("name"): W for nd, W in zip(g["nodes"], _node_worlds(g))}

    def deltas(self, clip=None, t=0.0, helpers=None):
        """
        Every bone's world-space move from its rest pose into `clip` at time `t` (identity without a clip), plus
        helper bones: `helpers` maps a name to `(parent, follow, share)`, a bone riding on `parent` that turns about
        `follow`'s joint by `share` of `follow`'s swing relative to `parent` (e.g. a pauldron that turns by half
        the arm's swing).
        """
        posed = self.worlds(clip, t) if clip else self.rest
        D = {b: posed[b] @ np.linalg.inv(self.rest[b]) for b in self.rest if b in posed}
        for name, (parent, follow, share) in (helpers or {}).items():
            rel = np.linalg.inv(D[parent]) @ D[follow]
            j = self.joint(follow)
            H = np.eye(4)
            H[:3, :3] = _rotation_power(rel[:3, :3], share)
            H[:3, 3] = j - H[:3, :3] @ j
            D[name] = D[parent] @ H
        return D

    def pose(self, parts, clip, t=0.0, helpers=None, inverse=False):
        """`{bone: piece}` with every piece (authored in the rest pose, bound rigidly to its bone) moved into
        `clip` at time `t`, or with `inverse`, pieces authored in that pose moved back to the rest pose."""
        D = self.deltas(clip, t, helpers)
        out = {}
        for bone, piece in parts.items():
            M = np.linalg.inv(D[bone]) if inverse else D[bone]
            out[bone] = kit.Piece(piece.verts @ M[:3, :3].T + M[:3, 3], piece.faces, piece.slots)
        return out


def _rotation_power(R, share):
    """The rotation `R` scaled to `share` of its angle about the same axis."""
    angle = np.arccos(np.clip((np.trace(R) - 1) / 2, -1.0, 1.0))
    if angle < 1e-8:
        return np.eye(3)
    axis = np.array([R[2, 1] - R[1, 2], R[0, 2] - R[2, 0], R[1, 0] - R[0, 1]]) / (2 * np.sin(angle))
    return kit.rotation(axis, np.degrees(share * angle))



@functools.lru_cache(maxsize=None)
def mannequin(size="medium"):
    """the standard mannequin for a rig size, as `skinned_meshes` keyed by piece name (`Body`, `ArmLeft`...)."""
    path = os.path.join(RIGS_DIR, f"mannequin_{size}.glb")
    prefix = f"Mannequin_{size.capitalize()}_"
    return {k[len(prefix):]: v for k, v in skinned_meshes(path).items()}


# Garments that follow one part of the body only, by kind (`characters` names a garment piece `<Kind><index>`): a
# collar or a belt must not stretch down the arms or the legs it happens to be near, nor a flared sleeve's cuff
# toward the body.
WEIGHT_SOURCES = {kind: ("Body",) for kind in ("collar", "belt", "apron", "breastplate", "backplate", "armor", "tunic",
                                               "cape", "wings", "tail", "ribs", "necklace", "fur_trim",
                                               "cuirass")}
WEIGHT_SOURCES.update({kind: ("Head",) for kind in ("helmet", "crown", "hat")})
WEIGHT_SOURCES.update({kind: ("ArmLeft", "ArmRight", "LegLeft", "LegRight")  # flared cuffs must not reach the body
                       for kind in ("cuffs", "sleeves", "gloves", "boots")})


def _islands(faces, n):
    """Each vertex's connected part of a mesh (pieces merged from separate hulls stay separate parts)."""
    parent = np.arange(n)

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for a, b, c in np.asarray(faces):
        for u, v in ((a, b), (b, c)):
            ru, rv = find(u), find(v)
            if ru != rv:
                parent[ru] = rv
    return np.array([find(i) for i in range(n)])


# Hard garments: stiff things fixed at one place that must not bend or shear (a blend of two bones shears a rigid
# shape), so the whole piece follows the one bone that carries most of it. Garments worn round the body stay soft so
# they never part from it (a rigid belt opens gaps when the waist bends), but the hard bits hung on them (`TRIMMED`:
# a belt's buckle and studs, a necklace's charms; every part but the biggest) each ride one bone.
HARD = {"wings", "tail", "helmet", "crown", "hat"}
TRIMMED = {"belt", "necklace", "collar"}
LIMB_HARD = {"fur_trim", "cuffs", "shackles"}  # rings on a limb: one bone per side (a boot's fur cuff over the knee)


def _one_bone(bones, weights):
    """The single bone carrying the most of these (n, 4) weights, as (n, 4) weights all on it."""
    total = {}
    for b, w in zip(bones.ravel(), weights.ravel()):
        total[b] = total.get(b, 0.0) + w
    best = max(total, key=total.get)
    n = len(bones)
    return (np.column_stack([np.full(n, best, object), np.full((n, 3), "root", object)]),
            np.tile([1.0, 0.0, 0.0, 0.0], (n, 1)))


def smooth_weights(verts, faces, bones, weights, rounds=8, keep=4):
    """
    Weights diffused over a mesh's surface: each round, every vertex moves halfway to the mean of its neighbours
    (points at the same place count as one). Copying weights vertex by vertex leaves seams where neighbours took
    different parts of the mannequin (the hips and a thigh); a soft garment tears there when the leg swings.
    """
    names = sorted({b for b in bones.ravel()})
    col = {b: i for i, b in enumerate(names)}
    W = np.zeros((len(verts), len(names)))
    for k in range(bones.shape[1]):
        np.add.at(W, (np.arange(len(verts)), [col[b] for b in bones[:, k]]), weights[:, k])
    welded = np.unique(np.round(verts * 1e4).astype(np.int64), axis=0, return_inverse=True)[1].ravel()
    m = welded.max() + 1
    E = np.concatenate([faces[:, [0, 1]], faces[:, [1, 2]], faces[:, [2, 0]]])
    E = welded[E]
    E = np.unique(np.sort(np.vstack([E, E[:, ::-1]]), axis=1), axis=0)
    E = E[E[:, 0] != E[:, 1]]
    Wp = np.zeros((m, len(names)))
    np.add.at(Wp, welded, W)
    Wp /= np.maximum(Wp.sum(1, keepdims=True), 1e-12)
    deg = np.bincount(E.ravel(), minlength=m)[:, None]
    for _ in range(rounds):
        acc = np.zeros_like(Wp)
        np.add.at(acc, E[:, 0], Wp[E[:, 1]])
        np.add.at(acc, E[:, 1], Wp[E[:, 0]])
        Wp = np.where(deg > 0, 0.5 * Wp + 0.5 * acc / np.maximum(deg, 1), Wp)
    W = Wp[welded]
    if W.shape[1] < keep:  # always `keep` columns, the extra ones empty
        W = np.hstack([W, np.zeros((len(W), keep - W.shape[1]))])
        names += ["root"] * (keep - len(names))
    order = np.argsort(-W, 1)[:, :keep]
    w = np.take_along_axis(W, order, 1)
    w /= np.maximum(w.sum(1, keepdims=True), 1e-12)
    return np.array(names, object)[order], w


def body_skin(pieces, size="medium"):
    """The character's own skin as a weight source for its garments: `{piece: (verts, bone names, weights)}` for
    the body pieces among `pieces`, each weighted from the mannequin."""
    m = mannequin(size)
    return {name: (p.verts, *transfer_weights(p.verts, name, size)) for name, p in pieces.items() if name in m}


def transfer_weights(verts, piece, size="medium", faces=None, skin=None):
    """Skin weights for `verts` copied from the nearest vertex of the standard mannequin: from its piece of the same name
    (`ArmLeft`...), from the parts a garment of that kind follows (`WEIGHT_SOURCES`), or from the whole mannequin
    (wraps, skirts, sleeves), or with `skin` (`body_skin`) from the character's own skin under it, so cloth and
    skin move alike. `HARD` garments follow a single bone, unbent; with `faces`, so do the parts hung on a
    `TRIMMED` garment (a belt's buckle and studs), while the rest blends like skin. Returns `(bone names, weights)`,
    (n, 4)."""
    import re

    from scipy.spatial import cKDTree

    kind = re.sub(r"\d+$", "", piece).lower()
    if kind == "pauldrons":  # half on the chest, half on the upper arm: no shoulder bone to turn half as far
        verts = np.asarray(verts, float)
        side = np.where(verts[:, 0] >= 0, "upperarm.l", "upperarm.r")
        bones = np.column_stack([side, np.full(len(verts), "chest"), np.full((len(verts), 2), "root")]).astype(object)
        return bones, np.tile([0.5, 0.5, 0.0, 0.0], (len(verts), 1))
    m = mannequin(size)
    limb = re.match(r"(Arm|Leg)[A-Z]", piece)  # `characters` names a limb's garment `Leg<Kind><i>` / `Arm<Kind><i>`
    names = ([piece] if piece in m else [limb.group(1) + "Left", limb.group(1) + "Right"] if limb
             else WEIGHT_SOURCES.get(kind, list(m)))
    verts = np.asarray(verts, float)
    if skin and piece not in m and all(n in skin for n in names):  # a garment follows the skin right under it
        src = [skin[n] for n in names]
        _, i = cKDTree(np.vstack([s[0] for s in src])).query(verts)
        bones, weights = np.vstack([s[1] for s in src])[i], np.vstack([s[2] for s in src])[i]
    else:
        src = [m[n] for n in names]
        _, i = cKDTree(np.vstack([s[0] for s in src])).query(verts)
        bones, weights = np.vstack([s[2] for s in src])[i], np.vstack([s[3] for s in src])[i]
    if kind in HARD:
        return _one_bone(bones, weights)
    if faces is not None and len(verts) and piece not in m:  # a soft garment: no seams between body parts
        bones, weights = smooth_weights(verts, np.asarray(faces), bones, weights, rounds=3)
    base = kind[3:] if limb else kind
    if base in LIMB_HARD and set(names) <= {"ArmLeft", "ArmRight", "LegLeft", "LegRight"}:
        for side in (verts[:, 0] >= 0, verts[:, 0] < 0):
            if side.any():
                bones[side], weights[side] = _one_bone(bones[side], weights[side])
        return bones, weights
    if faces is not None and len(verts) and kind in TRIMMED:  # buckles, studs, charms ride one bone, unbent
        part = _islands(faces, len(verts))
        ids, counts = np.unique(part, return_counts=True)
        sel = part != ids[np.argmax(counts)]  # everything but the band itself, together: a buckle's bars stay one
        if sel.any():
            bones[sel], weights[sel] = _one_bone(bones[sel], weights[sel])
    return bones, weights


def skin(D, verts, bones, weights):
    """`verts` moved by linear blend skinning: each by its `bones`' world moves `D`, mixed by `weights`."""
    out = np.zeros_like(verts)
    for b in set(bones[weights > 0]):
        w = np.where(bones == b, weights, 0.0).sum(1)
        sel = w > 0
        out[sel] += w[sel, None] * (verts[sel] @ D[b][:3, :3].T + D[b][:3, 3])
    return out


def skinned(rig, pieces, clip, t=0.0, size="medium"):
    """`{name: piece}` in the rest pose, skinned with weights from the standard mannequin (`transfer_weights`) and moved
    into `clip` at time `t`."""
    D = rig.deltas(clip, t)
    own = body_skin(pieces, size)
    return {name: kit.Piece(skin(D, p.verts, *transfer_weights(p.verts, name, size, p.faces, own)), p.faces, p.slots)
            for name, p in pieces.items()}


def skinned_meshes(path):
    """A skinned GLB's meshes in their bind pose, in world space: `{name: (verts, faces, bone names (n, 4), weights
    (n, 4))}`."""
    g, _, acc = mm.read_glb(path)
    W = _node_worlds(g)
    out = {}
    for nd in g["nodes"]:
        if "mesh" not in nd or "skin" not in nd:
            continue
        skin = g["skins"][nd["skin"]]
        joints = skin["joints"]
        ibm = np.asarray(acc(skin["inverseBindMatrices"]), float).reshape(-1, 4, 4).transpose(0, 2, 1)
        for prim in g["meshes"][nd["mesh"]]["primitives"]:
            a = prim["attributes"]
            P = np.asarray(acc(a["POSITION"]), float)
            J = np.asarray(acc(a["JOINTS_0"]), int)
            Wt = np.asarray(acc(a["WEIGHTS_0"]), float)
            Wt = Wt / np.maximum(Wt.sum(1, keepdims=True), 1e-12)
            first = np.argmax(Wt, 1)
            M = np.stack([W[joints[J[i, first[i]]]] @ ibm[J[i, first[i]]] for i in range(len(P))])
            V = np.einsum("nij,nj->ni", M[:, :3, :3], P) + M[:, :3, 3]
            names = np.array([[g["nodes"][joints[j]]["name"] for j in row] for row in J], object)
            out[nd["name"]] = (V, np.asarray(acc(prim["indices"]), np.int64).reshape(-1, 3), names, Wt)
    return out
