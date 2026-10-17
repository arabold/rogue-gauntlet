"""Modular uncolored mesh prefab loader and index generator.

Provides unified loading, scaling, positioning, and on-demand recoloring for curated
geometric prefabs embedded directly inside their respective domain folders:
- `prefabs/anatomy/meshes/`: skulls, skeletal parts
- `prefabs/outfits/meshes/`: helmets, hats
- `prefabs/items/meshes/`: weapons, shields, props
- `prefabs/details/meshes/`: gems, horns
"""

import argparse
import json
import os
from pathlib import Path
import sys

STUDIO_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if STUDIO_DIR not in sys.path:
    sys.path.insert(0, STUDIO_DIR)

import numpy as np
from PIL import Image

from core import catalog as cat_renderer, kit

PREFABS_DIR = Path(__file__).resolve().parent

DOMAINS = {
    "anatomy": PREFABS_DIR / "anatomy" / "meshes",
    "outfits": PREFABS_DIR / "outfits" / "meshes",
    "items": PREFABS_DIR / "items" / "meshes",
    "details": PREFABS_DIR / "details" / "meshes",
}

CATEGORY_TO_DOMAIN = {
    "skulls": "anatomy",
    "skeletal": "anatomy",
    "headgear": "outfits",
    "weapons": "items",
    "shields": "items",
    "props": "items",
    "gems": "details",
    "horns": "details",
}

PREF_COLORS = {
    "skulls": ("bone", (232, 226, 212)),
    "skeletal": ("bone", (232, 226, 212)),
    "horns": ("horn", (224, 210, 180)),
    "gems": ("gem", (80, 220, 240)),
    "shields": ("armor", (140, 150, 160)),
    "headgear": ("cloth", (160, 60, 50)),
    "props": ("leather", (140, 95, 65)),
    "weapons": ("metal", (170, 180, 190)),
}

ITEM_COLORS = {
    "knight_helmet": ("metal", (170, 180, 190)),
    "flask_potion": ("glass", (90, 180, 230)),
    "wand_arcane": ("wood", (135, 95, 60)),
    "crossbow_1h": ("wood", (135, 95, 60)),
}


def find_prefab_path(name):
    """Locate `{name}.npz` across all domain mesh folders."""
    filename = f"{name}.npz"
    for domain, d_path in DOMAINS.items():
        candidate = d_path / filename
        if candidate.exists():
            return candidate
    return None


def list_prefabs():
    """Return a dict of available prefabs grouped by domain and category."""
    out = {}
    for domain, d_path in DOMAINS.items():
        if not d_path.exists():
            continue
        for p in sorted(d_path.glob("*.npz")):
            data = np.load(p)
            meta = json.loads(str(data["meta"]))
            cat = meta.get("category", domain)
            out.setdefault(domain, {}).setdefault(cat, {})[meta["name"]] = meta
    return out


def load(name, slot="base", scale=1.0, offset=(0.0, 0.0, 0.0), slot_map=None):
    """
    Load an uncolored prefab mesh as a `kit.Piece`, scaled and placed at `offset`.

    Parameters:
        name: Prefab name (e.g. 'skull_complete', 'gem_diamond', 'shield_heater')
        slot: Default material slot to assign (e.g. 'bone', 'gold', 'gem', 'metal')
        scale: Uniform scale multiplier or (sx, sy, sz)
        offset: (x, y, z) translation
        slot_map: Optional dict to map original slots to target materials
    """
    path = find_prefab_path(name)
    if path is None:
        raise FileNotFoundError(f"Prefab {name!r} not found in any domain mesh folders")

    data = np.load(path)
    verts = data["verts"].astype(float)
    faces = data["faces"].astype(np.int64)
    orig_slots = data["slots"]

    # Scale and translate
    s = np.asarray(scale, float)
    verts = verts * s + np.asarray(offset, float)

    # Remap slots or assign default
    if slot_map:
        slots = np.array([slot_map.get(str(s), slot) for s in orig_slots], dtype=object)
    else:
        slots = np.full(len(faces), slot, dtype=object)

    return kit.Piece(verts, faces, slots)


def catalog():
    """Yield every harvested mesh prefab across all domains for catalog rendering."""
    for domain, d_path in DOMAINS.items():
        if not d_path.exists():
            continue
        for p in sorted(d_path.glob("*.npz")):
            data = np.load(p)
            meta = json.loads(str(data["meta"]))
            name = meta["name"]
            cat = meta.get("category", domain)

            if name in ITEM_COLORS:
                slot, col = ITEM_COLORS[name]
            elif cat in PREF_COLORS:
                slot, col = PREF_COLORS[cat]
            else:
                slot, col = "base", (160, 160, 160)

            piece = kit.Piece(data["verts"].astype(float), data["faces"].astype(np.int64), np.full(len(data["faces"]), slot, dtype=object))
            yield f"{name} ({cat})", piece, None, {slot: col}


def generate_index():
    """Generate visual preview strips and Markdown tables for each domain."""
    print("Generating visual domain indexes...")

    domain_titles = {
        "anatomy": "Anatomy & Skeletal Mesh Prefabs",
        "outfits": "Outfits & Headgear Mesh Prefabs",
        "items": "Items & Equipment Mesh Prefabs",
        "details": "Details & Hardware Mesh Prefabs",
    }

    for domain, d_path in DOMAINS.items():
        if not d_path.exists():
            continue
        files = sorted(d_path.glob("*.npz"))
        if not files:
            continue

        prev_dir = d_path / "previews"
        prev_dir.mkdir(exist_ok=True)

        lines = [
            f"# {domain_titles.get(domain, domain.capitalize())}",
            "",
            f"Curated uncolored 3D geometry prefabs for `{domain}`.",
            "",
            "| Preview | Prefab Name | Tris / Verts | Dimensions (W × H × D) | Example Code |",
            "| :---: | :--- | :---: | :---: | :--- |",
        ]

        for p in files:
            data = np.load(p)
            meta = json.loads(str(data["meta"]))
            name = meta["name"]
            cat = meta.get("category", domain)

            if name in ITEM_COLORS:
                slot, col = ITEM_COLORS[name]
            elif cat in PREF_COLORS:
                slot, col = PREF_COLORS[cat]
            else:
                slot, col = "base", (160, 160, 160)

            piece = kit.Piece(data["verts"].astype(float), data["faces"].astype(np.int64), np.full(len(data["faces"]), slot, dtype=object))

            # Render 2-view preview strip (front + 3/4 isometric)
            cell = 140
            im1 = cat_renderer.render(piece, yaw=0.0, pitch=10.0, size=cell, colors={slot: col})
            im2 = cat_renderer.render(piece, yaw=32.0, pitch=18.0, size=cell, colors={slot: col})
            strip = Image.new("RGB", (cell * 2 + 2, cell), (255, 255, 255))
            strip.paste(im1, (0, 0))
            strip.paste(im2, (cell + 2, 0))
            strip_path = prev_dir / f"{name}.png"
            strip.save(strip_path)

            span = meta.get("span", [0, 0, 0])
            dim_str = f"{span[0]:.2f} × {span[1]:.2f} × {span[2]:.2f}m"
            code_str = f"`library.load(\"{name}\", slot=\"{slot}\")`"
            lines.append(
                f"| ![{name}](meshes/previews/{name}.png) | **`{name}`** | {meta['tris']} tris<br>{meta['verts']} verts | {dim_str} | {code_str} |"
            )

        readme_path = PREFABS_DIR / domain / "README.md"
        readme_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
        print(f"  [+] Wrote {domain}/README.md ({len(files)} prefabs)")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--index", action="store_true", help="Regenerate domain preview strips and indexes")
    args = parser.parse_args()
    if args.index:
        generate_index()
    else:
        print(json.dumps(list_prefabs(), indent=2))
