# Level generation folders

| Folder | What belongs here |
| --- | --- |
| `generators/` | Floor orchestration (with its access and loot partials), the `GeneratedFloor` handoff, map data, randomness and wall geometry |
| `generators/assembly/` | Room assembly: `RoomType`, `RoomAssembler`, `RoomBlueprint` and the shared `WalkabilityGrid` |
| `generators/assembly/shapes/`, `surfaces/`, `columns/`, `steps/`, `content/` | One strategy family per axis: footprints, floor/wall styles, column patterns, furnishing steps, props and groups |
| `generators/factories/` | Room, tile and monster factory contracts/implementations |
| `generators/layouts/`, `generators/connectors/` | Floor packing and corridor-routing strategies |
| `access/` | Door access policies and the interior-door capability |
| `encounters/` | Realm encounter factories, their per-floor plan and the descent objective |
| `.` (this folder) | `Level`, `FloorDiscovery` (exploration and fog) and the floor-local runtime nodes |
| `dungeon/` | Shared floor, wall and decoration mesh libraries and the data-driven corridor tile factory |
| `dungeon/custom_tiles/` | Asset Studio tile meshes and collision registered in those libraries by `register_custom_tiles.gd`; rerun it after a `studio.py tile export`, and after re-exporting a library from its `*_mesh_library.tscn` source scene, which holds only the KayKit items |
| `rooms/room_pool.tres` | The room pool every floor draws from; [realm tags](../../docs/level-design.md#current-generation) narrow it per realm |
| `rooms/room_types/` | One `RoomType` per room kind, tagged with the realms it appears in |
| `rooms/props/<category>/`, `rooms/groups/` | Props (one object, cosmetic variants) and groups (anchor plus members, seated tables) |
| `rooms/styles/floors/`, `rooms/styles/walls/` | Floor materials and styles, inlays and transitions; wall styles, each tagged with its realms |
| `realms/` | One `Realm` per band of depths and its corridor tiles |
| `stairs/` | The level's up- and down-stairs scenes (staircase, arrival spawn, level transition) placed by `StairsStep` |
| `../props/furnishings/` | Prop scenes that need nodes (torches, banners, bookcases) and the furnishing mesh library |

Start with [implemented level design](../../docs/level-design.md), [room assembly authoring](generators/assembly/README.md), or the [room generation harness](../debug/rooms/room_generation_harness.tscn). [Harness index](../debug/README.md) lists the debug entry points.
