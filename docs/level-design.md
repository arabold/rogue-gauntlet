# Level Design

## Overview

This document explains the level generation, connectivity, and visibility (fog of war) systems in Rogue Gauntlet, covering core concepts and implementation details for both developers and level designers.

## Coordinate model (important)

There are three sizes in play; mixing them up causes alignment bugs:

- **GridMap cells: 1 unit.** All room `GridMap`s (`Floor`, `Wall`, `Decoration`, and the runtime `Occlusion` grid) use a `cell_size` of 1.
- **Floor/wall meshes: variable.** Authored tile meshes are 2×2, 4×4, or even 8×8 units depending on the kit (e.g. sewer grates are 2×2, dirt floors 4×4).
- **Logical map tiles: 4 units (`TileSize = 4`).** `MapData` — the grid that generation, connectivity, fog, and occlusion all reason on — buckets the world into 4×4 logical tiles. `Room.BakeTileMap` marks a logical tile as floor if it finds *any* floor cell in its 4×4 area.

When converting: `MapGenerator.TileToWorld` maps a logical tile to GridMap coordinates (centered on the map), and `WorldToTile` inverts it.

## Level Generation Pipeline

`MapGenerator.GenerateMap` runs these steps:

1. **Map initialization (`Reset`)**

   - Builds a `MapData` grid (`MapTile` enum: `Empty, Wall, Room, Connector, Corridor, Chasm`).
   - Outer border is set to `Wall`; everything else starts `Empty`.
   - Width/depth configurable (20–100 logical tiles).

2. **Room placement (`RoomLayoutStrategy`)**

   - Instantiates authored room templates and places them with spacing/intersection checks (`MapData.Intersects`).
   - Each placed room's local `MapData` is stamped into the master map, and per-room runtime data is recorded: a `RoomRegion` (the room's tiles and connector tiles) plus a `tile → room id` lookup. Room ids are assigned in placement order, which is deterministic for a given seed.

3. **Corridor connection (`CorridorConnectorStrategy`)** — see [Connectivity](#connectivity-doorways-vs-open-edges).

4. **Door resolution (`FinalizeDoors`)**

   - Matches hand-placed `Door` props to the nearest connector of their room.
   - A door at a doorway that was actually connected becomes a real gating door; a door at a doorway that got walled shut is removed (so there are no interactable doors embedded in solid walls).

5. **Occlusion placement (`PlaceOcclusion`)** — see [Map Occlusion & Fog of War](#map-occlusion--fog-of-war).

6. **Navigation & spawns**

   - Bakes the navigation mesh for AI.
   - Places the player spawn point and enemy spawn points from logical room/corridor tiles, avoiding occupied decoration/prop tiles, stairs, transitions, and other spawn points.

Generation is seed-based (`GameSession.GetLevelSeed(seed, depth)`), so a given run + depth always produces the same layout — this is what lets fog reveal be persisted compactly (see below).

## Connectivity: doorways vs. open edges

Rooms connect to corridors through **connector** tiles, and there are two kinds:

- **Doorways** — connectors created from an explicit `DoorwayMarker` in the room scene (`Room.BakeDoorwayMarkers`). These are intentional entrances and are flagged in `MapData` via `IsDoorway`. Doors (`Door` props) are placed at doorways.
- **Inferred open edges** — when a room has _no_ `DoorwayMarker`s, every wall-free edge tile becomes a connector (`Room.BakeInferredConnectors`). These are optional openings.

`AStarCorridorConnector` connects them in two phases:

1. **Spanning tree (`ConnectComponents`)** — links every room into one reachable network with one corridor each. This guarantees **every room has at least one connection**.
2. **Remaining doorways (`ConnectRemainingDoorways`)** — force-connects every `IsDoorway` connector the spanning tree left unconnected. **Inferred edges are never force-connected** — they stay optional.

Net effect: a 4-way sewer crossing keeps all four doored entrances; a cave (inferred edges only) gets one opening and the rest stay closed. After connection, `PlaceWalls` closes any exposed room/corridor edge that didn't become a passage.

## Map Occlusion & Fog of War

The dungeon is a 3D scene viewed through a rotatable orthographic (isometric) camera, so two visibility problems are solved together with one `OcclusionGridMap` of flat, unlit black "cap" meshes sitting at wall-top height:

- **Void occlusion:** unreachable space (and the exterior faces of walls) is hidden behind black caps.
- **Fog of war:** undiscovered rooms/corridors start capped and are revealed as the player explores.

In gameplay (`PlaceOcclusion(fog: true)`) **every** tile starts capped; the occluder is a faithful inverse of the map mask. In the editor preview (`fog: false`) only the void/border is capped so the layout stays visible.

### Reveal flow

- **`RoomManager`** polls the player's tile each physics frame (`WorldToTile` → `GetRoomIdAt`) and emits `SignalBus.RoomEntered` when the owning room changes. Detection uses interior floor tiles only — standing against a closed door does not count as entering.
- **`FogOfWar`** listens for `RoomEntered` and calls `MapGenerator.RevealRoom`, which removes the caps over:
  - the room's footprint (floor, connectors, and chasm pits), and
  - everything reachable from it **without crossing a door** — it floods through open connectors into connected corridors and cascades into further door-free rooms.
- A **doored** connector blocks the cascade, so the corridor and rooms beyond a closed door stay hidden. The doorway tile itself is still revealed (it's part of the room); the closed door plus the hidden corridor are the seal.
- Opening a door (`Door` emits `SignalBus.DoorOpened`) calls `MapGenerator.OpenDoorAt`, which unseals that connector and reveals through it.

### Door indicators (x-ray)

Because the camera rotates, a closed door can end up hidden behind a wall. Each `Door` carries an x-ray silhouette (`door_xray.gdshader`) that:

- only draws where the door is actually occluded by scene geometry (it samples the depth texture), and
- fades with distance, and
- is only enabled once a tile adjacent to the door has been revealed (so undiscovered doors don't leak through the fog — `UpdateDoorIndicators`).

### Persistence

Reveal state is saved per dungeon depth (`WorldSaveData.RevealedLevels`) as the **rooms entered** and **doors opened**, not the raw tiles. On load, `FogOfWar` replays them via `MapGenerator.RestoreReveal`; because generation is deterministic, replaying reproduces the exact explored area. This survives quit/reload and travelling between depths.

## Enemy Door Routing Concept

Enemies should continue to use Godot navigation for movement shape. The map-level door data should only decide whether a target is currently reachable, not replace the navmesh with hand-authored waypoint steering.

- `DoorwayMarker` directions are already baked into connector tiles via `MapData.GetConnectorDirections`; this data identifies the corridor side of each intentional doorway.
- `MapGenerator` already tracks `_dooredConnectors`. A connector in that set is currently closed; `OpenDoorAt` removes it when the player opens the matching door.
- A future door-aware chase pass should use that state to detect "target is behind a closed door" and either stop, search, pick an already-open alternate route, or interact with the door. It should avoid forcing enemies through doorway waypoints unless the navmesh itself cannot model the route.

The important rule: navmesh remains the source of movement paths; logical door state gates target selection and reachability.

## Room Template Creation

### Technical requirements

1. **Grid compatibility**

   - The logical grid is 4 units per tile; floor meshes may be 2×2/4×4/8×8 but must tile cleanly.
   - Room bounds should align to the grid so `BakeTileMap` buckets cleanly.

2. **Components**

   - Provide the three `GridMap`s (`Floor`, `Wall`, `Decoration`); collision lives on the wall meshes.

3. **Doorways & doors**

   - Add a `DoorwayMarker` at each intentional entrance. Every doorway will be connected by a corridor, so only mark real entrances.
   - A room with **no** `DoorwayMarker`s exposes all its open edges as optional connectors (good for caves/organic rooms where any edge may connect).
   - Place a `Door` prop at a doorway to make it a gating door; doorways without a door are open archways. Doors at doorways that don't end up connected are removed automatically.

4. **Props**

   - Parent props under the room scene; include collision where relevant.

### Design guidelines

- Leave room for combat (≥ 4×4 tiles of open floor).
- Use doorways deliberately — a 4-way crossing with four doors will branch four ways; a single-doorway room is a dead-end pocket.
- Use the debug overlay (`Room.ShowDebugOverlay`) to inspect the baked mask (cyan = room, purple = connector, black = empty).

## Implementation Notes

- Room templates load via `RoomFactory`; `TileFactory` selects tile variants; `MobFactory` configures enemies.
- The navigation mesh is auto-baked after generation; seed-based generation keeps layouts reproducible (and fog reveal replayable).
- Level transitions reload the scene; the reload is deferred out of the trigger's physics callback to avoid freeing collision bodies mid-step.
