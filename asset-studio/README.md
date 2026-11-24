# Rogue Gauntlet Asset Studio

Asset Studio owns visual authoring and exports for Rogue Gauntlet. Godot gameplay scenes and resources own stats, equipment, collisions, behavior and spawning. The Studio also builds the [2D UI kit](../docs/user-interface.md#ui-kit).

## Choose a workflow

| Asset | Default |
| --- | --- |
| New enemies, helmets, body armor, and future heroes | [Concept and turnaround → separate image-to-3D parts → Blender → Godot](character-workflow.md). Start from the [approved Orc Warrior](characters/orc/orc_warrior/README.md). |
| Food, potions, weapons, shields, tools, and props | [Standalone item workflow](#standalone-item-workflow). Keep the existing recipe, validation, review and export lifecycle. |
| Modular floors, walls and rugs | [Tileset workflow](#modular-floor-and-wall-tiles). |
| Existing kit characters, creature rigs and costume recipes | [Maintenance commands](maintenance/character-and-wearable-recipes.md). |

The Orc's Studio export uses its approved Blender source. Its [earlier kit recipe](maintenance/orc-warrior-kit/README.md) is archived outside the active character recipes.

## Current catalogs and folder policy

| Location | Keep |
| --- | --- |
| `characters/<family>/<name>/` | Selected concepts and exact prompts, approved turnaround inputs and the current editable source. Existing recipe-built assets keep the inputs their recipe consumes. |
| `objects/<category>/<name>/` | Item recipe, selected concept and prompt, current editable or GLB source, and source provenance or export receipt where the recipe uses it. |
| `costumes/<name>/` | Existing art-only outfit recipes, selected concepts, and construction notes. |
| [Item catalog](catalogs/items/README.md), [costume catalog](catalogs/costumes/README.md), [character catalog](catalogs/characters/README.md), [prefab catalog](catalogs/prefabs/README.md) | Selected publications and their manifests. |
| OS temporary work root | Raw reconstructions, intermediate builds, technical reports, review comparisons, shot frames and jobs. `core.paths.work_root()` gives the checkout-specific location. |
| `assets/custom-*` | Game-ready exports consumed by Godot. |

Rendering stages candidates. Publication copies selected reviews to the catalog and export installs game art. Recipe publication manifests track the source inputs used for review. A `source_current: false` status indicates that the source changed after publication. See the [Studio architecture](ARCHITECTURE.md) for implementation boundaries.

## Class heroes

Existing classes use the recipe-built heroes described in the [maintenance guide](maintenance/character-and-wearable-recipes.md#class-heroes) and the [game's model swap documentation](../docs/character-classes.md#the-model-swap). New hero and wearable art follows the [character workflow](character-workflow.md#rig-and-fit-for-the-assets-role).

## Item CLI quick start

Run Studio commands using the workspace virtual environment.

```bash
./.venv/bin/python asset-studio/studio.py item list
./.venv/bin/python asset-studio/studio.py item build tools/tool_lantern_brass
./.venv/bin/python asset-studio/studio.py item validate tools/tool_lantern_brass
./.venv/bin/python asset-studio/studio.py item lineup tools/tool_lantern_brass
./.venv/bin/python asset-studio/studio.py item catalog
```

## Standalone Item Workflow

Food, potions, weapons, shields, tools, and props share a recipe lifecycle:

```text
item.json + concept/source → explicit build backend → placement → validation + preview → export GLB
```

Items live under `objects/<category>/<name>/` to distinguish authored assets from the Python implementation in `items/`. Category names are open-ended; a custom prop does not need a procedural archetype.

### Choose a creation method

| Backend | Use it for | Input |
| --- | --- | --- |
| `source` | Concept-driven modeling in Blender, manual modeling, or existing art | Self-contained `source.glb`, generated from a committed `source.py` model script or exported by hand |
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

### Weapons

Every catalog weapon is a procedural recipe under `objects/weapons/` built by one of the KayKit-style weapon families (`axe`, `blade`, `blunt`, `polearm`, `staff`, `ranged`) with an explicit `"design": {"kind": ...}`. The [weapon style guide](objects/weapons/README.md) holds the rules, reference proportions, shared palette and construction. Exported art contains no equipment rules or gameplay resources. Review the current [weapons catalog](catalogs/items/weapons.png) and individual lineups from the [catalog index](catalogs/items/README.md).

### Room furnishings

The `furnishing` builder ([`items/furnishings.py`](items/furnishings.py)) makes world props for generated rooms: altars, candelabras and candle heaps, lecterns, statues, braziers, vases, busts, armor stands, globes, cauldrons, mortars, scroll and weapon racks, hearths, bunk beds, kitchen food, hay bales, an archery target, prison fittings (shackles, a torture rack, stocks, hanging cages, iron maidens, a warden's desk, a gallows and an orc fighting ring), a rat king's heap, ruin debris, tapestries, heraldic shields and sconces. Recipes live under `objects/furnishings/<name>/` with an explicit `"design": {"kind": "statue", "pose": "sword"}` and export to `assets/custom-props/<theme>/`, a game art tree separate from held items.

Everything is built from the KayKit-style vocabulary in [`items/furnishing_kit.py`](items/furnishing_kit.py) rather than smooth primitives: thick boards with broad chamfers laid side by side so the chamfers read as grooves, aged with chipped corners, small notches bitten from their edges and cracks along the grain; square posts with caps; stone stacked in staggered courses, with jagged cracks (`crack`, `crack_path`) cut into its faces; logs of pale ringed end grain inside a rim of chunky bark strips; smooth rolled rims on cast pots and bowls; and oversized rivets, candles and heads. Each design seeds its own generator, so builds are reproducible. Sizes match the remastered KayKit dungeon props (4-unit tiles, 1-unit tables): floor props stand on y = 0 facing +Z, back-to-wall props keep a flat back on -Z, and wall props hang with their back on z = 0 at their final height. Candle-bearing designs also build a separate `flames` model so the game can put flames and light out together on dark floors; braziers and hearths hold dead coals and get their fire in the game scene.

Furnishing recipes use `"color_mode": "world"`: [`items/world_surface.py`](items/world_surface.py) maps every material slot onto one of the few swatches KayKit's own props are painted with in `dungeon_texture.png` (byte-identical to `rigs/item_palette.png`; wood, dark wood, stone and iron, dark stone, cloth colors, gold), never a per-prop shade, and shades it with the characters' ramp (height plus slope) over the v range KayKit's props use, under KayKit's single material, matte as the project shades it (metallic 0, roughness 0.85; flames and brews get an emissive copy). Colors therefore come from the world's palette, never from per-prop RGB values; check new props side by side with related KayKit assets. Details KayKit already models are reused rather than imitated: [`items/borrowed_parts.py`](items/borrowed_parts.py) lifts triangles out of KayKit models with their UVs and material (`python -m items.borrowed_parts` regenerates the bunk bedding from `bed_frame.gltf`), and game scenes combine props with existing art, such as KayKit's woodcutter's axe in the chopping block. Room composition (`Prop`/`PropGroup` resources, lit and composed scenes, room types) stays game-side; see the [room assembly guide](../scenes/levels/generators/assembly/README.md).

Realm furnishings follow the same rules. Sarcophagi (`crypt/`), the Cathedral's bell, reliquary cabinet, bishop's throne and coffins ([`items/cathedral.py`](items/cathedral.py), `cathedral/`), the Mines' shaft and smelting furnace (`mines/`), the burial ground of [`items/graveyard.py`](items/graveyard.py) (`graveyard/`: graves with stone, cross and wooden markers, grave pedestals, a mausoleum, and an iron fence on stone pillars whose 4 m and 2 m modules, corner and arched gateway fit `FencedAreaStep`), sewer fixtures (valve, outfall, pipe run, sluice gate, goo barrel) and raised goo puddles (`sewer/`) wear the dungeon texture; the drain pipe and slime take the swatches of the slimy wall's own pipe and slime. Statues and the sarcophagus effigy are the pack's own adventurers carved in stone rather than modeled figures: [`items/pose_kaykit.py`](items/pose_kaykit.py) poses a KayKit character in headless Blender (`Blender -b --factory-startup --python items/pose_kaykit.py -- <character.glb> <clip> items/meshes/<name>.npz`, never in an open Blender session) and stores its closed parts, which `furnishings.figure` unions. Jungle plants ([`items/foliage.py`](items/foliage.py): ferns, bushes, broadleaves, root tangles, mushrooms) are the walls' growth come down to the floor, so their recipes use `color_mode: "character"` with the jungle walls' moss, fern and root colors and the tiles' ramp instead of the dungeon texture.

`Assembly.cut` subtracts from everything added before it, so details added afterwards can sit inside a recess; `Assembly.label`/`join` let a part take its own booleans (a chipped board) before it joins.

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

### Scripted item models

The item `source` backend can run `item model <key>` in a fresh headless Blender when a recipe provides `source.py`. Keep the regenerated `source.glb` and its provenance `source.json` beside the recipe. `item compare <key>` can review a compatible concept turnaround. See [maintenance](maintenance/character-and-wearable-recipes.md#wearables) for existing scripted helmets.

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

For concept-driven modeling prefer a model script (above); Blender MCP stays useful for inspecting a generated model or hand edits. A hand-made model saves the editable project as `objects/tools/moon_lantern/source.blend` and exports an embedded-texture GLB as `source.glb` in the same folder. The CLI consumes that file; it does not start an MCP connection itself. Match the project palette, readable silhouettes, and scene scale. Set a meaningful origin in Blender or configure placement in the recipe.

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

`dimensions` is a bounding envelope in Godot units, fit uniformly to preserve proportions; `scale` applies afterward. Pivots are `authored` (default), `ground`, `center`, or `grip`. For `grip`, add a `grip: [x,y,z]` point in source scene coordinates, before recipe rotation and scaling. Optional `offset` translates the result after pivot alignment. Source files use recipe-local paths or explicit `res://` repository references; export paths stay within `assets/custom-items/`, or `assets/custom-props/` for world props.

### World props: fountains

`objects/fountains/` holds eight `fountain` builder recipes (`items/fountains.py`): `font`, `chalice`, `spring` and `tiered` designs, each in a `health` and a `mana` variant. The variants differ only in their emissive `liquid` color. Each basin is one closed solid, hollowed down to a stone floor. The pools and streams form a second closed solid, which `export_static` writes as its own `Liquid` node next to the `Item` node. The game hides that node to show an empty fountain, and the same models stand dry as decoration (`scenes/props/fountains/fountain_<design>_dry.tscn`), so a decorative basin never reads as a usable fountain. Variants share the concept and exact prompt saved as `<design>-reference.png` beside them. The origin is the floor center. Exports go to `assets/custom-props/fountains/`. Sparkles, shimmer and prop scenes are game-side; see [fountain effects](../docs/world-effects.md#fountain-effects). `POOLS` lists each liquid level and inscribed radius for placing those effects. Keep it in step with the geometry.

```bash
./.venv/bin/python asset-studio/studio.py item build fountains/fountain_tiered_mana
./.venv/bin/python asset-studio/studio.py item lineup fountains/fountain_tiered_mana
./.venv/bin/python asset-studio/studio.py item export fountains/fountain_tiered_mana
```

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

Tests cover source round trips, transforms across mesh instances/primitives, pivot/scale placement, budgets, stale builds, adoption, export protection, actual solid unions, floating geometry rejection, welded render seams, open/duplicate/inward surfaces, fresh family topology, shared character shading, and the art-only boundary. Blender-modeled sources add material-slot ingestion, model provenance, shared-scale concept scoring and host show-through; their runner tests use the pinned Blender and skip without it. Tests use isolated temporary output trees and make no provider calls.

---

## Modular Floor and Wall Tiles

Floor, wall and rug tiles in the KayKit dungeon style, on the KayKit footprints (4x4 and 2x2 floors, 4x4x1 walls with half and corner pieces), extend the existing dungeon set. A recipe under `tilesets/<set>/` builds one family: every piece and variant of a material, so they can be validated and reviewed together. Floor patterns repeat every two units, so large and small tiles and all variants mix in one room without the visible grid of KayKit's repeating wood joints; walls join through shared end cross-sections and keep KayKit's face depth where the game hangs decor. `tile build` checks this on every tile edge and wall face, and `tile lineup` renders the family on the game grid in Godot. See the [tileset guide](tilesets/README.md) for footprints, the seam contract, the current families and recipe parameters.

```bash
./.venv/bin/python asset-studio/studio.py tile list
./.venv/bin/python asset-studio/studio.py tile build dungeon/floor_planks
./.venv/bin/python asset-studio/studio.py tile lineup dungeon/floor_planks
```

Water comes in two tile kinds: inlays (`floor_water`, KayKit's own grates with water in their shafts, laid where grates are) and pools (`floor_pool`, deep water laid like rugs with quay walls in KayKit's foundation profile). Their exported water is a translucent stand-in; the game draws it with its water shader.

The studio owns the tile art only. Mesh libraries, collision, navigation and which realm or room uses which family are game-side wiring.

---
