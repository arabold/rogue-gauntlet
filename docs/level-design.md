# Level design

Implemented generation, room authoring, encounters and visibility. [Dungeon layout, access and discovery](dungeon-exploration-design.md) owns intended realm pools, recipes, locks and secrets; the [remaining plan](specs/level-progression-prototype-plan.md) describes that future work.

See the [folder guide](../scenes/levels/README.md) for code and preset locations, and the [room generation harness](../scenes/debug/README.md#room-generation) for single-room and full-floor previews.

## Current generation

Gameplay uses the [Garrison factory preset](../scenes/levels/garrison/factories/garrison_mixed_room_factory.tres): ordinary rooms use coded hall, cellar, mess hall, barracks and library generator presets with a stone-and-wood palette. Halls have paired columns and wall fixtures; cellars repeat storage arrangements around a clear center. Barracks and storage are optional authored landmarks, each selected at most once per generation; exhausted landmark slots become ordinary rooms. Entrances and exits remain authored. Caves, sewers and other templates remain available in the general development factory.

This is a curated first-realm preset, currently shared by all depths, rather than a realm/depth resolver. Per-depth room roles and required routes remain proposed. In the builder Inspector, **RandomPurposeMask** limits random purposes; a fixed **Purpose** overrides it. Empty/unknown random masks fail before replacing existing geometry. The generic room builder remains available for other presets. The editor’s **Coded Generator Preset** selects a room recipe; the arrangement playground uses the gameplay factory.

Generation packs and rotates rooms around an entrance, prefers a geometrically distant exit, connects rooms and explicit doorways, resolves walls/doors, furnishes procedural rooms, then places loot and bakes navigation. Encounters use the completed geometry. Ordinary connectivity and a distant exit do not guarantee a mandatory route or encounter order.

Generation settings and factories are assigned in [level.tscn](../scenes/levels/level.tscn). [Randomness and seeds](randomness-and-seeds.md) owns reproducibility and saved differences. Normal map dimensions are unchanged by the arrangement playground.

## First-realm encounter prototype

The first three depths use the [Sunken Garrison encounter resource](../scenes/levels/encounters/sunken_garrison.tres). Weighted, depth-bounded entries spend authored threat budgets; clearance may reduce the number actually placed. Nest and boss substitutions happen afterward, so budgets alone do not measure final floor difficulty. [Monster definitions](monster-catalog/README.md) supply fixed strength and rewards. Other depths use an explicit development fallback, not finished realm content.

- Depth 1 introduces ordinary skeleton encounters.
- Depth 2 adds rats and finite nests. Nests activate in revealed nearby space, limit simultaneous summons and total slots, and never refill killed slots. Destroying a nest collapses bound survivors without XP or loot.
- Depth 3 adds the Forsaken Warden. Defeat unlocks downward travel through an objective check. Visible seals, boss phases and guaranteed reward-room topology are not implemented.

A room may provide a `GatekeeperSpawnMarker` to control boss position and facing after rotation. Author one intended anchor and ensure its physical clearance. Otherwise generation searches for a roomy supported location and retries a bounded number of deterministic layouts; failure returns safely instead of loading an invalid floor. Large-actor movement retains its clearance constraints, and tall enemies stay hidden until their room is revealed.

Cleared enemies, destroyed nests, killed summon slots and Warden defeat persist across revisits. Living actors reset to their generated initial state; partial health, position and summon timers are not saved. See [monster authoring](monster-authoring-design.md) for tactics and the [roadmap](development-roadmap.md#first-three-floor-acceptance) for playtest acceptance.

## Room template creation

Author a self-contained `Room` scene with explicitly assigned floor, wall and decoration GridMaps. Use the shared mesh libraries and include relevant prop collision. Complete authored rooms bring their own content; generic gameplay furnishing/loot does not decorate their interior automatically.

GridMap cells are one world unit with centering disabled. Logical map tiles are **4×4 units centered on their anchor**, even when floor meshes are smaller or larger. Keep tiles aligned to those centers; the room's bounds and rotation derive from geometry, not a manually edited map copy.

Add `DoorwayMarker` nodes for intentional connection points, facing outward. With explicit markers, other edges are sealed; without them, open edges are optional connectors, suitable for caves. A real `Door` makes an opening gated; an opening without one is an archway. Unconnected doors are removed when walls are resolved. Do not infer an opening from a decorative door mesh alone.

The generator rotates complete rooms in quarter-turns, including GridMaps, props and markers. Verify the scene after rotation, especially doorway facing, collider alignment and boss anchors. Keep spaces large enough for the actors and encounters intended to use them.

## Procedural rooms

`ProceduralRoomBuilder` is an Inspector-authored Resource selecting footprint shape, surface materials, architectural purpose, wear, columns, openings and optional chasms. These produce structural variety; reusable furnishings supply small deliberate compositions. Runtime gameplay builds shells before corridor resolution. Standalone editor generation can add arrangements and save an editable room scene.

The room retains its resolved purpose for arrangement eligibility. A saved authored room is a deliberate content choice; ordinary room variety should not depend on stockpiling frozen copies of generated shells. Use the [arrangement authoring guide](../scenes/levels/arrangements/README.md) for the existing generator controls and preview workflow.

### Coded room generators

`RoomGenerator` is a Resource base for room-specific C# algorithms. Each `.tres` instance is a recipe: it assigns an explicit `ShellBuilder` and type-specific assets/settings. `MixedRoomFactory.RoomGenerators` selects recipes by their `Weight`; an empty list uses the existing generic `RoomBuilder`. Entrances, exits and authored landmarks retain their existing factory path.

Generators choose required entrances together with their structural layout, before shared floor and wall construction. Generation builds structure first, places/rotates rooms and resolves corridors, then applies each generated room's interior pool using its actual connected entrances. The dungeon-wide arrangement pool is used only for generic procedural rooms. `HallRoomGenerator` owns its dimensions and mirrored column placement: `WidthTiles` is odd, `LengthTiles` lists compatible odd lengths, and the aisle always has an opening at both ends. `SideOpeningChance` controls extra midpoint openings on the side walls; the shell’s generic opening-count settings do not control halls. Its primary decoration pool accepts wall fixtures; `ColumnItems` restricts the allowed column meshes and selects one consistent style per hall. The generic shell builder also honors its configured column palette for every purpose. `CellarRoomGenerator` exposes a `StorageArrangements` palette, cluster/density limits and passage radius. Its `PerimeterStorageStrategy` restricts storage to the perimeter while retaining the shared collision, support and connected-free-space checks. Counts are upper bounds: optional placements that cannot fit are omitted.

The furniture-led concepts additionally place required `RoomFurnishingSlot` child anchors while building structure. Their transforms rotate with the room and resolve into master-map coordinates after placement. `SlotArrangementStrategy` uses those exact anchors and facing (including small offsets for wall trim) through the common collision and passage checks. Every required slot must fit; a failure reports an invalid composition instead of dropping identity-defining furniture. Generic rerolls remove obsolete slots, while saved authored scenes retain their editable furniture and anchors.

| Generator | Composition and sizing rule |
| --- | --- |
| `DiningRoomGenerator` | One to four banquet rows; room length grows by two tiles per row, with end entrances and circulation around the tables. Assign a centered `TableArrangement` with a three-tile footprint and eligible `TableRows`. |
| `BarracksRoomGenerator` | One to four opposing bed pairs face a central passage; three- or five-tile width, length determined by pair count, entrances at both ends. Assign `BedArrangement`, `WidthTiles` and eligible `BedPairs`. |
| `LibraryRoomGenerator` | Five- or seven-tile chambers combine perimeter bookcases with interior stacks on either side of a central entrance-to-entrance aisle. Larger rooms add stack rows. Assign wall-aligned `WallBookcase`, freestanding back-to-back `StackArrangement`, eligible `Sizes` and `StackRowSpacing`. Closely spaced rows retain actor-width aisles. |

Each recipe may assign a `RoomDressingProfile`. Required furniture is placed first, followed by intentional wall lighting and a small optional clutter pass. Clutter palettes suit the room: provisions in dining rooms and halls, footlockers in barracks, archive boxes in libraries; cellars use their larger main storage palette. All passes share wall support, collision and connected-passage checks. Selection uses separate derived RNG streams, so optional dressing does not change the structural layout or required furniture.

`RoomLightingProfile` chooses fixtures beside actual connected doorways, optionally in pairs. Barracks may also place lights at assigned bed heads; halls may line their side walls at a configured spacing. Door, pair and bed chances, mounting height, spacing, count, and the lit/unlit ratio are authored per recipe. Fixtures require explicit lit and unlit definitions; the unlit scene disables both its light and fire particles. Unsafe positions are omitted. Libraries mount fixtures above their wall shelves. The room's explicit `LayoutAxis` rotates with the room so hall-side lighting follows its architectural axis.

The in-game **Debug → Dark level** checkbox sets the current `Level.IsDark` flag. Darkness removes ambient light from the explicitly assigned `LightingEnvironment` and extinguishes mounted torch lights and flames through their authored `EnvironmentalLightComponent`. Turning it off restores the original lit/unlit fixture states and ambient intensity. Player lights and fog-of-war reveal remain unchanged. This is a temporary override for the current level instance; it is not saved or automatically selected during generation. Seeded dark-floor selection remains [planned gameplay](game-design.md#dark-floors-and-portable-lighting-planned).

The player's unaided lights use short ranges and distance falloff to keep dark floors difficult to see across. **Debug → Spawn item → Lamps and torches** offers six usable portable lights. Equip one in either hand through its inventory menu to attach its model, flame and light. Only the held scene emits light; inventory and dropped models stay unlit. See [portable-light equipment](stats-and-itemization.md#portable-light-equipment) for hand conflicts and persistence. Fuel and manual ignition remain deferred.

Dimensions are part of a room algorithm’s contract, rather than arbitrary inputs to repair afterward. A generator can require odd widths for central doors, module-sized increments, or bounds derived from its furniture. The hall is the first implementation; the mess hall also derives its dimensions from table count. Entrance-to-entrance carpet routing remains a future extension.

To add a room type, subclass `RoomGenerator`, supply its `CreateInteriorPool()` policy and validation, and override `PopulateStructure()` when custom architecture is needed. Reuse `ProceduralRoomBuilder` for ordinary shells and subclass `ConnectedArrangementStrategy` to filter/weight candidate positions through `CandidateWeight()` while retaining safety checks. More complex coordinated layouts can implement `RoomArrangementPlacementStrategy.Plan()` directly, but must enforce the same passage and collision invariants. Partitioned prisons and operable internal-cell topology are not implemented by this prototype.

Shared recipe/asset Resources remain definitions; runtime pools duplicate storage definitions before changing repeat limits. Per-room policies travel with placed room regions, use the existing arrangement RNG stream keyed by generation attempt and room ID, and also supply the clearance used for later chest placement. `EnableFurnishings` toggles all interiors without changing shell/layout randomness. Standalone editor generation protects every opening. Gameplay must connect every explicit doorway, including both hall ends; an unrouteable required doorway fails generation before wall placement rather than being sealed. Generic inferred open edges remain optional. Saved generated scenes become authored rooms and are not furnished again. Generation changes invalidate earlier development saves; `SaveGame.CurrentVersion` identifies the supported format.

## Doors and enemy navigation

Closed doors block physics, sight and navigation. Open doors enable their navigation connection; enemies commit to crossing rather than repeatedly steering between sides. Reachability and actual movement use the same door-aware navigation state. [Monster authoring](monster-authoring-design.md#awareness-and-pursuit) owns detection and pursuit rules.

## Map occlusion and fog of war

Black caps hide void and undiscovered space under the rotating isometric camera. Gameplay begins covered; editor previews expose generated space. Entering a room reveals it and connected open corridors, stopping at closed doors. Standing against a closed doorway does not count as entering the room. Reveal respects sanctioned connections rather than leaking through adjacent walls.

Closed-door x-ray silhouettes appear only when occluded, nearby and adjacent to revealed space, so they do not disclose unexplored doors. Unopened treasure chests use a golden occlusion silhouette in revealed rooms, controlled by the loot X-ray toggle; opening a chest or restoring it as opened removes the highlight. Revealed room/door state survives travel and save/load. This behavior is not secret-door support: concealed passages need their own discovery rules in the [dungeon design](dungeon-exploration-design.md#random-secrets).

## Reusable room arrangements

Ordinary rooms combine generated layouts with small authored arrangements: a table and chairs, a barrel cluster, or other reusable furnishings. Complete authored rooms supply landmarks and deliberate gameplay spaces. Standard-room selection currently favors procedural rooms; entrances, exits and special rooms remain authored.

Arrangement definitions select an editable scene, eligible room purposes, selection weight, repeat limit and wall/interior placement preferences. Pools separately bound floor furnishing density and wall mount counts. Compositions rotate together in quarter-turns. The general garrison arrangement pool remains available for generic rooms and contains broken and feasting banquet tables, barrel and stacked crate caches, a dining table with chairs, rubble mounds, mounted torches, banners, and wall-mounted heraldic shields; dining tables exclude sewer-purpose rooms. Mounted torches and banners demonstrate full scenes, including local light and particles.

**Every remaining free area must stay connected, and every active opening must remain accessible.** Placement accounts for actor width, existing obstacles and previously accepted arrangements. Door-to-door connectivity alone is insufficient: isolated pockets are rejected too. Later generated chests must preserve the same rule. Optional furnishings are omitted when they cannot fit safely. The conservative bounds can reject physically possible placements; actual movement, especially for large actors, still needs playtesting.

Gameplay furnishes procedural rooms after their walls and openings are resolved, before navigation baking. Furniture is reproducible from the level seed without consuming layout, encounter or loot randomness. Authored rooms keep their manually composed content. The built-in room generator can also furnish and save editable room scenes through the same placement policy.

See the [arrangement authoring guide](../scenes/levels/arrangements/README.md) for Inspector settings, editor commands and the review playground. Each scene explicitly binds a Content hierarchy and a static placement envelope. Enabled solid collision determines walkable space; light and particle extents do not reserve floor space. Wall compositions require a real supporting span at their configured height and rotate together. Corridor placement patterns remain [future work](development-roadmap.md#room-variety-and-furnishing).
