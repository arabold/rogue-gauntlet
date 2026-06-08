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

## Enemy AI structure

Enemy behavior is split across small, single-purpose pieces so each concern can change without
destabilizing the others (the layout that replaced the original monolithic controller):

- **`EnemyBehaviorComponent`** (the host node) wires everything together and owns the **action
  layer**: timed, animation-driving actions (spawn, stand up, hit, melee/ranged attack, death). While
  an action is active the body holds still and the behavior layer is frozen; when it elapses control
  returns to the state machine. The host also exposes the `Is*` flags the `AnimationTree` reads.
- **`PerceptionComponent`** answers "what can I sense" — the vision cone, hearing radius, and
  line-of-sight test (see "Enemy Detection" below). It never decides what to do about a target.
- **`NavigationComponent`** owns "how the body moves" along the baked navmesh — path-following, wall
  sliding, stuck detection, and navmesh queries (reachability, doorway crossing). Recovery *policy*
  (repath, abandon roam, give up a chase) lives in the states, not here.
- **The behavior state machine** (`EnemyStateMachine` + the `IEnemyState` classes Idle, Patrolling,
  Searching, Chasing, Fleeing, and a passive state for Sleeping/Guarding/Dead) decides what to do.
  Each state has `Enter`/`Update`/`Exit`; `Update` returns the next state's id or null to stay. One
  instance of each state is created per enemy and reused, so state-local data persists across
  re-entry. Shared data (current target, last-known position, roam anchor, throttle `Cooldown`s) and
  the perception/navigation helper queries live on a shared **`EnemyContext`** passed to every state.

`EnemyBehaviorProfile` resources hold the per-monster tuning. When changing detection or chase rules,
edit the perception/state code and keep the invariants documented below intact.

## Enemy Detection (sight & hearing)

Enemies acquire the player through `PerceptionComponent.FindVisibleTarget` (driven from the behavior
layer's `EnemyContext.LookForNewTarget`). A candidate must be within `DetectionRange` **and**
reachable over the navmesh (`EnemyContext.CanReachTarget`, backed by `NavigationComponent.IsReachable`),
and then pass one of two detection modes — **but every mode requires an unobstructed line of sight**:

```
detected = isReachable && (isClose || IsWithinVisionCone) && CanSee
```

- **Sight (long range, directional).** Up to `DetectionRange`, the player is seen only when inside
  the forward vision cone (`IsWithinVisionCone`, half-angle `DetectionAngle`). Because it is
  directional, the player can slip past *behind* an enemy without being seen.
- **Hearing (close range, omnidirectional).** Within `DetectionRange * CloseDetectionRangeMultiplier`
  the player is detected regardless of facing — the enemy "hears" them. Facing is dropped, but the
  line-of-sight requirement is **not**.
- **Line of sight (`PerceptionComponent.CanSee`) is mandatory for both modes.** This is what keeps
  detection inside a single room: walls and closed doors sit on the sight collision mask, so a clear
  line proves enemy and player share an open space. Without it, the omnidirectional hearing radius
  would wake enemies through walls in adjacent rooms.

Invariants that future changes must preserve (breaking either silently re-introduces past bugs):

1. **The sight ray is cast at eye height, not between body origins.** `CanSee` raises
   both endpoints to the `SightRay` node's authored Y (~1.5). The body origins sit at floor level
   (y≈0) while the collision shapes are at hip height; a floor-level ray grazes the ground and wall
   bases and rarely reaches the target, so it would report "no clear line" for everyone and enemies
   would never detect the player. Keep the cast horizontal at eye height.
2. **Hearing must keep the line-of-sight check.** The `&& CanSee` applies to the whole
   condition, including the `isClose` branch. Re-ordering it to `isClose || (cone && line)` would
   let proximity alone aggro through walls again — the exact cross-room bug this design fixes.

The relevant tuning lives on `EnemyBehaviorProfile`: `DetectionRange`, `DetectionAngle`,
`CloseDetectionRangeMultiplier`. The sight mask and eye height are authored on the enemy's
`SightRay` node (`enemy_behavior_component.tscn`).

### Chase retention

Acquisition (above) is separate from how long a chase *sticks*. Retention is **reachability-based,
not sight-based**: once alerted, the enemy keeps repathing to the target's **live** position for as
long as the navmesh can still reach it. This is deliberate — an enemy that has to take the long way
around (out through another door, around a wall) loses line of sight to the player *en route*, so
ending the chase on lost sight would make it give up exactly when it is pursuing correctly.

- While the target stays reachable, the enemy follows its live position, pursuing around corners and
  through open doors **even with no line of sight**.
- A chase ends only when the navmesh path to the target breaks — e.g. a door closes between them, or
  the target reaches an area with no connecting path. The enemy then enters `Searching`, walks to the
  last reachable position for `SearchDuration`, and returns to patrol if it finds nothing.
- Reachability is tested with `NavigationComponent.IsReachable` (door-aware; see "Enemy Door
  Awareness"), on a throttled periodic check rather than every frame.
- The give-up check is skipped while the agent is mid-doorway crossing a `NavigationLink3D`, where it
  is briefly off-mesh; it commits to the crossing instead of oscillating between the doorway sides.

Line of sight (`PerceptionComponent.CanSee`) still governs **acquisition** — it is what stops an
enemy waking to a player in another room — but it must **not** be re-added as a chase give-up
condition (that was the earlier design, and it abandoned valid pursuits around corners). There is no
distance leash: an aggroed enemy pursues anywhere a path exists until the path is severed.

The net effect: enemies follow you relentlessly through the connected space once alerted, and you
shake them by breaking the path (closing a door between you) or reaching somewhere they can't path
to — not merely by ducking out of sight.

## Enemy Door Awareness

The baked navigation mesh is the single source of truth for both enemy movement and target
reachability. It reflects door state directly, so there is no separate logical door-gating for
AI:

- The main navmesh is permanently severed at every doored doorway (the closed door geometry blocks
  the bake; open archways with no door bake through as one). Each `Door` scene carries a
  `NavigationLink3D` spanning the doorway gap (endpoints on each side, `z = ±1.5` at floor height).
  `Door.Update` enables the link only while the door is open, so a closed door leaves the doorway
  disconnected. A link reconnects by *proximity* to the navmesh on each end (within the map's link
  connection radius) instead of by fragile edge-alignment, so it bridges the doorway reliably — a
  hand-authored flat region patch only stitched one side of the gap and the path dead-ended at the
  door.
- Because a link *spans* the gap rather than filling it, an agent is briefly off the navmesh while
  crossing a doorway. The chase state detects this (`NavigationComponent.IsCrossingDoorway`, via
  horizontal distance to the nearest navmesh point) and freezes path refresh and reachability
  give-ups until the agent lands back on the mesh, so it commits to the crossing instead of
  oscillating between the two doorway sides.
- Enemy target acquisition and chase retention test reachability with
  `NavigationServer3D.MapGetPath` (`NavigationComponent.IsReachable`): a target behind
  a closed door yields no connecting path, so the enemy will not aggro through it and will route
  through open doors instead. The query is side-effect free and never disturbs the agent's path.
- When a chase is lost (the player breaks contact or a door closes), the enemy enters `Searching`
  and walks to the last known position for `EnemyBehaviorProfile.SearchDuration` before returning
  to patrol, rather than forgetting the target instantly.
- Closed doors still block physics, so a roaming enemy that bumps one recovers via stuck handling.
- The navigation debug overlay (Debug menu → Navigation) draws each door link as a strip in the
  same blue as the baked navmesh, snapped to the navmesh height, so every doorway bridge is
  visible whether the door is open or closed.

The important rule: door state lives in the navmesh (open = link enabled), and the same navmesh
drives movement, reachability, and aggro — no parallel reachability model.

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
