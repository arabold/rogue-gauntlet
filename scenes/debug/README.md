# Debug harnesses

Open the scene in Godot and run it with **F6**. Each feature folder keeps its scene, scripts and supporting fixtures together. Harnesses author their own staging; the generic standalone-scene bootstrap skips this directory.

| Harness | Entry point | Purpose |
| --- | --- | --- |
| Room generation | [rooms/room_generation_harness.tscn](rooms/room_generation_harness.tscn) | Play any realm's room types and room groups, trap gauntlets included, with the game's hero, HUD, camera and lighting |
| Map generation | [maps/map_generation_harness.tscn](maps/map_generation_harness.tscn) | Preview production floors for every realm and floor profile from above |
| Combat | [combat/combat_harness.tscn](combat/combat_harness.tscn) | Interactive combat and spells with the game's debug menu, monster tactics and scripted traces; [guide](../../docs/combat-harness.md) |
| World effects | [effects/world_effect_harness.tscn](effects/world_effect_harness.tscn) | Replay and tune production trap/secret discovery, loot glow, blood hit and prop x-rays |
| Actor/item effects | [effects/actor_item_effect_preview.tscn](effects/actor_item_effect_preview.tscn) | Inspect one effect/subject, skin finishes, teleport fades and fitted curse wisps; [guide](../../docs/world-effects.md) |
| Scroll reading | [effects/runes/scroll_rune_study.tscn](effects/runes/scroll_rune_study.tscn) | Preview the production ring with its assigned scroll appearance; [guide](../../docs/world-effects.md#scroll-reading-preview) |
| Screen effects | [effects/screen_effect_harness.tscn](effects/screen_effect_harness.tscn) | Exercise overlays in gameplay; [guide](../../docs/screen-effects.md) |
| Loot supply probe | [balance/loot_supply_probe.tscn](balance/loot_supply_probe.tscn) | Headless: samples every loot source on seeded floors for the balance model's `simulate.py supply` |

Shared pieces live beside them: `stage/` (the `DebugArena` world, its `DebugStage` wall arrangements and the sample floor), `hero_build_profiles/` (hero presets), and the helpers behind two debug tools, `navigation/` (the clearance-grid overlay) and `playtest/` (the playtest recorder).

## Lighting, camera and floor

Every harness except map generation shows its subject as the game does. It lights through `scenes/levels/lighting/world_lighting.tscn`, the game's own environment with the **Dark level** switch, and the hero carries `scenes/player/player_light.tscn`; workbenches without a playing hero place that light beside their subject. The combat arena and the room harness use the game camera (`scenes/main/camera/game_camera.tscn`, its angle and zoom in `game_camera_profile.tres`), and the workbench cameras start at the same angle. Subjects stand on `scenes/debug/stage/sample_floor.tscn`, paved with the dungeon's stone floor tile; the room harness stands its rooms on their own floors. Map generation keeps its bright top-down overview, set on its level's lighting node.

## In-game debug menu

The **Debug** button in the gameplay HUD (debug builds only) has one submenu per tool in [`scenes/ui/debug/`](../ui/debug/). Each tool is a `DebugTool` child of `debug_menu.tscn`, listed in the menu's `Tools`, and its entries only call the tool's public methods, which harness scenarios and tests call directly. The menu's `World` names the `IDebugWorld` the tools act in: the `Level` in `main.tscn`, or the `DebugArena` at the root of the combat or room harness, which builds monster navigation from its own walls or its staged room. Entries a world cannot serve are disabled.

- **Items** (`ItemSpawnTool`): any authored item, with quality, curse and wand/staff spell pickers, a **Delivery** choice of drop, backpack or equip, and **Fill backpack**. Items authored into a folder of its `Categories` appear without code changes.
- **Spells** (`SpellCastTool`): any spell in its pool at a chosen level, cast with the hero's weapon hit, no wand or mana needed.
- **Monsters** (`MonsterTool`): spawn any monster under `scenes/enemies/` or the practice dummy, 1, 3 or 5 at a time in a ring around the hero, or scattered across an arena; toggles for AI, riposte, invulnerability and rewards; a forced defense mode; **Clear spawned** and **Kill all enemies**. In the game a spawned monster plays like a regular one: it targets the hero at once, attacks, defends, counterattacks and drops its rewards. The combat arena authors them passive instead (AI, riposte and rewards off). Either way a floor never saves them, and **Invulnerable** stops every hit, though not poison ticking on its health.
- **Hero** (`HeroTool`): restore health, kill the hero, a **Loot magnet** switch that leaves loot where it lies until the hero walks onto it (for staging pickups), and **Builds** from `scenes/debug/hero_build_profiles/`. A build applies its class's stat profile and attributes and replaces the inventory with exact items rolled from the run seed; the hero keeps their model and the run its class.
- **Combat** (`CombatDisplayTool`): debug meshes (attack hit volumes and helpers), hurtboxes, hit effects, movement effects, camera shake, slow motion, and the combat and AI logs.
- **Level** (`LevelTool`): the navigation overlay, **Dark level**, an arena's **Walls**, map reveal, restart, **Change floor...** and the run seed (copy, or replay a run from one). Floor travel and the run seed need a generated `Level`.
- **Visual effects** (`VisualEffectsTool`): door, monster and loot x-rays, atmospheric fog, ambient occlusion, focus blur, and the realm effects (surface marks, drips, dust, wind, reflections).
- **Playtest** (`PlaytestTool`): records a run across floors for the balance model's `simulate.py playtest` report; see [Calibration](../../docs/balance-model.md#calibration).

**Change floor...** jumps to any positive floor number, beyond current content too, keeping the hero, equipment, run seed and visited-floor state. It bypasses stair gates for testing; ordinary stairs keep their requirements.

Traps have no tool: gauntlet rooms play in the [room harness](#room-generation), floors place plate traps, and tests stage single hazards and plate traps themselves (`tests/TrapStaging.cs`).

## Room generation

The room harness is an arena (`DebugArena`) with the game's hero, HUD and debug menu, camera and lighting, in which you play one room at a time. Its panel picks the **Realm** and the **Room**: the realm's room pool (ordinary rooms, entrances, exits and gauntlets), then the rooms the realm schedules, then room groups and every other room type under `room_types/` that suits it, so a new room type is playable before any floor places it. The production `RoomAssembler` builds the room in the realm's look, and the harness stands it with a two-tile corridor of the realm's floor and walls outside every doorway, between a group's rooms too. The hero waits in the corridor before the doorway nearest the camera, facing in; **Back to entrance** restores the hero and puts them there again.

- **Overrides:** Shape, Walls, Floor and Columns (the type's chance, off, or a specific pattern), the seed, quarter-turn rotation, **Furnishings** and **Motifs**, which furnishes as a floor carrying every motif (the default), none, or one of the realm's motifs. Overrides never change production Resources.
- **Walkability overlay** colours standable floor: green is reachable from the doorways, blue is reserved circulation or a stair landing, red is an inaccessible pocket.
- **Overview camera** frames the whole room from above instead of following the hero, for layout reviews and screenshots.
- **Play:** hazards, doors and furnishings act as on a floor, and monsters spawned from the debug menu navigate the room (the arena rebuilds its navigation from the room's collision on every rebuild). Trap gauntlets wake as the hero nears them; gauntlet rooms live in `scenes/levels/rooms/room_types/gauntlets/`.
- Generation errors appear in the status panel and Godot output.

The [level folder guide](../levels/README.md) identifies where to change generator code, room presets and furnishing assets. Rebuild after editing a preset; the shared Resource remains the authored definition.

## Map generation

The map harness instances the production level and lists its generator's realms; it keeps no catalog of its own. It builds the selected realm's first depth with the production generation, corridor, wall and furnishing pipeline and frames it from above. The **Profile** picker lists every floor profile in `scenes/levels/generators/floor_profiles/`, each with its own layout, map size and room count. The realm's first depth never holds a gatekeeper, so `boss.tres` previews without the gatekeeper-sized boss room. R advances the seed; the panel also takes a manual seed and rebuilds without advancing it. These are static previews: play rooms in the room harness and floors in the game.

## World effects

World, actor/item and scroll previews share tabs through `effect_harness_navigation.tscn`; open any of the three entry scenes and the tabs navigate among them. `EffectPreviewCamera` starts at the game camera's angle and provides right-drag orbit and wheel zoom plus the game's camera bindings; **Game view** returns to the game's angle and zoom.

The **World effects** tab stages production trap/secret discovery, loot glow, blood hits and prop x-rays. Select a world effect, then use its reveal/replay or visibility button. Discovery starts concealed; choosing an option resets the original surface. Loot loops continuously. Glow and blending options belong to discovery, and wall styles to secret-door and x-ray previews. These are preview-only settings: x-rays honor production behavior and do not alter AI, discovery or the global debug toggles.

The **Actor / item effects** tab shows one effect on one subject with replay/clear, actor pose, playback and **Dark level**. Subject labels and scenes are authored together in its `Subjects` dictionary; `EffectPreviewStage` and `EffectPreviewControls` prepare actors and build controls. Replay restarts bursts and sustained effects. **Faceted shading** and **Polished steel** appear only for the applicable skins, and **Preview full teleport** only for teleport effects on actors, running the gameplay fade, relocate and fade-in. Emissive effects have matching local lights; material-only skins and invisibility do not. Frozen changes only the appearance here; Frost Nova shows the paused pose in play.

The **Scroll reading** tab shows the approved ring and its assigned paper appearance, with replay, slow motion/pause, still-frame inspection and **Dark level**. It uses the production renderer and the current run's appearance assignments.

Review movement dust and dash feedback in the combat harness: move and dodge the hero, toggle **Combat → Movement effects**, and add walls under **Level → Walls**; see the [movement presentation guide](../../docs/movement-feedback.md). The [screen-effect harness](../../docs/screen-effects.md) exercises fullscreen overlays in gameplay.

## Rendering regression check

Run `scripts/verify_helper_rendering.gd` windowed after changing actor helper passes or world fog:

```bash
.agents/skills/godot-mcp/scripts/godot.sh --path "$PWD" --windowed --audio-driver Dummy --fixed-fps 30 \
  --script res://scripts/verify_helper_rendering.gd -- /tmp/rogue-helper-rendering
```

It uses the combat harness's production skeleton and x-ray component, checks emerging and standing actors for unwanted helper color under fog, checks the teleport depth pass, and checks the silhouette behind a low wall at two camera clip ranges. It saves comparison PNGs and exits nonzero on failure. Headless rendering cannot run this pixel check. Add `--rendering-method mobile` to verify the Mobile renderer too.

## Spells

Cast spells in the [combat harness](combat/combat_harness.tscn): **Spells** casts any spell at any level with the hero's weapon hit, and a wand or staff from **Items** casts its bound spell for mana. **Monsters → Count** places several targets around the hero, **Level → Walls** switches the arena's wall arrangement (Default, One wall, Open or Small room, `DebugArrangement` resources on the shared `debug_stage.tscn`; the arena starts open), and **Level → Dark level** darkens it as a dark floor; equip a torch or lamp to open up the view. The arena rebuilds monster navigation for each arrangement. Spell scenes compose `VisualEffectTimeline` with focused motion components whose exports and materials tune in the Inspector; the walls show how [area visuals clip](../../docs/weapon-attack-system.md#elemental-spells) around corners and through doorways.
