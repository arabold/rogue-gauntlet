"""Modular floor and wall tile families: recipe -> staged GLBs -> seam/topology validation -> Godot
review on the game grid -> explicit publication -> explicit export of art to the game tree.

A family recipe (`tilesets/<set>/<family>.json`) builds every piece and variant of one material,
so they share one seam contract and can be reviewed together. Builds and renders live in OS
temporary storage; catalogs keep the reviewed lineup; export copies GLBs to `assets/custom-tiles/`.
No mesh libraries, collision, navigation or gameplay resources are generated here.
"""

import argparse
import hashlib
import json
import shutil
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

from core import publication
from core.export import export_static
from core.paths import work_root
from core.publication import digest
from items import solid
from items.workflow import check, godot, run_godot, save, within
from prefabs import decor
from tiles import caves, dressing, floors, inlays, kaykit, overlays, pools, walls
from tiles.geometry import SEAM, boundary_profile, rng

STUDIO = Path(__file__).resolve().parents[1]
REPO = STUDIO.parent
TILESETS = STUDIO / "tilesets"
EXPORT_ROOT = REPO / "assets/custom-tiles"
CONTEXT_FLOOR = REPO / "assets/kaykit-dungeon-remastered/floor_tile_large.gltf"
KINDS = {"floor": floors.STYLES, "wall": {**walls.STYLES, **dressing.STYLES, **caves.STYLES}, "overlay": overlays.STYLES,
         "inlay": inlays.STYLES, "pool": pools.STYLES}
# Rock walls bulge inside their footprint and may crest below it; only their ends fill it exactly.
INSET = {"cave": .25}
# Styles that decorate an original KayKit tile: its open panels and seams are the reference.
DERIVED = {"dressed", "water"}
# Growth may rise above a wall's cap and stand slightly proud of its faces, never past its ends.
GROWTH_ALLOWANCE = np.array([.15, .25, .15])
PIECES = {"floor": list(floors.SIZES), "wall": list(dict.fromkeys([*walls.PIECES, *caves.PIECES])),
          "overlay": list(overlays.BORDERS), "inlay": list(inlays.SOURCES), "pool": list(overlays.BORDERS)}
# Pieces each wall style can build; the rest of a kind's pieces are rejected at recipe load.
STYLE_PIECES = {"masonry": list(walls.PIECES), "dressed": list(walls.PIECES), "cave": list(caves.PIECES)}
# Fixed shading ramps: tops of floors and walls land mid-ramp whatever a tile's own extent.
# Inlay water sits under the floor top; its fixed range lands every surface on one shade.
HEIGHT_RANGE = {"floor": (-0.03, 0.27), "wall": (-3.0, 7.0), "overlay": (0.0, 0.3), "inlay": (-0.4, 0.3), "pool": (-1.7, 0.3)}
# Footprints (min, max) every piece must fill exactly, matching the KayKit dungeon tiles.
FOOTPRINT = {"large": ([-2, -.1, -2], [2, None, 2]), "small": ([-1, -.1, -1], [1, None, 1]),
             "wall": ([-2, 0, -.5], [2, 4, .5]), "wall_half": ([0, 0, -.5], [2, 4, .5]),
             "wall_corner": ([-2, 0, -.5], [.5, 4, 2]), "wall_Tsplit": ([-2, 0, -.5], [2, 4, 2]),
             "wall_crossing": ([-2, 0, -2], [2, 4, 2]), "wall_doorway": ([-2, 0, -.5], [2, 4, .5]),
             "wall_gated": ([-2, 0, -.5], [2, 4, .5]), "wall_half_endcap": ([-2, 0, -.5], [0, 4, .5]),
             "pillar": ([-.75, 0, -.75], [.75, 4, .75]), "gate_leaf": ([-.16, .05, -.075], [1.8, 2.72, .075]),
             "channel": ([-2, None, -1], [2, .05, 1]), "pool": ([-2, None, -2], [2, .05, 2])}
# Overlays only have to stay on their cell and just above the floor; rug ends are inset. Pools fill their cell's
# width and length, sunk below the floor top.
WITHIN = {"overlay": ([-2, .03, -2], [2, .12, 2]), "pool": ([-2, -3, -2], [2, .05, 2])}
VARIANT = {"type": "array", "minItems": 1, "maxItems": 26, "items": {"type": "string", "pattern": "^[A-Z]$"}}
SCHEMA = {
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "title": "Asset Studio tile family recipe", "type": "object", "additionalProperties": False,
    "required": ["version", "name", "kind", "style", "colors", "pieces", "budget", "export"],
    "properties": {
        "version": {"type": "integer", "const": 1}, "name": {"type": "string", "minLength": 1},
        "kind": {"enum": sorted(KINDS)}, "style": {"enum": sorted({s for styles in KINDS.values() for s in styles})},
        "concept": {"type": "string"}, "reference": {"type": "string", "pattern": "^[a-z0-9_.]+\\.png$"},
        "design": {"type": "object"},
        "colors": {"type": "object"}, "materials": {"type": "object"},
        "ramp": {"type": "object", "additionalProperties": False,
                 "properties": {"light": {"type": "number", "minimum": 1}, "shadow": {"type": "number", "minimum": 0, "maximum": 1}}},
        "pieces": {"type": "object", "additionalProperties": False,
                   "properties": {name: VARIANT for names in PIECES.values() for name in names}},
        "budget": {"type": "object", "additionalProperties": False, "required": ["triangles"],
                   "properties": {"triangles": {"type": "integer", "minimum": 1}}},
        "export": {"type": "object", "additionalProperties": False, "required": ["folder"],
                   "properties": {"folder": {"type": "string", "pattern": "^res://assets/custom-tiles/[a-z0-9_/]+$"}}},
    },
}


class Family:
    """One tile family recipe (`set/family`) and its studio-only build products."""

    def __init__(self, key):
        parts = key.split("/")
        if len(parts) != 2 or not all(p and p.replace("_", "").isalnum() and p == p.lower() for p in parts):
            raise ValueError("Tile family key must be set/family using lowercase letters, digits, underscores")
        self.key, (self.set, self.id) = key, parts
        self.path = within(TILESETS / self.set / f"{self.id}.json", TILESETS)
        self.staged = work_root(STUDIO) / "tiles" / key

    def recipe(self):
        data = json.loads(self.path.read_text())
        check(data, SCHEMA)
        if data["style"] not in KINDS[data["kind"]]:
            raise ValueError(f"{self.key}: style {data['style']} is not a {data['kind']} style")
        allowed = STYLE_PIECES.get(data["style"], PIECES[data["kind"]])
        unknown = set(data["pieces"]) - set(allowed)
        if unknown or not data["pieces"]:
            raise ValueError(f"{self.key}: {data['style']} pieces must be among {allowed}")
        if data.get("reference") and not (self.path.parent / data["reference"]).is_file():
            raise ValueError(f"{self.key}: concept {data['reference']} is missing beside the recipe")
        self.destination(data)
        return data

    def destination(self, data):
        return within(REPO / data["export"]["folder"][6:], EXPORT_ROOT)

    def tiles(self, data):
        """(piece, variant, file stem) for every tile, named like the KayKit originals."""
        out = []
        for piece, variants in data["pieces"].items():
            if data["kind"] in ("floor", "inlay", "pool"):
                stem = f"{self.id}_{piece}"
            else:
                stem = self.id if piece == "wall" else f"{self.id}_{piece.removeprefix('wall_')}"
            out.extend((piece, v, f"{stem}_{v}") for v in variants)
        return out

    def colors(self, data):
        """Recipe colors, over the original swatches for tiles derived from KayKit pieces."""
        if data["style"] in DERIVED:
            return {**decor.palette()[0], **dressing.source_colors(data.get("design", {}), data["pieces"]), **data["colors"]}
        return data["colors"]

    def looks(self, data, colors):
        """Per-slot ramps and finish: tiles keep a gentle ramp so neighbors meet in one tone."""
        light, shadow = data.get("ramp", {}).get("light", 1.08), data.get("ramp", {}).get("shadow", .62)
        decor_materials = decor.palette()[1] if data["style"] in DERIVED else {}
        looks = {}
        for slot, rgb in colors.items():
            base = np.asarray(rgb, float)
            # KayKit's dungeon material: no metal, roughness 0.45, so tiles share its soft sheen.
            spec = {"metallic": 0.0, "roughness": .45, "crease": 30.0, **decor_materials.get(slot, {}),
                    **data.get("materials", {}).get(slot, {})}
            if slot in kaykit.gradients() and "ramp" not in spec:
                # KayKit's own swatch gradient, copied exactly rather than approximated.
                spec["gradient"] = kaykit.gradients()[slot]
            if "gradient" in spec:
                pass
            elif "ramp" in spec:
                # An explicit [light, shadow] pair matches a KayKit swatch's own gradient.
                spec["ramp"] = (tuple(spec["ramp"][0]), tuple(spec["ramp"][1]), tuple(base))
            else:
                spec["ramp"] = (tuple(np.clip(base * light + 6, 0, 255)), tuple(base * shadow), tuple(base))
            looks[slot] = spec
        return looks

    def build_piece(self, data, piece, variant):
        return KINDS[data["kind"]][data["style"]](data.get("design", {}), piece, variant, self.key)

    def build(self):
        """Stage every tile of the family, then validate each one and the shared seams."""
        data = self.recipe()
        if self.staged.exists():
            shutil.rmtree(self.staged)
        self.staged.mkdir(parents=True)
        pieces = {}
        colors = self.colors(data)
        for piece, variant, stem in self.tiles(data):
            built = self.build_piece(data, piece, variant)
            missing = set(str(s) for s in built.slots) - set(colors)
            if missing:
                raise ValueError(f"{self.key}: no colors for slots {sorted(missing)}")
            export_static(str(self.staged / f"{stem}.glb"), stem, built, colors, materials=self.looks(data, colors),
                          crease=30.0, height_range=HEIGHT_RANGE[data["kind"]])
            pieces[stem] = (piece, built)
        report = self.validate(data, pieces)
        save(self.staged / "build.json", {"recipe_sha256": digest(self.path), "inputs": self.inputs(),
             "models": {p.name: digest(p) for p in sorted(self.staged.glob("*.glb"))}, "report": report})
        publication.stage_stamp(self.staged, self.fingerprint())
        print(self.staged)
        return report

    def validate(self, data, pieces):
        """Topology, budget and footprint per tile, plus the family-wide seam contract."""
        errors, report = [], {"tiles": {}}
        for stem, (piece, built) in pieces.items():
            p, f = built.verts, built.faces
            topology = solid.inspect(p, f)
            lo, hi = FOOTPRINT.get(piece, (None, None))
            row = {"triangles": len(f), "components": topology["components"],
                   "bounds": [p.min(0).round(4).tolist(), p.max(0).round(4).tolist()]}
            report["tiles"][stem] = row
            if not np.isfinite(p).all():
                errors.append(f"{stem}: non-finite vertices")
            if len(f) > data["budget"]["triangles"]:
                errors.append(f"{stem}: {len(f)} triangles exceeds budget {data['budget']['triangles']}")
            allowed = {}
            if data["style"] in DERIVED:
                source = kaykit.load(data.get("design", {}).get("source", {}).get(piece, piece))[0]
                allowed = solid.inspect(source.verts, source.faces)
                row["source_open_edges"] = allowed["boundary_edges"]
            # A dressed wall may lose some of KayKit's open brick accents to a carving, never gain defects.
            for key, count in getattr(built, "authored", {}).items():
                allowed[key] = allowed.get(key, 0) + count
                row.setdefault("decor_authored", {})[key] = count
            for key in ("boundary_edges", "nonmanifold_edges", "inconsistent_edges", "duplicate_faces"):
                if topology[key] > allowed.get(key, 0):
                    errors.append(f"{stem}: {topology[key]} {key}" + (f" (source has {allowed[key]})" if allowed else ""))
            # A derived tile may keep its source's open, inward-facing shell (a grate's shaft); it must not lose volume.
            least = min(0.0, allowed.get("signed_volume", 0.0)) if data["style"] in DERIVED else 0.0
            if topology["signed_volume"] <= least - 1e-6 or (not least and topology["signed_volume"] <= 0):
                errors.append(f"{stem}: surface must enclose a positive volume")
            if data["kind"] in WITHIN:
                lo, hi = WITHIN[data["kind"]]
                if np.any(p.min(0) < np.array(lo) - 1e-4) or np.any(p.max(0) > np.array(hi) + 1e-4):
                    errors.append(f"{stem}: leaves its cell or floats: {row['bounds']}")
                continue
            ends = {(axis, value) for axis, value, _ in wall_ends(piece)}
            for axis in range(3):
                for value, actual, name, out in ((lo[axis], p[:, axis].min(), "min", -1), (hi[axis], p[:, axis].max(), "max", 1)):
                    end = (axis, value) in ends
                    grow = GROWTH_ALLOWANCE[axis] if data["style"] in DERIVED and not end else 0.0
                    inset = INSET.get(data["style"], 0.0) if not end else 0.0
                    if value is not None and not -1e-4 - inset <= (actual - value) * out <= grow + 1e-4:
                        errors.append(f"{stem}: footprint {name}[{axis}] is {actual:.4f}, expected {value}")
        seams = seam_report(data, pieces)
        report["seams"] = seams
        errors.extend(seams["errors"])
        report["errors"] = errors
        save(self.staged / "validation.json", report)
        if errors:
            raise ValueError(f"{self.key}: " + "; ".join(errors[:12]))
        return report

    def inputs(self):
        paths = [self.path, *(STUDIO / "tiles" / name for name in ("geometry.py", "floors.py", "walls.py", "overlays.py",
                                                                      "dressing.py", "kaykit.py", "caves.py", "inlays.py", "pools.py")),
                 *sorted(kaykit.SOURCE.glob("floor_tile*grate*.gltf")), *sorted(kaykit.SOURCE.glob("floor_tile*grate*.bin")),
                 *sorted(kaykit.SOURCE.glob("floor_foundation*.gltf")), *sorted(kaykit.SOURCE.glob("floor_foundation*.bin")),
                 *sorted(kaykit.SOURCE.glob("wall*.gltf")), *sorted(kaykit.SOURCE.glob("wall*.bin")),
                 STUDIO / "prefabs/decor.py", *sorted(decor.MESHES.glob("*.npz")),
                 kaykit.SOURCE / "dungeon_texture.png",
                 STUDIO / "core/export.py", STUDIO / "core/mesh.py",
                 STUDIO / "core/kit.py", STUDIO / "items/solid.py"]
        return {str(p.relative_to(REPO)): digest(p) for p in paths}

    def fingerprint(self):
        """Shared Studio inputs plus the tile builders, which the shared fingerprint does not cover."""
        shared = publication.fingerprint(STUDIO, self.path)
        return hashlib.sha256(json.dumps([shared, self.inputs()], sort_keys=True).encode()).hexdigest()

    def models(self):
        stamp = json.loads((self.staged / "build.json").read_text())
        if stamp["recipe_sha256"] != digest(self.path) or stamp["inputs"] != self.inputs():
            raise ValueError(f"{self.key}: recipe or tile builders changed; run tile build again")
        for name, expected in stamp["models"].items():
            if digest(self.staged / name) != expected:
                raise ValueError(f"{self.key}: staged {name} changed; run tile build again")
        return [self.staged / name for name in stamp["models"]]

    def lineup(self):
        """Render the family on the game grid in Godot and compose the review sheet."""
        self.models()
        jobs = self.render_jobs()
        save(self.staged / "render/jobs.json", jobs)
        result = run_godot(["--script", str(STUDIO / "godot/render_tiles.gd"), "--", str(self.staged / "render/jobs.json")],
                           headless=False)
        shots = [shot["out"] for job in jobs for shot in job["shots"]]
        if result.count("TILE_SHOT ") != len(shots):
            raise ValueError(f"Godot did not render every tile shot:\n{result}")
        self.compose(shots)
        shutil.rmtree(self.staged / "render")

    def render_jobs(self):
        data = self.recipe()
        tiles = self.tiles(data)
        glb = {stem: str(self.staged / f"{stem}.glb") for _, _, stem in tiles}
        out = self.staged / "render"
        by_piece = {}
        for piece, _, stem in tiles:
            by_piece.setdefault(piece, []).append(glb[stem])
        r = rng(self.key, "review-layout")
        return {"floor": floor_jobs, "wall": wall_jobs, "overlay": overlay_jobs, "inlay": inlay_jobs, "pool": pool_jobs}[data["kind"]](by_piece, r, out)

    def compose(self, shots):
        """Three review panels over a strip of every variant, labeled with the family and its pieces."""
        data = self.recipe()
        panels = [Image.open(path).convert("RGB") for path in shots]
        width = 1440
        main = [p.resize((480, 480), Image.Resampling.LANCZOS) for p in panels[:3]]
        strip = panels[3]
        strip = strip.resize((width, round(strip.height * width / strip.width)), Image.Resampling.LANCZOS)
        sheet = Image.new("RGB", (width, 36 + 480 + 28 + strip.height), (20, 23, 30))
        draw = ImageDraw.Draw(sheet)
        names = ", ".join(f"{piece} x{len(v)}" for piece, v in data["pieces"].items())
        draw.text((12, 10), f"{self.key} | {data['name']} | {names}", fill="white")
        for i, (panel, title) in enumerate(zip(main, self.titles(data))):
            sheet.paste(panel, (480 * i, 36))
            draw.text((480 * i + 10, 42), title, fill="white")
        sheet.paste(strip, (0, 36 + 480 + 28))
        draw.text((12, 36 + 480 + 8), "Every variant: " + "  ".join(stem for _, _, stem in self.tiles(data)), fill="white")
        sheet.save(self.staged / "lineup.png")
        publication.render_stamp(self.staged, self.fingerprint(), ("lineup.png",))
        print(self.staged / "lineup.png")

    @staticmethod
    def titles(data):
        return {"floor": ["Top: random variants, mixed 4x4 / 2x2", "Game camera", "Seam close-up"],
                "wall": ["Game camera: corner, walls, half", "Elevation", "Outer corner"],
                "overlay": ["Top: 3x3 rug and a runner", "Game camera", "Corner close-up"],
                "inlay": ["Top: channel run and pool in stone", "Game camera", "Close-up"],
                "pool": ["Top: 3x3 pool and a runner in stone", "Game camera", "Corner close-up"]}[data["kind"]]

    def publish(self):
        self.models()
        report = json.loads((self.staged / "validation.json").read_text())
        publication.publish(self.staged, STUDIO / "catalogs/tiles" / self.key, self.key, self.fingerprint(),
                            ("lineup.png",), {"recipe": self.recipe(), "report": report,
                                              "models": json.loads((self.staged / "build.json").read_text())["models"]})
        print(STUDIO / "catalogs/tiles" / self.key)

    def export(self, no_import=False, force=False):
        """Copy the validated current build into the game tree; Godot proves each file imports."""
        data = self.recipe()
        models = self.models()
        target = self.destination(data)
        receipt_path = self.path.with_name(f".{self.id}.export.json")
        receipt = json.loads(receipt_path.read_text()) if receipt_path.exists() else {}
        for model in models:
            dest = target / model.name
            owned = receipt.get(model.name) == (digest(dest) if dest.exists() else None)
            if dest.exists() and digest(dest) != digest(model) and not owned and not force:
                raise ValueError(f"Existing model differs: {dest}; inspect it and use --force to replace")
        target.mkdir(parents=True, exist_ok=True)
        for model in models:
            shutil.copy2(model, target / model.name)
        paths = [data["export"]["folder"] + "/" + model.name for model in models]
        if not no_import:
            godot(paths)
        save(receipt_path, {model.name: digest(target / model.name) for model in models})
        print(target)


def floor_seams(pieces):
    """Every floor edge must show one cross-section that repeats each seam, in pattern space.

    X edges (normal along x) and Z edges are checked separately: planks differ by direction.
    A small tile's center sits on odd pattern units, so its samples shift by one unit.
    """
    ts = np.arange(0.0071, SEAM, 0.0193)
    ys = np.arange(-0.118, 0.32, 0.0041)
    canonical, errors, worst = {}, [], 0.0
    for stem, (piece, built) in pieces.items():
        half, center = floors.frame(piece)
        for axis, along in ((0, 2), (2, 0)):
            for value in (-half, half):
                for k in range(-3, 4):
                    local = ts - center + k * SEAM
                    if local.min() < -half - 1e-9 or local.max() >= half:
                        continue
                    profile = boundary_profile(built, axis, value, along, ys, local)
                    reference = canonical.setdefault(axis, profile)
                    mismatch = float(np.mean(profile != reference))
                    worst = max(worst, mismatch)
                    if mismatch > .002:
                        errors.append(f"{stem}: {'xz'[axis // 2]} edge {value:+} differs from the family seam ({mismatch:.1%})")
    return {"worst_mismatch": worst, "errors": sorted(set(errors))}


def seam_report(data, pieces):
    """The seam check for a family: against its own contract, or its KayKit source when derived."""
    if data["kind"] == "inlay":
        return inlay_seams(pieces, data.get("design", {}))
    if data["kind"] == "pool":
        return overlay_seams(pieces, ys=np.arange(-2.9993, .05, .0041), walled=True)
    if data["style"] in DERIVED:
        return source_seams(pieces, data.get("design", {}).get("source", {}))
    return {"floor": floor_seams, "wall": wall_seams, "overlay": overlay_seams}[data["kind"]](pieces)


def wall_ends(piece):
    """Seam planes of a wall piece (none for floors and rugs); cave-only pieces derive theirs from their open arms."""
    if piece in WALL_ENDS:
        return WALL_ENDS[piece]
    return caves.ends(piece) if piece in caves.PIECES else []


WALL_ENDS = {"wall": [(0, 2.0, 2), (0, -2.0, 2)], "wall_half": [(0, 0.0, 2), (0, 2.0, 2)],
             "wall_corner": [(0, -2.0, 2), (2, 2.0, 0)]}


def source_seams(pieces, sources):
    """Derived walls must leave every end exactly as the original KayKit piece has it."""
    ns = np.arange(-.5993, .6, .0087)
    ys = np.arange(.0031, 4.0, .0137)
    errors, worst = [], 0.0
    for stem, (piece, built) in pieces.items():
        source = kaykit.load(sources.get(piece, piece))[0]
        for axis, value, along in WALL_ENDS[piece]:
            mismatch = float(np.mean(boundary_profile(built, axis, value, along, ys, ns)
                                     != boundary_profile(source, axis, value, along, ys, ns)))
            worst = max(worst, mismatch)
            if mismatch > 0:
                errors.append(f"{stem}: growth reaches the end at {'xyz'[axis]}={value:+}")
    return {"worst_mismatch": worst, "errors": errors}


def inlay_seams(pieces, design):
    """Inlays keep their KayKit grate's rim, so every tile edge must be exactly the source's edge."""
    ys = np.arange(-.9993, .05, .0041)
    errors, worst = [], 0.0
    for stem, (piece, built) in pieces.items():
        source = kaykit.load(inlays.source(design, piece))[0]
        half = np.array(FOOTPRINT[piece][1])[[0, 2]]
        for axis, along, value in ((0, 2, half[0]), (0, 2, -half[0]), (2, 0, half[1]), (2, 0, -half[1])):
            cross = np.arange(-half[along // 2] + .0063, half[along // 2], .0137)
            mismatch = float(np.mean(boundary_profile(built, axis, value, along, ys, cross)
                                     != boundary_profile(source, axis, value, along, ys, cross)))
            worst = max(worst, mismatch)
            if mismatch > 0:
                errors.append(f"{stem}: edge {'xyz'[axis]}={value:+} differs from its KayKit grate")
    return {"worst_mismatch": worst, "errors": errors}


def wall_seams(pieces):
    """Every wall end must match the straight wall's end, mirrored front to back."""
    ns = np.arange(.0043, .5, .0087)
    ns = np.concatenate([-ns[::-1], ns])
    ys = np.arange(.0031, 4.0, .0137)
    reference, errors, worst = None, [], 0.0
    for stem, (piece, built) in pieces.items():
        for axis, value, along in wall_ends(piece):
            profile = boundary_profile(built, axis, value, along, ys, ns)
            if reference is None:
                reference = profile
            mismatch = max(float(np.mean(profile != reference)), float(np.mean(profile != profile[:, ::-1])))
            worst = max(worst, mismatch)
            if mismatch > .004:
                errors.append(f"{stem}: end at {'xyz'[axis]}={value:+} differs from the family seam ({mismatch:.1%})")
    return {"worst_mismatch": worst, "errors": errors}


def overlay_seams(pieces, ys=None, walled=False):
    """Rug pieces turn in place, so edges are compared by role rather than by fixed axis.

    Field edges (rug continues in both pieces) must all match and be symmetric. Run edges (a border
    band crosses them) must match once oriented with the border first; a runner's run edges carry
    borders at both ends and must be symmetric. Rug ends must leave the cell edge bare.
    """
    ts = np.arange(-1.9963, 2.0, .0137)
    ys = np.arange(.0302, .12, .0021) if ys is None else ys
    sides = {(1, 0): (0, 2.0, 2), (-1, 0): (0, -2.0, 2), (0, 1): (2, 2.0, 0), (0, -1): (2, -2.0, 0)}
    reference, errors, worst = {}, [], 0.0
    for stem, (piece, built) in pieces.items():
        borders = overlays.BORDERS[piece]
        for side, (axis, value, along) in sides.items():
            profile = boundary_profile(built, axis, value, along, ys, ts)
            if side in borders:
                role = "end"
            else:
                cross = [b for b in borders if (b[0] != 0) == (side[0] == 0)]
                role = "field" if not cross else "run2" if len(cross) == 2 else "run"
                if role == "run" and (cross[0][0] > 0 or cross[0][1] > 0):
                    profile = profile[:, ::-1]
            if role == "end" and not walled:
                if profile.any():
                    errors.append(f"{stem}: rug end {side} reaches the cell edge")
                continue
            if role in ("field", "run2") and np.mean(profile != profile[:, ::-1]) > .004:
                errors.append(f"{stem}: {role} edge {side} is not symmetric")
            ref = reference.setdefault(role, profile)
            mismatch = float(np.mean(profile != ref))
            worst = max(worst, mismatch)
            if mismatch > .004:
                errors.append(f"{stem}: {role} edge {side} differs from the family ({mismatch:.1%})")
    return {"worst_mismatch": worst, "errors": errors}


def pool_jobs(by_piece, r, out):
    """A 3x3 pool and a three-tile runner sunk into KayKit stone floor, laid as the game lays them."""
    jobs = overlay_jobs(by_piece, r, out)
    pool = {(4 * i, 4 * j) for i in range(3) for j in range(3)} | {(4 * k, 12) for k in range(3)}
    # Floor only where there is no pool, in a ring around both.
    floor = [{"glb": str(CONTEXT_FLOOR), "position": [x, 0, z]} for x in range(-4, 13, 4) for z in range(-4, 17, 4)
             if (x, z) not in pool]
    jobs[0]["tiles"] = [tile for tile in jobs[0]["tiles"] if tile["glb"] != str(CONTEXT_FLOOR)] + floor
    return jobs


def overlay_jobs(by_piece, r, out):
    """A 3x3 rug of turned corners, edges and a center, and a runner, on neutral context floor."""
    floor = str(CONTEXT_FLOOR)
    tiles = [{"glb": floor, "position": [4 * i, 0, 4 * j]} for i in range(5) for j in range(4)]
    pick = lambda piece: str(r.choice(by_piece[piece])) if by_piece.get(piece) else None  # noqa: E731
    # Turns that point the authored border (local -z, corners also -x) at each side of the rug.
    rug = {(0, 0): ("corner", 0), (1, 0): ("edge", 0), (2, 0): ("corner", -90), (0, 1): ("edge", 90),
           (1, 1): ("center", 0), (2, 1): ("edge", -90), (0, 2): ("corner", 90), (1, 2): ("edge", 180),
           (2, 2): ("corner", 180)}
    for (i, j), (piece, turn) in rug.items():
        if pick(piece):
            tiles.append({"glb": pick(piece), "position": [4 * i, 0, 4 * j], "rotation": turn})
    run = [("runner_end", 0), ("runner", 0), ("runner_end", 180)]
    for k, (piece, turn) in enumerate(run):
        if pick(piece):
            tiles.append({"glb": pick(piece), "position": [4 * k, 0, 12 + 1e-3], "rotation": turn})
    rows = {"tiles": tiles, "resolution": [960, 960], "shots": [
        {"out": str(out / "top.png"), "target": [6, 0, 6.5], "direction": [0, 1, 1e-4], "size": 17},
        {"out": str(out / "game.png"), "target": [5, 0, 6], "direction": [6.8, 11.5, 6.8], "size": 15},
        {"out": str(out / "close.png"), "target": [-.5, 0, -.5], "direction": [6.8, 11.5, 6.8], "size": 5}]}
    strip, x = [], 0.0
    for piece in overlays.BORDERS:
        for path in by_piece.get(piece, []):
            strip.append({"glb": str(path), "position": [x + 2, 0, 0]})
            x += 4.8
    single = {"tiles": strip, "resolution": [1920, 360], "shots": [
        {"out": str(out / "variants.png"), "target": [(x - .8) / 2, 0, 0], "direction": [0, 1, 1e-4],
         "size": max(5.0, (x + .4) * 360 / 1920)}]}
    return [rows, single]


def inlay_jobs(by_piece, r, out):
    """A run of channels and a pool set into KayKit stone floor beside a KayKit grate, as the game lays them."""
    floor, grate = str(CONTEXT_FLOOR), str(CONTEXT_FLOOR.with_name("floor_tile_big_grate.gltf"))
    channels, pools = by_piece.get("channel", []), by_piece.get("pool", [])
    # Cells (i, j) of 4 units; the channel run fills the middle row's halves along x.
    tiles = [{"glb": floor, "position": [4 * i, 0, 4 * j]} for i in range(5) for j in range(4) if j != 1 and (i, j) not in ((3, 2), (1, 2))]
    for i in range(5):
        if channels:
            tiles.append({"glb": str(channels[i % len(channels)]), "position": [4 * i, 0, 3]})
        tiles.append({"glb": floor.replace("large", "small"), "position": [4 * i - 1, 0, 5]})
        tiles.append({"glb": floor.replace("large", "small"), "position": [4 * i + 1, 0, 5]})
    if pools:
        tiles.append({"glb": str(pools[0]), "position": [12, 0, 8]})
    tiles.append({"glb": grate, "position": [4, 0, 8]})
    grid = {"tiles": tiles, "resolution": [960, 960], "shots": [
        {"out": str(out / "top.png"), "target": [8, 0, 6], "direction": [0, 1, 1e-4], "size": 16.6},
        {"out": str(out / "game.png"), "target": [8, 0, 6], "direction": [6.8, 11.5, 6.8], "size": 12},
        {"out": str(out / "close.png"), "target": [12, 0, 6], "direction": [6.8, 11.5, 6.8], "size": 5.5}]}
    strip, x = [], 0.0
    for path in channels + pools:
        strip.append({"glb": str(path), "position": [x + 2, 0, 0]})
        x += 4.8
    single = {"tiles": strip, "resolution": [1920, 360], "shots": [
        {"out": str(out / "variants.png"), "target": [(x - .8) / 2, 0, 0], "direction": [0, 1, 1e-4],
         "size": max(5.0, (x + .4) * 360 / 1920)}]}
    return [grid, single]


def floor_jobs(by_piece, r, out):
    """Random variants on a 5x4 grid of 4x4 cells, three cells split into 2x2 tiles."""
    tiles, cols, rows = [], 5, 4
    large, small = by_piece.get("large", []), by_piece.get("small", [])
    cells = [(i, j) for i in range(cols) for j in range(rows)]
    split = {cells[k] for k in r.choice(len(cells), 3, replace=False)} if small else set()
    for i in range(cols):
        for j in range(rows):
            if (i, j) in split or not large:
                tiles += [{"glb": str(r.choice(small)), "position": [4 * i + qx, 0, 4 * j + qz]} for qx in (-1, 1) for qz in (-1, 1)]
            else:
                tiles.append({"glb": str(r.choice(large)), "position": [4 * i, 0, 4 * j]})
    seam = min(split, key=lambda c: abs(c[0] - 2) + abs(c[1] - 1.5)) if split else (2, 2)
    grid = {"tiles": tiles, "resolution": [960, 960], "shots": [
        {"out": str(out / "top.png"), "target": [8, 0, 6], "direction": [0, 1, 1e-4], "size": 16.6},
        {"out": str(out / "game.png"), "target": [8, 0, 6], "direction": [6.8, 11.5, 6.8], "size": 12},
        {"out": str(out / "close.png"), "target": [4 * seam[0] - 2, 0, 4 * seam[1] - 2], "direction": [6.8, 11.5, 6.8], "size": 4.5}]}
    variants = large + small
    strip, x = [], 0.0
    for path in variants:
        size = 4.0 if path in large else 2.0
        strip.append({"glb": path, "position": [x + size / 2, 0, 0]})
        x += size + .8
    single = {"tiles": strip, "resolution": [1920, 360], "shots": [
        {"out": str(out / "variants.png"), "target": [(x - .8) / 2, 0, 0], "direction": [0, 1, 1e-4], "size": max(5.0, (x + .4) * 360 / 1920)}]}
    return [grid, single]


def wall_jobs(by_piece, r, out):
    """A room corner on context floor: the corner piece, straight walls (a doorway and a gate when the
    family has them) and a half wall closing each run, plus junctions, a wall end and a pillar."""
    floor = str(CONTEXT_FLOOR)
    tiles = [{"glb": floor, "position": [4 * i, 0, 4 * j]} for i in range(4) for j in range(3)]
    pick = lambda *pieces: next((str(r.choice(by_piece[p])) for p in pieces if by_piece.get(p)), None)  # noqa: E731
    place = lambda glb, position, rotation=0: tiles.append({"glb": glb, "position": position, "rotation": rotation}) if glb else None  # noqa: E731
    place(pick("wall_corner"), [-2, 0, -2], 90)
    # A gate leaf hangs swung open in the back doorway, on KayKit's hinge offset, as the game's Door turns it.
    place(pick("gate_leaf"), [6 + caves.LEAF_PIVOT, 0, -2], 90)
    for i, piece in enumerate(("wall", "wall_doorway", "wall")):
        place(pick(piece, "wall"), [4 * i + 2, 0, -2])
    for j, piece in enumerate(("wall_gated", "wall")):
        place(pick(piece, "wall"), [-2, 0, 4 * j + 2], 90)
    place(pick("wall_half"), [12, 0, -2])
    place(pick("wall_half"), [-2, 0, 10], 90)
    # Junctions and free pieces stand in the room: a T-split off the back wall, a crossing, an end, a pillar.
    place(pick("wall_Tsplit"), [14, 0, -2])
    place(pick("wall_crossing"), [10, 0, 6])
    place(pick("wall_half_endcap"), [10, 0, 10])
    place(pick("pillar"), [4, 0, 6])
    room = {"tiles": tiles, "resolution": [960, 960], "shots": [
        {"out": str(out / "game.png"), "target": [5, 1.5, 3.5], "direction": [6.8, 11.5, 6.8], "size": 19},
        {"out": str(out / "front.png"), "target": [6, 2, -2], "direction": [0, .2, 1], "size": 8},
        {"out": str(out / "corner.png"), "target": [-1.5, 2, -1.5], "direction": [-1, .7, -1.3], "size": 6.5}]}
    strip, x = [], 0.0
    # Every piece in a row, each placed by its footprint's x extent.
    for piece, paths in by_piece.items():
        lo, hi = FOOTPRINT[piece][0][0], FOOTPRINT[piece][1][0]
        for path in paths:
            strip.append({"glb": str(path), "position": [x - lo, 0, 0]})
            x += hi - lo + .8
    single = {"tiles": strip, "resolution": [1920, 360], "shots": [
        {"out": str(out / "variants.png"), "target": [(x - .8) / 2, 2, 0], "direction": [0, .25, 1],
         "size": max(5.0, (x + .4) * 360 / 1920)}]}
    return [room, single]


KAYKIT = REPO / "assets/kaykit-dungeon-remastered"
# Sample rooms mixing studio tiles ("family/stem") with KayKit pieces ("kaykit:name"):
# (source, x, y, z, Y rotation). A hall of dressed walls, our floors and a rug beside KayKit's
# floors, doorway, walls and props; then a timbered cave beside KayKit's rubble and rocky floors.
SHOWCASE = [
    *[(f"dungeon/floor_flagstones_large_{'ABCD'[(i + j) % 4]}", 4 * i, 0, 4 * j, 0) for i in range(2) for j in range(3)],
    *[("kaykit:floor_tile_large", 4 * i, 0, 4 * j, 0) for i in range(2, 4) for j in range(3)],
    ("dungeon/carpet_red_runner_end_A", 0, 0, 4, 0), ("dungeon/carpet_red_runner_A", 4, 0, 4, 0),
    ("dungeon/carpet_red_runner_end_A", 8, 0, 4, 180),
    ("kaykit:wall_corner", -2, 0, -2, 90), ("dungeon/wall_crypt_A", 2, 0, -2, 0), ("kaykit:wall_doorway", 6, 0, -2, 0),
    ("dungeon/wall_prison_A", 10, 0, -2, 0), ("kaykit:wall_half", 12, 0, -2, 0),
    ("dungeon/wall_alcove_A", -2, 0, 2, 90), ("kaykit:wall", -2, 0, 6, 90), ("dungeon/wall_mossy_half_A", -2, 0, 10, 90),
    ("kaykit:torch_mounted", -1.75, 2.3, 6, 90),
    ("kaykit:banner_patternA_red", -2, 0, 6, 90), ("kaykit:torch_mounted", 4.3, 2.3, -1.75, 0),
    ("kaykit:table_long_decorated_A", 6, 0, 6.5, 90), ("kaykit:chair", 5, 0, 5.3, 0), ("kaykit:chair", 7, 0, 7.7, 180),
    ("kaykit:barrel_large", 12.6, 0, -0.6, 0), ("kaykit:crates_stacked", 10.8, 0, -0.5, 20),
    ("kaykit:rubble_half", 0.2, 0, 9.0, 90), ("kaykit:column", 12, 0, 8, 0),
    *[(f"dungeon/floor_cave_large_{'ABCDEF'[(i * 3 + j) % 6]}", 22 + 4 * i, 0, 4 * j, 0) for i in range(3) for j in range(3)
      if (i, j) not in ((2, 1), (1, 2))],
    ("kaykit:floor_tile_large_rocks", 30, 0, 4, 0), ("kaykit:floor_dirt_large_rocky", 26, 0, 8, 0),
    ("dungeon/wall_cave_corner_A", 20, 0, -2, 90), ("dungeon/wall_cave_A", 24, 0, -2, 0),
    ("dungeon/wall_cave_doorway_A", 28, 0, -2, 0), ("dungeon/wall_cave_gate_leaf_A", 28 - .82, 0, -2, 70),
    ("dungeon/wall_cave_half_A", 30, 0, -2, 0), ("dungeon/wall_cave_B", 20, 0, 2, 90),
    ("dungeon/wall_cave_half_endcap_A", 20, 0, 6, 90), ("dungeon/wall_cave_pillar_A", 27, 0, 5, 0),
    ("kaykit:rubble_large", 30, 0, 9.3, 0), ("kaykit:rubble_half", 22.5, 0, 6.5, 40),
    ("kaykit:barrel_small_stack", 22, 0, 0, 0), ("kaykit:box_stacked", 25.5, 0, -0.4, 10),
]
TORCHES = [[-1.4, 2.8, 6], [4.3, 2.8, -1.4], [6, 2.2, 1.5]]


def showcase():
    """Render sample rooms mixing current studio tiles with KayKit pieces, to judge how they combine."""
    out = work_root(STUDIO) / "tiles/showcase"
    out.mkdir(parents=True, exist_ok=True)
    tiles, missing = [], set()
    for source, x, y, z, rotation in SHOWCASE:
        if source.startswith("kaykit:"):
            path = KAYKIT / f"{source[7:]}.gltf"
        else:
            # A tile's family is the longest family name its file stem starts with.
            tileset, stem = source.split("/")
            key = max((k for k in families() if k.startswith(tileset + "/") and stem.startswith(k.split("/")[1] + "_")),
                      key=len, default=None)
            path = Family(key).staged / f"{stem}.glb" if key else Path(source)
        if not path.exists():
            missing.add(source)
            continue
        tiles.append({"glb": str(path), "position": [x, y, z], "rotation": rotation})
    if missing:
        raise ValueError(f"Build these tiles' families first: {sorted(missing)}")
    game = [6.8, 11.5, 6.8]
    jobs = [{"tiles": tiles, "resolution": [1280, 960], "shots": [
                {"out": str(out / "hall.png"), "target": [5, 1, 3.5], "direction": game, "size": 17},
                {"out": str(out / "cave.png"), "target": [26, 1, 3.5], "direction": game, "size": 16},
                {"out": str(out / "back_wall.png"), "target": [6, 2, -2], "direction": [.15, .35, 1], "size": 9}]},
            {"tiles": tiles, "resolution": [1280, 960], "ambient": .12, "sun": 0.0,
             "lights": [{"position": p, "range": 7.5, "energy": 3.0} for p in TORCHES],
             "shots": [{"out": str(out / "hall_torchlit.png"), "target": [5, 1, 3.5], "direction": game, "size": 17}]}]
    save(out / "jobs.json", jobs)
    result = run_godot(["--script", str(STUDIO / "godot/render_tiles.gd"), "--", str(out / "jobs.json")], headless=False)
    shots = [shot["out"] for job in jobs for shot in job["shots"]]
    if result.count("TILE_SHOT ") != len(shots):
        raise ValueError(f"Godot did not render every showcase shot:\n{result}")
    sheet = Image.new("RGB", (1280, 960), (20, 23, 30))
    titles = ["Hall: studio and KayKit pieces", "Cave: studio walls and floor with KayKit rubble", "Hall back wall",
              "Hall by torchlight"]
    for i, (path, title) in enumerate(zip(shots, titles)):
        with Image.open(path) as image:
            panel = image.convert("RGB").resize((640, 480), Image.Resampling.LANCZOS)
        sheet.paste(panel, ((i % 2) * 640, (i // 2) * 480))
        ImageDraw.Draw(sheet).text(((i % 2) * 640 + 10, (i // 2) * 480 + 8), title, fill="white")
    sheet.save(out / "showcase.png")
    print(out / "showcase.png")


def families():
    return [f"{p.parent.name}/{p.stem}" for p in sorted(TILESETS.glob("*/*.json"))]


def write_catalog():
    """Recompose the tile overview from published lineups only."""
    root = STUDIO / "catalogs/tiles"
    entries = []
    for key in families():
        folder = root / key
        if not (folder / "manifest.json").exists():
            continue
        record = publication.read(folder, Family(key).fingerprint())
        entries.append((key, record))
    lines = ["# Tile catalog", "", "Published modular floor and wall families. Each lineup shows random variants on the game",
             "grid (4x4 and 2x2 floors mixed), the gameplay camera and every variant. Regenerate with",
             "`studio.py tile catalog`; rebuild and review a family with `tile build` and `tile lineup`.", "",
             "| Family | Name | Pieces | Current source | Lineup |", "| --- | --- | --- | --- | --- |"]
    for key, record in entries:
        recipe = record["metadata"]["recipe"]
        pieces = ", ".join(f"{p} x{len(v)}" for p, v in recipe["pieces"].items())
        lines.append(f"| `{key}` | {recipe['name']} | {pieces} | {'yes' if record['source_current'] else 'no'} | "
                     f"[lineup]({key}/lineup.png) |")
    root.mkdir(parents=True, exist_ok=True)
    (root / "README.md").write_text("\n".join(lines) + "\n")
    print(root / "README.md")


def main(argv=None):
    parser = argparse.ArgumentParser(prog="studio.py tile", description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("schema", help="Print the tile family recipe JSON schema")
    sub.add_parser("list", help="List tile family recipes")
    sub.add_parser("catalog", help="Recompose the tile catalog index from published lineups")
    sub.add_parser("showcase", help="Render sample rooms mixing current builds with KayKit pieces")
    for name in ("build", "lineup", "publish", "export"):
        cmd = sub.add_parser(name)
        cmd.add_argument("key", nargs="?")
        cmd.add_argument("--all", action="store_true")
        if name == "export":
            cmd.add_argument("--no-import", action="store_true")
            cmd.add_argument("--force", action="store_true")
    args = parser.parse_args(argv)
    try:
        if args.command == "schema":
            print(json.dumps(SCHEMA, indent=2))
        elif args.command == "list":
            for key in families():
                family = Family(key)
                data = family.recipe()
                pieces = ", ".join(f"{p} x{len(v)}" for p, v in data["pieces"].items())
                print(f"{key:36} {data['kind']:6} {data['style']:12} {pieces:40} {'built' if family.staged.exists() else '-'}")
        elif args.command == "catalog":
            write_catalog()
        elif args.command == "showcase":
            showcase()
        else:
            if bool(args.key) == args.all:
                raise ValueError("Provide one family key or --all")
            keys = families() if args.all else [args.key]
            failures = []
            for key in keys:
                family = Family(key)
                try:
                    if args.command == "export":
                        family.export(args.no_import, args.force)
                    else:
                        getattr(family, args.command)()
                except (ValueError, OSError, KeyError) as exc:
                    if not args.all:
                        raise
                    failures.append(f"{key}: {exc}")
            if args.command == "publish" and not failures:
                write_catalog()
            if failures:
                raise ValueError("\n".join(failures))
    except (ValueError, OSError, KeyError) as exc:
        parser.exit(1, f"Tile workflow: {exc}\n")
