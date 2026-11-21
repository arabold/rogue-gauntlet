# Floor generators, profiles, and room groups

Status: proposed. [Level design](../level-design.md) describes the implemented generator; [Dungeon layout, access, and discovery](../dungeon-exploration-design.md) owns gameplay rules for realm pools, route obligations, locks, and secrets; [Door access](../door-access.md) owns implemented lock/secret mechanics; [Dungeon progression](level-progression-prototype-plan.md) tracks per-depth realm recipes and the visible Warden stair seal.

## Scope

Add three new floor generator archetypes and an Inversion-of-Control (`FloorProfile`) + composite room-group (`RoomGroup`) architecture on the existing fixed `MapWidth × MapDepth` canvas (default `20×20`, no dynamic bounds cropping):

1. **Labyrinth (`labyrinth.tres`):** Entrance, distant Exit, and 1–3 dedicated rooms; the remaining playable space is carved into 1-tile-wide hallways with dead ends, traps, and optional loops. No secret doors. Physical doors on every room and the exit; lockable dedicated rooms and exit (with ordered key chains); keys, chests, and loose items eligible anywhere (weighted toward dead ends and corridors).
2. **Dense / Gapless (`dense.tres`):** Tightly packed rooms with 1-tile corridors filling all interior empty pockets between rooms (organic outer silhouette, zero enclosed interior holes).
3. **Linear Boss (`linear_boss.tres`):** Linear spine (`Entrance → [Approach...] → [Boss → Exit Group]`) with optional dead-end sidearms, strict no-bypass routing (`MustTraverse`), and an atomic `boss_exit.tres` `RoomGroup` placing the Exit behind one physical door reserved from random mechanical key locks. Boss-kill unlock logic on that door is deferred; this plan exposes the clean seam (`RoomRegion.ReserveAccess`, `RoomRegion.SlotId`, `Floor.Doors`).

**Shared baseline change:** Loose items, access keys, and wall-backed chests become eligible in both rooms and corridors across all profiles; scheduled fountains stay room-only.

**Delivery boundary:** The new profiles (`labyrinth.tres`, `dense.tres`, `linear_boss.tres`) remain unwired from live `Realm` progression initially and are previewed in [RoomGenerationHarness](../../scenes/debug/rooms/RoomGenerationHarness.cs) and verified via GdUnit4 sweeps. `level.tscn` switches to `floor_profiles/packed.tres` (preserving identical live gameplay behavior); `SimpleRoomLayout` is retained for now via `floor_profiles/simple.tres`.

---

## Naming and taxonomy alignment

No backward-compatibility shims are retained; all callers, `.tres`/`.tscn` files, docs, and tests migrate together:

| Current / Draft | Target | Location / Notes |
| --- | --- | --- |
| `AssembledRoomFactory` | **`RoomPool`** | Move from `scenes/levels/generators/factories/` to `scenes/levels/generators/assembly/RoomPool.cs`; matches `room_pool.tres`, `LootPool`, `SpellPool`, `AppearancePool`. Rename `EntranceTypes`/`ExitTypes`/`RoomTypes`/`AllTypes` → **`Entrances`**, **`Exits`**, **`Rooms`**, **`AllDefinitions`** (`RoomDefinition[]`). |
| `generators/factories/MobFactory.cs` | **`encounters/MobFactory.cs`** | Move to `scenes/levels/encounters/` beside `RealmEncounterFactory`; delete empty `generators/factories/`. |
| `LevelAccessProfile` | **`FloorAccessProfile`** | Rename in `scenes/levels/access/FloorAccessProfile.cs`; update `garrison_access.tres`; align `Validate()` to `string[]` + `ValidateOrThrow()`. |
| `scenes/levels/rooms/groups/` | **`scenes/levels/rooms/prop_groups/`** | Rename directory and update all `.tres`/`.md` paths (`PropGroup` & `SeatedTable` assets); mirrors `props/` vs. `room_types/` + new **`room_groups/`**. |
| `FloorProfile.Corridors` (draft) | **`FloorProfile.Connector`** | Avoids colliding with `Realm.Corridors` (`DungeonTileFactory`). |
| `RoomGenerationHarness.Factory` | **`RoomGenerationHarness.Pool`** | Matches `RoomPool`. |
| `Boss` vs. `Gatekeeper` | **`Boss`** for new floor slots/presets | Use `SlotId = "boss"`, `rooms/room_groups/boss_exit.tres`, `floor_profiles/linear_boss.tres` (matching `MonsterArchetype.Boss` and `DamageSourceFlags.Boss`); retain existing `Gatekeeper*` encounter/balance symbols (`GeneratedFloor.GatekeeperSite`, `RealmEncounterFactory`, `scripts/balance/`) untouched in this scope. |

---

## Core seams and types

### 1. `MapData` edge walls and shared wall truth

`scenes/levels/generators/MapData.cs` stores corridor-to-corridor walls in a flat 4-bit `byte[,]` `_sealedEdges` (`N=1, E=2, S=4, W=8`) alongside a parallel 4-bit `byte[,]` `_connectorMasks` mirror for open connector directions—eliminating tuple-hash and LINQ/Dictionary allocations on `FloorDiscovery`'s 10 Hz DDA raycast hot path.

- `SealCorridorEdge(Vector2I tile, Vector2I direction)` — sets `(tile, direction)` and, when the neighbor is in bounds, `(tile + direction, -direction)` symmetrically (cluster-local perimeter bridges may seal against out-of-bounds edges).
- `IsCorridorEdgeSealed(int x, int y, Vector2I direction)` — bit test on `_sealedEdges[x, y]`.
- `RequiresInteriorWall(int x, int y, Vector2I direction)` — existing room/corridor/connector checks (via `_connectorMasks`) plus `IsCorridorEdgeSealed(x, y, direction)` when both sides are `Corridor` (or on cluster maps).
- `HasWallToward(int x, int z, Vector2I direction)` — single source of truth (`!IsWithinBounds(nx, ny) || IsWallOrEmpty(nx, ny) || RequiresInteriorWall(x, z, direction)`), replacing duplicated checks in `MapGenerator.PlaceWalls`, `CorridorBlueprint`, `MapGenerator.Loot`, `GeneratedFloorSweepTest`, and `LootPlacementTest`.
- `OpenNeighborCount(int x, int z)` — counts cardinal neighbors where `!HasWallToward(x, z, dir)` and `IsWalkable(nx, ny)` (e.g., `1` identifies maze dead ends).
- `Intersects(MapData roomMap, Vector2I placement, int buffer = 1)` — parameterizes the existing 1-tile Chebyshev room buffer.
- `Stamp(MapData source, Vector2I offset)` — stamps non-empty tiles, connector directions, and sealed edges onto the target map (shared by `RoomLayoutStrategy`, `RestampAll`, and `RoomAssembler.AssembleCluster`).
- `Turned()` — rotates `_sealedEdges` nibbles via 1-bit cyclic shift alongside `TurnTile` and `TurnDirection`.
- `SetTile(...)` / `ResetToBorderedEmpty()` — `ResetToBorderedEmpty()` calls `Array.Clear` on `_sealedEdges` and `_connectorMasks`; overwriting a tile clears stale sealed bits symmetrically.

### 2. Composite room definitions (`RoomDefinition`, `RoomType`, `RoomGroup`)

Pure data `Resource`s (assembly stays in `RoomAssembler`, not inside `Resource` subclasses):

```csharp
[Tool, GlobalClass]
public abstract partial class RoomDefinition : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export(PropertyHint.Range, "0.01,10,0.01")] public float Weight { get; set; } = 1f;
	[Export] public string[] Realms { get; set; } = [];
	public abstract string[] Validate();
}

// Existing RoomType inherits RoomDefinition (moves Id, Weight, Realms to base).
public partial class RoomType : RoomDefinition { ... }

[Flags]
public enum RoomObligations { None = 0, MustExist = 1, MustTraverse = 2 }

public enum RoomPoolSlot { Standard, Entrance, Exit }

[Tool, GlobalClass]
public partial class RoomGroupMember : Resource
{
	// Empty Options draws from PoolSlot in the active realm's RoomPool.
	[Export] public RoomDefinition[] Options { get; set; } = [];
	[Export] public RoomPoolSlot PoolSlot { get; set; } = RoomPoolSlot.Standard;
	// -1 for member 0 (root); 0 <= AttachToIndex < i for i > 0 (acyclic tree by construction).
	[Export] public int AttachToIndex { get; set; } = -1;
	[Export] public string SlotId { get; set; } = "";
	[Export] public RoomObligations Obligations { get; set; } = RoomObligations.None;
	[Export] public bool AllowExternalDoorways { get; set; } = true;
	[Export] public bool AllowSecretDoors { get; set; } = true;
	[Export(PropertyHint.Range, "-1,1,0.05")] public float DoorChanceOverride { get; set; } = -1f;
	[Export(PropertyHint.Range, "-1,1,0.05")] public float FrameChanceOverride { get; set; } = -1f;
	[Export] public bool ReserveAccess { get; set; } = false;
}

[Tool, GlobalClass]
public partial class RoomGroup : RoomDefinition
{
	public const int MaxNestingDepth = 2;
	[Export] public RoomGroupMember[] Members { get; set; } = [];
	[Export(PropertyHint.Range, "1,8")] public int Attempts { get; set; } = 4;
	public override string[] Validate(); // Checks non-empty tree, valid AttachToIndex, realm subset, cycle/depth <= 2.
}
```

### 3. Room assembly, `RoomCluster`, and `RoomPool`

- **`RoomAssemblyOptions` (`scenes/levels/generators/assembly/RoomAssembler.cs`):** Add optional overrides (replacing the duplicate pool-copy helpers in `tests/TestFactories.cs` and `tests/TestFactories.Level.cs`):
  - `public bool? AllowSecretDoors { get; init; }`
  - `public float? DoorChance { get; init; }`
  - `public float? FrameChance { get; init; }`
  - `public float? SecretDoorChance { get; init; }`
  - `public SecretDetectionProfile SecretDetection { get; init; }`
  - `public int? MinDoorways { get; init; }`
  - `public int? MaxDoorways { get; init; }`
  - `public LargeActorFitProfile RequiredFit { get; init; }`
  - **Boss clearance check at assembly:** When `RequiredFit != null`, `RoomBlueprint`'s `ClearanceGrid` initializes `maxClearance = Mathf.Max(ActorRadius, RequiredFit.MinimumClearance)` (while `WalkabilityGrid` still evaluates reachability at `ActorRadius`), and `RoomAssembler.TryBuild` rejects the candidate inside the room's own `Attempts` loop if no reachable floor point satisfies `RequiredFit.MinimumClearance`.
- **`ClusterMember` & `RoomCluster` (`scenes/levels/generators/RoomCluster.cs`, replaces `RoomPlacement`):**
  - `public sealed record ClusterMember(Room Room, Vector2I Offset, string SlotId, RoomObligations Obligations, bool ReserveAccess);`
  - `public sealed class RoomCluster` holds `MapData Map`, `IReadOnlyList<ClusterMember> Members`, `Rotate(int steps)` (turns each `Room`, updates offsets via `MapData.TurnTile`, rotates `Map` via `MapData.Turned()`), `DetachRooms()` (transfers ownership to `FloorContent`), and idempotent `Free()` (immediately `Free()`s unparented `Room` nodes on failed placements or backtracking—zero node leaks).
  - External connectors are derived from `Map` (connector tiles whose open direction does not lead to an internal `Corridor` tile); internal bridges are `MapTile.Corridor` tiles in `cluster.Map`.
- **`RoomAssembler.AssembleCluster(...)`:** Assembles a leaf `RoomType` into a 1-member `RoomCluster`, or builds a `RoomGroup` tree incrementally:
  - Assembles each member with merged `FloorProfile` + `RoomGroupMember` options (`AllowExternalDoorways == false` clamps `MaxDoorways` to the member's incoming/outgoing link count).
  - Joins each child cluster across a 1-tile `MapTile.Corridor` bridge with both perpendicular bridge edges sealed (`SealCorridorEdge`), enforcing `MapData.Intersects(..., buffer: 1)` between members and rejecting alignments where an unlinked doorway faces into another member's 1-tile buffer.
  - Precomputes `MapData[4]` orientations before searching `(connectorParent, connectorChild, rotation)` pairings, calling `child.Rotate(...)` at most once on success and `Free()`ing all owned `Room`s on retry/failure.
- **`RoomPool` (`scenes/levels/generators/assembly/RoomPool.cs`):**
  - `[Export] public RoomDefinition[] Entrances { get; set; } = [];`
  - `[Export] public RoomDefinition[] Exits { get; set; } = [];`
  - `[Export] public RoomDefinition[] Rooms { get; set; } = [];`
  - `public RoomDefinition[] AllDefinitions => [.. Rooms, .. Entrances, .. Exits];`
  - `public RoomPool ForRealm(string realm)`
  - `CreateEntrance`, `CreateExit`, `CreateStandard`, and `CreateFrom(RoomDefinition, ...)` accept `RoomAssemblyOptions` and `RoomPool` context (for nested group pool picks) and return `RoomCluster`.

### 4. `RoomRegion`, `FloorPlan`, `GeneratedFloor`, and `FloorProfile`

- **`RoomRegion` (`scenes/levels/RoomRegion.cs`):** Add `SlotId` (`string`), `Obligations` (`RoomObligations`), `ReserveAccess` (`bool`), `ClusterId` (`int`).
- **`FloorPlan` (`scenes/levels/generators/FloorPlan.cs`):** Pure data record returned by `RoomLayoutStrategy.GenerateLayout`:
  - `List<(RoomCluster Cluster, Vector2I Position)> Placements`
  - `List<(int FromRoomIndex, int ToRoomIndex)> ApprovedEdges` (empty for unconstrained layouts)
  - `HashSet<Vector2I> ProtectedCorridors` (bridge tiles stamped from placed clusters)
  - `void FreeUnplaced()` (safety cleanup if generation aborts before `PlaceClusters`)
- **`FloorBuildContext`:** Plain C# record passed into layouts: `(RoomPool Pool, FloorProfile Profile, EncounterPlan Encounter, uint MaxRooms, RandomNumberGenerator Rng)`.
- **`FloorProfile` (`scenes/levels/generators/FloorProfile.cs`, presets under `scenes/levels/generators/floor_profiles/`):**
  ```csharp
  [Tool, GlobalClass]
  public partial class FloorProfile : Resource
  {
  	[Export] public RoomLayoutStrategy Layout { get; set; }
  	[Export] public CorridorConnectorStrategy Connector { get; set; }
  	[Export] public FloorAccessProfile Access { get; set; }

  	[ExportGroup("Rooms")]
  	[Export] public bool AllowSecretDoors { get; set; } = true;
  	[Export(PropertyHint.Range, "-1,1,0.05")] public float DoorChanceOverride { get; set; } = -1f;
  	[Export] public RoomDefinition GoalOverride { get; set; }

  	[ExportGroup("Placement")]
  	[Export(PropertyHint.Range, "0.1,10,0.1")] public float DeadEndLootWeight { get; set; } = 1f;
  	[Export(PropertyHint.Range, "0,5,0.1")] public float TrapDensityScale { get; set; } = 1f;

  	public string[] Validate();
  	public void ValidateOrThrow();
  }
  ```
  *(Authored Variant-safe fields only; `-1f` sentinel means defer to each `RoomType.DoorChance`. When `GoalOverride` is set, e.g. `boss_exit.tres` on `linear_boss.tres`, it replaces `RoomPool.Exits` for that floor without polluting `room_pool.tres`.)*
- **`FloorAccessProfile` (`scenes/levels/access/FloorAccessProfile.cs`):** Retains `Locks`, `MaximumLockedRooms`, `MaximumLockedDoors`, `LockedRoomChance`; adds:
  - `[Export] public bool AllowLockingExit { get; set; } = false;`
  - `[Export] public bool AllowLockChains { get; set; } = false;`
  - `public string[] Validate()` + `ValidateOrThrow()`
  - **Contract:** Never locks Entrance, Gatekeeper room, `ReserveAccess == true` rooms (such as `boss_exit`'s Exit door), or `MustTraverse` rooms that would cut progression without a reachable key. When `AllowLockChains == true`, locks rooms sequentially one tier at a time, placing each lock's key(s) in the region reachable *before* that lock (`AccessSolutionValidator.Reachable` + `ReachableWithoutConnectors`), guaranteeing acyclic solvability.
- **`MapGenerator` (`scenes/levels/generators/MapGenerator.cs`, `.Access.cs`, `.Loot.cs`):**
  - Replaces `RoomLayout`, `CorridorConnector`, and `AccessProfile` exports with `[Export] public FloorProfile Profile { get; set; }` (wired to `floor_profiles/packed.tres` in `level.tscn`).
  - Retains `MapWidth`, `MapDepth`, `MaxRooms`, `MinChests..MaxLooseItems`, `ChestScene`, `TrapScene`, `LooseItemScene`, `LooseItems`, `Fountains`, `MobFactory`, `Rooms : RoomPool`, `Realms` on `MapGenerator` (preserving `scripts/balance/content.py` defaults, run-scoped fountains, and test isolation).
- **`GeneratedFloor` (`scenes/levels/generators/GeneratedFloor.cs`):**
  - Keep `GatekeeperSite` and `GatekeeperSpawn`; add no parallel `BossRoomId`/`BossExitDoors`/`RoomRoles` arrays (read `SlotId`/`Obligations`/`ReserveAccess` directly from `Rooms`).
  - `TryFindLargeEncounterPosition(Vector3 reference, float minimumRadius, float preferredRadius, out Vector3 position, Func<int, bool> allowedRoom = null)` — iterates matching room tiles (excluding `SlotId == "exit"` and `SlotId == "entrance"` by default; restricted to `SlotId == "boss"` when a `"boss"` room exists).
  - `TryPlacePackMember(...)` — checks `DungeonDiscovery.HasSight` (with `!Map.RequiresInteriorWall`) between `anchor` and `candidate` so corridor monster packs never spawn across thin sealed corridor walls.

### 5. Layout and corridor strategies

- **`RoomLayoutStrategy` (`scenes/levels/generators/layouts/RoomLayoutStrategy.cs`):**
  - `public abstract FloorPlan GenerateLayout(MapData map, FloorBuildContext context);`
  - Provides protected shared helpers: `FitsWithinPlayableBounds`, `RestampAll`, `GetExternalConnectors`, `CountAlignedConnectors`, `TryFindBestPlacementAcrossRotations` (computes pure `MapData[4]` orientations via `.Turned()`, scores all candidate placements on lightweight `MapData` arrays, and calls `cluster.Rotate(chosen.Steps)` at most once on winner selection), and `CreateBalancedStandardRoom` / `RecordRoomUse` keyed by `RoomDefinition`.
  - **4 subclasses:**
    1. `SimpleRoomLayout` — retained for now, updated to `RoomCluster` / `FloorPlan` and `RoomDefinition`.
    2. `PackedRoomLayout` — serves both `packed.tres` and `dense.tres`; updated to `RoomCluster` / `FloorPlan` and pure `MapData[4]` search (with optional `AllowOverlapFreeEdges` / existing `RoomGap = 1`).
    3. `SparseRoomLayout` — places Entrance, geometrically distant Exit (never evicted), and `MinDedicatedRooms..MaxDedicatedRooms` spaced rooms with configurable `MinRoomGap` (for `labyrinth.tres`).
    4. `LinearRoomLayout` — places spine `Entrance → [MinApproach..MaxApproach] → Goal` (`GoalOverride` or Exit) plus `[MinSidearms..MaxSidearms]` 1-doorway dead-end sidearms with a bounded backtrack cap (default 24), emitting `ApprovedEdges` and marking spine rooms `RoomObligations.MustTraverse`. Passes `RequiredFit = encounter.GatekeeperFit` when assembling the `"boss"` slot (either via `GoalOverride`'s `boss_exit.tres` member or directly).
- **`CorridorConnectorStrategy` (`scenes/levels/generators/connectors/CorridorConnectorStrategy.cs`):**
  - `public abstract void ConnectRooms(MapData map, FloorPlan plan, IReadOnlyList<RoomRegion> rooms, RandomNumberGenerator rng);`
  - Pure-C# routing/carving helpers separated from thin `[GlobalClass]` wrappers so layout/corridor algorithms are unit-tested without booting Godot.
  - **2 subclasses:**
    1. `AStarCorridorConnector`: marks `plan.ProtectedCorridors` solid; builds initial components grouped by `RoomRegion.ClusterId` (so pre-linked `RoomGroup` rooms don't split into separate components); routes only external connectors. When `plan.ApprovedEdges` is non-empty, routes strictly edge-by-edge in order and seals corridor-to-corridor edges (`SealCorridorEdge`) between unrelated routed corridors so separate branches never fuse into a bypass; otherwise runs the existing spanning tree + remaining doorways.
    2. `MazeCorridorConnector`: exports `CorridorFillScope FillScope` (`AllPlayable` for `labyrinth.tres`, `InteriorGapsOnly` for `dense.tres`), `WindingChance`, `LoopChance`, `DeadEndPruneChance`.
       - `AllPlayable`: carves a 1-tile-wide maze across all empty playable tiles via randomized growing-tree (`WindingChance`), sealing non-carved shared corridor edges (`SealCorridorEdge`); links every external room doorway into the maze; opens controlled loops (`LoopChance`) while never breaching `ProtectedCorridors`; runs O(W×H) queue-based dead-end pruning (`DeadEndPruneChance`) preserving connected doorways and a configurable minimum dead-end count.
       - `InteriorGapsOnly`: connects every doorway first (A*), floods exterior void from the map border (`HasWallToward` / empty walk), identifies enclosed interior `Empty` tiles and 1-tile crevices pinched inside the cluster hull, and carves accessible 1-tile corridors through them with `SealCorridorEdge`.

---

## Shared fixes and performance guardrails

1. **Opaque symmetric corridor partition walls (`BoundaryWallBuilder.cs` + `MapGenerator.cs`):**
   - `BoundaryWallBuilder.Build` records whether an edge was added from two walkable neighbors (`isPartition`) and passes `isPartition` into `pickStraight(Vector3I cell, bool isPartition)`. Corridor partitions (`isPartition == true`) pick only opaque symmetric `WallStyle.Straight` pieces (never see-through `wall_open_scaffold`/`wall_gated`/`wall_archedwindow_gated` or one-sided face accents like `wall_slimy_*`/`wall_mossy_*`).
2. **Every `HasWallToward` and cross-wall seam fixed uniformly:**
   - `WallRun.Find` (`RoomBlueprint.cs`) accepts an optional `Func<Vector2I, Vector2I, bool> canStep` predicate (`(tile, along) => !Map.HasWallToward(tile.X, tile.Y, along)` in `CorridorBlueprint`) so corridor wall runs break cleanly at perpendicular sealed maze walls.
   - `CorridorBlueprint`: measure all walls via `Map.HasWallToward` (door proximity sets `Decoratable = false` rather than dropping the wall from `_walls`), use `!Map.HasWallToward` in `ComputeJunctions` and `ComputeSpans` (also checking along-direction walls so parallel sealed dead ends don't merge into invalid spans), build an O(1) `(Tile, Outward) -> WallRun` lookup index, and scope light/door/prop proximity so opposite sides of a 1-unit wall don't suppress each other.
   - `MapGenerator.Loot.cs`: update `GetSolidPropTiles(bool includeCorridors)` to call `Map.HasWallToward(x, z, offset)` (fixing corridor-backed room walls too) and allow corridor chests (`includeCorridors: true`, while `MapGenerator.Fountains.cs` passes `includeCorridors: false`); update `GetLooseItemCandidates(bool includeCorridors = true)` for loose loot and access keys; weight candidate selection via `Weighted.PickIndex` using `Profile.DeadEndLootWeight` (`OpenNeighborCount == 1`) and scale trap count by `Profile.TrapDensityScale`.
   - `FloorDiscovery.cs`: skip revealing closed-door x-ray indicators across `RequiresInteriorWall(tile.X, tile.Y, offset)`.
   - `GeneratedFloor.cs`: validate `DungeonDiscovery.HasSight` (with `!Map.RequiresInteriorWall`) in `TryPlacePackMember` so corridor packs never split across sealed walls.
3. **Dense Labyrinth wall and collision scaling:**
   - Cache `(Aabb Bounds, string Name)` per `MeshLibrary` item ID and spatially bucket pieces/frames into 4×4-unit cells in `BoundaryWallBuilder.Build` and `WallFootprints.FindOverlap` (reducing O(P²) to O(P) on 500–1,000+ wall labyrinths).
   - Cache local convex hulls per `(MeshLibrary item, shapeIndex)` in `CollisionOutlines.FromGridMap` and reuse static `WallGridMap`/`DecorationGridMap` outlines from `BuildWalkability` inside `BuildNavigation`.
   - Pre-filter corridor chest candidates to dead ends (`OpenNeighborCount == 1`) or ≥2-open-side tiles, cache `doorClearances` and `ChestScene` outlines once per `PlaceLoot`, and add localized `ClearanceGrid.SaveRegion(Rect2)`/`RestoreRegion` (avoiding 412 KB full-grid clones per rejected chest or connector probe).
   - Build `GeneratedFloor._decoratedTiles` lazily once decoration finishes and hoist `RefreshPropTiles()` out of `GetSolidPropTiles` / `GetLooseItemCandidates` / `IsFootprintClear` loops; shuffle loose candidate lists before lazy `IsColumnObstructed` Jolt probes in `PlaceGeneratedLocks`.

---

## Orchestration and ordering invariants (`MapGenerator.GenerateMapAttempt`)

```text
MapGenerator.GenerateMapAttempt(includeGameplay)
├─ Reset()                                   // Profile.ValidateOrThrow(); Map.ResetToBorderedEmpty(); resolve CurrentRealm & _realmPool
├─ Floor.Encounter = MobFactory.PlanEncounters(DungeonDepth)
│                                            // Invariant: planned BEFORE layout so RoomAssembler validates GatekeeperFit at room assembly
├─ FloorPlan = Profile.Layout.GenerateLayout(Map, context)
│                                            // Invariant: scores pure MapData[4]; cluster.Rotate() once on win; cluster.Free() on discard
├─ PlaceClusters(FloorPlan)                  // Invariant: cluster.DetachRooms() in deterministic order -> FloorContent; sets RoomRegion metadata
├─ if (includeGameplay): FindProgressionAnchors()
│                                            // Invariant: fails fast for retry if Arrival or Descent is missing
├─ ConnectRooms(FloorPlan)
│   ├─ Profile.Connector.ConnectRooms(Map, FloorPlan, Floor.Rooms, connectorRng)
│   ├─ Verify every connector opens onto Corridor (throw on broken room doorway)
│   ├─ Validate MustTraverse obligations on realized MapData via AccessSolutionValidator.Reachable
│   │                                        // Invariant: returns false (triggers 8-attempt retry) if any unintended bypass reaches Descent
│   ├─ PlaceCorridors()
│   └─ PlaceWalls()                          // Passes isPartition to draw opaque symmetric straight pieces on corridor partitions
├─ DecorateCorridors() -> PlaceOcclusion() -> Floor.SolidWalls
├─ if (!includeGameplay): Floor.Freeze(); return true
├─ BuildWalkability()                        // Caches static Wall/Decoration SolidOutlines for BuildNavigation
├─ ResolveEncounters()                       // Restricts TryFindLargeEncounterPosition to SlotId=="boss" if present; excludes Exit/Arrival
├─ PlaceAccess()                             // FloorAccessProfile: AllowLockingExit, AllowLockChains (stepwise Reachable + ReachableWithoutConnectors); skips ReserveAccess & MustTraverse; records ConnectionId & baselines
├─ PlaceLoot()                               // Fountains (room-only) -> Chests/Traps/LooseItems (rooms + corridors, weighted by DeadEndLootWeight & TrapDensityScale)
├─ BuildNavigation()                         // Reuses cached static wall outlines + refreshed FloorContent solids + door gates
└─ GenerateEnemySpawnPoints() -> Floor.Freeze()
```

**Failure & cleanup contract:** On any failed attempt (`return false` or thrown exception before commit), unplaced `RoomCluster`s are immediately `Free()`d via `FloorPlan.FreeUnplaced()`, and placed `FloorContent` children are cleared on the next `Reset()`. Exhausting 8 attempts throws `InvalidOperationException`, matching existing `Level.Generate()` failure handling.

---

## Affected file tree

```text
docs/
├── README.md                                          (~ link new spec)
└── specs/
    └── floor-generators-plan.md                       (+ this spec)

scenes/levels/
├── RoomRegion.cs                                      (~ SlotId, Obligations, ReserveAccess, ClusterId)
├── FloorDiscovery.cs                                  (~ skip door x-ray indicators across RequiresInteriorWall)
├── level.tscn                                         (~ assign Profile = floor_profiles/packed.tres; remove old RoomLayout/CorridorConnector/AccessProfile)
├── access/
│   ├── LevelAccessProfile.cs -> FloorAccessProfile.cs (~ rename + AllowLockingExit, AllowLockChains, string[] Validate())
│   └── garrison_access.tres                           (~ script/path updated to FloorAccessProfile)
├── encounters/
│   └── MobFactory.cs                                  (+ moved from generators/factories/MobFactory.cs)
├── rooms/
│   ├── room_pool.tres                                 (~ script/path updated to RoomPool, Entrances/Exits/Rooms arrays)
│   ├── groups/ -> prop_groups/                        (~ rename folder; update 4 files inside + 18 room_types/*.tres + README refs)
│   └── room_groups/
│       └── boss_exit.tres                             (+ Boss -> 1-tile sealed bridge -> locked 1-doorway Exit RoomGroup)
└── generators/
    ├── MapData.cs                                     (~ 4-bit _sealedEdges & _connectorMasks, SealCorridorEdge, HasWallToward, OpenNeighborCount, Stamp, Intersects buffer, Turned)
    ├── FloorProfile.cs                                (+ FloorProfile Resource + Validate()/ValidateOrThrow())
    ├── FloorPlan.cs                                   (+ FloorPlan, FloorBuildContext)
    ├── RoomCluster.cs                                 (+ RoomCluster, ClusterMember; replaces RoomPlacement.cs)
    ├── RoomPlacement.cs                               (- removed, replaced by RoomCluster)
    ├── MapGenerator.cs                                (~ Profile export, early PlanEncounters, MustTraverse check, PlaceClusters, isPartition walls, hull/outline reuse)
    ├── MapGenerator.Access.cs                         (~ FloorAccessProfile, AllowLockingExit, AllowLockChains, ReserveAccess, lazy IsColumnObstructed)
    ├── MapGenerator.Loot.cs                           (~ HasWallToward, corridor chests/loose loot, DeadEndLootWeight, TrapDensityScale, outline caching)
    ├── MapGenerator.Fountains.cs                      (~ pass includeCorridors: false to GetSolidPropTiles)
    ├── MapGenerator.Walkability.cs                    (~ share static wall outlines with Navigation; localized ReachableWithoutConnectors)
    ├── MapGenerator.Navigation.cs                     (~ reuse cached static wall outlines)
    ├── GeneratedFloor.cs                              (~ allowedRoom predicate in TryFindLargeEncounterPosition; HasSight in TryPlacePackMember; lazy _decoratedTiles)
    ├── BoundaryWallBuilder.cs                         (~ isPartition callback, MeshLibrary bounds cache, 4x4 spatial bucketing)
    ├── WallFootprints.cs                              (~ spatial bucketing in FindOverlap; item bounds/name cache)
    ├── floor_profiles/
    │   ├── packed.tres                                (+ current default profile wired in level.tscn)
    │   ├── simple.tres                                (+ SimpleRoomLayout + AStarCorridorConnector)
    │   ├── labyrinth.tres                             (+ SparseRoomLayout + MazeCorridorConnector(AllPlayable) + lockable exit/chains)
    │   ├── dense.tres                                 (+ PackedRoomLayout + MazeCorridorConnector(InteriorGapsOnly))
    │   └── linear_boss.tres                           (+ LinearRoomLayout + AStarCorridorConnector(ApprovedEdges) + boss_exit.tres)
    ├── factories/                                     (- removed directory after moving AssembledRoomFactory & MobFactory)
    ├── assembly/
    │   ├── RoomDefinition.cs                          (+ abstract base Resource for RoomType & RoomGroup)
    │   ├── RoomType.cs                                (~ inherit RoomDefinition)
    │   ├── RoomGroupMember.cs                         (+ tree member Resource + RoomObligations + RoomPoolSlot)
    │   ├── RoomGroup.cs                               (+ composite RoomDefinition + depth/cycle/realm validation)
    │   ├── RoomPool.cs                                (+ renamed/moved from factories/AssembledRoomFactory.cs; Entrances/Exits/Rooms)
    │   ├── RoomAssembler.cs                           (~ extended RoomAssemblyOptions + RequiredFit check + AssembleCluster)
    │   ├── RoomBlueprint.cs                           (~ WallRun.Find canStep; maxClearance support for RequiredFit)
    │   └── CollisionOutlines.cs                       (~ cached local convex hulls per MeshLibrary item/shape)
    ├── corridors/
    │   └── CorridorBlueprint.cs                       (~ HasWallToward, Decoratable=false near doors, O(1) WallRun lookup, span wall breaks, same-side spacing)
    ├── layouts/
    │   ├── RoomLayoutStrategy.cs                      (~ FloorPlan signature + lifted protected helpers + MapData[4] rotation search)
    │   ├── SimpleRoomLayout.cs                        (~ updated to FloorPlan/RoomCluster)
    │   ├── PackedRoomLayout.cs                        (~ updated to FloorPlan/RoomCluster + pure MapData[4] search)
    │   ├── SparseRoomLayout.cs                        (+ entrance, distant exit, MinDedicatedRooms..MaxDedicatedRooms, MinRoomGap)
    │   └── LinearRoomLayout.cs                        (+ spine Entrance->Approach->Goal + dead-end Sidearms + ApprovedEdges + backtrack cap)
    └── connectors/
        ├── CorridorConnectorStrategy.cs               (~ FloorPlan + RNG signature + lifted pure-C# helpers)
        ├── AStarCorridorConnector.cs                  (~ ClusterId components, ProtectedCorridors, ApprovedEdges isolated routing)
        └── MazeCorridorConnector.cs                   (+ AllPlayable maze & InteriorGapsOnly gap-fill + pure-C# core)

scenes/levels/navigation/
└── ClearanceGrid.cs                                   (~ localized SaveRegion/RestoreRegion for transactional prop/access probes)

scenes/debug/rooms/
└── RoomGenerationHarness.cs                           (~ Pool rename, DebugCatalog FloorProfile picker, RoomGroup cluster preview)

.agents/skills/godot-mcp/scripts/
└── render_level_topdown.gd                            (~ optional [profile.tres] CLI arg)

tests/
├── MapDataTileTest.cs                                 (~ unit tests for SealCorridorEdge, HasWallToward, Stamp, Turned, Reset)
├── FloorAlgorithmTest.cs                              (+ plain-C# tests for maze carving, gap-fill, linear topology, cluster MapData join math)
├── RoomGroupTest.cs                                   (+ Validate() acyclicity/depth + AssembleCluster + Free() cleanup tests)
├── GarrisonRoomPoolTest.cs                            (~ RoomPool property/method renames)
├── DoorAccessTest.cs                                  (~ FloorProfile/FloorAccessProfile + lockable exit & chain solvability tests)
├── DiscoveryAndAccessBalanceTest.cs                   (~ LevelAccessProfile -> FloorAccessProfile)
├── LootPlacementTest.cs                               (~ HasWallToward check; verify corridor chests/loot stay reachable & wall-backed)
├── CorridorDecorationTest.cs                          (~ verify interior sealed-corridor walls decorate cleanly)
├── WallIntegrityTest.cs                               (~ verify double-sided partition straight walls)
├── GeneratedFloorSweepTest.cs                         (~ FloorProfile migration + tiered 5-profile sweep across all 3 realms)
└── TestFactories.cs / TestFactories.Level.cs          (~ FloorProfile configuration; retire duplicate-pool helpers in favor of overrides)
```

*(Also update `RoomPool`/`FloorAccessProfile`/`prop_groups` mentions in `scenes/levels/README.md`, `scenes/levels/generators/assembly/README.md`, `docs/door-access.md`, and `docs/specs/room-owned-secrets-plan.md` during implementation.)*

---

## Delivery slices

### Slice 1: Taxonomy renames and `MapData` edge-wall substrate
- **Changes:** Rename `AssembledRoomFactory` → `RoomPool`, move `MobFactory.cs` → `scenes/levels/encounters/`, rename `LevelAccessProfile` → `FloorAccessProfile`, rename `rooms/groups/` → `rooms/prop_groups/`. Add 4-bit `_sealedEdges` / `_connectorMasks`, `SealCorridorEdge`, `HasWallToward`, `OpenNeighborCount`, `Stamp`, `Intersects(buffer)` to `MapData`. Apply `BoundaryWallBuilder` `isPartition` + item-bounds caching + 4×4 bucketing, `WallFootprints.FindOverlap` bucketing, `WallRun.Find` `canStep`, `CorridorBlueprint` `HasWallToward` / O(1) index / span fixes, `FloorDiscovery` x-ray wall guard, `GeneratedFloor.TryPlacePackMember` `HasSight`, and `CollisionOutlines` hull cache.
- **Verification:** `dotnet build "Rogue Gauntlet.sln"` + `dotnet build "Rogue Gauntlet.csproj" -c ExportRelease`; headless Godot `--import` and `inspect_resource.gd` on `room_pool.tres`, `garrison_access.tres`, and sample `prop_groups/` + `room_types/` resources; plain-C# `MapDataTileTest` for `_sealedEdges`, `Turned()`, and `HasWallToward`; existing `WallIntegrityTest`, `CorridorDecorationTest`, `WallClipTest`, `DoorAccessTest`, and `GeneratedFloorSweepTest`.
- **Remaining:** New generators, `RoomGroup`, and `FloorProfile` do not exist yet; live gameplay unchanged.

### Slice 2: `RoomDefinition`, `RoomGroup`, `RoomCluster`, and early encounter planning
- **Changes:** Add `RoomDefinition`, `RoomGroupMember`, `RoomGroup`, `RoomCluster` (replacing `RoomPlacement`), `RoomRegion` metadata (`SlotId`, `Obligations`, `ReserveAccess`, `ClusterId`). Update `RoomType` and `RoomPool` (`Entrances`, `Exits`, `Rooms`, `AllDefinitions`). Extend `RoomAssemblyOptions` (overrides + `RequiredFit`) and implement `RoomAssembler.AssembleCluster` + pure `MapData[4]` placement helpers + deterministic `Free()`. Move `MobFactory.PlanEncounters` before layout in `MapGenerator`. Author `scenes/levels/rooms/room_groups/boss_exit.tres`.
- **Verification:** `dotnet build`; Godot `--import` + `inspect_resource.gd` on `boss_exit.tres` and `room_pool.tres`; plain-C# and runtime `RoomGroupTest` (acyclicity, depth limit, 1-tile sealed bridge assembly, doorway isolation, leak-free `Free()`, and `boss_exit.tres` assembling across all 3 realms).
- **Remaining:** `MapGenerator` still uses legacy `RoomLayout`/`CorridorConnector`/`AccessProfile` exports until Slice 3.

### Slice 3: `FloorProfile`, `FloorPlan`, corridor loot, and `FloorAccessProfile` extensions
- **Changes:** Add `FloorProfile` and `FloorPlan`; update `RoomLayoutStrategy` (`SimpleRoomLayout`, `PackedRoomLayout`) and `CorridorConnectorStrategy` (`AStarCorridorConnector`) signatures; replace `MapGenerator`'s `RoomLayout`/`CorridorConnector`/`AccessProfile` exports with `Profile`; author `floor_profiles/packed.tres` (wired in `level.tscn`) and `floor_profiles/simple.tres`. Enable corridor loose items, keys, and pre-filtered/region-saved corridor chests in `MapGenerator.Loot.cs` (`DeadEndLootWeight`, `TrapDensityScale`; fountains pass `includeCorridors: false`). Add `AllowLockingExit`, `AllowLockChains`, `ReserveAccess` / `MustTraverse` guards, and shuffled lazy `IsColumnObstructed` checks in `MapGenerator.Access.cs`. Add `allowedRoom` predicate to `GeneratedFloor.TryFindLargeEncounterPosition`. Migrate `TestFactories` (`FloorProfile` helpers, retiring `RoomPoolWithDoors`/`WithSecrets`/`WithDoorsAndSecrets`).
- **Verification:** `dotnet build` (Debug & `ExportRelease`); verify `scripts/balance/content.py` still parses `MinTraps`/`MaxTraps` cleanly; run `LootPlacementTest`, `DoorAccessTest` (plus new exit-locking & chained-key solvability cases), `FountainTest`, `GarrisonRoomPoolTest`, and `GeneratedFloorSweepTest`.
- **Remaining:** New layout/connector strategies (`SparseRoomLayout`, `LinearRoomLayout`, `MazeCorridorConnector` modes) not yet implemented.

### Slice 4: New strategies and floor profile presets
- **Changes:** Implement pure-C# core algorithms and `[GlobalClass]` wrappers for `SparseRoomLayout`, `LinearRoomLayout`, `MazeCorridorConnector` (`AllPlayable` and `InteriorGapsOnly`), and `AStarCorridorConnector` `ApprovedEdges` isolated routing + `MustTraverse` realized-map validation in `MapGenerator.ConnectRooms`. Author `floor_profiles/labyrinth.tres`, `floor_profiles/dense.tres`, and `floor_profiles/linear_boss.tres` (unwired from live `Realm`s).
- **Verification:** `dotnet build`; Godot `--import` + `inspect_resource.gd` on all `.tres` presets under `floor_profiles/`; sub-second plain-C# `FloorAlgorithmTest` verifying maze full connectivity + dead ends, Dense zero enclosed interior holes, and Linear Boss non-bypassability on realized `MapData`.
- **Remaining:** Visual harness preview and full multi-profile Godot runtime sweeps (Slice 5).

### Slice 5: `RoomGenerationHarness` preview, top-down visual checks, and 5-profile sweeps
- **Changes:** Update `RoomGenerationHarness` (`Pool` rename, `DebugCatalog.Resources<FloorProfile>` selector in floor mode, `RoomGroup` cluster preview with `BoundaryWallBuilder` bridge rendering and axis-override hiding); extend `render_level_topdown.gd` with optional `--profile <path>`; add tiered `[RequireGodotRuntime]` sweeps in `GeneratedFloorSweepTest` across all 5 `FloorProfile`s and all 3 realms checking wall gaps, piece overlaps, doorway frames, connectivity, corridor loot/chest reachability, lock/key solvability, and boss placement.
- **Verification:** Run full `dotnet test "Rogue Gauntlet.csproj" --settings .runsettings` (isolated `TMPDIR`) and inspect windowed `render_level_topdown.gd` renders for `labyrinth.tres`, `dense.tres`, and `linear_boss.tres` across realms.

---

## Open decisions and deferred work

1. **Wiring profiles into live runs:** Selecting which `FloorProfile` runs per `Realm` or depth (e.g., `linear_boss.tres` on boss depths) stays deferred until all 3 new profiles are visually verified in `RoomGenerationHarness`.
2. **Boss-kill door unlock:** `boss_exit.tres` marks the Exit `RoomRegion` with `ReserveAccess = true` and `SlotId = "exit"` so `Floor.Doors` exposes the boss exit door cleanly when boss-kill unlocking is added later.
3. **Retiring `SimpleRoomLayout` / `simple.tres`:** Retained during initial development and sweeps; delete in a small follow-up once the new profiles stabilize.
4. **Mid-corridor labyrinth doors (optional later):** Initial Labyrinth locks live on room-owned doors (`Entrance`, `Dedicated` rooms, `Exit`); standalone corridor doors on sealed edges can be added later without changing `FloorProfile`.
5. **Global `Gatekeeper*` rename (optional later):** Existing `Gatekeeper*` symbols in `scenes/levels/encounters/` and `scripts/balance/` stay untouched until a dedicated naming cleanup.

---

## Completion checklist

- Follows project taxonomy (`*Pool`, `*Profile`, `*Definition`, `*Strategy`, `prop_groups/` vs. `room_groups/`, `Boss` for new floor concepts, zero shims).
- Pure data `RoomDefinition` (`RoomType` | `RoomGroup`) keeps assembly in `RoomAssembler`, avoids recursive cycles (`AttachToIndex` tree + depth `<= 2`), and frees unparented `Room` nodes deterministically.
- `MapData` 4-bit `_sealedEdges` + `HasWallToward` drives walls, corridor decoration, loot/chest candidates, fog, pack sight checks, and topology validation from one source of truth.
- Call-stack order enforces early `PlanEncounters`, pure `MapData[4]` rotation scoring, realized `MustTraverse` validation, and `isPartition` opaque straight walls.
- Slices are independently verifiable with explicit C#, Godot `--import`/`inspect_resource.gd`, plain-C#, and GdUnit4 checks.
