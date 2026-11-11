# Debug harnesses

Open the scene in Godot and run it with **F6**. Each feature folder keeps its scene, scripts and supporting fixtures together. Harnesses author their own staging; the generic standalone-scene bootstrap skips this directory.

| Harness | Entry point | Purpose |
| --- | --- | --- |
| Room generation | [rooms/room_generation_harness.tscn](rooms/room_generation_harness.tscn) | Inspect each realm's room types alone or in a production floor |
| Combat | [combat/combat_harness.tscn](combat/combat_harness.tscn) | Interactive combat with the game's debug menu, monster tactics and scripted traces; [guide](../../docs/combat-harness.md) |
| World effects | [effects/world_effect_harness.tscn](effects/world_effect_harness.tscn) | Replay and tune production trap/secret discovery, loot glow, blood hit and prop x-rays |
| Audio | [audio/audio_harness.tscn](audio/audio_harness.tscn) | Audition production cue profiles and exploration/boss music, compare left/center/right playback, pause routing and voice limits |
| Actor/item effects | [effects/actor_item_effect_preview.tscn](effects/actor_item_effect_preview.tscn) | Inspect one effect/subject, skin finishes, teleport fades and fitted curse wisps; [guide](../../docs/world-effects.md) |
| Scroll reading | [effects/runes/scroll_rune_study.tscn](effects/runes/scroll_rune_study.tscn) | Preview the production ring with its assigned scroll appearance; [guide](../../docs/world-effects.md#scroll-reading-preview) |
| Screen effects | [effects/screen_effect_harness.tscn](effects/screen_effect_harness.tscn) | Exercise overlays in gameplay; [guide](../../docs/screen-effects.md) |

## Lighting, camera and floor

The audio harness uses a diagnostic marker stage and listener for stereo/distance audition. Combat synchronization uses the production combat arena; see [audio authoring](../../docs/audio-system.md).

Visual harnesses use the game's presentation. It lights through `scenes/levels/lighting/world_lighting.tscn`, the game's own environment with the **Dark level** switch, and the hero carries `scenes/player/player_light.tscn`; workbenches without a playing hero place that light beside their subject. The combat arena uses the game camera (`scenes/main/camera/game_camera.tscn`, its angle and zoom in `game_camera_profile.tres`), and the workbench cameras start at the same angle. Subjects stand on `scenes/debug/stage/sample_floor.tscn`, paved with the dungeon's stone floor tile. Room generation keeps its bright top-down overview, set on its level's lighting node.

## In-game debug menu

The **Debug** button in the gameplay HUD (debug builds only) has one submenu per tool in [`scenes/ui/debug/`](../ui/debug/): **Items** (any authored item, with quality, curse and wand/staff spell pickers, a **Delivery** choice of drop, backpack or equip, and **Fill backpack**), **Spells** (any spell at a chosen level, without a wand), **Monsters** (any monster or the practice dummy, in a ring of 1, 3 or 5 around the hero, with AI, riposte, forced defense, invulnerability and rewards toggles), **Hero** (restore, kill, and hero builds from `scenes/debug/hero_build_profiles/`, which replace the hero's attributes and inventory with exact items), **Combat** (hit volumes, hurtboxes, hit effects, movement effects, camera shake, slow motion, combat and AI logs) and **Level** (navigation, x-rays, darkness, an arena's walls, map reveal, restart and run seeds). Each tool is a `DebugTool` child of `debug_menu.tscn` listed in the menu's `Tools`. Its entries only call the tool's public methods, which tests call directly. The menu's `World` names the `IDebugWorld` the tools act in: the `Level` in `main.tscn`, or the `DebugArena` at the root of the combat harness, which builds monster navigation from its own walls. Items authored into a folder of the Items tool's `Categories`, spells added to its pool and monsters added under `scenes/enemies/` appear without code changes. In the game a spawned monster plays like a regular one: it targets the hero at once, attacks, defends, counterattacks and drops its rewards. The combat arena authors them passive instead (AI, riposte and rewards off). Either way a floor never saves them, and **Invulnerable** stops every hit, though not poison ticking on its health. The [debug toolkit plan](../../docs/specs/debug-toolkit-plan.md) lists what remains open.

## Room generation

The scene instances the production level and lists its generator's realms; it keeps no catalog of its own. The **Realm** picker selects a realm: the room pool's types tagged for it, then the entrance and exit, form the recipe list, and the style pickers list only that realm's styles; the floor view generates that realm's first depth, with its rooms and corridors.


- **Single room:** select a recipe and seed, rotate in quarter-turns and toggle furnishings. Room types expose every axis as an override — Shape, Walls, Floor and Columns (type chance, off, or a specific pattern) — and run the production `RoomAssembler` with them. The **Walkability overlay** colours standable floor: green is reachable from the doorways, blue is reserved circulation or a stair landing, red is an inaccessible pocket. Overrides never change production Resources.
- **Generated floor:** uses the production packing, corridor, wall and furnishing pipeline with the same factory. Recipe selection and room rotation are disabled in this view.
- **Controls:** R advances the seed, A toggles furnishings, W toggles the walkability overlay, F switches views, 1–9 and 0 select the first ten room types, Q/E rotates a room. The panel also accepts a manual seed and can rebuild without advancing it.
- Generation errors appear in the status panel and Godot output. These static previews do not exercise player or monster movement—use gameplay for those checks.

[Level folder guide](../levels/README.md) identifies where to change generator code, room presets and furnishing assets. Regenerate after editing a preset; the shared Resource remains the authored definition.

## World effects

World, actor/item and scroll previews share tabs through `effect_harness_navigation.tscn`. Open any of the three entry scenes with F6; the tabs navigate among them. `EffectPreviewCamera` starts at the game camera's angle and provides right-drag orbit and wheel zoom, plus the game's existing camera bindings (comma/period and apostrophe/slash by default); **Game view** returns to the game's angle and zoom.

Review movement dust and dash feedback in the [combat harness](combat/combat_harness.tscn): move and dodge the hero, toggle **Combat → Movement effects**, and add walls under **Level → Walls**. The `movement_feedback.json` scenario replays a run-stop-dash sequence. See the [movement presentation guide](../../docs/movement-feedback.md) for cue behavior and limits.

The **World effects** tab stages production trap/secret discovery, loot glow, blood hits and prop x-rays. Select a world effect, then use its relevant reveal/replay or visibility button. Discovery starts concealed; choosing an option resets the original surface. Loot loops continuously. Glow/blending options belong to discovery, and wall styles belong to secret-door/x-ray previews. These are preview-only settings. X-rays honor production behavior and do not alter AI, discovery or global debug toggles.

The **Actor / item effects** tab shows one effect and one subject with replay/clear, actor pose, playback and **Dark level**. Subject labels and scenes are authored together in its `Subjects` dictionary. Shared `EffectPreviewStage` and `EffectPreviewControls` helpers prepare actors and build controls for these previews; tabs are the sole navigation path. Replay restarts bursts and sustained effects. **Faceted shading** and **Polished steel** appear only for the applicable skins. **Preview full teleport** appears only for teleport effects on actors and runs the same fade/relocate/fade-in sequence as gameplay. Emissive effects have matching local lights; material-only skins and invisibility do not. Invisibility and Frozen use this same actor-effect selection and playback. Frozen changes the appearance only in this workbench; pose pausing is demonstrated by Frost Nova.

The **Scroll reading** tab shows the approved ring and its assigned paper appearance, with replay, slow motion/pause, still-frame inspection and **Dark level**. It uses the production renderer and the current run's appearance assignments.

Auto-replay, object comparisons, seed editing, crowds, combined-effect buttons and item-knowledge/source/death controls are removed. Automated tests exercise lifecycle and combinations. The separate [screen-effect harness](../../docs/screen-effects.md) exercises fullscreen overlays in gameplay.

## Spells

Cast spells in the [combat harness](combat/combat_harness.tscn) rather than a preview: **Spells** casts any pooled spell at any level with the hero's weapon hit, and a wand or staff from **Items** casts its bound spell with mana. **Monsters → Count** places 1, 3 or 5 practice dummies or monsters in a ring around the hero, **Level → Walls** switches the arena's wall arrangement (Default, One wall, Open or Small room, from the shared `debug_stage.tscn`; the arena starts open) and **Level → Dark level** darkens it exactly as a dark dungeon floor: the ambient light goes out and the hero's light shrinks. Arrangements are authored as `DebugArrangement` resources on the stage, and the arena rebuilds monster navigation for each.

Spell scenes compose `VisualEffectTimeline` with focused motion components. Falling meteors and rocks share `FallingBodyMotion`; fire ribbons and guards use their own components, while ice crystals read the shared timeline. Solar Flares instances a reusable orbit scene, and Frost Guard uses a crystalline guard scene. Tune component exports and scene materials in the Inspector.

Area visuals capture a sampled line-of-sight boundary at cast time. The KayKit walls include an opening for inspecting clipping around corners and through doorways. Moving blockers and height differences require further work before gameplay integration; see [area boundaries](../../docs/specs/elemental-spells-plan.md#targeting-area-and-guard-rules).
