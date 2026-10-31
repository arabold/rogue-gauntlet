# Level generation folders

| Folder | What belongs here |
| --- | --- |
| `generators/` | Floor orchestration (with its access and loot partials), the `GeneratedFloor` handoff, map data, randomness and wall geometry |
| `generators/assembly/` | Room assembly: `RoomType`, `RoomAssembler`, `RoomBlueprint` and the shared `WalkabilityGrid` |
| `generators/assembly/shapes/`, `surfaces/`, `columns/`, `steps/`, `content/` | One strategy family per axis: footprints, floor/wall styles, column patterns, furnishing steps, props and groups |
| `generators/rooms/` | Legacy coded recipes (prison, fenced walkway, partitioned store) and the generic shell builder, pending their port |
| `generators/factories/` | Room, tile and monster factory contracts/implementations |
| `generators/layouts/`, `generators/connectors/` | Floor packing and corridor-routing strategies |
| `access/` | Door access policies and the interior-door capability |
| `encounters/` | Realm encounter factories, their per-floor plan and the descent objective |
| `.` (this folder) | `Level`, `FloorDiscovery` (exploration and fog) and the floor-local runtime nodes |
| `garrison/factories/` | Gameplay Garrison factory presets |
| `garrison/room_types/` | One `RoomType` per Garrison room kind |
| `garrison/props/<category>/`, `garrison/groups/` | Props (one object, cosmetic variants) and groups (anchor plus members, seated tables) |
| `garrison/styles/floors/`, `garrison/styles/walls/` | Floor materials and styles; wall styles |
| `garrison/rooms/`, `garrison/layouts/` | Legacy recipe presets still used by the prison, fenced walkway and partitioned store |
| `garrison/arrangements/` | Realm-wide and themed optional furnishing pools |
| `arrangements/` | Shared arrangement nodes, definitions, slot anchors and physical validation |
| `arrangements/strategies/` | Generic, perimeter and required-slot placement policies |
| `arrangements/furniture/`, `storage/`, `wall/`, `debris/` | Reusable compositions; keep each scene beside its definition |
| `arrangements/libraries/` | Supporting mesh libraries and extracted meshes |

Start with [implemented level design](../../docs/level-design.md), [arrangement authoring](arrangements/README.md), or the [room generation harness](../debug/rooms/room_generation_harness.tscn). [Harness index](../debug/README.md) lists the debug entry points.
