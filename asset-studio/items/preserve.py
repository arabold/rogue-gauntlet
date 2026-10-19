"""Adopt original catalog detail into the shared character surface workflow."""

import subprocess
import numpy as np
from core.export import DEFAULT_MATERIALS
from items.source_surface import palette_piece
from items.source_repair import repair

METALS = {"steel", "silver", "blue_steel", "dark_iron"}
GOLDS = {"gold", "amber", "brass"}
ORGANIC = {"wood", "copper", "bark", "tan"}
COLORS = {"crimson", "rose", "magenta", "purple", "teal", "lime", "coral", "sea_green",
          "green", "sky_blue", "orange", "cobalt", "jade", "emerald", "obsidian"}


def appearance(category, name, swatches, authored=False):
    """Persist editable material roles by catalog family while keeping palette regions distinct."""
    slots, colors, materials = {}, {}, {}
    for swatch, rgb in swatches.items():
        role = "surface"
        if swatch in METALS:
            role = "iron" if swatch == "dark_iron" else "metal"
        elif swatch in GOLDS:
            role = "gold"
        elif swatch in ORGANIC:
            role = "leather"
        elif swatch == "ivory":
            role = "bone"
        if category == "food":
            role = "surface"
        elif category == "scrolls":
            role = "paper" if swatch in ORGANIC | {"ivory"} else "ink" if swatch in COLORS else role
        elif category == "jewelry":
            role = "gem" if swatch in COLORS else role
            if "wood" in name and swatch in ORGANIC | GOLDS:
                role = "leather"
            elif "gold" in name and swatch in ORGANIC | GOLDS:
                role = "gold"
        elif category == "keys":
            # Shape/material differences identify keys; no colored gem accents.
            role = "bone" if "bone" in name and swatch == "ivory" else "gold" if any(t in name for t in ["gold", "bronze"]) else "iron"
            base = np.array([192, 148, 69] if role == "gold" else [214, 206, 184] if role == "bone" else [155, 164, 172])
            value = float(np.dot(np.asarray(rgb) / 255, [.2126, .7152, .0722]))
            rgb = np.clip(base * (.65 + .40 * value), 0, 255).round().astype(int).tolist()
        elif category == "potions":
            role = "glass" if swatch in COLORS | {"blue_steel"} else "paper" if swatch == "ivory" else "cork" if swatch in ORGANIC else role
        elif category == "tools":
            if any(t in name for t in ["candle", "candelabrum"]):
                if swatch in {"ivory", "tan"}:
                    role = "wax"
                elif swatch in {"bark", "obsidian"}:
                    role = "wick"
            if any(t in name for t in ["bell", "mirror", "bugle"]) and swatch in GOLDS | {"copper"}:
                role = "metal" if "silver" in name or "mirror" in name else "gold"
        slot = f"{role}_{swatch}"
        slots[swatch] = slot
        colors[slot] = [int(x) for x in rgb]
        spec = dict(DEFAULT_MATERIALS.get(role, {"metallic": 0, "roughness": .95, "crease": 55}))
        if role in {"wax", "paper", "cork", "wick", "ink"}:
            spec = {"metallic": 0, "roughness": 1, "crease": 60}
        elif role == "gem":
            spec = {"metallic": .1, "roughness": .25, "crease": 45}
        elif role == "glass":
            spec = {"metallic": 0, "roughness": .2, "crease": 60, "alpha": .38 if swatch == "blue_steel" else .72,
                    "double_sided": True}
        if authored:
            spec["double_sided"] = True
        materials[slot] = spec
    return slots, colors, materials


def preserve(item, revision="HEAD"):
    """Copy original art from an explicit Git revision; never overwrite an unrelated local source."""
    from items.workflow import REPO, PALETTE, save, digest
    data = item.recipe()
    if data.get("provenance", {}).get("kind") == "original_geometry_character_surface":
        return False
    if data.get("provenance", {}).get("kind") == "studio_refinement":
        return False
    path = data["export"]["glb"].removeprefix("res://")
    revision = subprocess.check_output(["git", "rev-parse", revision], cwd=REPO, text=True).strip()
    source_bytes = subprocess.check_output(["git", "show", revision + ":" + path], cwd=REPO)
    source = item.folder / "source.glb"
    if source.exists() and source.read_bytes() != source_bytes:
        raise ValueError(f"{source}: unrelated local source exists; inspect before replacing")
    if not source.exists():
        source.write_bytes(source_bytes)
    piece, swatches = palette_piece(source, PALETTE)
    original_triangles = len(piece.faces)
    marking = None
    if item.category == "scrolls":
        from items.surface_marks import scroll_ink
        piece, marking = scroll_ink(piece)
    fixed, repairs = repair(piece)
    topology = repairs["topology"]
    authored = bool(topology["boundary_edges"] or topology["nonmanifold_edges"])
    slots, colors, materials = appearance(item.category, item.id, swatches, authored)
    if marking:
        materials[slots[marking["ink_slot"]]] = {"metallic": 0, "roughness": 1, "crease": 60}
    for key in ["builder", "design", "archetype", "palette"]:
        data.pop(key, None)
    data.update(backend="source", source="source.glb", placement={"pivot": "authored", "scale": 1},
                topology="authored_assembly" if authored else "closed_assembly", color_mode="character",
                surface={"palette": "studio_grid", "slots": slots, "repair": "boundaries"},
                colors=colors, materials=materials, crease=50)
    if marking:
        data["surface"]["paint"] = "scroll_ink"
    data["budget"] = {"triangles": 2400 if len(fixed.faces) <= 2400 else 4096, "materials": 8, "texture_size": 2048}
    data["provenance"] = {"kind": "original_geometry_character_surface", "git_revision": revision,
                          "original_path": path, "source_sha256": digest(source), "original_triangles": original_triangles,
                          "quality_notes": "Original forms and color regions retained. See build/source-repair.json for cap/winding corrections and build/validation.json for authored-surface exceptions."}
    save(item.path, data)
    return True
