# Debug harnesses

Open the scene in Godot and run it with **F6**. Each feature folder keeps its scene, scripts and supporting fixtures together. Harnesses author their own staging; the generic standalone-scene bootstrap skips this directory.

| Harness | Entry point | Purpose |
| --- | --- | --- |
| Room generation | [rooms/room_generation_harness.tscn](rooms/room_generation_harness.tscn) | Inspect coded recipes alone or in the production Garrison floor |
| Combat | [combat/combat_harness.tscn](combat/combat_harness.tscn) | Interactive combat, monster tactics and scripted traces; [guide](../../docs/combat-harness.md) |
| Item lifecycle | [items/item_lifecycle_harness.tscn](items/item_lifecycle_harness.tscn) | Item creation, inventory, equipment and consumables |
| World effects | [effects/world_effect_harness.tscn](effects/world_effect_harness.tscn) | Replay and tune production trap/secret discovery, loot glow, blood hit and prop x-rays |
| Actor/item effects | [effects/actor_item_effect_preview.tscn](effects/actor_item_effect_preview.tscn) | Inspect one effect/subject, skin finishes, teleport fades and fitted curse wisps; [guide](../../docs/world-effects.md) |
| Scroll reading | [effects/runes/scroll_rune_study.tscn](effects/runes/scroll_rune_study.tscn) | Preview the production ring with its assigned scroll appearance; [guide](../../docs/world-effects.md#scroll-reading-preview) |
| Screen effects | [effects/screen_effect_harness.tscn](effects/screen_effect_harness.tscn) | Exercise overlays in gameplay; [guide](../../docs/screen-effects.md) |

## Room generation

The scene explicitly assigns the production Garrison factory, map generator, preview root, camera and UI. Its recipe list comes from the factory's Inspector-authored `RoomGenerators`; it does not maintain a second catalog or generation algorithm.

- **Single room:** select a recipe and seed, rotate in quarter-turns and toggle furnishings. The harness builds the production shell with walls, rotates it, then runs the production furnishing pass. The camera centers and frames the result automatically.
- **Generated floor:** uses the production packing, corridor, wall and furnishing pipeline with the same factory. This checks that required room entrances and furnishings survive placement among other rooms. Recipe selection and room rotation are disabled in this view.
- **Controls:** R advances the seed, A toggles furnishings, F switches views, 1–5 selects a recipe, Q/E rotates a room. The panel also accepts a manual seed and can rebuild without advancing it.
- Generation errors appear in the status panel and Godot output. Optional furnishings can be omitted; required slots must all fit. These static previews do not exercise player movement or navmesh baking—use gameplay for those checks.

[Level folder guide](../levels/README.md) identifies where to change generator code, room presets and furnishing assets. Regenerate after editing a preset; the shared Resource remains the authored definition.

## World effects

World, actor/item and scroll previews share tabs through `effect_harness_navigation.tscn`. Open any of the three entry scenes with F6; the tabs navigate among them. `EffectPreviewCamera` provides right-drag orbit and wheel zoom, plus the game's existing camera bindings (comma/period and apostrophe/slash by default).

The **World effects** tab stages production trap/secret discovery, loot glow, blood hits and prop x-rays. Select a world effect, then use its relevant reveal/replay or visibility button. Discovery starts concealed; choosing an option resets the original surface. Loot loops continuously. Glow/blending options belong to discovery, and wall styles belong to secret-door/x-ray previews. These are preview-only settings. X-rays honor production behavior and do not alter AI, discovery or global debug toggles.

The **Actor / item effects** tab shows one effect and one subject with replay/clear, actor pose, playback and dark lighting. Subject labels and scenes are authored together in its `Subjects` dictionary. Shared `EffectPreviewStage` and `EffectPreviewControls` helpers prepare actors and build controls for these previews; tabs are the sole navigation path. Replay restarts bursts and sustained effects. **Faceted shading** and **Polished steel** appear only for the applicable skins. **Preview full teleport** appears only for teleport effects on actors and runs the same fade/relocate/fade-in sequence as gameplay. Emissive effects have matching local lights; material-only skins and invisibility do not. Invisibility uses this same actor-effect selection and playback.

The **Scroll reading** tab shows the approved ring and its assigned paper appearance, with replay, slow motion/pause, still-frame inspection and dark lighting. It uses the production renderer and the current run's appearance assignments.

Auto-replay, object comparisons, seed editing, crowds, combined-effect buttons and item-knowledge/source/death controls are removed. Automated tests exercise lifecycle and combinations. The separate [screen-effect harness](../../docs/screen-effects.md) exercises fullscreen overlays in gameplay. A future spell scene can join the same navigation and stage the player plus up to four actual monster targets; see the [remaining VFX plan](../../docs/specs/actor-item-vfx-plan.md).
