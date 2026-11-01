namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Correctness contracts for Room.Rotate, swept across seeds using every room type
/// (irregular footprints, doorways, doors, props and stairwell pits -- the hardest shapes to get right):
/// <list type="bullet">
/// <item>Rotating four quarter turns (360 degrees) returns the exact original GridMap
/// cells, child transforms and tile map -- rotation must be a lossless, invertible
/// permutation of the tile lattice, not an approximation that drifts.</item>
/// <item>A single quarter turn swaps Map.Width/Height, and every tile-type population
/// (Room/Connector/Chasm counts) is preserved.</item>
/// <item>At EVERY intermediate rotation the tile map, rotated as data, still matches the
/// rotated geometry: floor tiles lie under floor cells, each blueprint doorway turned
/// with the room is a connector facing its turned direction, and its door stays in
/// its gap.</item>
/// </list>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class RoomRotationTest
{
	private const int SeedCount = 64;
	private const int TileSize = Room.TileSize;

	[TestCase]
	public void RotatingProceduralRoomsKeepsTheirMapOnTheirGeometry()
	{
		var failures = new List<string>();
		int doors = 0;
		for (ulong seed = 1; seed <= SeedCount; seed++)
		{
			var blueprint = Build(seed, withDoors: true);
			doors += blueprint.Room.Doors.Count;
			CheckRotationSweep(blueprint, $"seed {seed}", failures);
			blueprint.Room.Free();
		}

		AssertInt(doors).IsGreater(0);
		AssertArray(failures)
			.OverrideFailureMessage($"Found {failures.Count} rotation violation(s) across {SeedCount} procedural seeds:\n  "
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
		Room room = Build(1).Room;

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

	/// <summary>
	/// A GridMap cell can legitimately sit at any of Godot's 24 orthogonal
	/// orientations, not just the 4 upright Y-rotations this project's own wall/floor
	/// content happens to use (e.g. a decoration tipped over for variety).
	/// Rotation must compose correctly for those too, via the engine's own basis math,
	/// not a lookup table limited to the 4 orientations this project's content
	/// currently exercises.
	/// </summary>
	[TestCase]
	public void RotatingANonUprightGridMapOrientationComposesCorrectly()
	{
		Room room = Build(1).Room;

		// Far outside the room's own generated content, so this doesn't collide with it.
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

	/// <summary>A room assembled from the pool's types (entrances and exits included), cycling through them by seed.</summary>
	private static RoomBlueprint Build(ulong seed, bool withDoors = false)
	{
		var factory = withDoors ? TestFactories.RoomPoolWithDoors() : TestFactories.RoomPool();
		RoomType[] types = [.. factory.RoomTypes, .. factory.EntranceTypes, .. factory.ExitTypes];
		return RoomAssembler.Build(types[(int)(seed % (ulong)types.Length)], seed);
	}

	/// <summary>
	/// Rotates an assembled room through all 4 cumulative quarter-turn states (90/180/270/360)
	/// from its original orientation, checking invariants at every step and a full
	/// original-state match once back at 360 (== 0).
	/// </summary>
	private static void CheckRotationSweep(RoomBlueprint blueprint, string label, List<string> failures)
	{
		Room room = blueprint.Room;
		var originalFloor = SnapshotCells(room.FloorGridMap);
		var originalWall = SnapshotCells(room.WallGridMap);
		var originalDecoration = SnapshotCells(room.DecorationGridMap);
		string originalChildren = SnapshotChildren(room);
		string originalMap = Fingerprint(room);
		int originalWidth = room.Map.Width;
		int originalHeight = room.Map.Height;
		var originalTileCounts = CountTileTypes(room.Map);
		var doorways = blueprint.Doorways.Select(doorway => (doorway, room.Doors.GetValueOrDefault(doorway.Tile))).ToList();

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

			foreach (string violation in FindFloorViolations(room).Concat(FindDoorwayViolations(room, doorways, cumulativeSteps)))
			{
				failures.Add($"{context}: {violation}");
			}
		}

		// 4 quarter turns is a full 360-degree rotation: must be back to the exact
		// original state, not merely the same dimensions/tile counts.
		AssertGridMapMatches(originalFloor, SnapshotCells(room.FloorGridMap), $"{label}: FloorGridMap", failures);
		AssertGridMapMatches(originalWall, SnapshotCells(room.WallGridMap), $"{label}: WallGridMap", failures);
		AssertGridMapMatches(originalDecoration, SnapshotCells(room.DecorationGridMap), $"{label}: DecorationGridMap", failures);
		if (SnapshotChildren(room) != originalChildren) failures.Add($"{label}: children did not return to their original transforms");
		if (Fingerprint(room) != originalMap) failures.Add($"{label}: tile map, origin or doors did not return to the original");
	}

	/// <summary>Map tiles agree with the rotated floor geometry: floor and doorway tiles hold floor cells, pits and empty tiles none.</summary>
	private static IEnumerable<string> FindFloorViolations(Room room)
	{
		for (int x = 0; x < room.Map.Width; x++)
		for (int z = 0; z < room.Map.Height; z++)
		{
			var center = room.Origin + new Vector3I(x, 0, z) * TileSize;
			bool hasFloor = false;
			for (int dx = -TileSize / 2; dx < TileSize / 2 && !hasFloor; dx++)
			for (int dz = -TileSize / 2; dz < TileSize / 2 && !hasFloor; dz++)
				hasFloor = room.FloorGridMap.GetCellItem(center + new Vector3I(dx, 0, dz)) >= 0;
			bool walkable = room.Map.IsRoom(x, z) || room.Map.IsConnector(x, z);
			if (hasFloor != walkable) yield return $"tile ({x},{z}) is {room.Map.Tiles[x, z]} but {(hasFloor ? "has" : "lacks")} floor cells";
		}
	}

	/// <summary>
	/// Each blueprint doorway, turned with the room by the engine's own rotation, lands on a connector facing its turned
	/// direction, and the door set into it stays registered there and in its gap.
	/// </summary>
	private static List<string> FindDoorwayViolations(Room room, List<(RoomDoorway Doorway, Door Door)> doorways, int steps)
	{
		var failures = new List<string>();
		var turn = new Basis(Vector3.Up, Mathf.DegToRad(90f * steps));
		foreach (var (doorway, door) in doorways)
		{
			Vector3 approach = turn * doorway.Approach;
			Vector3 outward = turn * doorway.Outward;
			var tile = new Vector2I(Mathf.RoundToInt((approach.X - room.Origin.X) / TileSize), Mathf.RoundToInt((approach.Z - room.Origin.Z) / TileSize));
			var direction = new Vector2I(Mathf.RoundToInt(outward.X), Mathf.RoundToInt(outward.Z));
			if (!room.Map.IsWithinBounds(tile.X, tile.Y) || !room.Map.IsConnector(tile.X, tile.Y))
			{
				failures.Add($"doorway at tile ({tile.X},{tile.Y}) is not a connector");
				continue;
			}

			var directions = room.Map.GetConnectorDirections(tile.X, tile.Y);
			if (directions.Count != 1 || directions[0] != direction)
			{
				failures.Add($"doorway at tile ({tile.X},{tile.Y}) opens toward [{string.Join(",", directions)}], expected {direction}");
			}

			room.Doors.TryGetValue(tile, out var registered);
			if (registered != door)
			{
				failures.Add($"doorway at tile ({tile.X},{tile.Y}) lost its door");
			}
			else if (door != null && door.Position.DistanceTo(turn * doorway.Edge) > 0.01f)
			{
				failures.Add($"door at {door.Position} left its doorway's gap at {turn * doorway.Edge}");
			}
		}

		if (room.Doors.Count != doorways.Count(pair => pair.Door != null))
		{
			failures.Add($"room lists {room.Doors.Count} doors, expected {doorways.Count(pair => pair.Door != null)}");
		}

		return failures;
	}

	/// <summary>Every tile with its connector directions, the origin and each door's tile.</summary>
	private static string Fingerprint(Room room)
	{
		var builder = new StringBuilder($"{room.Origin}\n");
		for (int z = 0; z < room.Map.Height; z++)
		{
			for (int x = 0; x < room.Map.Width; x++)
			{
				builder.Append(room.Map.Tiles[x, z]).Append(string.Join("", room.Map.GetConnectorDirections(x, z))).Append(' ');
			}
			builder.Append('\n');
		}
		foreach (var (tile, door) in room.Doors.OrderBy(pair => pair.Key.X).ThenBy(pair => pair.Key.Y))
		{
			builder.Append($"{tile}:{door.Name} ");
		}

		return builder.ToString();
	}

	/// <summary>Transforms of the room's doors, frames and props.</summary>
	private static string SnapshotChildren(Room room) => string.Join("|", room.GetChildren().OfType<Node3D>()
		.Concat(room.GetNode("Props").GetChildren().OfType<Node3D>()).Where(node => node is not GridMap)
		.Select(node => $"{node.Name}:{node.Transform.Origin.Snapped(Vector3.One * .001f)}:{node.Transform.Basis.X.Snapped(Vector3.One * .001f)}:{node.Transform.Basis.Z.Snapped(Vector3.One * .001f)}"));

	private static bool BasisApproxEquals(Basis a, Basis b)
	{
		return a.X.IsEqualApprox(b.X) && a.Y.IsEqualApprox(b.Y) && a.Z.IsEqualApprox(b.Z);
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