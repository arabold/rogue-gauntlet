namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Coded recipes retain their identity, deterministic interiors and safe passage after placement and rotation.</summary>
[TestSuite, RequireGodotRuntime]
public class RoomGeneratorTest
{
	private const string Root = "res://scenes/levels/garrison/";

	[TestCase("hall")]
	[TestCase("cellar")]
	public void SizeSeedAndRotationSweepKeepsThemedInteriorsConnected(string kind)
	{
		var generator = ResourceLoader.Load<RoomGenerator>($"{Root}rooms/{kind}/garrison_{kind}_generator.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		AssertArray(generator.Validate()).IsEmpty();
		int total = 0;
		foreach (int size in new[] { 5, 7 })
		for (ulong seed = 1; seed <= 3; seed++)
		{
			generator.ShellBuilder.MinTiles = generator.ShellBuilder.MaxTiles = size;
			if (generator is HallRoomGenerator hall)
			{
				hall.WidthTiles = size;
				hall.LengthTiles = [size, size + 2];
			}
			using var rng = new RandomNumberGenerator { Seed = seed };
			var room = new Room();
			AssertBool(generator.PopulateRoom(room, rng)).IsTrue();
			foreach (var arrangement in room.GetChildren().OfType<RoomArrangement>())
			{
				room.RemoveChild(arrangement);
				arrangement.Free();
			}
			var initialAisle = room.Map.Width >= room.Map.Height ? Vector2I.Right : Vector2I.Down;
			try
			{
				for (int turn = 0; turn < 4; turn++)
				{
					if (kind == "hall")
					{
						AssertAisleEnds(room, ConnectedArrangementStrategy.Rotate(initialAisle, turn));
						AssertBool(room.Map.Width % 2 == 1 && room.Map.Height % 2 == 1).IsTrue();
						int centerX = room.Bounds.Position.X + (room.Bounds.Size.X - Room.TileSize) / 2;
						int centerZ = room.Bounds.Position.Y + (room.Bounds.Size.Y - Room.TileSize) / 2;
						var columns = room.DecorationGridMap.GetUsedCells().ToHashSet();
						AssertInt(columns.Count).IsGreater(0);
						foreach (var cell in columns)
						{
							AssertBool(columns.Contains(new(2 * centerX - cell.X, cell.Y, cell.Z))).IsTrue();
							AssertBool(columns.Contains(new(cell.X, cell.Y, 2 * centerZ - cell.Z))).IsTrue();
						}
						foreach (var door in room.GetChildren().OfType<DoorwayMarker>())
							AssertBool(door.Position.X == centerX || door.Position.Z == centerZ).IsTrue();
					}
					var region = new RoomRegion(7) { IsProcedural = true, Purpose = room.Purpose };
					for (int x = 0; x < room.Map.Width; x++)
					for (int z = 0; z < room.Map.Height; z++)
						if (room.Map.IsWalkable(x, z))
						{
							region.Tiles.Add(new(x, z));
							if (room.Map.GetConnectorDirections(x, z).Count > 0) region.ConnectorTiles.Add(new(x, z));
						}
					Vector3 Center(Vector2I tile) => (Vector3)room.TileCenterCell(tile.X, tile.Y);
					var obstacles = RoomArrangement.GetGridBounds(room.DecorationGridMap, Transform3D.Identity);
					var walls = RoomArrangement.GetGridBounds(room.WallGridMap, Transform3D.Identity);
					var pool = generator.CreateInteriorPool();
					var plan = pool.PlacementStrategy.Plan(room.Map, [region], pool, seed, 0, Center, obstacles, walls);
					string Signature(IEnumerable<RoomArrangementPlacement> placements) => string.Join("|", placements.Select(p => $"{p.Definition.Id}:{p.Transform}"));
					AssertString(Signature(plan)).IsEqual(Signature(pool.PlacementStrategy.Plan(room.Map, [region], pool, seed, 0, Center, obstacles, walls)));
					var blocking = new List<Aabb>();
					foreach (var placement in plan)
					{
						AssertBool(placement.Occupied.Any(region.ConnectorTiles.Contains)).IsFalse();
						if (kind == "hall") AssertBool(placement.Definition.Placement == ArrangementPlacement.Wall).IsTrue();
						else AssertPerimeter(placement.Occupied, region.Tiles);
						var instance = placement.Definition.Instantiate();
						try { blocking.AddRange(instance.GetBlockingBounds().Select(box => placement.Transform * box)); }
						finally { instance.Free(); }
					}
					if (plan.Count > 0)
						AssertBool(new RoomPassageValidator(room.Map, region.Tiles.ToHashSet(), region.ConnectorTiles,
							Center, obstacles.Concat(walls).ToArray(), pool.PassageRadius).IsConnected(blocking)).IsTrue();
					total += plan.Count;
					room.Rotate(1);
				}
			}
			finally { room.Free(); }
		}
		AssertInt(total).IsGreater(0);
		if (generator is CellarRoomGenerator cellar) AssertBool(cellar.StorageArrangements.All(entry => entry.MaximumPerRoom == 1)).IsTrue();
	}

	[TestCase(5, 5, 0f)]
	[TestCase(5, 7, 0f)]
	[TestCase(5, 5, 1f)]
	[TestCase(5, 7, 1f)]
	public void HallKeepsBothAisleEndsRegardlessOfSeedOrSideOpenings(int width, int length, float sideChance)
	{
		var hall = ResourceLoader.Load<HallRoomGenerator>(Root + "rooms/hall/garrison_hall_generator.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		hall.WidthTiles = width;
		hall.LengthTiles = [length];
		hall.SideOpeningChance = sideChance;
		// Hall entrances belong to its algorithm, independently of generic shell doorway selection.
		hall.ShellBuilder.MinDoorways = hall.ShellBuilder.MaxDoorways = 1;
		hall.ShellBuilder.DoorwayChance = 0;
		for (ulong seed = 1; seed <= 8; seed++)
		{
			using var rng = new RandomNumberGenerator { Seed = seed };
			var room = hall.BuildRoom(rng);
			try
			{
				AssertInt(room.GetChildren().OfType<DoorwayMarker>().Count()).IsEqual(sideChance == 0 ? 2 : 4);
				AssertAisleEnds(room, width == length ? Vector2I.Right : Vector2I.Down);
			}
			finally { room.Free(); }
		}
	}

	[TestCase]
	public void HallRejectsDimensionsThatCannotSupportItsSymmetryBeforeReplacingGeometry()
	{
		var hall = ResourceLoader.Load<HallRoomGenerator>(Root + "rooms/hall/garrison_hall_generator.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		using var rng = new RandomNumberGenerator { Seed = 23 };
		var room = hall.BuildRoom(rng);
		try
		{
			string first = string.Join("|", room.FloorGridMap.GetUsedCells());
			hall.WidthTiles = 4;
			AssertBool(hall.PopulateRoom(room, rng)).IsFalse();
			AssertString(string.Join("|", room.FloorGridMap.GetUsedCells())).IsEqual(first);
		}
		finally { room.Free(); }
	}

	[TestCase]
	public void EditorRerollsValidateBeforeReplacementAndSaveEditableAuthoredRooms()
	{
		var generator = ResourceLoader.Load<CellarRoomGenerator>(Root + "rooms/cellar/garrison_cellar_generator.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		using var rng = new RandomNumberGenerator { Seed = 23 };
		var room = new Room();
		const string path = "user://coded_room_generator_test.tscn";
		try
		{
			AssertBool(generator.PopulateRoom(room, rng)).IsTrue();
			string Signature() => string.Join("|", room.GetChildren().OfType<RoomArrangement>().Select(a => a.Transform));
			string first = Signature();
			AssertBool(first.Length > 0).IsTrue();
			rng.Seed = 23;
			AssertBool(generator.PopulateRoom(room, rng)).IsTrue();
			AssertString(Signature()).IsEqual(first);
			generator.MaximumClusters = 0;
			AssertBool(generator.PopulateRoom(room, rng)).IsFalse();
			AssertString(Signature()).IsEqual(first);
			generator.MaximumClusters = 10;
			AssertBool(generator.SaveRoomScene(path, 23)).IsTrue();
			var saved = ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.IgnoreDeep).Instantiate<Room>();
			AssertBool(saved.SceneFilePath.Length > 0).IsTrue();
			AssertObject(saved.Generator).IsNotNull();
			AssertString(string.Join("|", saved.GetChildren().OfType<RoomArrangement>().Select(a => a.Transform))).IsEqual(first);
			saved.Free();
		}
		finally
		{
			room.Free();
			if (FileAccess.FileExists(path)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
		}
	}

	[TestCase]
	public async Task ProductionFactoryUsesPerRoomPoliciesAndToggleLeavesLayoutUnchanged()
	{
		var factory = GD.Load<MixedRoomFactory>(Root + "factories/garrison_mixed_room_factory.tres");
		var (runner, generator) = await TestFactories.LoadGenerator(new PackedRoomLayout(), factory);
		using (runner)
		{
			generator.MapWidth = generator.MapDepth = 26;
			generator.MaxRooms = 6;
			// Recipe interiors work without the generic dungeon-wide furnishing pool.
			generator.ArrangementPool = null;
			int total = 0;
			foreach (ulong seed in new ulong[] { 2, 23 })
			{
				generator.Seed = seed;
				generator.EnableFurnishings = true;
				generator.GenerateMap(false);
				AssertArray(LayoutConnectivityTest.FindConnectivityFailures(generator)).IsEmpty();
				foreach (var room in generator.NavigationRegion.GetChildren().OfType<Room>().Where(room => room.Generator is HallRoomGenerator))
				{
					// The hall creates its two primary ends before optional side entrances; rotation preserves them.
					var ends = room.GetChildren().OfType<DoorwayMarker>().Take(2).ToArray();
					AssertInt(ends.Length).IsEqual(2);
					AssertBool(ends[0].GetDirectionVectors().Single() == -ends[1].GetDirectionVectors().Single()).IsTrue();
					foreach (var marker in ends)
					{
						var tile = generator.Map.WorldToTile(marker.GlobalPosition);
						var outside = tile + marker.GetDirectionVectors().Single();
						AssertBool(generator.Map.IsDoorway(tile.X, tile.Y)).IsTrue();
						AssertBool(generator.Map.IsCorridor(outside.X, outside.Y)).IsTrue();
					}
				}
				var arrangements = generator.NavigationRegion.GetChildren().OfType<RoomArrangement>().ToArray();
				foreach (var arrangement in arrangements.Where(a => !a.GetMeta("is_room_detail", false).AsBool()))
				{
					var region = generator.Floor.Rooms.Single(r => r.Id == arrangement.GetMeta("room_id").AsInt32());
					AssertObject(region.InteriorPool).IsNotNull();
					var definition = region.InteriorPool.Entries.Single(d => d.Id == arrangement.GetMeta("arrangement_id").AsString());
					if (region.Purpose == RoomPurpose.GreatHall) AssertBool(definition.Placement == ArrangementPlacement.Wall).IsTrue();
					else if (region.FurnishingSlots.Count == 0) AssertPerimeter([arrangement.GetMeta("anchor").AsVector2I()], region.Tiles);
				}
				total += arrangements.Length;
				string signature = RuntimeSignature(generator);
				generator.GenerateMap(false);
				AssertString(RuntimeSignature(generator)).IsEqual(signature);
				string map = string.Join("|", generator.Map.Tiles.Cast<MapTile>());
				generator.EnableFurnishings = false;
				generator.GenerateMap(false);
				AssertInt(generator.ArrangementCount).IsEqual(0);
				AssertString(string.Join("|", generator.Map.Tiles.Cast<MapTile>())).IsEqual(map);
			}
			AssertInt(total).IsGreater(0);
		}
	}

	private static void AssertAisleEnds(Room room, Vector2I direction)
	{
		foreach (var outward in new[] { direction, -direction })
		{
			var center = room.Bounds.Position + (room.Bounds.Size - Vector2I.One * Room.TileSize) / 2;
			var position = new Vector3(center.X + outward.X * (room.Bounds.Size.X - Room.TileSize) / 2, 0,
				center.Y + outward.Y * (room.Bounds.Size.Y - Room.TileSize) / 2);
			var marker = room.GetChildren().OfType<DoorwayMarker>().SingleOrDefault(m => m.Position.IsEqualApprox(position)
				&& m.GetDirectionVectors().Contains(outward));
			AssertObject(marker).OverrideFailureMessage($"Missing aisle end at {position}, facing {outward}.").IsNotNull();
			var tile = room.LocalToTile(position);
			AssertBool(room.Map.IsDoorway(tile.X, tile.Y)).IsTrue();
			AssertBool(room.Map.GetConnectorDirections(tile.X, tile.Y).Contains(outward)).IsTrue();
		}
	}

	private static string RuntimeSignature(MapGenerator generator) => string.Join("|", generator.NavigationRegion.GetChildren()
		.OfType<RoomArrangement>().Select(a => $"{a.GetMeta("arrangement_id")}:{a.Transform}"));

	private static void AssertPerimeter(IEnumerable<Vector2I> occupied, IReadOnlyList<Vector2I> floor)
	{
		int minX = floor.Min(t => t.X), maxX = floor.Max(t => t.X);
		int minZ = floor.Min(t => t.Y), maxZ = floor.Max(t => t.Y);
		AssertBool(occupied.All(t => t.X == minX || t.X == maxX || t.Y == minZ || t.Y == maxZ)).IsTrue();
	}
}