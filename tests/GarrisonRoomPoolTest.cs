namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Curated room types, optional landmark exhaustion and the actual three-floor generation path.</summary>
[TestSuite, RequireGodotRuntime]
public class GarrisonRoomPoolTest
{
	private const string Preset = "res://scenes/levels/garrison/factories/garrison_mixed_room_factory.tres";

	[TestCase]
	public void StandardSlotsDrawEveryRoomTypeAndReproduceTheirContent()
	{
		var factory = TestFactories.Load<MixedRoomFactory>(Preset);
		var seen = new HashSet<RoomType>();
		for (ulong seed = 1; seed <= 60; seed++)
		{
			using var a = new RandomNumberGenerator { Seed = seed };
			using var b = new RandomNumberGenerator { Seed = seed };
			var first = factory.CreateStandardRoom(a);
			var second = factory.CreateStandardRoom(b);
			AssertObject(first.Type).IsNotNull();
			AssertObject(first.Type).IsSame(second.Type);
			AssertString(Signature(first)).IsEqual(Signature(second));
			seen.Add(first.Type);
			first.Free();
			second.Free();
		}
		AssertInt(seen.Count).IsEqual(factory.RoomTypes.Length);
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
			AssertObject(room.Type).IsNotNull();
			if (index == 0) first = Signature(room);
			room.Free();
		}
		factory.Reset();
		rng.Seed = 42;
		var reset = factory.CreateSpecialRoom(rng);
		AssertString(Signature(reset)).IsEqual(first);
		reset.Free();
	}

	[TestCase]
	public async Task ThreeDepthsKeepConnectedGeometryEncountersAndReachableSpawns()
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
			AssertInt(generator.Floor.Rooms.Count(room => room.IsProcedural)).IsGreater(0);
			var ordinary = generator.EnemySpawnPoints.Where(spawn => encounters.Entries.Any(entry => entry.ScenePath == spawn.Scenes[0].ResourcePath)).ToArray();
			int spent = ordinary.Sum(spawn => encounters.Entries.Single(entry => entry.ScenePath == spawn.Scenes[0].ResourcePath).Threat);
			using var encounterRng = GenerationRandom.Create(generator.Seed, GenerationRandom.Monsters);
			AssertInt(generator.EnemySpawnPoints.Count).IsEqual(encounters.CreateEncounter(depth, encounterRng).Count);
			AssertBool(spent > 0 && spent <= encounters.FloorBudgets[depth - 1]).IsTrue();
			AssertInt(generator.EnemySpawnPoints.Count(spawn => spawn.Scenes[0] == encounters.NestScene)).IsEqual(depth == 2 ? 2 : 0);
			AssertBool(level.CanDescend).IsEqual(depth != 3);
			if (depth == 3) AssertInt(generator.EnemySpawnPoints.Count(spawn => spawn.PersistentId == level.Encounters.GatekeeperId)).IsEqual(1);
			AssertBool(generator.EnemySpawnPoints.All(spawn => generator.Floor.IsReachable(spawn.GlobalPosition))).IsTrue();
			AssertString(UnreachableLoot(generator)).OverrideFailureMessage($"seed={runSeed} depth={depth}").IsEmpty();
			string first = Fingerprint(generator);
			level.Generate();
			AssertString(Fingerprint(generator)).IsEqual(first);
			await runner.SimulateFrames(1);
		}
	}

	/// <summary>Pickups and traps lie on reachable floor; a chest blocks its own cells, so it needs reachable floor beside it.</summary>
	private static string UnreachableLoot(MapGenerator generator)
	{
		var floor = generator.Floor;
		bool Beside(Vector3 point) => Enumerable.Range(0, 8).Any(step =>
			floor.IsReachable(point + new Vector3(Mathf.Cos(step * Mathf.Tau / 8), 0, Mathf.Sin(step * Mathf.Tau / 8)) * 1.75f));
		foreach (var node in generator.NavigationRegion.FindChildren("*", "", true, false).OfType<Node3D>())
		{
			bool chest = node.HasMeta("persistent_id") && node.GetMeta("persistent_id").AsString().Contains(":chest:");
			if (chest && !Beside(node.GlobalPosition)) return $"chest at {node.GlobalPosition}";
			if (node is LootableItem or FloorTrap && !floor.IsReachable(node.GlobalPosition)) return $"{node.Name} at {node.GlobalPosition}";
		}
		return "";
	}

	private static string Signature(Room room) => string.Join("|", room.FloorGridMap.GetUsedCells().Select(cell =>
			$"{cell}:{room.FloorGridMap.GetCellItem(cell)}:{room.FloorGridMap.GetCellItemOrientation(cell)}"))
		+ string.Join("|", room.GetNode("Props").GetChildren().OfType<Node3D>().Select(prop => $"{prop.GetMeta("prop_id")}:{prop.Transform}"));

	private static string Fingerprint(MapGenerator generator) => string.Join("|", generator.Map.Tiles.Cast<MapTile>())
		+ string.Join("|", generator.EnemySpawnPoints.Select(spawn => $"{spawn.PersistentId}:{spawn.Scenes[0].ResourcePath}:{spawn.GlobalTransform}"))
		+ string.Join("|", generator.NavigationRegion.GetChildren().OfType<Room>().Where(room => room.Type != null)
			.SelectMany(room => room.GetNode("Props").GetChildren().OfType<Node3D>()).Select(prop => $"{prop.GetMeta("prop_id")}:{prop.GlobalTransform}"));
}