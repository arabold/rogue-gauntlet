"""Modular uncolored mesh prefab loader and index generator.

Provides unified loading, scaling, positioning, and on-demand recoloring for curated
geometric prefabs embedded directly inside their respective domain folders:
- `prefabs/anatomy/meshes/`: skulls, skeletal parts
- `prefabs/outfits/meshes/`: helmets, hats
- `prefabs/items/meshes/`: weapons, shields, props
- `prefabs/details/meshes/`: gems, horns
- `prefabs/decor/meshes/`: colored KayKit decor (books, bottles, candles, coins, keys, skull); see `prefabs/decor.py`
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

from core import catalog as cat_renderer, kit

PREFABS_DIR = Path(__file__).resolve().parent

DOMAINS = {
    "anatomy": PREFABS_DIR / "anatomy" / "meshes",
    "outfits": PREFABS_DIR / "outfits" / "meshes",
    "items": PREFABS_DIR / "items" / "meshes",
    "details": PREFABS_DIR / "details" / "meshes",
    "decor": PREFABS_DIR / "decor" / "meshes",
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
    "decor": "decor",
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

            if "colors" in meta:
                # Decor prefabs keep their own color regions.
                piece = kit.Piece(data["verts"].astype(float), data["faces"].astype(np.int64), data["slots"].astype(object))
                yield f"{name} ({cat})", piece, None, {k: tuple(v) for k, v in meta["colors"].items()}
                continue
            if name in ITEM_COLORS:
                slot, col = ITEM_COLORS[name]
            elif cat in PREF_COLORS:
                slot, col = PREF_COLORS[cat]
            else:
                slot, col = "base", (160, 160, 160)

            piece = kit.Piece(data["verts"].astype(float), data["faces"].astype(np.int64), np.full(len(data["faces"]), slot, dtype=object))
            yield f"{name} ({cat})", piece, None, {slot: col}


def generate_index():
    """Write source metadata tables without creating previews in authoring folders."""
    for domain, d_path in DOMAINS.items():
        files = sorted(d_path.glob("*.npz"))
        lines = [f"# {domain.capitalize()} mesh sources", "",
            "Reusable harvested source geometry. These are parts, not complete costumes.", "",
            f"[Published part catalog](../../catalogs/prefabs/{domain}.png) · "
            "[Complete costumes](../../costumes/README.md)", "",
            "Generate preview candidates with `studio.py catalog " + domain + "`. "
            "After review, publish with `studio.py catalog " + domain + " --publish`.", "",
            "| Prefab | Triangles | Vertices | Dimensions (W × H × D) |",
            "| --- | ---: | ---: | --- |"]
        for path in files:
            with np.load(path) as data:
                meta = json.loads(str(data["meta"]))
            span = meta.get("span", [0, 0, 0])
            dims = " × ".join(f"{v:.2f}" for v in span)
            lines.append(f"| `{meta['name']}` | {meta['tris']} | {meta['verts']} | {dims} m |")
        (PREFABS_DIR / domain / "README.md").write_text("\n".join(lines) + "\n")
        print(f"{domain}: indexed {len(files)} source meshes (no renders)")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--index", action="store_true", help="Regenerate source metadata indexes without rendering")
    args = parser.parse_args()
    if args.index:
        generate_index()
    else:
        print(json.dumps(list_prefabs(), indent=2))
