namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>The production room pool: every type drawable, seeded replay, and the actual first three floors it builds.</summary>
[TestSuite, RequireGodotRuntime]
public class GarrisonRoomPoolTest
{
	private const string Preset = TestFactories.RoomPoolPath;

	/// <summary>Every room type in the pool can be drawn: some realm's view holds it, only realms it is tagged for do, and its weight is positive.</summary>
	[TestCase]
	public void EveryPooledRoomTypeIsDrawnByTheRealmsItBelongsTo()
	{
		var pool = TestFactories.Load<AssembledRoomFactory>(Preset);
		var drawn = new HashSet<RoomType>();
		foreach (string realm in new[] { "garrison", "sewer", "jungle" })
		{
			var factory = (AssembledRoomFactory)pool.ForRealm(realm);
			AssertBool(factory.RoomTypes.Length > 0).OverrideFailureMessage($"{realm}: no room types").IsTrue();
			foreach (var type in factory.RoomTypes)
			{
				AssertBool(type.Realms.Contains(realm)).OverrideFailureMessage($"{realm} draws {type.Id}, which is not tagged for it").IsTrue();
				AssertFloat(type.Weight).OverrideFailureMessage($"{realm}: {type.Id} can never be drawn").IsGreater(0);
				drawn.Add(type);
			}
		}
		AssertArray(pool.RoomTypes.Where(type => !drawn.Contains(type)).Select(type => type.Id).ToArray()).IsEmpty();
	}

	[TestCase]
	public void EverySlotIsAssembledAndSeedReproducesItsGeometry()
	{
		var factory = TestFactories.Load<AssembledRoomFactory>(Preset);
		using var rng = new RandomNumberGenerator { Seed = 42 };
		string first = "";
		for (int index = 0; index < 8; index++)
		{
			var room = (index % 3) switch
			{
				0 => factory.CreateStandardRoom(rng),
				1 => factory.CreateEntrance(rng),
				_ => factory.CreateExit(rng),
			};
			AssertObject(room.Type).IsNotNull();
			if (index == 0) first = Signature(room);
			room.Free();
		}
		rng.Seed = 42;
		var reset = factory.CreateStandardRoom(rng);
		AssertString(Signature(reset)).IsEqual(first);
		reset.Free();
	}

	[TestCase]
	public async Task ThreeDepthsKeepConnectedGeometryEncountersAndReachableSpawns()
	{
		using var runner = TestFactories.LoadScene("res://scenes/levels/level.tscn");
		await runner.SimulateFrames(2, 20);
		var level = (Level)runner.Scene();
		var generator = level.MapGenerator;
		AssertString(generator.Rooms.ResourcePath).IsEqual(Preset);
		var encounters = (RealmEncounterFactory)generator.MobFactory;
		string failure = "";
		level.GenerationFailed += error => failure = error;
		const ulong runSeed = 23;
		for (uint depth = 1; depth <= 3; depth++)
		{
			level.ConfigureGeneration(new RunState(runSeed), depth, null, null);
			failure = "";
			level.Generate();
			AssertString(failure).OverrideFailureMessage($"seed={runSeed} depth={depth}: {failure}").IsEmpty();
			AssertArray(TestFactories.ConnectivityFailures(generator).ToArray()).OverrideFailureMessage($"seed={runSeed} depth={depth}").IsEmpty();
			// Every pack's members are placed, and stand together in one room around the pack's first member.
			using var encounterRng = GenerationRandom.Create(generator.Seed, GenerationRandom.Monsters);
			var packs = encounters.CreatePacks(depth, level.ThreatProfile, encounterRng);
			int floor = encounters.FloorIndex(depth);
			int replaced = floor == RealmEncounterFactory.GatekeeperFloorIndex ? 1 : floor == RealmEncounterFactory.NestFloorIndex ? MapGenerator.NestsPerFloor : 0;
			AssertInt(generator.EnemySpawnPoints.Count).IsEqual(replaced + packs.Skip(replaced).Sum(pack => pack.Length));
			var ordinary = generator.EnemySpawnPoints.Where(spawn => spawn.PersistentId.Contains(":pack:") && !generator.Floor.IsNest(spawn));
			foreach (var pack in ordinary.GroupBy(spawn => spawn.PersistentId[..spawn.PersistentId.LastIndexOf(':')]))
			{
				Vector3 lead = pack.First().GlobalPosition;
				AssertBool(pack.All(spawn => spawn.GlobalPosition.HorizontalDistanceTo(lead) <= MapGenerator.PackRadius + .01f
					&& generator.Floor.RoomIdAt(spawn.GlobalPosition) == generator.Floor.RoomIdAt(lead))).OverrideFailureMessage(pack.Key).IsTrue();
			}
			// Monsters keep the rules their pack's anchor keeps: off props and decoration, clear of the arrival and stairs.
			// A nest is a prop itself, so its own tile counts as occupied once it stands there.
			foreach (var spawn in generator.EnemySpawnPoints.Where(spawn => spawn != generator.Floor.GatekeeperSpawn && !generator.Floor.IsNest(spawn)))
			{
				Vector2I tile = generator.Floor.Map.WorldToTile(spawn.GlobalPosition);
				AssertBool(generator.Floor.IsOccupied(tile.X, tile.Y) || generator.Floor.IsInSpawnClearance(spawn.GlobalPosition))
					.OverrideFailureMessage($"seed={runSeed} {spawn.PersistentId}").IsFalse();
			}
			AssertInt(generator.EnemySpawnPoints.Count(generator.Floor.IsNest)).IsEqual(floor == RealmEncounterFactory.NestFloorIndex ? MapGenerator.NestsPerFloor : 0);
			AssertBool(level.CanDescend).IsEqual(depth != 3);
			if (depth == 3) AssertInt(generator.EnemySpawnPoints.Count(spawn => spawn.PersistentId == level.Encounters.GatekeeperId)).IsEqual(1);
			AssertBool(generator.EnemySpawnPoints.All(spawn => generator.Floor.IsReachable(spawn.GlobalPosition))).IsTrue();
			AssertString(UnreachableLoot(generator)).OverrideFailureMessage($"seed={runSeed} depth={depth}").IsEmpty();
			string first = Fingerprint(generator);
			level.Generate();
			AssertString(Fingerprint(generator)).IsEqual(first);
			await runner.SimulateFrames(1, 20);
		}
	}

	/// <summary>Pickups and traps lie on reachable floor; a chest blocks its own cells, so it needs reachable floor beside it.</summary>
	private static string UnreachableLoot(MapGenerator generator)
	{
		var floor = generator.Floor;
		bool Beside(Vector3 point) => Enumerable.Range(0, 8).Any(step =>
			floor.IsReachable(point + new Vector3(Mathf.Cos(step * Mathf.Tau / 8), 0, Mathf.Sin(step * Mathf.Tau / 8)) * 1.75f));
		foreach (var node in generator.FloorContent.FindChildren("*", "", true, false).OfType<Node3D>())
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
		+ string.Join("|", generator.FloorContent.GetChildren().OfType<Room>().Where(room => room.Type != null)
			.SelectMany(room => room.GetNode("Props").GetChildren().OfType<Node3D>()).Select(prop => $"{prop.GetMeta("prop_id")}:{prop.GlobalTransform}"));
}