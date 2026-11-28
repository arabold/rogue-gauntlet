# Level generation folders

| Folder | What belongs here |
| --- | --- |
| `generators/` | Floor orchestration (`MapGenerator` and its `Access`, `Fountains`, `Gauntlets`, `Loot`, `Navigation`, `PlateTraps`, `Shops` and `Walkability` partials), the `GeneratedFloor` handoff, map data, randomness, wall geometry, the realm schedule types (`Realm`, `RealmRoom`, `RealmMotif`, `RealmFloor`, `FloorPlanChoice`, `UniqueFloorPlan`), `FloorProfile`, and the run's fountain and gauntlet profiles |
| `generators/floor_profiles/` | One `FloorProfile` per floor plan (cluster, cluster_large, hallway, prison, cathedral, cloister, mines, labyrinth, boss): its layout, corridor connector, lock preset, map size and room count |
| `generators/layouts/`, `generators/connectors/` | Room layout strategies (packed, hall and courtyard, sparse, linear) and corridor connectors (A*, maze, mine tunnels) |
| `generators/assembly/` | Room assembly: `RoomDefinition` (`RoomType`, `RoomGroup`), `RoomPool`, `RoomAssembler`, `RoomBlueprint` and the shared `WalkabilityGrid` |
| `generators/assembly/shapes/`, `surfaces/`, `columns/`, `steps/`, `content/` | One strategy family per axis: footprints, floor and wall styles, column patterns, furnishing steps, props and groups |
| `generators/corridors/` | Corridor decoration: `CorridorBlueprint` and `CorridorStep` strategies (passageways, thresholds, corners, hallway walls, mine rails, dead ends) |
| `access/` | Locks, unlock methods, secret doors, proximity discovery and the interior-door capability |
| `access/floor_access_profiles/`, `access/locks/` | The lock presets floor plans draw from, and the lock definitions they share |
| `encounters/` | Realm encounter factories (`MobFactory`, `RealmEncounterFactory`) and their resources, the threat profile, nests, the floor's `EncounterPlan` and the descent objective |
| `navigation/` | The clearance grids and shared flow fields monsters navigate on |
| `lighting/` | World lighting, surface finishes and atmosphere profiles and effects |
| `.` (this folder) | `Level`, `FloorDiscovery` (exploration and fog) and the floor-local runtime nodes |
| `dungeon/` | Shared floor, wall and decoration mesh libraries, water, the data-driven corridor tile factory (`DungeonTileFactory`) and the development monster fallback below the last realm encounters (`DungeonMobFactory`) |
| `dungeon/custom_tiles/` | Asset Studio tile meshes and collision registered in those libraries by `register_custom_tiles.gd`; rerun it after a `studio.py tile export`, and after re-exporting a library from its `*_mesh_library.tscn` source scene, which holds only the KayKit items |
| `rooms/room_pool.tres` | The entrances, exits and gauntlet rooms every realm shares, and the ordinary rooms a realm without a schedule draws from |
| `rooms/room_types/` | One `RoomType` per room kind, tagged with the themes it suits; gauntlet rooms in `gauntlets/`. Rooms only a plan or a schedule names (headlines, the nave, the courtyard, cells) stay out of the pool |
| `rooms/room_groups/` | Composite room definitions placed as one cluster: the boss approach (`boss_exit`) and the gauntlet approach (`gauntlet_exit`) |
| `rooms/props/<category>/`, `rooms/prop_groups/` | Props (one object, cosmetic variants) and prop groups (anchor plus members, seated tables) |
| `rooms/styles/floors/`, `rooms/styles/walls/` | Floor materials and styles, inlays and transitions; wall styles, each tagged with its themes |
| `realms/` | One `Realm` per act of five depths (its theme, room schedule, motifs, floor plans, boss plan, shops and atmosphere), and the corridor tile factories each theme shares |
| `stairs/` | The level's up- and down-stairs scenes (staircase, arrival spawn, level transition) placed by `StairsStep` |
| `../props/furnishings/` | Prop scenes that need nodes (torches, banners, bookcases) and the furnishing mesh library |
| `../props/traps/`, `../props/triggers/` | Trap hazards, wall launchers and plate-trap profiles; pressure plates and the trigger core |

Start with [implemented level design](../../docs/level-design.md), [realm content](../../docs/realm-content.md), [room assembly authoring](generators/assembly/README.md), or the [room generation](../debug/rooms/room_generation_harness.tscn) and [map generation](../debug/maps/map_generation_harness.tscn) harnesses. The [harness index](../debug/README.md) lists the debug entry points.
