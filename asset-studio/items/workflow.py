"""Recipe-based item authoring, independent of gameplay balance and character rigs.

Backends converge on a staged GLB in external OS temporary storage. Only the explicit export
command writes finished 3D art to the game asset tree. Imported/Blender models keep their authored shading;
the legacy contour builder is an opt-in procedural backend for known archetypes.
"""

import argparse
import json
import math
import re
import shutil
import subprocess
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

from core.paths import work_root
from core import publication
from core.publication import digest
from items import align, model
from items.builders import BUILDERS
from items.designs import FAMILIES, COLORS, MATERIALS
from items import solid

STUDIO = Path(__file__).resolve().parents[1]
REPO = STUDIO.parent
OBJECTS = STUDIO / "objects"
PALETTE = STUDIO / "rigs/item_palette.png"
BACKENDS = ["source", "prefab", "procedural", "image3d"]
PLACEMENT = {
    "type": "object", "additionalProperties": False,
    "properties": {
        "pivot": {"enum": ["authored", "center", "ground", "grip"]},
        "scale": {"type": "number", "exclusiveMinimum": 0},
        **{key: {"type": "array", "minItems": 3, "maxItems": 3, "items": {"type": "number"}}
           for key in ["dimensions", "rotation_degrees", "grip", "offset"]},
    },
}
SCHEMA = {
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "title": "Asset Studio item recipe", "type": "object", "additionalProperties": False,
    "required": ["version", "name", "backend", "budget", "export"],
    "properties": {
        "version": {"type": "integer", "const": 1}, "name": {"type": "string", "minLength": 1},
        "backend": {"enum": BACKENDS}, "archetype": {"enum": sorted(align.ARCHETYPE_PROFILES)},
        "builder": {"enum": sorted(set(BUILDERS) | set(FAMILIES))},
        "design": {"type": "object"},
        "colors": {"type": "object"},
        "materials": {"type": "object"},
        "color_mode": {"enum": ["character", "vertex", "palette"]},
        "surface": {"type": "object", "additionalProperties": False, "required": ["palette"],
                    "properties": {"palette": {"enum": ["studio_grid"]}, "slots": {"type": "object"},
                                   "repair": {"enum": ["boundaries"]},
                                   "paint": {"enum": ["scroll_ink", "potion_liquid"]},
                                   "potion_size": {"enum": ["small", "medium", "large"]},
                                   "carve": {"enum": ["cheese_holes"]}}},
        "topology": {"enum": ["closed_assembly", "closed_surface", "authored_assembly"]},
        "crease": {"type": "number", "minimum": 0, "maximum": 180},
        "concept": {"type": "string"}, "reference": {"type": "string"},
        "source": {"type": "string"}, "blend": {"type": "string"},
        "provenance": {"type": "object"},
        "palette": {"type": "array", "minItems": 1, "items": {"enum": sorted(align.SWATCH_NAMES)}},
        "parts": {"type": "array", "minItems": 1, "items": {
            "type": "object", "additionalProperties": False, "required": ["mesh"],
            "properties": {
                "mesh": {"type": "string", "pattern": "^[a-z0-9_]+$"},
                "slot": {"enum": sorted(align.SWATCH_NAMES)},
                "scale": {"type": "number", "exclusiveMinimum": 0},
                "offset": {"type": "array", "minItems": 3, "maxItems": 3, "items": {"type": "number"}},
            },
        }},
        "placement": PLACEMENT,
        "budget": {"type": "object", "additionalProperties": False, "required": ["triangles"],
                   "properties": {key: {"type": "integer", "minimum": 1}
                                  for key in ["triangles", "materials", "texture_size"]}},
        "export": {"type": "object", "additionalProperties": False, "required": ["glb"],
                   "properties": {"glb": {"type": "string"}}},
    },
}


def check(value, spec, label="recipe"):
    """Validate the small JSON schema subset used here without an extra dependency."""
    types = {"object": dict, "array": list, "string": str, "number": (int, float), "integer": int}
    kind = spec.get("type")
    if kind and (not isinstance(value, types[kind]) or kind in ("integer", "number") and isinstance(value, bool)):
        raise ValueError(f"{label}: expected {kind}")
    if isinstance(value, (float, int)) and not math.isfinite(value):
        raise ValueError(f"{label}: must be finite")
    if "const" in spec and value != spec["const"] or "enum" in spec and value not in spec["enum"]:
        raise ValueError(f"{label}: unsupported value {value!r}")
    if kind == "object":
        missing = set(spec.get("required", [])) - value.keys()
        unknown = value.keys() - spec.get("properties", {}).keys() if spec.get("additionalProperties") is False else set()
        if missing or unknown:
            raise ValueError(f"{label}: missing {sorted(missing)}, unknown {sorted(unknown)}")
        for key, val in value.items():
            if key in spec.get("properties", {}):
                check(val, spec["properties"][key], f"{label}.{key}")
    if kind == "array":
        if not spec.get("minItems", 0) <= len(value) <= spec.get("maxItems", math.inf):
            raise ValueError(f"{label}: wrong array length")
        for val in value:
            check(val, spec["items"], label)
    if kind == "string" and (len(value) < spec.get("minLength", 0) or "pattern" in spec and not re.fullmatch(spec["pattern"], value)):
        raise ValueError(f"{label}: invalid string")
    if kind in ("number", "integer") and (value < spec.get("minimum", -math.inf) or value <= spec.get("exclusiveMinimum", -math.inf)
                                         or value > spec.get("maximum", math.inf)):
        raise ValueError(f"{label}: number outside allowed range")


def save(path, data):
    """Persist recipes and reports in a stable human-editable format."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2, allow_nan=False) + "\n")


def within(path, root):
    """Resolve and restrict outputs, including symlinks, to their intended content tree."""
    result = path.resolve()
    if not result.is_relative_to(root.resolve()):
        raise ValueError(f"Path must be inside {root}: {path}")
    return result


class Item:
    """A category/name recipe and its studio-only build products."""

    def __init__(self, key):
        if not re.fullmatch(r"[a-z0-9_]+/[a-z0-9_]+", key):
            raise ValueError("Item key must be category/name using lowercase letters, digits, underscores")
        self.key = key
        self.category, self.id = key.split("/")
        self.folder = within(OBJECTS / key, OBJECTS)
        self.path = self.folder / "item.json"
        self.built = work_root(STUDIO) / "items" / key / f"{self.id}.glb"

    def resolve(self, value):
        """Sources are recipe-local or explicit res:// repository paths, never machine-specific paths."""
        if value.startswith("res://"):
            return within(REPO / value[6:], REPO)
        if Path(value).is_absolute():
            raise ValueError("Use recipe-local paths or res:// repository paths")
        return within(self.folder / value, self.folder)

    def recipe(self):
        data = json.loads(self.path.read_text())
        check(data, SCHEMA)
        backend = data["backend"]
        if data.get("surface") and (backend != "source" or data.get("color_mode", "character") != "character"):
            raise ValueError("Source surface conversion requires the source backend and character shading")
        if data.get("topology") == "authored_assembly" and not (backend == "source" and data.get("surface", {}).get("repair")):
            raise ValueError("Authored surface exceptions require explicit repaired source conversion")
        required = {"source": "source", "prefab": "parts"}
        if backend in required and not data.get(required[backend]):
            raise ValueError(f"{self.key}: {backend} requires {required[backend]}")
        if backend == "procedural" and not data.get("builder") and not data.get("archetype"):
            raise ValueError("Procedural recipes require a builder or archetype")
        if data.get("builder") in FAMILIES and data.get("color_mode", "character") != "character":
            raise ValueError("Solid-family recipes use the shared character surface model")
        placement = data.get("placement", {})
        if placement.get("dimensions") and any(v <= 0 for v in placement["dimensions"]):
            raise ValueError("dimensions must be positive")
        for key in ("source", "reference", "blend"):
            if data.get(key):
                self.resolve(data[key])
        self.destination(data)
        return data

    def destination(self, data):
        value = data["export"]["glb"]
        if not value.startswith("res://") or not value.endswith(".glb"):
            raise ValueError("export.glb must be a res:// GLB path")
        # World props such as fountains share the item pipeline but are not inventory items.
        root = "assets/custom-props" if value.startswith("res://assets/custom-props/") else "assets/custom-items"
        return within(REPO / value[6:], REPO / root)

    def new(self, backend="source", archetype=None, source=None, builder=None):
        """Create a recipe without replacing existing work or writing to the game tree."""
        if self.path.exists():
            raise ValueError(f"Recipe already exists: {self.path}")
        if backend == "procedural" and not archetype and not builder:
            raise ValueError("Procedural recipes need --builder or --archetype")
        data = {
            "version": 1, "name": self.id.replace("_", " ").title(), "backend": backend,
            "concept": "", "reference": "reference.png",
            "placement": {"pivot": "authored", "scale": 1.0},
            "budget": {"triangles": 1500},
            "export": {"glb": f"res://assets/custom-items/{self.key}.glb"},
        }
        if archetype:
            data["archetype"] = archetype
        if builder:
            data["builder"] = builder
        if backend == "source":
            data["source"] = "source.glb"
            data["blend"] = "source.blend"
        elif backend == "prefab":
            data["parts"] = [{"mesh": "flask_potion", "slot": "teal", "scale": 1.0}]
        check(data, SCHEMA)
        if source:
            model.read(source)
            data["backend"] = "source"
            data["source"] = "source.glb"
            self.folder.mkdir(parents=True, exist_ok=True)
            if (self.folder / "source.glb").exists():
                raise ValueError("source.glb already exists")
            shutil.copy2(source, self.folder / "source.glb")
        save(self.path, data)
        print(self.path)

    def reference(self, brief=None, provider="gemini", image=None, prompt_only=False):
        """Import a concept (including ImageGen output) or explicitly request a provider call."""
        data = self.recipe()
        from core.prompts import item_prompt, style_images, STYLE_REFERENCES
        if prompt_only:
            if image:
                raise ValueError("--prompt-only cannot be combined with --image")
            print(json.dumps({"prompt": item_prompt(brief or data.get("concept")),
                              "referenced_image_paths": [str(p) for p in STYLE_REFERENCES],
                              "aspect": "1:1"}, indent=2))
            return
        target = self.resolve(data.get("reference", "reference.png"))
        within(target, self.folder)
        if image:
            with Image.open(image) as im:
                im.save(target, format="PNG")
        else:
            from core.ai import get_provider
            brief = brief or data.get("concept")
            prompt = item_prompt(brief)
            get_provider(provider).edit_image(prompt, style_images(), aspect="1:1").save(target)
            target.with_suffix(".prompt.txt").write_text(prompt + "\n")
        if brief:
            data["concept"] = brief
        data["reference"] = str(target.relative_to(self.folder))
        save(self.path, data)
        print(target)

    def build(self):
        """Build locally with one explicit backend; failed AI calls never substitute a different asset."""
        data = self.recipe()
        backend = data["backend"]
        self.built.parent.mkdir(parents=True, exist_ok=True)
        raw = self.built.parent / "raw.glb"
        if backend == "source":
            source = self.resolve(data["source"])
            if not source.is_file():
                raise ValueError(f"Export a self-contained GLB from Blender to {source}")
            if data.get("surface"):
                from items.source_surface import palette_piece
                from core.export import export_static
                piece, colors = palette_piece(source, PALETTE, data["surface"].get("slots"))
                if data["surface"].get("paint"):
                    if data["surface"]["paint"] == "potion_liquid":
                        from items.source_surface import potion_liquid
                        piece, report = potion_liquid(piece, data["surface"].get("potion_size"))
                    else:
                        from items.surface_marks import scroll_ink
                        piece, report = scroll_ink(piece)
                    save(self.built.parent / "surface-marks.json", report)
                if data["surface"].get("repair"):
                    from items.source_repair import repair
                    piece, report = repair(piece)
                    save(self.built.parent / "source-repair.json", report)
                if data["surface"].get("carve"):
                    from items.cheese import carve_holes
                    piece, report = carve_holes(piece)
                    save(self.built.parent / "source-carving.json", report)
                export_static(str(raw), self.id, piece, {**colors, **data.get("colors", {})},
                              materials=data.get("materials"), crease=data.get("crease", 50))
                source = raw
        elif backend == "image3d":
            from core.ai import image_to_3d
            ref = self.resolve(data.get("reference", "reference.png"))
            if not ref.exists():
                raise ValueError("Create or import a reference first")
            with Image.open(ref) as im:
                image_to_3d([im.copy()], str(raw))
            source = raw
        else:
            palette = data.get("palette")
            if backend == "procedural" and data.get("builder") in FAMILIES:
                from core.export import export_static
                built_piece = FAMILIES[data["builder"]](data.get("design", {}))
                export_static(str(raw), self.id, built_piece, {**COLORS, **data.get("colors", {})},
                              materials={**MATERIALS, **data.get("materials", {})}, crease=data.get("crease", 50))
                source = raw
                model.place(source, self.built, data.get("placement", {}))
                return self.finish_build(data)
            elif backend == "procedural" and data.get("builder"):
                verts, faces, cols = BUILDERS[data["builder"]]()
            elif backend == "procedural":
                from items.concept import blockout
                ref = self.resolve(data.get("reference", "reference.png"))
                if not ref.exists():
                    # No provider calls during local builds. The silhouette is explicitly a blockout.
                    ref = self.built.parent / "blockout.png"
                    blockout(data["archetype"], str(ref), palette)
                verts, faces, cols = align.reconstruct_from_concept_image(
                    str(ref), data["archetype"], palette_names=palette, item_id=self.id)
            else:
                from core import kit
                from prefabs import library
                parts = [library.load(p["mesh"], slot=p.get("slot", "steel"), scale=p.get("scale", 1),
                                      offset=p.get("offset", [0, 0, 0])) for p in data["parts"]]
                piece = kit.Piece.merge(*parts)
                swatches = {s["cell"]: s["rgb"] for s in align.load_palette_swatches(str(PALETTE))}
                verts, faces = piece.verts.tolist(), piece.faces.tolist()
                cols = [swatches[align.SWATCH_NAMES[str(slot)]] for slot in piece.slots]
            # Procedural geometry also obeys the budget, unlike the old contour-only path.
            verts, faces, cols = align.decimate_mesh_grid(verts, faces, cols, data["budget"]["triangles"])
            if data.get("color_mode", "character") == "character":
                from core import kit
                from core.export import export_static
                if backend == "prefab":
                    # Joining meshes is not a union. Disconnected meshes must really intersect.
                    assembly = solid.Assembly()
                    for part in parts:
                        for slot in set(str(s) for s in part.slots):
                            if len(set(str(s) for s in part.slots)) > 1:
                                raise ValueError("Prefab solid union currently needs one slot per structural part")
                            assembly.add(part, slot)
                    built_piece = assembly.build()
                    colors = {str(slot): swatches[align.SWATCH_NAMES[str(slot)]] for slot in piece.slots}
                else:
                    palette_rgb, labels = np.unique(cols, axis=0, return_inverse=True)
                    names = np.array([f"surface_{i}" for i in range(len(palette_rgb))], dtype=object)
                    built_piece = kit.Piece(verts, faces, names[labels])
                    colors = {str(name): rgb for name, rgb in zip(names, palette_rgb)}
                export_static(str(raw), self.id, built_piece, {**colors, **data.get("colors", {})},
                              materials=data.get("materials"), crease=data.get("crease", 50))
            elif data["color_mode"] == "vertex":
                model.colored(raw, verts, faces, cols, crease=data.get("crease", 50))
            else:
                packed = align.map_faces_to_palette(verts, faces, cols, str(PALETTE), palette)
                align.write_item_glb(str(raw), *packed, str(PALETTE))
            source = raw
        model.place(source, self.built, data.get("placement", {}))
        result = self.finish_build(data)
        if backend in ("source", "procedural", "prefab") and raw.exists():
            raw.unlink()
        return result

    def finish_build(self, data):
        """All backends pass the same topology, budget, and preview gate."""
        report = self.validate()
        save(self.built.parent / "build.json", {"recipe_sha256": digest(self.path), "inputs": self.inputs(data),
             "model_sha256": digest(self.built), "report": report})
        publication.stage_stamp(self.built.parent, publication.fingerprint(STUDIO, self.path))
        print(self.built)

    def validate(self):
        """Enforce finite, nondegenerate geometry and the recipe's instance-aware triangle budget."""
        data = self.recipe()
        p, f, _ = model.geometry(self.built)
        errors = []
        if not np.isfinite(p).all():
            errors.append("non-finite vertex positions")
        area = np.linalg.norm(np.cross(p[f[:, 1]] - p[f[:, 0]], p[f[:, 2]] - p[f[:, 0]]), axis=1)
        if np.any(area <= 1e-12):
            errors.append(f"{int(np.sum(area <= 1e-12))} degenerate triangles")
        if len(f) > data["budget"]["triangles"]:
            errors.append(f"{len(f)} triangles exceeds budget {data['budget']['triangles']}")
        topology = solid.inspect(p, f)
        warnings = []
        if topology["components"] != 1 and data.get("topology", "closed_assembly") == "closed_surface":
            errors.append(f"{topology['components']} disconnected surface components; join with shared topology or a solid union")
        elif topology["components"] > 1:
            warnings.append(f"{topology['components']} surface components: visually review intended attachments and gaps; component count cannot establish floating parts")
        for key in ["boundary_edges", "nonmanifold_edges", "inconsistent_edges", "duplicate_faces"]:
            if topology[key]:
                if data.get("topology") == "authored_assembly" and key in ("boundary_edges", "nonmanifold_edges"):
                    warnings.append(f"Authored surface exception: {topology[key]} {key}; review the preserved original geometry")
                else:
                    errors.append(f"{topology[key]} {key}")
        if topology["signed_volume"] <= 0:
            errors.append("surface must enclose a positive oriented volume")
        g, chunks = model.read(self.built)
        materials = len(g.get("materials", []))
        if materials > data["budget"].get("materials", math.inf):
            errors.append(f"{materials} materials exceeds budget")
        binary = next((body for kind, body in chunks if kind == b"BIN\x00"), b"")
        textures = []
        for image in g.get("images", []):
            import io
            view = g["bufferViews"][image["bufferView"]]
            start = view.get("byteOffset", 0)
            with Image.open(io.BytesIO(binary[start:start + view["byteLength"]])) as im:
                textures.append(list(im.size))
                if max(im.size) > data["budget"].get("texture_size", math.inf):
                    errors.append(f"Texture {im.size} exceeds texture_size budget")
        report = {"triangles": len(f), "bounds": [p.min(axis=0).tolist(), p.max(axis=0).tolist()],
                  "dimensions": np.ptp(p, axis=0).tolist(), "materials": materials, "textures": textures,
                  "topology": topology, "warnings": warnings, "errors": errors}
        save(self.built.parent / "validation.json", report)
        if errors:
            raise ValueError(f"{self.key}: {'; '.join(errors)}")
        return report

    def inputs(self, data):
        """Track source edits so publishing cannot silently export an obsolete build."""
        paths = []
        if data["backend"] == "source":
            paths.append(self.resolve(data["source"]))
            if data.get("surface"):
                paths.extend([PALETTE, STUDIO / "items/source_surface.py", STUDIO / "items/model.py",
                              STUDIO / "items/align.py", STUDIO / "core/export.py", STUDIO / "core/mesh.py"])
                if data["surface"].get("repair"):
                    paths.append(STUDIO / "items/source_repair.py")
                if data["surface"].get("paint") == "scroll_ink":
                    paths.extend([STUDIO / "items/surface_marks.py", STUDIO / "items/solid.py"])
                if data["surface"].get("carve"):
                    paths.extend([STUDIO / "items/cheese.py", STUDIO / "items/solid.py"])
        elif data["backend"] in ("procedural", "image3d"):
            ref = self.resolve(data.get("reference", "reference.png"))
            if ref.exists():
                paths.append(ref)
        elif data["backend"] == "prefab":
            from prefabs import library
            paths.extend(library.find_prefab_path(part["mesh"]) for part in data["parts"])
        if data["backend"] in ("procedural", "prefab"):
            paths.append(PALETTE)
            paths.extend(STUDIO / "items" / name for name in ["designs.py", "builders.py", "solid.py", "bread.py", "arsenal.py", "shieldcraft.py", "fountains.py"])
            if data.get("builder") == "shieldcraft":
                paths.extend(STUDIO / name for name in ["prefabs/details/construction.py", "prefabs/details/wear.py", "items/heroic_shields.py", "prefabs/details/heraldry.py", "core/kit.py"])
            paths.append(STUDIO / "core/export.py")
            paths.append(STUDIO / "core/mesh.py")
        return {str(path.relative_to(REPO)): digest(path) for path in paths}

    def preview(self):
        """Render a multi-angle geometry/base-color study without changing any game content."""
        from core import catalog, kit
        p, f, cols = model.geometry(self.built)
        palette, labels = np.unique(cols, axis=0, return_inverse=True)
        names = [f"color_{i}" for i in range(len(palette))]
        piece = kit.Piece(p, f, np.array(names, dtype=object)[labels])
        colors = dict(zip(names, palette))
        panels = [catalog.render(piece, colors=colors, yaw=yaw, size=360, crease=0,
                                  center=(p.min(axis=0) + p.max(axis=0)) / 2,
                                  extent=float(np.ptp(p, axis=0).max()) * 1.7)
                  for yaw in [0, 35, 90, 180]]
        sheet = Image.new("RGB", (1440, 390), "white")
        for i, panel in enumerate(panels):
            sheet.paste(panel, (360 * i, 30))
            ImageDraw.Draw(sheet).text((360 * i + 12, 8), f"{self.key} | {[0, 35, 90, 180][i]} degrees", fill="black")
        sheet.save(self.built.parent / "preview.png")
        print(self.built.parent / "preview.png")

    def lineup(self, mount="ground"):
        """Render the staged art in Godot, including a gameplay-camera view beside/on a mannequin."""
        self.validate()
        outdir = self.built.parent / "lineup"
        outdir.mkdir(parents=True, exist_ok=True)
        result = run_godot(["--script", str(STUDIO / "godot/render_item_lineup.gd"), "--",
                           str(self.built), str(STUDIO / "rigs/mannequin_medium.glb"), str(outdir), mount], headless=False)
        if result.count("ITEM_SHOT ") != 4:
            raise ValueError(f"Godot did not render every lineup panel:\n{result}")
        self.compose_lineup(mount)
        shutil.rmtree(outdir)

    def compose_lineup(self, mount="ground"):
        """Compose verified Godot frames, shared by individual and batched rendering."""
        outdir = self.built.parent / "lineup"
        tiles = []
        data = self.recipe()
        reference = self.resolve(data.get("reference", "reference.png"))
        if reference.exists():
            tiles.append(("Concept", reference))
        tiles.extend((title, outdir / f"{name}.png") for name, title in [
            ("front", "Front"), ("three_quarter", "Three-quarter"), ("side", "Side"),
            ("game_context", f"Game camera / {mount.replace('_', ' ')}")])
        sheet = Image.new("RGB", (360 * len(tiles), 400), (20, 23, 30))
        draw = ImageDraw.Draw(sheet)
        for i, (title, path) in enumerate(tiles):
            with Image.open(path) as image:
                panel = image.convert("RGB")
                panel.thumbnail((360, 360), Image.Resampling.LANCZOS)
                sheet.paste(panel, (i * 360 + (360 - panel.width) // 2, 40 + (360 - panel.height) // 2))
            draw.text((i * 360 + 12, 8), title, fill="white")
        sheet.save(self.built.parent / "lineup.png")
        publication.render_stamp(self.built.parent, publication.fingerprint(STUDIO, self.path),
                                 (self.built.name, "lineup.png"))
        print(self.built.parent / "lineup.png")

    def publish(self):
        """Finalize a reviewed lineup without exporting or changing game resources."""
        report = self.validate()
        recipe = self.recipe()
        publication.publish(self.built.parent, STUDIO / "catalogs/items" / self.key, self.key,
            publication.fingerprint(STUDIO, self.path), (self.built.name, "lineup.png"),
            {"report": report, "recipe": recipe})

    def export(self, no_import=False, force=False):
        """Publish a validated current build, protecting unrelated pre-existing models."""
        data = self.recipe()
        self.validate()
        stamp = json.loads((self.built.parent / "build.json").read_text())
        if (stamp["recipe_sha256"] != digest(self.path) or stamp["model_sha256"] != digest(self.built)
                or stamp.get("inputs") != self.inputs(data)):
            raise ValueError("Recipe, source inputs, or staged model changed; run item build again before export")
        target = self.destination(data)
        same = target.exists() and digest(target) == digest(self.built)
        if data["backend"] == "source" and self.resolve(data["source"]) == target and not same:
            raise ValueError("Copy the adopted original to source.glb, set recipe.source to source.glb, and rebuild before replacing it")
        receipt = self.folder / ".export.json"
        previous_path = receipt if receipt.exists() else self.folder / "export.json"
        previous = json.loads(previous_path.read_text()) if previous_path.exists() else {}
        owned = previous.get("path") == data["export"]["glb"] and target.exists() and previous.get("sha256") == digest(target)
        if target.exists() and not same and not owned and not force:
            raise ValueError(f"Existing model differs: {target}; inspect it and use --force to replace")
        target.parent.mkdir(parents=True, exist_ok=True)
        self.import_settings(target)
        if not same:
            shutil.copy2(self.built, target)
        if not no_import:
            godot([data["export"]["glb"]])
        save(receipt, {"path": data["export"]["glb"], "sha256": digest(target), "godot_validated": not no_import})
        print(target)

    def has_vertex_material(self):
        """Only studio-generated vertex-color materials opt into the Godot importer correction."""
        g, _ = model.read(self.built)
        return any(m.get("extras", {}).get("asset_studio_vertex_color") for m in g.get("materials", []))

    def import_settings(self, target):
        """Preserve existing importer settings while enabling the studio's art-only color correction."""
        if not self.has_vertex_material():
            return
        path = Path(str(target) + ".import")
        text = path.read_text() if path.exists() else '[remap]\n\nimporter="scene"\ntype="PackedScene"\n\n[params]\n'
        setting = 'import_script/path="res://asset-studio/godot/import_item.gd"'
        match = re.search(r'^import_script/path="([^"]*)"$', text, re.MULTILINE)
        if match and match[1] not in ("", "res://asset-studio/godot/import_item.gd"):
            raise ValueError("Existing custom import script needs manual composition with studio surface colors")
        if match:
            text = text[:match.start()] + setting + text[match.end():]
        else:
            if "[params]" not in text:
                raise ValueError("Existing import metadata has no [params] section")
            text = text.replace("[params]", "[params]\n" + setting, 1)
        path.write_text(text)


def run_godot(args, headless=True):
    """Fail on engine diagnostics as well as process errors; imports can exit zero on resource errors."""
    command = [str(STUDIO / "godot/godot.sh"),
               *(["--headless"] if headless else []), "--path", str(REPO), *args]
    result = subprocess.run(command, capture_output=True, text=True, timeout=600)
    if result.returncode or any(marker in result.stdout + result.stderr for marker in ["ERROR:", "SCRIPT ERROR:", "Parse Error"]):
        raise ValueError(f"Godot validation failed:\n{result.stdout}\n{result.stderr}")
    return result.stdout


def godot(paths):
    """Import finished assets and prove each exported scene/resource loads."""
    run_godot(["--editor", "--import"])
    result = run_godot(["--script", str(STUDIO / "godot/validate_item.gd"), "--", *paths])
    if result.count("ITEM_OK ") != len(paths):
        raise ValueError(f"Godot did not validate every item:\n{result}")
    print(result.strip())


def adopt(path):
    """Register existing custom-item art without rebuilding it or changing any game resources."""
    path = within(path, REPO / "assets/custom-items")
    relative = path.relative_to(REPO / "assets/custom-items")
    category = "shields" if relative.parts[:2] == ("armor", "shield") else relative.parts[0]
    item = Item(f"{category}/{path.stem}")
    if item.path.exists():
        print(f"skip existing {item.key}")
        return
    p, f, _ = model.geometry(path)
    data = {"version": 1, "name": path.stem.replace("_", " ").title(), "backend": "source",
            "source": "res://" + path.relative_to(REPO).as_posix(),
            "placement": {"pivot": "authored", "scale": 1},
            "budget": {"triangles": len(f)},
            "export": {"glb": "res://" + path.relative_to(REPO).as_posix()},
            "provenance": {"kind": "existing_custom_item", "notes": "Original generator inputs unavailable; preserves existing art and game resources."}}
    if len(relative.parts) > 2 and relative.parts[-2] in align.ARCHETYPE_PROFILES:
        data["archetype"] = relative.parts[-2]
    check(data, SCHEMA)
    save(item.path, data)
    print(item.path)


def lineup_many(items, mount="ground"):
    """Render candidates in one GPU session without modifying any published catalog."""
    jobs = []
    for item in items:
        item.validate()
        item_mount = mount
        if mount == "ground" and item.category == "weapons":
            item_mount = "right_hand"
        elif mount == "ground" and item.recipe().get("builder") == "shieldcraft":
            item_mount = "shield"
        jobs.append([str(item.built), str(STUDIO / "rigs/mannequin_medium.glb"), str(item.built.parent / "lineup"), item_mount])
    manifest = work_root(STUDIO) / "item-lineup/jobs.json"
    save(manifest, jobs)
    result = run_godot(["--script", str(STUDIO / "godot/render_item_lineup.gd"), "--", str(manifest)], headless=False)
    if result.count("ITEM_SHOT ") != 4 * len(items):
        raise ValueError(f"Incomplete catalog lineup rendering:\n{result}")
    for item, job in zip(items, jobs):
        item.compose_lineup(job[3])
    for item in items:
        shutil.rmtree(item.built.parent / "lineup")
    shutil.rmtree(manifest.parent)


def main(argv=None):
    """Expose item commands under studio.py without changing existing character commands."""
    parser = argparse.ArgumentParser(prog="studio.py item", description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("schema", help="Print the item recipe JSON schema")
    sub.add_parser("catalog", help="Update the current overview and category catalogs from Godot lineups")
    listing = sub.add_parser("list", help="List authored recipes or existing game models")
    listing.add_argument("--existing", action="store_true", help="Inventory custom-items GLBs")
    new = sub.add_parser("new", help="Create a persistent item recipe")
    new.add_argument("key")
    new.add_argument("--backend", choices=BACKENDS, default="source")
    new.add_argument("--archetype", choices=sorted(align.ARCHETYPE_PROFILES))
    new.add_argument("--source", type=Path, help="Copy an existing self-contained GLB into the recipe")
    new.add_argument("--builder", choices=sorted(set(BUILDERS) | set(FAMILIES)), help="Explicit topology-first procedural builder")
    adoption = sub.add_parser("adopt", help="Register existing custom-items models without modifying them")
    adoption.add_argument("paths", nargs="*", type=Path)
    adoption.add_argument("--all", action="store_true")
    for name in ["reference", "build", "preview", "lineup", "validate", "export", "recreate", "preserve", "publish"]:
        cmd = sub.add_parser(name)
        cmd.add_argument("key", nargs="?")
        cmd.add_argument("--all", action="store_true", help="Apply to all item recipes")
        if name == "preserve":
            cmd.add_argument("--revision", default="HEAD", help="Git revision containing the original detailed GLBs")
        elif name == "reference":
            cmd.add_argument("--brief")
            cmd.add_argument("--provider", choices=["gemini", "openai"], default="gemini")
            cmd.add_argument("--image", type=Path, help="Import a concept from ImageGen or another source")
            cmd.add_argument("--prompt-only", action="store_true", help="Print shared prompt and style image paths without generation or writes")
        elif name == "export":
            cmd.add_argument("--no-import", action="store_true")
            cmd.add_argument("--force", action="store_true")
        elif name == "lineup":
            cmd.add_argument("--mount", choices=["ground", "right_hand", "left_hand", "shield"], default="ground")
    args = parser.parse_args(argv)
    try:
        if args.command == "schema":
            print(json.dumps(SCHEMA, indent=2))
        elif args.command == "catalog":
            from items.catalogs import write_catalogs
            write_catalogs([Item(p.parent.relative_to(OBJECTS).as_posix()) for p in sorted(OBJECTS.glob("*/*/item.json"))],
                           STUDIO / "catalogs/items")
        elif args.command == "list":
            if args.existing:
                for path in sorted((REPO / "assets/custom-items").rglob("*.glb")):
                    print(path.relative_to(REPO))
            else:
                for path in sorted(OBJECTS.glob("*/*/item.json")):
                    item = Item(path.parent.relative_to(OBJECTS).as_posix())
                    data = item.recipe()
                    print(f"{item.key:48} {data['backend']:12} {'built' if item.built.exists() else '-':6} {data['export']['glb']}")
        elif args.command == "new":
            Item(args.key).new(args.backend, args.archetype, args.source, args.builder)
        elif args.command == "adopt":
            paths = sorted((REPO / "assets/custom-items").rglob("*.glb")) if args.all else args.paths
            if not paths:
                raise ValueError("Provide GLB paths or --all")
            for path in paths:
                adopt(path)
        else:
            if bool(args.key) == args.all:
                raise ValueError("Provide one item key or --all")
            if args.command == "reference" and args.all:
                raise ValueError("Generate/import references for one item at a time")
            keys = [path.parent.relative_to(OBJECTS).as_posix() for path in sorted(OBJECTS.glob("*/*/item.json"))] if args.all else [args.key]
            if args.command == "lineup" and args.all:
                lineup_many([Item(key) for key in keys], args.mount)
                return
            failures, published = [], []
            for key in keys:
                item = Item(key)
                try:
                    if args.command == "reference":
                        item.reference(args.brief, args.provider, args.image, args.prompt_only)
                    elif args.command == "preserve":
                        from items.preserve import preserve
                        if preserve(item, args.revision):
                            print(f"preserved original geometry {key}")
                    elif args.command == "recreate":
                        from items.recreate import recreate
                        if args.all and item.recipe().get("provenance", {}).get("kind") != "existing_custom_item":
                            continue
                        if recreate(item):
                            print(f"recreated recipe {key}")
                    elif args.command == "export":
                        item.export(args.no_import or args.all, args.force)
                        published.append(item.recipe()["export"]["glb"])
                    elif args.command == "validate":
                        print(f"{key}: {json.dumps(item.validate())}")
                    elif args.command == "lineup":
                        item.lineup(args.mount)
                    else:
                        getattr(item, args.command)()
                except (ValueError, OSError, KeyError) as exc:
                    if not args.all:
                        raise
                    failures.append(f"{key}: {exc}")
            if published and not args.no_import:
                if args.all:
                    godot(published)
                    for key in keys:
                        item = Item(key)
                        receipt = item.folder / ".export.json"
                        if receipt.exists() and item.recipe()["export"]["glb"] in published:
                            record = json.loads(receipt.read_text())
                            record["godot_validated"] = True
                            save(receipt, record)
            if args.command == "publish" and not failures:
                from items.catalogs import write_catalogs
                write_catalogs([Item(p.parent.relative_to(OBJECTS).as_posix()) for p in sorted(OBJECTS.glob("*/*/item.json"))], STUDIO / "catalogs/items")
            if failures:
                raise ValueError("\n".join(failures))
    except (ValueError, OSError, KeyError, subprocess.SubprocessError) as exc:
        parser.exit(1, f"Item workflow: {exc}\n")


