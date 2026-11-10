namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Wall pieces fit by their real mesh footprints: different lengths, junctions, door frames and decorated variants meet
/// without interpenetrating, and every edge they are asked to seal is fully covered. Whole generated floors are swept in
/// <see cref="GeneratedFloorSweepTest"/>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class WallIntegrityTest
{
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
	[TestCase]
	public void BoundaryGraphFitsTJunctionsAndCrossings()
	{
		var library = TestFactories.Load<MeshLibrary>("res://scenes/levels/dungeon/WallsMeshLibrary.tres");
		// -1 omits no arm: a crossing; otherwise the T without arm `omitted`.
		for (int omitted = -1; omitted < 4; omitted++) AssertJunction(library, omitted);
	}

	private static void AssertJunction(MeshLibrary library, int omitted)
	{
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

	[TestCase]
	public void FramedOpeningFitsAllAdjacentWallArms()
	{
		var library = TestFactories.Load<MeshLibrary>("res://scenes/levels/dungeon/WallsMeshLibrary.tres");
		for (int turn = 0; turn < 4; turn++) AssertFramedOpening(library, turn);
	}

	private static void AssertFramedOpening(MeshLibrary library, int turn)
	{
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

	[TestCase]
	public void FittingKeepsDecoratedWallsWhenTheirFullSpanIsClear()
	{
		var library = TestFactories.Load<MeshLibrary>("res://scenes/levels/dungeon/WallsMeshLibrary.tres");
		foreach (string name in new[] { "wall_cracked", "wall_pillar", "wall_mossy_A" })
		{
			var grid = new GridMap { MeshLibrary = library, CellSize = Vector3.One, CellCenterX = false, CellCenterY = false, CellCenterZ = false };
			try
			{
				int variant = library.FindItemByName(name);
				var source = new[] { (Vector2I.Zero, Vector3I.Zero) };
				BoundaryWallBuilder.Build(grid, source, (_, dx, dz) => dx == 0 && dz == -1, _ => variant);
				AssertString(library.GetItemName(grid.GetCellItem(Vector3I.Forward * 2))).IsEqual(name);
				AssertString(WallFootprints.FindOverlap(grid)).OverrideFailureMessage(name).IsEmpty();
			}
			finally { grid.Free(); }
		}
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
}