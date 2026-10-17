---
name: assets
description: Use when creating, customizing, previewing, or exporting 3D characters, monsters, items, or accessories using the project's `asset-studio/` pipeline (and `blender-mcp` when needed), or when fetching external CC0 3D asset packs.
---

# Asset Studio & 3D Asset Pipeline

All procedural 3D character, monster, and item generation code, modular prefabs, character recipes, interim previews, and helper scripts live in the root [`asset-studio/`](file:///Users/andrerabold/Projects/Personal/rogue-gauntlet/asset-studio) folder.

> [!IMPORTANT]
> - **Interim vs. Export Separation**: Keep all interim steps (recipes, concept art, previews, lineups, part studies, weapon mesh caches) inside `asset-studio/` (which contains a `.gdignore` so Godot ignores it). Only the `export` step writes finished `.glb` and `.import` files into `assets/custom-heroes/`, `assets/custom-monsters/`, or `assets/custom-items/`.
> - **Brand-Neutral Code**: Never use external asset-creator brand names in `asset-studio/` code, preset names, or comments. Use our own naming (`standard`, `heroic`, `stylized`, etc.).

---

## 1. Architecture of `asset-studio/`

- [`asset-studio/studio.py`](file:///Users/andrerabold/Projects/Personal/rogue-gauntlet/asset-studio/studio.py): Unified CLI entrypoint for listing, inspecting schemas, creating recipes, rendering previews/catalogs, and exporting rigged `.glb` models.
- [`asset-studio/core/`](file:///Users/andrerabold/Projects/Personal/rogue-gauntlet/asset-studio/core): Core geometry & lofting engine (`kit.py`), palette/atlas builder (`mesh.py`), voxel/mesh utilities (`image3d.py`), 41-joint rig & weight transfer (`rig.py`), glTF/GLB exporter (`export.py`), orthographic renderer (`catalog.py`), and concept matcher (`match.py`).
- [`asset-studio/prefabs/`](file:///Users/andrerabold/Projects/Personal/rogue-gauntlet/asset-studio/prefabs): Modular, parameterized 3D building blocks:
  - [`prefabs/registry.py`](file:///Users/andrerabold/Projects/Personal/rogue-gauntlet/asset-studio/prefabs/registry.py): Machine-readable JSON schema of all prefabs, styles, and parameter signatures (powering `studio.py schema` and future Godot visual character/monster creator tools).
  - **`prefabs/anatomy/`**:
    - `heads.py` — Cranium shapes (`round`, `tall`, `brute`, `blocky`, `snout`, `skull`, `flat`), head presets, and head-to-feature normal smoothing.
    - `faces.py` — Intricate facial details: `eyes` (`beads`, `female` with lashes, `socket`, `slit`, `glow`, `cyclops`), `nose` (`wedge`, `boxer`, `lump`, `hooked`, `bulb`, `button`, `snout`, `slits` with adjustable `size`, `width`, `length`, `depth`), `mouth`, `jaw`, `block_jaw`, `tusks`, `beard`, `ears`, `horns`, `hair`, `wraps`, `scar`, `paint`, `WARPAINT`.
    - `bodies.py` — Torso trunks (`standard`, `hero`, `female`, `brute`, `round`, `skinny`, `goblin`).
    - `limbs.py` — Arm and leg tubes (`normal`, `thin`, `muscular`, `gorilla`, `bony`, `stocky`).
    - `extremities.py` — Hands (`mitten`, `fist`, `claw`, `bony`) and feet (`boot`, `bare`, `paw`).
    - `appendages.py` — Wings (`bat`, `scalloped`), tails (`spade`, `whip`), exposed ribs, curved beast horns.
    - `meshes/` — Reusable harvested meshes (e.g. skulls, jaw, ribcage).
  - **`prefabs/outfits/`**:
    - `clothes.py`, `cuirass.py`, `armor.py`, `helmets.py`, `crowns.py`, `pauldrons.py`, `belts.py`.
    - `meshes/` — Reusable harvested meshes (e.g. helmets, wizard hats).
  - **`prefabs/items/`**:
    - `weapons.py`, `shields.py`, `props.py`.
    - `meshes/` — Reusable harvested meshes (e.g. swords, axes, daggers, wands, crossbow, shields, tomes, flasks).
  - **`prefabs/details/`**:
    - `fasteners.py` (rivets, studs, frame/plate buckles, chain links, shackles, cuffs, straps).
    - `trophies.py` (gems, horn trophies, scarabs, pouches, sash knots, emblems).
    - `wear.py` (battle damage gashes, surface weathering, carvings).
    - `meshes/` — Reusable harvested meshes (e.g. faceted gems, curved horns).
  - **`prefabs/creatures/`**: `characters.py` (layered recipe assembler + covered-skin culling) and `golems.py`.
  - **`prefabs/library.py` & `prefabs/harvest.py`**: Unified mesh loader, automatic domain router, and markdown catalog index generator (`prefabs/library.py --index`).
- [`asset-studio/catalogs/`](file:///Users/andrerabold/Projects/Personal/rogue-gauntlet/asset-studio/catalogs): Visual multi-angle contact sheets for each domain (`anatomy_catalog.png`, `outfits_catalog.png`, `items_catalog.png`, `details_catalog.png`).
- [`asset-studio/items/`](file:///Users/andrerabold/Projects/Personal/rogue-gauntlet/asset-studio/items): 5-stage 3D item/weapon/consumable builder (`build_item.py`, `align.py`, `hf_image3d.py`).
- [`asset-studio/characters/`](file:///Users/andrerabold/Projects/Personal/rogue-gauntlet/asset-studio/characters): Per-character folders (`<family>/<name>/character.json`, `reference.png`, `lineup.png`, `weapons.json`).
- [`asset-studio/rigs/`](file:///Users/andrerabold/Projects/Personal/rogue-gauntlet/asset-studio/rigs) & [`asset-studio/godot/`](file:///Users/andrerabold/Projects/Personal/rogue-gauntlet/asset-studio/godot): Self-contained base rigs, mannequins, animation templates, item palette, and Godot inspection/render scripts.

---

## 2. Common Workflows

Always run `asset-studio` commands with `./.venv/bin/python`:

### Inspect Available Prefabs & Parameters
```bash
./.venv/bin/python asset-studio/studio.py list
./.venv/bin/python asset-studio/studio.py schema
./.venv/bin/python asset-studio/studio.py schema anatomy
./.venv/bin/python asset-studio/studio.py schema outfits
./.venv/bin/python asset-studio/studio.py schema items
./.venv/bin/python asset-studio/studio.py schema details
```

### Build, Inspect Lineup, and Export a Character or Monster
1. Edit or create `asset-studio/characters/<family>/<name>/character.json` (or run `./.venv/bin/python asset-studio/studio.py new <family>/<name> --base <family>/<base>`).
2. Render the multi-pose animated lineup (`asset-studio/characters/<family>/<name>/lineup.png`):
   ```bash
   ./.venv/bin/python asset-studio/studio.py lineup <family>/<name>
   ```
3. Export the rigged `.glb` + `.import` into `assets/` (which automatically updates `lineup.png` and validates in Godot):
   ```bash
   ./.venv/bin/python asset-studio/studio.py export <family>/<name>
   ```

### Render Prefab Contact Sheets & Rebuild Visual Indices
```bash
# Render domain contact sheets into asset-studio/catalogs/
./.venv/bin/python asset-studio/studio.py catalog anatomy
./.venv/bin/python asset-studio/studio.py catalog outfits
./.venv/bin/python asset-studio/studio.py catalog items
./.venv/bin/python asset-studio/studio.py catalog details

# Rebuild markdown visual tables & previews in prefabs/*/README.md
./.venv/bin/python asset-studio/prefabs/library.py --index
```

### Generate & Integrate a 3D Item / Weapon / Consumable
```bash
./.venv/bin/python asset-studio/items/build_item.py \
  --id battleaxe_horned \
  --name "Horned Battle Axe" \
  --archetype battleaxe \
  --tier 3
```

### Using `blender-mcp` with Asset Studio
When a part or prop needs custom sculpting, boolean bevels, or lookdev in Blender:
1. Export or assemble the base mesh via `asset-studio/studio.py` or `core/kit.py`.
2. Import into Blender via `blender-mcp` tools to refine geometry, sculpt organic details, or inspect shading.
3. Save or harvest parts back into domain `meshes/` via `asset-studio/prefabs/harvest.py` (or export the finished `.glb` into `assets/`).
