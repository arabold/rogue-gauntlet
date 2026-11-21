namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Generation-time chest, trap and loose-item placement, swept across seeds: every requested piece is placed, pieces
/// keep their minimum spacing, and a chest sits on a room tile clear of doorway traffic with its back to a real wall.
/// Loose items roll as reward sources that survive a save round trip.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class LootPlacementTest
{
	private const int SeedCount = 15;

	[TestCase]
	public async Task PlacedLootIsSpacedAndChestsBackOntoRealWalls()
	{
		var (runner, mapGenerator) = await TestFactories.LoadGenerator(new PackedRoomLayout());
		using (runner)
		{
			TestFactories.WireLootScenes(mapGenerator);
			// The smallest map that still fits every request on every seed: a map too small to hold all nine pieces
			// silently drops some and checks fewer pairs, while larger maps mostly add corridor routing and grid time.
			const int PerKind = 3;
			mapGenerator.MapWidth = 30;
			mapGenerator.MapDepth = 30;
			mapGenerator.MaxRooms = 14;
			mapGenerator.MinChests = mapGenerator.MaxChests = PerKind;
			mapGenerator.MinTraps = mapGenerator.MaxTraps = PerKind;
			mapGenerator.MinLooseItems = mapGenerator.MaxLooseItems = PerKind;

			var failures = new List<string>();
			for (ulong seed = 1; seed <= SeedCount; seed++)
			{
				mapGenerator.Seed = seed;
				mapGenerator.GenerateMap(includeGameplay: true);

				var loot = mapGenerator.FloorContent.GetChildren().Where(node => node is Chest or FloorTrap or LootableItem).Cast<Node3D>().ToArray();
				if (loot.Length < 3 * PerKind)
				{
					failures.Add($"seed {seed}: placed only {loot.Length} of {3 * PerKind} requested pieces, so fewer pairs were checked");
				}
				for (int i = 0; i < loot.Length; i++)
				{
					for (int j = i + 1; j < loot.Length; j++)
					{
						float distance = loot[i].GlobalPosition.HorizontalDistanceTo(loot[j].GlobalPosition);
						if (distance < MapGenerator.LootSpawnSpacing)
						{
							failures.Add($"seed {seed}: loot at {loot[i].GlobalPosition} and {loot[j].GlobalPosition} are only {distance:F2} apart "
								+ $"(minimum {MapGenerator.LootSpawnSpacing})");
						}
					}
				}
				foreach (Chest chest in loot.OfType<Chest>())
				{
					failures.AddRange(FindChestPlacementFailures(mapGenerator, chest, seed));
				}

				await runner.SimulateFrames(1, 20);
			}

			AssertArray(failures)
				.OverrideFailureMessage($"Found {failures.Count} placement failure(s) across {SeedCount} seeds:\n  "
					+ string.Join("\n  ", failures))
				.IsEmpty();
		}
	}

	[TestCase]
	public async Task LooseItemsRollAsRewardSourcesAndSurviveASaveRoundTrip()
	{
		var (runner, level) = await TestFactories.LoadLevel(new PackedRoomLayout());
		using var scope = runner;
		var map = level.MapGenerator;
		TestFactories.WireLootScenes(map);
		map.MinLooseItems = map.MaxLooseItems = 3;
		level.Generate();
		LootableItem[] Loose() => level.FindChildren("*", "", true, false).OfType<LootableItem>()
			.Where(p => !p.IsQueuedForDeletion() && p.GetMeta("persistent_id", "").AsString().Contains(":loose:")).ToArray();
		var loose = Loose();
		AssertInt(loose.Length).IsGreater(1);
		// Instance ids follow the reward-source scheme, so a spot's items are keyed like a chest's.
		AssertBool(loose.All(p => p.Item.InstanceId.StartsWith(p.GetMeta("persistent_id").AsString() + ":drop:"))).IsTrue();
		string taken = loose[0].Item.InstanceId;
		loose[0].CommitCollected();
		var delta = JsonSerializer.Deserialize<LevelItemDeltaSaveData>(JsonSerializer.Serialize(level.ItemPersistence.Capture()));
		AssertBool(delta.RemovedItemIds.Contains(taken)).IsTrue();
		var survivors = loose.Skip(1).Select(p => p.Item.InstanceId).ToArray();
		level.Generate();
		level.ItemPersistence.Apply(delta);
		// Applying a delta rebuilds every world pickup from its record, so match the floor by instance id.
		var ground = level.FindChildren("*", "", true, false).OfType<LootableItem>()
			.Where(p => !p.IsQueuedForDeletion()).Select(p => p.Item.InstanceId).ToHashSet();
		AssertBool(ground.Contains(taken)).IsFalse();
		AssertBool(survivors.All(ground.Contains)).IsTrue();
		await runner.SimulateFrames(1, 20);
	}

	[TestCase]
	public async Task CorridorLootRespectsWallBackingAndDeadEndWeights()
	{
		var profile = TestFactories.CreateFloorProfile();
		profile.DeadEndLootWeight = 6f;
		var (runner, mapGenerator) = await TestFactories.LoadGenerator(profile);
		using (runner)
		{
			TestFactories.WireLootScenes(mapGenerator);
			mapGenerator.MapWidth = 26;
			mapGenerator.MapDepth = 26;
			mapGenerator.MaxRooms = 8;
			mapGenerator.MinChests = mapGenerator.MaxChests = 3;
			mapGenerator.MinTraps = mapGenerator.MaxTraps = 2;
			mapGenerator.MinLooseItems = mapGenerator.MaxLooseItems = 4;

			for (ulong seed = 1; seed <= 6; seed++)
			{
				mapGenerator.Seed = seed;
				mapGenerator.GenerateMap(includeGameplay: true);
				var loot = mapGenerator.FloorContent.GetChildren().Where(node => node is Chest or FloorTrap or LootableItem).Cast<Node3D>().ToArray();
				foreach (var item in loot)
				{
					var tile = mapGenerator.Map.WorldToTile(item.GlobalPosition);
					AssertBool(mapGenerator.Map.IsRoom(tile.X, tile.Y) || mapGenerator.Map.IsCorridor(tile.X, tile.Y)).IsTrue();
					AssertBool(TestFactories.IsLootReachable(mapGenerator.Floor, item)).OverrideFailureMessage($"seed {seed}: {item.Name} at {item.GlobalPosition} is unreachable").IsTrue();
				}
				foreach (Chest chest in loot.OfType<Chest>())
				{
					AssertArray(FindChestPlacementFailures(mapGenerator, chest, seed)).IsEmpty();
				}
				await runner.SimulateFrames(1, 20);
			}
		}
	}

	private static List<string> FindChestPlacementFailures(MapGenerator mapGenerator, Chest chest, ulong seed)
	{
		var failures = new List<string>();
		Vector2I tile = mapGenerator.Map.WorldToTile(chest.GlobalPosition);
		MapData map = mapGenerator.Map;

		if (!map.IsRoom(tile.X, tile.Y) && !map.IsCorridor(tile.X, tile.Y))
		{
			failures.Add($"seed {seed}: chest at tile {tile} is not on a room or corridor tile");
			return failures;
		}

		foreach (var offset in MapData.CardinalOffsets)
		{
			var neighbor = tile + offset;
			if (map.IsWithinBounds(neighbor.X, neighbor.Y) && map.IsConnector(neighbor.X, neighbor.Y))
			{
				failures.Add($"seed {seed}: chest at tile {tile} is adjacent to connector {neighbor}");
			}
		}

		// Recover the direction the chest is facing from its rotation. GetChestCandidates
		// rotates toward the WALL direction (not away from it): chest.tscn's lid/opening
		// faces the opposite way from the North-facing convention MapData.FacingYaw
		// assumes, so rotating toward the wall makes the chest physically face away from
		// it. That means this table maps each rotation to the opposite of FacingYaw's own
		// inverse -- e.g. rotation 0 came from FacingYaw(North), so the chest actually
		// faces South. Then assert the tile directly behind it has a wall via HasWallToward.
		Vector2I facing = Mathf.RoundToInt(chest.RotationDegrees.Y) switch
		{
			270 => Vector2I.Left,
			180 => Vector2I.Up,
			90 => Vector2I.Right,
			_ => Vector2I.Down,
		};
		if (!map.HasWallToward(tile.X, tile.Y, -facing))
		{
			failures.Add($"seed {seed}: chest at tile {tile} faces {facing} but edge {-facing} behind it isn't a wall");
		}

		return failures;
	}
}