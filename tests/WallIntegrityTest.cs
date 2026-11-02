namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Generates many random dungeons (for every layout strategy) and asserts every logical
/// tile edge that must be sealed is fully covered by wall geometry: edges between
/// walkable space (Room/Corridor/Connector) and void (Empty/Wall/out-of-bounds), plus
/// interior room/corridor contacts not sanctioned by a connector
/// (<see cref="MapData.RequiresInteriorWall"/>). This is a generation-correctness
/// contract: a single missing wall segment is a see-through gap the player can exploit,
/// and the failure is layout- and seed-dependent, so only a sweep across seeds reliably
/// catches it.
///
/// Coverage is measured from the actual wall <em>mesh</em> footprints (each cell's mesh
/// AABB rotated by its orientation, via <see cref="WallFootprints"/>), not physics
/// colliders — wall colliders are narrower than the meshes and would report false gaps.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class WallIntegrityTest
{
	// 120, not the 60 the suite started with: rotated authored-room placements only
	// became reachable once every room validated at all 4 orientations, and those new
	// configurations need sweep coverage. Seed 27 (the authored-doorway-sealed-against-
	// frame-cells regression this suite caught) stays inside the swept range.
	private const int SeedCount = 120;
	private const int TileSize = 4;

	[TestCase]
	public async Task SimpleLayoutDungeonsHaveNoWallGaps()
	{
		await AssertNoWallGaps(new SimpleRoomLayout { Retries = 3 });
	}

	[TestCase]
	public async Task PackedLayoutDungeonsHaveNoWallGaps()
	{
		await AssertNoWallGaps(new PackedRoomLayout());
	}

	[TestCase]
	public async Task ProceduralRoomsWithDoorsHaveNoWallGaps()
	{
		// Explicit doorways make every OTHER edge fall back to a plain Room tile, which
		// must still be sealed exactly like a void-facing edge -- the doorway/door path's
		// stress on the wall pass.
		await AssertNoWallGaps(new PackedRoomLayout(), TestFactories.RoomPoolWithDoors());
	}

	/// <summary>Different-length pieces meet at their real mesh endpoints, irrespective of rotation.</summary>
	[TestCase]
	public void DifferentLengthWallsMeetWithoutPenetration()
	{
		var library = TestFactories.Load<MeshLibrary>("res://scenes/levels/dungeon/WallsMeshLibrary.tres");
		int full = library.FindItemByName("wall");
		int half = library.FindItemByName("wall_half");
		var offsets = new[] { Vector3I.Right, Vector3I.Forward, Vector3I.Left, Vector3I.Back };
		for (int turn = 0; turn < 4; turn++)
		{
			int orientation = GridTurns.Upright[turn];
			bool Overlaps(int distance) => WallFootprints.PiecesOverlap(library, full, orientation, Vector3I.Zero,
				half, orientation, offsets[turn] * distance);
			AssertBool(Overlaps(2)).IsFalse();
			AssertBool(Overlaps(1)).IsTrue();
		}
	}

	/// <summary>All T orientations and a crossing share their junction without overlapping adjacent half walls.</summary>
	[TestCase(-1)]
	[TestCase(0)]
	[TestCase(1)]
	[TestCase(2)]
	[TestCase(3)]
	public void BoundaryGraphFitsTJunctionsAndCrossings(int omitted)
	{
		var library = TestFactories.Load<MeshLibrary>("res://scenes/levels/dungeon/WallsMeshLibrary.tres");
		var grid = new GridMap { MeshLibrary = library, CellSize = Vector3.One, CellCenterX = false, CellCenterY = false, CellCenterZ = false };
		try
		{
			var source = new[] { (new Vector2I(0, 0), Vector3I.Zero), (new Vector2I(1, 0), Vector3I.Right * 4),
				(new Vector2I(0, 1), Vector3I.Back * 4) };
			var arms = new[] { (new Vector2I(0, 0), 0, 1), (new Vector2I(1, 0), 0, 1),
				(new Vector2I(0, 0), 1, 0), (new Vector2I(0, 1), 1, 0) };
			var wanted = new HashSet<(Vector2I, int, int)>();
			for (int i = 0; i < arms.Length; i++) if (i != omitted) wanted.Add(arms[i]);
			BoundaryWallBuilder.Build(grid, source, (tile, dx, dz) => wanted.Contains((tile, dx, dz)));
			var junction = new Vector3I(2, 0, 2);
			AssertString(library.GetItemName(grid.GetCellItem(junction))).IsEqual(omitted < 0 ? "wall_crossing" : "wall_Tsplit");
			AssertString(WallFootprints.FindOverlap(grid)).IsEmpty();
			var boxes = WallFootprints.Collect(grid);
			for (int i = 0; i < arms.Length; i++)
			{
				if (i == omitted) continue;
				var (tile, dx, dz) = arms[i];
				var center = (Vector3I)RoomFootprint.TileCenter(tile) + new Vector3I(dx, 0, dz) * 2;
				bool horizontal = dz != 0;
				float line = horizontal ? center.Z : center.X;
				float along = horizontal ? center.X : center.Z;
				AssertBool(WallFootprints.EdgeFullyCovered(boxes, horizontal, line, along - 2, along + 2)).IsTrue();
			}
		}
		finally { grid.Free(); }
	}

	[TestCase]
	public void OverlappingDoorFramesAreRejectedBeforeChangingWalls()
	{
		var library = TestFactories.Load<MeshLibrary>("res://scenes/levels/dungeon/WallsMeshLibrary.tres");
		var grid = new GridMap { MeshLibrary = library, CellSize = Vector3.One, CellCenterX = false, CellCenterY = false, CellCenterZ = false };
		try
		{
			int original = library.FindItemByName("wall");
			grid.SetCellItem(Vector3I.Zero, original);
			var frames = new HashSet<(Vector3I Center, bool Horizontal)> { (new(0, 0, -2), true), (new(2, 0, 0), false) };
			bool rejected = false;
			try { BoundaryWallBuilder.Build(grid, System.Array.Empty<(Vector2I, Vector3I)>(), (_, _, _) => false, framedEdges: frames); }
			catch (System.InvalidOperationException) { rejected = true; }
			AssertBool(rejected).IsTrue();
			AssertInt(grid.GetCellItem(Vector3I.Zero)).IsEqual(original);
		}
		finally { grid.Free(); }
	}

	[TestCase]
	public void ShortWallMeshAndCollisionHaveTheSameFootprint()
	{
		var library = TestFactories.Load<MeshLibrary>("res://scenes/levels/dungeon/WallsMeshLibrary.tres");
		int item = library.FindItemByName("wall_junction_short");
		AssertInt(item).IsGreaterEqual(0);
		var mesh = WallFootprints.MeshBounds(library, item);
		var shapes = library.GetItemShapes(item);
		var box = (BoxShape3D)shapes[0].AsGodotObject();
		var collider = shapes[1].AsTransform3D() * new Aabb(-box.Size / 2, box.Size);
		AssertFloat(mesh.Position.DistanceTo(collider.Position)).IsLess(WallFootprints.FitTolerance);
		AssertFloat(mesh.Size.DistanceTo(collider.Size)).IsLess(WallFootprints.FitTolerance);
		AssertFloat(mesh.Size.X).IsEqualApprox(1.5f, .001f);
	}

	[TestCase(0)]
	[TestCase(1)]
	[TestCase(2)]
	[TestCase(3)]
	public void FramedOpeningFitsAllAdjacentWallArms(int turn)
	{
		var library = TestFactories.Load<MeshLibrary>("res://scenes/levels/dungeon/WallsMeshLibrary.tres");
		var grid = new GridMap { MeshLibrary = library, CellSize = Vector3.One, CellCenterX = false, CellCenterY = false, CellCenterZ = false };
		Vector2I Rotate(Vector2I p) => turn switch { 1 => new(p.Y, -p.X), 2 => -p, 3 => new(-p.Y, p.X), _ => p };
		try
		{
			var tiles = new[] { new Vector2I(0, 0), new Vector2I(0, -1), new Vector2I(1, 0) };
			var arms = new[] { (tiles[0], Vector2I.Right), (tiles[1], Vector2I.Right), (tiles[2], Vector2I.Up) };
			var wanted = new HashSet<(Vector2I, Vector2I)>(System.Linq.Enumerable.Select(arms, arm => (Rotate(arm.Item1), Rotate(arm.Item2))));
			var source = System.Linq.Enumerable.Select(tiles, tile => (Rotate(tile), (Vector3I)RoomFootprint.TileCenter(Rotate(tile))));
			var edge = Rotate(new Vector2I(0, -2));
			var frame = new Vector3I(edge.X, 0, edge.Y);
			bool horizontal = turn % 2 == 0;
			var framed = new HashSet<(Vector3I Center, bool Horizontal)> { (frame, horizontal) };
			BoundaryWallBuilder.Build(grid, source, (tile, dx, dz) => wanted.Contains((tile, new Vector2I(dx, dz))), framedEdges: framed);
			AssertString(WallFootprints.FindOverlap(grid)).IsEmpty();
			int door = library.FindItemByName("wall");
			int facing = horizontal ? GridTurns.Turn0 : GridTurns.Turn90;
			foreach (Vector3I cell in grid.GetUsedCells())
				AssertBool(WallFootprints.PiecesOverlap(library, door, facing, frame, grid.GetCellItem(cell), grid.GetCellItemOrientation(cell), cell)).IsFalse();
			var boxes = WallFootprints.Collect(grid);
			boxes.Add(WallFootprints.FootprintWorld(WallFootprints.MeshBounds(library, door), facing, frame));
			foreach (var (tile, direction) in wanted)
			{
				var center = (Vector3I)RoomFootprint.TileCenter(tile) + new Vector3I(direction.X, 0, direction.Y) * 2;
				bool alongX = direction.Y != 0;
				float line = alongX ? center.Z : center.X;
				float along = alongX ? center.X : center.Z;
				AssertBool(WallFootprints.EdgeFullyCovered(boxes, alongX, line, along - 2, along + 2)).IsTrue();
			}
		}
		finally { grid.Free(); }
	}

	[TestCase("wall_cracked")]
	[TestCase("wall_pillar")]
	[TestCase("wall_mossy_A")]
	public void FittingKeepsDecoratedWallsWhenTheirFullSpanIsClear(string name)
	{
		var library = TestFactories.Load<MeshLibrary>("res://scenes/levels/dungeon/WallsMeshLibrary.tres");
		var grid = new GridMap { MeshLibrary = library, CellSize = Vector3.One, CellCenterX = false, CellCenterY = false, CellCenterZ = false };
		try
		{
			int variant = library.FindItemByName(name);
			var source = new[] { (Vector2I.Zero, Vector3I.Zero) };
			BoundaryWallBuilder.Build(grid, source, (_, dx, dz) => dx == 0 && dz == -1, _ => variant);
			AssertString(library.GetItemName(grid.GetCellItem(Vector3I.Forward * 2))).IsEqual(name);
			AssertString(WallFootprints.FindOverlap(grid)).IsEmpty();
		}
		finally { grid.Free(); }
	}

	[TestCase]
	public void FittingFallsBackWhenDecoratedWallWouldOverlap()
	{
		var library = TestFactories.Load<MeshLibrary>("res://scenes/levels/dungeon/WallsMeshLibrary.tres");
		var grid = new GridMap { MeshLibrary = library, CellSize = Vector3.One, CellCenterX = false, CellCenterY = false, CellCenterZ = false };
		try
		{
			int variant = library.FindItemByName("wall_mossy_A");
			var source = new[] { (Vector2I.Zero, Vector3I.Zero), (Vector2I.Down, Vector3I.Back) };
			BoundaryWallBuilder.Build(grid, source, (_, dx, dz) => dx == 0 && dz == -1, _ => variant);
			AssertString(library.GetItemName(grid.GetCellItem(Vector3I.Forward * 2))).IsEqual("wall");
			AssertString(WallFootprints.FindOverlap(grid)).IsEmpty();
		}
		finally { grid.Free(); }
	}

	[TestCase]
	public void LibraryMeshTransformsAreIncludedInWallFits()
	{
		using var library = new MeshLibrary();
		using var mesh = new BoxMesh { Size = new Vector3(2, 4, 1) };
		library.CreateItem(0);
		library.SetItemName(0, "offset");
		library.SetItemMesh(0, mesh);
		library.SetItemMeshTransform(0, new Transform3D(Basis.Identity, Vector3.Right));
		library.CreateItem(1);
		library.SetItemName(1, "centered");
		library.SetItemMesh(1, mesh);
		AssertBool(WallFootprints.PiecesOverlap(library, 0, 0, Vector3I.Zero, 1, 0, Vector3I.Right * 3)).IsFalse();
		AssertBool(WallFootprints.PiecesOverlap(library, 0, 0, Vector3I.Zero, 1, 0, Vector3I.Right * 2)).IsTrue();
	}

	/// <summary>A T's empty corners must not be mistaken for solid wall, while its stem must join without overlap.</summary>
	[TestCase]
	public void JunctionFootprintsDistinguishContactFromPenetrationInEveryOrientation()
	{
		var library = TestFactories.Load<MeshLibrary>("res://scenes/levels/dungeon/WallsMeshLibrary.tres");
		int junction = library.FindItemByName("wall_Tsplit");
		int wall = library.FindItemByName("wall");
		int end = library.FindItemByName("wall_endcap");
		AssertBool(junction >= 0 && wall >= 0 && end >= 0).IsTrue();
		for (int turn = 0; turn < 4; turn++)
		{
			Vector3I Rotate(Vector3I p) => turn switch
			{
				1 => new(p.Z, p.Y, -p.X),
				2 => new(-p.X, p.Y, -p.Z),
				3 => new(-p.Z, p.Y, p.X),
				_ => p,
			};
			int facing = GridTurns.Upright[turn];
			int perpendicular = GridTurns.Upright[(turn + 1) % 4];
			bool Overlaps(int item, int orientation, Vector3I position) =>
				WallFootprints.PiecesOverlap(library, junction, facing, Vector3I.Zero, item, orientation, Rotate(position));
			AssertBool(Overlaps(wall, perpendicular, new Vector3I(0, 0, 4))).IsFalse();
			AssertBool(Overlaps(wall, perpendicular, new Vector3I(0, 0, 3))).IsTrue();
			AssertBool(Overlaps(end, facing, new Vector3I(1, 0, 1))).IsFalse();
		}
	}

	[TestCase]
	public void SampledRoomWallsDoNotOverlap()
	{
		var overlaps = new List<string>();
		foreach (string name in new[] { "hall", "prison", "ruins", "barracks", "storage" })
		{
			var type = TestFactories.Load<RoomType>($"res://scenes/levels/rooms/room_types/{name}.tres");
			foreach (ulong seed in new ulong[] { 1, 2, 3, 4 })
			{
				var room = RoomAssembler.Build(type, seed, new RoomAssemblyOptions { Furnish = false });
				try
				{
					string overlap = WallFootprints.FindOverlap(room.Room.WallGridMap);
					if (overlap != "") overlaps.Add($"{name} {seed}: {overlap}");
				}
				finally { room.Room.Free(); }
			}
		}
		AssertArray(overlaps).OverrideFailureMessage(string.Join("\n", overlaps)).IsEmpty();
	}

	[TestCase("packed")]
	[TestCase("simple")]
	public async Task RoomFramesAndSecretWallsDoNotOverlapFloorWalls(string layout)
	{
		RoomLayoutStrategy strategy = layout == "packed" ? new PackedRoomLayout() : new SimpleRoomLayout { Retries = 3 };
		var (runner, generator) = await TestFactories.LoadGenerator(strategy, TestFactories.RoomPoolWithSecrets(.5f));
		var grid = generator.WallGridMap;
		int frameItem = grid.MeshLibrary.FindItemByName("wall"); // same 4x1 footprint as the door, opening frame and disguise
		int frameCount = 0, secretCount = 0;
		for (ulong seed = 1; seed <= 12; seed++)
		{
			generator.Seed = seed;
			generator.GenerateMap(includeGameplay: false);
			foreach (var room in generator.NavigationRegion.GetChildren().OfType<Room>())
			{
				secretCount += room.Doors.Values.Count(door => door.Secret?.IsSecret == true);
				foreach (var (local, horizontal) in room.FramedEdges)
				{
					frameCount++;
					var center = (Vector3I)grid.ToLocal(room.ToGlobal(local));
					int facing = horizontal ? GridTurns.Turn0 : GridTurns.Turn90;
					foreach (Vector3I cell in grid.GetUsedCells())
						AssertBool(WallFootprints.PiecesOverlap(grid.MeshLibrary, frameItem, facing, center,
							grid.GetCellItem(cell), grid.GetCellItemOrientation(cell), cell))
							.OverrideFailureMessage($"{layout} seed {seed}: doorway {center} overlaps wall {cell}").IsFalse();
				}
			}
			await runner.SimulateFrames(1);
		}
		AssertInt(frameCount).IsGreater(0);
		AssertInt(secretCount).IsGreater(0);
	}

	private static async Task AssertNoWallGaps(RoomLayoutStrategy layout, RoomFactory factory = null)
	{
		var (runner, mapGenerator) = await TestFactories.LoadGenerator(layout, factory);

		var gaps = new List<string>();
		for (ulong seed = 1; seed <= SeedCount; seed++)
		{
			mapGenerator.Seed = seed;
			mapGenerator.GenerateMap(includeGameplay: false); // preview: skip navmesh bake + spawns
			foreach (string gap in FindWallGaps(mapGenerator))
			{
				gaps.Add($"seed {seed}: {gap}");
			}
			string overlap = WallFootprints.FindOverlap(mapGenerator.WallGridMap);
			if (overlap != "") gaps.Add($"seed {seed}: {overlap}");

			// GenerateMap re-instantiates room scenes each call and QueueFrees the previous
			// ones; that free is deferred to frame end, so pump a frame here. Without it,
			// 60 generations' worth of freed nodes pile up in a single frame and surface as
			// orphan-node warnings (and needless peak memory) -- an artifact of the tight
			// loop, not something that happens in play where generation spans frames.
			await runner.SimulateFrames(1);
		}

		AssertArray(gaps)
			.OverrideFailureMessage($"Found {gaps.Count} wall gap(s) or overlap(s) across {SeedCount} seeds:\n  " + string.Join("\n  ", gaps))
			.IsEmpty();
	}

	private static List<string> FindWallGaps(MapGenerator mapGenerator)
	{
		var gaps = new List<string>();
		MapData map = mapGenerator.Map;
		List<Aabb> wallBoxes = WallFootprints.Collect(mapGenerator.WallGridMap);
		// A frame seals the half-unit where a perpendicular wall meets its endpoint.
		var grid = mapGenerator.WallGridMap;
		int frame = grid.MeshLibrary.FindItemByName("wall");
		foreach (var room in mapGenerator.NavigationRegion.GetChildren().OfType<Room>())
		foreach (var (local, horizontal) in room.FramedEdges)
		{
			var center = (Vector3I)grid.ToLocal(room.ToGlobal(local));
			wallBoxes.Add(WallFootprints.FootprintWorld(WallFootprints.MeshBounds(grid.MeshLibrary, frame),
				horizontal ? GridTurns.Turn0 : GridTurns.Turn90, center));
		}

		var dirs = new (int dx, int dz, string name)[] { (0, -1, "N"), (0, 1, "S"), (-1, 0, "W"), (1, 0, "E") };
		for (int x = 0; x < map.Width; x++)
		{
			for (int z = 0; z < map.Height; z++)
			{
				if (!IsWalkable(map, x, z))
				{
					continue;
				}
				float cx = (x - map.Width / 2) * TileSize;
				float cz = (z - map.Height / 2) * TileSize;
				foreach (var (dx, dz, name) in dirs)
				{
					// An edge must be sealed when it faces void, or when it is an
					// unsanctioned room/corridor contact (see MapData.RequiresInteriorWall).
					if (!NeedsWall(map, x + dx, z + dz)
						&& !map.RequiresInteriorWall(x, z, new Vector2I(dx, dz)))
					{
						continue;
					}
					bool horizontal = dz != 0;
					float lineCoord = horizontal ? cz + dz * (TileSize / 2f) : cx + dx * (TileSize / 2f);
					float spanMin = (horizontal ? cx : cz) - TileSize / 2f;
					float spanMax = (horizontal ? cx : cz) + TileSize / 2f;

					if (!WallFootprints.EdgeFullyCovered(wallBoxes, horizontal, lineCoord, spanMin, spanMax))
					{
						gaps.Add($"tile=({x},{z}) edge={name} span=[{spanMin:0.#},{spanMax:0.#}]");
					}
				}
			}
		}
		return gaps;
	}

	private static bool IsWalkable(MapData map, int x, int z)
	{
		return map.IsWithinBounds(x, z) && map.IsWalkable(x, z);
	}

	private static bool NeedsWall(MapData map, int x, int z)
	{
		return !map.IsWithinBounds(x, z) || map.IsWallOrEmpty(x, z);
	}
}