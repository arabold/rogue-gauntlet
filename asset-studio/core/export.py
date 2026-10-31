"""
Kit characters as drop-in standard GLBs. The game expects the standard exact structure (`player.tscn`, enemy scenes,
`CharacterModel`): the `Rig` armature with its 41 joints, the standard clips baked into the model's own AnimationPlayer,
and props as nodes parented to bones (Godot imports them as `BoneAttachment3D`s, which `CharacterClass`
`AttachmentNodeNames` look up by name). So a character is not exported from scratch: a standard GLB (`TEMPLATES`) is
copied with its skeleton, skin, clips and prop nodes untouched, and only the meshes are ours:

- every kit piece becomes a mesh skinned to the template's skin, weights copied from the standard mannequin
  (`rig.transfer_weights`); positions stay in the rig's bind space, so size and orientation match standard exactly;
- headgear and capes go into the template's hat and cape prop nodes, rigid on `head` and `chest` like the standard
  (or are skinned like the rest where the template has no such node);
- the template's own props are emptied (the nodes stay, so equipment still finds its attachment point); hero
  templates keep their prop names (the class resources look them up), monster templates' are renamed after the
  character;
- weapons go into new nodes on `handslot.r` / `handslot.l`, placed like the player's equipment and counter-scaled
  by the rig scale, so a goblin's sword keeps its true size;
- `scale` sizes the whole character through the `Rig` node, which no clip animates;
- colors are one small palette texture, a flat swatch per material slot (`monster_mesh.Atlas`), and slots that are
  metal or glow get their own materials (`MATERIALS`; weapons keep each surface's metallic and roughness); normals
  are smooth within `crease` degrees and hard across sharper edges, the standard soft low-poly look.
"""

import io
import json
import os
import struct

import numpy as np

from core.rig import body_skin, transfer_weights
from core import mesh as mm

STUDIO_DIR = os.path.normpath(os.path.join(os.path.dirname(__file__), ".."))
REPO = os.path.normpath(os.path.join(STUDIO_DIR, ".."))
TEMPLATES = {name: os.path.join(STUDIO_DIR, "rigs", "templates", f"{name}.glb") for name in ("barbarian", "knight", "mage", "rogue", "rogue_hooded", "skeleton")}
# Surface looks and shading per material slot (everything else stays the standard matte default): metal shines, glows glow.
DEFAULT_MATERIALS = MATERIALS = {**{slot: dict(metallic=0.5, roughness=0.3, crease=45.0)
                for slot in ("gold", "gold_light", "gold_worn", "gold_dark", "metal", "metal_light", "metal_worn",
                             "metal_dark", "tarnish", "rivet")},
             **{slot: dict(metallic=0.45, roughness=0.4, crease=48.0)
                for slot in ("armor", "armor_light", "armor_worn", "armor_dark", "armor_lame", "armor_joint", "trim")},
             **{slot: dict(metallic=0.35, roughness=0.6, crease=48.0)
                for slot in ("iron", "iron_light", "iron_worn", "iron_dark", "rivet_iron")},
             **{slot: dict(metallic=0.25, roughness=0.6, crease=50.0) for slot in ("rust", "rust_dark")},
             **{slot: dict(emissive=True, crease=60.0) for slot in ("glow", "halo", "crystal")},
             **{slot: dict(metallic=0.08, roughness=0.10, crease=75.0) for slot in ("eye", "iris")},
             **{slot: dict(metallic=0.0, roughness=0.85, crease=50.0)
                for slot in ("bone", "bone_light", "bone_worn", "bone_dark", "tusk", "tusk_tip", "horn",
                             "leather", "leather_light", "leather_worn", "leather_dark", "stitch",
                             "hide", "hide_light", "hide_worn", "hide_dark", "dirt", "grime", "rope")},
             **{slot: dict(metallic=0.0, roughness=0.9, crease=62.0)
                for slot in ("skin", "skin_dark", "skin_light", "ear", "ear_dark", "lip", "mouth", "nostril", "socket", "ring", "makeup")},
             # hair, fur, feathers, wraps and cloth stay matte and shade smoothly along loops/folds:
             **{slot: dict(metallic=0.0, roughness=1.0, crease=60.0)
                for slot in ("brow", "hair", "beard", "fur", "fur_trim", "pelt", "pelt_dark", "feather",
                             "heraldry", "wrap", "wrap_dark", "cloth", "cloth_light", "cloth_worn", "cloth_dark", "rag",
                             "cloak", "cloak_dark")}}
# The same surface response as the approved dragon-scale shield.
MATERIALS.update({slot:dict(metallic=.12,roughness=.64,crease=48.0)
                  for slot in ("dragon","dragon_light","dragon_dark")})
MATERIALS.update({slot:dict(metallic=.45,roughness=.5,crease=55.0) for slot in ("mail","mail_dark")})
MATERIALS.update({slot:dict(MATERIALS["armor"]) for slot in ("steel_scale","steel_scale_light","steel_scale_dark")})
HEROES = ("barbarian", "knight", "mage", "rogue")
GENERATOR = "Rogue Gauntlet asset-studio"  # glTF asset.generator of every export (`character_kit` backs up others)  # templates whose prop node names the class resources look up
# where standard grips props: weapons in the right hand turned about Y, off-hand ones as they come (`Barbarian.glb`)
WEAPON_SLOTS = {"handslot.r": dict(translation=[0.0, 0.0333, 0.0], rotation=[0.0, 1.0, 0.0, 0.0]),
                "handslot.l": dict(translation=[0.0, 0.0333, 0.0])}


def _crease_of(spec, default=50.0):
    if not isinstance(spec, dict):
        return default
    if spec.get("shading") == "flat":
        return 0.0
    if spec.get("shading") == "smooth":
        return 180.0
    return float(spec.get("crease", default))


def corner_normals(verts, faces, crease=50.0, slots=None, slot_crease=None):
    """Per-corner normals (n faces, 3, 3): each the area-weighted mean of the faces around its point that turn less
    than their material's `crease` degrees from its own face, so curved surfaces shade smooth and sharp edges stay hard."""
    welded = np.unique(np.round(verts * 1e5).astype(np.int64), axis=0, return_inverse=True)[1].ravel()
    F = welded[faces]
    tri = verts[faces]
    fn = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    unit = fn / np.maximum(np.linalg.norm(fn, axis=1, keepdims=True), 1e-12)
    order = np.argsort(F.ravel(), kind="stable")
    corner_v = F.ravel()[order]
    cuts = np.flatnonzero(np.diff(corner_v)) + 1
    out = np.zeros((len(faces) * 3, 3))
    if slots is not None and slot_crease:
        creases = np.array([slot_crease.get(str(s), crease) for s in slots], float)
        cos_face = np.cos(np.radians(creases))
    else:
        cos_face = np.full(len(faces), np.cos(np.radians(crease)), float)
    for group in np.split(order, cuts):
        f = group // 3
        thresh = np.maximum(cos_face[f, None], cos_face[None, f])
        near = (unit[f] @ unit[f].T) >= thresh
        out[group] = near.astype(float) @ fn[f]
    out = out.reshape(-1, 3, 3)
    return out / np.maximum(np.linalg.norm(out, axis=2, keepdims=True), 1e-12)


class _Writer:
    """A new GLB built from a template: template accessors are copied over on demand, new data appended."""

    def __init__(self, template=None):
        if template is None:
            self.src, self.src_bin = {}, b""
            self.g = {"asset": {"version": "2.0", "generator": GENERATOR}, "scene": 0,
                      "scenes": [{"nodes": [0]}], "nodes": [{"name": "Item", "mesh": 0}],
                      "meshes": [], "accessors": [], "bufferViews": []}
            self.bin, self._views, self._accessors = bytearray(), {}, {}
            return
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


def resolve_palette(used, colors, looks=None):
    """Derive intermediate tints (_mid, _light, _dark, _worn) and organic ramps (ear, tusk, lip)."""
    slot_colors = dict(colors)
    resolved_looks = dict(looks or {})
    for s in list(used):
        if s not in slot_colors and s.endswith("_mid"):
            base_s = s[:-4]
            if base_s in slot_colors and "skin" in slot_colors:
                c1, c2 = np.array(slot_colors[base_s], float), np.array(slot_colors["skin"], float)
                slot_colors[s] = tuple(np.round(0.5 * c1 + 0.5 * c2).astype(int))
            elif base_s in slot_colors:
                slot_colors[s] = slot_colors[base_s]
            if s not in resolved_looks:
                resolved_looks[s] = dict(resolved_looks.get(base_s, {}))
        elif s not in slot_colors and s.endswith("_light"):
            base_s = s[:-6]
            if base_s in slot_colors:
                c = np.array(slot_colors[base_s], float)
                slot_colors[s] = tuple(np.round(np.clip(c * np.array([1.15, 1.14, 1.12]) + 8.0, 0, 255)).astype(int))
            if s not in resolved_looks:
                resolved_looks[s] = dict(resolved_looks.get(base_s, {}))
        elif s not in slot_colors and s.endswith("_worn"):
            base_s = s[:-5]
            if base_s in slot_colors:
                c = np.array(slot_colors[base_s], float)
                slot_colors[s] = tuple(np.round(np.clip(c * np.array([0.84, 0.81, 0.76]), 0, 255)).astype(int))
            if s not in resolved_looks:
                resolved_looks[s] = dict(resolved_looks.get(base_s, {}))
        elif s not in slot_colors and s.endswith("_dark"):
            base_s = s[:-5]
            if base_s in slot_colors:
                c = np.array(slot_colors[base_s], float)
                patina = np.clip(c * np.array([0.65, 0.58, 0.48]), 0, 255)
                slot_colors[s] = tuple(np.round(patina).astype(int))
            if s not in resolved_looks:
                resolved_looks[s] = dict(resolved_looks.get(base_s, {}))
    if "ear" in used and "skin" in slot_colors:
        ear_col = slot_colors.get("ear", slot_colors.get("skin_dark", slot_colors["skin"]))
        canal_col = slot_colors.get("ear_dark", slot_colors.get("canal", (
            int(ear_col[0] * 0.72), int(ear_col[1] * 0.68), int(ear_col[2] * 0.62)
        )))
        slot_colors["ear"] = ear_col
        resolved_looks["ear"] = {**resolved_looks.get("ear", {}), "ramp": (slot_colors["skin"], canal_col, ear_col)}
    if "tusk" in used and "tusk_tip" in slot_colors:
        resolved_looks["tusk"] = {**resolved_looks.get("tusk", {}), "ramp": (slot_colors["tusk_tip"], slot_colors["tusk"])}
    if "lip" in used and "skin" in slot_colors:
        mouth_color = slot_colors.get("lip", (54, 44, 34))
        skin_color = slot_colors["skin"]
        resolved_looks["lip"] = {**resolved_looks.get("lip", {}), "ramp": (mouth_color, skin_color)}
    return slot_colors, resolved_looks


def gradient_coordinates(triangles, normals):
    """Shared character/item ramp coordinates from vertex height and surface slope."""
    tri = np.asarray(triangles)
    y_min, y_max = tri[:, :, 1].min(), tri[:, :, 1].max()
    y_span = max(y_max - y_min, 1e-4)
    h_weight = float(np.clip(y_span / 0.08, 0.25, 0.85))
    t_h = 1.0 - (tri[:, :, 1] - y_min) / y_span
    t_h_centered = 0.5 + (t_h - 0.5) * h_weight
    t_slope = np.clip(-np.asarray(normals).reshape(-1, 3, 3)[:, :, 1] * 0.35, -0.25, 0.25)
    return np.clip(t_h_centered + t_slope, 0.06, 0.94)


def surface_material(base, name, spec):
    """The same palette texture, PBR, and emission definition for static items and skinned characters."""
    spec = dict(spec or {})
    pbr = dict(base["pbrMetallicRoughness"], baseColorTexture={"index": 0})
    mat = dict(base, name=name, pbrMetallicRoughness=pbr)
    if "metallic" in spec:
        pbr["metallicFactor"], pbr["roughnessFactor"] = spec["metallic"], spec["roughness"]
    if spec.get("emissive"):
        mat["emissiveTexture"], mat["emissiveFactor"] = {"index": 0}, [1.0, 1.0, 1.0]
    if "alpha" in spec:
        alpha = float(spec["alpha"])
        if not 0 <= alpha <= 1:
            raise ValueError("Material alpha must be between zero and one")
        pbr["baseColorFactor"] = [1, 1, 1, alpha]
        if alpha < 1:
            mat["alphaMode"] = "BLEND"
    if spec.get("double_sided"):
        mat["doubleSided"] = True
    return mat


def export_static(path, name, piece, colors, materials=None, crease=50.0, parts=None):
    """Export an item through the character surface model, without a skeleton or template geometry.

    `parts` adds further named nodes, `{node name: piece}`, sharing this file's palette and materials. They stay
    separate scene nodes so the game can show or hide them on their own, such as a fountain's liquid.
    """
    w = _Writer()
    nodes = [("Item", name, piece)] + [(node, node, part) for node, part in (parts or {}).items()]
    used = sorted({str(s) for _, _, p in nodes for s in p.slots})
    palette, looks = resolve_palette(used, colors, {**DEFAULT_MATERIALS, **(materials or {})})
    atlas = mm.Atlas([palette[s] for s in used], pad=4, col_w=16, col_h=128, looks=looks, slot_names=used)
    indices = {s: i for i, s in enumerate(used)}
    slot_crease = {s: _crease_of(looks.get(s), crease) for s in used}
    # Surface groups follow physical shading, independent of the palette's color count, and are shared by every node.
    specs = []
    w.g["meshes"], w.g["nodes"] = [], []
    for node, mesh_name, part in nodes:
        tri = part.verts[part.faces]
        if part.normals is not None:
            normals = np.asarray(part.normals, float)
            if normals.shape != tri.shape or not np.isfinite(normals).all() or np.any(np.linalg.norm(normals, axis=2) < 1e-8):
                raise ValueError("Authored static corner normals must be finite, nonzero and match the triangles")
            normals = normals / np.linalg.norm(normals, axis=2, keepdims=True)
        else:
            normals = corner_normals(part.verts, part.faces, crease=crease, slots=part.slots, slot_crease=slot_crease)
        t = gradient_coordinates(tri, normals)
        for i, slot in enumerate(part.slots):
            if looks.get(str(slot), {}).get("emissive") or str(slot) in {"paint", "gem", "stitch", "rivet"}:
                t[i] = .5
        labels = np.repeat(np.array([indices[str(s)] for s in part.slots])[:, None], 3, axis=1)
        uv = atlas.gradient_uv(labels, t)
        attrs = {"POSITION": w.add(tri.reshape(-1, 3), "VEC3", 5126, 34962, bounds=True),
                 "NORMAL": w.add(normals.reshape(-1, 3), "VEC3", 5126, 34962),
                 "TEXCOORD_0": w.add(uv, "VEC2", 5126, 34962)}
        groups = []
        for slot in part.slots:
            spec = looks.get(str(slot), {})
            shading = {key: spec[key] for key in ("metallic", "roughness", "emissive", "alpha", "double_sided") if key in spec}
            if shading not in specs:
                specs.append(shading)
            groups.append(specs.index(shading))
        f = np.arange(len(tri) * 3).reshape(-1, 3)
        primitives = []
        for i in sorted(set(groups)):
            index = w.add(f[np.array(groups) == i].ravel(), "SCALAR", 5125, 34963)
            primitives.append({"attributes": attrs, "indices": index, "material": i})
        w.g["meshes"].append({"name": mesh_name, "primitives": primitives})
        w.g["nodes"].append({"name": node, "mesh": len(w.g["meshes"]) - 1})
    w.g["scenes"] = [{"nodes": list(range(len(w.g["nodes"])))}]
    png = io.BytesIO()
    atlas.image.save(png, "PNG")
    w.g["images"] = [{"name": "surface_palette", "mimeType": "image/png", "bufferView": w._view(png.getvalue())}]
    w.g["samplers"] = [{"magFilter": 9729, "minFilter": 9729, "wrapS": 33071, "wrapT": 33071}]
    w.g["textures"] = [{"source": 0, "sampler": 0}]
    base = {"pbrMetallicRoughness": {"metallicFactor": 0, "roughnessFactor": .5}}
    w.g["materials"] = [surface_material(base, f"{name}_{i}", spec) for i, spec in enumerate(specs)]
    w.save(path)


def export(path, name, pieces, colors, template="skeleton", scale=1.0, hat=None, cape=None, weapons=None,
           materials=None, crease=50.0):
    """
    Write the character `name` to `path` as a standard drop-in on the `template` GLB (a `TEMPLATES` key or a path).
    `pieces`: `{piece name: kit.Piece}` in the rest pose, skinned by their names (`Body`, `ArmLeft`...; anything else
    follows the nearest part of the mannequin); `hat` / `cape`: pieces placed rigidly in the template's hat and cape
    prop nodes; `weapons`: `{"handslot.r" | "handslot.l": (verts (n, 3, 3) triangles in the item's own frame,
    their colors (n, 3), their metallic and roughness (n, 2))}`; `colors`: slot -> RGB; `materials`: slot ->
    `dict(metallic, roughness)` or `dict(emissive=True)` over `MATERIALS`. Returns the bind-pose height of the
    character, unscaled. A piece may declare `bone` for an explicit rigid attachment (plate armor); otherwise
    weights are transferred from the mannequin. A rigid piece may also declare `hinge` (parent, follow,
    rotation share and mount pivot); its extra joint and baked clip channels are exported automatically.
    Unknown attachment bones fail validation.
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
    from core import hinges
    hinge_specs = hinges.specifications(pieces)
    hinge_source = None
    if hinge_specs:
        from core.rig import Rig
        hinge_source = Rig(TEMPLATES.get(template,template))
        hinges.install(w,hinge_source,hinge_specs)
        joint_of = {nodes[n]["name"]:j for j,n in enumerate(skin["joints"])}

    # palette: every slot any piece, prop or weapon uses; a weapon's slots are its colors with their materials
    raw_colors, raw_looks = dict(colors), {**MATERIALS, **(materials or {})}
    weapon_slots = {}
    for i, (bone, (_, rgb, mr)) in enumerate((weapons or {}).items()):
        weapon_slots[bone] = []
        for c, (m, r) in zip(np.asarray(rgb, np.uint8), np.asarray(mr, float)):
            key = f"weapon{i}:{tuple(int(v) for v in c)}:{m:.2f}:{r:.2f}"
            raw_colors[key], raw_looks[key] = tuple(int(v) for v in c), dict(metallic=float(m), roughness=float(r))
            weapon_slots[bone].append(key)
    used = sorted({str(s) for p in [*pieces.values(), hat, cape] if p is not None for s in p.slots} |
                  {k for k in raw_colors if k.startswith("weapon")})
    slot_colors, looks = resolve_palette(used, raw_colors, raw_looks)
    missing = [s for s in used if s not in slot_colors]
    if missing:
        raise ValueError(f"no color for slots {missing}")
    label = {s: i for i, s in enumerate(used)}
    atlas = mm.Atlas([slot_colors[s] for s in used], pad=4, col_w=16, col_h=128, looks=looks, slot_names=used)
    base = g["materials"][0]
    material_specs = [None]  # 0: the template's matte material

    FLAT_SLOTS = {
        'paint', 'warpaint', 'scar', 'scars',
        'eye', 'iris', 'glint', 'pupil', 'bead', 'rings', 'makeup',
        'nostril', 'nostrils',
        'teeth', 'tooth',
        'rivet', 'rivets', 'buckle', 'buckles', 'stud', 'studs',
        'nail', 'gem', 'stitch',
    }

    def material(slot):
        spec = looks.get(str(slot))
        key = None if spec is None else tuple(sorted(spec.items()))
        if key not in material_specs:
            material_specs.append(key)
        return material_specs.index(key)

    def mesh(tri, slots, normals_from=None, bones=None, weights=None, uv_t=None):
        """A glTF mesh of triangles `tri` (n, 3, 3) with a slot per face; skinned when `bones` are given."""
        verts, faces = tri.reshape(-1, 3), np.arange(3 * len(tri)).reshape(-1, 3)
        if isinstance(normals_from, np.ndarray):
            N = normals_from.reshape(-1, 3)
        else:
            slot_crease = {s: _crease_of(looks.get(str(s)), crease) for s in used}
            N = corner_normals(*(normals_from or (verts, faces)), crease=crease, slots=slots, slot_crease=slot_crease).reshape(-1, 3)
        
        n_faces = len(tri)
        if n_faces == 0:
            uv = np.zeros((0, 2), float)
        else:
            t_val = gradient_coordinates(tri, N)

            # Check which slots are flat details
            is_flat = np.array([
                (str(s) in FLAT_SLOTS or
                 any(str(s).startswith(f"{p}_") or str(s).endswith(f"_{p}") for p in FLAT_SLOTS) or
                 looks.get(str(s), {}).get("emissive", False))
                for s in slots
            ], dtype=bool)

            t_val[is_flat] = 0.5

            if uv_t is not None:
                has_uv_t = np.isfinite(uv_t)
                if has_uv_t.any():
                    t_val = np.where(has_uv_t, uv_t, t_val)

            is_tusk = np.array([str(s) == "tusk" for s in slots], dtype=bool)
            if is_tusk.any():
                for sgn in [-1, 1]:
                    side_sel = is_tusk & ((tri[:, :, 0].mean(1) * sgn) > 0)
                    if side_sel.any():
                        y_side = tri[side_sel, :, 1]
                        y0, y1 = y_side.min(), y_side.max()
                        p_tusk = np.clip((y_side - y0) / max(y1 - y0, 1e-4), 0.0, 1.0)
                        t_val[side_sel] = np.clip(1.0 - p_tusk, 0.05, 0.95)

            is_lip = np.array([str(s) == "lip" for s in slots], dtype=bool)
            if is_lip.any():
                lip_tri = tri[is_lip]
                vx = lip_tri[:, :, 0]
                vy = lip_tri[:, :, 1]
                y_top = vy.max()
                y_bot = vy.min()
                y_span = max(y_top - y_bot, 1e-4)
                x_max = max(np.abs(vx).max(), 1e-4)
                y_c = y_top - 0.42 * y_span + 0.008 * (vx / x_max)**2 * (y_span / 0.04)
                dy = np.abs(vy - y_c)
                x_norm = np.clip(np.abs(vx) / x_max, 0.0, 1.0)
                h_local = np.maximum(0.55 * y_span * np.clip(1.0 - x_norm**2, 0.08, 1.0)**0.6, 1e-4)
                dist = np.clip(dy / h_local, 0.0, 1.0)
                dist_s = dist * dist * (3.0 - 2.0 * dist)
                t_val[is_lip] = np.clip(0.06 + 0.88 * dist_s, 0.05, 0.95)

            label_indices = np.array([label[str(s)] for s in slots])
            lbl_expanded = np.repeat(label_indices[:, None], 3, axis=1)

            uv = atlas.gradient_uv(lbl_expanded, t_val)
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
        norms_prop = None
        if getattr(piece, "normals", None) is not None:
            norms_prop = piece.normals @ M[:3, :3].T
        nodes[node_i]["mesh"] = mesh(tri, piece.slots, norms_prop if norms_prop is not None else (piece.verts @ M[:3, :3].T + M[:3, 3], piece.faces), uv_t=getattr(piece, "uv_t", None))

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
        attachment = getattr(piece, "bone", None)
        if attachment is not None:
            if attachment not in joint_of:
                raise ValueError(f"{piece_name}: unknown attachment bone {attachment!r}")
            bones = np.full((len(piece.verts), 4), attachment, object)
            weights = np.zeros((len(piece.verts), 4))
            weights[:, 0] = 1.0
        else:
            bones, weights = transfer_weights(piece.verts, piece_name, faces=piece.faces, skin=own)
        corner = piece.faces.ravel()
        norms_body = piece.normals if getattr(piece, "normals", None) is not None else (piece.verts, piece.faces)
        m = mesh(piece.verts[piece.faces], piece.slots, norms_body, bones[corner], weights[corner], uv_t=getattr(piece, "uv_t", None))
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
    if not hinge_specs:
        skin["inverseBindMatrices"] = w.copy(skin["inverseBindMatrices"])
    for anim in g.get("animations", []):
        for s in anim["samplers"]:
            s["input"], s["output"] = w.copy(s["input"]), w.copy(s["output"])
    if hinge_specs:
        hinges.bake(w,hinge_source,hinge_specs)

    png = io.BytesIO()
    atlas.image.save(png, "PNG")
    g["images"] = [{"name": f"{name.lower()}_texture", "mimeType": "image/png", "bufferView": w._view(png.getvalue())}]
    g["textures"] = [{"sampler": 0, "source": 0}]
    g["materials"] = []
    for i, spec in enumerate(material_specs):
        mat = surface_material(base, f"{name.lower()}_material" + (f"_{i}" if i else ""), spec)
        g["materials"].append(mat)
    g["asset"] = dict(g.get("asset", {}), generator=GENERATOR)
    w.save(path)
    return float(max(p.verts[:, 1].max() for p in pieces.values()))


def _worlds(g):
    """Every node's rest world matrix."""
    from core.mesh import _node_worlds
    return _node_worlds(g)


def import_settings(glb, template="skeleton"):
    """
    Write `glb`'s Godot `.import` with the template's import parameters: the legacy glTF naming (props become bones
    with `BoneAttachment3D`s of their own names, as `CharacterClass.AttachmentNodeNames` expect) and the template's
    looping clips (glTF has no loop flag; the standard `.import`s set `Idle`, `Walking_B`... to loop). An existing
    file keeps its uid and paths; Godot fills them in on the first import otherwise.
    """
    src = TEMPLATES.get(template, template) + ".import"
    if not os.path.exists(src):
        raise ValueError(f"Missing Studio-local template import settings: {src}")
    text = open(src, encoding="utf-8").read() if os.path.exists(src) else ""
    if not text or "[params]" not in text:
        return []
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
