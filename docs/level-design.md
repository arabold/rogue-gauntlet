# Level Design

## Overview

This document explains the level generation, connectivity, and visibility (fog of war) systems in Rogue Gauntlet, covering core concepts and implementation details for both developers and level designers.

## Coordinate model (important)

There are three sizes in play; mixing them up causes alignment bugs:

- **GridMap cells: 1 unit.** All room `GridMap`s (`Floor`, `Wall`, `Decoration`, and the runtime `Occlusion` grid) use a `cell_size` of 1, with cell centering disabled — a mesh's own geometry is centered on its anchor cell.
- **Floor/wall meshes: variable.** Authored tile meshes are 2×2, 4×4, or even 8×8 units depending on the kit (e.g. sewer grates are 2×2, dirt floors 4×4).
- **Logical map tiles: 4 units (`TileSize = 4`).** `MapData` — the grid that generation, connectivity, fog, and occlusion all reason on — buckets the world into 4×4 logical tiles.

**The one convention everything follows: a logical tile is *centered* on its anchor
coordinate, and a position belongs to the tile whose center is nearest.** Concretely:

- Tile `(x,z)`'s center cell is `Bounds.Position + (x,z) · TileSize` in room-local space
  (`Room.TileCenterCell`) and `TileToWorld(x,z)` in master-map space; the tile's cells
  span that center ± `TileSize/2`.
- Position→tile conversions bin by **nearest center** — `Room.LocalToTile` locally,
  `MapGenerator.WorldToTile` globally — giving every conversion `TileSize/2` of slack in
  all directions. Never floor-bin a position against a tile-corner origin: that puts
  tile centers exactly on a bin boundary, where rotation or float drift flips results
  into the neighboring tile (this knife edge is what historically made rotated rooms
  fail doorway validation).
- `Room.Bounds.Position` is the **center cell of the room's first (north-west) tile**,
  derived from floor anchors by a parity rule (tile-sized meshes anchor on even cells =
  the centers themselves; half-tile meshes anchor at centers ± 1 = odd cells, snapped
  inward), so the invariant holds for every floor-mesh size and rotation.
- `DoorwayMarker`s (and other tile-anchored markers) are authored **exactly on tile
  centers**; `Room.BakeDoorwayMarkers` logs an error for any marker drifted more than
  1 unit off its tile's center.
- Whether a tile edge is walled is judged from **real wall-mesh footprints**
  (`WallFootprints`: each piece's AABB rotated by its orientation), never from which
  cells hold anchors — footprints rotate exactly with the geometry, and frame posts or
  perpendicular pieces that merely occupy cells near an edge don't read as covering it.

## Level Generation Pipeline

`MapGenerator.GenerateMap` runs these steps:

1. **Map initialization (`Reset`)**

   - Builds a `MapData` grid (`MapTile` enum: `Empty, Wall, Room, Connector, Corridor, Chasm`).
   - Outer border is set to `Wall`; everything else starts `Empty`.
   - Width/depth configurable (20–100 logical tiles).

2. **Room placement (`RoomLayoutStrategy`)**

   - Instantiates authored room templates and places them with spacing/intersection checks (`MapData.Intersects`). The intersection check enforces a 1-tile empty ring around every room tile — rooms never sit directly adjacent. This ring is load-bearing: door resolution (`FinalizeDoors`) and the fog-reveal cascade both assume a real room-to-room link always has a corridor tile between the two connectors.
   - Each placed room's local `MapData` is stamped into the master map, and per-room runtime data is recorded: a `RoomRegion` (the room's tiles and connector tiles) plus a `tile → room id` lookup. Room ids are assigned in placement order, which is deterministic for a given seed.
   - Two strategies exist. **`PackedRoomLayout` (the default)** grows a tight cluster: the entrance anchors the map center, each following room is placed on a candidate position hugging the already-placed rooms at the minimum legal gap (scored for compactness plus a bonus for doorways that end up facing each other across the gap), and the exit is placed last as far from the entrance as possible — rolling back standard rooms if the map filled up before the exit fit. **`SimpleRoomLayout`** places rooms at random positions with retries, producing sparse, sprawling layouts with long corridors.

3. **Corridor connection (`CorridorConnectorStrategy`)** — see [Connectivity](#connectivity-doorways-vs-open-edges).

4. **Door resolution (`FinalizeDoors`, `FinalizeMarkers`)**

   - Matches hand-placed `Door` props to the nearest connector of their room.
   - A door at a doorway that was actually connected becomes a real gating door; a door at a doorway that got walled shut is removed (so there are no interactable doors embedded in solid walls).
   - A `DoorwayMarker`'s tile keeps its `Connector` classification in `MapData` regardless of whether a corridor ever got routed to it (wall placement is pure geometry and never touches tile classification) — so a doorway can validate fine yet still end up walled shut, most often when a room has more doorways than the layout had room to route corridors to. `FinalizeMarkers` matches every marker to its connector the same way `FinalizeDoors` matches doors, and hides (`Enabled = false`) any marker whose every sanctioned direction ended up sealed, so its editor-only arrow gizmo never misleadingly points at a solid wall.

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

Net effect: a 4-way sewer crossing keeps all four doored entrances; a cave (inferred edges only) gets one opening and the rest stay closed. After connection, `PlaceWalls` closes any exposed room/corridor edge that didn't become a passage. That includes *interior* separation (`MapData.RequiresInteriorWall`): a corridor routed directly alongside a room — common with the packed layout — is walled off from it except through a connector's open direction, so an unwalled room edge next to a passing corridor never becomes an unintended entrance. (A cave's open edges *are* connectors, so a corridor brushing past a cave can still open into it — intended, organic behavior.)

`PlaceWalls` runs as a decision pass and a coverage pass. The decision pass applies one rule per tile edge (`NeedsWallToward`: the neighbor is void/out-of-bounds, or the contact is unsanctioned interior contact) and skips edges the room template already sealed with authored walls (judged from mesh footprints via `WallFootprints`). Placement then puts a full-width wall on each required edge's free midpoint cell (the overwhelmingly common case), after which the coverage pass re-measures the actual mesh footprints and patches any required edge still showing daylight — placing full or half pieces anchored at whatever free cells actually cover the hole. An edge that cannot be sealed is a loud `GD.PrintErr`, never a silent see-through gap; the historical failure mode here was an unconnected authored doorway whose frame pieces occupied the seal's anchor cells while covering none of the opening.

Generation correctness is covered by seed-sweep tests: `WallIntegrityTest` (no gaps in wall geometry, for every layout strategy) and `LayoutConnectivityTest` (single connected walkable component, all doorways connected, per-seed determinism, packed density beats simple). To *visually* sanity-check a layout — e.g. whether a doorway's connector tile sits flush with its wall gap — render a top-down screenshot with `.agents/skills/godot-mcp/scripts/render_level_topdown.gd` (see the godot-mcp skill's "Visual verification" section); it overlays `MapGenerator.GetConnectorDebugInfo()` directly rather than relying on the editor-only `DoorwayMarker` gizmo.

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
- The flood only ever crosses a connector's own sanctioned direction (`ConnectorOpensToward`) — both when it first steps out of a room into a corridor and when it later reaches another room's connector from that corridor. A corridor is allowed to run alongside a connector's other, non-sanctioned sides (`MapData.RequiresInteriorWall` keeps that contact walled); without this the flood would reveal that corridor straight through the wall the moment the room next to it opens. `FogOfWarRevealTest` guards this by re-deriving, independently of `RevealRoom`/`FloodCorridors`, which tiles a single room's reveal should legitimately expose.

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

## Procedural Rooms

`ProceduralRoomBuilder` (a `Resource`, not a scene) builds a `Room` node programmatically instead
of instantiating an authored scene, for instant layout variety. `MixedRoomFactory` blends it with
an authored `RoomFactory`: entrances, exits, and special rooms always come from authored content
(they carry hand-placed gameplay content a builder can't provide); standard rooms are procedural
with a configurable probability (`ProceduralShare`).

- **Footprint**: a rectangle, an L-shape (one corner carved away), or a union of two overlapping
  rectangles (an S/T-like shape), sized within `MinTiles`/`MaxTiles`. A large rectangle can also
  get an interior pit (kept a tile clear of every edge) that bakes into a chasm — L/union shapes
  never get a pit, since a notch could merge with one into a region touching the room's own
  bounds, which would not bake into a chasm.
- **Floors**: a base floor item is picked per room for coherence. With `FloorAccentChance` the
  room instead mixes in a second accent item (e.g. wood over stone) across one deliberate
  sub-region — a centered rug-like patch, or a trim border around the room's edge — rather than
  varying material per tile at random, which reads as noise instead of a designed room. Each
  logical tile's anchor placement generalizes to any mesh footprint size that evenly divides
  `TileSize` with matching parity (1×1 doesn't qualify under this `GridMap`'s integer-coordinate,
  centering-disabled setup — only 2×2/4×4 today), so base and accent items can even differ in size.
- **Columns**: placed on interior tile corners (where four floor tiles meet) using one arrangement
  chosen for the whole room with `ColumnChance` — centered single column, a tight 2×2 cluster at
  the center, an evenly spaced colonnade along the walls, or a mirror-symmetric scatter — rather
  than an independent per-corner coin flip, since real rooms place columns by design, not at
  random. Every arrangement degrades gracefully on irregular (L-shape/union) footprints: a
  candidate corner that isn't actually a valid interior corner for that room's shape is simply
  skipped.
- **Doorways/doors**: by default a room stays fully open — no `DoorwayMarker`s, so every edge is
  an inferred connector, exactly like an authored cave room. With `DoorwayChance` it instead gets
  1–`MaxDoorways` explicit, guaranteed-connected doorways, each independently getting a real
  `Door` (`DoorScene`) with `DoorChance` probability (0 makes every doorway an open archway, 1
  makes every doorway doored, in between mixes both per room). A corner tile can face two
  directions at once, but the builder commits to exactly **one**: corridor routing happens later
  (once the whole map is placed) and can only ever connect through a single direction, so
  combining both into one marker could let the corridor connect through the side a placed door
  does *not* face — leaving that door pointless against a generated wall while the real,
  connected passage sits open with no door at all.
- **No wall authoring needed**: once any `DoorwayMarker` exists, every *other* open edge stops
  being an inferred connector and falls back to a plain `Room` tile, which the generator's normal
  wall pass (above) seals exactly like a void-facing edge. Marking doorways is never paired with
  hand-authoring walls to avoid unintended openings — the existing generated-wall mechanism
  already covers it.

## Room Rotation

`Room.Rotate(steps)` rotates a room's authored content — floor/wall/decoration `GridMap` cells
and every other child node (props, doorway markers, doors) — by 1–3 quarter turns around the
room's local origin, then re-bakes so `Map`/`Bounds` reflect the rotated geometry.

This only ever rotates *raw geometry*, never a hand-derived `MapData`: a 90-degree-multiple
rotation around an integer pivot is a pure permutation of the integer lattice (a coordinate swap
plus sign flips, no scaling or fractional offset), so it preserves every tile-grid alignment
relationship `BakeTileMap` depends on — a mesh anchored exactly on a tile's origin stays exactly
on a (relabeled) tile's origin after rotation. Re-baking on the rotated geometry is therefore
exactly as correct as if the room had been authored in that orientation to begin with, and it
reuses the same wall/doorway-marker scanning logic every room already goes through.

`PackedRoomLayout` tries all 4 orientations of the *same* room instance for every candidate slot
(not 4 independently created rooms — for a procedurally built room that would each be a different
random shape) and keeps whichever (position, rotation) pair scores best; "unrotated" is one of the
4 options considered, so this can only ever match or beat placing the room without rotation.
Rotating is re-checked for connectability at every orientation before it's considered a candidate,
and any orientation that comes out unreachable is skipped rather than ever being placed — see
`RoomLayoutStrategy.IsConnectable`, re-run after every `Room.Rotate` call in
`PackedRoomLayout.TryFindBestPlacementAcrossRotations`.

**Every room in the library — authored and procedural — validates at all 4 orientations**, and
`RoomRotationTest.EveryAuthoredRoomBakesConnectorsAtAllFourRotations` enforces that as a permanent
contract. (Historically most authored rooms failed at 1–3 rotations; that was never real geometry
— it was two coordinate knife edges, since fixed: marker position→tile conversion floor-binned
against a tile-corner origin, putting tile centers exactly on a bin boundary, and `HasWall`
sampled a half-open cell row whose corner anchors flipped in and out of range per orientation.
Both were replaced by the nearest-center/footprint-based conventions in the
[Coordinate model](#coordinate-model-important) section.) The per-orientation connectability
re-check in `PackedRoomLayout` remains as defense in depth, but a skipped orientation now
indicates a content bug worth investigating, not expected behavior.

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

- Room templates load via `RoomFactory`, which returns ready-to-place (not yet baked) `Room`
  instances — `DungeonRoomFactory` instantiates authored scenes, `MixedRoomFactory` optionally
  routes standard-room requests through `ProceduralRoomBuilder` instead (see Procedural Rooms
  above). `TileFactory` selects tile variants; `MobFactory` configures enemies.
- The navigation mesh is auto-baked after generation; seed-based generation keeps layouts reproducible (and fog reveal replayable).
- Level transitions reload the scene; the reload is deferred out of the trigger's physics callback to avoid freeing collision bodies mid-step.
