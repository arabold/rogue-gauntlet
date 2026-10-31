# Level design

Implemented generation, room authoring, encounters and visibility. [Dungeon layout, access and discovery](dungeon-exploration-design.md) owns intended realm pools, recipes, locks and secrets; [door access](door-access.md) describes implemented locks/secrets and the [progression plan](specs/level-progression-prototype-plan.md) retains future realm recipes and seals.

See the [folder guide](../scenes/levels/README.md) for code and preset locations, and the [room generation harness](../scenes/debug/README.md#room-generation) for single-room and full-floor previews.

## Current generation

Gameplay uses the [Garrison factory preset](../scenes/levels/garrison/factories/garrison_mixed_room_factory.tres): ordinary and special slots select assembled hall, storage, mess hall, barracks, library, alchemist and passage room types, plus the legacy fenced-walkway, partitioned-storage and prison recipes. Entrances and exits remain authored. Caves, sewers and other templates remain available in the general development factory.

This is a curated first-realm preset, currently shared by all depths, rather than a realm/depth resolver. Per-depth room roles and required routes remain proposed. In the builder Inspector, **RandomPurposeMask** limits random purposes; a fixed **Purpose** overrides it. Empty/unknown random masks fail before replacing existing geometry. The generic room builder remains available for other presets. The editor’s **Coded Generator Preset** selects a room recipe; the arrangement playground uses the gameplay factory.

Generation packs and rotates rooms around an entrance, prefers a geometrically distant exit, connects rooms and explicit doorways, resolves walls/doors, reserves optional secret pockets, furnishes procedural rooms, decides special encounters, assigns validated lock/key pairs, then places other loot and bakes navigation. Ordinary encounters use the completed geometry. Ordinary connectivity and a distant exit do not guarantee a mandatory route or encounter order.

Generation hands gameplay a `GeneratedFloor` (`Level.Floor`): the layout, rooms, doors, arrival, descent and encounter plan, plus every spatial query gameplay asks (room lookup, footprint clearance, free spawn and landing positions). The generator runs its own placement checks against the same instance, and freezes it at handoff. Components read seed, depth, floor, run and exploration from their `Level`, never from the generator or the session.

Generation settings and factories are assigned in [level.tscn](../scenes/levels/level.tscn). [Randomness and seeds](randomness-and-seeds.md) owns reproducibility and saved differences. Normal map dimensions are unchanged by the arrangement playground.

## First-realm encounter prototype

The first three depths use the [Sunken Garrison encounter resource](../scenes/levels/encounters/sunken_garrison.tres). Weighted, depth-bounded entries spend authored threat budgets; clearance may reduce the number actually placed. The factory's encounter plan substitutes nests and the gatekeeper for ordinary slots, so budgets alone do not measure final floor difficulty. [Monster definitions](monster-catalog/README.md) supply fixed strength and rewards. Other depths use an explicit development fallback, not finished realm content.

- Depth 1 introduces ordinary skeleton encounters.
- Depth 2 adds rats and finite nests. Nests activate in revealed nearby space, limit simultaneous summons and total slots, and never refill killed slots. Destroying a nest collapses bound survivors without XP or loot.
- Depth 3 adds the Forsaken Warden. Defeat unlocks downward travel through an objective check. Visible seals, boss phases and guaranteed reward-room topology are not implemented.

A room may provide a `GatekeeperSpawnMarker` to control boss position and facing after rotation. Author one intended anchor and ensure its physical clearance. Otherwise generation picks the roomiest supported location nearest the descent, and retries a bounded number of deterministic layouts; failure returns safely instead of loading an invalid floor. The site is decided once, before locks and loot: locks never seal its room, loot keeps clear of its minimum footprint, and the gatekeeper takes the first encounter slot there. Large-actor movement retains its clearance constraints, and tall enemies stay hidden until their tile is discovered.

Cleared enemies, destroyed nests, killed summon slots and Warden defeat persist across revisits. Living actors reset to their generated initial state; partial health, position and summon timers are not saved. See [monster authoring](monster-authoring-design.md) for tactics and the [roadmap](development-roadmap.md#first-three-floor-acceptance) for playtest acceptance.

## Room template creation

Author a self-contained `Room` scene with explicitly assigned floor, wall and decoration GridMaps. Use the shared mesh libraries and include relevant prop collision. Complete authored rooms bring their own content; generic gameplay furnishing/loot does not decorate their interior automatically.

GridMap cells are one world unit with centering disabled. Logical map tiles are **4×4 units centered on their anchor**, even when floor meshes are smaller or larger. Keep tiles aligned to those centers; the room's bounds and rotation derive from geometry, not a manually edited map copy.

Add `DoorwayMarker` nodes for intentional connection points, facing outward. With explicit markers, other edges are sealed; without them, open edges are optional connectors, suitable for caves. A real `Door` makes an opening gated; an opening without one is an archway. Unconnected doors are removed when walls are resolved. Do not infer an opening from a decorative door mesh alone.

The generator rotates complete rooms in quarter-turns, including GridMaps, props and markers. Verify the scene after rotation, especially doorway facing, collider alignment and boss anchors. Keep spaces large enough for the actors and encounters intended to use them.

## Procedural rooms

`ProceduralRoomBuilder` is an Inspector-authored Resource selecting footprint shape, surface materials, architectural purpose, wear, columns, openings and optional chasms. These produce structural variety; reusable furnishings supply small deliberate compositions. Runtime gameplay builds shells before corridor resolution. Standalone editor generation can add arrangements and save an editable room scene.

The room retains its resolved purpose for arrangement eligibility. A saved authored room is a deliberate content choice; ordinary room variety should not depend on stockpiling frozen copies of generated shells. Use the [arrangement authoring guide](../scenes/levels/arrangements/README.md) for the existing generator controls and preview workflow.

### Room assembly

Garrison rooms are assembled with their content by a `RoomType` ([room types](../scenes/levels/garrison/room_types/)): one strategy, or weighted palette of strategies, per axis. `RoomAssembler` runs them in a fixed order — shape → doorways → walls → circulation → columns → furnishing steps → floor → props — each axis on its own derived stream, so changing a floor style or toggling columns never moves furniture. Rooms are finished before floor packing and rotate as a whole; gameplay must connect every doorway, as before. A required step that cannot fit retries with a new shape (`Attempts`); a type that never fits throws instead of delivering a different room. The [plan](specs/room-assembly-rebuild-plan.md) tracks the remaining ports.

| Axis | Strategies |
| --- | --- |
| Shape and size | `RectangleShape` (optional odd width), `LShape`, `TShape`, `UShape`, `CrossShape`; width across, length along the room axis (+Z) |
| Doorways | `MinDoorways`/`MaxDoorways`, `DoorwayPlacement` (any, long-axis ends, long sides), optional door scene and chance |
| Circulation | Doorway approaches or a door-to-door spine kept free of furniture |
| Walls | `WallStyle`: weighted straight pieces, corner/half pieces, a rhythm piece every N modules (pillared), accents (built-in shelves); asymmetric pieces face the room |
| Floor | `UniformFloor`, `BorderedFloor`, `PatchedFloor` over `FloorMaterial`s (weighted slabs, quarter pieces and wear accents) |
| Columns | `AisleColumns`, `PerimeterColumns`, `CenterColumns`, `CornerColumns`, behind `ColumnChance`; symmetric groups place all-or-nothing |
| Furnishing | `AlongWallsStep`, `CornersStep`, `RowsStep`, `CenterStep`, `ScatterStep`, `WallDecorStep`; a step with `MinimumCount` is required |

Steps place `Placeable`s. A `Prop` is one object: mesh-library items (cosmetic variants) or scenes for props that need nodes, measured from the model. Its front faces local +Z; back-to-wall props stand with -Z on the wall face, wall props hang on the mounting surface at `MountHeight`. A `PropGroup` is an anchor plus optional members at offsets (a bed with nightstand and chest, a barrel with a side table); `SeatedTable` spaces seats along a measured table. Every piece is validated on its own, so a member that does not fit is dropped. Weighted picks prefer content and variants not yet used in the room.

`RoomBlueprint` checks every piece for floor support, wall and prop overlap (with each prop's `Gap`) and circulation, and keeps all doorways mutually reachable on a 0.5-unit `WalkabilityGrid` sized for the navigation agent. Inaccessible pockets behind furniture are allowed up to the type's `MaxPocketShare` of standable floor (default 10%). Interactive props reserve the space in front of them. Assembled props are plain nodes under the room's `Props` (mesh plus static collision on the prop layer), so navigation, exploration and spawn checks treat them like arrangements. The generic furnishing pass skips assembled rooms.

The remaining legacy coded recipes — prison, fenced walkway and partitioned store — still use `ModularRoomGenerator`/`PrisonRoomGenerator`, required furnishing slots and the arrangement pools until their port; their cell gates are described under [interior door access](door-access.md#interior-cell-gates).

### Light flicker

Every flickering light shares one Inspector-tunable `LightFlicker` resource that turns noise into a brightness multiplier. Presets live in `scenes/effects/light_flicker/`: `torch` for wall torches, candles and the player's light, `flame` for fire spells and `electric` for lightning strobes. Each light starts its flicker at its own random offset, so the torches spawned with a floor never flicker in step. Standalone lights are `FlickerLight` nodes, which flicker around their `Energy` only while visible; lights whose energy a spell effect already drives apply the flicker through `EffectLight`.

## Doors and enemy navigation

Closed exterior doors block physics, exploration sight and navigation. Barred interior cell gates block traversal while allowing exploration into the cell. Open doors enable their navigation connection; enemies commit to crossing rather than repeatedly steering between sides. Reachability and actual movement use the same door-aware navigation state. [Monster authoring](monster-authoring-design.md#awareness-and-pursuit) owns detection and pursuit rules.

## Map occlusion and fog of war

Permanent black caps hide void under the rotating isometric camera. Gameplay exploration uses bounded omnidirectional line of sight from the player, independent of room-entry events, facing, lighting and camera zoom. Walls, unsanctioned room/corridor edges and closed door thresholds block sight; open doorways reveal only their visible wedge. Exact diagonal corner crossings require both routes to be open. Furnishings do not block discovery in this first implementation. Editor previews expose generated space.

Discovery records an eight-by-eight sample grid per tile. Newly seen samples stay explored permanently, including across travel and save/load; unseen parts of a partially explored tile stay covered. A depth-based screen shader maps rendered surfaces back to this world-aligned history, feathers the boundary inward and fades newly discovered space into view. Feathering does not change authoritative discovery. There is no dimmed remembered-space state or current-line-of-sight requirement for already discovered content.

Exploration belongs to the level's `FloorDiscovery` node ([floor_discovery.tscn](../scenes/levels/floor_discovery.tscn), wired to `Level.Discovery`). `SightRadius` controls reach in world units (default 24), `FadeSeconds` how long newly seen space fades in (default 0.35 seconds) and `FeatherWidth` how far the fog feathers inward (default 1 world unit). Each floor owns its exploration texture and publishes it with the map bounds and fade clock as `fog_*` global shader uniforms ([fog_of_war.gdshaderinc](../scenes/levels/fog_of_war.gdshaderinc)), so the fog and the silhouettes above it share one coverage test. Sight updates at most ten times per second while moving, and refreshes after door changes; a closed door seals its threshold because sight reads the door itself. The fade clock stops once a reveal has faded in. Exploration is independent of normal/dark-floor illumination.

Content gates retain tile-based discovery: discovering any sample on a tile enables its loot, effects and eligible x-ray silhouettes. The existing `ExplorationGateComponent` hides torch visuals, particles and lights until that tile is discovered. Previously discovered loot does not disappear when the player looks or moves away. The debug fog reveal fills the interior history, so gated loot, traps and effects appear with it; void remains capped.

Closed-door x-ray silhouettes appear only while the door is hidden (occluded, or in view on still-unexplored space; the fog's soft fringe over a door's explored face does not count), nearby and adjacent to revealed space. Unopened treasure chests use a golden occlusion silhouette on discovered tiles, controlled by the loot X-ray toggle; opening a chest or restoring it as opened removes the highlight. Both silhouettes draw above the fog, so walls still under fog do not hide them; their discovery gates keep them from revealing unexplored space. [Door access](door-access.md) adds concealed wall entrances, locks and colored key markers. Concealed doors expose no silhouette/prompt/marker; discovering a secret reveals a closed door, and opening it permits ordinary sight checks. Door access and explored samples persist independently; `Level.CaptureExploration` owns doors, traps and samples together. `SaveGame.CurrentVersion` rejects earlier development saves; no conversion is attempted.

## Reusable room arrangements

Ordinary rooms combine generated layouts with small authored arrangements: a table and chairs, a barrel cluster, or other reusable furnishings. Complete authored rooms supply landmarks and deliberate gameplay spaces. Standard-room selection currently favors procedural rooms; entrances, exits and special rooms remain authored.

Arrangement definitions select an editable scene, eligible room purposes, selection weight, repeat limit and wall/interior placement preferences. Pools separately bound floor furnishing density and wall mount counts. Compositions rotate together in quarter-turns. The general garrison arrangement pool remains available for generic rooms and contains broken and feasting banquet tables, barrel and stacked crate caches, a dining table with chairs, rubble mounds, mounted torches, banners, and wall-mounted heraldic shields; dining tables exclude sewer-purpose rooms. Mounted torches and banners demonstrate full scenes, including local light and particles.

**Every active opening must remain accessible, and nothing is placed where actors cannot reach.** Generic arrangement placement still rejects isolated pockets. After rooms, corridors, walls and secret pockets exist, the floor builds one `WalkabilityGrid` from the real collision (convex outlines, so corner walls keep corners walkable) and flood-fills it from the arrival with every door open. Enemy spawns, chests, traps, loose loot, keys, teleport targets and drop landings only use reachable cells; a chest is rejected if it would cut off any reachable space. Large actors additionally need reachable cells across their footprint. Actual movement, especially for large actors, still needs playtesting with the baked navmesh.

Gameplay furnishes procedural rooms after their walls and openings are resolved, before navigation baking. Furniture is reproducible from the level seed without consuming layout, encounter or loot randomness. Authored rooms keep their manually composed content. The built-in room generator can also furnish and save editable room scenes through the same placement policy.

See the [arrangement authoring guide](../scenes/levels/arrangements/README.md) for Inspector settings, editor commands and the review playground. Each scene explicitly binds a Content hierarchy and a static placement envelope. Enabled solid collision determines walkable space; light and particle extents do not reserve floor space. Wall compositions require a real supporting span at their configured height and rotate together. Corridor placement patterns remain [future work](development-roadmap.md#room-variety-and-furnishing).
