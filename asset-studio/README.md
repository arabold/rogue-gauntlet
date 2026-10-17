# Rogue Gauntlet Asset Studio (`asset-studio/`)

`asset-studio/` is the self-contained procedural 3D character, monster, and item generation studio for **Rogue Gauntlet**.

All interim steps — recipes, concept drawings, orthographic previews, lineup sheets, part studies, and weapon mesh caches — live inside `asset-studio/` (hidden from Godot's asset scanner via `.gdignore`). Only finished `.glb` and `.import` files are exported into the game's `assets/` directory (`assets/custom-heroes/`, `assets/custom-monsters/`, `assets/custom-items/`).

---

## Directory Layout

```text
asset-studio/
├── .gdignore                 # Keeps interim studio files out of Godot's asset importer
├── README.md                 # Architecture and usage guide
├── requirements.txt          # Python dependencies (numpy, scipy, Pillow, trimesh, etc.)
├── studio.py                 # Unified CLI entrypoint (list, schema, new, reference, preview, export, lineup, catalog)
├── core/                     # Core geometry, meshing, rigging, shading, and export engine
│   ├── kit.py                # Superellipse lofts, sweeps, hulls, bevel boxes, surface projection, battle damage
│   ├── mesh.py               # GLB reader, gradient/flat palette Atlas, color space & view splitting
│   ├── image3d.py            # Voxel remeshing, orientation, decimation, and software rasterizer
│   ├── rig.py                # 41-joint rig sampler, mannequin skin-weight transfer, Laplacian smoothing
│   ├── export.py             # Skinned glTF/GLB writer, crease-angle normals, PBR material presets, .import generator
│   ├── catalog.py            # Multi-angle contact-sheet renderer and stand-in hosts
│   ├── match.py              # Concept reference generator, silhouette/color IoU scorer, coordinate-descent tuner
│   ├── ai.py                 # Gemini / OpenAI / TRELLIS image & 3D providers
│   └── trellis_runner.py     # Remote GPU runner for image-to-3D reconstruction
├── prefabs/                  # Modular, parameterized 3D building blocks
│   ├── registry.py           # Introspectable JSON schema of all prefabs, presets, and parameters
│   ├── anatomy/              # 1. BODY PARTS & CREATURE ANATOMY
│   │   ├── heads.py          # Cranium shapes (round, tall, brute, blocky, snout, skull, flat), head presets & smoothing
│   │   ├── faces.py          # Eyes (beads, female lashes, socket, glow, slit, cyclops), noses (wedge, boxer, lump,
│   │   │                     #   hooked, bulb, button, snout, slits), mouths, jaws, tusks, beards, ears, horns,
│   │   │                     #   hair, wraps, scars, and warpaint
│   │   ├── bodies.py         # Torso trunks (standard, hero, female, brute, round, skinny, goblin) and body assembly
│   │   ├── limbs.py          # Arm and leg limb tubes (normal, thin, muscular, gorilla, bony, stocky)
│   │   ├── extremities.py    # Hands (mitten, fist, claw, bony) and feet (boot, bare, paw)
│   │   └── appendages.py     # Wings (bat, scalloped), tails (spade, whip), exposed ribs, curved beast horns
│   ├── outfits/              # 2. WEARABLE EQUIPMENT (ARMOR & CLOTHES)
│   │   ├── clothes.py        # Tunics, robes, sleeves, shorts, loincloths, aprons, collars, cuffs, wraps
│   │   ├── cuirass.py        # Fitted plate cuirasses (knight, orc, corset)
│   │   ├── armor.py          # Breastplates, backplates, lames, mail, scales, tabards, faulds, limb plates, cops
│   │   ├── helmets.py        # Helmets (great, nasal, horned, kettle, skull, hood, wizard)
│   │   ├── crowns.py         # Crowns, circlets, diadems, halos
│   │   ├── pauldrons.py      # Layered, spiked, and skull shoulder armor
│   │   └── belts.py          # Belts, sashes, ropes, chains, buckles, pouches, trophies
│   ├── items/                # 3. HELD & WORLD ITEMS
│   │   ├── weapons.py        # 1H/2H swords, 1H/double battleaxes, assassin daggers, arcane wands, crossbows
│   │   ├── shields.py        # Heater, kite, round, and spiked shields
│   │   └── props.py          # Spellbooks (closed/open), potion flasks
│   ├── details/              # 4. SMALL ACCENTS & FASTENERS ONLY
│   │   ├── fasteners.py      # Rivets, studs, frame buckles, plate buckles, chain links, shackles, cuffs, straps
│   │   ├── trophies.py       # Mini skulls, faceted gems, horn trophies, scarabs, pouches, sash knots, emblems
│   │   └── wear.py           # Battle damage gashes, surface weathering, carvings, paint cuts
│   ├── harvest.py            # Automated CC0 model harvester routing into domain meshes/
│   ├── library.py            # Unified loader, scaler, recolorer, and visual index generator
│   └── creatures/            # Full character recipe loaders and monster builders
├── items/                    # Procedural & AI-assisted 3D item/weapon/equipment pipeline
│   ├── build_item.py         # 5-stage item generator (.glb + wrapper .tscn + .tres + Godot import)
│   ├── align.py              # Low-poly item builders, palette UV mapper, grip/AABB alignment
│   └── hf_image3d.py         # Image-to-3D mesh reconstruction helper
├── characters/               # Per-character folders (<family>/<name>/character.json, reference.png, lineup.png)
├── catalogs/                 # Dedicated home for domain catalog contact sheets
├── rigs/                     # Self-contained base rigs, mannequins, animation templates, and item palette
└── godot/                    # Headless/windowed GDScript helpers for inspection and catalog rendering
```

---

## Quick Start CLI

Run all commands using the workspace `.venv`:

```bash
# List all characters and their export status
./.venv/bin/python asset-studio/studio.py list

# Inspect the modular prefab schema (used by CLI and future Godot creator UI)
./.venv/bin/python asset-studio/studio.py schema
./.venv/bin/python asset-studio/studio.py schema anatomy
./.venv/bin/python asset-studio/studio.py schema items
./.venv/bin/python asset-studio/studio.py schema details

# Render domain contact sheets into asset-studio/catalogs/
./.venv/bin/python asset-studio/studio.py catalog anatomy
./.venv/bin/python asset-studio/studio.py catalog outfits
./.venv/bin/python asset-studio/studio.py catalog items
./.venv/bin/python asset-studio/studio.py catalog details

# Render a character's animated multi-pose lineup into asset-studio/characters/<family>/<name>/lineup.png
./.venv/bin/python asset-studio/studio.py lineup mage/necromancer

# Export a character's rigged GLB + .import (automatically generates lineup.png)
./.venv/bin/python asset-studio/studio.py export mage/necromancer

# Build and integrate a 3D item/weapon/consumable
./.venv/bin/python asset-studio/items/build_item.py \
  --id battleaxe_horned \
  --name "Horned Battle Axe" \
  --archetype battleaxe \
  --tier 3
```

---

## Blender MCP & Godot Creator Tool Integration

- **Blender MCP (`blender-mcp`)**: Any `kit.Piece` or exported `.glb` can be loaded into Blender via MCP for custom adjustments, boolean bevels, NLA animation tweaks, or lookdev, and saved back into domain `meshes/` via `harvest.py` or exported to `assets/`.
- **Godot Character/Monster Creator Tool**: `prefabs/registry.py` (`studio.py schema`) exposes every prefab category, preset list, and parameter signature as structured JSON, and `characters/<family>/<name>/character.json` is a declarative recipe format designed so an in-engine Godot visual editor can inspect, tweak, preview, and export characters directly.
