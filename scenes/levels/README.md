# Level generation folders

| Folder | What belongs here |
| --- | --- |
| `generators/` | Floor orchestration (with its access and loot partials), the `GeneratedFloor` handoff, map data, randomness and wall geometry |
| `generators/assembly/` | Room assembly: `RoomType`, `RoomAssembler`, `RoomBlueprint` and the shared `WalkabilityGrid` |
| `generators/assembly/shapes/`, `surfaces/`, `columns/`, `steps/`, `content/` | One strategy family per axis: footprints, floor/wall styles, column patterns, furnishing steps, props and groups |
| `generators/corridors/` | Corridor decoration: `CorridorBlueprint` and `CorridorStep` strategies (passageways, doorway thresholds, corners/T-junctions, hallway walls) |
| `generators/factories/` | Room, tile and monster factory contracts/implementations |
| `generators/layouts/`, `generators/connectors/` | Floor packing and corridor-routing strategies |
| `access/` | Door access policies and the interior-door capability |
| `encounters/` | Realm encounter factories, their per-floor plan and the descent objective |
| `.` (this folder) | `Level`, `FloorDiscovery` (exploration and fog) and the floor-local runtime nodes |
| `dungeon/` | Shared floor, wall and decoration mesh libraries and the data-driven corridor tile factory |
| `dungeon/custom_tiles/` | Asset Studio tile meshes and collision registered in those libraries by `register_custom_tiles.gd`; rerun it after a `studio.py tile export`, and after re-exporting a library from its `*_mesh_library.tscn` source scene, which holds only the KayKit items |
| `dungeon/styles/` | Floor materials, their plain floor styles and wall styles over the studio tiles, shared by every realm |
| `garrison/factories/` | Gameplay Garrison factory presets |
| `garrison/room_types/` | One `RoomType` per Garrison room kind |
| `garrison/props/<category>/`, `garrison/groups/` | Props (one object, cosmetic variants) and groups (anchor plus members, seated tables) |
| `garrison/styles/floors/`, `garrison/styles/walls/` | Floor materials and styles; wall styles |
| `garrison/garrison_realm.tres`, `sewer/`, `jungle/` | One folder per [realm](../../docs/level-design.md#current-generation): its `Realm`, corridor tiles, room types, its own floor styles (rugs, inlays, splits) and realm-only props. Realms reuse the Garrison props and groups |
| `stairs/` | The level's up- and down-stairs scenes (staircase, arrival spawn, level transition) placed by `StairsStep` |
| `../props/furnishings/` | Prop scenes that need nodes (torches, banners, bookcases) and the furnishing mesh library |

Start with [implemented level design](../../docs/level-design.md), [room assembly authoring](generators/assembly/README.md), or the [room generation harness](../debug/rooms/room_generation_harness.tscn). [Harness index](../debug/README.md) lists the debug entry points.
