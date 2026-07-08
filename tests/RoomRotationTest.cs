namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Text;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Correctness contracts for Room.Rotate, swept across seeds using procedurally built
/// rooms (irregular footprints, doorways, doors -- the hardest shapes to get right) and
/// checked against authored rooms (real hand-placed props, a harder test of "rotate
/// arbitrary child nodes" than procedural rooms alone provide):
/// <list type="bullet">
/// <item>Rotating four quarter turns (360 degrees) returns the exact original GridMap
/// cells -- rotation must be a lossless, invertible permutation of the tile lattice,
/// not an approximation that drifts.</item>
/// <item>A single quarter turn swaps Map.Width/Height.</item>
/// <item>Every tile-type population (Room/Connector/Chasm counts) is preserved, and
/// every DoorwayMarker/Door stays correctly matched, at EVERY intermediate rotation.</item>
/// <item>Every authored room in the library bakes a usable connector at all 4
/// orientations. Historically most failed at 1-3 of them; that was never real geometry
/// -- it was two coordinate knife edges (marker floor-binning putting tile centers on a
/// bin boundary, and HasWall's half-open cell sampling flipping corner anchors in and
/// out of range per orientation), both since replaced by nearest-center binning
/// (Room.LocalToTile) and footprint-based edge coverage (WallFootprints).
/// PackedRoomLayout still re-checks connectability per orientation as defense in
/// depth, but skipped orientations are now a bug signal, not expected behavior.</item>
/// </list>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class RoomRotationTest
{
	private const int SeedCount = 60;
	private const int TileSize = Room.TileSize;

	[TestCase]
	public void RotatingProceduralRoomsFourTimesReturnsToOriginalGeometry()
	{
		var builder = ResourceLoader.Load<ProceduralRoomBuilder>(
			"res://scenes/levels/dungeon/dungeon_procedural_room_builder.tres",
			cacheMode: ResourceLoader.CacheMode.Ignore);
		builder.DoorwayChance = 1f;
		builder.DoorChance = 1f;
		builder.MaxDoorways = 4;

		var failures = new List<string>();
		for (ulong seed = 1; seed <= SeedCount; seed++)
		{
			GD.Seed(seed);
			Room room = builder.BuildRoom();
			room.BakeTileMap();
			CheckRotationSweep(room, $"seed {seed}", strictContentChecks: true, failures);
			room.QueueFree();
		}

		AssertArray(failures)
			.OverrideFailureMessage($"Found {failures.Count} rotation violation(s) across {SeedCount} procedural seeds:\n  "
				+ string.Join("\n  ", failures))
			.IsEmpty();
	}

	[TestCase]
	public void RotatingAnAuthoredRoomFourTimesReturnsToOriginalGeometry()
	{
		var scene = GD.Load<PackedScene>("res://scenes/levels/dungeon/rooms/sewer_crossing_4way.tscn");
		Room room = scene.Instantiate<Room>();
		room.BakeTileMap();

		var failures = new List<string>();
		CheckRotationSweep(room, "sewer_crossing_4way", strictContentChecks: true, failures);
		room.QueueFree();

		AssertArray(failures)
			.OverrideFailureMessage("Found rotation violation(s) on the authored 4-way crossing room:\n  "
				+ string.Join("\n  ", failures))
			.IsEmpty();
	}

	/// <summary>
	/// Every authored room must bake a usable connector at every one of the 4
	/// orientations. Historically most authored rooms failed doorway validation at 1-3
	/// rotations; that was the tile-coordinate knife edge (floor-binning put markers'
	/// tile centers exactly on a bin boundary), not real geometry -- with markers
	/// authored on tile centers and Room.LocalToTile binning by nearest center, all four
	/// orientations of every room in the library must now be placeable.
	/// </summary>
	[TestCase]
	public void EveryAuthoredRoomBakesConnectorsAtAllFourRotations()
	{
		string[] roomScenes =
		{
			"res://scenes/levels/dungeon/rooms/alchimist_chamber.tscn",
			"res://scenes/levels/dungeon/rooms/barracks.tscn",
			"res://scenes/levels/dungeon/rooms/cave_small.tscn",
			"res://scenes/levels/dungeon/rooms/cave_small_with_pillars.tscn",
			"res://scenes/levels/dungeon/rooms/cave_small_with_rubble.tscn",
			"res://scenes/levels/dungeon/rooms/cave_tiny.tscn",
			"res://scenes/levels/dungeon/rooms/cave_tiny2.tscn",
			"res://scenes/levels/dungeon/rooms/cave_tiny3.tscn",
			"res://scenes/levels/dungeon/rooms/hall.tscn",
			"res://scenes/levels/dungeon/rooms/level_entrance.tscn",
			"res://scenes/levels/dungeon/rooms/level_entrance2.tscn",
			"res://scenes/levels/dungeon/rooms/level_exit.tscn",
			"res://scenes/levels/dungeon/rooms/room_corner_with_bed.tscn",
			"res://scenes/levels/dungeon/rooms/room_dining.tscn",
			"res://scenes/levels/dungeon/rooms/sewer_crossing_4way.tscn",
			"res://scenes/levels/dungeon/rooms/sewer_grate_1x1.tscn",
			"res://scenes/levels/dungeon/rooms/sewer_room.tscn",
			"res://scenes/levels/dungeon/rooms/storage_room_small.tscn",
		};

		var failures = new List<string>();
		foreach (string scenePath in roomScenes)
		{
			var scene = GD.Load<PackedScene>(scenePath);
			Room room = scene.Instantiate<Room>();
			string roomName = scenePath.GetFile().GetBaseName();

			for (int rotation = 0; rotation < 4; rotation++)
			{
				if (rotation > 0)
				{
					room.Rotate(1);
				}
				else
				{
					room.BakeTileMap();
				}

				if (room.Map == null)
				{
					failures.Add($"{roomName} at {rotation * 90} degrees baked no map");
					continue;
				}

				bool hasConnector = false;
				for (int x = 0; x < room.Map.Width && !hasConnector; x++)
				{
					for (int z = 0; z < room.Map.Height && !hasConnector; z++)
					{
						hasConnector = room.Map.IsConnector(x, z)
							&& room.Map.GetConnectorDirections(x, z).Count > 0;
					}
				}

				if (!hasConnector)
				{
					failures.Add($"{roomName} at {rotation * 90} degrees has no usable connector");
				}
			}

			room.QueueFree();
		}

		AssertArray(failures)
			.OverrideFailureMessage($"Found {failures.Count} authored room(s)/rotation(s) without usable connectors:\n  "
				+ string.Join("\n  ", failures))
			.IsEmpty();
	}

	/// <summary>
	/// A prop tilted off pure-Y rotation (e.g. a knocked-over barrel authored with
	/// nonzero X rotation) must have the room's rotation composed onto its EXISTING
	/// orientation via matrix multiplication, not applied as Euler-degree addition to
	/// just its Y component -- the two are only equivalent when the child's own
	/// rotation is already pure-Y.
	/// </summary>
	[TestCase]
	public void RotatingATiltedChildComposesViaBasisNotEulerAddition()
	{
		var builder = ResourceLoader.Load<ProceduralRoomBuilder>(
			"res://scenes/levels/dungeon/dungeon_procedural_room_builder.tres",
			cacheMode: ResourceLoader.CacheMode.Ignore);
		GD.Seed(1);
		Room room = builder.BuildRoom();
		room.BakeTileMap();

		var tiltedProp = new Node3D
		{
			Position = new Vector3(2, 0, 2),
			RotationDegrees = new Vector3(30, 45, 0),
		};
		room.AddChild(tiltedProp);

		Basis expectedBasis = new Basis(Vector3.Up, Mathf.DegToRad(90f)) * tiltedProp.Transform.Basis;
		room.Rotate(1);
		Basis actualBasis = tiltedProp.Transform.Basis;

		room.QueueFree();

		AssertBool(BasisApproxEquals(expectedBasis, actualBasis))
			.OverrideFailureMessage("A tilted child's rotation was not composed via basis multiplication. "
				+ $"Expected basis {expectedBasis}, got {actualBasis}.")
			.IsTrue();
	}

	private static bool BasisApproxEquals(Basis a, Basis b)
	{
		return a.X.IsEqualApprox(b.X) && a.Y.IsEqualApprox(b.Y) && a.Z.IsEqualApprox(b.Z);
	}

	/// <summary>
	/// A GridMap cell can legitimately sit at any of Godot's 24 orthogonal
	/// orientations, not just the 4 upright Y-rotations this project's own wall/floor
	/// content happens to use (e.g. a hand-placed decoration tipped over for variety).
	/// Rotation must compose correctly for those too, via the engine's own basis math,
	/// not a lookup table limited to the 4 orientations this project's content
	/// currently exercises.
	/// </summary>
	[TestCase]
	public void RotatingANonUprightGridMapOrientationComposesCorrectly()
	{
		var builder = ResourceLoader.Load<ProceduralRoomBuilder>(
			"res://scenes/levels/dungeon/dungeon_procedural_room_builder.tres",
			cacheMode: ResourceLoader.CacheMode.Ignore);
		GD.Seed(1);
		Room room = builder.BuildRoom();
		room.BakeTileMap();

		// Far outside the room's own generated content, so this doesn't collide with
		// anything BakeTileMap scans.
		var cell = new Vector3I(1000, 0, 1000);
		const int nonUprightOrientation = 1; // not one of {0,16,10,22}
		room.DecorationGridMap.SetCellItem(cell, 0, nonUprightOrientation);
		Basis expectedBasis = new Basis(Vector3.Up, Mathf.DegToRad(90f))
			* room.DecorationGridMap.GetBasisWithOrthogonalIndex(nonUprightOrientation);

		room.Rotate(1);

		Vector3I rotatedCell = new(cell.Z, cell.Y, -cell.X);
		int rotatedOrientation = room.DecorationGridMap.GetCellItemOrientation(rotatedCell);
		Basis actualBasis = room.DecorationGridMap.GetBasisWithOrthogonalIndex(rotatedOrientation);

		room.QueueFree();

		AssertBool(BasisApproxEquals(expectedBasis, actualBasis))
			.OverrideFailureMessage("A non-upright GridMap cell orientation was not rotated correctly. "
				+ $"Expected basis {expectedBasis}, got {actualBasis} (orientation index {rotatedOrientation}).")
			.IsTrue();
	}

	/// <summary>
	/// Rotates a baked room through all 4 cumulative quarter-turn states (90/180/270/360)
	/// from its original orientation, checking invariants at every step and a full
	/// original-geometry match once back at 360 (== 0).
	/// </summary>
	private static void CheckRotationSweep(Room room, string label, bool strictContentChecks, List<string> failures)
	{
		var originalFloor = SnapshotCells(room.FloorGridMap);
		var originalWall = SnapshotCells(room.WallGridMap);
		var originalDecoration = SnapshotCells(room.DecorationGridMap);
		int originalWidth = room.Map.Width;
		int originalHeight = room.Map.Height;
		var originalTileCounts = CountTileTypes(room.Map);

		for (int cumulativeSteps = 1; cumulativeSteps <= 4; cumulativeSteps++)
		{
			room.Rotate(1);
			string context = $"{label}, after {cumulativeSteps} quarter turn(s)";

			bool dimensionsSwapped = cumulativeSteps % 2 == 1;
			int expectedWidth = dimensionsSwapped ? originalHeight : originalWidth;
			int expectedHeight = dimensionsSwapped ? originalWidth : originalHeight;
			if (room.Map.Width != expectedWidth || room.Map.Height != expectedHeight)
			{
				failures.Add($"{context}: map is {room.Map.Width}x{room.Map.Height}, expected {expectedWidth}x{expectedHeight}");
			}

			if (!strictContentChecks)
			{
				continue;
			}

			var tileCounts = CountTileTypes(room.Map);
			foreach (var (tile, count) in originalTileCounts)
			{
				tileCounts.TryGetValue(tile, out int rotatedCount);
				if (rotatedCount != count)
				{
					failures.Add($"{context}: {tile} count is {rotatedCount}, expected {count}");
				}
			}

			foreach (string violation in FindDoorwayViolations(room))
			{
				failures.Add($"{context}: {violation}");
			}
		}

		// 4 quarter turns is a full 360-degree rotation: must be back to the exact
		// original geometry, not merely the same dimensions/tile counts. This is the
		// one guarantee that holds unconditionally, for any room's content.
		AssertGridMapMatches(originalFloor, SnapshotCells(room.FloorGridMap), $"{label}: FloorGridMap", failures);
		AssertGridMapMatches(originalWall, SnapshotCells(room.WallGridMap), $"{label}: WallGridMap", failures);
		AssertGridMapMatches(originalDecoration, SnapshotCells(room.DecorationGridMap), $"{label}: DecorationGridMap", failures);
	}

	/// <summary>Mirrors ProceduralRoomBuilderTest's doorway/door checks, reused here after rotation.</summary>
	private static List<string> FindDoorwayViolations(Room room)
	{
		var failures = new List<string>();
		var markers = new List<DoorwayMarker>();

		foreach (Node child in room.GetChildren())
		{
			if (child is DoorwayMarker marker)
			{
				markers.Add(marker);
				// Matches Room.LocalToTile: nearest tile CENTER, relative to
				// Bounds.Position (which is the first tile's center cell).
				int tileX = Mathf.FloorToInt((marker.Position.X - room.Bounds.Position.X + TileSize / 2f) / TileSize);
				int tileZ = Mathf.FloorToInt((marker.Position.Z - room.Bounds.Position.Y + TileSize / 2f) / TileSize);
				if (!room.Map.IsWithinBounds(tileX, tileZ) || !room.Map.IsConnector(tileX, tileZ))
				{
					failures.Add($"doorway marker at tile ({tileX},{tileZ}) is not a connector");
				}
			}
		}

		foreach (Node child in room.GetChildren())
		{
			if (child is not Door doorNode)
			{
				continue;
			}

			DoorwayMarker nearestMarker = null;
			float nearestDistance = float.MaxValue;
			foreach (DoorwayMarker candidate in markers)
			{
				float distance = doorNode.Position.DistanceTo(candidate.Position);
				if (distance < nearestDistance)
				{
					nearestDistance = distance;
					nearestMarker = candidate;
				}
			}

			if (nearestMarker == null || nearestDistance > TileSize * 2f)
			{
				failures.Add($"door at {doorNode.Position} is not within range of any doorway marker");
				continue;
			}

			var directions = nearestMarker.GetDirectionVectors();
			bool matchesSomeDirection = false;
			foreach (Vector2I direction in directions)
			{
				Vector3 expectedPosition = nearestMarker.Position + new Vector3(direction.X, 0, direction.Y) * (TileSize / 2f);
				if (doorNode.Position.DistanceTo(expectedPosition) <= 0.01f)
				{
					matchesSomeDirection = true;
					break;
				}
			}

			if (!matchesSomeDirection)
			{
				failures.Add($"door at {doorNode.Position} does not sit at its marker's sanctioned direction");
			}
		}

		return failures;
	}

	private static Dictionary<MapTile, int> CountTileTypes(MapData map)
	{
		var counts = new Dictionary<MapTile, int>();
		for (int x = 0; x < map.Width; x++)
		{
			for (int z = 0; z < map.Height; z++)
			{
				MapTile tile = map.Tiles[x, z];
				counts.TryGetValue(tile, out int count);
				counts[tile] = count + 1;
			}
		}

		return counts;
	}

	private static Dictionary<Vector3I, (int Item, int Orientation)> SnapshotCells(GridMap gridMap)
	{
		var snapshot = new Dictionary<Vector3I, (int, int)>();
		if (gridMap == null)
		{
			return snapshot;
		}

		foreach (Vector3I cell in gridMap.GetUsedCells())
		{
			snapshot[cell] = (gridMap.GetCellItem(cell), gridMap.GetCellItemOrientation(cell));
		}

		return snapshot;
	}

	private static void AssertGridMapMatches(
		Dictionary<Vector3I, (int Item, int Orientation)> expected,
		Dictionary<Vector3I, (int Item, int Orientation)> actual,
		string label, List<string> failures)
	{
		if (expected.Count != actual.Count)
		{
			failures.Add($"{label}: has {actual.Count} cells after a full rotation, expected {expected.Count}");
			return;
		}

		foreach (var (cell, value) in expected)
		{
			if (!actual.TryGetValue(cell, out var actualValue))
			{
				failures.Add($"{label}: cell {cell} is missing after a full rotation");
			}
			else if (actualValue != value)
			{
				failures.Add($"{label}: cell {cell} is {actualValue}, expected {value}");
			}
		}
	}
}
