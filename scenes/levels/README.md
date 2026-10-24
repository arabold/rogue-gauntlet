# Level generation folders

| Folder | What belongs here |
| --- | --- |
| `generators/` | Floor orchestration, map data, randomness and wall geometry |
| `generators/rooms/` | Coded room generators and the shared shell builder |
| `generators/factories/` | Room, tile and monster factory contracts/implementations |
| `generators/layouts/`, `generators/connectors/` | Floor packing and corridor-routing strategies |
| `garrison/factories/` | Gameplay Garrison factory presets |
| `garrison/rooms/<type>/` | A room's generator preset, shell and dedicated dressing/decoration policies |
| `garrison/rooms/shared/` | Shells reused across room types and the generic builder preset |
| `garrison/arrangements/` | Realm-wide and themed optional furnishing pools |
| `arrangements/` | Shared arrangement nodes, definitions, slot anchors and physical validation |
| `arrangements/strategies/` | Generic, perimeter and required-slot placement policies |
| `arrangements/furniture/`, `storage/`, `wall/`, `debris/` | Reusable compositions; keep each scene beside its definition |
| `arrangements/libraries/` | Supporting mesh libraries and extracted meshes |

Start with [implemented level design](../../docs/level-design.md), [arrangement authoring](arrangements/README.md), or the [room generation harness](../debug/rooms/room_generation_harness.tscn). [Harness index](../debug/README.md) lists the debug entry points.
