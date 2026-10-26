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
	private const string TestDir = "res://tests/temp_rooms/";
	private const string TestScenePath = TestDir + "test_generated_room.tscn";

	[TestCase]
	public void BuiltRoomsSatisfyRoomContract()
	{
		var builder = GD.Load<ProceduralRoomBuilder>("res://scenes/levels/dungeon/dungeon_procedural_room_builder.tres");
		var failures = new List<string>();

		for (ulong seed = 1; seed <= SeedCount; seed++)
		{
			using var rng = new RandomNumberGenerator { Seed = seed };
			Room room = builder.BuildRoom(rng);
			if (room == null)
			{
				failures.Add($"seed {seed}: builder returned null");
				continue;
			}

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
		builder.MinDoors = 4;
		builder.MaxDoors = 4;
		builder.MaxDoorways = 4;

		var failures = new List<string>();
		for (ulong seed = 1; seed <= SeedCount; seed++)
		{
			using var rng = new RandomNumberGenerator { Seed = seed };
			Room room = builder.BuildRoom(rng);
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
			using var rng = new RandomNumberGenerator { Seed = seed };
			Room room = builder.BuildRoom(rng);
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
	public void PopulateRoomGeneratesAllShapeArchetypes()
	{
		var builder = ResourceLoader.Load<ProceduralRoomBuilder>(
			"res://scenes/levels/dungeon/dungeon_procedural_room_builder.tres",
			cacheMode: ResourceLoader.CacheMode.Ignore);
		var room = new Room();

		RoomShapeArchetype[] archetypes =
		[
			RoomShapeArchetype.Rectangle,
			RoomShapeArchetype.LShape,
			RoomShapeArchetype.SteppedZ,
			RoomShapeArchetype.Cross,
			RoomShapeArchetype.TJunction,
			RoomShapeArchetype.UShape,
		];

		foreach (var shape in archetypes)
		{
			using var rng = new RandomNumberGenerator { Seed = 42 };
			builder.ShapeArchetype = shape;
			bool success = builder.PopulateRoom(room, rng);

			AssertBool(success).IsTrue();
			AssertObject(room.FloorGridMap).IsNotNull();
			AssertObject(room.WallGridMap).IsNotNull();
			AssertObject(room.DecorationGridMap).IsNotNull();
			AssertBool(room.FloorGridMap.GetUsedCells().Count > 0).IsTrue();
			AssertObject(room.Map).IsNotNull();
			AssertBool(room.Bounds.Size.X > 0 && room.Bounds.Size.Y > 0).IsTrue();
			AssertInt(room.Bounds.Size.X % Room.TileSize).IsEqual(0);
			AssertInt(room.Bounds.Size.Y % Room.TileSize).IsEqual(0);
		}

		room.QueueFree();
	}

	[TestCase]
	public void AllMaterialsAndPurposesTileWithoutOverlapOrGaps()
	{
		var builder = ResourceLoader.Load<ProceduralRoomBuilder>(
			"res://scenes/levels/dungeon/dungeon_procedural_room_builder.tres",
			cacheMode: ResourceLoader.CacheMode.Ignore);
		var room = new Room();
		var failures = new List<string>();

		RoomMaterial[] materials =
		[
			RoomMaterial.StoneSlab,
			RoomMaterial.CobbleGrid,
			RoomMaterial.WoodPlanks,
			RoomMaterial.StoneAndWood,
			RoomMaterial.Earthen,
			RoomMaterial.RuinedMix,
		];

		RoomPurpose[] purposes =
		[
			RoomPurpose.Chamber,
			RoomPurpose.GreatHall,
			RoomPurpose.Temple,
			RoomPurpose.Colonnade,
			RoomPurpose.SewerCistern,
			RoomPurpose.SewerAqueduct,
			RoomPurpose.RuinedCollapse,
		];

		ulong seed = 100;
		foreach (var material in materials)
		{
			foreach (var purpose in purposes)
			{
				seed++;
				using var rng = new RandomNumberGenerator { Seed = seed };
				builder.Material = material;
				builder.Purpose = purpose;
				builder.MinTiles = 4;
				builder.MaxTiles = 6;
				builder.PopulateRoom(room, rng);

				foreach (string coverageFailure in FindFloorCoverageViolations(room))
				{
					failures.Add($"{material}/{purpose} (seed {seed}): {coverageFailure}");
				}

				MeshLibrary lib = room.FloorGridMap.MeshLibrary;
				var nonCornerRects = new List<(Vector3I Cell, string Name, Rect2 Rect)>();
				int? woodOrientation = null;

				foreach (Vector3I cell in room.FloorGridMap.GetUsedCells())
				{
					int item = room.FloorGridMap.GetCellItem(cell);
					string itemName = lib.GetItemName(item);
					int rot = room.FloorGridMap.GetCellItemOrientation(cell);

					if (itemName.StartsWith("floor_wood_"))
					{
						if (woodOrientation == null)
						{
							woodOrientation = rot;
						}
						else if (woodOrientation.Value != rot)
						{
							failures.Add($"{material}/{purpose} (seed {seed}): wood tile '{itemName}' at {cell} has orientation {rot}, expected uniform {woodOrientation.Value}");
						}
					}

					// Corner tiles are the only ones allowed to overlay a base tile.
					if (itemName.EndsWith("_corner"))
					{
						continue;
					}

					Aabb worldAabb = WallFootprints.FootprintWorld(lib.GetItemMesh(item).GetAabb(), rot, cell);
					// Shrink slightly by 0.05 to test interior overlap rather than shared boundary edges.
					var rect = new Rect2(
						new Vector2(worldAabb.Position.X + 0.05f, worldAabb.Position.Z + 0.05f),
						new Vector2(worldAabb.Size.X - 0.1f, worldAabb.Size.Z - 0.1f));
					nonCornerRects.Add((cell, itemName, rect));
				}

				for (int i = 0; i < nonCornerRects.Count; i++)
				{
					for (int j = i + 1; j < nonCornerRects.Count; j++)
					{
						if (nonCornerRects[i].Rect.Intersects(nonCornerRects[j].Rect))
						{
							failures.Add($"{material}/{purpose} (seed {seed}): '{nonCornerRects[i].Name}' at {nonCornerRects[i].Cell} overlaps '{nonCornerRects[j].Name}' at {nonCornerRects[j].Cell}");
						}
					}
				}
			}
		}

		room.QueueFree();
		AssertArray(failures)
			.OverrideFailureMessage("Floor coverage/overlap/wood orientation violations:\n  " + string.Join("\n  ", failures))
			.IsEmpty();
	}

	[TestCase]
	public void ChasmsAreDisabledByDefaultAndConfigurable()
	{
		var builder = ResourceLoader.Load<ProceduralRoomBuilder>(
			"res://scenes/levels/dungeon/dungeon_procedural_room_builder.tres",
			cacheMode: ResourceLoader.CacheMode.Ignore);
		AssertBool(builder.EnableChasms).IsFalse();

		builder.ShapeArchetype = RoomShapeArchetype.Rectangle;
		builder.ChasmChance = 1f;
		var room = new Room();

		for (ulong seed = 1; seed <= 20; seed++)
		{
			using var rng = new RandomNumberGenerator { Seed = seed };
			builder.PopulateRoom(room, rng);
			for (int x = 0; x < room.Map.Width; x++)
			{
				for (int z = 0; z < room.Map.Height; z++)
				{
					AssertBool(room.Map.IsChasm(x, z)).IsFalse();
				}
			}
		}

		builder.EnableChasms = true;
		builder.MinTiles = 5;
		builder.MaxTiles = 6;
		using var chasmRng = new RandomNumberGenerator { Seed = 1 };
		builder.PopulateRoom(room, chasmRng);

		bool foundChasm = false;
		for (int x = 0; x < room.Map.Width; x++)
		{
			for (int z = 0; z < room.Map.Height; z++)
			{
				if (room.Map.IsChasm(x, z))
				{
					foundChasm = true;
				}
			}
		}
		AssertBool(foundChasm).IsTrue();

		room.QueueFree();
	}

	[TestCase]
	public void GenerateWallsRespectsOpeningsAndDoorsRange()
	{
		var builder = ResourceLoader.Load<ProceduralRoomBuilder>(
			"res://scenes/levels/dungeon/dungeon_procedural_room_builder.tres",
			cacheMode: ResourceLoader.CacheMode.Ignore);
		builder.GenerateWalls = true;
		builder.MinDoorways = 2;
		builder.MaxDoorways = 3;
		builder.MinDoors = 1;
		builder.MaxDoors = 2;

		var room = new Room();
		for (ulong seed = 1; seed <= 25; seed++)
		{
			using var rng = new RandomNumberGenerator { Seed = seed };
			bool ok = builder.PopulateRoom(room, rng);
			AssertBool(ok).IsTrue();
			AssertBool(room.WallGridMap.GetUsedCells().Count > 0).IsTrue();

			int markers = 0;
			int doors = 0;
			foreach (Node child in room.GetChildren())
			{
				if (child is DoorwayMarker)
				{
					markers++;
				}
				else if (child is Door)
				{
					doors++;
				}
			}

			AssertBool(markers >= 2 && markers <= 3).IsTrue();
			AssertBool(doors >= 1 && doors <= 2 && doors <= markers).IsTrue();

			// Verify every DoorwayMarker validated into a real connector (not blocked by a generated wall).
			int connectorCount = 0;
			for (int x = 0; x < room.Map.Width; x++)
			{
				for (int z = 0; z < room.Map.Height; z++)
				{
					if (room.Map.IsConnector(x, z))
					{
						connectorCount++;
					}
				}
			}
			AssertInt(connectorCount).IsEqual(markers);
		}

		room.QueueFree();
	}

	[TestCase]
	public void SaveRoomScenePacksAndReloadsValidRoom()
	{
		var builder = ResourceLoader.Load<ProceduralRoomBuilder>(
			"res://scenes/levels/dungeon/dungeon_procedural_room_builder.tres",
			cacheMode: ResourceLoader.CacheMode.Ignore);
		builder.ShapeArchetype = RoomShapeArchetype.Cross;
		builder.GenerateWalls = true;

		bool saved = builder.SaveRoomScene(TestScenePath, 101);
		AssertBool(saved).IsTrue();
		AssertBool(FileAccess.FileExists(TestScenePath)).IsTrue();

		PackedScene loadedScene = ResourceLoader.Load<PackedScene>(TestScenePath, cacheMode: ResourceLoader.CacheMode.Ignore);
		AssertObject(loadedScene).IsNotNull();

		Room reloadedRoom = loadedScene.Instantiate<Room>();
		AssertObject(reloadedRoom).IsNotNull();

		AssertObject(reloadedRoom.FloorGridMap).IsNotNull();
		AssertObject(reloadedRoom.WallGridMap).IsNotNull();
		AssertObject(reloadedRoom.DecorationGridMap).IsNotNull();
		AssertBool(reloadedRoom.FloorGridMap.GetUsedCells().Count > 0).IsTrue();
		AssertBool(reloadedRoom.WallGridMap.GetUsedCells().Count > 0).IsTrue();

		reloadedRoom.BakeTileMap();
		AssertBool(reloadedRoom.Bounds.Size.X > 0 && reloadedRoom.Bounds.Size.Y > 0).IsTrue();
		AssertObject(reloadedRoom.Map).IsNotNull();

		reloadedRoom.QueueFree();

		if (FileAccess.FileExists(TestScenePath))
		{
			DirAccess.RemoveAbsolute(TestScenePath);
		}
		if (DirAccess.DirExistsAbsolute(TestDir))
		{
			DirAccess.RemoveAbsolute(TestDir);
		}
	}

	[TestCase]
	public void OverwriteGuardPreventsAccidentalOverwrite()
	{
		var builder = ResourceLoader.Load<ProceduralRoomBuilder>(
			"res://scenes/levels/dungeon/dungeon_procedural_room_builder.tres",
			cacheMode: ResourceLoader.CacheMode.Ignore);
		builder.ShapeArchetype = RoomShapeArchetype.Rectangle;

		bool firstSave = builder.SaveRoomScene(TestScenePath, 202, overwrite: true);
		AssertBool(firstSave).IsTrue();

		bool secondSave = builder.SaveRoomScene(TestScenePath, 203, overwrite: false);
		AssertBool(secondSave).IsFalse();

		if (FileAccess.FileExists(TestScenePath))
		{
			DirAccess.RemoveAbsolute(TestScenePath);
		}
		if (DirAccess.DirExistsAbsolute(TestDir))
		{
			DirAccess.RemoveAbsolute(TestDir);
		}
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
	public void HallAisleColumnsFlankCentralWalkway()
	{
		var footprint = MakeRectFootprint(7, 5);
		var corners = ProceduralRoomBuilder.GetHallAisleColumns(footprint, spacing: 2);
		AssertBool(corners.Count > 0).IsTrue();
		int cz = footprint.Height / 2;
		foreach (Vector2I corner in corners)
		{
			AssertBool(ProceduralRoomBuilder.IsInteriorCorner(footprint, corner.X, corner.Y)).IsTrue();
			AssertBool(corner.Y == cz || corner.Y == cz + 1).IsTrue();
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
		var corners = new HashSet<Vector2I>(builder.GetSymmetricScatterColumns(footprint, new RandomNumberGenerator { Seed = 1 }));
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
				Vector2I tile = room.LocalToTile(marker.Position);
				if (!room.Map.IsWithinBounds(tile.X, tile.Y) || !room.Map.IsConnector(tile.X, tile.Y))
				{
					failures.Add($"doorway marker at tile ({tile.X},{tile.Y}) is not a connector");
				}

				// Exactly one direction flag, not a combined corner (see
				// ProceduralRoomBuilder.CollectDoorwayCandidates): a corridor can only
				// ever connect through one direction, so a marker sanctioning a second,
				// unconnected direction would leave that side wall-free with no door.
				var directions = marker.GetDirectionVectors();
				if (directions.Count != 1)
				{
					failures.Add($"doorway marker at tile ({tile.X},{tile.Y}) has {directions.Count} "
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
	/// regardless of which floor item(s)/sizes were used -- shared so the floor-style
	/// and gutter tests can reuse the exact same coverage contract as the general room
	/// contract test.
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

			int rot = room.FloorGridMap.GetCellItemOrientation(cell);
			Aabb worldAabb = WallFootprints.FootprintWorld(mesh.GetAabb(), rot, cell);
			footprints.Add(new Rect2(
				new Vector2(worldAabb.Position.X, worldAabb.Position.Z),
				new Vector2(worldAabb.Size.X, worldAabb.Size.Z)));
		}

		return footprints;
	}

	private static IEnumerable<Vector2> TileQuadrantCenters(Room room, int tileX, int tileZ)
	{
		Vector3I center = room.TileCenterCell(tileX, tileZ);
		yield return new Vector2(center.X - 1, center.Z - 1);
		yield return new Vector2(center.X + 1, center.Z - 1);
		yield return new Vector2(center.X - 1, center.Z + 1);
		yield return new Vector2(center.X + 1, center.Z + 1);
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