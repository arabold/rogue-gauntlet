"""One current item catalog, composed from the canonical Godot lineup images."""

import json
import math
from collections import defaultdict
from core import publication
from PIL import Image, ImageDraw, ImageFont


def write_catalogs(items, destination):
    """Overwrite the overview, category sheets and current validation/export index."""
    from items.workflow import STUDIO, digest
    items = sorted((item for item in items if (destination / item.key / "manifest.json").exists()), key=lambda item: item.key)
    if not items:
        raise ValueError("No published items; use item publish <category/name>")
    destination.mkdir(parents=True, exist_ok=True)
    font = ImageFont.load_default(size=14)
    title_font = ImageFont.load_default(size=19)

    def thumbnail(item):
        path = destination / item.key / "lineup.png"
        if not path.exists():
            raise ValueError(f"Missing Godot lineup for {item.key}; run item lineup --all")
        with Image.open(path) as image:
            if image.height != 400 or image.width not in (1440, 1800):
                raise ValueError(f"Unexpected lineup layout: {path}; rerender it")
            index = image.width // 360 - 4 + 1
            return image.convert("RGB").crop((index * 360, 40, (index + 1) * 360, 400))

    def sheet(group, name, title, columns):
        width, height = 280, 280
        out = Image.new("RGB", (columns * width, math.ceil(len(group) / columns) * height + 42), (20, 23, 30))
        draw = ImageDraw.Draw(out)
        draw.text((12, 12), title, font=title_font, fill="white")
        for i, item in enumerate(group):
            x, y = i % columns * width, i // columns * height + 42
            image = thumbnail(item)
            image.thumbnail((280, 246), Image.Resampling.LANCZOS)
            out.paste(image, (x + (280 - image.width) // 2, y + 26))
            draw.text((x + 8, y + 4), item.id, font=font, fill="white")
        out.save(destination / name)

    categories = defaultdict(list)
    records = []
    shared = publication.shared_hashes(STUDIO)
    for item in items:
        categories[item.category].append(item)
        receipt = publication.read(destination / item.key, publication.fingerprint(STUDIO, item.path, shared))
        recipe = receipt["metadata"]["recipe"]
        report = receipt["metadata"]["report"]
        owner = item.folder / ".export.json"
        exported = json.loads(owner.read_text()) if owner.exists() else {}
        exported_current = (exported.get("godot_validated", False)
                            and exported.get("sha256") == receipt["files"][item.built.name]
                            and item.destination(recipe).exists()
                            and digest(item.destination(recipe)) == exported.get("sha256"))
        records.append({"key": item.key, "source_current": receipt["source_current"], "inputs": receipt["inputs"], "triangles": report["triangles"], "materials": report["materials"],
                        "source": recipe.get("source"), "surface": recipe.get("surface"),
                        "topology_policy": recipe.get("topology", "closed_assembly"),
                        "topology": report["topology"], "warnings": report["warnings"],
                        "export": recipe["export"]["glb"], "export_verified": exported_current,
                        "lineup": f"{item.key}/lineup.png"})
    for category, group in sorted(categories.items()):
        columns = 6 if len(group) > 18 else 4 if len(group) > 6 else 3
        sheet(group, category + ".png", category.upper() + " - character shading", columns)
    current_images = {category + ".png" for category in categories} | {"overview.png"}
    for path in destination.glob("*.png"):
        if path.name not in current_images:
            path.unlink()
    sheet(items, "overview.png", f"ITEM CATALOG - {len(items)} current assets", 7)
    status = {"items": len(items), "godot_verified_exports": sum(r["export_verified"] for r in records),
              "authored_surface_exceptions": [r["key"] for r in records if r["topology_policy"] == "authored_assembly"],
              "assets": records}
    (destination / "status.json").write_text(json.dumps(status, indent=2) + "\n")
    lines = ["# Item catalogs", "", f"Current catalog: **{len(items)} items**, using the shared character shading pipeline.", "",
             "[Complete visual overview](overview.png) · [Current validation and export status](status.json)", "",
             "| Category | Items | Catalog |", "| --- | ---: | --- |"]
    for category, group in sorted(categories.items()):
        lines.append(f"| {category.title()} | {len(group)} | [View {category}]({category}.png) |")
    lines += ["", "Each published item has a final GLB, fingerprint manifest and `lineup.png` in its category/name folder: front, three-quarter, side and gameplay-scale views.", "",
              f"Godot-verified current exports: **{status['godot_verified_exports']}/{len(items)}**. "
              f"Authored-surface exceptions still flagged for geometry review: **{len(status['authored_surface_exceptions'])}**. "
              "These exceptions are preserved original surfaces, not clean closed-mesh certifications.", "",
              "Regenerate these catalogs from published outputs with `./.venv/bin/python asset-studio/studio.py item catalog`. "
              "Use `item lineup` to render candidates in OS temporary storage, inspect them, then `item publish` to finalize. Rendering never updates this catalog. Changed sources set `source_current: false` until reviewed again.", "",
              "## Individual lineups", ""]
    for category, group in sorted(categories.items()):
        lines += [f"### {category.title()}", ""]
        lines += [f"- [{item.id}]({item.key}/lineup.png)" for item in group]
        lines.append("")
    (destination / "README.md").write_text("\n".join(lines))
    print(destination / "README.md")
