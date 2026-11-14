"""KayKit's adventurer heads, arms and legs (CC0), worn by the class heroes exactly as KayKit modeled them.

`harvest()` reads each class head, arms and legs from the KayKit characters in the rest pose (the frame Studio
characters are built in). It saves them to `meshes/<class>_head.npz`, `meshes/<class>_arms.npz` and
`meshes/<class>_legs.npz`, plus `meshes/hero_arms.npz` and `meshes/hero_legs.npz`: the rogue's plain limbs (full
sleeves, leather bracers and gloves; pants, bare knees and boots) that every bare hero wears. A class's own limbs belong
to a body armor suit, which hides the hero's while worn. Each face's KayKit swatch becomes a character slot (`skin`,
`hair`, `eye`; the limbs' `cloth`, `cloth_dark`, `strap`, `glove`, `pants`, `boot`), and each corner keeps its position
down that swatch's gradient (`uv_t`) and its authored normal. The sleeves take the hero's tunic color and the pants
its pants color.
Run once from `asset-studio/` (`python -c "from prefabs.anatomy import adventurer_parts as a; a.harvest()"`) and
commit the result, so builds never read the KayKit packs.

Faces are labeled by part, so the game can hide what headgear covers:

- `Ears`: the ear islands, with any earrings.
- `Hair`: the hair faces of the head shell. KayKit hair is the top of the head itself, with no scalp beneath, so the
  harvest also builds a `Scalp`: the same faces pulled down onto the barbarian's bald skull (every class head shares
  it), its hairline edge left exactly where the hair meets the skin. The game shows it only while the hair is hidden.
- `Head`: everything else (face, brows, eyes, nose, a beard).
"""

import io
import json
from functools import lru_cache
from pathlib import Path

import numpy as np

from core import kit

MESHES = Path(__file__).resolve().parent / "meshes"
SOURCES = {"barbarian": "Barbarian", "knight": "Knight", "mage": "Mage", "rogue": "Rogue"}
# The adventurer atlases share their layout: row and column of a swatch -> the character slot it colors. Cell 01 is a
# class's hair color (the barbarian's beard and brows).
SLOTS = {"00": "skin", "01": "hair", "02": "eye", "03": "metal", "11": "hair_tie"}
HAIR = {"barbarian": (), "knight": ("hair",), "mage": ("hair", "hair_tie"), "rogue": ("hair",)}
BARE = "Rogue"  # whose limbs every hero wears: plain sleeves, bracers and gloves; pants, bare knees and boots
# the rogue atlas's limb swatches -> the bare hero's slots, by limb
BARE_SLOTS = {"arms": {"10": "cloth", "11": "cloth_dark", "05": "strap", "25": "glove"},
              "legs": {"17": "pants", "23": "boot", "00": "skin"}}
HERO_COLORED = {"cloth", "cloth_dark", "pants", "skin"}  # bare limb slots in the hero's own colors, not KayKit's
LIMBS = ("arms", "legs")
EAR_X = 0.36  # islands centred this far out to the side are ears (and the earrings hung on them)
SCALP_INSET = 0.008  # the scalp sits this far under the hair, so it never shows through it


def _skinned(g, acc, worlds, index):
    """Rest-pose positions, normals, faces and texture coordinates of KayKit skinned node `index`."""
    node = g["nodes"][index]
    primitive = g["meshes"][node["mesh"]]["primitives"][0]
    attributes = primitive["attributes"]
    skin = g["skins"][node["skin"]]
    inverse = acc(skin["inverseBindMatrices"]).reshape(-1, 4, 4).transpose(0, 2, 1)
    joints = acc(attributes["JOINTS_0"]).astype(int)
    weights = acc(attributes["WEIGHTS_0"]).astype(float)
    weights = weights / {5121: 255.0, 5123: 65535.0}.get(g["accessors"][attributes["WEIGHTS_0"]]["componentType"], 1.0)
    matrices = np.stack([worlds[j] @ inverse[k] for k, j in enumerate(skin["joints"])])
    blend = sum(weights[:, c, None, None] * matrices[joints[:, c]] for c in range(4))
    p = acc(attributes["POSITION"]).astype(float)
    n = acc(attributes["NORMAL"]).astype(float)
    verts = np.einsum("nij,nj->ni", blend[:, :3, :3], p) + blend[:, :3, 3]
    normals = np.einsum("nij,nj->ni", blend[:, :3, :3], n)
    normals /= np.linalg.norm(normals, axis=1, keepdims=True)
    faces = acc(primitive["indices"]).reshape(-1, 3).astype(np.int64)
    return verts, normals, faces, acc(attributes["TEXCOORD_0"]).astype(float)


def _read(title, node="Head", cells=SLOTS):
    """`(verts, normals, faces, slots, uv_t, colors)` of KayKit character `title`'s `node` mesh, its swatches named by
    `cells` (a function of the cell, or a mapping)."""
    from PIL import Image

    from core import mesh
    from prefabs import decor

    g, binary, acc = mesh.read_glb(str(decor.ASSETS / "kaykit-adventurers" / f"{title}.glb"))
    worlds = mesh._node_worlds(g)
    view = g["bufferViews"][g["images"][0]["bufferView"]]
    start = view.get("byteOffset", 0)
    texture = np.asarray(Image.open(io.BytesIO(binary[start:start + view["byteLength"]])).convert("RGB"), float)
    index = [n.get("name") for n in g["nodes"]].index(f"{title}_{node}")
    verts, normals, faces, uv = _skinned(g, acc, worlds, index)
    swatches, uv_t, colors, looks = decor.swatch_faces(uv, faces, texture, "")
    name = cells if callable(cells) else lambda cell: cells[cell]
    slots = np.array([name(c.lstrip("_")) for c in swatches], object)
    return (verts, normals, faces, slots, uv_t, {name(c.lstrip("_")): rgb for c, rgb in colors.items()},
            {name(c.lstrip("_")): look for c, look in looks.items()})


def _welded(verts, faces):
    """Each corner's welded vertex id: KayKit splits vertices along texture seams."""
    return np.unique(np.round(verts * 1e4).astype(np.int64), axis=0, return_inverse=True)[1].ravel()[faces]


def _parts(name, verts, faces, slots):
    """Each face's part: `Ears` (side islands), `Hair` (the class's hair slots on the shell) or `Head`."""
    from core.rig import _islands

    welded = _welded(verts, faces)
    island = _islands(welded, welded.max() + 1)[welded[:, 0]]
    ids, counts = np.unique(island, return_counts=True)
    shell = island == ids[np.argmax(counts)]
    part = np.full(len(faces), "Head", object)
    for i in ids:
        sel = island == i
        if not shell[sel][0] and abs(verts[faces[sel]].reshape(-1, 3)[:, 0].mean()) > EAR_X:
            part[sel] = "Ears"
    part[shell & np.isin(slots, HAIR[name])] = "Hair"
    return part, shell


def _scalp(verts, faces, part, skull):
    """The hair faces' vertices pulled down onto `skull` (`(centre, verts, faces)`), the hairline left in place."""
    centre, skull_verts, skull_faces = skull
    welded = _welded(verts, faces)
    hairline = np.zeros(welded.max() + 1, bool)
    hairline[welded[part != "Hair"].ravel()] = True
    scalp = verts.copy()
    inner = np.unique(faces[part == "Hair"][~hairline[welded[part == "Hair"]]])
    offset = verts[inner] - centre
    reach = np.linalg.norm(offset, axis=1)
    direction = offset / reach[:, None]
    surface = np.nan_to_num(kit.ray_distance(centre, direction, skull_verts, skull_faces), nan=0.0)
    scalp[inner] = centre + direction * np.minimum(surface - SCALP_INSET, reach - SCALP_INSET)[:, None]
    return scalp


def harvest():
    """Read the four class heads, with their parts and scalps, and the heroes' and classes' limbs, and save them to
    `meshes/`."""
    from prefabs.anatomy import heads

    read = {name: _read(title)[:6] for name, title in SOURCES.items()}
    bald_verts, _, bald_faces, bald_slots, _, _ = read["barbarian"]
    _, bald_shell = _parts("barbarian", bald_verts, bald_faces, bald_slots)
    skull = bald_faces[bald_shell & (bald_slots == "skin")]
    skull = (heads.Head.fit(bald_verts[np.unique(skull)]).centre, bald_verts, bald_faces[bald_shell])
    for name, (verts, normals, faces, slots, uv_t, colors) in read.items():
        part, _ = _parts(name, verts, faces, slots)
        meta = {"name": f"{name}_head", "category": "heads", "domain": "anatomy", "license": "CC0 (KayKit)",
                "source": f"kaykit-adventurers/{SOURCES[name]}.glb", "verts": len(verts), "tris": len(faces),
                "span": [round(float(x), 4) for x in np.ptp(verts, axis=0)], "colors": colors,
                "parts": {p: int((part == p).sum()) for p in dict.fromkeys(part)}}
        np.savez_compressed(MESHES / f"{name}_head.npz", verts=verts.astype(np.float32),
                            faces=faces.astype(np.int32), slots=np.asarray(slots, str),
                            normals=normals[faces].astype(np.float32), uv_t=np.asarray(uv_t, np.float32),
                            part=np.asarray(part, str), scalp=_scalp(verts, faces, part, skull).astype(np.float32),
                            meta=json.dumps(meta))
        print(f"  [+] {name}_head {len(faces):4d} tris  {meta['parts']}")
    for limb in LIMBS:
        _save_limbs("hero", BARE, limb, BARE_SLOTS[limb], hero_colored=HERO_COLORED, gradients=False)
        for name, title in SOURCES.items():
            _save_limbs(name, title, limb, lambda cell, name=name, limb=limb: f"{name}_{limb[:-1]}_{cell}")


def sides(limb):
    """The KayKit nodes of `limb` (`arms`, `legs`), the pieces it builds: `("ArmLeft", "ArmRight")`."""
    return tuple(f"{limb[:-1].capitalize()}{side}" for side in ("Left", "Right"))


def _save_limbs(name, title, limb, cells, hero_colored=(), gradients=True):
    """Save KayKit character `title`'s two `limb` (`arms`, `legs`) as `meshes/<name>_<limb>.npz`, swatches named by
    `cells`, with KayKit's own colors but for the `hero_colored` slots that take the character's, and with `gradients`
    KayKit's exact swatch gradients."""
    read = {side: _read(title, side, cells) for side in sides(limb)}
    whole = kit.Piece.merge(*(kit.Piece(verts, faces, slots, normals=normals[faces], uv_t=uv_t)
                              for verts, normals, faces, slots, uv_t, _, _ in read.values()))
    meta = {"name": f"{name}_{limb}", "category": limb, "domain": "anatomy", "license": "CC0 (KayKit)",
            "source": f"kaykit-adventurers/{title}.glb", "verts": len(whole.verts), "tris": len(whole.faces),
            "span": [round(float(x), 4) for x in np.ptp(whole.verts, axis=0)],
            "colors": {k: v for r in read.values() for k, v in r[5].items() if k not in hero_colored},
            "materials": {k: {"gradient": look["gradient"]} for r in read.values() for k, look in r[6].items()}
            if gradients else {}}
    np.savez_compressed(MESHES / f"{name}_{limb}.npz", verts=whole.verts.astype(np.float32),
                        faces=whole.faces.astype(np.int32), slots=np.asarray(whole.slots, str),
                        normals=whole.normals.astype(np.float32), uv_t=whole.uv_t.astype(np.float32),
                        part=np.asarray(np.repeat(list(read), [len(r[2]) for r in read.values()]), str),
                        meta=json.dumps(meta))
    print(f"  [+] {name}_{limb} {len(whole.faces):4d} tris  <- {title}.glb")


@lru_cache(maxsize=None)
def _stored(name):
    with np.load(MESHES / f"{name}.npz") as data:
        return {k: data[k] for k in data.files if k != "meta"}, json.loads(str(data["meta"]))


def limbs(name, limb):
    """`limb` (`arms`, `legs`) as `{"ArmLeft": kit.Piece, "ArmRight": kit.Piece}` (`LegLeft`, `LegRight`), hands and
    feet and all: the bare heroes' (`hero`, their sleeves in the tunic's `cloth`, their pants in `pants`) or a class's
    own (`knight`), worn with a body armor suit."""
    data, _ = _stored(f"{name}_{limb}")
    whole = kit.Piece(data["verts"].astype(float), data["faces"].astype(np.int64), data["slots"].astype(object),
                      normals=data["normals"].astype(float), uv_t=data["uv_t"].astype(float))
    part = data["part"].astype(object)
    return {side: whole.faces_where(part == side) for side in sides(limb)}


def limb_colors(name, limb):
    """`(colors, looks)`: KayKit's own colors for `name`'s `limb` slots, and their exact gradients (a class's limbs
    only; the bare heroes' leather, gloves and boots take the character's shading)."""
    meta = _stored(f"{name}_{limb}")[1]
    return {slot: tuple(rgb) for slot, rgb in meta["colors"].items()}, meta["materials"]


def tinted(look, rgb):
    """A limb slot's `look` (KayKit's exact swatch gradient) recolored to `rgb` at its middle, keeping KayKit's shading:
    every row keeps its brightness relative to the middle, so a suit can dress a class's limbs in its own colors."""
    gradient = np.asarray(look["gradient"], float)
    luma = gradient @ (0.299, 0.587, 0.114)
    shade = (luma + 1.0) / (luma[len(gradient) // 2] + 1.0)
    rows = np.clip(np.asarray(rgb, float) * shade[:, None], 0, 255).round().astype(int)
    return {**look, "gradient": [tuple(int(v) for v in row) for row in rows]}


def colors(name):
    """KayKit's own colors for class `name`'s head slots."""
    return {slot: tuple(rgb) for slot, rgb in _stored(f"{name}_head")[1]["colors"].items()}


def parts(name, suppress=()):
    """Class `name`'s head as `{part: kit.Piece}`: `Head`, `Ears`, and with hair `Hair` and its `Scalp`. A part
    named in `suppress` (`"hair"`, `"ears"`) is left out (a hidden head's scalp stays)."""
    data, _ = _stored(f"{name}_head")
    faces, part = data["faces"].astype(np.int64), data["part"].astype(object)
    verts, slots = data["verts"].astype(float), data["slots"].astype(object)
    whole = kit.Piece(verts, faces, slots, normals=data["normals"].astype(float), uv_t=data["uv_t"].astype(float))
    out = {p: whole.faces_where(part == p) for p in dict.fromkeys(part) if p.lower() not in suppress}
    if (part == "Hair").any():
        hair = part == "Hair"
        out["Scalp"] = kit.Piece(data["scalp"].astype(float), faces[hair], "skin").faces_where(np.ones(hair.sum(), bool))
    return out
