# Debug harnesses

Open the scene in Godot and run it with **F6**. Each feature folder keeps its scene, scripts and supporting fixtures together. Harnesses author their own staging; the generic standalone-scene bootstrap skips this directory.

| Harness | Entry point | Purpose |
| --- | --- | --- |
| Room generation | [rooms/room_generation_harness.tscn](rooms/room_generation_harness.tscn) | Inspect room types alone or in the production Garrison floor |
| Combat | [combat/combat_harness.tscn](combat/combat_harness.tscn) | Interactive combat, monster tactics and scripted traces; [guide](../../docs/combat-harness.md) |
| Item lifecycle | [items/item_lifecycle_harness.tscn](items/item_lifecycle_harness.tscn) | Item creation, inventory, equipment and consumables |
| World effects | [effects/world_effect_harness.tscn](effects/world_effect_harness.tscn) | Replay and tune production trap/secret discovery, loot glow, blood hit and prop x-rays |
| Actor/item effects | [effects/actor_item_effect_preview.tscn](effects/actor_item_effect_preview.tscn) | Inspect one effect/subject, skin finishes, teleport fades and fitted curse wisps; [guide](../../docs/world-effects.md) |
| Scroll reading | [effects/runes/scroll_rune_study.tscn](effects/runes/scroll_rune_study.tscn) | Preview the production ring with its assigned scroll appearance; [guide](../../docs/world-effects.md#scroll-reading-preview) |
| Fire spells | [effects/fireball_preview.tscn](effects/fireball_preview.tscn) | Preview five fire spells at three levels with 0–5 targets, guard reflection and Fireball wall impact |
| Earth spells | [effects/earth_spell_preview.tscn](effects/earth_spell_preview.tscn) | Preview five earth spells at three levels, wall dust, camera shake and guard reflections |
| Air spells | [effects/air_spell_preview.tscn](effects/air_spell_preview.tscn) | Preview Ball Lightning, Chain Lightning, Gust, Thunderstorm and Storm Guard at three levels |
| Water spells | [effects/water_spell_preview.tscn](effects/water_spell_preview.tscn) | Preview Ice Lance, Frost Nova, Frost Guard, Hailstorm and Ice Wall at three levels |
| Screen effects | [effects/screen_effect_harness.tscn](effects/screen_effect_harness.tscn) | Exercise overlays in gameplay; [guide](../../docs/screen-effects.md) |

## Room generation

The scene explicitly assigns the production Garrison factory, map generator, preview root, camera and UI. Its recipe list is the factory's `RoomTypes`; it keeps no catalog of its own.

- **Single room:** select a recipe and seed, rotate in quarter-turns and toggle furnishings. Room types expose every axis as an override — Shape, Walls, Floor and Columns (type chance, off, or a specific pattern) — and run the production `RoomAssembler` with them. The **Walkability overlay** colours standable floor: green is reachable from the doorways, blue is reserved circulation, red is an inaccessible pocket. Overrides never change production Resources.
- **Generated floor:** uses the production packing, corridor, wall and furnishing pipeline with the same factory. Recipe selection and room rotation are disabled in this view.
- **Controls:** R advances the seed, A toggles furnishings, W toggles the walkability overlay, F switches views, 1–9 and 0 select the first ten room types, Q/E rotates a room. The panel also accepts a manual seed and can rebuild without advancing it.
- Generation errors appear in the status panel and Godot output. These static previews do not exercise player movement or navmesh baking—use gameplay for those checks.

[Level folder guide](../levels/README.md) identifies where to change generator code, room presets and furnishing assets. Regenerate after editing a preset; the shared Resource remains the authored definition.

## World effects

World, actor/item and scroll previews share tabs through `effect_harness_navigation.tscn`. Open any of the three entry scenes with F6; the tabs navigate among them. `EffectPreviewCamera` provides right-drag orbit and wheel zoom, plus the game's existing camera bindings (comma/period and apostrophe/slash by default).

The **World effects** tab stages production trap/secret discovery, loot glow, blood hits and prop x-rays. Select a world effect, then use its relevant reveal/replay or visibility button. Discovery starts concealed; choosing an option resets the original surface. Loot loops continuously. Glow/blending options belong to discovery, and wall styles belong to secret-door/x-ray previews. These are preview-only settings. X-rays honor production behavior and do not alter AI, discovery or global debug toggles.

The **Actor / item effects** tab shows one effect and one subject with replay/clear, actor pose, playback and dark lighting. Subject labels and scenes are authored together in its `Subjects` dictionary. Shared `EffectPreviewStage` and `EffectPreviewControls` helpers prepare actors and build controls for these previews; tabs are the sole navigation path. Replay restarts bursts and sustained effects. **Faceted shading** and **Polished steel** appear only for the applicable skins. **Preview full teleport** appears only for teleport effects on actors and runs the same fade/relocate/fade-in sequence as gameplay. Emissive effects have matching local lights; material-only skins and invisibility do not. Invisibility and Frozen use this same actor-effect selection and playback. Frozen changes the appearance only in this workbench; pose pausing is demonstrated by Frost Nova.

The **Scroll reading** tab shows the approved ring and its assigned paper appearance, with replay, slow motion/pause, still-frame inspection and dark lighting. It uses the production renderer and the current run's appearance assignments.

Auto-replay, object comparisons, seed editing, crowds, combined-effect buttons and item-knowledge/source/death controls are removed. Automated tests exercise lifecycle and combinations. The separate [screen-effect harness](../../docs/screen-effects.md) exercises fullscreen overlays in gameplay. The Fire spells tab previews Fireball, Flame Burst, Ember Guard, Solar Flares and Meteor Shower at three levels, using stationary target fixtures. Flame Burst sends a ring outward; a flame serpent circles the caster for Solar Flares. Ember Guard persists until cleared or its simulated shield capacity is depleted by preview reflections. Fireball uses a damage fixture; the other spell effects are visual-only. Learning, mana, moving targets and production gameplay integration remain future work; see the [spell plan](../../docs/specs/elemental-spells-plan.md).

The Earth spells tab previews Rockfall, Tremor, Rock Spikes, Stone Barrier and Spiked Armor. Tremor triggers a brief camera shake and dust from the workbench wall; falling rocks and emerging spikes also kick up dust. Temporary cracks spread outward beneath the spikes and appear at Rockfall impacts, which scatter fading fragments. Rockfall and Meteor Shower cycle through visible targets and may repeat them, with extra smaller cosmetic impacts. Guards persist until cleared or depleted by simulated hits; reflected shards and all other effects are cosmetic. Wall discovery, camera integration and combat behavior are not connected to gameplay.

The Air spells tab previews traveling lightning orbs, chained lightning, an expanding Gust, Thunderstorm with glowing strike cracks, and a sustained Storm Guard with cosmetic outward arcs and simulated reflection. Unused targeted slots can create cosmetic flights or arcs. The air effects do not apply damage, push enemies or implement guard mechanics.

The Water spells tab previews Ice Lance, Frost Nova, Frost Guard, Hailstorm and Ice Wall; Hailstorm rains cosmetic shards on its gameplay landing pattern. Frost Nova temporarily freezes the appearance and pose of visible preview targets, restoring both afterward; gameplay freeze, damage and guard behavior are not connected.

Each spell tab uses the shared `debug_stage.tscn` and offers Default, One wall, Open and Small room arrangements, plus an independent target-count selector. The default preserves the original wall opening. Arrangements are authored as `DebugArrangement` resources on the stage; their wall indices select the explicitly wired fixtures, and boundary indices choose where Tremor emits wall dust.

The spell scenes compose `VisualEffectTimeline` with focused motion components. Falling meteors and rocks share `FallingBodyMotion`; fire ribbons and guards use their own components, while ice crystals read the shared timeline. Solar Flares instances a reusable orbit scene, and Frost Guard uses a crystalline guard scene. Tune component exports and scene materials in the Inspector.

Area visuals in the spell tabs capture a sampled line-of-sight boundary at cast time. The KayKit walls include an opening for inspecting clipping around corners and through doorways. Moving blockers and height differences require further work before gameplay integration; see [area boundaries](../../docs/specs/elemental-spells-plan.md#targeting-area-and-guard-rules).
