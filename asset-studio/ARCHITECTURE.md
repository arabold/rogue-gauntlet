# Asset Studio architecture

The Studio owns art: geometry, materials, palettes, scale, skeletons, weights, animations,
previews and export metadata. Game scenes, physics, hurtboxes, navigation dimensions, stats,
behavior, combat timing, loot and spawning belong to game-side authoring.

No separate Godot project is required. The existing game project can host optional GPU
reviews and import validation. `godot/godot.sh` locates the engine through `GODOT_BIN` or
standard executable locations; Studio commands no longer depend on an agent skill launcher.

## Sources, builds, publications and game exports

- `characters/<family>/<name>/`: recipe, selected authoring sources, concepts and provenance.
- `rigs/`: local technical skeletons, animations and required skinning mannequins.
- `core.paths.work_root()`: disposable models, render frames, jobs, caches and rollback files,
  outside the repository. These can be rebuilt; they are not authoring inputs.
- `catalogs/`: selected final publications. Bound-source/golem entries include `model.glb`,
  `lineup.png`, `lineup.gif`, `asset.json` and a fingerprint `manifest.json`.
- `assets/custom-*`: configured game art destinations. Export may copy finished art here;
  it does not create gameplay Resources or actor scenes.
- `tilesets/<set>/<family>.json`: modular floor, wall and rug families built by `tiles/`. A family
  publishes one reviewed lineup to `catalogs/tiles/` and exports GLBs to `assets/custom-tiles/`;
  GridMap mesh libraries, collision and generator rules stay game-side.

`lineup` stages and renders a candidate without publishing. Explicit `export` for a bound
source or golem builds, validates and renders the candidate, writes art/import metadata to
the configured destination, verifies the actual Godot import, and publishes the selected
catalog entry. Publication rejects source/artifact drift. On import failure, the prior
GLB, `.import` and art metadata are restored. Existing Godot UIDs are preserved.
`--no-import` deliberately defers engine validation; the publication records that state.

The existing kit character and item/costume lifecycles remain supported. Their publication
commands and catalog organization are documented in [README.md](README.md).

## Character builders

All 18 registered recipes have an export handler through `studio.py list`, `lineup` and
`export`; `export --all` no longer silently excludes non-kit recipes.

| Builder | Inputs | Export path |
| --- | --- | --- |
| `kit` | Local modular parts, recipes, mannequin weights and technical humanoid templates | Existing character exporter |
| `skinned_source` | Selected bound GLB, semantic palette metadata and rig profile | `core/character_workflow.py` preserves geometry, skin and clips |
| `golem` | Procedural rigid rocks, local large rig and shoulder helper definitions | `core/rigid_export.py` binds rigid parts and bakes helper motion |
| `warden` | Reused plate construction, custom helmet and executioner blade, local large rig | Shared rigid exporter retains named removable gear and baked shoulder mounts |

Wolf and rat are registered as `beasts/wolf` and `beasts/rat`. Their selected GLBs retain
adapted skeletons and animation keys; export does not reshape them or transfer weights
again. `beasts/spider` is a surface-polished review candidate using the same builder;
its original geometry and motion are preserved, with named shell/eye palette controls.
These are credited CC0 derivatives, not claims of wholly original geometry.
Original donor appearance models and the old `creature study` builders are removed.

`source.blend` contains one scene, one creature mesh and its necessary actions/materials.
It contains no hidden study versions, other creatures or imported comparison characters.
`source.glb` is the authoritative bound input consumed by the CLI; Blender hand edits are
not automatically synchronized into it.

`surface.json` binds named palette slots to the exact selected source hash and atlas
layout. Recipe colors regenerate the same atlas without changing geometry, normals,
weights or clips. Changes to source geometry/UV layout require refreshing this metadata;
export fails rather than guessing at new surface regions. Scale/proportion edits from the
studies are already baked into the sources, so their current recipe scale is `1.0`.
Additional recipe scale applies once to mesh, skeleton and motion. Rat ground correction
is preserved in its selected source and scales with the whole creature.

The Forsaken Warden (`bosses/forsaken_warden`) is the second authored large character.
It reuses the plate vocabulary on the same large rig, with rigid armor and separately
named sword, shield and horn meshes. Its recipe maps boss animation roles explicitly;
all original clips remain in the GLB. The equipment is art geometry, without generated
gameplay resources or phase logic.

## Technical rig ownership

`rigs/creatures/{blob,spider,wolf,rat}/rig.glb` contains skeleton/bind data and normalized
animation clips without appearance geometry or textures. Profiles retain source edition,
author, license, original hash and technical-rig hash. Blob is rig-ready without selected authored art. Spider has a registered derivative
under visual review; it has not been published to the final catalog or game.

`rigs/large/rig.glb` includes general, movement and melee clips. Golem export retains rigid
weights, adds the two half-rotation shoulder joints and bakes them into ordinary GLB tracks.
`Rig.load("large")` uses this same local source; the redundant old general-only GLB is gone.

Humanoid templates retain skeletons, animations and tiny authored attachment markers,
without original visible character meshes or textures. Template cutover was checked by
comparing exported scene structure, materials, animation definitions and every decoded
accessor for all six templates. The medium technical rig also has no appearance payload.
The two local mannequins are deliberate exceptions: their geometry and weights are needed
for skin transfer. Curated geometry prefabs and selected derivative authoring sources are
also retained; full original asset packs are not part of the Studio build inputs.

Creature imports disable `animation/remove_immutable_tracks`, retain constant rest/reset
tracks, and explicitly assign loop modes to every exported clip. Art inspection discovers
the actual skeleton/AnimationPlayer rather than assuming a humanoid node path. Missing
roles remain missing; for example, the rat does not acquire a fabricated hit clip.

## Entry points and validation

```bash
./.venv/bin/python asset-studio/studio.py list
./.venv/bin/python asset-studio/studio.py lineup beasts/wolf beasts/rat golem/stone_golem
./.venv/bin/python asset-studio/studio.py export beasts/wolf beasts/rat golem/stone_golem
./.venv/bin/python asset-studio/studio.py audit
PYTHONDONTWRITEBYTECODE=1 ./.venv/bin/python -m unittest discover -s asset-studio/tests
```

`core/character_workflow.py` owns bound/golem build, validation, review, publication and
art-copy orchestration. `core/publication.py` supplies receipts. `godot/inspect_art.gd`
checks the actual exported joints, every clip, loop modes and reset-track coverage.
`asset.json` / the game-side `<name>.asset.json` describes art dimensions, rig, clips,
loop intentions and provenance; it contains no gameplay properties. Bounds are sampled
motion bounds for art framing, not collision dimensions or a guarantee of every extreme.

Tests cover exact selected-source builds, scale and palette edits without rebinding,
clip switching, rig-only extraction, stale-source rejection, import rollback/UID retention,
and rigid golem shoulder motion. Godot previews and actual file imports provide engine
validation in addition to the Python checks.

## Remaining explicit dependencies

Six older humanoid recipes reference game weapon Resources for their selected held art:
goblin, necromancer, dark wizard, orc warrior, orc shaman and orc king. `audit` lists these
as `legacy_weapon_inputs`, separately from authoring hygiene errors. Wolf, rat and golem
have no such dependencies. Migrating those six should use selected Studio item sources and
verify grip, scale and appearance; do not silently substitute a different weapon merely to
remove a path or copy original third-party packs into the Studio.

Original-pack armor comparison and pack-acquisition/harvesting commands are opt-in tools.
They can read external art explicitly; they are not required for normal creature exports.
The game project/engine remains the optional rendering/import host, not a new Studio project.

`audit` rejects backup/cache/frame files in authoring folders and checks technical rigs
for appearance payloads. Blender backup suffixes are ignored by Git, but should still be
kept outside authoring folders. Final catalog images and receipts are intentional products
and must not be treated as interim files. Earlier migration inputs were archived outside
the repository before removal.
