# Shared debug toolkit

Status: implemented. The in-game debug menu hosts one `DebugTool` per capability (items, spells, monsters, hero, combat display, level), and every debug action is a typed method that menu clicks, harness scenarios and tests call alike. The combat harness is a `DebugArena` that hosts the same menu, its scenarios name resources by path, hero builds apply from presets, and the item lifecycle harness and the four elemental spell previews are retired. See the [debug menu guide](../../scenes/debug/README.md#in-game-debug-menu) and the [combat harness guide](../combat-harness.md).

This document keeps only what remains open.

## Deferred

- **Class switching.** A hero build applies its class's stat profile and attributes in place and keeps the hero's model. Swapping the model needs a respawn, because `Player._EnterTree` applies the class and the model swap only works before `_Ready`: a new player node with a `Player.CharacterClass` export, then every holder of the old player rebinding on `PlayerSpawned` (camera, HUD, debug menu, harness, spawned monsters' targets). Nothing needs it yet.
- **Capturing builds.** "Save the current hero as a build" needs fixed affixes on `HeroBuildItem`; wait until a bug report needs an exact item.

## Open decisions

- **Screen-effect harness:** fold it into a tool and lose its intensity sliders, or keep it.
- **Debug menu ergonomics in the arena:** a popup menu takes more clicks than always-visible pickers. If spawning and equipping prove slow, add a "recent" section to `ItemSpawnTool` and `MonsterTool` rather than a second set of controls.
