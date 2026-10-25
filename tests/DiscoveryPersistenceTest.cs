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
		var (runner, map) = await TestFactories.LoadGenerator(new PackedRoomLayout(), TestFactories.AllProceduralWithDoors());
		map.GenerateMap();
		var material = (ShaderMaterial)map.GetNode<MeshInstance3D>("DiscoverySurface").MaterialOverride;
		var texture = material.GetShaderParameter("exploration").As<ImageTexture>();
		AssertFloat(texture.GetImage().GetPixel(0, 0).R).IsEqual(0);
		Vector3 position = map.PlayerSpawnPoint.GlobalPosition;
		map.RevealFrom(position);
		var saved = JsonSerializer.Deserialize<RevealedLevelSaveData>(JsonSerializer.Serialize(map.CaptureExploration()));
		AssertBool(saved.ExploredSamples.Count > 0).IsTrue();
		AssertBool(map.IsRevealedAt(position)).IsTrue();
		map.GenerateMap();
		AssertInt(map.CaptureExploration().ExploredSamples.Count).IsEqual(0);
		map.RestoreExploration(saved);
		AssertBool(map.CaptureExploration().ExploredSamples.SequenceEqual(saved.ExploredSamples)).IsTrue();
		AssertBool(map.IsRevealedAt(position)).IsTrue();
		saved.ExploredSamples.Add(int.MaxValue);
		bool rejected = false;
		try { map.RestoreExploration(saved); } catch (InvalidOperationException) { rejected = true; }
		AssertBool(rejected).IsTrue();
		AssertInt(map.CaptureExploration().ExploredSamples.Count).IsEqual(saved.ExploredSamples.Count - 1);
		await runner.SimulateFrames(2);
	}

	[TestCase]
	public async Task DoorEventsDoNotRevealAnEntireDestination()
	{
		var (runner, map) = await TestFactories.LoadGenerator(new PackedRoomLayout(), TestFactories.AllProceduralWithDoors());
		map.GenerateMap();
		var door = map.NavigationRegion.FindChildren("*", "", true, false).OfType<Door>().First();
		map.OpenDoorAt(door.GlobalPosition);
		AssertInt(map.CaptureExploration().ExploredSamples.Count).IsEqual(0);
		await runner.SimulateFrames(1);
	}
	[TestCase]
	public async Task AdjacentCorridorRequiresAnOpeningAndSecretsStaySealedUntilOpened()
	{
		var (runner, map) = await TestFactories.LoadGenerator(new PackedRoomLayout(), TestFactories.AllProcedural());
		map.GenerateMap();
		map.Map.ResetToBorderedEmpty();
		map.Map.SetTile(3, 5, MapTile.Room);
		map.Map.SetTile(4, 5, MapTile.Corridor);
		Vector3 Point(Vector2I tile) => new((tile.X - map.Map.Width / 2) * 4, 0, (tile.Y - map.Map.Height / 2) * 4);
		map.RevealFrom(Point(new Vector2I(3, 5)));
		AssertBool(map.IsRevealedAt(Point(new Vector2I(4, 5)))).IsFalse();
		map.Map.SetConnector(3, 5, [Vector2I.Right]);
		map.RevealFrom(Point(new Vector2I(3, 5)));
		AssertBool(map.IsRevealedAt(Point(new Vector2I(4, 5)))).IsTrue();

		map.AccessProfile = (LevelAccessProfile)GD.Load<LevelAccessProfile>("res://scenes/levels/access/garrison_access.tres").Duplicate(true);
		map.AccessProfile.MaximumLockedRooms = 0;
		map.AccessProfile.SecretRoomChance = 1;
		map.MapWidth = map.MapDepth = 30; map.MaxRooms = 6;
		for (ulong seed = 1; seed <= 8; seed++)
		{
			map.Seed = seed; map.GenerateMap();
			if (map.GeneratedSecrets.Count > 0) break;
			await runner.SimulateFrames(1);
		}
		AssertBool(map.GeneratedSecrets.Count > 0).IsTrue();
		var secret = map.GeneratedSecrets[0];
		map.RevealFrom(Point(secret.Host));
		AssertBool(map.IsRevealedAt(Point(secret.Connector))).IsFalse();
		secret.Door.Secret.Restore(true);
		map.RevealFrom(Point(secret.Host));
		AssertBool(map.IsRevealedAt(Point(secret.Connector))).IsFalse();
		map.OpenDoorAt(secret.Door.GlobalPosition);
		map.RevealFrom(Point(secret.Host));
		AssertBool(map.IsRevealedAt(Point(secret.Connector))).IsTrue();
		await runner.SimulateFrames(2);
	}

}