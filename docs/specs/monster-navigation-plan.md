# Monster navigation on a clearance grid

**Status:** all slices are implemented, and the system is described in [level design](../level-design.md#doors-and-enemy-navigation). Playtest acceptance and the open decisions below remain.

Replacement for monster pathfinding and steering. Monsters stop using Godot's navigation mesh and `NavigationAgent3D`. Instead they follow fields computed on a clearance grid built from the floor's real collision. Current behavior is described in [Level design: doors and enemy navigation](../level-design.md#doors-and-enemy-navigation) and [Monster authoring: awareness and pursuit](../monster-authoring-design.md#awareness-and-pursuit). This plan describes only the change.

## Why

A November 2026 audit measured generated floors on seeds and depths 23/1, 7/5 and 11/8 with windowed probes.

- **One navmesh for every size.** The navmesh is baked once with `agent_radius = 1.0`, which is the Warden's capsule. Rats (0.35) and skeletons (0.75) therefore only path over about half of the unobstructed floor, and aisles narrower than 2 units between furniture are closed to them.
- **The bake reads visual meshes as well as colliders.** Loose loot, X-ray meshes and the stairs' `DebugVolume` all cut holes in the navmesh.
- **Floors have bumps that physics treats as walls.** Floor tiles use trimesh colliders built from detailed visual meshes. Their lips register as walls with ~45–52° normals, while the navmesh treats them as walkable. Most permanent stalls happened on these lips.
- **Stuck recovery never changes anything.** `NavigationComponent` reports a stall, and the chase responds by re-requesting the same destination. Every failure in the chase benchmark became permanent.
- **Doorways themselves connect.** Ordinary monsters reached targets across 1–3 doorways in 90–95% of runs.
- **The Warden freezes.** `LargeActorFitComponent` halts locomotion whenever the next step touches a 4×4 tile with a nearby prop. More than half the Wardens froze even though their path continued.
- **Paths hug walls, and fleeing barely works.** Godot's path smoothing hugs corners by design. Retreat tests three straight lines and rejects any with an obstacle on it.

## Goals

- **Sizes:** one grid serves every monster size. A monster goes wherever its body physically fits, and nowhere it doesn't.
- **Paths:** monsters keep to the middle of corridors and doorways and round corners rather than cutting them. In open space they still take direct lines.
- **Doors:** opening or closing a door changes routes immediately, with no special case for crossing a doorway.
- **Fleeing:** a fleeing monster moves away along routes that lead somewhere, not into dead ends.
- **Stalls:** a monster that stops making progress tries a different move before reporting that it is stuck.
- **Testing:** pathfinding is plain C# and can be tested headless.
- **Out of scope:** room binding for bosses stays (`StayInHomeRoom`). Releasing it later is a data change.

Not in this plan: monsters opening doors, flying or swimming movement, new authored flee behaviors such as cowardly monsters, and crowd-size tuning. Each gets a seam rather than state.

## Concepts

- **Clearance grid:** world XZ cells (default 0.5 units, matching `WalkabilityGrid.DefaultCellSize`). Each cell stores the distance from its center to the nearest solid outline within the actor height band, capped at `MaxClearance`. It is built once per floor from the same outlines as floor reachability: walls, solid decoration and static bodies, with doors excluded. Doors are recorded as gates instead.
- **Traversable:** a cell is traversable for body radius `r` when `clearance + CellSize/2 ≥ r` and its gate is open. This is the same allowance `WalkabilityGrid` uses, so generation reachability and runtime movement agree on what fits.
- **Step cost:** the cost of each step is its length × `(1 + CenterBias × w)`, where `w = clamp((r + PreferredMargin − clearance) / PreferredMargin, 0, 1)`.
  - Low clearance costs more, which is what centers routes.
  - Diagonal steps never cut a blocked corner.
- **Chase field:** a Dijkstra map of cost-to-reach the target from every traversable cell for one size class.
  - All chasers of that class share it.
  - It is computed only as far as the farthest registered requester plus a margin.
  - It is recomputed at most every `FieldRefreshInterval`, and only for size classes that have active requesters.
- **Flee field:** every cell of a chase field is seeded with `−FleeCoefficient × chase cost`, then relaxed with the same step costs. Moving downhill leads away from the threat and prefers routes with somewhere to go.
- **Point path:** a bounded A* search with the same costs, for roam, search and ring destinations.
- **Size class:** a body radius rounded up to 0.25. Rats, skeletons and the Warden each get their own fields.
- **Steering:** walk the field downhill up to `LookaheadDistance`, and aim at the farthest cell reachable in a straight line where every crossed cell is traversable. The final approach goes straight to the target once that line is clear.
  - Soft separation keeps monsters apart; they still do not physically collide with each other.
  - Leashed actors never step outside their home room.
  - Movement still goes through `MovementComponent.SetInputDirection`.

## Types and signatures

All names are proposed. Unchanged names keep their current meaning.

```csharp
// scenes/levels/navigation/NavigationProfile.cs: floor-wide tuning, authored .tres
[Tool][GlobalClass]
public partial class NavigationProfile : Resource
{
	[Export] public float CenterBias { get; set; } = 2f;
	[Export] public float PreferredMargin { get; set; } = 1f;
	[Export] public int FieldRefreshFrames { get; set; } = 12;
	[Export] public float FieldBoundMargin { get; set; } = 8f;   // cost units past the farthest requester
	[Export] public float MaxFieldCost { get; set; } = 200f;
	[Export] public float FleeCoefficient { get; set; } = 1.2f;
	[Export] public float FleeHorizon { get; set; } = 12f;
	[Export] public float LookaheadDistance { get; set; } = 3f;
	[Export] public float DirectApproachDistance { get; set; } = 4f;
	[Export] public float SeparationMargin { get; set; } = .3f;
	[Export] public int MaxPathExpansions { get; set; } = 6000;
}

// scenes/levels/navigation/ClearanceGrid.cs: plain C#, built during generation, frozen with the floor
public sealed class ClearanceGrid
{
	public ClearanceGrid(Rect2 bounds, float maxClearance = DefaultMaxClearance, float cellSize = DefaultCellSize);
	public float CellSize { get; }
	public Vector2I Size { get; }
	public void AddFloor(Rect2 area);
	public void AddObstacle(SolidOutline solid);           // ignores solids outside WalkabilityGrid's height band
	public int AddGate(Vector2[] footprint);               // returns the gate index; cells inside close with it
	public float ClearanceAt(Vector2I cell);               // 0 off the floor
	public bool IsTraversable(Vector2I cell, float radius, bool[] openGates); // null treats every gate as open
	public void Freeze();                                  // InvalidOperationException on later mutation
}

// scenes/levels/navigation/FlowField.cs: an immutable snapshot of one Dijkstra map
public sealed class FlowField
{
	public float CostAt(Vector2I cell);                    // +inf when unreached
	public bool Reaches(Vector3 point);
	public bool TryDescend(Vector2I from, out Vector2I next); // false at a local minimum
}

// scenes/levels/navigation/FloorNavigation.cs: plain C#, runtime service owned by Level
public sealed class FloorNavigation
{
	public FloorNavigation(ClearanceGrid grid, NavigationProfile profile, Func<int, bool> isGateOpen, Func<Vector3, int> roomAt);
	public NavigationProfile Profile { get; }
	public void Register(NavigationComponent agent);       // idempotent; holds no freed nodes
	public void Unregister(NavigationComponent agent);
	public FlowField ChaseField(object key, Vector3 target, float radius, Vector3 requester);
	public FlowField FleeField(object key, Vector3 threat, float radius, Vector3 requester);
	public bool TryFindPath(Vector3 from, Vector3 to, float radius, List<Vector2> waypoints); // false: unreachable or over budget, cleared
	public bool TrySteer(FlowField field, Vector3 position, out Vector3 point);
	public Vector3 RoomiestDirection(Vector3 position, float radius);
	public bool HasClearLine(Vector3 from, Vector3 to, float radius);
	public bool TryFindTraversable(Vector3 point, float radius, float searchRadius, out Vector3 result);
	public int RoomAt(Vector3 point);                      // GeneratedFloor.RoomIdAt of the containing tile
}
```

- **`FloorNavigation` contracts:**
  - It reads door state through `isGateOpen` (gate i is `GeneratedFloor.NavigationGates[i]`, every door including interior gates) at query time and turns it into a gate mask and a version. Any change invalidates cached fields. No signal subscription is needed, so door, secret-door and cell-gate changes cannot be missed.
  - Fields are read-only once returned, and callers never mutate actors through them.
  - Queries never throw for points off the grid. They report unreachable instead.
- **`Level`:** gains `[Export] NavigationProfile NavigationProfile` and a runtime `FloorNavigation Navigation`. `Navigation` is created in `Generate()` right after `Floor` and cleared with it, following the same pattern as `Discovery`.
- **`GeneratedFloor`:** gains `ClearanceGrid Clearance`, set before `Freeze()`.
- **`MapGenerator`:** `BakeNavigationMesh` becomes `BuildNavigation` at the same pipeline step, after `PlaceLoot`, so chests and fountains count. `WalkabilityRadius` stops reading the navmesh and becomes `[Export] float ReachabilityRadius = 1` (the largest ordinary body). The tile and solid-outline enumeration is shared with `CreateWalkabilityGrid`. The grid caps clearance at `ClearanceGrid.DefaultMaxClearance` (3).

```csharp
// scenes/enemies/NavigationComponent.cs: rewritten; same node, same physics-step contract (called by the state layer)
public partial class NavigationComponent : Node
{
	public void Initialize(CharacterBody3D actor, MovementComponent movement, float bodyRadius, bool stayInHomeRoom);
	public bool IsAvailable { get; }            // false outside a generated level: every query reports unreachable
	public bool CanReach(Node3D target);        // the chase field reaches this actor
	public bool CanStandAt(Vector3 point);      // body fits there (inside the home room when held); used by AI dodges
	public void Chase(Node3D target);           // per physics frame
	public bool MoveTo(Vector3 destination);    // per physics frame; false when no route; replans on a new destination, door change or stall
	public bool HasArrived { get; }
	public bool Flee(Node3D threat);            // per physics frame; false when cornered (no downhill step)
	public bool TryFindTraversable(Vector3 point, float searchRadius, out Vector3 result);
	public void Stop();
	public bool IsStuck { get; }                // set only after local recovery failed
	public void ResetStuckTracking();
}
```

Removed: `SetDestination`, `IsNavigationFinished`, `FollowPath`, `GetClosestNavPoint`, `IsReachable`, `IsCrossingDoorway`, `TrySetRetreatDestination`.

- **Progress:** measured as a drop in cost-to-go (field cost, or the remaining path length), not as displacement. Oscillating in place therefore counts as a stall.
- **Recovery:** first, side-step for a short time toward the neighbor with the highest clearance that does not raise the cost-to-go by more than one cell. If that also fails, set `IsStuck`.

Other changes:

- **`EnemyBase`:** gains `[Export] CollisionShape3D BodyShape` and a derived `float BodyRadius`. `MonsterSceneValidator` requires a capsule or cylinder that is a direct child of the actor. This makes the body shape the single source of a monster's size.
- **`EnemyBehaviorProfile`:** gains `[Export] bool StayInHomeRoom` (false by default; true for the Warden). The home room is resolved from the spawn position when navigation first becomes available.
- **`LargeActorFitComponent`:** its movement veto and `MovementClearance` are deleted. Hiding tall art above unrevealed fog remains its only job, so it becomes `TallActorVisibilityComponent`. `LargeActorFitProfile` keeps only the staging clearances that encounter placement uses.

## Call order

Generation:

```text
MapGenerator.GenerateMap
├─ … BuildWalkability → ResolveEncounters → PlaceAccess → PlaceLoot
├─ BuildNavigation                          (replaces BakeNavigationMesh; same step, after every solid is placed)
│  ├─ ClearanceGrid from map floor tiles + shared solid outlines (doors excluded)
│  ├─ AddGate per Floor.Doors entry          (gate index = door order; door state is never baked)
│  └─ Floor.Clearance = grid.Freeze()
├─ GenerateEnemySpawnPoints
└─ Floor.Freeze
Level.Generate → Floor = MapGenerator.Floor → Navigation = new FloorNavigation(Floor, NavigationProfile)
               → RestoreFloorDeltas         (restored door states are picked up on the first query)
```

Chase, per physics frame:

```text
EnemyBehaviorComponent._PhysicsProcess (CombatPhysics.Steering, before Movement)
└─ ChasingState.Update
   ├─ EnemyContext.CanReachCurrentTarget → Navigation.CanReach(target)   (throttled; shares the chase field)
   └─ EnemyContext.ExecuteTactics
      ├─ Approach  → Navigation.Chase(target)
      │  ├─ FloorNavigation.ChaseField(target, radius, frame)  (cache hit, or refresh when stale or the door version changed)
      │  ├─ steer point: direct approach if clear, else downhill lookahead with a clear line
      │  ├─ leash: a step into another room is dropped; the actor holds and faces the target
      │  ├─ + separation from registered neighbors
      │  └─ Movement.SetInputDirection → progress check → recovery or IsStuck
      ├─ Retreat   → Navigation.Flee(target)    (false → TacticalSelector.RetreatBlocked, as today)
      └─ Encircle  → Navigation.MoveTo(traversable ring point)
MovementComponent._PhysicsProcess (CombatPhysics.Movement) applies the direction
```

Invariants:

- **Order:** navigation never moves the body itself. It runs before `MovementComponent` in the same physics frame.
- **Shared fields:** a field returned in a frame does not change during that frame.
- **Size safety:** every steer point lies in a cell traversable for this radius.
- **Pursuit rules:** chase retention stays as documented, with no line-of-sight requirement. `PerceptionComponent.FindVisibleTarget` evaluates sight before reachability, so idle monsters do not widen field bounds.
- **Doors:** a closed door gate is non-traversable for every size. Opening it changes routes on the next query, with no link and no off-mesh state.

## Affected files

```text
Slice 1: collision ground truth
~ scenes/levels/dungeon/custom_tiles/register_custom_tiles.gd   the floor library's source of collision: one flat BoxShape3D per
                                                     floor item sized to its footprint (4×4 large, 2×2 small, 8×8 extra-large,
                                                     4×2 grates/channels); pools keep their curb box, foundations their own shape
~ scenes/levels/dungeon/FloorMeshLibrary.tres        re-exported
- scenes/levels/dungeon/custom_tiles/shapes/floor_*.res   no longer referenced
~ scenes/levels/generators/map_generator.tscn        interim: navmesh parses static colliders only
+ tests/FloorTraversalTest.cs                        small and medium capsules cross every floor-tile seam with no wall contact

Slice 2: grid core
+ scenes/levels/navigation/{ClearanceGrid,FlowField,FloorNavigation,NavigationProfile}.cs
+ scenes/levels/navigation/navigation_profiles/default.tres
~ scenes/levels/generators/{MapGenerator,MapGenerator.Walkability,GeneratedFloor}.cs
~ scenes/levels/{Level.cs,level.tscn}
~ scenes/debug/navigation/NavigationDebugOverlay.cs  clearance heat map and a selected field
+ tests/{ClearanceGridTest,FlowFieldTest}.cs         plain C#
+ tests/FloorNavigationTest.cs                       runtime: generated floors, door gates, size classes

Slice 3a: monsters on the grid
~ scenes/enemies/{NavigationComponent,EnemyBehaviorComponent,EnemyBase,MonsterSceneValidator,PerceptionComponent}.cs
~ scenes/enemies/enemy_behavior_component.tscn       NavigationAgent3D removed
~ scenes/enemies/{rat,skeleton,forsaken_warden,templates}/*.tscn   BodyShape wiring
~ scenes/enemies/ai/{EnemyContext,ChasingState,SearchingState,PatrollingState}.cs
~ scenes/ui/DebugMenu.cs                             steering debug instead of agent paths
~ tests/MonsterRosterTest.cs
+ tests/MonsterChaseTest.cs                          seeded floors, fixed delta, open and closed doors, fleeing targets

Slice 3b: Warden
~ scenes/enemies/EnemyBehaviorProfile.cs, forsaken_warden/forsaken_warden_behavior.tres   StayInHomeRoom
- scenes/enemies/LargeActorFitComponent.cs  + scenes/enemies/TallActorVisibilityComponent.cs
~ scenes/enemies/{LargeActorFitProfile,EnemyBase}.cs, forsaken_warden/{forsaken_warden.tscn,forsaken_warden_fit.tres}
~ scenes/levels/generators/GeneratedFloor.cs         footprint checks stay for staging only
~ tests/WardenTest.cs

Slice 4: navmesh removal
~ scenes/levels/generators/{MapGenerator*.cs,map_generator.tscn}   NavigationRegion3D becomes a plain FloorContent node
~ scenes/props/{Door.cs,door.tscn,prison/cell_gate.tscn}, scenes/levels/access/InteriorDoorComponent.cs   NavigationLink3D removed
~ scenes/levels/generators/assembly/RoomBlueprint.cs   PitNavigation obstacles removed
~ scenes/levels/dungeon/{floor,walls}_mesh_library.tscn and libraries; - custom_tiles/shapes/floor_{2,4}_navigation.res
~ scenes/debug/combat/combat_harness.tscn, scenes/debug/navigation/NavigationDebugOverlay.cs
~ tests that reference MapGenerator.NavigationRegion
~ docs/{level-design,monster-authoring-design,README}.md, README.md, ARCHITECTURE.md, AGENTS.md, .agents/skills/godot-mcp/SKILL.md
```

## Slices

- **Slice 1: collision ground truth.** This slice stands alone and also helps the current navmesh.
   - **What lands:** floor tiles collide as flat boxes; visual detail stays visual. Until slice 4, the interim bake reads colliders only, so loot and debug meshes stop cutting holes.
   - **Verify:**
     - `FloorTraversalTest`.
     - `GenerationBaselineTest` stays green: floor boxes sit below the walkability height band, so generation is unchanged.
     - The windowed chase probe shows no stalls against `FloorGridMap`.
   - **Limits:** the 1.0-radius navmesh, stuck recovery and the Warden are unchanged.
- **Slice 2: grid core.** This slice settles the two riskiest unknowns first: field cost on large maps, and quantization at 2.0–2.4-unit gates for a 1.0 body.
   - **What lands:** the grid, fields and paths are built for every floor and shown in the debug overlay. Monsters still use the navmesh.
   - **Verify:**
     - **Unit tests:** clearance near boxes; traversable cells agree with `WalkabilityGrid` standability; corridor routes keep to the middle; a 2.4 gate admits r=1.0 but not r=1.3.
     - **Door gates:** a closed gate splits a field; opening it rejoins.
     - **Flee:** the flee field leaves a dead end through its mouth instead of retreating deeper.
     - **Timing:** measure chase field times on 20×20 and 60×60 floors, with a target of ≤2 ms per field on 20×20. If the target is missed, the cell size or bounds change before slice 3a.
   - **Limits:** gameplay is unchanged.
- **Slice 3a: monsters on the grid.** Every state switches at once, because the old and new APIs cannot coexist on one component.
   - **What lands:** all chasing, moving and retreating uses the grid. `MaintainDistanceStrategy`, `AttackThenRetreatStrategy` and the low-health rule retreat through the flee field. Monsters separate in doorways.
   - **Verify:**
     - `MonsterChaseTest` runs headless now that no bake is needed. Across three realms, rats and skeletons arrive in at least 98% of runs with no stall longer than 3 s. Closed doors stop the chase and searching starts.
     - `MonsterRosterTest`: the combat harness has no level, so retreat still reports `retreat_blocked`.
     - Playtest depth 1–2 doorway congestion.
   - **Limits:** the Warden still freezes until 3b.
- **Slice 3b: Warden.**
   - **What lands:** the Warden walks around furniture in its room. At the room boundary it holds and faces a player outside. Releasing it later means setting `StayInHomeRoom = false`.
   - **Verify:** rewritten `WardenTest` (it walks at least 6 units around props within its room, and never leaves the room); review the depth 3 fight.
- **Slice 4: navmesh removal.** Mechanical deletion, plus a rename where the navmesh name no longer fits.
   - **What lands:** no bake, links or agents remain. Generated content lives under `FloorContent`.
   - **Verify:**
     - Full `dotnet test`.
     - `dotnet build "Rogue Gauntlet.csproj" -c ExportRelease`.
     - Godot import; a headless `main.tscn` smoke check with no navmesh errors.
     - The docs no longer describe navmesh behavior.

Verification cadence for every slice: `dotnet build "Rogue Gauntlet.sln"`, then the affected GdUnit4 suites with `GODOT_BIN` set and an isolated `TMPDIR`. Run the Godot import after adding scripts so the `.cs.uid` sidecars exist. `SaveGame.CurrentVersion` stays unchanged: generation output and the save schema do not change.

## Open decisions and risks

- **Cell size:** 0.5 (default, shared with walkability) or 0.25 (finer gates, four times the cells). The slice 2 timings and gate tests decide.
- **Low obstacles:** the walkability height band starts at 0.15. A rat's capsule (0.35) can only step over about 0.10 at the 45° floor limit. Slice 2 must check curbs, grates and pool rims. If small classes snag, store a second low-obstacle layer that only small classes treat as blocking.
- **Separation, not collision:** monsters keep passing through each other physically and only steer apart. Hard collision between monsters would bring back doorway jams.
- **Body sizes:** skeletons are as wide as the player (0.75). Shrinking them is a separate tuning decision once slice 3a has been played.
- **Boss leash:** the Warden stays room-bound for now. Releasing it is data, but its 1.0 body clears doorways by only about 0.2 per side.
- **Dynamic obstacles:** only doors change at runtime. A destroyed `DestructibleNest` keeps its cells blocked until the floor is rebuilt. That is acceptable until something destructible stands on a route.
- **Single floor plane:** the grid is 2D. Stairs and pits are not monster terrain. Flying movement would need its own traversability rule, which is a seam on `IsTraversable`, not state now.
