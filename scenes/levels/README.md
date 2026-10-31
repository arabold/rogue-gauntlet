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
| `garrison/factories/` | Gameplay Garrison factory presets |
| `garrison/room_types/` | One `RoomType` per Garrison room kind |
| `garrison/props/<category>/`, `garrison/groups/` | Props (one object, cosmetic variants) and groups (anchor plus members, seated tables) |
| `garrison/styles/floors/`, `garrison/styles/walls/` | Floor materials and styles; wall styles |
| `../props/furnishings/` | Prop scenes that need nodes (torches, banners, bookcases) and the furnishing mesh library |

Start with [implemented level design](../../docs/level-design.md), [room assembly authoring](generators/assembly/README.md), or the [room generation harness](../debug/rooms/room_generation_harness.tscn). [Harness index](../debug/README.md) lists the debug entry points.
