---
name: assets
description: Use when creating, customizing, previewing, or exporting 3D characters, monsters, items, accessories, or room props and furnishings using the project's `asset-studio/` pipeline (and `blender-mcp` when needed), or when fetching external CC0 3D asset packs.
---

# Asset Studio & 3D Asset Pipeline

All procedural 3D character, monster, and item generation code, modular prefabs, character recipes, selected concepts, and helper scripts live in the root [`asset-studio/`](file:///Users/andrerabold/Projects/Personal/rogue-gauntlet/asset-studio) folder.

> [!IMPORTANT]
> - **Final catalog vs. temporary review**: All builds, preview candidates, comparison images, shot frames, jobs and weapon caches MUST go to OS temporary storage outside the repository (`core.paths.work_root()`). Authoring folders keep recipes, selected concepts/prompts and original editable/model sources only. `catalogs/` is reserved for selected final outputs; never write draft/interim files there. `item lineup` and `armor compare` render external candidates. After inspection use explicit `item publish` / `armor publish` to replace a final entry. Game `export` is a separate action. Publication manifests bind reviewed artifacts to recipe/modeling/shading/rig inputs; source changes invalidate current approval. Read [Studio's folder policy](../../../asset-studio/README.md#current-catalogs-and-folder-policy).
> - **Brand-Neutral Code**: Keep external asset-creator brand names out of code identifiers, preset names, and comments. The concept prompt may name the user-requested art inspiration. Use our own naming (`standard`, `heroic`, `stylized`, etc.).

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
- **`asset-studio/catalogs/items/README.md`**: The current finished-item catalog, with a complete overview, category sheets and individual Godot lineup links. **`catalogs/prefabs/`** separately contains reusable anatomy, outfit, item-part and detail catalogs.
- [`asset-studio/items/`](file:///Users/andrerabold/Projects/Personal/rogue-gauntlet/asset-studio/items): 3D-only item workflow (`workflow.py`, `model.py`, `builders.py`, `concept.py`) with source, prefab, procedural, and image-to-3D backends. `build_item.py` forwards to the same recipe commands; its old gameplay-generation flags are retired.
- **`asset-studio/objects/`**: Persistent item folders (`<category>/<name>/item.json`, optional `reference.png`, `source.glb`, `source.blend`); generated builds, previews, validation reports, and export receipts stay local. The original custom-item catalog now uses fresh solid-family recipes with stable IDs/export paths; adopted sources can still be registered explicitly.
- [`asset-studio/characters/`](file:///Users/andrerabold/Projects/Personal/rogue-gauntlet/asset-studio/characters): Per-character folders (`<family>/<name>/character.json`, `reference.png`).
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

### Approved armor reference

The user-approved armor pattern and style guide is `asset-studio/costumes/knight_plate/`, with shared rules in [asset-studio/costumes/README.md](../../../asset-studio/costumes/README.md). Consult its current recipe, Godot stills, standalone animation and matched original/studio comparison before designing or reviewing any armor. Legacy armor catalog entries are audit studies, not quality references. Preserve the approved breast/belly/skirt proportions, visible neck/shoulder anatomy, taut belt, restrained flare and front-to-back hip plates with a visible hem-length difference. Keep the shared character shading and test actual support/contact through motion. Vary silhouettes, materials, colors and ornaments to suit new designs rather than cloning every knight detail.

The finished knight adds the existing registered `trophies.shield_emblem` sunburst with brass rays and a dark red center. Reuse a greeble rather than approximating it with primitives; mount it on the actual plate surface, not a guessed torso offset. The implementation unions the greeble's original closed elements to remove coincident ray-root seams, sinks its backing slightly into the breastplate and binds it to the chest. A contact-union regression proves the emblem touches its support. `costume.json` records `status: style_reference`; the armor catalog exposes this separately from legacy entries. `studio.py armor catalog` refreshes current status/index/overview without rerendering.

### Coordinated costumes

`asset-studio/costumes/<name>/costume.json` defines an art-only outfit choice and palette. Keep its selected reference/prompt beside the recipe; staged wearable GLBs and all review images belong in OS temporary storage. Final lineups, animations and wearable GLBs belong in `catalogs/costumes/<name>/` after explicit `armor publish`. The first set is `knight_plate`, exposed as `{"kind":"costume","variant":"knight_plate"}` in character outfits and through `studio.py schema outfits`. It includes torso front/back, articulated waist plates, belt, suspension straps and thigh plates; boots, shoulders, gloves and arm protection are future separate pieces.

`prefabs/outfits/costumes.py` returns named physical parts rather than one deforming mesh. Each rigid plate declares its carrying `bone`; both character GLB export and software posing honor it and reject unknown bones. Do not merge parts with different attachment bones before export. Use integrated surface-colored rims, actual closed negative cutters for damage, and closed buckle frames. Validate each component's topology, inspect actual attachments in posed Godot views, and avoid equating closedness with fit or physical contact. Run `studio.py armor lineup costume_knight_plate` to render an external review candidate; `armor build costume_knight_plate` also stages the wearable-only rigged temporary `costume.glb` without publishing gameplay art or resources.

For pelvis-mounted armor flaps, bind each rigid plate and its suspension straps to the same hips attachment. A thigh bone pivots at the leg socket and can pull a belt-mounted flap free or hide it inside the body. Flare the plates for leg clearance and verify both sides across motion. The knight costume exposes `belt_y` (default `0.625`, lowered from `0.715`); its belt, buckle, hangers and plates move together. Do not move reference shoulder bones to repair a thin dummy silhouette: its joints match the source rig. Fitting dummies use arm scale `1.25` to bring shoulder volume closer to the reference. Regression checks sample walking, running and attack clips for stable suspension and rigid geometry; this is not full collision certification.

Humanoid torso presets now use a continuous clavicle/shoulder slope and a distinct narrower neck (`Trunk.neck`), preserving the reference skeleton. `body.build` enables it for standard, hero, female, brute and skinny presets; custom torso recipes can opt out with `neck: false`. Do not confuse correct joint positions with correct shoulder/neck surface anatomy. Armor starts below this transition. Lower breast and waist plates independently of the belt's body anchor so the belt moves higher relative to the armor. The knight uses a fitted `belt_height: 0.105` and 0.018-thick strap; generic character belts retain configurable defaults of height 0.13 and thickness 0.04, with shared closed `belts.frame_buckle` geometry. Explicit recipe sizes are honored. Short curved lap plates wrap the hips and overlap the skirt, retaining the same hips attachment as their curved straps.

`studio.py armor export costume_knight_plate` now rebuilds the wearable and GPU reviews, including a standalone animated `lineup.gif`, then packages the rigged wearable, `preview.gif`, fitting/comparison images and manifest in the external temporary review `export/` folder. `--out /absolute/folder` selects another bundle destination; unrelated nonempty folders are rejected. The GLB includes the wearable skeleton and clips without the dummy. This exports an art review bundle, without publishing to game assets or creating gameplay resources. Keep comparison images and animations in temporary storage. Publish only the selected fitting, standalone animation and wearable GLB to `catalogs/costumes/knight_plate/`.

Fit a costume belt to the actual waist surface instead of adding a constant padded `Worn` envelope: excess padding makes even a broad strap read as a floating ring. `costumes.BeltHost` uses a taut cross-section from `clothes.Fitted`; keep the buckle readable while reducing strap thickness and girth. `costumes.WaistHost` is body-close at the upper belly and adds a modest continuous flare toward the lower hem, shared across the overlapping waist plates. Keep upper belly plates on the spine and the belt/lower skirt/hip supports on the hips. Attaching the upper belly to the chest can fix its top overlap but drives its lower portion through the belt. Shorten and recess the upper waist lip instead, preserving the original articulation and visible belly. Review chest coverage and belt clearance together in motion; test actual waist-to-belt clearance across walking samples rather than only proving chest occlusion. For padded cloth, keep the skirt's upper edge within the belt height with a margin below the strap top. Use `cuirass.plate(..., azimuth=<degrees>)` for short curved hip plates and straps; keep their borders colored on the same surface. Avoid flat trapezoid paddles or excessive independent plate flare. The current lap plates are 0.18 rig units high, wrap about 156 degrees around each hip from front to back, and extend about 0.045 below the shortened skirt hem. Leave small front/back center openings and support each plate with front, side and back straps on the same hips attachment. Keep a visible difference between skirt and flap lengths so the panels do not disappear into the skirt. Verify actual belt-to-shell proximity, closed panel geometry, curvature and rigid suspension through motion; refresh the current images and export bundle after adjustment.

### Review Wearable Armor on a Dummy

Use `./.venv/bin/python asset-studio/studio.py armor list`, then `armor lineup --all` or a named study such as `armor lineup costume_knight`. The current fitting catalog is `asset-studio/catalogs/costumes/README.md`; shields remain in the finished-item catalog. This derives candidates from current authored costume recipes, excluding legacy builtins and drafts, and, assembles neutral dummies with the production character builder and skinned exporter, then renders T-pose front/three-quarter/side/back, sampled walk and overhead attack, and idle game-camera context using Godot GPU shading. Intended torso presets are preserved; this is not a fit test across every body shape.

Use the fittings to audit and rework one armor construction at a time. Existing ridged mail is not convincing linked chain mail, older plate trim uses overlapping shells, and isolated pieces do not establish combined fit. Preserve actual rolled edges and fittings while keeping surface colors on existing geometry. Check shoulder/elbow/hip clearances, side/back coverage and full animation sweeps before accepting production armor. The sampled poses are visual review evidence, not collision certification. Stage GLBs and temporary frames outside the repo. Use `armor publish` to update final GLB/lineup/animation and fingerprint manifest under `catalogs/costumes/<name>/`; `armor catalog` only recomposes existing publications. This does not publish GLBs, change character recipes or create gameplay resources.

For reference comparisons, `studio.py armor compare costume_knight_plate` loads the actual original knight from the asset pack and produces temporary `comparison.png` and `comparison.gif` outside the repository. It preserves source geometry/materials and hides helmet, cape and held props only in the render. Match world origin, rig scale, cameras, lighting and exact clip timestamps; never normalize the two model heights independently. The GIF samples walk and attack clips at 24 frames each. Keep real remaining differences visible (dummy limb thickness, hands/feet and missing limb armor); do not mistake shared skeleton transforms for matching surface proportions. Comparison frames/manifests are temporary cache outputs. Use small positive plate-to-body clearance; excessive forward sculpt can add a large gap even when base shell thickness is sensible. The knight's sampled front clearance is about 0.015–0.029 rig units over the revised breast area after reducing the chest bulge and shell offset; check side views and poses rather than treating this sample as complete fit certification.

### Build, Inspect Lineup, and Export a Character or Monster
1. Edit or create `asset-studio/characters/<family>/<name>/character.json` (or run `./.venv/bin/python asset-studio/studio.py new <family>/<name> --base <family>/<base>`).
2. Render the multi-pose animated lineup (in OS temporary storage):
   ```bash
   ./.venv/bin/python asset-studio/studio.py lineup <family>/<name>
   ```
3. Export the rigged `.glb` + `.import` into `assets/` (which automatically updates `lineup.png` and validates in Godot):
   ```bash
   ./.venv/bin/python asset-studio/studio.py export <family>/<name>
   ```

### Render Prefab Contact Sheets & Rebuild Visual Indices
```bash
# Render reusable-part candidates in OS temporary storage; inspect before publication:
./.venv/bin/python asset-studio/studio.py catalog anatomy
./.venv/bin/python asset-studio/studio.py catalog outfits
./.venv/bin/python asset-studio/studio.py catalog items
./.venv/bin/python asset-studio/studio.py catalog details

# Publish the existing reviewed sheet, without rerendering:
./.venv/bin/python asset-studio/studio.py catalog outfits --publish

# Rebuild metadata tables only in prefabs/*/README.md (no images):
./.venv/bin/python asset-studio/prefabs/library.py --index
```

### Consistent Concept Art

Use the canonical [master style prompt](../../../asset-studio/prompts/stylized-fantasy.txt) for every generated concept. It defines the requested KayKit-inspired style; do not replace it with an ad hoc generic low-poly prompt. `core/prompts.py` composes it with the subject and layout for Studio generation.

Always pair the verbatim master style prompt with our two visual style anchors — [`asset-studio/characters/heroes/knight/reference.png`](../../../asset-studio/characters/heroes/knight/reference.png) and [`asset-studio/costumes/knight_plate/reference.png`](../../../asset-studio/costumes/knight_plate/reference.png) — passing both paths as reference images so outputs keep the exact plain white background, charcoal-gray chunky chibi fitting dummy (when showing equipment fit), broad planar facets with softly chamfered edges, and clean solid color regions without surface noise, scratches, dark backgrounds, or ink outlines.

For item concepts, run `./.venv/bin/python asset-studio/studio.py item reference <category/name> --prompt-only --brief "<design>"`. This is read-only and returns the exact prompt and style-reference image paths. Inspect those images, then pass the prompt and referenced images to `image-generator` / the image tool. If the built-in image generator hits quota limits (or when batching turnarounds through `asset-studio/core/ai.py`), use the exported `SKILL_IMAGE_GEN_GEMINI_KEY` environment variable (bind it to `GEMINI_API_KEY` in-memory only — never echo or commit key values) with `GeminiProvider(image_model="gemini-nano-banana-2.1").edit_image(compose(task), refs, aspect="16:9")` via `uv run --with google-genai --with pillow`. For character concepts preserve the mannequin's pose and front/side layout through `core.match`.

Keep proposals in the image tool’s preview history; copy only the selected image to `reference.png` and its exact prompt to `reference.prompt.txt`. Compare the result with the character references for facets, proportions, color regions and detail scale. Import the selected concept through `item reference --image`; then model and verify its actual Godot lineup. A concept image does not establish mesh connectivity or runtime shading.

### Create a Standalone 3D Item or Prop

Asset Studio owns **3D art only**. Keep damage, tiers, equipment rules, item identities, effects, Godot gameplay scenes, and `.tres` definitions in a separate Godot resource-authoring step. Do not restore the old builder's stat generation or assign gameplay properties in recipes.

Read `asset-studio/README.md` for the full recipe format and backend tradeoffs. The machine-readable schema is available through `studio.py item schema`.

```bash
./.venv/bin/python asset-studio/studio.py item list
./.venv/bin/python asset-studio/studio.py item new tools/moon_lantern --backend source
./.venv/bin/python asset-studio/studio.py item reference tools/moon_lantern --image /path/to/concept.png
./.venv/bin/python asset-studio/studio.py item build tools/moon_lantern
./.venv/bin/python asset-studio/studio.py item lineup tools/moon_lantern
./.venv/bin/python asset-studio/studio.py item export tools/moon_lantern
```

Choose the method from the design:
- **Custom design / quality modeling:** generate reference art with the imagegen skill/tool, then use `blender-director` plus `prop-artist` or `hard-surface`, the relevant style/material skills, `asset-optimization`, and `godot-export`. Save `source.blend` and a self-contained `source.glb` beside `item.json`. The `source` backend preserves authored shading, UVs, and hierarchy; the CLI consumes files, while the agent drives Blender through MCP.
- **Reusable parts:** use `prefab` with explicit mesh parts, offsets, scales, and color slots. Character-mode assembly performs a real solid union; parts must intersect their supports.
- **Known item families:** use `procedural` with a fresh builder (`potion`, `weapon`, `shield`, `scroll`, `jewelry`, `key`, `food`, `lamp`, `tool`) and explicit `design` choices, e.g. `{"kind":"sword"}`. Continuous profiles and real solid unions create their exterior geometry. The standalone `steak` builder remains a small study. Legacy `archetype` builders are blockout options and may fail the topology gate. No procedural backend makes provider calls.
- **AI reconstruction:** use `image3d` with an existing reference; it uses the shared provider and may need Blender cleanup. Failure must remain visible rather than silently switching methods.

Preserve the original models' silhouette, bevels, curved profiles, rims, collars, holders, wraps and other functional secondary forms. Inspect and compare the original before replacing a design with a procedural family. Generic primitives are blockouts, not a fidelity target.

Separate physical shape from surface color. Author color on the existing exterior using face/corner color regions or vertex colors; preserve hard color boundaries with separate corner attributes where needed. The character exporter stores named face colors through a gradient palette atlas. Actual `COLOR_0` vertex colors are also supported. Either representation is valid surface coloring; a painted stripe, rune or material tint must not become an overlapping shell simply to carry color. Real raised rims, inset gems, wraps and cut grooves still need geometry.

An asset may contain several meshes or closed surface components. Chains, separate slices, candle assemblies and material parts need not become one boolean solid. Use shared topology or boolean union where a design requires a continuous body or actual carving; do not apply union globally as a quality requirement. Joining objects does not repair bad attachments, and counting components does not establish floating geometry. Inspect actual contact, interlocking links, support and intended gaps. Never add hidden bridges or inflate forms to satisfy a connectivity check.

Generated items default to **the character surface model** (`color_mode: character`): `core/export.py` shares palette resolution, gradient atlas, height/slope ramps, crease normals, PBR presets, and emission between skinned characters and static items. Use named `colors` and `materials` overrides as with characters. Do not substitute a separate flat vertex-color style by default. Explicit vertex/legacy palette modes remain available for existing assets; source GLBs retain their authored materials unless explicitly converted through `surface` as described below.

Review geometry for accidental floating parts, unwanted intersections, duplicate color shells, malformed normals and unintended holes. Check silhouette, medium-scale details, color boundaries and gameplay readability against the original in the Godot lineup. Technical checks do not establish visual fidelity.

Validation defaults to `topology: closed_assembly`, permitting multiple closed components while reporting the need to review their actual attachments. Explicit repaired-source `authored_assembly` policies report retained thin/open or coincident surfaces as review exceptions; never describe them as closed-mesh certifications. Use `closed_surface` only when the design requires one surface. Finite geometry, budgets, closedness, winding and duplicate/degenerate-face checks remain. The procedural `Assembly` helper requires one physical body for explicit union; enclosed cavity surfaces are permitted. Use `surface.repair: boundaries` for conservative source cap/winding repair. `surface.paint: scroll_ink` converts raised glyphs into face colors on parchment, and `surface.carve: cheese_holes` replaces the original cheese spots with actual cavities. These deliberate conversions preserve functional detail rather than genericizing the models.

For originals using the Studio's verified 8×4 palette, use `backend: source`, keep `source.glb` untouched, and opt into `surface: {palette: studio_grid, slots: {...}}` with `color_mode: character`. Read the exact JSON example in `asset-studio/README.md` and the current `objects/tools/tool_grappling_hook/item.json` / `lineup.png`. This recovers authored face color regions by palette cell and uses the character exporter for ramps, normals and materials, preserving world-space triangles without adding shells or unioning parts. Use explicit named color/material overrides, not sampled baked brightness as fresh albedo. Unsupported textures fail rather than guessing. This conversion flattens static scene hierarchy; omit `surface` when source hierarchy/shading must remain unchanged. Always compare the original, candidate and gameplay-scale lineup before accepting the result.

`item build` stages and validates art outside the repository in its checkout-specific temporary work folder. Use `item preview` only when an optional software study (temporary `preview.png`) is useful, then use `item lineup` for real Godot renderer screenshots (windowed GPU required). `lineup.png` includes front, three-quarter, side, and an elevated game-camera view with the studio mannequin for scale. Use `--mount right_hand` or `left_hand` for a static hand attachment; this tests the art's grip/orientation without loading a gameplay item. It does not certify later equipment scene transforms, effects, or runtime behavior. Include a concept panel when a reference is present. Inspect the rendered lineup before calling an asset visually complete.

The recipe controls dimensions, rotation, offsets, and authored/ground/center/grip pivots. Generated geometry obeys triangle budgets and optional material/texture-size budgets. Color settings do not repair bad topology by themselves. Fix intersecting shells, twisted surfaces, poor silhouettes, and unnecessary interior faces in the source/builder.

`item preserve --all --revision <original-revision>` adopts original geometry for the approved character-shading workflow. Use `recreate` only for an explicitly requested family/blockout experiment; it is not the production migration path. `build --all` and `validate --all` check the catalog; `lineup --all` renders all items in one GPU session, saves temporary per-item review lineups without modifying final catalogs. Inspect them, then run `item publish <key>` to replace the final GLB/lineup and manifest in `catalogs/items/<category>/<name>/`. `item catalog` regenerates those sheets from published entries without rerendering. Only `item export` publishes GLBs to `assets/custom-items/`; Godot handles the import settings. When explicitly using vertex colors, studio-generated materials opt into the art-only `godot/import_item.gd` post-import correction for this Godot version; lineups apply the same correction. Preserve custom artist import scripts and compose them explicitly if needed. `--no-import` explicitly defers engine verification. Recipes, sources, and staged builds must match before publishing. Preserve an adopted model locally as `source.glb` and update its recipe before modifying it; adoption alone never improves or validates its visual quality.

### World props: match the dungeon pack, never approximate it

Room furnishings, set dressing and any prop that stands in the world must read as part of the existing KayKit dungeon. Two passes that matched scale but invented their own palette, smooth primitives and realistic proportions were rejected. Work in this order:

1. **Reuse before modeling.** Search the pack and our own art first (`find assets -iname '*axe*'`; `assets/kaykit-*`, `assets/custom-*`). Compose existing models in a game scene (`scenes/props/furnishings/*.tscn`: the chopping block holds the pack's woodcutter's axe), or lift the exact part with [`items/borrowed_parts.py`](../../../asset-studio/items/borrowed_parts.py), which keeps its UVs, material and texture: add an entry to `PARTS` and run `python -m items.borrowed_parts` from `asset-studio/`, as the bunk bed does with the pack's own pillow and blanket. Never re-model a detail the pack already has. Figures (statues, effigies) are the pack's own characters carved in stone: pose one with [`items/pose_kaykit.py`](../../../asset-studio/items/pose_kaykit.py) in headless Blender (`-b --factory-startup`, never the user's open session) and union its parts with `furnishings.figure`; hand-modeled chibi figures read as unrecognizable blobs.
2. **Wear the world's texture.** Studio props use `color_mode: "world"` ([`items/world_surface.py`](../../../asset-studio/items/world_surface.py)): each slot maps to a swatch of the pack's `dungeon_texture.png`, shaded by height the way the pack does, under its single material, matte as the project imports it (metallic 0, roughness 0.85). Add or tune a slot in `SWATCHES` (cell, u, shade offset) against a pack asset of the same material instead of choosing RGB values; stone walls, for example, sit in the darker half of the stone swatch.
3. **Build with the chunky vocabulary** in [`items/furnishing_kit.py`](../../../asset-studio/items/furnishing_kit.py): separate thick boards with broad chamfers that read as grooves, square capped posts, staggered stone courses, logs of ringed end grain inside a rim of bark strips, rolled rims on cast metal, oversized rivets and candles, chibi heads (about a third of a figure). Stubby and thick beats realistic.
4. **Show age.** `board()` chips corners, bites small notches out of long edges and grooves cracks along the grain; keep `wear` on. Paper gets torn notches along free edges and fold creases; a curled sheet is one profile rolled into a spiral (`curl_profile`), never separate tubes. Missing edges show fresh material (`wood_light`) in the cut.
5. **Compare side by side, then show the user.** Render each new prop in one row with the pack assets it will stand beside (tables, barrels, beds, pillars, candles) using [`scripts/compare_models.gd`](scripts/compare_models.gd) (windowed; it also loads staged Studio builds by absolute path), read the PNG, fix the palette, proportion or detail gaps it shows, and send the user a contact sheet before wiring content into rooms. Lineups alone hide these gaps.

```bash
.agents/skills/godot-mcp/scripts/godot.sh --path "$PWD" --script .agents/skills/assets/scripts/compare_models.gd -- \
  /tmp/compare.png 30 res://assets/kaykit-dungeon-remastered/bed_frame.gltf res://scenes/props/furnishings/bunk_bed.tscn
```

Game-side wiring (`Prop`/`PropGroup` resources, lit and composed scenes, room types) follows the [room assembly guide](../../../scenes/levels/generators/assembly/README.md).

### Using `blender-mcp` with Asset Studio
When authoring custom armor, helmets, or props in Blender:
1. **KayKit `Mannequin_Medium_Head` Facial Reference & Reusable BMesh Helpers (`asset-studio/core/blender_compare_render.py`):**
   - **Always use the seamless KayKit facial head (`Mannequin_Medium_Head` with visible eyes, eyebrows, nose, mouth, and ears)** in both the 3-view turnaround base passed to the concept artist (`base_kaykit_head_3view.png`) and in `COL_Reference` inside Blender. This guarantees that brow bands, nasal guards, visor cutouts, and cheek/mouth openings align with the real KayKit character face on every iteration pass rather than discovering facial alignment misses at the end.
   - `Mannequin_Medium_Head` (`X = ±0.453` head / `±0.535` ears, `Y = [-0.460, +0.460]`, `Z = [-0.405, +0.475]`) is a **rounded box (superellipse)** in XY whose rear-diagonal corners (`azimuth 120°–150°`) bulge out. Always call `bcr.enforce_head_clearance_on_grid(grid, min_outer_clearance=0.038..0.056)` before solidifying.
   - Reuse the built-in helpers in [`core/blender_compare_render.py`](../../../asset-studio/core/blender_compare_render.py) (`import blender_compare_render as bcr`):
     - `bcr.get_or_create_workbench("COL_Workbench")` and `bcr.make_pbr_mat(...)` (accepts 3-tuple RGB or 4-tuple RGBA).
     - `bcr.solidify_open_shell(bm, thickness=0.048, inner_mat_idx=1, horizontal_only_verts=brow_verts)` so visor/brow cutout rims offset horizontally and never dip below exterior gold/metal trim.
     - `bcr.inset_quad_slot_pockets(bm, face_groups, border_thickness=0.019, depth=-0.028)` to create recessed ventilation slits and panel grooves directly on the quad grid—**never** use Blender's `BOOLEAN` (`solver="EXACT"`) on solidified shells with concave creases, which silently drops cuts or slices horizontal slits across the mesh.
     - `bcr.build_lofted_tube_sharp_rails(bm, rings, ...)` for bevel-profiled crests, brow trims, horns, and feather blades. When mounting multi-tier wings or feathers into a circular ear boss/medallion, seat each blade's root (`Station 0`) directly inside the cylinder barrel (`behind the raised outer bezel ring`) and sweep monotonically without downward-looping collar tubes that twist cross-sections.
     - **Coaxial Collars, Snug Wraps & Tapered Internal Y-Roots for Branching Props:** When building socket collars (`AntlerCollar`) or leather wraps (`AntlerWrap`) around a curved shaft, align the collar stations along the shaft's exact initial tangent vector (`base_dir`) and match the polygon side count (`sides=6`) and phase angle of the shaft so wraps hug the shaft with zero radial gap. When branching secondary tines off a main beam, start `ring[0]` on the parent centerline with a reduced internal radius (`r0 ≈ 0.45 * parent_radius`) and flare to full tine radius at `ring[1]` where the branch exits the parent surface, preventing tilted base rings from poking out the underside of the parent shaft.
     - `bcr.finalize_and_link_mesh(wb, name, bm, mats, sharp_angle_deg=32.0, split_sharp=True)`—in Blender 4.x/5.x EEVEE, `edge.smooth = False` alone does not split normals; `finalize_and_link_mesh` physically splits sharp bevel rails via `bmesh.ops.split_edges` while keeping C1 bell-curve flares (`< 32°`) smooth (or pass `sharp_angle_deg=1.0` for crisp low-poly faceted leather/bone props).
2. **Concept-vs-Wireframe + Automated Seam Audit Loop (3–6 iterations):**
   - Align vertices, edge loops, and proportions BEFORE picking final flat KayKit palette colors or carving surface effects.
   - Render synchronized 3-point-lit solid + Holdout hidden-line wireframe passes (`lens=135.0, distance=10.2` telephoto default to match 2D turnaround perspective across all views) **plus** 4 zoomed macro junction closeups (`brow_nasal`, `left_socket`, `right_socket`, `rear_band`) via `bcr.render_compare_passes(out_dir, prefix)` and `bcr.render_junction_closeups(out_dir, prefix)`, then run `concept_compare.py` with `--render-dir` and `--prefix`:
     ```bash
     uv run --with pillow --with numpy --with scipy --with google-genai python asset-studio/core/concept_compare.py \
       --concept <concept.png> --render-dir <out_dir> --prefix <prefix> \
       --asset-name "<AssetName>" --audit --out <diag.png>
     ```
   - Use the printed `[3D Axis Hint]` (and per-Z-slice `Front X` vs `Side Y` intervals when calibrating multi-branch antlers or feather fans) to decouple lateral `X` width (from `Front`), front-to-back `Y` sweep (from `Side`), and vertical `Z` height (since `Three-Quarter` projects screen-right as `u = +0.81*X + 0.59*Y`).
   - Inspect `diag.png` **Wireframe on Concept**, `diag_closeups.png`, and the `gemini-3.8-flash` JSON audit (`diag_audit.json`) explicitly for:
     - **Structural junctions & physical plausibility**: socket/ear-boss roots seated cleanly without twisted collars or diagonal clipping; vertical crests/straps tapering or terminating flush into rear/front rims; zero dummy-head bleed-through at the `135°` rear corners.
     - **Silhouette & small detail proportions**: recessed quad-inset vents, oversized chunky KayKit studs, faceted crystalline gems, and smooth S-curve/bell-curve neck flares.
     - **Shading & material separation**: crisp 3-point key/fill/rim separation and split sharp bevel edges.
3. **Final Character Mounting & 360° Turntable Verification:**
   - Before declaring any headgear asset complete, call `bcr.render_character_showcase(out_dir, prefix, character_glb=..., hide_ears=..., hide_hair=True)` in Blender (which mounts `COL_Workbench` 1:1 onto a rigged KayKit Adventurer via `scale_f=1.000, y_shift=-0.0155, z_shift=+0.4018`) and `cc.compose_character_showcase(out_dir, prefix, out_sheet, out_gif)` to inspect the 6-panel character showcase sheet and 360° rotating GIF.
4. Save editable Blender sources / harvest reusable parts into domain `meshes/`, then build and review with `studio.py`.

### Curated equipment

Build weapons with the KayKit-style weapon families (`axe`, `blade`, `blunt`, `polearm`, `staff`, `ranged`) described in `asset-studio/objects/weapons/README.md`. Use `builder: shieldcraft` for curated shield combinations (`knight`, `bone_kite`, `barbarian`, `spiked_buckler`, `guard_scutum`, `paladin`). Do not generate a full shape-by-material matrix unless asked. Shields use the original KayKit shallow V cross-section as a geometry reference; round bodies remain flat and rectangular scuta use a cylindrical bend. Bone-themed shields have a wooden body, a bone frame and an attached carved skull, rather than a solid bone body. Their selected concept sheet and prompt are shared under `objects/shields/reference.png` and `reference.prompt.txt` and referenced through explicit `res://` paths. `item lineup --mount shield` keeps the grip at the left hand while presenting the shield upright for static inspection; batch lineups select this mount for shieldcraft recipes. These previews do not author gameplay equipment transforms.

### Reusable shield and prop detail

Reuse `prefabs/details/construction.py` (`timber_plank`, `repair_plate`, `corner_bracket`) and the existing `kit.stud` for physical fittings. `studio.py schema details construction` exposes parameters and the prefab details catalog includes them. Boards need actual bevels, subtle bow/twist, uneven edges and few deliberate grain splits/knots; vary base colors only slightly. Clip boards beneath their frame and fuse to backing. Wear wood before adding hardware so wood cuts cannot drill through later studs. `prefabs/details/wear.solid_gash` is a closed negative cutter matching the character armor's chiseled V-groove style; use it for closed props instead of applying the surface-patch `gash` blindly. Dents and scratches carve the supporting surface, with cavity-face colors rather than dark overlay meshes. The curated shield recipes expose `design.details`; most stay below 5,000 triangles and the detailed wooden scutum allows 6,000. Track all shared helper inputs, check topology after export and inspect gameplay-scale lineups.

### Heroic and royal shield additions

Additional shieldcraft kinds are `heroic_quarters`, `heroic_sun`, `wood_simple`, `dragon_long`, `iron_tower`, `royal_crown` and `royal_lily`. Their shared selected concept is `objects/shields/heroic-reference.png` with its exact prompt sidecar. Use `prefabs/details/heraldry.py` for reusable beveled crown/lily appliqués and physically overlapping scale plates; discover them through `studio.py schema details heraldry`. Heraldic paint partitions the existing face, while raised metal ornaments have actual thickness and attachments. Simple wooden shields use wood braces, pegs and handles without metal. Round frames need continuous bevels and inset backing to avoid rim artifacts. Vary repairs deliberately: a replacement board can distinguish one shield from another shield's forged patch. Keep these combinations distinct rather than generating a material matrix. Paid-pack images may guide new original modeling; record provenance accurately.

V-shaped shields use two joined angled panels with `design.fold_degrees` (default 22 degrees per side). Keep the center ridge sharp through explicit corner normals while retaining the shared character shading on curved fittings. Verify the bend in side and three-quarter views and validate the joined topology; object joining alone does not repair a seam. Round shields remain flat and rectangular scuta remain cylindrical. Four-panel designs are optional future art direction, not required for the current two-panel process.

Inset colored plates beneath framed shield rims (currently 20 mm), including their outer side walls. Coincident outer plate/rim boundaries can expose colored strips after bending; validate the material boundary geometrically and inspect the three-quarter view. Reuse `prefabs.details.heraldry.horn` for curved tapered horns with broad buried roots. The approved dragon reference uses two substantial swept shoulder horns with leather collars and metal mounts, plus a notched top frame. Keep its individual selected reference/prompt beside the recipe. Reuse `heraldry.bound_horn` for the closed horn-and-collar assembly; its root must join the shield. Review this tall design beside the mannequin when the generic hand pose obscures its silhouette.

For horn-to-shield joins, use substantial `construction.horn_socket` mounting boots aligned with each root, broad shoulder plates and deliberately oversized fasteners. `construction.rim_clamp` wraps front and back jaws around a rim with an outside bridge and broad chamfers. Favor these readable structural connections over tiny flat tabs. The dragon uses larger shoulder clamps and roughly doubled side-clamp widths for the requested exaggerated fantasy proportions; preserve its approved horn silhouette and scale treatment. Both fittings are reusable and discoverable in the construction schema and prefab catalog.

### Keep the Studio clean

Keep current authoring state only: recipe, original source GLB/Blender project, one selected reference and prompt. Store reproducible models, candidate lineups, software previews and technical reports in OS temporary storage outside the repository; the hidden `.export.json` is only the current export-ownership receipt. `catalogs/items/` owns one overview, category images, README, `status.json` and final per-item folders. `catalogs/costumes/` owns final wearable models, still/motion lineups and manifests. Shields are authored in `objects/shields/`; existing game paths `assets/custom-items/armor/shield/` remain compatible. Prefab catalogs belong in `catalogs/prefabs/`.

Do not create `reviews/`, numbered review images, rejected-recipe copies or duplicate source snapshots inside Studio. Use Git history for previous versions and the image tool’s preview history for unselected concepts. Remove temporary Godot shot frames and manifests after composing the current lineup. CLI catalog generation overwrites the current files and removes stale category images.

### Complete character costume references

Use `{"kind":"costume_ref","name":"knight"}` in character outfits to select the entire authored `costumes/knight/costume.json` assembly and palette, including shoulders. Character anatomy and explicit colors take precedence. Low-level `kind: costume` entries select procedural builders inside costume definitions. Orc warrior, shaman and goblin sets are preserved legacy designs requiring rework, not current quality references. Prefab sheets exclude retired complete armor presets; source mesh folders never receive generated previews. `catalog <domain>` stages externally and `catalog <domain> --publish` validates and copies the reviewed candidate to the final catalog.
