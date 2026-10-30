namespace RogueGauntlet.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Authored furniture, real generated floors, isolated randomness and conservative actor-sized connectivity.</summary>
[TestSuite, RequireGodotRuntime]
public class RoomArrangementTest
{
	private static RoomArrangementPool Pool(bool walls = false)
	{
		var authored = TestFactories.Load<RoomArrangementPool>("res://scenes/levels/garrison/arrangements/garrison_arrangements.tres");
		if (walls) return authored;
		var floor = (RoomArrangementPool)authored.Duplicate();
		floor.Entries = authored.Entries.Where(entry => entry.Placement == ArrangementPlacement.Floor).ToArray();
		floor.MaximumWallPerRoom = 0;
		return floor;
	}
	private static Vector3 Center(Vector2I tile) => new(tile.X * Room.TileSize, 0, tile.Y * Room.TileSize);

	private static (MapData Map, RoomRegion Region) Rectangle()
	{
		var map = new MapData(9, 9);
		map.ResetToBorderedEmpty();
		var region = new RoomRegion(0) { IsProcedural = true };
		for (int x = 1; x <= 7; x++)
			for (int z = 1; z <= 7; z++) { map.SetTile(x, z, MapTile.Room); region.Tiles.Add(new(x, z)); }
		map.SetConnector(4, 1, [Vector2I.Up], true);
		map.SetTile(4, 0, MapTile.Corridor);
		region.ConnectorTiles.Add(new(4, 1));
		return (map, region);
	}

	private static string Signature(IEnumerable<RoomArrangementPlacement> placements) => string.Join(";",
		placements.Select(p => $"{p.RoomId}:{p.Definition.Id}:{p.Anchor}:{p.QuarterTurns}"));
	private static string MapSignature(MapData map) => string.Join(",", map.Tiles.Cast<MapTile>().Select(tile => (int)tile));

	[TestCase]
	public void AuthoredDefinitionsValidateAndFactoryInstancesAreIndependent()
	{
		var pool = Pool();
		AssertArray(pool.Validate()).IsEmpty();
		foreach (var entry in pool.Entries)
		{
			using var first = entry.Instantiate();
			using var second = entry.Instantiate();
			first.Content.Position += Vector3.Right;
			AssertBool(first.Content.Position == second.Content.Position).IsFalse();
			AssertBool(second.GetContentBounds().Count > 0).IsTrue();
		}
	}

	[TestCase]
	public void NewlyAuthoredArrangementsValidateCleanly()
	{
		string[] paths = [
			"res://scenes/levels/arrangements/furniture/table_banquet.tres",
			"res://scenes/levels/arrangements/wall/wall_sword_shield.tres",
			"res://scenes/levels/arrangements/storage/crate_cache.tres",
			"res://scenes/levels/arrangements/debris/rubble_mound.tres"
		];
		foreach (var path in paths)
		{
			var definition = TestFactories.Load<RoomArrangementDefinition>(path);
			AssertObject(definition).IsNotNull();
			var errors = definition.Validate();
			AssertArray(errors).OverrideFailureMessage(path + ": " + string.Join("; ", errors)).IsEmpty();
			using var instance = definition.Instantiate();
			AssertObject(instance.Content).IsNotNull();
			AssertBool(instance.GetContentBounds().Count > 0).IsTrue();
		}
		var wallDef = TestFactories.Load<RoomArrangementDefinition>("res://scenes/levels/arrangements/wall/wall_sword_shield.tres");
		using var wallInst = wallDef.Instantiate();
		for (int turn = 0; turn < 4; turn++)
		{
			var basis = new Basis(Vector3.Up, turn * Mathf.Pi / 2);
			var outward = ConnectedArrangementStrategy.Rotate(Vector2I.Up, turn);
			var wall = new Transform3D(basis, Vector3.Zero) * new Aabb(-2, 0, -2.5f, 4, 4, 1);
			AssertBool(WallArrangementMount.TryPlace(wallInst.PlacementBounds, basis, Vector3.Zero, outward,
				wallDef.MountHeight, [wall], out var transform, wallDef.WallInset)).IsTrue();
			foreach (var local in wallInst.GetContentBounds())
			{
				var box = transform * local;
				float back = outward.X != 0 ? (outward.X > 0 ? box.End.X : -box.Position.X)
					: (outward.Y > 0 ? box.End.Z : -box.Position.Z);
				float gap = 1.67f - back;
				AssertBool(gap >= 0 && gap < .015f).IsTrue();
			}
			AssertBool(WallArrangementMount.OverlapsWall(transform * wallInst.PlacementBounds, wall, outward, wallDef.WallInset)).IsFalse();
		}
	}

	[TestCase]
	public void SeedSweepPreservesAllFreeSpaceAndSupportsWeightedInteriorPlacement()
	{
		var pool = Pool();
		var (map, room) = Rectangle();
		string baseline = MapSignature(map);
		var rotations = new HashSet<int>();
		bool foundInterior = false;
		for (ulong seed = 1; seed <= 20; seed++)
		{
			var plan = pool.PlacementStrategy.Plan(map, [room], pool, seed, 0, Center, [], []);
			AssertInt(plan.Count).IsEqual(2);
			AssertString(Signature(pool.PlacementStrategy.Plan(map, [room], pool, seed, 0, Center, [], []))).IsEqual(Signature(plan));
			var blocked = new HashSet<Vector2I>();
			var geometry = new List<Aabb>();
			foreach (var placement in plan)
			{
				rotations.Add(placement.QuarterTurns);
				AssertBool(placement.Occupied.Any(blocked.Contains)).IsFalse();
				AssertBool(placement.Occupied.Any(room.ConnectorTiles.Contains)).IsFalse();
				blocked.UnionWith(placement.Occupied);
				using var instance = placement.Definition.Instantiate();
				var transform = new Transform3D(new Basis(Vector3.Up, placement.QuarterTurns * Mathf.Pi / 2), Center(placement.Anchor));
				geometry.AddRange(instance.GetBlockingBounds().Select(box => transform * box));
				if (!placement.Definition.RequiresWall && placement.Occupied.All(tile =>
					!ConnectedArrangementStrategy.IsClosedEdge(map, tile, ConnectedArrangementStrategy.Rotate(Vector2I.Up, placement.QuarterTurns)))) foundInterior = true;
			}
			AssertBool(new RoomPassageValidator(map, room.Tiles.ToHashSet(), room.ConnectorTiles, Center, [], pool.PassageRadius).IsConnected(geometry)).IsTrue();
			AssertInt(blocked.Count).IsLessEqual((int)(room.Tiles.Count * pool.MaximumOccupiedFraction));
			AssertInt(plan.Select(p => p.Definition.Id).Distinct().Count()).IsEqual(plan.Count);
			AssertString(MapSignature(map)).IsEqual(baseline);
		}
		AssertBool(foundInterior).IsTrue();
		AssertInt(rotations.Count).IsEqual(4);
	}

	[TestCase]
	public void PocketIsRejectedEvenWhenBothDoorsRemainConnected()
	{
		var (map, room) = Rectangle();
		// Two bottom openings stay connected, while a barrier isolates the entire top strip.
		var doors = new Vector2I[] { new(2, 7), new(6, 7) };
		var passage = new RoomPassageValidator(map, room.Tiles.ToHashSet(), doors, Center, [], .85f);
		AssertBool(passage.IsConnected([])).IsTrue();
		AssertBool(passage.IsConnected([new Aabb(new Vector3(2, 0, 8), new Vector3(28, 3, 1))])).IsFalse();
	}

	[TestCase]
	public void PassageWidthAccountsForHeroRadiusAndExistingColumns()
	{
		var (map, room) = Rectangle();
		var left = new Aabb(new Vector3(2, 0, 12), new Vector3(13, 3, 1));
		// A 1.5-unit gap cannot pass the 1.7-unit safety diameter; 2.0 units can.
		var narrow = new Aabb(new Vector3(16.5f, 0, 12), new Vector3(13.5f, 3, 1));
		var wide = new Aabb(new Vector3(17, 0, 12), new Vector3(13, 3, 1));
		var passage = new RoomPassageValidator(map, room.Tiles.ToHashSet(), room.ConnectorTiles, Center, [left], .85f);
		AssertBool(passage.IsConnected([narrow])).IsFalse();
		AssertBool(passage.IsConnected([wide])).IsTrue();
		// Obstructing an opening is forbidden even if the rest of the floor remains connected.
		AssertBool(passage.IsConnected([new Aabb(Center(new(4, 1)) - Vector3.One, Vector3.One * 2)])).IsFalse();
	}

	[TestCase]
	public void UnsupportedFloorAndInternalWallsParticipateInConnectivity()
	{
		var (map, room) = Rectangle();
		var floor = room.Tiles.Where(tile => tile.Y != 4 || tile.X == 4).ToHashSet();
		var passage = new RoomPassageValidator(map, floor, room.ConnectorTiles, Center, [], .85f);
		AssertBool(passage.IsConnected([])).IsTrue();
		AssertBool(passage.IsConnected([new Aabb(new Vector3(14, 0, 14), new Vector3(4, 3, 4))])).IsFalse();
	}

	[TestCase]
	public void CrowdedOrAuthoredRoomsOmitOptionalFurnitureWithoutMutatingResources()
	{
		var pool = Pool();
		var (map, room) = Rectangle();
		AssertArray(pool.PlacementStrategy.Plan(map, [room], pool, 1, 0, Center,
			[new Aabb(Vector3.Zero, new Vector3(40, 5, 40))], [])).IsEmpty();
		room.IsProcedural = false;
		AssertArray(pool.PlacementStrategy.Plan(map, [room], pool, 1, 0, Center, [], [])).IsEmpty();
		AssertInt(pool.Entries[0].MaximumPerRoom).IsEqual(1);
	}

	[TestCase]
	public void GeometryEditsOutsideTheDeclaredFootprintAreRejected()
	{
		using var definition = (RoomArrangementDefinition)Pool().Entries[0].Duplicate();
		using var instance = definition.Instantiate();
		instance.Content.Position += Vector3.Right * 20;
		using var packed = new PackedScene();
		AssertObject(packed.Pack(instance)).IsEqual(Error.Ok);
		definition.Scene = packed;
		AssertBool(definition.Validate().Any(error => error.Contains("exceeds PlacementBounds"))).IsTrue();
	}

	[TestCase]
	public void UnaccountedSceneGeometryAndMissingBindingsAreRejected()
	{
		using var definition = (RoomArrangementDefinition)Pool().Entries[1].Duplicate();
		using var instance = definition.Instantiate();
		var extra = new MeshInstance3D { Mesh = new BoxMesh(), Position = Vector3.Right * 20 };
		instance.AddChild(extra);
		extra.Owner = instance;
		using var packed = new PackedScene();
		AssertObject(packed.Pack(instance)).IsEqual(Error.Ok);
		definition.Scene = packed;
		AssertBool(definition.Validate().Any(error => error.Contains("Content hierarchy"))).IsTrue();
		instance.Content = null;
		using var unbound = new PackedScene();
		AssertObject(unbound.Pack(instance)).IsEqual(Error.Ok);
		definition.Scene = unbound;
		AssertBool(definition.Validate().Any(error => error.Contains("explicitly assigned"))).IsTrue();
	}

	[TestCase]
	public void ManualSceneEditsSurvivePackingSavingAndReopening()
	{
		const string path = "user://arrangement_scene_test.tscn";
		using var instance = Pool().Entries[1].Instantiate();
		instance.Content.Position += Vector3.Right * .1f;
		using var packed = new PackedScene();
		try
		{
			AssertObject(packed.Pack(instance)).IsEqual(Error.Ok);
			AssertObject(ResourceSaver.Save(packed, path)).IsEqual(Error.Ok);
			using var restored = TestFactories.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<RoomArrangement>();
			AssertBool(restored.Content != null).IsTrue();
			AssertBool(restored.Content.Position.IsEqualApprox(instance.Content.Position)).IsTrue();
		}
		finally { DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path)); }
	}

	[TestCase]
	public async Task ProductionPassRegeneratesWithoutDuplicatesAndDoesNotChangeLayoutWhenToggled()
	{
		var (runner, generator) = await TestFactories.LoadGenerator(new PackedRoomLayout(), TestFactories.AllProcedural());
		using (runner)
		{
			generator.MapWidth = generator.MapDepth = 26;
			generator.MaxRooms = 6;
			generator.Seed = 23;
			generator.GenerateMap(false);
			string baseline = MapSignature(generator.Map);
			generator.ArrangementPool = Pool();
			generator.Position = new Vector3(10, 2, -5);
			generator.Rotation = new Vector3(0, .3f, 0);
			generator.GenerateMap(false);
			string signature = RuntimeSignature(generator);
			AssertInt(generator.ArrangementCount).IsGreater(0);
			AssertString(MapSignature(generator.Map)).IsEqual(baseline);
			foreach (var arrangement in generator.NavigationRegion.GetChildren().OfType<RoomArrangement>())
			{
				var anchor = arrangement.GetMeta("anchor").AsVector2I();
				AssertBool(arrangement.GlobalPosition.IsEqualApprox(generator.ToGlobal(new Vector3((anchor.X - generator.Map.Width / 2) * Room.TileSize, 0, (anchor.Y - generator.Map.Height / 2) * Room.TileSize)))).IsTrue();
			}
			generator.GenerateMap(false);
			AssertString(RuntimeSignature(generator)).IsEqual(signature);
			AssertInt(generator.NavigationRegion.GetChildren().OfType<RoomArrangement>().Count()).IsEqual(generator.ArrangementCount);
			generator.ArrangementPool = null;
			generator.GenerateMap(false);
			AssertInt(generator.ArrangementCount).IsEqual(0);
			AssertInt(generator.NavigationRegion.GetChildren().OfType<RoomArrangement>().Count()).IsEqual(0);
			AssertString(MapSignature(generator.Map)).IsEqual(baseline);
		}
	}

	[TestCase]
	public void StandaloneEditorBuilderRerollsAndSavesEditableArrangementsDeterministically()
	{
		using var builder = (ProceduralRoomBuilder)TestFactories.Load<ProceduralRoomBuilder>("res://scenes/levels/dungeon/dungeon_procedural_room_builder.tres").Duplicate();
		builder.ArrangementPool = Pool();
		builder.MinTiles = builder.MaxTiles = 7;
		builder.ShapeArchetype = RoomShapeArchetype.Rectangle;
		builder.Purpose = RoomPurpose.Chamber;
		builder.GenerateWalls = true;
		builder.ColumnChance = 0;
		using var rng = new RandomNumberGenerator { Seed = 2 };
		using var room = builder.BuildRoom(rng);
		AssertObject(room).IsNotNull();
		string signature = StandaloneSignature(room);
		AssertInt(room.GetChildren().OfType<RoomArrangement>().Count()).IsEqual(2);
		rng.Seed = 2;
		AssertBool(builder.PopulateRoom(room, rng)).IsTrue();
		AssertString(StandaloneSignature(room)).IsEqual(signature);
		AssertInt(room.GetChildren().OfType<RoomArrangement>().Count()).IsEqual(2);
		const string path = "user://arrangement_room_test.tscn";
		try
		{
			AssertBool(builder.SaveRoomScene(path, 2)).IsTrue();
			using var restored = TestFactories.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<Room>();
			AssertString(StandaloneSignature(restored)).IsEqual(signature);
			AssertObject(restored.Purpose).IsEqual(RoomPurpose.Chamber);
			foreach (var arrangement in restored.GetChildren().OfType<RoomArrangement>())
			{
				AssertBool(arrangement.Owner == restored).IsTrue();
				AssertBool(arrangement.Content != null).IsTrue();
			}
		}
		finally { DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path)); }
		builder.ArrangementPool = null;
		rng.Seed = 2;
		AssertBool(builder.PopulateRoom(room, rng)).IsTrue();
		AssertInt(room.GetChildren().OfType<RoomArrangement>().Count()).IsEqual(0);
	}

	[TestCase]
	public void MultirowFootprintsAreAllowedAndImpossiblePositionWeightsAreRejected()
	{
		using var definition = (RoomArrangementDefinition)Pool().Entries[2].Duplicate();
		definition.OccupiedTiles = [new(0, 0), new(1, 0), new(0, 1), new(1, 1)];
		AssertArray(definition.Validate()).IsEmpty();
		definition.WallPositionWeight = definition.InteriorPositionWeight = 0;
		AssertBool(definition.Validate().Length > 0).IsTrue();
	}

	[TestCase]
	public async Task GeneratedChestsPreserveAllFreeSpaceInFurnishedRooms()
	{
		var (runner, generator) = await TestFactories.LoadGenerator(new PackedRoomLayout(), TestFactories.AllProcedural());
		using (runner)
		{
			generator.MapWidth = generator.MapDepth = 26;
			generator.MaxRooms = 6;
			generator.ArrangementPool = Pool();
			TestFactories.WireLootScenes(generator);
			generator.MinChests = generator.MaxChests = 3;
			int chests = 0, furnishedRooms = 0;
			foreach (ulong seed in new ulong[] { 1, 2, 3 })
			{
				generator.Seed = seed;
				generator.GenerateMap(true);
				chests += generator.NavigationRegion.GetChildren().OfType<Chest>().Count();
				var roomIds = generator.NavigationRegion.GetChildren().OfType<RoomArrangement>()
					.Select(node => node.GetMeta("room_id").AsInt32()).Distinct();
				var obstacles = RoomArrangement.GetGridBounds(generator.DecorationGridMap, generator.DecorationGridMap.Transform)
					.Concat(RoomArrangement.GetGridBounds(generator.WallGridMap, generator.WallGridMap.Transform))
					.Concat(RoomArrangement.GetStaticPropBounds(generator.NavigationRegion, Transform3D.Identity)).ToArray();
				foreach (int id in roomIds)
				{
					furnishedRooms++;
					var region = generator.Floor.Rooms.Single(room => room.Id == id);
					foreach (var arrangement in generator.NavigationRegion.GetChildren().OfType<RoomArrangement>()
						.Where(node => node.GetMeta("room_id").AsInt32() == id))
						AssertBool(Pool().Entries.Single(entry => entry.Id == arrangement.GetMeta("arrangement_id").AsString())
							.AllowsPurpose(region.Purpose)).IsTrue();
					var floor = region.Tiles.Where(tile => generator.Map.IsWalkable(tile.X, tile.Y)).ToHashSet();
					var doors = region.ConnectorTiles.Where(tile => generator.Map.GetConnectorDirections(tile.X, tile.Y)
						.Any(direction => { var next = tile + direction; return generator.Map.IsWithinBounds(next.X, next.Y) && generator.Map.IsCorridor(next.X, next.Y); }));
					Vector3 TileCenter(Vector2I tile) => new((tile.X - generator.Map.Width / 2) * Room.TileSize, 0,
						(tile.Y - generator.Map.Height / 2) * Room.TileSize);
					AssertBool(new RoomPassageValidator(generator.Map, floor, doors, TileCenter, obstacles,
						generator.ArrangementPool.PassageRadius).IsConnected([]))
						.OverrideFailureMessage($"seed={seed}, room={id}, floor={floor.Count}, obstacles={obstacles.Length}, openings={string.Join(';', doors)}, tiles={string.Join(';', floor)}")
						.IsTrue();
				}
			}
			AssertInt(furnishedRooms).IsGreater(0);
			AssertInt(chests).IsGreater(0);
		}
	}

	[TestCase(RoomPurpose.SewerCistern)]
	[TestCase(RoomPurpose.SewerAqueduct)]
	public void SewerPurposesExcludeTablesButRetainEligibleFurniture(RoomPurpose purpose)
	{
		var (map, room) = Rectangle();
		room.Purpose = purpose;
		var pool = Pool();
		var plan = pool.PlacementStrategy.Plan(map, [room], pool, 12, 0, Center, [], []);
		AssertInt(plan.Count).IsEqual(1);
		AssertString(plan[0].Definition.Id).IsEqual("garrison.barrel_edge");
		AssertBool(pool.Entries.Single(entry => entry.Id == "garrison.table_chairs").AllowsPurpose(RoomPurpose.Chamber)).IsTrue();
		AssertBool(pool.Entries[0].AllowsPurpose(RoomPurpose.Random)).IsFalse();
	}

	[TestCase]
	public void StandaloneBuilderResolvesAndRetainsSewerPurposeForFurnishing()
	{
		using var builder = (ProceduralRoomBuilder)TestFactories.Load<ProceduralRoomBuilder>("res://scenes/levels/dungeon/dungeon_procedural_room_builder.tres").Duplicate();
		builder.ArrangementPool = Pool();
		builder.Purpose = RoomPurpose.SewerCistern;
		builder.ShapeArchetype = RoomShapeArchetype.Rectangle;
		builder.MinTiles = builder.MaxTiles = 7;
		builder.ColumnChance = 0;
		using var rng = new RandomNumberGenerator { Seed = 2 };
		using var room = builder.BuildRoom(rng);
		AssertObject(room.Purpose).IsEqual(RoomPurpose.SewerCistern);
		AssertBool(room.GetChildren().OfType<RoomArrangement>().All(node => node.GetMeta("arrangement_id").AsString() == "garrison.barrel_edge")).IsTrue();
	}

	private static Aabb[] RoomWalls() =>
	[
		new(new Vector3(2, 0, 1.5f), new Vector3(12, 4, 1.5f)),
		new(new Vector3(18, 0, 1.5f), new Vector3(12, 4, 1.5f)),
		new(new Vector3(2, 0, 29), new Vector3(28, 4, 1.5f)),
		new(new Vector3(1.5f, 0, 2), new Vector3(1.5f, 4, 28)),
		new(new Vector3(29, 0, 2), new Vector3(1.5f, 4, 28))
	];

	[TestCase]
	public void FullSceneTorchPreservesLightAndParticlesWithoutBlockingFloor()
	{
		var pool = Pool(true);
		AssertArray(pool.Validate()).IsEmpty();
		using var torch = pool.Entries.Single(entry => entry.Id == "garrison.wall_torch").Instantiate();
		AssertBool(torch.Content != null).IsTrue();
		AssertInt(torch.FindChildren("*", "OmniLight3D", true, false).Count).IsGreater(0);
		var particles = torch.FindChildren("*", "GPUParticles3D", true, false).OfType<GpuParticles3D>().Single();
		particles.VisibilityAabb = new Aabb(Vector3.One * -100, Vector3.One * 200);
		AssertArray(torch.GetBlockingBounds()).IsEmpty();
		AssertBool(torch.GetLocalBounds()[0].Size.Length() < 2).IsTrue();
		AssertBool(torch.Content.IsInGroup("prop")).IsFalse();
	}

	[TestCase]
	public void WallMountsUseRealSpansRotateAndLeaveFloorChoicesAndReservationsAlone()
	{
		var pool = Pool(true);
		var (map, room) = Rectangle();
		var walls = RoomWalls();
		var turns = new HashSet<int>();
		for (ulong seed = 1; seed <= 12; seed++)
		{
			var plan = pool.PlacementStrategy.Plan(map, [room], pool, seed, 0, Center, [], walls);
			var floor = plan.Where(p => p.Definition.Placement == ArrangementPlacement.Floor).ToArray();
			AssertString(Signature(floor)).IsEqual(Signature(Pool().PlacementStrategy.Plan(map, [room], Pool(), seed, 0, Center, [], walls)));
			var mounts = plan.Where(p => p.Definition.Placement == ArrangementPlacement.Wall).ToArray();
			AssertInt(mounts.Length).IsEqual(2);
			foreach (var mount in mounts)
			{
				turns.Add(mount.QuarterTurns);
				AssertArray(mount.Occupied).IsEmpty();
				AssertBool(room.ConnectorTiles.Contains(mount.Anchor)).IsFalse();
				using var instance = mount.Definition.Instantiate();
				var box = mount.Transform * instance.PlacementBounds;
				AssertBool(walls.Any(wall => WallArrangementMount.OverlapsWall(box, wall, ConnectedArrangementStrategy.Rotate(Vector2I.Up, mount.QuarterTurns), mount.Definition.WallInset))).IsFalse();
				AssertBool(Mathf.IsEqualApprox(mount.Transform.Origin.Y, mount.Definition.MountHeight)).IsTrue();
			}
		}
		AssertInt(turns.Count).IsEqual(4);
		AssertArray(pool.PlacementStrategy.Plan(map, [room], pool, 1, 0, Center, [], [])
			.Where(p => p.Definition.Placement == ArrangementPlacement.Wall).ToArray()).IsEmpty();
	}

	[TestCase]
	public void BannerAndTorchSitAgainstRecessedWallFaceInEveryOrientation()
	{
		foreach (var definition in Pool(true).Entries.Where(entry => entry.Placement == ArrangementPlacement.Wall))
		{
			using var instance = definition.Instantiate();
			for (int turn = 0; turn < 4; turn++)
			{
				var basis = new Basis(Vector3.Up, turn * Mathf.Pi / 2);
				var outward = ConnectedArrangementStrategy.Rotate(Vector2I.Up, turn);
				var wall = new Transform3D(basis, Vector3.Zero) * new Aabb(-2, 0, -2.5f, 4, 4, 1);
				AssertBool(WallArrangementMount.TryPlace(instance.PlacementBounds, basis, Vector3.Zero, outward,
					definition.MountHeight, [wall], out var transform, definition.WallInset)).IsTrue();
				// The kit's broad envelope includes trim at 0.5; the main wall face is at 0.33.
				// Check actual visible geometry, so excess envelope padding cannot make it float again.
				foreach (var local in instance.GetContentBounds())
				{
					var box = transform * local;
					float back = outward.X != 0 ? (outward.X > 0 ? box.End.X : -box.Position.X)
						: (outward.Y > 0 ? box.End.Z : -box.Position.Z);
					float gap = 1.67f - back;
					AssertBool(gap >= 0 && gap < .015f).IsTrue();
				}
				AssertBool(WallArrangementMount.OverlapsWall(transform * instance.PlacementBounds, wall, outward, definition.WallInset)).IsFalse();
				// An adjoining wall without a full backing span must still reject overlap.
				var side = new Aabb((transform * instance.PlacementBounds).GetCenter() - Vector3.One * .1f, Vector3.One * .2f);
				AssertBool(WallArrangementMount.OverlapsWall(transform * instance.PlacementBounds, side, outward, definition.WallInset)).IsTrue();
			}
		}
	}

	[TestCase]
	public void SolidSceneCollidersAffectPassageButEffectsAndAreasDoNot()
	{
		using var arrangement = new RoomArrangement { PlacementBounds = new Aabb(Vector3.One * -1, Vector3.One * 2) };
		arrangement.Content = new Node3D();
		arrangement.AddChild(arrangement.Content);
		var body = new StaticBody3D { CollisionLayer = 16 };
		arrangement.Content.AddChild(body);
		var solid = new CollisionShape3D { Shape = new BoxShape3D() };
		body.AddChild(solid);
		var area = new Area3D();
		arrangement.Content.AddChild(area);
		area.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 100 } });
		arrangement.Content.AddChild(new OmniLight3D { OmniRange = 100 });
		arrangement.Content.AddChild(new GpuParticles3D { VisibilityAabb = new Aabb(Vector3.One * -100, Vector3.One * 200) });
		AssertInt(arrangement.GetBlockingBounds().Count).IsEqual(1);
		AssertInt(arrangement.GetContentBounds().Count).IsEqual(1);
		solid.Disabled = true;
		AssertArray(arrangement.GetBlockingBounds()).IsEmpty();
	}

	[TestCase]
	public void DecoratedStandaloneRoomSavesAndReopensFullSceneMounts()
	{
		using var builder = (ProceduralRoomBuilder)TestFactories.Load<ProceduralRoomBuilder>("res://scenes/levels/dungeon/dungeon_procedural_room_builder.tres").Duplicate();
		builder.ArrangementPool = Pool(true);
		builder.Purpose = RoomPurpose.Chamber;
		builder.ShapeArchetype = RoomShapeArchetype.Rectangle;
		builder.MinTiles = builder.MaxTiles = 7;
		builder.ColumnChance = 0;
		builder.GenerateWalls = true;
		const string path = "user://full_arrangement_room_test.tscn";
		try
		{
			AssertBool(builder.SaveRoomScene(path, 2)).IsTrue();
			using var restored = TestFactories.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<Room>();
			var entries = restored.GetChildren().OfType<RoomArrangement>().ToArray();
			AssertInt(entries.Length).IsEqual(4);
			var torch = entries.Single(entry => entry.GetMeta("arrangement_id").AsString() == "garrison.wall_torch");
			AssertBool(torch.Content != null && torch.Owner == restored).IsTrue();
			AssertInt(torch.FindChildren("*", "GPUParticles3D", true, false).Count).IsEqual(1);
			AssertInt(torch.FindChildren("*", "OmniLight3D", true, false).Count).IsEqual(1);
			AssertBool(torch.Position.Y > 0).IsTrue();
		}
		finally { DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path)); }
	}

	[TestCase]
	public void MovingBodiesAndOutOfEnvelopeChildrenFailBeforePlacement()
	{
		using var definition = (RoomArrangementDefinition)Pool().Entries[0].Duplicate();
		using var instance = definition.Instantiate();
		var body = new RigidBody3D();
		instance.Content.AddChild(body);
		ProceduralRoomBuilder.SetOwnersForPacking(instance, instance);
		using var packed = new PackedScene();
		AssertObject(packed.Pack(instance)).IsEqual(Error.Ok);
		definition.Scene = packed;
		AssertBool(definition.Validate().Any(error => error.Contains("moving physics bodies"))).IsTrue();
		instance.Content.RemoveChild(body);
		body.Free();
		var mesh = new MeshInstance3D { Mesh = new BoxMesh(), Position = Vector3.Right * 30 };
		instance.Content.AddChild(mesh);
		ProceduralRoomBuilder.SetOwnersForPacking(instance, instance);
		using var outside = new PackedScene();
		AssertObject(outside.Pack(instance)).IsEqual(Error.Ok);
		definition.Scene = outside;
		AssertBool(definition.Validate().Any(error => error.Contains("exceeds PlacementBounds"))).IsTrue();
	}

	private static string StandaloneSignature(Room room) => string.Join(";", room.GetChildren().OfType<RoomArrangement>()
		.Select(node => $"{node.GetMeta("arrangement_id")}:{node.Transform}"));

	private static string RuntimeSignature(MapGenerator generator) => string.Join(";", generator.NavigationRegion.GetChildren()
		.OfType<RoomArrangement>().Select(node => $"{node.GetMeta("arrangement_id")}:{node.GetMeta("anchor")}:{node.GetMeta("quarter_turns")}"));
}