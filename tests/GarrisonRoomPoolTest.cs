namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Curated architecture, optional landmark exhaustion and the actual three-floor generation path.</summary>
[TestSuite, RequireGodotRuntime]
public class GarrisonRoomPoolTest
{
	private const string Preset = "res://scenes/levels/garrison/factories/garrison_mixed_room_factory.tres";

	[TestCase]
	public void RandomPurposesRespectPresetAndReproduceRoomGeometry()
	{
		var factory = GD.Load<MixedRoomFactory>(Preset);
		var seen = new HashSet<RoomPurpose>();
		for (ulong seed = 1; seed <= 40; seed++)
		{
			using var a = new RandomNumberGenerator { Seed = seed };
			using var b = new RandomNumberGenerator { Seed = seed };
			var first = factory.CreateStandardRoom(a);
			var second = factory.CreateStandardRoom(b);
			AssertObject(first).IsNotNull();
			AssertBool(IsGarrisonPurpose(first.Purpose)).IsTrue();
			AssertBool(first.Purpose == second.Purpose).IsTrue();
			AssertString(string.Join("|", first.FloorGridMap.GetUsedCells().Select(cell =>
				$"{cell}:{first.FloorGridMap.GetCellItem(cell)}:{first.FloorGridMap.GetCellItemOrientation(cell)}")))
				.IsEqual(string.Join("|", second.FloorGridMap.GetUsedCells().Select(cell =>
					$"{cell}:{second.FloorGridMap.GetCellItem(cell)}:{second.FloorGridMap.GetCellItemOrientation(cell)}")));
			seen.Add(first.Purpose);
			first.Free();
			second.Free();
		}
		AssertInt(seen.Count).IsEqual(2);
	}

	[TestCase(0)]
	[TestCase(128)]
	[TestCase(-1)]
	public void InvalidRandomSelectionDoesNotEraseRoomAndFixedPurposeOverridesIt(int mask)
	{
		var factory = ResourceLoader.Load<MixedRoomFactory>(Preset, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		var builder = factory.RoomBuilder;
		using var rng = new RandomNumberGenerator { Seed = 42 };
		var room = builder.BuildRoom(rng);
		var cells = room.FloorGridMap.GetUsedCells();
		builder.RandomPurposeMask = mask;
		AssertBool(builder.PopulateRoom(room, rng)).IsFalse();
		AssertString(string.Join("|", room.FloorGridMap.GetUsedCells())).IsEqual(string.Join("|", cells));
		builder.Purpose = RoomPurpose.SewerCistern;
		AssertBool(builder.PopulateRoom(room, rng)).IsTrue();
		AssertBool(room.Purpose == RoomPurpose.SewerCistern).IsTrue();
		room.Free();
	}

	[TestCase]
	public void ExhaustedLandmarksBecomeProceduralSlotsAndResetPerFloor()
	{
		var factory = ResourceLoader.Load<MixedRoomFactory>(Preset, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		using var rng = new RandomNumberGenerator { Seed = 42 };
		var first = factory.CreateSpecialRoom(rng);
		var second = factory.CreateSpecialRoom(rng);
		AssertBool(first.SceneFilePath != second.SceneFilePath).IsTrue();
		AssertBool(first.SceneFilePath.EndsWith("barracks.tscn") || second.SceneFilePath.EndsWith("barracks.tscn")).IsTrue();
		var filler = factory.CreateSpecialRoom(rng);
		AssertString(filler.SceneFilePath).IsEmpty();
		AssertBool(IsGarrisonPurpose(filler.Purpose)).IsTrue();
		factory.Reset();
		rng.Seed = 42;
		var reset = factory.CreateSpecialRoom(rng);
		AssertString(reset.SceneFilePath).IsEqual(first.SceneFilePath);
		foreach (var room in new[] { first, second, filler, reset }) room.Free();
	}

	[TestCase]
	public async Task ThreeDepthsKeepConnectedGeometryEncountersAndReproducibleFurnishings()
	{
		using var runner = TestFactories.LoadScene("res://scenes/levels/level.tscn");
		await runner.SimulateFrames(2);
		var level = (Level)runner.Scene();
		var generator = level.MapGenerator;
		AssertString(generator.RoomFactory.ResourcePath).IsEqual(Preset);
		var encounters = (RealmEncounterFactory)generator.MobFactory;
		foreach (ulong runSeed in new ulong[] { 1, 23, 12345 })
		for (uint depth = 1; depth <= 3; depth++)
		{
			level.ConfigureGeneration(new RunState(runSeed), depth, null, null);
			level.Generate();
			AssertArray(LayoutConnectivityTest.FindConnectivityFailures(generator)).IsEmpty();
			AssertBool(generator.Floor.Rooms.Where(room => room.IsProcedural).All(room => IsGarrisonPurpose(room.Purpose))).IsTrue();
			AssertInt(generator.Floor.Rooms.Count(room => room.IsProcedural)).IsGreater(0);
			var ordinary = generator.EnemySpawnPoints.Where(spawn => encounters.Entries.Any(entry => entry.ScenePath == spawn.Scenes[0].ResourcePath)).ToArray();
			int spent = ordinary.Sum(spawn => encounters.Entries.Single(entry => entry.ScenePath == spawn.Scenes[0].ResourcePath).Threat);
			using var encounterRng = GenerationRandom.Create(generator.Seed, GenerationRandom.Monsters);
			AssertInt(generator.EnemySpawnPoints.Count).IsEqual(encounters.CreateEncounter(depth, encounterRng).Count);
			AssertBool(spent > 0 && spent <= encounters.FloorBudgets[depth - 1]).IsTrue();
			AssertInt(generator.EnemySpawnPoints.Count(spawn => spawn.Scenes[0] == encounters.NestScene)).IsEqual(depth == 2 ? 2 : 0);
			AssertBool(level.CanDescend).IsEqual(depth != 3);
			if (depth == 3) AssertInt(generator.EnemySpawnPoints.Count(spawn => spawn.PersistentId == level.Encounters.GatekeeperId)).IsEqual(1);
			GD.Print($"GARRISON seed={runSeed} depth={depth} rooms={generator.Floor.Rooms.Count} generated={generator.Floor.Rooms.Count(room => room.IsProcedural)} enemies={ordinary.Length} threat={spent} arrangements={generator.ArrangementCount}");
			string first = Fingerprint(generator);
			level.Generate();
			AssertString(Fingerprint(generator)).IsEqual(first);
			await runner.SimulateFrames(1);
		}
	}

	private static bool IsGarrisonPurpose(RoomPurpose purpose) => purpose is RoomPurpose.Chamber
		or RoomPurpose.GreatHall or RoomPurpose.Colonnade or RoomPurpose.RuinedCollapse;

	private static string Fingerprint(MapGenerator generator) => string.Join("|", generator.Map.Tiles.Cast<MapTile>())
		+ string.Join("|", generator.EnemySpawnPoints.Select(spawn => $"{spawn.PersistentId}:{spawn.Scenes[0].ResourcePath}:{spawn.GlobalTransform}"))
		+ string.Join("|", generator.NavigationRegion.GetChildren().OfType<RoomArrangement>().Select(arrangement =>
			$"{arrangement.GetMeta("arrangement_id")}:{arrangement.Transform}"));
}