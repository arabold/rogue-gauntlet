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
		var (runner, level) = await TestFactories.LoadLevel(new PackedRoomLayout());
		using var scope = runner;
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
		await runner.SimulateFrames(2, 20);
	}

	/// <summary>
	/// Sight crosses a room edge only through a connector: a room beside a corridor without one stays sealed, and of two
	/// dooring connectors in a row, opening one unseals only the space up to the next. Door events alone reveal nothing.
	/// </summary>
	[TestCase]
	public void SightCrossesRoomEdgesOnlyThroughOpenConnectors()
	{
		var (level, generator) = TestFactories.UngeneratedLevel();
		TestFactories.AddToSceneTree(level);
		// room | connector (opens right) | corridor | connector (opens left) | room, and an unconnected room beside a corridor.
		var map = new MapData(12, 12);
		map.ResetToBorderedEmpty();
		map.SetTile(2, 5, MapTile.Room);
		map.SetConnector(3, 5, [Vector2I.Right]);
		map.SetTile(4, 5, MapTile.Corridor);
		map.SetConnector(5, 5, [Vector2I.Left]);
		map.SetTile(6, 5, MapTile.Room);
		map.SetTile(2, 9, MapTile.Room);
		map.SetTile(3, 9, MapTile.Corridor);
		var floor = new GeneratedFloor(map, generator, generator.DecorationGridMap);
		var doorScene = TestFactories.Load<PackedScene>("res://scenes/props/door.tscn");
		var near = TestFactories.AddToSceneTree(doorScene.Instantiate<Door>());
		var far = TestFactories.AddToSceneTree(doorScene.Instantiate<Door>());
		floor.AddDoor(near, new Vector2I(3, 5));
		floor.AddDoor(far, new Vector2I(5, 5));
		level.Discovery.Initialize(floor);

		level.Discovery.RevealFrom(map.TileToWorld(2, 9));
		AssertBool(level.Discovery.IsRevealedAt(map.TileToWorld(3, 9))).OverrideFailureMessage("Sight leaked into a corridor without a connector").IsFalse();

		near.IsOpen = far.IsOpen = true;
		SignalBus.EmitDoorOpened(near);
		SignalBus.EmitDoorOpened(far);
		AssertBool(level.Discovery.IsRevealedAt(map.TileToWorld(4, 5)) || level.Discovery.IsRevealedAt(map.TileToWorld(6, 5)))
			.OverrideFailureMessage("A door event revealed space nobody saw").IsFalse();
		near.IsOpen = far.IsOpen = false;
		SignalBus.EmitDoorClosed(near);
		SignalBus.EmitDoorClosed(far);

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
	}

	/// <summary>A secret doorway seals sight from the corridor until its door is open, even once discovered.</summary>
	[TestCase]
	public async Task SecretsStaySealedUntilOpened()
	{
		var (runner, level) = await TestFactories.LoadLevel(new PackedRoomLayout(), TestFactories.RoomPoolWithSecrets(1));
		using var scope = runner;
		var map = level.MapGenerator;
		map.MapWidth = map.MapDepth = 30; map.MaxRooms = 6;
		level.Generate();
		var secret = map.Floor.Doors.First(d => d.Door.Secret?.IsSecret == true);
		Vector3 Point(Vector2I tile) => map.Map.TileToWorld(tile);
		var direction = map.Map.GetConnectorDirections(secret.Connector.X, secret.Connector.Y).Single();
		var outside = secret.Connector + direction;
		level.Discovery.RevealFrom(Point(outside));
		AssertBool(level.Discovery.IsRevealedAt(Point(secret.Connector))).IsFalse();
		secret.Door.Secret.Restore(true);
		level.Discovery.RevealFrom(Point(outside));
		AssertBool(level.Discovery.IsRevealedAt(Point(secret.Connector))).IsFalse();
		secret.Door.IsOpen = true;
		level.Discovery.RevealFrom(Point(outside));
		AssertBool(level.Discovery.IsRevealedAt(Point(secret.Connector))).IsTrue();
		await runner.SimulateFrames(2, 20);
	}
}