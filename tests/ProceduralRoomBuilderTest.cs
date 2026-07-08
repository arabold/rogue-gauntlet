namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Invariants of procedurally built rooms, swept across seeds: the room must bake into
/// valid map data of the configured size, expose connectors (or it could never be
/// reached), have every walkable tile fully covered by floor meshes (a misaligned
/// anchor means a visual hole in the ground and a hole in the navmesh), and leave pit
/// tiles floorless so they bake into chasms.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ProceduralRoomBuilderTest
{
	private const int SeedCount = 60;
	private const int TileSize = Room.TileSize;

	[TestCase]
	public void BuiltRoomsSatisfyRoomContract()
	{
		var builder = GD.Load<ProceduralRoomBuilder>("res://scenes/levels/dungeon/dungeon_procedural_room_builder.tres");
		var failures = new List<string>();

		for (ulong seed = 1; seed <= SeedCount; seed++)
		{
			GD.Seed(seed);
			Room room = builder.BuildRoom();
			if (room == null)
			{
				failures.Add($"seed {seed}: builder returned null");
				continue;
			}

			room.BakeTileMap();
			foreach (string failure in FindRoomContractViolations(room, builder))
			{
				failures.Add($"seed {seed}: {failure}");
			}

			room.QueueFree();
		}

		AssertArray(failures)
			.OverrideFailureMessage($"Found {failures.Count} contract violation(s) across {SeedCount} seeds:\n  "
				+ string.Join("\n  ", failures))
			.IsEmpty();
	}

	[TestCase]
	public void BuiltDoorwaysAndDoorsFollowGeometryContract()
	{
		// Ignore the resource cache: mutating exported properties on a cached instance
		// would leak into other tests that load the same resource by path.
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
			foreach (string failure in FindDoorwayContractViolations(room))
			{
				failures.Add($"seed {seed}: {failure}");
			}

			room.QueueFree();
		}

		AssertArray(failures)
			.OverrideFailureMessage($"Found {failures.Count} doorway contract violation(s) across {SeedCount} seeds:\n  "
				+ string.Join("\n  ", failures))
			.IsEmpty();
	}

	[TestCase]
	public void PlacedColumnsSitAtValidInteriorCorners()
	{
		var builder = ResourceLoader.Load<ProceduralRoomBuilder>(
			"res://scenes/levels/dungeon/dungeon_procedural_room_builder.tres",
			cacheMode: ResourceLoader.CacheMode.Ignore);
		builder.ColumnChance = 1f;

		var failures = new List<string>();
		for (ulong seed = 1; seed <= SeedCount; seed++)
		{
			GD.Seed(seed);
			Room room = builder.BuildRoom();
			room.BakeTileMap();
			foreach (Vector3I cell in room.DecorationGridMap.GetUsedCells())
			{
				int tileX = (cell.X + TileSize / 2) / TileSize;
				int tileZ = (cell.Z + TileSize / 2) / TileSize;
				bool valid = IsWalkable(room.Map, tileX, tileZ) && IsWalkable(room.Map, tileX - 1, tileZ)
					&& IsWalkable(room.Map, tileX, tileZ - 1) && IsWalkable(room.Map, tileX - 1, tileZ - 1);
				if (!valid)
				{
					failures.Add($"seed {seed}: column at cell {cell} is not at a valid interior corner "
						+ $"(resolved to tile ({tileX},{tileZ}))");
				}
			}

			room.QueueFree();
		}

		AssertArray(failures)
			.OverrideFailureMessage($"Found {failures.Count} misplaced column(s) across {SeedCount} seeds:\n  "
				+ string.Join("\n  ", failures))
			.IsEmpty();
	}

	[TestCase]
	public void SmallFloorItemsTileWithoutGaps()
	{
		// The default resource only configures "large" (4x4) floor items, so the
		// generalized any-size anchor formula in ProceduralRoomBuilder.PlaceFloorTile
		// otherwise never gets exercised by "small" (2x2) items.
		var builder = ResourceLoader.Load<ProceduralRoomBuilder>(
			"res://scenes/levels/dungeon/dungeon_procedural_room_builder.tres",
			cacheMode: ResourceLoader.CacheMode.Ignore);
		builder.FloorItems = new[] { "floor_dirt_small_A" };
		builder.AccentFloorItems = System.Array.Empty<string>();

		var failures = new List<string>();
		for (ulong seed = 1; seed <= SeedCount; seed++)
		{
			GD.Seed(seed);
			Room room = builder.BuildRoom();
			room.BakeTileMap();
			foreach (string failure in FindFloorCoverageViolations(room))
			{
				failures.Add($"seed {seed}: {failure}");
			}

			room.QueueFree();
		}

		AssertArray(failures)
			.OverrideFailureMessage($"Found {failures.Count} floor coverage violation(s) across {SeedCount} seeds:\n  "
				+ string.Join("\n  ", failures))
			.IsEmpty();
	}

	[TestCase]
	public void MixedAccentFloorTilesWithoutGaps()
	{
		var builder = ResourceLoader.Load<ProceduralRoomBuilder>(
			"res://scenes/levels/dungeon/dungeon_procedural_room_builder.tres",
			cacheMode: ResourceLoader.CacheMode.Ignore);
		builder.FloorAccentChance = 1f;

		var failures = new List<string>();
		for (ulong seed = 1; seed <= SeedCount; seed++)
		{
			GD.Seed(seed);
			Room room = builder.BuildRoom();
			room.BakeTileMap();
			foreach (string failure in FindFloorCoverageViolations(room))
			{
				failures.Add($"seed {seed}: {failure}");
			}

			room.QueueFree();
		}

		AssertArray(failures)
			.OverrideFailureMessage($"Found {failures.Count} floor coverage violation(s) across {SeedCount} seeds:\n  "
				+ string.Join("\n  ", failures))
			.IsEmpty();
	}

	[TestCase]
	public void CenterSingleColumnPicksNearestValidCorner()
	{
		var footprint = MakeRectFootprint(7, 7);
		var corners = ProceduralRoomBuilder.GetCenterSingleColumns(footprint);
		AssertInt(corners.Count).IsEqual(1);
		AssertBool(ProceduralRoomBuilder.IsInteriorCorner(footprint, corners[0].X, corners[0].Y)).IsTrue();
		// A 7x7 footprint (tile indices 0..6) has an exact center corner at (3,3).
		AssertObject(corners[0]).IsEqual(new Vector2I(3, 3));
	}

	[TestCase]
	public void CenterClusterColumnsSurroundCentroid()
	{
		var footprint = MakeRectFootprint(8, 6);
		var corners = ProceduralRoomBuilder.GetCenterClusterColumns(footprint);
		AssertInt(corners.Count).IsEqual(4);
		foreach (Vector2I corner in corners)
		{
			AssertBool(ProceduralRoomBuilder.IsInteriorCorner(footprint, corner.X, corner.Y)).IsTrue();
			AssertBool(Mathf.Abs(corner.X - footprint.Width / 2f) <= 1f).IsTrue();
			AssertBool(Mathf.Abs(corner.Y - footprint.Height / 2f) <= 1f).IsTrue();
		}
	}

	[TestCase]
	public void WallColonnadeColumnsStayOnBoundaryRing()
	{
		var footprint = MakeRectFootprint(9, 5);
		var corners = ProceduralRoomBuilder.GetWallColonnadeColumns(footprint, spacing: 2);
		AssertBool(corners.Count > 0).IsTrue();
		foreach (Vector2I corner in corners)
		{
			bool onRing = corner.X == 1 || corner.X == footprint.Width - 1
				|| corner.Y == 1 || corner.Y == footprint.Height - 1;
			AssertBool(onRing).IsTrue();
		}
	}

	[TestCase]
	public void SymmetricScatterColumnsAreMirroredOnBothAxes()
	{
		var footprint = MakeRectFootprint(8, 6);
		var builder = new ProceduralRoomBuilder { ColumnChance = 1f };
		var corners = new HashSet<Vector2I>(builder.GetSymmetricScatterColumns(footprint));
		AssertBool(corners.Count > 0).IsTrue();
		foreach (Vector2I corner in corners)
		{
			AssertBool(corners.Contains(new Vector2I(footprint.Width - corner.X, corner.Y))).IsTrue();
			AssertBool(corners.Contains(new Vector2I(corner.X, footprint.Height - corner.Y))).IsTrue();
			AssertBool(corners.Contains(new Vector2I(footprint.Width - corner.X, footprint.Height - corner.Y))).IsTrue();
		}
	}

	private static ProceduralRoomBuilder.Footprint MakeRectFootprint(int width, int height)
	{
		var tiles = new bool[width, height];
		for (int x = 0; x < width; x++)
		{
			for (int z = 0; z < height; z++)
			{
				tiles[x, z] = true;
			}
		}

		return new ProceduralRoomBuilder.Footprint(tiles);
	}

	private static bool IsWalkable(MapData map, int x, int z)
	{
		return map.IsWithinBounds(x, z) && (map.IsRoom(x, z) || map.IsConnector(x, z));
	}

	private static List<string> FindDoorwayContractViolations(Room room)
	{
		var failures = new List<string>();
		var markers = new List<DoorwayMarker>();
		int doorCount = 0;

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

				// Exactly one direction flag, not a combined corner (see
				// ProceduralRoomBuilder.CollectDoorwayCandidates): a corridor can only
				// ever connect through one direction, so a marker sanctioning a second,
				// unconnected direction would leave that side wall-free with no door.
				var directions = marker.GetDirectionVectors();
				if (directions.Count != 1)
				{
					failures.Add($"doorway marker at tile ({tileX},{tileZ}) has {directions.Count} "
						+ "sanctioned directions instead of exactly 1");
				}
			}
			else if (child is Door)
			{
				doorCount++;
				var doorNode = (Node3D)child;

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
					failures.Add($"door at {doorNode.Position} is not within range of any doorway marker "
						+ "(MapGenerator.TryFindNearestConnector would fail to match it)");
					continue;
				}

				// The door must sit exactly at its marker's tile edge, in the marker's
				// own sanctioned direction -- not just "near" it. A door facing a
				// different side than the one that ends up connected would be
				// pointless (embedded against a generated wall) while the real,
				// connected side stays a bare, undoored opening.
				Vector2I direction = nearestMarker.GetDirectionVectors()[0];
				Vector3 expectedPosition = nearestMarker.Position + new Vector3(direction.X, 0, direction.Y) * (TileSize / 2f);
				if (doorNode.Position.DistanceTo(expectedPosition) > 0.01f)
				{
					failures.Add($"door at {doorNode.Position} does not sit at its marker's sanctioned "
						+ $"direction (expected {expectedPosition})");
				}
			}
		}

		if (doorCount > markers.Count)
		{
			failures.Add($"{doorCount} doors placed for only {markers.Count} doorways");
		}

		return failures;
	}

	private static List<string> FindRoomContractViolations(Room room, ProceduralRoomBuilder builder)
	{
		var failures = new List<string>();
		MapData map = room.Map;
		if (map == null)
		{
			failures.Add("room baked no map data");
			return failures;
		}

		if (map.Width < builder.MinTiles || map.Width > builder.MaxTiles
			|| map.Height < builder.MinTiles || map.Height > builder.MaxTiles)
		{
			failures.Add($"room size {map.Width}x{map.Height} outside configured range "
				+ $"[{builder.MinTiles},{builder.MaxTiles}]");
		}

		failures.AddRange(FindFloorCoverageViolations(room));

		bool hasConnector = false;
		bool hasRoomTile = false;
		for (int x = 0; x < map.Width; x++)
		{
			for (int z = 0; z < map.Height; z++)
			{
				if (map.IsConnector(x, z) && map.GetConnectorDirections(x, z).Count > 0)
				{
					hasConnector = true;
				}

				if (map.IsRoom(x, z))
				{
					hasRoomTile = true;
				}
			}
		}

		if (!hasConnector)
		{
			failures.Add("room has no connector tiles");
		}

		if (!hasRoomTile)
		{
			// Every floor tile bakes as a Connector (Room.BakeInferredConnectors) when
			// none is far enough from an edge to be interior -- a degenerate room with
			// no true floor, only doorway-like thresholds.
			failures.Add("room has no interior floor tile (every tile bakes as a connector)");
		}

		return failures;
	}

	/// <summary>
	/// Every walkable tile must be fully floor-covered and every chasm tile floorless,
	/// regardless of which floor item(s)/sizes were used -- shared so the size-generalized
	/// and accent-mixing tests below can reuse the exact same coverage contract as the
	/// general room contract test.
	/// </summary>
	private static List<string> FindFloorCoverageViolations(Room room)
	{
		var failures = new List<string>();
		MapData map = room.Map;
		var floorFootprints = CollectFloorFootprints(room);
		for (int x = 0; x < map.Width; x++)
		{
			for (int z = 0; z < map.Height; z++)
			{
				if (map.IsRoom(x, z) || map.IsConnector(x, z))
				{
					// Probe the four quadrant centers of the tile: with 1x1, 2x2, or
					// 4x4 floor meshes, four covered probes imply the tile is fully floored.
					foreach (var probe in TileQuadrantCenters(room, x, z))
					{
						if (!IsCovered(floorFootprints, probe))
						{
							failures.Add($"tile ({x},{z}) has a floor hole at {probe}");
						}
					}
				}
				else if (map.IsChasm(x, z))
				{
					foreach (var probe in TileQuadrantCenters(room, x, z))
					{
						if (IsCovered(floorFootprints, probe))
						{
							failures.Add($"chasm tile ({x},{z}) has floor at {probe}");
						}
					}
				}
			}
		}

		return failures;
	}

	/// <summary>World-space XZ rectangles of every floor mesh, from its AABB at its anchor cell.</summary>
	private static List<Rect2> CollectFloorFootprints(Room room)
	{
		var footprints = new List<Rect2>();
		MeshLibrary library = room.FloorGridMap.MeshLibrary;
		foreach (Vector3I cell in room.FloorGridMap.GetUsedCells())
		{
			Mesh mesh = library.GetItemMesh(room.FloorGridMap.GetCellItem(cell));
			if (mesh == null)
			{
				continue;
			}

			Aabb aabb = mesh.GetAabb();
			footprints.Add(new Rect2(
				new Vector2(cell.X + aabb.Position.X, cell.Z + aabb.Position.Z),
				new Vector2(aabb.Size.X, aabb.Size.Z)));
		}

		return footprints;
	}

	private static IEnumerable<Vector2> TileQuadrantCenters(Room room, int tileX, int tileZ)
	{
		// Tile (x,z) covers grid units [x*4 + bounds.X - 2, x*4 + bounds.X + 2)
		// (see Room.BakeTileMap); its quadrant centers sit 1 unit off the tile center.
		float centerX = tileX * TileSize + room.Bounds.Position.X;
		float centerZ = tileZ * TileSize + room.Bounds.Position.Y;
		yield return new Vector2(centerX - 1, centerZ - 1);
		yield return new Vector2(centerX + 1, centerZ - 1);
		yield return new Vector2(centerX - 1, centerZ + 1);
		yield return new Vector2(centerX + 1, centerZ + 1);
	}

	private static bool IsCovered(List<Rect2> footprints, Vector2 point)
	{
		foreach (Rect2 footprint in footprints)
		{
			if (footprint.HasPoint(point))
			{
				return true;
			}
		}

		return false;
	}
}
