namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Correctness contracts for Room.Rotate, swept across seeds using every assembled room type
/// (irregular footprints, doorways, doors, props and stairwell pits -- the hardest shapes to get right):
/// <list type="bullet">
/// <item>Rotating four quarter turns (360 degrees) returns the exact original GridMap
/// cells -- rotation must be a lossless, invertible permutation of the tile lattice,
/// not an approximation that drifts.</item>
/// <item>A single quarter turn swaps Map.Width/Height.</item>
/// <item>Every tile-type population (Room/Connector/Chasm counts) is preserved, and
/// every DoorwayMarker/Door stays correctly matched, at EVERY intermediate rotation.</item>
/// </list>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class RoomRotationTest
{
	private const int SeedCount = 64;
	private const int TileSize = Room.TileSize;

	[TestCase]
	public void RotatingRoomsFourTimesReturnsToOriginalGeometry()
	{
		var types = Types(withDoors: true);
		var failures = new List<string>();
		for (ulong seed = 1; seed <= SeedCount; seed++)
		{
			Room room = Assemble(types, seed);
			var props = SnapshotProps(room);
			CheckRotationSweep(room, $"seed {seed}", failures);
			if (SnapshotProps(room) != props) failures.Add($"seed {seed}: props did not return to their original transforms");
			room.Free();
		}

		AssertArray(failures)
			.OverrideFailureMessage($"Found {failures.Count} rotation violation(s) across {SeedCount} seeds:\n  "
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
		Room room = Assemble(Types(withDoors: false), 1);

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

	/// <summary>Every Garrison type, entrances and exits included; loaded once per test, not per seed.</summary>
	private static RoomType[] Types(bool withDoors) => (withDoors ? TestFactories.GarrisonRoomsWithDoors() : TestFactories.GarrisonRooms()).AllTypes;

	/// <summary>A room assembled from <paramref name="types"/>, cycling through them by seed.</summary>
	private static Room Assemble(RoomType[] types, ulong seed) => RoomAssembler.Assemble(types[(int)(seed % (ulong)types.Length)], seed);

	private static string SnapshotProps(Room room) => string.Join("|", room.GetNode("Props").GetChildren().OfType<Node3D>()
		.Select(prop => $"{prop.Name}:{prop.Transform.Origin.Snapped(Vector3.One * .001f)}:{prop.Transform.Basis.X.Snapped(Vector3.One * .001f)}:{prop.Transform.Basis.Z.Snapped(Vector3.One * .001f)}"));

	private static bool BasisApproxEquals(Basis a, Basis b)
	{
		return a.X.IsEqualApprox(b.X) && a.Y.IsEqualApprox(b.Y) && a.Z.IsEqualApprox(b.Z);
	}

	/// <summary>
	/// A GridMap cell can legitimately sit at any of Godot's 24 orthogonal
	/// orientations, not just the 4 upright Y-rotations this project's own wall/floor
	/// content happens to use (e.g. a piece tipped over for variety).
	/// Rotation must compose correctly for those too, via the engine's own basis math,
	/// not a lookup table limited to the 4 orientations this project's content
	/// currently exercises.
	/// </summary>
	[TestCase]
	public void RotatingANonUprightGridMapOrientationComposesCorrectly()
	{
		Room room = Assemble(Types(withDoors: false), 1);

		// Far outside the room's own generated content, so this doesn't collide with
		// anything BakeTileMap scans.
		var cell = new Vector3I(1000, 0, 1000);
		const int nonUprightOrientation = 1; // not one of {0,16,10,22}
		room.WallGridMap.SetCellItem(cell, 0, nonUprightOrientation);
		Basis expectedBasis = new Basis(Vector3.Up, Mathf.DegToRad(90f))
			* room.WallGridMap.GetBasisWithOrthogonalIndex(nonUprightOrientation);

		room.Rotate(1);

		Vector3I rotatedCell = new(cell.Z, cell.Y, -cell.X);
		int rotatedOrientation = room.WallGridMap.GetCellItemOrientation(rotatedCell);
		Basis actualBasis = room.WallGridMap.GetBasisWithOrthogonalIndex(rotatedOrientation);

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
	private static void CheckRotationSweep(Room room, string label, List<string> failures)
	{
		var originalFloor = SnapshotCells(room.FloorGridMap);
		var originalWall = SnapshotCells(room.WallGridMap);
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
	}

	/// <summary>Doorway markers stay on connectors and doors stay on their marker's edge after rotation.</summary>
	private static List<string> FindDoorwayViolations(Room room)
	{
		var failures = new List<string>();
		var markers = new List<DoorwayMarker>();

		foreach (Node child in room.GetChildren())
		{
			if (child is DoorwayMarker marker)
			{
				markers.Add(marker);
				Vector2I tile = room.LocalToTile(marker.Position);
				if (!room.Map.IsWithinBounds(tile.X, tile.Y) || !room.Map.IsConnector(tile.X, tile.Y))
				{
					failures.Add($"doorway marker at tile ({tile.X},{tile.Y}) is not a connector");
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

	internal static Dictionary<MapTile, int> CountTileTypes(MapData map)
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