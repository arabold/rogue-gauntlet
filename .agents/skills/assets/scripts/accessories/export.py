"""
Kit characters as drop-in KayKit GLBs. The game expects KayKit's exact structure (`player.tscn`, enemy scenes,
`CharacterModel`): the `Rig` armature with its 41 joints, KayKit's clips baked into the model's own AnimationPlayer,
and props as nodes parented to bones (Godot imports them as `BoneAttachment3D`s, which `CharacterClass`
`AttachmentNodeNames` look up by name). So a character is not exported from scratch: a KayKit GLB (`TEMPLATES`) is
copied with its skeleton, skin, clips and prop nodes untouched, and only the meshes are ours:

- every kit piece becomes a mesh skinned to the template's skin, weights copied from KayKit's mannequin
  (`rig.transfer_weights`); positions stay in the rig's bind space, so size and orientation match KayKit exactly;
- headgear and capes go into the template's hat and cape prop nodes, rigid on `head` and `chest` like KayKit's
  (or are skinned like the rest where the template has no such node);
- the template's own props are emptied (the nodes stay, so equipment still finds its attachment point); hero
  templates keep their prop names (the class resources look them up), monster templates' are renamed after the
  character;
- weapons go into new nodes on `handslot.r` / `handslot.l`, placed like the player's equipment and counter-scaled
  by the rig scale, so a goblin's sword keeps its true size;
- `scale` sizes the whole character through the `Rig` node, which no clip animates;
- colors are one small palette texture, a flat swatch per material slot (`monster_mesh.Atlas`), and slots that are
  metal or glow get their own materials (`MATERIALS`; weapons keep each surface's metallic and roughness); normals
  are smooth within `crease` degrees and hard across sharper edges, KayKit's soft low-poly look.
"""

import io
import json
import os
import struct

import numpy as np

from accessories.rig import body_skin, transfer_weights
import monster_mesh as mm

REPO = os.path.normpath(os.path.join(os.path.dirname(__file__), "../../../../.."))
TEMPLATES = {name: os.path.join(REPO, "assets", path) for name, path in {
    "barbarian": "kaykit-adventurers/Barbarian.glb", "knight": "kaykit-adventurers/Knight.glb",
    "mage": "kaykit-adventurers/Mage.glb", "rogue": "kaykit-adventurers/Rogue.glb",
    "rogue_hooded": "kaykit-adventurers/Rogue_Hooded.glb", "skeleton": "kaykit-skeletons/Skeleton_Warrior.glb",
}.items()}
# Surface looks per material slot (everything else stays KayKit's matte default): metal shines, glows glow.
MATERIALS = {**{slot: dict(metallic=0.5, roughness=0.3) for slot in ("gold", "gold_dark", "metal", "rivet")},
             **{slot: dict(metallic=0.45, roughness=0.4) for slot in ("armor", "armor_dark", "trim")},
             **{slot: dict(metallic=0.35, roughness=0.6) for slot in ("iron", "iron_dark", "rivet_iron")},
             "rust": dict(metallic=0.25, roughness=0.6),
             **{slot: dict(emissive=True) for slot in ("glow", "halo", "crystal")},
             # hair, fur, feathers and cloth stay matte: KayKit's default sheen makes them look like plastic
             **{slot: dict(metallic=0.0, roughness=1.0) for slot in ("brow", "hair", "beard", "fur", "fur_trim", "pelt",
                                                                     "pelt_dark", "feather", "wrap", "wrap_dark",
                                                                     "cloth", "rag")}}
HEROES = ("barbarian", "knight", "mage", "rogue")
GENERATOR = "Rogue Gauntlet character_kit"  # glTF asset.generator of every export (`character_kit` backs up others)  # templates whose prop node names the class resources look up
# where KayKit grips props: weapons in the right hand turned about Y, off-hand ones as they come (`Barbarian.glb`)
WEAPON_SLOTS = {"handslot.r": dict(translation=[0.0, 0.0333, 0.0], rotation=[0.0, 1.0, 0.0, 0.0]),
                "handslot.l": dict(translation=[0.0, 0.0333, 0.0])}


def corner_normals(verts, faces, crease=50.0):
    """Per-corner normals (n faces, 3, 3): each the area-weighted mean of the faces around its point that turn less
    than `crease` degrees from its own face, so curved surfaces shade smooth and sharp edges stay hard."""
    welded = np.unique(np.round(verts * 1e5).astype(np.int64), axis=0, return_inverse=True)[1].ravel()
    F = welded[faces]
    tri = verts[faces]
    fn = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    unit = fn / np.maximum(np.linalg.norm(fn, axis=1, keepdims=True), 1e-12)
    order = np.argsort(F.ravel(), kind="stable")
    corner_v = F.ravel()[order]
    cuts = np.flatnonzero(np.diff(corner_v)) + 1
    out = np.zeros((len(faces) * 3, 3))
    cos = np.cos(np.radians(crease))
    for group in np.split(order, cuts):
        f = group // 3
        near = (unit[f] @ unit[f].T) > cos
        out[group] = near.astype(float) @ fn[f]
    out = out.reshape(-1, 3, 3)
    return out / np.maximum(np.linalg.norm(out, axis=2, keepdims=True), 1e-12)


class _Writer:
    """A new GLB built from a template: template accessors are copied over on demand, new data appended."""

    def __init__(self, template):
        self.src, self.src_bin, _ = mm.read_glb(template)
        self.g = json.loads(json.dumps(self.src))
        self.g["bufferViews"], self.g["accessors"], self.bin = [], [], bytearray()
        self._views, self._accessors = {}, {}

    def _view(self, data, target=None):
        self.bin.extend(b"\x00" * (-len(self.bin) % 4))
        view = {"buffer": 0, "byteOffset": len(self.bin), "byteLength": len(data)}
        if target:
            view["target"] = target
        self.bin.extend(data)
        self.g["bufferViews"].append(view)
        return len(self.g["bufferViews"]) - 1

    def copy(self, i):
        """Template accessor `i` in the new file (its buffer view copied once)."""
        if i not in self._accessors:
            a = dict(self.src["accessors"][i])
            v = a["bufferView"]
            if v not in self._views:
                sv = self.src["bufferViews"][v]
                o = sv.get("byteOffset", 0)
                self._views[v] = self._view(bytes(self.src_bin[o:o + sv["byteLength"]]), sv.get("target"))
                if "byteStride" in sv:
                    self.g["bufferViews"][self._views[v]]["byteStride"] = sv["byteStride"]
            a["bufferView"] = self._views[v]
            self.g["accessors"].append(a)
            self._accessors[i] = len(self.g["accessors"]) - 1
        return self._accessors[i]

    def add(self, array, kind, component, target=None, bounds=False):
        """`array` as a new accessor of glTF `kind` ("VEC3"...) and `component` type (5126 float...)."""
        dtype = {5126: "<f4", 5125: "<u4", 5123: "<u2", 5121: "u1"}[component]
        arr = np.ascontiguousarray(array, dtype)
        a = {"bufferView": self._view(arr.tobytes(), target), "componentType": component, "count": len(arr),
             "type": kind}
        if bounds:
            a["min"], a["max"] = arr.min(0).tolist(), arr.max(0).tolist()
        self.g["accessors"].append(a)
        return len(self.g["accessors"]) - 1

    def save(self, path):
        self.g["buffers"] = [{"byteLength": len(self.bin)}]
        js = json.dumps(self.g, separators=(",", ":")).encode()
        js += b" " * (-len(js) % 4)
        self.bin.extend(b"\x00" * (-len(self.bin) % 4))
        os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
        with open(path, "wb") as f:
            f.write(struct.pack("<4sII", b"glTF", 2, 28 + len(js) + len(self.bin)))
            f.write(struct.pack("<I4s", len(js), b"JSON") + js)
            f.write(struct.pack("<I4s", len(self.bin), b"BIN\x00") + bytes(self.bin))


def export(path, name, pieces, colors, template="skeleton", scale=1.0, hat=None, cape=None, weapons=None,
           materials=None, crease=50.0):
    """
    Write the character `name` to `path` as a KayKit drop-in on the `template` GLB (a `TEMPLATES` key or a path).
    `pieces`: `{piece name: kit.Piece}` in the rest pose, skinned by their names (`Body`, `ArmLeft`...; anything else
    follows the nearest part of the mannequin); `hat` / `cape`: pieces placed rigidly in the template's hat and cape
    prop nodes; `weapons`: `{"handslot.r" | "handslot.l": (verts (n, 3, 3) triangles in the item's own frame,
    their colors (n, 3), their metallic and roughness (n, 2))}`; `colors`: slot -> RGB; `materials`: slot ->
    `dict(metallic, roughness)` or `dict(emissive=True)` over `MATERIALS`. Returns the bind-pose height of the
    character, unscaled.
    """
    w = _Writer(TEMPLATES.get(template, template))
    g = w.g
    nodes = g["nodes"]
    index = {nd.get("name"): i for i, nd in enumerate(nodes)}
    skin = g["skins"][0]
    joint_of = {nodes[n]["name"]: j for j, n in enumerate(skin["joints"])}
    parent = {c: i for i, nd in enumerate(nodes) for c in nd.get("children", [])}
    rig = index["Rig"]
    worlds = _worlds(g)

    # palette: every slot any piece, prop or weapon uses; a weapon's slots are its colors with their materials
    slot_colors, looks = dict(colors), {**MATERIALS, **(materials or {})}
    weapon_slots = {}
    for i, (bone, (_, rgb, mr)) in enumerate((weapons or {}).items()):
        weapon_slots[bone] = []
        for c, (m, r) in zip(np.asarray(rgb, np.uint8), np.asarray(mr, float)):
            key = f"weapon{i}:{tuple(int(v) for v in c)}:{m:.2f}:{r:.2f}"
            slot_colors[key], looks[key] = tuple(int(v) for v in c), dict(metallic=float(m), roughness=float(r))
            weapon_slots[bone].append(key)
    used = sorted({str(s) for p in [*pieces.values(), hat, cape] if p is not None for s in p.slots} |
                  {k for k in slot_colors if k.startswith("weapon")})
    missing = [s for s in used if s not in slot_colors]
    if missing:
        raise ValueError(f"no color for slots {missing}")
    label = {s: i for i, s in enumerate(used)}
    cell, pad = 8, 4
    size = 1 << int(np.ceil(np.log2(max(64, len(used) * cell + 2 * pad))))
    atlas = mm.Atlas([slot_colors[s] for s in used], size=size, pad=pad, cell=cell)
    base = g["materials"][0]
    material_specs = [None]  # 0: the template's matte material

    def material(slot):
        spec = looks.get(str(slot))
        key = None if spec is None else tuple(sorted(spec.items()))
        if key not in material_specs:
            material_specs.append(key)
        return material_specs.index(key)

    def mesh(tri, slots, normals_from=None, bones=None, weights=None):
        """A glTF mesh of triangles `tri` (n, 3, 3) with a slot per face; skinned when `bones` are given."""
        verts, faces = tri.reshape(-1, 3), np.arange(3 * len(tri)).reshape(-1, 3)
        N = corner_normals(*(normals_from or (verts, faces)), crease).reshape(-1, 3)
        uv = np.repeat(np.array([atlas.flat_uv(label[str(s)]) for s in slots]), 3, axis=0)
        cols = [verts, N, uv]
        if bones is not None:
            J = np.vectorize(lambda b: joint_of.get(b, 0))(bones).astype(np.uint8)
            cols += [J, weights]
        rows = np.column_stack([np.round(c.astype(float), 6) for c in cols])
        uniq, inv = np.unique(rows, axis=0, return_inverse=True)
        attrs = {"POSITION": w.add(uniq[:, 0:3], "VEC3", 5126, 34962, bounds=True),
                 "NORMAL": w.add(uniq[:, 3:6], "VEC3", 5126, 34962),
                 "TEXCOORD_0": w.add(uniq[:, 6:8], "VEC2", 5126, 34962)}
        if bones is not None:
            attrs["JOINTS_0"] = w.add(uniq[:, 8:12], "VEC4", 5121, 34962)
            attrs["WEIGHTS_0"] = w.add(uniq[:, 12:16], "VEC4", 5126, 34962)
        faces_of = np.array([material(s) for s in slots])
        prims = []
        for mat in np.unique(faces_of):  # one primitive per material, all sharing the vertex data
            idx = w.add(inv.reshape(-1, 3)[faces_of == mat].ravel(), "SCALAR", 5125 if len(uniq) > 65535 else 5123,
                        34963)
            prims.append({"attributes": attrs, "indices": idx, "material": int(mat), "mode": 4})
        g["meshes"].append({"primitives": prims})
        return len(g["meshes"]) - 1

    g["meshes"] = []
    # The template's own character meshes go. Its prop nodes stay as attachment points, holding a speck of a mesh:
    # Godot only gives a prop node its named BoneAttachment3D when it has a mesh (the game strips them anyway).
    speck = None
    for i, nd in enumerate(nodes):
        if "skin" in nd:
            nodes[parent[i]]["children"].remove(i)
            nodes[i] = {"name": nd["name"] + "_unused"}
        elif "mesh" in nd:
            if speck is None:
                speck = mesh(np.zeros((1, 3, 3)) + [[0.0, 0.0, 0.0], [1e-4, 0.0, 0.0], [0.0, 1e-4, 0.0]], [used[0]])
            nd["mesh"] = speck

    def into_node(node_i, piece):
        """`piece` (rest-pose world) as the mesh of a prop node, in that node's own frame."""
        M = np.linalg.inv(worlds[node_i])
        tri = piece.verts[piece.faces] @ M[:3, :3].T + M[:3, 3]
        nodes[node_i]["mesh"] = mesh(tri, piece.slots, (piece.verts @ M[:3, :3].T + M[:3, 3], piece.faces))

    pieces = dict(pieces)
    for kinds, piece in ((("_Hat", "_Helmet"), hat), (("_Cape",), cape)):
        if piece is not None:
            slot_node = next((i for i, nd in enumerate(nodes) if nd.get("name", "").endswith(kinds)
                              and i in parent and "children" not in nd), None)
            if slot_node is None:  # no such prop node on this template: worn like any other piece
                pieces[kinds[0][1:]] = piece
            else:
                into_node(slot_node, piece)
    if not template.startswith(tuple(HEROES)):  # a monster's props are its own, not the template skeleton's
        prefix = os.path.splitext(os.path.basename(TEMPLATES.get(template, template)))[0] + "_"
        for nd in nodes:
            if nd.get("name", "").startswith(prefix) and "mesh" in nd:
                nd["name"] = name + "_" + nd["name"][len(prefix):]

    own = body_skin(pieces)
    for piece_name, piece in pieces.items():
        bones, weights = transfer_weights(piece.verts, piece_name, faces=piece.faces, skin=own)
        corner = piece.faces.ravel()
        m = mesh(piece.verts[piece.faces], piece.slots, (piece.verts, piece.faces), bones[corner], weights[corner])
        nodes.append({"name": f"{name}_{piece_name}", "mesh": m, "skin": 0})
        nodes[rig]["children"].insert(0, len(nodes) - 1)

    for bone, (tri, _, _) in (weapons or {}).items():
        side = "R" if bone.endswith(".r") else "L"
        nodes.append({"name": f"{name}_Weapon_{side}", "mesh": mesh(np.asarray(tri, float), weapon_slots[bone]),
                      "scale": [1.0 / scale] * 3, **WEAPON_SLOTS[bone]})
        nodes[index[bone]].setdefault("children", []).append(len(nodes) - 1)

    if abs(scale - 1.0) > 1e-6:
        nodes[rig]["scale"] = [float(scale)] * 3

    # skin and clips keep their template data
    skin["inverseBindMatrices"] = w.copy(skin["inverseBindMatrices"])
    for anim in g.get("animations", []):
        for s in anim["samplers"]:
            s["input"], s["output"] = w.copy(s["input"]), w.copy(s["output"])

    png = io.BytesIO()
    atlas.image.save(png, "PNG")
    g["images"] = [{"name": f"{name.lower()}_texture", "mimeType": "image/png", "bufferView": w._view(png.getvalue())}]
    g["textures"] = [{"sampler": 0, "source": 0}]
    g["materials"] = []
    for i, spec in enumerate(material_specs):
        spec = dict(spec or {})
        pbr = dict(base["pbrMetallicRoughness"], baseColorTexture={"index": 0})
        mat = dict(base, name=f"{name.lower()}_material" + (f"_{i}" if i else ""), pbrMetallicRoughness=pbr)
        if "metallic" in spec:
            pbr["metallicFactor"], pbr["roughnessFactor"] = spec["metallic"], spec["roughness"]
        if spec.get("emissive"):
            mat["emissiveTexture"], mat["emissiveFactor"] = {"index": 0}, [1.0, 1.0, 1.0]
        g["materials"].append(mat)
    g["asset"] = dict(g.get("asset", {}), generator=GENERATOR)
    w.save(path)
    return float(max(p.verts[:, 1].max() for p in pieces.values()))


def _worlds(g):
    """Every node's rest world matrix."""
    from accessories.sculpts import _node_worlds
    return _node_worlds(g)


def import_settings(glb, template="skeleton"):
    """
    Write `glb`'s Godot `.import` with the template's import parameters: the legacy glTF naming (props become bones
    with `BoneAttachment3D`s of their own names, as `CharacterClass.AttachmentNodeNames` expect) and the template's
    looping clips (glTF has no loop flag; KayKit's `.import`s set `Idle`, `Walking_B`... to loop). An existing
    file keeps its uid and paths; Godot fills them in on the first import otherwise.
    """
    src = TEMPLATES.get(template, template) + ".import"
    text = open(src, encoding="utf-8").read()
    params = text[text.index("[params]"):]
    looping, clip, depth = [], None, 0
    sub = params[params.index("_subresources={"):params.index("\ngltf/")]
    for line in sub.splitlines()[1:]:
        s = line.strip()
        if s.endswith("{"):
            depth += 1
            if depth == 2:
                clip = s.split('"')[1]
        elif s.startswith("}"):
            depth -= 1
        elif s.startswith('"settings/loop_mode"') and not s.rstrip(",").endswith(" 0") and depth == 2:
            looping.append((clip, s.rstrip(",").split(":")[1].strip()))
    loops = ",\n".join(f'"{c}": {{\n"settings/loop_mode": {m}\n}}' for c, m in looping)
    params = params.replace(sub, '_subresources={\n"animations": {\n' + loops + "\n}\n}")
    target = glb + ".import"
    head = '[remap]\n\nimporter="scene"\nimporter_version=1\ntype="PackedScene"\n\n'
    if os.path.exists(target):
        old = open(target, encoding="utf-8").read()
        head = old[:old.index("[params]")]
    with open(target, "w", encoding="utf-8") as f:
        f.write(head + params)
    return [c for c, _ in looping]
