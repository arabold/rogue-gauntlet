# Debug harnesses

Open the scene in Godot and run it with **F6**. Each feature folder keeps its scene, scripts and supporting fixtures together. Harnesses author their own staging; the generic standalone-scene bootstrap skips this directory.

| Harness | Entry point | Purpose |
| --- | --- | --- |
| Room generation | [rooms/room_generation_harness.tscn](rooms/room_generation_harness.tscn) | Play any realm's room types and room groups, trap gauntlets included, with the game's hero, HUD, camera and lighting |
| Map generation | [maps/map_generation_harness.tscn](maps/map_generation_harness.tscn) | Preview production floors for every realm and floor profile from above |
| Combat | [combat/combat_harness.tscn](combat/combat_harness.tscn) | Interactive combat with the game's debug menu, monster tactics and scripted traces; [guide](../../docs/combat-harness.md) |
| World effects | [effects/world_effect_harness.tscn](effects/world_effect_harness.tscn) | Replay and tune production trap/secret discovery, loot glow, blood hit and prop x-rays |
| Actor/item effects | [effects/actor_item_effect_preview.tscn](effects/actor_item_effect_preview.tscn) | Inspect one effect/subject, skin finishes, teleport fades and fitted curse wisps; [guide](../../docs/world-effects.md) |
| Scroll reading | [effects/runes/scroll_rune_study.tscn](effects/runes/scroll_rune_study.tscn) | Preview the production ring with its assigned scroll appearance; [guide](../../docs/world-effects.md#scroll-reading-preview) |
| Screen effects | [effects/screen_effect_harness.tscn](effects/screen_effect_harness.tscn) | Exercise overlays in gameplay; [guide](../../docs/screen-effects.md) |

## Lighting, camera and floor

Every harness except map generation shows its subject as the game does. It lights through `scenes/levels/lighting/world_lighting.tscn`, the game's own environment with the **Dark level** switch, and the hero carries `scenes/player/player_light.tscn`; workbenches without a playing hero place that light beside their subject. The combat arena and the room harness use the game camera (`scenes/main/camera/game_camera.tscn`, its angle and zoom in `game_camera_profile.tres`), and the workbench cameras start at the same angle. Subjects stand on `scenes/debug/stage/sample_floor.tscn`, paved with the dungeon's stone floor tile; the room harness stands its rooms on their own floors. Map generation keeps its bright top-down overview, set on its level's lighting node.

## In-game debug menu

The **Debug** button in the gameplay HUD (debug builds only) has one submenu per tool in [`scenes/ui/debug/`](../ui/debug/): **Items** (any authored item, with quality, curse and wand/staff spell pickers, a **Delivery** choice of drop, backpack or equip, and **Fill backpack**), **Spells** (any spell at a chosen level, without a wand), **Monsters** (any monster or the practice dummy, in a ring of 1, 3 or 5 around the hero, or scattered across the combat arena, with AI, riposte, forced defense, invulnerability and rewards toggles), **Hero** (restore, kill, a **Loot magnet** switch that leaves loot where it lies until the hero walks onto it, for staging pickups beside the hero, and hero builds from `scenes/debug/hero_build_profiles/`, which replace the hero's attributes and inventory with exact items), **Combat** (hit volumes, hurtboxes, hit effects, movement effects, camera shake, slow motion, combat and AI logs), **Level** (navigation, x-rays, darkness, an arena's walls, map reveal, restart, floor selection and run seeds) and **Playtest** (records a run across floors for the balance model's `simulate.py playtest` report; see [Calibration](../../docs/specs/balance-model.md#calibration)). Each tool is a `DebugTool` child of `debug_menu.tscn` listed in the menu's `Tools`. Its entries only call the tool's public methods, which tests call directly. The menu's `World` names the `IDebugWorld` the tools act in: the `Level` in `main.tscn`, or the `DebugArena` at the root of the combat harness or the room harness, which builds monster navigation from its own walls or its staged room. Items authored into a folder of the Items tool's `Categories`, spells added to its pool and monsters added under `scenes/enemies/` appear without code changes. Traps have no tool: gauntlet rooms play in the [room harness](#room-generation), floors place plate traps, and tests stage single hazards and plate traps themselves (`tests/TrapStaging.cs`). In the game a spawned monster plays like a regular one: it targets the hero at once, attacks, defends, counterattacks and drops its rewards. The combat arena authors them passive instead (AI, riposte and rewards off). Either way a floor never saves them, and **Invulnerable** stops every hit, though not poison ticking on its health. The [debug toolkit plan](../../docs/specs/debug-toolkit-plan.md) lists what remains open.

**Level → Change floor...**, directly below **Restart level**, jumps to any floor number while keeping the hero, equipment, run seed and visited-floor state. It bypasses stair progression gates for testing; ordinary stair travel keeps its normal requirements. The dialog accepts any positive floor number supported by the game, including higher floors beyond current content. Floor selection is disabled in the combat arena.

## Room generation

The room harness is an arena (`DebugArena`) with the game's hero, HUD and debug menu, camera and lighting, in which you play one room at a time. Its panel picks the **Realm** and the **Room**: the realm's room pool (ordinary rooms, entrances, exits and gauntlets), then the rooms the realm schedules, then room groups and every other room type under `room_types/` that suits it, so a new room type is playable before any floor places it. The production `RoomAssembler` builds the room in the realm's look, and the harness stands it with a two-tile corridor of the realm's floor and walls outside every doorway, between a group's rooms too. The hero waits in the corridor before the doorway nearest the camera, facing in; **Back to entrance** restores the hero and puts them there again.

- **Overrides:** Room types expose every axis — Shape, Walls, Floor and Columns (type chance, off, or a specific pattern) — plus the seed, quarter-turn rotation, furnishing and the **Motifs** picker, which furnishes as a floor carrying every motif (the default), none, or one of the realm's motifs. Overrides never change production Resources.
- **Walkability overlay** colours standable floor: green is reachable from the doorways, blue is reserved circulation or a stair landing, red is an inaccessible pocket.
- **Overview camera** frames the whole room from above instead of following the hero, for layout reviews and screenshots.
- **Play:** hazards, doors and furnishings act as on a floor, and monsters spawned from the debug menu navigate the room (the arena rebuilds its navigation from the room's collision on every rebuild). Trap gauntlets wake as the hero nears them; gauntlet rooms live in `scenes/levels/rooms/room_types/gauntlets/`.
- Generation errors appear in the status panel and Godot output.

[Level folder guide](../levels/README.md) identifies where to change generator code, room presets and furnishing assets. Rebuild after editing a preset; the shared Resource remains the authored definition.

## Map generation

The map harness instances the production level and lists its generator's realms; it keeps no catalog of its own. It builds the selected realm's first depth with the production generation, corridor, wall and furnishing pipeline and frames it from above. The **Profile** picker lists every floor profile in `scenes/levels/generators/floor_profiles/`, each with its own layout, map size and room count. The realm's first depth never holds a gatekeeper, so `boss.tres` previews without the gatekeeper-sized boss room. R advances the seed; the panel also takes a manual seed and rebuilds without advancing it. These are static previews: play rooms in the room harness and floors in the game.

## World effects

World, actor/item and scroll previews share tabs through `effect_harness_navigation.tscn`. Open any of the three entry scenes with F6; the tabs navigate among them. `EffectPreviewCamera` starts at the game camera's angle and provides right-drag orbit and wheel zoom, plus the game's existing camera bindings (comma/period and apostrophe/slash by default); **Game view** returns to the game's angle and zoom.

Review movement dust and dash feedback in the [combat harness](combat/combat_harness.tscn): move and dodge the hero, toggle **Combat → Movement effects**, and add walls under **Level → Walls**. The `movement_feedback.json` scenario replays a run-stop-dash sequence. See the [movement presentation guide](../../docs/movement-feedback.md) for cue behavior and limits.

The **World effects** tab stages production trap/secret discovery, loot glow, blood hits and prop x-rays. Select a world effect, then use its relevant reveal/replay or visibility button. Discovery starts concealed; choosing an option resets the original surface. Loot loops continuously. Glow/blending options belong to discovery, and wall styles belong to secret-door/x-ray previews. These are preview-only settings. X-rays honor production behavior and do not alter AI, discovery or global debug toggles.

The **Actor / item effects** tab shows one effect and one subject with replay/clear, actor pose, playback and **Dark level**. Subject labels and scenes are authored together in its `Subjects` dictionary. Shared `EffectPreviewStage` and `EffectPreviewControls` helpers prepare actors and build controls for these previews; tabs are the sole navigation path. Replay restarts bursts and sustained effects. **Faceted shading** and **Polished steel** appear only for the applicable skins. **Preview full teleport** appears only for teleport effects on actors and runs the same fade/relocate/fade-in sequence as gameplay. Emissive effects have matching local lights; material-only skins and invisibility do not. Invisibility and Frozen use this same actor-effect selection and playback. Frozen changes the appearance only in this workbench; pose pausing is demonstrated by Frost Nova.

The **Scroll reading** tab shows the approved ring and its assigned paper appearance, with replay, slow motion/pause, still-frame inspection and **Dark level**. It uses the production renderer and the current run's appearance assignments.

Auto-replay, object comparisons, seed editing, crowds, combined-effect buttons and item-knowledge/source/death controls are removed. Automated tests exercise lifecycle and combinations. The separate [screen-effect harness](../../docs/screen-effects.md) exercises fullscreen overlays in gameplay.

## Rendering regression check

Run `scripts/verify_helper_rendering.gd` windowed after changing actor helper passes or world fog:

```bash
.agents/skills/godot-mcp/scripts/godot.sh --path "$PWD" --windowed --audio-driver Dummy --fixed-fps 30 \
  --script res://scripts/verify_helper_rendering.gd -- /tmp/rogue-helper-rendering
```

It uses the combat harness's production skeleton and x-ray component, checks emerging and standing actors for unwanted helper color under fog, checks the teleport depth pass, and checks the silhouette behind a low wall at two camera clip ranges. It saves comparison PNGs and exits nonzero on failure. Headless rendering cannot run this pixel check. Add `--rendering-method mobile` to verify the Mobile renderer too.

## Spells

Cast spells in the [combat harness](combat/combat_harness.tscn) rather than a preview: **Spells** casts any pooled spell at any level with the hero's weapon hit, and a wand or staff from **Items** casts its bound spell with mana. **Monsters → Count** places 1, 3 or 5 practice dummies or monsters at random spots within 10 m of the hero (the Monsters tool's `ScatterRadius`), each at least its `ScatterSeparation` (6 m) from the hero and every other target, **Level → Walls** switches the arena's wall arrangement (Default, One wall, Open or Small room, from the shared `debug_stage.tscn`; the arena starts open) and **Level → Dark level** uses the gameplay darkness effect. Equip a torch or lamp through **Items** to open up the view. Arrangements are authored as `DebugArrangement` resources on the stage, and the arena rebuilds monster navigation for each.

Spell scenes compose `VisualEffectTimeline` with focused motion components. Falling meteors and rocks share `FallingBodyMotion`; fire ribbons and guards use their own components, while ice crystals read the shared timeline. Solar Flares instances a reusable orbit scene, and Frost Guard uses a crystalline guard scene. Tune component exports and scene materials in the Inspector.

Area visuals capture a sampled line-of-sight boundary at cast time. The KayKit walls include an opening for inspecting clipping around corners and through doorways. Moving blockers and height differences require further work before gameplay integration; see [area boundaries](../../docs/specs/elemental-spells-plan.md#targeting-area-and-guard-rules).
