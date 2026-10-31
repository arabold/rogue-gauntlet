# Room assembly authoring

How to add content to generated rooms. [Level design](../../../../docs/level-design.md#room-assembly) describes the system; this guide is the workflow. Garrison content lives under [`scenes/levels/garrison/`](../../garrison/): `room_types/`, `props/<category>/`, `groups/` and `styles/{floors,walls}/`. Every file is a small Inspector-editable `.tres`; copy a neighbour and change it.

## Conventions

- **Facing.** After a prop's `QuarterTurns`, its front faces local **+Z** and its back **-Z**. Back-to-wall props stand with -Z on the wall face; wall props hang with their back on the mounting surface. Seats face local +Z too (`SeatedTable` turns them toward the table).
- **Measured, not authored.** Sizes come from the model's mesh and collision. A prop that should block actors but has no collision gets a box collider over its mesh.
- **Gap.** `Gap` is the free margin to neighbouring props (the larger of two props' gaps applies). Use 0 for sections that should abut (shelf runs, fences, bars).
- **Ids** are stable strings (`garrison.bed`); they name emitted nodes, gate identities and failure messages.
- **Weights.** Every list of options is a weighted pick by each entry's `Weight`, balanced over the room: counts follow the weights (a 0.3 entry appears about once per three of a 1.0 entry) and equal entries alternate. Unused variants of a prop come first.

## Add a prop

Create `props/<category>/<name>.tres` with script `Prop`:

| Field | Use |
| --- | --- |
| `Library` + `Items` | Mesh-library item names (a dropdown once the library is set); several items are cosmetic variants |
| `Scenes` | Instead of items, for props that need nodes: lights, particles, interaction (torches, gates). Several scenes are variants |
| `QuarterTurns` | Turn the model so its front faces +Z |
| `Mount` | `Floor` or `Wall`; wall props use `MountHeight` and `WallInset` (recess from wall trim to the flat face) |
| `BackToWall` | Floor props that stand against a wall (beds, shelves, barrels) |
| `Blocking` | Off for things actors walk over or that rest on other props |
| `Interactive` | Keeps the space in front (`FrontDepth` deep, as wide as the prop) on the floor, free of other floor props and reachable from every doorway |
| `Sunken` | Stands in a pit cut into the floor tiles under it (down-stairs); needs floor on every side |

## Add a group

Things that belong together, as data: script `PropGroup` with weighted `Anchor` options and `Members`. Each member has weighted `Options` (props, groups or seated tables), an anchor-local `Offset` (+X right, +Z front, -Z toward the wall, Y up for things on tables), `QuarterTurns` and a `Chance`. `MirrorChance` flips members to the other side. Tableware rests on a table by setting `Offset.Y` to the table top (1.0 for the kit's tables) and `Blocking` off; groups nest, so a set table (`set_long_table`) can be a `SeatedTable`'s table.

Offsets must clear the anchor's **collision**, not just its mesh, plus both gaps: a member that touches is dropped, so a wrong offset shows up as a member that never appears. For seats along a table of any size use `SeatedTable` (`Tables`, `Seats`, spacing and chances); it picks one seat kind per table and spaces wide seats by their width.

## Add a floor or wall style

Floor pieces tile edge to edge on a quarter-tile grid and never overlap, so every item's size is checked against its mesh.

- `styles/floors/<material>.tres` (`FloorMaterial`): weighted `Large` (4×4, whole-tile) and `Small` (2×2, quarter-tile) floor items, optional 2×2 `Wear` accents with `WearChance`, and `AlignedPlanks` for wood.
- Floor styles combine materials: `UniformFloor`, `BorderedFloor` (border width in quarter tiles) and `SplitFloor` (`Other` covers a rolled share of the room beyond one boundary). Materials never mix in scattered patches: lone squares and triangles read as broken tiles.
- A split's boundary runs along the room's axes on a tile edge, a straight seam that suits any pair (stone and wood). Where the kit has a diagonal piece for the pair (stone and dirt: `floor_dirt_small_corner`), give the split a `FloorTransition` (`styles/floors/transitions/`) and `DiagonalChance` lays 45° boundaries from it.
- `FloorInlay` (`styles/floors/inlays/`) sets features such as grates into the floor: items of one footprint up to a tile (the 4×2 channel, the 4×4 grate), an optional `Host` material, counts, and `MinimumRun`/`MaximumRun` pieces end to end for gutters. Inlays sit whole on the quarter-tile grid, off the wall ring and doorway tiles, clear of floor props, transitions and each other. Never list them as a material's `Small` items.
- `styles/walls/<name>.tres` (`WallStyle`): weighted `Straight` pieces (decorations hang only on these), `Corner`/`Half`, an optional `Pillar` every `PillarEvery` modules, and `Accents` with `AccentChance`. Perimeter walls back onto void, so never use see-through pieces (open scaffolds, gated or open windows).

## Add a room type

`room_types/<name>.tres` (`RoomType`): weighted `Shapes` (sizes in tiles: width across, length along the room's axis), doorway count and placement, optional door scene, `Circulation` (doorway approaches, or a door-to-door spine), `MaxPocketShare`, weighted `Walls` and `Floors`, optional `Columns` behind `ColumnChance`, then ordered `Steps`. Add it to the [Garrison factory](../../garrison/factories/garrison_room_factory.tres) `RoomTypes` with a `Weight`; entrance and exit types go in `EntranceTypes` and `ExitTypes` and must place the stairs.

Steps run in order and take the space they need first: place identity-defining content (beds, cells, tables) before decoration and clutter. A `MinimumCount` makes a step required; `Chance` makes it occasional; `Uniform` keeps one content choice for the whole room (one banner colour, one table kind); `ChoiceStep` runs one of several weighted variants.

| Step | Places |
| --- | --- |
| `AlongWallsStep` | Content packed along wall runs (long, short or any sides), or one piece `Centered` per run (an altar) |
| `CornersStep` | Content in room corners |
| `RowsStep` | A grid in the open floor, optionally two banks around a center aisle; fills from the middle out |
| `CenterStep` | One centerpiece |
| `ScatterStep` | Loose clutter against walls or in the open |
| `WallDecorStep` | Wall mounts beside doorways or spread along plain walls |
| `CellsStep` | Barred cells with operable gates, corner and junction pieces where bars meet, and content against each back wall; rows start flush in a room corner when they fit |
| `FencedAreaStep` | A wall or corner bay (with content), a fenced walkway, or a divider |
| `StairsStep` | The level's stairs: up-stairs against a wall, or a `Sunken` stairs prop over two adjacent floor tiles; the stairs props are `Interactive`, so their landing stays free and reachable |

A new kind of placement is a new `FurnishingStep` subclass that composes plans from `Content` and places them through `RoomBlueprint.TryPlace` (or `AgainstWall` for wall runs); the blueprint enforces floor support, overlap, circulation, connectivity and the pocket budget.

## Preview and debug

Run the [room generation harness](../../../debug/rooms/room_generation_harness.tscn) (**F6**). Pick the room type (entrances and exits are listed after the ordinary types) and seed, override any axis (shape, walls, floor, columns), toggle furnishings and the walkability overlay (green reachable, blue reserved circulation and stair landings, red pocket), and rotate. When a type cannot fit, the error names the step and the last refused placement, such as `garrison.bed: blocks reserved circulation`.

Validate content with `RoomAssemblyTest` (every definition validates; every type keeps doorways connected, pockets bounded and props apart across seeds; stair rooms land arrivals on reachable floor).