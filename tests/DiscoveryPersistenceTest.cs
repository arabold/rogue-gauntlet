namespace RogueGauntlet.Tests;

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Exercise the same floor-local discovery, gates and save round trip that gameplay uses.</summary>
[TestSuite]
[RequireGodotRuntime]
public class DiscoveryPersistenceTest
{
	[TestCase]
	public async Task SampleHistorySurvivesRegenerationExactlyAndInvalidRestoreIsAtomic()
	{
		var (runner, level) = await TestFactories.LoadLevel(new PackedRoomLayout(), TestFactories.GarrisonRoomsWithDoors());
		level.Generate();
		AssertInt(level.CaptureExploration().ExploredSamples.Count).IsEqual(0);
		Vector3 position = level.Floor.Arrival.GlobalPosition;
		level.Discovery.RevealFrom(position);
		var saved = JsonSerializer.Deserialize<RevealedLevelSaveData>(JsonSerializer.Serialize(level.CaptureExploration()));
		AssertBool(saved.ExploredSamples.Count > 0).IsTrue();
		AssertBool(level.Discovery.IsRevealedAt(position)).IsTrue();
		level.Generate();
		AssertInt(level.CaptureExploration().ExploredSamples.Count).IsEqual(0);
		level.RestoreExploration(saved);
		AssertBool(level.CaptureExploration().ExploredSamples.SequenceEqual(saved.ExploredSamples)).IsTrue();
		AssertBool(level.Discovery.IsRevealedAt(position)).IsTrue();
		saved.ExploredSamples.Add(int.MaxValue);
		bool rejected = false;
		try { level.RestoreExploration(saved); } catch (InvalidOperationException) { rejected = true; }
		AssertBool(rejected).IsTrue();
		AssertInt(level.CaptureExploration().ExploredSamples.Count).IsEqual(saved.ExploredSamples.Count - 1);
		await runner.SimulateFrames(2);
	}

	[TestCase]
	public async Task DoorEventsDoNotRevealAnEntireDestination()
	{
		var (runner, level) = await TestFactories.LoadLevel(new PackedRoomLayout(), TestFactories.GarrisonRoomsWithDoors());
		level.Generate();
		var door = level.Floor.Doors[0].Door;
		door.IsOpen = true;
		SignalBus.EmitDoorOpened(door);
		AssertInt(level.CaptureExploration().ExploredSamples.Count).IsEqual(0);
		await runner.SimulateFrames(1);
	}

	[TestCase]
	public async Task AdjacentCorridorRequiresAnOpeningAndSecretsStaySealedUntilOpened()
	{
		var (runner, level) = await TestFactories.LoadLevel(new PackedRoomLayout(), TestFactories.GarrisonRooms());
		var map = level.MapGenerator;
		level.Generate();
		map.Map.ResetToBorderedEmpty();
		map.Map.SetTile(3, 5, MapTile.Room);
		map.Map.SetTile(4, 5, MapTile.Corridor);
		Vector3 Point(Vector2I tile) => map.Map.TileToWorld(tile);
		level.Discovery.RevealFrom(Point(new Vector2I(3, 5)));
		AssertBool(level.Discovery.IsRevealedAt(Point(new Vector2I(4, 5)))).IsFalse();
		map.Map.SetConnector(3, 5, [Vector2I.Right]);
		level.Discovery.RevealFrom(Point(new Vector2I(3, 5)));
		AssertBool(level.Discovery.IsRevealedAt(Point(new Vector2I(4, 5)))).IsTrue();

		map.AccessProfile = (LevelAccessProfile)TestFactories.Load<LevelAccessProfile>("res://scenes/levels/access/garrison_access.tres").Duplicate(true);
		map.AccessProfile.MaximumLockedRooms = 0;
		map.AccessProfile.SecretRoomChance = 1;
		map.MapWidth = map.MapDepth = 30; map.MaxRooms = 6;
		for (ulong seed = 1; seed <= 8; seed++)
		{
			map.Seed = seed; level.Generate();
			if (map.GeneratedSecrets.Count > 0) break;
			await runner.SimulateFrames(1);
		}
		AssertBool(map.GeneratedSecrets.Count > 0).IsTrue();
		var secret = map.GeneratedSecrets[0];
		level.Discovery.RevealFrom(Point(secret.Host));
		AssertBool(level.Discovery.IsRevealedAt(Point(secret.Connector))).IsFalse();
		secret.Door.Secret.Restore(true);
		level.Discovery.RevealFrom(Point(secret.Host));
		AssertBool(level.Discovery.IsRevealedAt(Point(secret.Connector))).IsFalse();
		// Seals are read from the doors themselves: opening the leaf is enough, no event needed.
		secret.Door.IsOpen = true;
		level.Discovery.RevealFrom(Point(secret.Host));
		AssertBool(level.Discovery.IsRevealedAt(Point(secret.Connector))).IsTrue();
		await runner.SimulateFrames(2);
	}

	[TestCase]
	public async Task OpeningOneOfTwoAdjacentDooredConnectorsUnsealsOnlyThatOne()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/world_effect_harness.tscn");
		await runner.SimulateFrames(2);
		var (level, generator) = TestFactories.UngeneratedLevel();
		TestFactories.AddToSceneTree(level);
		// room | connector (opens right) | corridor | connector (opens left) | room
		var map = new MapData(12, 12);
		map.ResetToBorderedEmpty();
		map.SetTile(2, 5, MapTile.Room);
		map.SetConnector(3, 5, [Vector2I.Right]);
		map.SetTile(4, 5, MapTile.Corridor);
		map.SetConnector(5, 5, [Vector2I.Left]);
		map.SetTile(6, 5, MapTile.Room);
		var floor = new GeneratedFloor(map, generator, generator.DecorationGridMap);
		var doorScene = TestFactories.Load<LevelAccessProfile>("res://scenes/levels/access/garrison_access.tres").DoorScene;
		var near = TestFactories.AddToSceneTree(doorScene.Instantiate<Door>());
		var far = TestFactories.AddToSceneTree(doorScene.Instantiate<Door>());
		floor.AddDoor(near, new Vector2I(3, 5));
		floor.AddDoor(far, new Vector2I(5, 5));
		level.Discovery.Initialize(floor);
		Vector3 origin = map.TileToWorld(2, 5);
		level.Discovery.RevealFrom(origin);
		AssertBool(level.Discovery.IsRevealedAt(map.TileToWorld(4, 5))).IsFalse();
		near.IsOpen = true;
		level.Discovery.RevealFrom(origin);
		AssertBool(level.Discovery.IsRevealedAt(map.TileToWorld(4, 5))).IsTrue();
		AssertBool(level.Discovery.IsRevealedAt(map.TileToWorld(6, 5))).IsFalse();
		far.IsOpen = true;
		level.Discovery.RevealFrom(origin);
		AssertBool(level.Discovery.IsRevealedAt(map.TileToWorld(6, 5))).IsTrue();
		await runner.SimulateFrames(1);
	}
}