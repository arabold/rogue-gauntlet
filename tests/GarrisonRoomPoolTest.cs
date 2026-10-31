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
		var factory = TestFactories.Load<MixedRoomFactory>(Preset);
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
		var factory = TestFactories.Load<MixedRoomFactory>(Preset, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
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
	public void OrdinaryAndSpecialSlotsAreGeneratedAndResetReproducesTheirGeometry()
	{
		var factory = TestFactories.Load<MixedRoomFactory>(Preset, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		var authored = (DungeonRoomFactory)factory.AuthoredFactory;
		AssertInt(authored.StandardRoomScenePaths.Count).IsEqual(0);
		AssertInt(authored.SpecialRoomScenePaths.Count).IsEqual(0);
		using var rng = new RandomNumberGenerator { Seed = 42 };
		string first = "";
		for (int index = 0; index < 8; index++)
		{
			var room = index % 2 == 0 ? factory.CreateSpecialRoom(rng) : factory.CreateStandardRoom(rng);
			AssertString(room.SceneFilePath).IsEmpty();
			AssertObject(room.Generator).IsNotNull();
			if (index == 0) first = string.Join("|", room.FloorGridMap.GetUsedCells());
			room.Free();
		}
		factory.Reset();
		rng.Seed = 42;
		var reset = factory.CreateSpecialRoom(rng);
		AssertString(string.Join("|", reset.FloorGridMap.GetUsedCells())).IsEqual(first);
		reset.Free();
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
		string failure = "";
		level.GenerationFailed += error => failure = error;
		foreach (ulong runSeed in new ulong[] { 1, 23, 12345 })
		for (uint depth = 1; depth <= 3; depth++)
		{
			level.ConfigureGeneration(new RunState(runSeed), depth, null, null);
			failure = "";
			level.Generate();
			AssertString(failure).OverrideFailureMessage($"seed={runSeed} depth={depth}: {failure}").IsEmpty();
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

	/// <summary>
	/// Every configured library length must survive packing onto gameplay floors. A size the map can never place is
	/// silently rebuilt as another room, which left one fixed library layout on every floor.
	/// </summary>
	[TestCase]
	public async Task EveryLibraryLengthLandsOnGameplayFloors()
	{
		using var runner = TestFactories.LoadScene("res://scenes/levels/level.tscn");
		await runner.SimulateFrames(2);
		var level = (Level)runner.Scene();
		var library = TestFactories.Load<LibraryRoomGenerator>("res://scenes/levels/garrison/rooms/library/garrison_library_generator.tres");
		var placed = new HashSet<int>();
		var lengths = library.StackRows.Select(rows => library.SizeForRows(rows).Y).ToArray();
		for (ulong runSeed = 1; runSeed <= 40 && !placed.SetEquals(lengths); runSeed++)
		{
			level.ConfigureGeneration(new RunState(runSeed), 1, null, null);
			level.Generate();
			placed.UnionWith(level.MapGenerator.NavigationRegion.GetChildren().OfType<Room>()
				.Where(room => room.Generator is LibraryRoomGenerator)
				.Select(room => Mathf.Max(room.Map.Width, room.Map.Height)));
			await runner.SimulateFrames(1);
		}
		AssertArray(placed.Order().ToArray()).IsEqual(lengths.Order().ToArray());
	}

	private static bool IsGarrisonPurpose(RoomPurpose purpose) => purpose is RoomPurpose.Chamber
		or RoomPurpose.GreatHall or RoomPurpose.Colonnade or RoomPurpose.RuinedCollapse;

	private static string Fingerprint(MapGenerator generator) => string.Join("|", generator.Map.Tiles.Cast<MapTile>())
		+ string.Join("|", generator.EnemySpawnPoints.Select(spawn => $"{spawn.PersistentId}:{spawn.Scenes[0].ResourcePath}:{spawn.GlobalTransform}"))
		+ string.Join("|", generator.NavigationRegion.GetChildren().OfType<RoomArrangement>().Select(arrangement =>
			$"{arrangement.GetMeta("arrangement_id")}:{arrangement.Transform}"));
}