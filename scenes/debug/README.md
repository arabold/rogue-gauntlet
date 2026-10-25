# Debug harnesses

Open the scene in Godot and run it with **F6**. Each feature folder keeps its scene, scripts and supporting fixtures together. Harnesses author their own staging; the generic standalone-scene bootstrap skips this directory.

| Harness | Entry point | Purpose |
| --- | --- | --- |
| Room generation | [rooms/room_generation_harness.tscn](rooms/room_generation_harness.tscn) | Inspect coded recipes alone or in the production Garrison floor |
| Combat | [combat/combat_harness.tscn](combat/combat_harness.tscn) | Interactive combat, monster tactics and scripted traces; [guide](../../docs/combat-harness.md) |
| Item lifecycle | [items/item_lifecycle_harness.tscn](items/item_lifecycle_harness.tscn) | Item creation, inventory, equipment and consumables |
| World effects | [effects/world_effect_harness.tscn](effects/world_effect_harness.tscn) | Replay and tune production trap/secret discovery, loot glow, blood hit, actor invisibility and prop x-rays |
| Screen effects | [effects/screen_effect_harness.tscn](effects/screen_effect_harness.tscn) | Exercise overlays in gameplay; [guide](../../docs/screen-effects.md) |

## Room generation

The scene explicitly assigns the production Garrison factory, map generator, preview root, camera and UI. Its recipe list comes from the factory's Inspector-authored `RoomGenerators`; it does not maintain a second catalog or generation algorithm.

- **Single room:** select a recipe and seed, rotate in quarter-turns and toggle furnishings. The harness builds the production shell with walls, rotates it, then runs the production furnishing pass. The camera centers and frames the result automatically.
- **Generated floor:** uses the production packing, corridor, wall and furnishing pipeline with the same factory. This checks that required room entrances and furnishings survive placement among other rooms. Recipe selection and room rotation are disabled in this view.
- **Controls:** R advances the seed, A toggles furnishings, F switches views, 1–5 selects a recipe, Q/E rotates a room. The panel also accepts a manual seed and can rebuild without advancing it.
- Generation errors appear in the status panel and Godot output. Optional furnishings can be omitted; required slots must all fit. These static previews do not exercise player movement or navmesh baking—use gameplay for those checks.

[Level folder guide](../levels/README.md) identifies where to change generator code, room presets and furnishing assets. Regenerate after editing a preset; the shared Resource remains the authored definition.

## World effects

Run `effects/world_effect_harness.tscn` with **F6**. It stages the actual gameplay trap, secret door, loot beam, blood particles, animated actor invisibility and prop x-rays, without dungeon generation.

- **1–7:** select trap discovery, secret-door discovery, loot glow, blood hit, invisibility, door x-ray or chest x-ray.
- Discovery starts concealed and stays that way until **Reveal / R / Space**. Selecting an effect or changing its options resets to the original tile. **Auto replay** is opt-in and holds the concealed surface for one second before each reveal. Blood uses **Replay / R / Space**; loot runs continuously.
- **Q / E:** orbit the camera; **mouse wheel:** zoom.
- The panel shows only relevant options: glow strength/blending for traps and secrets and wall variants for secrets and x-ray occluders. Discovery uses the production 0.6-second duration and yellow color; speed and tint controls are omitted. Blood only shows playback controls. These controls change the preview only; apply chosen defaults to the production effect scene/profile in the Inspector.

Discovery previews copy the actual selected floor/wall mesh and fade it into the discovered surface. Once the transition finishes, glow and temporary geometry disappear. Traps retain their floor collider and dangerous trigger; revealed doors remain closed. Loot and blood use their production implementations and lifetimes. The separate screen-effect harness exercises fullscreen overlays in gameplay.

Invisibility stages the production player and skeleton with idle animation and equipped gear, with combat/input disabled. **R / Space** fades both between normal and invisible; **Auto replay** alternates every three seconds. The floor remains visible through their faint, softly shimmering forms. Orbit and zoom to compare edges at different angles. It uses the shared [actor invisibility component](../../docs/invisibility.md); this preview does not exercise AI stealth or detection.

**6: Door x-ray** and **7: Chest x-ray** stage closed production props behind ordinary opaque walls. **R / Space** hides/shows the wall; **Auto replay** toggles it every three seconds. Orbit to inspect full and partial occlusion. The wall-style selector changes the blocking wall. The previews honor the game's door/loot x-ray settings and never change those global toggles; the status explains if the relevant setting is off. These previews do not discover secrets or open/loot props.
