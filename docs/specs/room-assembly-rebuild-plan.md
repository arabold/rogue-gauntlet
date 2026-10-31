# Room assembly rebuild

Status: slices 1–4 implemented (October 31, 2026); slices 5–6 and the replacement test suites remain. [Level design](../level-design.md#room-assembly) describes the implemented part. Replaces the room composition in [PR #68](https://github.com/arabold/rogue-gauntlet/pull/68) and the coded generators it extends. [Level design](../level-design.md) describes the current system; [randomness and seeds](../randomness-and-seeds.md) owns stream rules. Supersedes the [reusable room sections plan](reusable-room-sections-plan.md) once accepted.

## Why rebuild

The current assembly works, but each new idea added a parallel mechanism instead of extending one:

- **Three placement paths.** Optional pools (`RoomArrangementPool` + `Connected`/`Perimeter`/`Slot` strategies), furnishing functions (`RoomFurnishingContext` + `RoomFurnishingPatterns`) and required slots (`RoomFurnishingSlot`, planned during structure, flattened to nodes, then re-placed after floor routing). Dressing and lighting add two more passes (`RoomDressingProfile`, `RoomLightingProfile`).
- **Deep, overlapping configuration.** A barracks is `ModularRoomGenerator` → `RoomLayoutVariant` → `RoomBaseLayout` + `RoomContentLayout` → `CompositeRoomLayout` → `WallBedsRoomLayout` → `BedClusterDefinition` → `RoomArrangementDefinition` (+ `Variations`) → `RoomArrangement` scene. Prisons, boundaries, chambers, assemblies and columns each add their own definition/pattern pair.
- **Axes are tangled in one class.** `ProceduralRoomBuilder` (~2,000 lines) owns shape, floor material, walls, columns, doorways, purpose-driven gutters/grates, chasms, furnishing and saving. Coded generators duplicate it and switch features off (`ColumnChance = 0`, `EnableChasms = false`). Floor "mixes" are hard-coded per `RoomMaterial`; wall variety (arched, pillar, cracked, broken, windows) in `WallsMeshLibrary` is not an independent choice. `RoomPurpose` bit flags gate eligibility, so a new room kind means editing an enum and every flag hint.
- **Expensive authoring.** One bedside table is a `.tscn` (root script, GridMap child, hand-measured `PlacementBounds`) plus a `.tres` (`OccupiedTiles`, wall flags, purposes); each variation repeats both files.
- **The dead-zone rule is enforced indirectly.** Every placement must keep *all* free space connected (`RoomPassageValidator`), which forbids intentional inaccessible areas and makes compositions brittle (companion subsets retried 3/1/2/0, half-cell advances after rejection). Spawns and loot use a different, tile-level model (`GeneratedFloor._reservations`, decoration tiles, physics probes) and never consult reachability, so safety depends on every placement rule being right.

## Goals

- Randomize each room along independent axes: **shape and size**, **floor style**, **wall style**, **columns**, **furniture**, **wall decorations**.
- Room types are compositions of reusable strategy Resources run by one fixed pipeline; new behavior is a new strategy subclass, never a new room-generator subclass.
- Adding a prop, a group, a floor/wall style or a room type is one small `.tres`, authorable in the Inspector or by an agent.
- **Inaccessible areas are allowed; nothing is ever spawned or placed in one.** Every doorway of a room stays mutually reachable.
- Deterministic per seed, with one RNG stream per axis so changing one axis does not reroll the others.

## Design

A `RoomType` Resource names one strategy (or weighted palette) per axis. `RoomAssembler` runs the axes in a fixed order against a `RoomBlueprint`, the per-attempt plan, then emits a `Room` node. Rooms are assembled completely, content included, before floor packing, so the result is previewable in isolation and rotates as one unit.

| Axis | Strategy base | Initial implementations |
| --- | --- | --- |
| Shape and size | `RoomShape` | `RectangleShape` (optional odd width), `LShape`, `TShape`, `UShape`, `CrossShape` (ported carving) |
| Doorways | `RoomType` fields | count range; placement `Any` / `LongAxisEnds` / `Sides`; optional door scene and chance |
| Wall style | `WallStyle` (data) | weighted straight pieces (`wall`, `wall_cracked`, `wall_broken`, `wall_arched`…), corner/half pieces, rhythm pillar every N modules, accent chance (windows, shelves) |
| Floor style | `FloorStyle` | `UniformFloor`, `BorderedFloor` (border + center material), `PatchedFloor` (base + clumped patches: stone/dirt, ruined) |
| Floor material | `FloorMaterial` (data) | weighted large/small tile items: stone, light wood, dark wood, dirt, cobble |
| Columns | `ColumnPattern` | `AisleColumns`, `PerimeterColumns`, `CenterColumns`, `CornerColumns`; `RoomType.ColumnChance` makes them optional |
| Furniture and decor | `FurnishingStep` | `AlongWallsStep`, `CornersStep`, `RowsStep`, `CenterStep`, `ScatterStep`, `WallDecorStep`; later `CellsStep` (prison), `FencedAreaStep` |
| What a step places | `Placeable` | `Prop` (one object), `PropGroup` (anchor + relative members), `SeatedTable` (coded: seats spaced along a measured table) |

**Arrangements become data or code behind one interface.** `Placeable` is a composite: a `Prop` is a leaf, a `PropGroup` is "anchor plus optional members at offsets" (table and chairs, wine barrel with a small table and bottles), and parametric layouts are small coded subclasses. Every member is placed and validated individually, so an optional piece that does not fit is simply dropped. There are no pre-authored multi-object scenes; scenes remain only for props that need nodes (lights, particles, interaction).

**Every array of definitions is a weighted pick** using the entries' own `Weight`. Mesh-item choices inside data Resources use `Dictionary<string, float>` (item name → weight); names are validated against the assigned `MeshLibrary` and offered as an Inspector dropdown.

### Walkability and dead zones

One plain C# `WalkabilityGrid` serves both room assembly and the floor. It rasterizes floor, walls and solid prop collision onto 0.5-unit cells (8×8 per tile, matching `FloorDiscovery` samples), erodes free space by the actor radius with an integral image, and flood-fills.

- **During assembly** (hard rule): after each blocking piece, all doorway approach cells must lie in one standable component; otherwise that piece is rejected. Pockets are allowed. `Interactive` props reserve an approach cell so later pieces cannot block them. A `RoomType.Circulation` mode can pre-reserve doorway approaches or a door-to-door spine before furniture, which keeps rooms readable and avoids most rejections.
- **On the floor** (the dead-zone guarantee): after rooms, corridors and secret pockets exist, `GeneratedFloor` builds one grid from the real geometry and flood-fills from the arrival with every door treated as passable. Enemy spawns, chests, loose loot, traps, teleport targets and drop landings require a reachable cell. This covers authored rooms, corridors and any future content source, and replaces the tile reservation model. Freely-accessible queries (teleport, arrival safety) keep their existing locked/undiscovered-door filter on top.

Headless Godot cannot bake navmeshes, so this grid is the testable stand-in; the baked navmesh remains the runtime truth and is reviewed in the editor.

## Contracts

Abbreviated; one `[GlobalClass]` Resource per file. Strategy Resources are shared and stateless; all mutable state lives in the blueprint of one assembly attempt.

```csharp
[Tool, GlobalClass] public partial class RoomType : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export(PropertyHint.Range, "0.01,10,0.01")] public float Weight { get; set; } = 1;
	[ExportGroup("Structure")]
	[Export] public RoomShape[] Shapes { get; set; } = [];
	[Export(PropertyHint.Range, "1,4")] public int MinDoorways { get; set; } = 1;
	[Export(PropertyHint.Range, "1,4")] public int MaxDoorways { get; set; } = 3;
	[Export] public DoorwayPlacement DoorwayPlacement { get; set; }   // Any, LongAxisEnds, Sides
	[Export] public PackedScene DoorScene { get; set; }
	[Export(PropertyHint.Range, "0,1")] public float DoorChance { get; set; }
	[Export] public CirculationMode Circulation { get; set; }         // DoorApproaches, DoorToDoorSpine
	[ExportGroup("Surfaces")]
	[Export] public WallStyle[] Walls { get; set; } = [];
	[Export] public FloorStyle[] Floors { get; set; } = [];
	[ExportGroup("Content")]
	[Export] public ColumnPattern[] Columns { get; set; } = [];
	[Export(PropertyHint.Range, "0,1")] public float ColumnChance { get; set; }
	[Export] public FurnishingStep[] Steps { get; set; } = [];        // run in order
	[Export(PropertyHint.Range, "1,16")] public int Attempts { get; set; } = 8;
	public string[] Validate();
}

[Tool, GlobalClass] public abstract partial class RoomShape : Resource
{
	[Export] public float Weight { get; set; } = 1;
	[Export] public Vector2I MinimumSize { get; set; } = new(3, 3);   // tiles
	[Export] public Vector2I MaximumSize { get; set; } = new(6, 6);
	public abstract RoomFootprint Build(RandomNumberGenerator rng); // long axis along +Z
}

[Tool, GlobalClass] public abstract partial class FloorStyle : Resource
{
	[Export] public float Weight { get; set; } = 1;
	public abstract void Paint(RoomBlueprint room, RandomNumberGenerator rng);
}

[Tool, GlobalClass] public abstract partial class ColumnPattern : Resource
{
	[Export] public float Weight { get; set; } = 1;
	[Export] public Prop[] Column { get; set; } = [];
	public abstract void Place(RoomBlueprint room, RandomNumberGenerator rng); // omits columns that break traversal
}

[Tool, GlobalClass] public abstract partial class Placeable : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public float Weight { get; set; } = 1;
	/// <summary>Choose variants and optional members, local to the anchor. Not yet validated.</summary>
	public abstract PlacementPlan Compose(RoomBlueprint room, RandomNumberGenerator rng);
}

public enum PropMount { Floor, Wall }
[Tool, GlobalClass] public partial class Prop : Placeable
{
	[Export] public MeshLibrary Library { get; set; }
	[Export] public string[] Items { get; set; } = [];       // cosmetic variants, each measured
	[Export] public PackedScene Scene { get; set; }          // instead of Items: lights, particles, interaction
	[Export] public PropMount Mount { get; set; }
	[Export] public bool BackToWall { get; set; }            // beds, shelves, wall-side tables
	[Export] public float MountHeight { get; set; } = 2.2f;
	[Export] public float Gap { get; set; } = .2f;           // 0 lets sections abut
	[Export] public bool Blocking { get; set; } = true;      // false: rugs, items on tables
	[Export] public bool Interactive { get; set; }           // reserves an approach cell
	[Export] public Aabb BoundsOverride { get; set; }        // empty = measured mesh + collision
}

[Tool, GlobalClass] public partial class PropGroup : Placeable
{
	[Export] public Placeable[] Anchor { get; set; } = [];
	[Export] public PropGroupMember[] Members { get; set; } = [];
	[Export(PropertyHint.Range, "0,1")] public float MirrorChance { get; set; } = .5f;
}

[Tool, GlobalClass] public partial class PropGroupMember : Resource
{
	[Export] public Placeable[] Options { get; set; } = [];
	[Export] public Vector3 Offset { get; set; }             // anchor-local; -Z faces the backing wall
	[Export(PropertyHint.Range, "0,3")] public int QuarterTurns { get; set; }
	[Export(PropertyHint.Range, "0,1")] public float Chance { get; set; } = 1;
}

[Tool, GlobalClass] public abstract partial class FurnishingStep : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public Placeable[] Content { get; set; } = [];
	[Export(PropertyHint.Range, "0,32")] public int MinimumCount { get; set; }  // > 0 makes the step required
	[Export(PropertyHint.Range, "0,32")] public int MaximumCount { get; set; } = 4;
	/// <summary>Returns the number placed; never throws on misfit.</summary>
	public abstract int Furnish(RoomBlueprint room, RandomNumberGenerator rng);
}

/// <summary>One assembly attempt: footprint, doorways, walkability, wall runs and accepted pieces.</summary>
public sealed class RoomBlueprint
{
	public RoomFootprint Footprint { get; }
	public IReadOnlyList<RoomDoorway> Doorways { get; }
	public WalkabilityGrid Grid { get; }
	public IReadOnlyList<WallRun> WallRuns { get; }          // free straight spans, split at doorways and corners
	/// <summary>Required pieces are all-or-nothing; optional pieces are dropped individually. Unchanged on false.</summary>
	public bool TryPlace(PlacementPlan plan, Transform3D at);
	public void Reserve(Rect2 area);                          // circulation and interaction approaches
}

public sealed class WalkabilityGrid
{
	public const float CellSize = .5f;
	public WalkabilityGrid(Rect2 bounds, float actorRadius);
	public void MarkFloor(Rect2 area);
	public void MarkBlocked(Aabb solid);
	public bool IsStandable(Vector2I cell);
	public bool AreConnected(IReadOnlyList<Vector2> points);
	public HashSet<Vector2I> Reachable(IEnumerable<Vector2> sources);
}
```

Failure contract: a step that places fewer than `MinimumCount` fails the attempt; the assembler retries with a new shape stream up to `Attempts`, then throws with the room type and step ID. A failed attempt never mutates an editor room. Optional content never fails an attempt.

### Authoring examples

A prop with four cosmetic variants:

```ini
[resource]
script = ExtResource("prop")
Id = "garrison.bedside_table"
Library = ExtResource("decoration_library")
Items = ["table_small", "table_small_decorated_A", "table_small_decorated_B", "stool"]
Gap = 0.05
```

A wine-barrel station (anchor plus optional members):

```ini
[sub_resource type="Resource" id="table"]
script = ExtResource("member")
Options = [ExtResource("table_small")]
Offset = Vector3(1.6, 0, 0.3)
Chance = 0.8

[sub_resource type="Resource" id="bottles"]
script = ExtResource("member")
Options = [ExtResource("bottles")]
Offset = Vector3(1.6, 0.9, 0.3)
Chance = 0.6

[resource]
script = ExtResource("group")
Id = "garrison.wine_station"
Anchor = [ExtResource("barrel_large")]
Members = [SubResource("table"), SubResource("bottles")]
```

A barracks is one `RoomType` file: rectangle and L shapes; stone, light-wood, dark-wood and bordered floors; plain, cracked and pillared walls; optional aisle columns; then `AlongWallsStep` (bed groups on long walls, 2–6), `CornersStep` (crates), `WallDecorStep` (torches beside doorways, banners and shields) and `ScatterStep` (footlockers, stools).

## Runtime flow

```
MixedRoomFactory.CreateStandardRoom(rng)                  ~ picks a weighted RoomType instead of a RoomGenerator
└─ RoomAssembler.Assemble(type, seed)                     + plain C#; seed = one draw from the factory rng
   ├─ per attempt: shape.Build            stream "shape:{attempt}"
   ├─ doorways + circulation reservation  stream "doorways:{attempt}"
   ├─ wall style → wall runs, pillars     stream "walls"        (pieces that intrude mark cells blocked)
   ├─ columns (ColumnChance)              stream "columns"
   ├─ steps in order                      stream "step:{index}:{id}"
   │    invariant: doorway approaches stay connected after every blocking piece
   ├─ floor style (cosmetic)              stream "floor"
   └─ RoomEmitter.Emit(blueprint) → Room  + GridMaps, Props/ instances, DoorwayMarkers, doors
MapGenerator.GenerateMapAttempt
├─ GenerateRooms / ConnectRooms / FinalizeDoors           = rooms already contain their content
├─ CreateSecretBranches                                   ~ host approach must be standable
├─ PlaceArrangements                                      ~ only the generic development rooms until ported; then removed
├─ Floor.BuildWalkability()                               + real geometry, flood from arrival, doors passable
├─ PlaceAccess / PlaceLoot                                ~ require reachable cells
└─ GenerateEnemySpawnPoints                               ~ candidates from reachable cells, not tile centers of unreserved tiles
```

## Affected files

```
scenes/levels/generators/assembly/              + RoomAssembler, RoomBlueprint, RoomFootprint, WalkabilityGrid, PlacementPlan, RoomEmitter, RoomType
scenes/levels/generators/assembly/shapes/       + RoomShape and implementations (carving ported from ProceduralRoomBuilder)
scenes/levels/generators/assembly/surfaces/     + FloorMaterial, FloorStyle + implementations, WallStyle
scenes/levels/generators/assembly/columns/      + ColumnPattern + implementations
scenes/levels/generators/assembly/content/      + Placeable, Prop, PropGroup, PropGroupMember, SeatedTable
scenes/levels/generators/assembly/steps/        + FurnishingStep + implementations
scenes/levels/garrison/{props,groups,surfaces,room_types}/  + migrated content (one .tres each)
scenes/levels/generators/GeneratedFloor.cs      ~ walkability, reachable queries; - _reservations / Reserve
scenes/levels/generators/MapGenerator*.cs       ~ order above; loot and spawn queries
scenes/levels/generators/factories/MixedRoomFactory.cs   ~ RoomType[] instead of RoomGenerator[]
scenes/levels/Room.cs                           ~ RoomType reference instead of Generator; Purpose retired with ProceduralRoomBuilder
scenes/debug/rooms/RoomGenerationHarness.cs     ~ per-axis overrides; walkability overlay (reachable, pocket, reserved)
addons/room_generator/plugin.gd                 ~ generate/save a room from a RoomType
scenes/levels/access/InteriorDoorComponent.cs   = kept for prison cell gates
scenes/levels/generators/rooms/*                - RoomGenerator and subclasses, layouts, patterns, chamber/boundary/assembly definitions
scenes/levels/arrangements/**                   - arrangement scripts, strategies, validators, dressing/lighting; scenes with lights → Prop.Scene
scenes/levels/garrison/{layouts,rooms,arrangements}/   - replaced by the migrated content
scenes/levels/generators/rooms/ProceduralRoomBuilder.cs  - last slice, after the development factory's purposes are ported
tests/                                          ~ replace room composition suites; add walkability and floor reachability suites
docs/level-design.md, scenes/levels/README.md, scenes/levels/arrangements/README.md   ~ rewritten for the new system
scripts/saves/SaveGame.cs                       ~ bump CurrentVersion with each generation change
```

## Slices

Hybrid delivery: shared infrastructure first because it carries the dead-zone guarantee, then vertical room types.

1. **Walkability.** `WalkabilityGrid`, `GeneratedFloor.BuildWalkability()`, reachable-only spawns, loot, traps, teleports and drops; remove tile reservations. Works with today's content. *Verify:* plain C# grid tests (erosion, pockets, connectivity); floor suite over many seeds asserting every spawn, chest, loose item and trap lies on a reachable cell.
2. **Assembler core and first room type.** Blueprint, emitter, `RectangleShape`, `UniformFloor`, `WallStyle`, `Prop`, `PropGroup`, `AlongWallsStep`, `WallDecorStep`, `ScatterStep`; storage as a `RoomType` in the harness and in `MixedRoomFactory` next to the remaining generators. *Verify:* determinism, axis independence (changing floor or walls moves no prop), doorway connectivity and no overlaps across seeds; harness capture.
3. **Axis breadth.** Remaining shapes, `BorderedFloor`, `PatchedFloor`, arched/pillared/cracked wall styles, column patterns. *Verify:* invariants per axis; visual review of a seed matrix.
4. **Room types.** `RowsStep`, `CornersStep`, `CenterStep`, `SeatedTable`; port barracks, library, alchemist, dining, hall and passage; remove their old generators and content. *Verify:* invariant suite per type; full-floor smoke; windowed captures.
5. **Partitions.** `CellsStep` with `InteriorDoorComponent` gates (prison) and `FencedAreaStep` (bays, walkways); remove the remaining PR #68 composition code. *Verify:* gate interaction and save round trip; cells reachable through open gates.
6. **Retire the old builder.** Port the development factory's remaining purposes (temple, colonnade, sewer, ruined, caves) or drop them; delete `ProceduralRoomBuilder`, `RoomPurpose` and the arrangement system; update the editor plugin and docs.

Each slice that changes generation bumps `SaveGame.CurrentVersion` and keeps the game loadable.

## Verification

`dotnet build "Rogue Gauntlet.sln"`, targeted GdUnit4 suites with an isolated `TMPDIR`, `-c ExportRelease` build, headless `main.tscn` smoke, and windowed harness/top-down captures for visual review. Tests assert outcomes (reachability, connectivity, overlaps, determinism, independence), not implementation details.

## Implementation notes (slices 1–4)

Decisions made while building, which later slices keep:

- Walkability rasterizes real convex collision outlines grown by a disc of the agent radius (`CollisionOutlines`). Bounding boxes made every room corner and corridor turn look blocked, because a corner wall's convex hull is far smaller than its box.
- Room assembly also enforces `RoomType.MaxPocketShare` (default 10% of standable floor). One mess-hall seed otherwise sealed off 42% of its floor behind tables while the doorways stayed connected.
- `Prop.Scenes` is an array, so scene props get cosmetic variants like mesh-library props. All content uses one convention: front +Z after `QuarterTurns`, back -Z on the wall.
- `RoomBlueprint.AgainstWall` stands any plan against a wall run from its measured bounds; furniture-vs-wall overlap ignores corner pieces, whose visual arms coincide with the straight walls.
- Group member offsets must clear the anchor's collision, not just its mesh: beds' collision is wider than the mesh. Validation drops a member that touches, so a wrong offset shows up as a missing member, not an overlap.
- The Garrison factory mixes `RoomTypes` with the three legacy recipes; the harness lists both. Old room-composition tests for removed generators were deleted, with replacements due in slice 6.

## Open decisions

- Continue on PR #68's branch (recommended: keeps its assets, gates and harness work) or start a fresh PR from `main`.
- Allow inaccessible pockets with floor-level reachability (recommended) or keep the strict "all free space connected" rule.
- Whether the development factory's sewer/temple/cave purposes are worth porting in slice 6 or can be dropped.
