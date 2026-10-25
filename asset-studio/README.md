# Rogue Gauntlet Asset Studio (`asset-studio/`)

`asset-studio/` is the self-contained procedural 3D character, monster, and item generation studio for **Rogue Gauntlet**.

Authoring recipes, original models and selected concepts live inside `asset-studio/`, hidden from Godot by `.gdignore`. **All generated candidates, caches, render frames, comparisons and job manifests live in OS temporary storage outside the repository.** `core.paths.work_root()` reports the checkout-specific location. Temporary storage may be deleted at any time; rebuild candidates when needed.

Final outputs live in `catalogs/` and change only through explicit publication. Rendering never publishes. `export` separately writes finished game art to `assets/custom-heroes/`, `assets/custom-monsters/` or `assets/custom-items/`. Existing game shield paths remain `assets/custom-items/armor/shield/` for compatibility.

The studio owns **3D art only**. Damage, stats, equipment rules, item identities, effects, `.tres` definitions, and gameplay `.tscn` composition belong to a separate Godot resource-authoring step. Godot import/mesh validation here verifies the exported art. The agreed [monster authoring workflow](../docs/monster-authoring-design.md) and [first-realm acceptance](../docs/development-roadmap.md#first-three-floor-acceptance) compose gameplay separately in the Godot editor; monster balance, tactics and realm eligibility do not belong in Studio JSON.

## Current catalogs and folder policy

Start at **[catalogs/items/README.md](catalogs/items/README.md)** for every recreated item, category sheets, individual Godot lineups and current validation/export status. [catalogs/items/overview.png](catalogs/items/overview.png) shows the complete catalog. Reusable prefab catalogs live separately in [catalogs/prefabs/](catalogs/prefabs/README.md).

| Location | Keep |
| --- | --- |
| `objects/<category>/<name>/` | `item.json`, original `source.glb`/`source.blend`, selected concept and exact prompt, export ownership receipt |
| `characters/<family>/<name>/` | Character recipe and selected reference/source assets |
| `costumes/<name>/` | Art-only outfit recipe, construction notes and selected concept/prompt |
| `catalogs/items/<category>/<name>/` | Final GLB, Godot lineup and fingerprint manifest |
| `catalogs/costumes/<name>/` | Final wearable GLB, Godot fitting lineup, walk/attack GIF and fingerprint manifest |
| `catalogs/characters/` | Current selected previews; bound/golem entries also keep the final GLB, motion, art metadata and fingerprint receipt |
| `catalogs/prefabs/` | Current reusable-part catalogs |
| OS temporary work root | Every build, technical report, software preview, review comparison, weapon cache and render job |

`catalogs/items/` and `catalogs/costumes/` also keep an overview, useful family/category sheets, README and status index. Barbarian and orc concepts are now selected; their model candidates stay outside the final catalog until review. `gorget_study` is an unpublished draft recipe. Legacy armor previews and ad hoc comparison sheets do not belong in the catalog.

The hidden `.export.json` beside an item recipe records ownership of its game export. Publication manifests store hashes of final files and the recipe/modeling/shading/rig inputs used for review. `source_current: false` means the final art predates source changes; it does not silently approve the new source. Catalog regeneration reads published files and needs no build cache. If a candidate changes after rendering, publication rejects it until rebuilt and rendered again.

```bash
# Iterate in temporary storage, inspect the resulting stills and motion, then finalize:
./.venv/bin/python asset-studio/studio.py armor compare costume_knight
./.venv/bin/python asset-studio/studio.py armor publish costume_knight
./.venv/bin/python asset-studio/studio.py item build shields/shield_dragon_scale
./.venv/bin/python asset-studio/studio.py item lineup shields/shield_dragon_scale --mount shield
./.venv/bin/python asset-studio/studio.py item publish shields/shield_dragon_scale
# Refresh indexes from final artifacts only, without rendering or importing:
./.venv/bin/python asset-studio/studio.py armor catalog
./.venv/bin/python asset-studio/studio.py item catalog
```


---

## Creature art and rig exports

Wolf, rat and stone golem use the same normal character entry points as heroes and other
monsters. Sources and rig/animation inputs are local to the Studio; no donor model or
previous game export is needed for these builds.

```bash
./.venv/bin/python asset-studio/studio.py lineup beasts/wolf beasts/rat golem/stone_golem
./.venv/bin/python asset-studio/studio.py export beasts/wolf beasts/rat golem/stone_golem
./.venv/bin/python asset-studio/studio.py audit
```

`export` writes the finished GLB, deliberate Godot import settings and art metadata to the
recipe's configured game destination, verifies the actual import, and updates the current
character catalog. `lineup` only stages temporary GPU reviews. Source/skin fidelity,
constant reset tracks, clip roles and loop settings are validated without humanoid node
assumptions. `--no-import` explicitly defers Godot verification.

The selected [wolf](characters/beasts/wolf/README.md) and [rat](characters/beasts/rat/README.md)
are bound sources with editable named palettes and optional additional uniform scale. The
[golem](characters/golem/stone_golem/README.md) exports rigid stone parts and baked shoulder
helpers on its local large rig. No enemy scenes or gameplay Resources are generated.

`beasts/spider` is a registered surface-polished review candidate using the same
bound-source workflow. Its original proportions and five clips are preserved;
`lineup beasts/spider` previews its charcoal shell and red-eye palette.

The [Forsaken Warden](characters/bosses/forsaken_warden/README.md) is a large-rig
boss assembled from the approved plate vocabulary, with glowing eyes and a broad
executioner blade. `lineup bosses/forsaken_warden` and `export bosses/forsaken_warden`
use the same character workflow; the GLB keeps sword/shield/horn as separate meshes.

See [Studio architecture](ARCHITECTURE.md) for source ownership, export boundaries and the
remaining older weapon-preview dependencies. [Technical creature rigs](rigs/creatures/README.md)
cover blob/spider/wolf/rat extraction and explicit external-donor tools. The old
`creature study` builders are superseded by the normal production recipes.

## Wearable armor fitting

The approved reference pattern for all future armor is [Paladin plate](costumes/knight_plate/README.md) (the original `knight_plate` path is retained), documented in the [armor style guide](costumes/README.md): torso, articulated waist, belt and thigh plates as a single `{"kind":"costume","variant":"knight_plate"}` outfit choice. The builder returns separately named physical pieces so the exporter can preserve rigid plate attachments through each piece's explicit `bone`. Both software posing and skinned GLB export honor those attachments and reject unknown bones. Surface color uses the shared character palette; rims share their plate's topology. The costume recipe supplies its colors. Future boots, gloves, shoulder and arm pieces remain separate from this first set.

The costume's `belt_y` parameter controls the lower stylized belt position and moves its hanging plates and straps together. Pelvis-mounted thigh plates share the hips attachment with their straps; binding them to upper-leg sockets causes separation during motion. The fitting dummy uses fuller arm geometry while preserving the reference skeleton's shoulder joints.

Humanoid torso presets use an integrated shoulder slope and narrow neck instead of tapering a barrel straight into the head; custom torso recipes can set `neck: false`. This is body geometry, not a skeleton relocation. The first costume's waist is body-close at the upper belly and modestly flared toward the lower skirt. The belt keeps its body height, with a costume-specific `belt_height` of 0.105 and a thin strap fitted to the actual armor outline. Short curved hip panels and their straps follow the same surface using `cuirass.plate`'s `azimuth` parameter. Generic belts retain a broader configurable default with a shared closed buckle.

For direct source comparisons, use `./.venv/bin/python asset-studio/studio.py armor compare costume_knight_plate`. It creates a temporary fitting, `comparison.png` and animated `comparison.gif` outside the repository. The actual original knight is rendered with its source materials, hiding helmet/cape/held props to expose the body. Both models share scale, world origin, camera, lights and clip timestamps; no separate height normalization is applied. The comparison retains visible proportion differences for review. Current comparison images supplement the fitting lineup; intermediate frames stay in OS temporary storage and are removed after success.

`armor compare` also creates the standalone animated `lineup.gif`. `./.venv/bin/python asset-studio/studio.py armor export costume_knight_plate` automatically rebuilds the model and these previews, then writes a portable art bundle to the temporary review `export/` folder: wearable-only `costume.glb`, `preview.gif`, fitting/comparison images and a manifest. Use `--out /absolute/folder` for another destination. This is a review/export package; publishing game art and creating gameplay resources remain separate actions. Publish only the final fitting, animation and wearable with `armor publish`; comparisons remain temporary.

The current plate family also includes [practical knight](costumes/knight/README.md), [Maiden](costumes/maiden/README.md) and [dark knight](costumes/dark_knight/README.md), all built by the same costume constructor. Compare them with the approved Paladin in the [plate-family sheet](catalogs/costumes/plate_family.png). Each has a current fitting, walking/attack animation and wearable export bundle; these three plate costumes are user-approved. Legacy studies are excluded from the final catalog.

The [chain mail](costumes/chain_mail/README.md), [Crusader](costumes/crusader/README.md) and [scale armor](costumes/scale/README.md) studies add modeled ring relief, a caped tabard with articulated steel pauldrons, and shield-derived torso scales recolored as silver steel above plain metal hip protection. See the [current comparison](catalogs/costumes/mail_family.png). These remain pending visual review. The approved barbarian and royal orc concepts are documented below.

The next batch comprises [segmented plate](costumes/segmented/README.md), [brigandine](costumes/brigandine/README.md) and [gambeson](costumes/gambeson/README.md). These replace their legacy audit studies with material-specific construction while reusing the fitted costume foundation. See their [current comparison](catalogs/costumes/field_family.png); these three are user-approved. Gambeson uses softly skinned torso cloth and an integrated quilt pattern, with cloth skirt and hip panels rather than recolored metal plates. Wearable bundles include source animation clips and current walk/attack previews.

The [female necromancer costume](costumes/necromancer/README.md) adapts that pattern with a breast-only shaped cuirass, extended fitted belly plates, violet seams on the same shell and an oversized bone skull relief buckle. `./.venv/bin/python asset-studio/studio.py armor character costume_necromancer` stages the associated character GLB and its `lineup.png`/`lineup.gif` in OS temporary storage in Godot, using the same lighting as the dummy review. An authored costume can associate its character via the art-only `character` key.

Use `./.venv/bin/python asset-studio/studio.py armor list` for current authored costumes. `armor lineup --all` renders temporary fitting candidates; `armor compare costume_knight` adds matched source comparison and animation. After inspection, `armor publish costume_knight` replaces that costume's final catalog entry. `armor build --all` stages current costumes without starting Godot; legacy builtins and draft recipes are excluded.

Fittings use featureless gray dummies, the production assembler, shared palette gradients, crease normals and skinned GLB exporter. All review views use the actual Godot GPU renderer. Front, three-quarter, side and back poses expose fit; sampled walk/attack poses and motion previews expose deformation; an elevated camera shows game-scale readability. Technical checks and sampled motion do not establish complete collision-free animation.

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
├── items/                    # 3D-only item authoring pipeline
│   ├── workflow.py           # Recipes, backends, references, previews, validation, adoption, export
│   ├── model.py              # Static GLB hierarchy/primitive inspection and non-destructive placement
│   ├── builders.py           # Topology-first builders with surface colors (steak)
│   ├── designs.py            # Fresh item families built with coherent profiles and solid unions
│   ├── solid.py              # Boolean assembly and closed-surface connectivity validation
│   ├── preserve.py           # Preserve original detail, choose character surface roles
│   ├── source_surface.py     # Recover original palette regions for character shading
│   ├── source_repair.py      # Repair source winding and missing boundary caps
│   ├── surface_marks.py      # Paint scroll symbols into parchment faces
│   ├── catalogs.py           # Current overview/category catalog generator
│   ├── recreate.py           # Optional family/blockout recipe migration
│   ├── concept.py            # Offline procedural blockout images
│   ├── build_item.py         # Compatibility entrypoint accepting the new item subcommands
│   ├── align.py              # Low-poly item builders, palette UV mapper, grip/AABB alignment
│   └── hf_image3d.py         # Image-to-3D mesh reconstruction helper
├── objects/                  # Item source folders (<category>/<name>/item.json, reference.png, source.blend, source.glb)
├── characters/               # Per-character folders (<family>/<name>/character.json, reference.png)
├── catalogs/                 # Current finished-item and reusable-prefab catalogs
│   ├── items/                # Overview, categories, index and current validation/export status
│   └── prefabs/              # Anatomy, outfits, item parts and details
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

# Render reusable-part candidates outside the repo; inspect before publishing:
./.venv/bin/python asset-studio/studio.py catalog anatomy
./.venv/bin/python asset-studio/studio.py catalog outfits
./.venv/bin/python asset-studio/studio.py catalog items
./.venv/bin/python asset-studio/studio.py catalog details
./.venv/bin/python asset-studio/studio.py catalog outfits --publish

# Render a character's multi-pose review into OS temporary storage
./.venv/bin/python asset-studio/studio.py lineup mage/necromancer

# Export a character's rigged GLB + .import (automatically generates lineup.png)
./.venv/bin/python asset-studio/studio.py export mage/necromancer

# List item recipes, then build and inspect an existing item locally
./.venv/bin/python asset-studio/studio.py item list
./.venv/bin/python asset-studio/studio.py item build tools/tool_lantern_brass
./.venv/bin/python asset-studio/studio.py item validate tools/tool_lantern_brass
./.venv/bin/python asset-studio/studio.py item lineup tools/tool_lantern_brass
```

---

## Standalone Item Workflow

Weapons, potions, trinkets, lamps, tools, and arbitrary props share a recipe lifecycle:

```text
item.json + concept/source → explicit build backend → placement → validation + preview → export GLB
```

Items live under `objects/<category>/<name>/` to distinguish authored assets from the Python implementation in `items/`. Category names are open-ended; a custom prop does not need a procedural archetype. Existing character commands keep their original syntax and behavior.

### Choose a creation method

| Backend | Use it for | Input |
| --- | --- | --- |
| `source` | Blender MCP modeling, manual modeling, or existing art | Self-contained `source.glb`; optional editable `source.blend` |
| `prefab` | Recoloring and composing reusable parts | `parts` referencing harvested meshes from any prefab domain |
| `procedural` | Solid-family recipes or local blockouts/variations | Explicit `builder` and `design`, or legacy `archetype` with optional reference and palette |
| `image3d` | AI mesh reconstruction | Reference image and the shared `core.ai` image-to-3D provider |

Use `source` for a custom design that needs precise geometry and shading. The procedural backend supports explicit builders and wraps the older archetype builders; the latter do not faithfully reconstruct arbitrary concept art and some vary shapes by item name. It never calls an AI provider. AI reference creation and image-to-3D are explicit commands/backends, and failures never silently substitute procedural output.

Generated geometry defaults to the **character surface model** (`color_mode: "character"`): shared `core/export.py` palette resolution, gradient atlas, height/slope ramps, crease-aware corner normals, and material-specific PBR/emission. Named `colors` and `materials` overrides use the same conventions as character recipes. This is the character shading implementation, rather than an approximation based on vertex colors. Explicit `vertex` and legacy flat `palette` modes remain available for older workflows; source GLBs retain their authored shading.

Preserve the original model's silhouette and functional detail before changing its color representation. Surface color belongs on the existing faces/corners, using the character palette atlas or actual vertex colors. Painted stripes and runes should not be overlapping color shells; real rims, wraps, gems, holders and grooves still need their modeled depth. Multiple physical parts are valid. Use union for continuous bodies and carving where appropriate, rather than forcing every asset into one solid. Verify supports, attachments and intended gaps in several views; a component count cannot identify floating decoration by itself.

Validation defaults to `topology: "closed_assembly"`: multiple closed surface components are accepted and reported for attachment review. Use `closed_surface` when a design explicitly requires a single surface. `authored_assembly` is an explicit repaired-source policy that retains thin decorative surfaces or coincident parts and records their open/non-manifold edges as review exceptions; it does not certify a closed mesh. Other checks reject open/non-manifold edges, duplicate or degenerate faces and nonpositive total volume. The solid-family helper still requires one physical body when performing an explicit union; enclosed negatively oriented cavity surfaces are valid. No component count establishes correct attachments or visual fidelity. Intentional open surfaces outside this repaired-source policy require an explicit authored policy.

### Preserve original geometry with character shading

Keep the original self-contained GLB as `source.glb` and select the `source` backend. Omitting `surface` preserves source shading as before. For originals using the verified Studio 8×4 palette, opt into conversion:

```json
{
  "backend": "source",
  "source": "source.glb",
  "color_mode": "character",
  "surface": {
    "palette": "studio_grid",
    "slots": {"steel": "metal", "silver": "metal_light", "wood": "leather"}
  },
  "topology": "closed_assembly",
  "placement": {"pivot": "authored", "scale": 1}
}
```

This fragment supplements the usual recipe name, version, budget and export path. `items/source_surface.py` verifies the embedded palette and recovers each face's swatch identity from its UV cell. It retains the scene's world-space triangles and their authored color boundaries, then uses the shared character exporter for new gradient ramps, crease-aware corner normals and PBR materials. Named `colors` and `materials` remain editable. Source swatches that only encode painted highlights/shadows should use the same base color and material override; let the shared character ramps and scene lighting provide shading. Retain separate colors for actual material regions (for example, fruit versus leaves or meat versus bone), rather than reproducing strong source-palette bands. It does not infer new regions from old baked brightness, decimate, add shells or union parts. Source hierarchy is flattened for static shading conversion, so use the untouched source mode when preserving separate object hierarchy is required. Unknown textures, vertex-color inputs and triangles spanning palette cells fail explicitly.

`surface.repair: "boundaries"` repairs recoverable source boundaries without moving original vertices and records unresolved edges in temporary `source-repair.json`. The current scrolls use `surface.paint: "scroll_ink"` to put their runes on the parchment surface; the cheese uses `surface.carve: "cheese_holes"` for actual cavities. Material overrides support `alpha` and `double_sided` for glass. The catalog status lists the remaining authored-surface exceptions.

Preserved potions use `surface.paint: "potion_liquid"` with an explicit `surface.potion_size` (`small`, `medium`, or `large`). This recognizes their authored bottle regions and assigns one `liquid` color/material slot, preserving labels, straps, fittings, collars and stoppers even where source swatches are shared. Set `colors.liquid` to the base tint; the shared character ramps and lighting provide shading instead of painted bands of different hues. This conversion targets the preserved octagonal layouts, not arbitrary bottle meshes.

The approved example is [`objects/tools/tool_grappling_hook/item.json`](objects/tools/tool_grappling_hook/item.json), with its [current Godot lineup](catalogs/items/tools/tool_grappling_hook/lineup.png). It retains all 420 original oriented triangles. Source/converter/palette/exporter changes invalidate the build receipt before publication. `item preserve --all --revision <original-git-revision>` adopts original catalog geometry without exporting it; then build, inspect lineups and export.

Available fresh builders are `potion`, `weapon`, `shield`, `scroll`, `jewelry`, `key`, `food`, `lamp`, and `tool`, plus the standalone `steak` study. A recipe uses, for example, `"builder": "weapon", "design": {"kind": "sword"}`. Bottle bodies, shoulders, necks, and stoppers use continuous profiles; multi-part structures use solid unions. Material/color slots survive those intersections. New family geometry does not sample or reuse the legacy item meshes.

The `steak` builder demonstrates this: one closed exterior surface, with fat, meat, bone, and marrow assigned to flush surface regions. It replaces the fragile ring construction for new art without modifying the existing legacy steak in the game. Color conversion alone cannot repair awkward topology in adopted assets.

```bash
./.venv/bin/python asset-studio/studio.py item new food/steak_variant --backend procedural --builder steak
```

### Additional weapon designs

The `arsenal` procedural builder contains dedicated designs for `wooden_club`, `steel_club`, `morning_star`, `short_dagger`, `saber`, `rapier`, `executioner`, `greatsword`, `broadsword`, `scythe`, `halberd`, and `spear`. These use continuous profiled grips, beveled diamond-section blades, curved guards, ferrules and solid-unioned structural details. A morning star is the rigid spiked-mace design. The rapier has a swept knuckle guard and pierced ring; the scythe has a crescent blade and two side grips. Color regions represent wood, leather, steel, honed edges and brass.

Recipes live under `objects/weapons/`, using `"builder": "arsenal"` and an explicit `"design": {"kind": "saber"}`. Each model is authored around a grip-centered origin, carries a 3,500-triangle budget and uses the shared character surface model. The long sword has a two-hand grip; mannequin previews use a static right-hand mount for scale, without posing the second hand. Exported art contains no equipment rules or gameplay resources. Review the current [weapons catalog](catalogs/items/weapons.png) and individual lineups from the [catalog index](catalogs/items/README.md).

### Curated shields

The `shieldcraft` builder makes curated combinations rather than material variants of every shape. The original six are: `knight` (shallow steel V), `bone_kite` (wood, bone frame and carved skull), `barbarian` (flat wooden round), `spiked_buckler` (flat iron round), `guard_scutum` (curved wood rectangle) and `paladin` (curved steel rectangle with brass reinforcement). Existing shield IDs and export paths remain stable. The original KayKit `assets/kaykit-dungeon-legacy/shield_uncommon.gltf.glb` supplies the reference for the gentle V cross section; the scuta use a stronger cylindrical curve. Round shield bodies stay flat.

The original six share a concept sheet and exact prompt at `objects/shields/reference.png` and `reference.prompt.txt`. Recipes use explicit `res://` reference paths so the sheet is not duplicated across item folders. The selected sheet describes art direction; the Godot lineups verify actual geometry. The grip sits behind the shield, with joined support posts and a grip-centered origin. Spikes, bosses, rims and rivets meet their supports. Skull sockets, nostrils, teeth and plank grooves are geometric cavities. Color stays on existing faces. The skull decorates a wooden shield, never a solid bone plate. Most shields have a 5,000-triangle ceiling; the detailed curved wooden scutum allows 6,000 triangles and exports through the shared character material pipeline. Shield lineups use `--mount shield` for an upright static left-hand pose; batch lineups select it automatically. This pose does not define gameplay equipment transforms.

Additional combinations use `heroic_quarters` (flush blue/ivory quartered heater), `heroic_sun` (crimson sun), `wood_simple` (all-wood body, braces, pegs and handle), `dragon_long` (large overlapping emerald scales), `iron_tower` (curved long shield with studded reinforcing bars), `royal_crown` (azure crowned kite) and `royal_lily` (purple lily shield). Their selected concept and exact prompt are `objects/shields/heroic-reference.png` and `heroic-reference.prompt.txt`. These are original modeled designs inspired by supplied visual references. Quartered paint partitions a single plate at the same surface depth; raised emblems and scales are structural ornaments with real support and thickness.

Round rims use one continuous chamfered ring, with backing inset beneath it. The barbarian retains its forged patch; the guard scutum instead has a lighter replacement board. Most shields stay below 5,000 triangles, and detailed or new designs have an explicit 6,000 ceiling. `prefabs/details/heraldry.py` exposes reusable `crown`, `lily`, and `scale_plate` parts through `studio.py schema details heraldry` and the current prefab details catalog.

V-shaped shield recipes expose `design.fold_degrees` (default **22 degrees per side**, supported range 5–35). The body is split at the center, each half forms one angled panel, and their boundary loops join into a closed ridge. The ridge keeps explicit corner normals so character-style smoothing cannot erase the fold; rounded fittings retain their smooth shading. Static export preserves validated authored corner normals. Round bodies stay flat and scuta retain their cylindrical curvature.

Framed shields inset their plate beneath the rim by 20 mm. Avoid coincident outer plate/rim faces, which can expose colored strips on the border after folding. The approved dragon design has two substantial swept shoulder horns with leather collars, reinforced mounting plates, a notched upper frame and a clear lower outline. Its individual reference and exact prompt live beside its recipe. The reusable `heraldry.bound_horn` provides the swept horn and two attached collars, while `heraldry.horn` accepts a root, middle, tip and radius; its root must intersect a support before union. Horn counts describe physical silhouettes, not the number of visible material islands.

The dragon horn mounts use exaggerated faceted `construction.horn_socket` boots aligned to the horn roots, with reinforced feet and mouth collars. `construction.rim_clamp` is a chamfered C-shaped fitting with front/back jaws and an outside bridge, so it wraps the rim rather than merely sitting on its face. Broad shoulder clamps, larger side clamps and oversized hexagonal heads make the joints readable at game scale. These parts are exposed by `studio.py schema details construction` and the current prefab catalog. Cast mounts fill unintended sealed microvoids while preserving their visible exterior; disconnected positive bodies still fail validation.

### Reusable construction and wear details

`prefabs/details/construction.py` exposes `timber_plank`, `repair_plate` and `corner_bracket` through `studio.py schema details construction` and the [details catalog](catalogs/prefabs/details.png). Boards have beveled edges, subtle geometric bow/twist and variable width; shield planks are clipped beneath the rim and fused to a continuous backing. Their base colors differ only slightly. Grain splits and knots are a few deliberately placed carved shapes, never noisy texture or floating color shells.

The existing character `kit.stud` provides repair-plate fasteners. `prefabs/details/wear.py` adds `solid_gash`, a closed negative cutter matching the armor's chiseled V-groove shape. Subtract it for closed props; the original surface-patch `gash` remains available for character armor. Dents subtract shallow faceted volumes. Wear is deterministic and uses explicit placements, with wood damage applied before attaching hardware so knots cannot bore through later rivets. Shield recipes enable this pass with `design.details: true`; `false` is available for geometry comparison. Shared construction and wear helpers are included in build-freshness checks.

### Create an item with Blender MCP and AI reference art

Concepts share the KayKit-inspired art direction in [`prompts/stylized-fantasy.txt`](prompts/stylized-fantasy.txt), composed by `core/prompts.py` for both characters and items. Item generation also uses the existing knight concept as a visual style anchor. Keep subject-specific changes in the brief; change the master file to adjust the whole library's art direction.

For the agent's built-in ImageGen tool, first print the exact prompt and reference paths (no provider calls or file changes):

```bash
./.venv/bin/python asset-studio/studio.py item reference food/food_bread_roll --prompt-only \
  --brief "One chunky bread roll cut open at one end, with one thick slice leaning against its cut face"
```

Inspect the listed style images, then pass the returned prompt and images to ImageGen. Keep proposals in the image tool’s preview history; import only the selected concept with `--image` and retain its exact prompt as `reference.prompt.txt`. API generation uses the same prompt/reference contract and writes a `.prompt.txt` sidecar. Concept art guides modeling; inspect the actual GLB in its Godot lineup before accepting geometry or shading.

```bash
./.venv/bin/python asset-studio/studio.py item new tools/moon_lantern --backend source

# Import a concept produced by the agent's image generation tool or another artist:
./.venv/bin/python asset-studio/studio.py item reference tools/moon_lantern --image /path/to/concept.png

# Alternatively, explicitly generate a concept through the shared API provider:
./.venv/bin/python asset-studio/studio.py item reference tools/moon_lantern \
  --provider gemini --brief "A squat brass lantern with a crescent handle and teal glass"
```

For a new visual direction, finish one item through concept, modeling, geometry checks and Godot review before applying it across the catalog. Publish only the selected final lineup; review candidates belong outside the repo and previous finals belong in Git history. Technical validation does not imply visual acceptance.

When using Blender MCP, follow the `assets` skill's Blender routing. Save the editable project as `objects/tools/moon_lantern/source.blend` and export an embedded-texture GLB as `source.glb` in the same folder. The CLI consumes that file; it does not start an MCP connection itself. Match the project palette, readable silhouettes, and scene scale. Set a meaningful origin in Blender or configure placement in the recipe.

```bash
./.venv/bin/python asset-studio/studio.py item build tools/moon_lantern
./.venv/bin/python asset-studio/studio.py item preview tools/moon_lantern
./.venv/bin/python asset-studio/studio.py item lineup tools/moon_lantern
./.venv/bin/python asset-studio/studio.py item validate tools/moon_lantern
./.venv/bin/python asset-studio/studio.py item export tools/moon_lantern
```

`build` stages the GLB and validation reports in OS temporary storage. `item preview` adds an optional software study there; `item lineup` adds the Godot review strip. `item publish` explicitly replaces the final catalog GLB/lineup/manifest after inspection, and `item export` separately updates game art. Batch `--all` supports build, validate, lineup, publish and export. Batch rendering never writes catalog files. `item catalog` recomposes only published entries and remains usable after temporary builds are deleted. Source models preserve authored materials, UVs and hierarchy unless the recipe requests surface conversion.

`lineup` creates **real Godot GPU screenshots**: front, three-quarter, side, and an elevated gameplay-camera view beside the studio mannequin at actual authored scale. It adds the concept panel when a reference exists. Run windowed; headless Godot cannot render images. Frames load the staged GLB directly, so no export or gameplay resource is needed. `--mount right_hand` or `left_hand` attaches the model to the static mannequin's hand for grip/orientation review. This is an art showcase in the game renderer, not a gameplay/equipment simulation. Later wrappers, gameplay lighting, animations, and effects still need their own integration checks.

```bash
./.venv/bin/python asset-studio/studio.py item lineup tools/moon_lantern --mount right_hand
```

`export` validates budgets and build freshness, copies the finished GLB into `assets/custom-items/`, then imports and instantiates it in Godot. Godot owns the generated `.import` settings. Use `--no-import` for batch exports; this explicitly skips engine verification. Existing differing art requires `--force` unless it matches this recipe's previous export receipt. No game scenes, resources, stats, effects, or attribution/license claims are generated.

For studio-generated vertex-color materials, export sets an art-only post-import script in the GLB's `.import` settings. The installed Godot 4.7 importer otherwise leaves the first primitive's vertex-color material flag disabled. The lineup uses the same correction. It is restricted to marked studio materials, and existing custom import scripts require explicit composition rather than replacement. Vertex attributes remain standard glTF; this only corrects their rendering in Godot.

### Recipe example

```json
{
  "version": 1,
  "name": "Moon Lantern",
  "backend": "source",
  "source": "source.glb",
  "blend": "source.blend",
  "reference": "reference.png",
  "concept": "Squat brass lantern with a crescent handle and teal glass",
  "placement": {
    "pivot": "ground",
    "dimensions": [0.5, 0.8, 0.5],
    "rotation_degrees": [0, 0, 0],
    "scale": 1.0
  },
  "budget": {"triangles": 1500, "materials": 3, "texture_size": 1024},
  "export": {"glb": "res://assets/custom-items/tools/moon_lantern.glb"}
}
```

`dimensions` is a bounding envelope in Godot units, fit uniformly to preserve proportions; `scale` applies afterward. Pivots are `authored` (default), `ground`, `center`, or `grip`. For `grip`, add a `grip: [x,y,z]` point in source scene coordinates, before recipe rotation and scaling. Optional `offset` translates the result after pivot alignment. Source files use recipe-local paths or explicit `res://` repository references; export paths stay within `assets/custom-items/`.

The six portable-light recipes under `objects/tools/` (`tool_candelabrum`, `tool_candle_tallow`, `tool_lamp_oil`, `tool_lantern_brass`, `tool_torch_iron`, `tool_torch_wooden`) preserve their original grip origins and use `placement.rotation_degrees: [0, 0, -90]` to point their tops upward in the standard idle and `Walking_B` poses. Keep their local `source.glb` inputs unrotated: build applies this correction once, and their Godot model wrappers need no additional rotation. The right hand uses the game's existing melee grip; the left-hand adapter uses the mirrored socket's orientation so both hands keep the light upright. The held scenes place their light/flame at the rotated source locations and use the same model rotation for the flame's rising direction. Review regenerated art with `item lineup <key> --mount right_hand` / `left_hand`, then verify the real equipped scene. Gameplay-held light/flame scenes, equipment slots and fuel rules remain game-side authoring; Studio must not regenerate or overwrite those Resources.

For `prefab`, use a `parts` list such as `[{"mesh": "flask_potion", "slot": "teal", "scale": 1.0, "offset": [0,0,0]}]`. Slots and optional `palette` entries use names from `items/align.py`. For `procedural`, add a `builder` such as `steak` or a legacy `archetype` such as `potion_small` or `lamp`. `crease` controls generated surface normal smoothing in degrees (default 50). For `image3d`, supply `reference.png`; dense reconstruction may need Blender cleanup to meet the budget. `studio.py item schema` prints the machine-readable recipe schema; semantic requirements are additionally checked when loading/building.

### Existing custom items

The approved migration for original `custom-items` preserves their detailed source geometry and applies the shared character surface/shading workflow. Keep IDs and export destinations stable. Capture the original GLB as a local authoring source before changing it; adoption alone neither improves nor visually validates the model. Godot wrappers and gameplay definitions stay in the separate resource-authoring layer.

```bash
./.venv/bin/python asset-studio/studio.py item list --existing
./.venv/bin/python asset-studio/studio.py item adopt assets/custom-items/tools/lamp/tool_lantern_brass.glb
./.venv/bin/python asset-studio/studio.py item adopt --all
```

Adoption is idempotent. Before changing an adopted asset, copy its GLB into its recipe folder as `source.glb` and change `source` to `source.glb`. This retains the original as an authoring input and prevents repeated placement from accumulating on an exported model. Existing models have not been regenerated or certified visually just by adoption; builds report geometry problems for explicit cleanup.

Use `item preserve --all --revision <original-revision>` for the approved source-preservation migration described above. `item recreate <key>` and `recreate --all` are optional family/blockout experiments for an explicitly requested new design, not the production migration path. They preserve export paths and placement envelopes, record the previous model hash and skip already recreated recipes. They do not publish art or overwrite custom recipes. Build, inspect lineups, then explicitly publish or export any selected replacement.

Generated builds, preview sheets, validation reports, and export receipts stay local via `objects/.gitignore`. Commit recipes, references, source models, and Blender files when appropriate. A changed recipe, source, or staged GLB requires rebuilding before export.

The old `items/build_item.py --id ... --tier ...` interface has been retired. That file now accepts the same item subcommands as `studio.py item`; gameplay generation has been removed.

### Verification

```bash
./.venv/bin/python -m unittest discover -s asset-studio/tests -v
```

Tests cover source round trips, transforms across mesh instances/primitives, pivot/scale placement, budgets, stale builds, adoption, export protection, actual solid unions, floating geometry rejection, welded render seams, open/duplicate/inward surfaces, fresh family topology, shared character shading, and the art-only boundary. They use isolated temporary output trees and make no provider calls.

---

## Blender MCP & Godot Creator Tool Integration

- **Blender MCP (`blender-mcp`)**: Any `kit.Piece` or exported `.glb` can be loaded into Blender via MCP for custom adjustments, boolean bevels, NLA animation tweaks, or lookdev, and saved back into domain `meshes/` via `harvest.py` or exported to `assets/`.
- **Godot Character/Monster Creator Tool**: `prefabs/registry.py` (`studio.py schema`) exposes every prefab category, preset list, and parameter signature as structured JSON, and `characters/<family>/<name>/character.json` is a declarative recipe format designed so an in-engine Godot visual editor can inspect, tweak, preview, and export characters directly.

Current cloth costume studies: `costumes/ranger`, `costumes/scout`, `costumes/arcanist` and `costumes/adventurer`. Their current comparison is [`catalogs/costumes/travel_family.png`](catalogs/costumes/travel_family.png). Reusable scarves, cowls and a separate steel gorget live in `prefabs/outfits/neckwear.py`; `costumes/gorget_study` fits the steel option on the practical knight without changing existing armor. See [costume construction notes](costumes/README.md). Mantles currently use skeletal deformation, without physical cloth simulation.

The five steel armor costumes now include gorget-mounted pauldrons through `kind: steel_shoulders` (`size` controls dome size). The caps use two rigid pivot joints with baked animation; see [construction and export notes](costumes/README.md#gorget-mounted-steel-shoulders) and the [current steel family](catalogs/costumes/plate_family.png).

## Primitive and royal orc costumes

`costumes/barbarian/` preserves the existing blue barbarian as a coordinated `costume_set`. Its `parts` are the original shorts, tunic, harness, belt and cape recipes. The character references that set and retains its headgear and limb accessories. A `costume_set` expands normal garment layering with original fitting semantics; nested sets are rejected.

`costumes/barbarian_primitive/` is a separate bare-chested hero-body costume: a continuous leather harness, one hinged fur-lined pauldron, a fitted belt, hide skirt and curved hip panels. `costumes/orc_king/` fits the brute torso and retains the king's established head, crown and limbs while replacing his obsolete torso and shoulder armor. Their selected image concepts and exact master prompts are authoring inputs; generated models remain visual-review candidates until publication. Other orc costumes are unchanged.

Reusable connected fur borders and horned-skull buckle reliefs live in `prefabs/details/primal.py` and are discoverable under `studio.py schema details primal`. The new shoulder fittings reuse horn sockets and curved horns, maintain actual contact and bake the existing half-arm hinge into standard animation clips. Fitting recipes can carry a complete `body` preset so broader orc arms and legs are visible in review instead of silently substituting human limbs.


### Complete costumes versus prefab parts

A character selects a complete authored outfit with `{"kind":"costume_ref","name":"knight"}`.
This expands `costumes/knight/costume.json` in place, including steel shoulders and the costume palette.
The character keeps its own anatomy, head, scale, equipment and explicit color overrides. Character colors
win over costume colors; later costume references win over earlier references. Cyclic references are rejected.
The lower-level `kind: costume` selects only a procedural builder; use it inside a costume definition.
`costume_set` remains available for the preserved blue barbarian assembly.

The knight now selects the practical steel costume. Necromancer, primitive barbarian and orc king select their
complete authored costumes. Orc warrior, shaman and goblin clothing has been extracted without geometry changes
into named `status: legacy` costume recipes. These distinct designs still need art rework and are excluded from
the current armor fitting catalog. Their reusable straps, collars and limb armor remain available as parts.
Unused complete-outfit presets were removed from `prefabs/outfits/armor.py`; its shared geometry is retained.

Prefab source folders contain code, harvested `.npz` geometry and metadata indexes only. `catalog <domain>`
stages a sheet in OS temporary storage. `catalog <domain> --publish` publishes the existing reviewed candidate,
checking source and image hashes without rerendering. `--out` accepts external preview destinations only.
`catalog library --index` refreshes metadata tables without generating images. Complete costumes belong in
`catalogs/costumes/`; prefab sheets show reusable parts. Superseded knight, barbarian, necromancer and orc king
character previews were removed. Regenerating a character review does not update the game's exported GLB.

Character categories group zombies, ghouls, mummies and wraiths under `characters/undead/`. An optional recipe `export.glb` (`res://assets/...glb`) preserves an existing game-art destination when an authoring category moves; the zombie retains its historical export path for compatibility.
