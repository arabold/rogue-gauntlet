namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Generation-time chest/trap/loose-item placement contracts, swept across seeds: a
/// placed chest sits on an interior room tile with its back to a real wall and its
/// rotation actually facing away from that wall (not just "some" wall it happened to
/// find); nothing lands adjacent to a connector (doorway traffic); every placed prop is
/// spaced apart and parented under <see cref="MapGenerator.NavigationRegion"/> (not
/// <c>Level</c>) so it becomes a navmesh obstacle the same way rooms already are; the
/// feature is fully opt-in — zero configured counts places nothing; and loot only ever
/// goes into procedurally built rooms — an authored room's interior is hand-designed
/// (including any chests it wants, placed by its author) and must never receive
/// generated content.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class LootPlacementTest
{
	private const int SeedCount = 30;

	[TestCase]
	public async Task PlacedChestsFaceAwayFromARealWall()
	{
		// All-procedural standard rooms: loot placement only considers procedural rooms,
		// so the default all-authored factory would yield zero placements to check.
		var (runner, mapGenerator) = await TestFactories.LoadGenerator(new PackedRoomLayout(), TestFactories.RoomPool());
		TestFactories.WireLootScenes(mapGenerator);
		mapGenerator.MinChests = mapGenerator.MaxChests = 2;

		var failures = new List<string>();
		for (ulong seed = 1; seed <= SeedCount; seed++)
		{
			mapGenerator.Seed = seed;
			mapGenerator.GenerateMap(includeGameplay: true);

			foreach (Chest chest in FindChests(mapGenerator))
			{
				failures.AddRange(FindChestPlacementFailures(mapGenerator, chest, seed));
			}

			await runner.SimulateFrames(1);
		}

		AssertArray(failures)
			.OverrideFailureMessage($"Found {failures.Count} chest placement failure(s) across {SeedCount} seeds:\n  "
				+ string.Join("\n  ", failures))
			.IsEmpty();
	}

	[TestCase]
	public async Task LooseItemsRollAsRewardSourcesAndSurviveASaveRoundTrip()
	{
		var (runner, level) = await TestFactories.LoadLevel(new PackedRoomLayout(), TestFactories.RoomPool());
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
		await runner.SimulateFrames(1);
	}

	[TestCase]
	public async Task PlacedLootIsParentedUnderNavigationRegionForNavmeshBaking()
	{
		var (runner, mapGenerator) = await TestFactories.LoadGenerator(new PackedRoomLayout(), TestFactories.RoomPool());
		TestFactories.WireLootScenes(mapGenerator);
		mapGenerator.MinChests = mapGenerator.MaxChests = 1;
		mapGenerator.MinTraps = mapGenerator.MaxTraps = 1;
		mapGenerator.MinLooseItems = mapGenerator.MaxLooseItems = 1;
		mapGenerator.Seed = 1;
		mapGenerator.GenerateMap(includeGameplay: true);

		var failures = new List<string>();
		foreach (Node node in mapGenerator.NavigationRegion.GetChildren())
		{
			if (node is not (Chest or FloorTrap or LootableItem))
			{
				continue;
			}

			if (node.GetParent() != mapGenerator.NavigationRegion)
			{
				failures.Add($"{node.Name} is parented under {node.GetParent()?.Name}, not NavigationRegion");
			}
		}

		await runner.SimulateFrames(1);
		AssertArray(failures).OverrideFailureMessage(string.Join("\n  ", failures)).IsEmpty();
	}

	[TestCase]
	public async Task PlacedLootRespectsMinimumSpacing()
	{
		var (runner, mapGenerator) = await TestFactories.LoadGenerator(new PackedRoomLayout(), TestFactories.RoomPool());
		TestFactories.WireLootScenes(mapGenerator);
		// The smallest map that still fits every request on every seed: a map too small to hold all nine pieces
		// silently drops some and checks fewer pairs, while larger maps mostly add corridor routing and navmesh time.
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

			var points = new List<Vector3>();
			foreach (Node3D node in FindLootNodes(mapGenerator))
			{
				points.Add(node.GlobalPosition);
			}

			if (points.Count < 3 * PerKind)
			{
				failures.Add($"seed {seed}: placed only {points.Count} of {3 * PerKind} requested pieces, so fewer pairs were checked");
			}

			for (int i = 0; i < points.Count; i++)
			{
				for (int j = i + 1; j < points.Count; j++)
				{
					float distance = new Vector2(points[i].X, points[i].Z).DistanceTo(new Vector2(points[j].X, points[j].Z));
					if (distance < MapGenerator.LootSpawnSpacing)
					{
						failures.Add($"seed {seed}: loot at {points[i]} and {points[j]} are only {distance:F2} apart "
							+ $"(minimum {MapGenerator.LootSpawnSpacing})");
					}
				}
			}

			await runner.SimulateFrames(1);
		}

		AssertArray(failures)
			.OverrideFailureMessage($"Found {failures.Count} spacing failure(s) across {SeedCount} seeds:\n  "
				+ string.Join("\n  ", failures))
			.IsEmpty();
	}

	[TestCase]
	public async Task UnwiredScenesPlaceNothing()
	{
		// A level that never calls TestFactories.WireLootScenes (i.e. never wires
		// ChestScene/TrapScene/LooseItemScene) leaves them at their null default -- the
		// count ranges default to a non-zero, immediately-useful value (see MaxChests
		// etc.), so it's the *scene references*, not the counts, that gate the feature off
		// for any pre-existing level/test that doesn't opt in.
		var (runner, mapGenerator) = await TestFactories.LoadGenerator(new PackedRoomLayout());
		mapGenerator.Seed = 1;
		mapGenerator.GenerateMap(includeGameplay: true);

		int lootNodeCount = 0;
		foreach (Node _ in FindLootNodes(mapGenerator))
		{
			lootNodeCount++;
		}

		await runner.SimulateFrames(1);
		AssertInt(lootNodeCount).IsEqual(0);
	}

	[TestCase]
	public void PickEntryOnlyReturnsConfiguredItemsAndHandlesEmptyTables()
	{
		// Exercise the real grouped resource every placed chest already uses.
		var table = TestFactories.Load<LootTable>("res://scenes/items/loot/loot_chest_common.tres");
		var rng = new RandomNumberGenerator { Seed = 1 };

		for (int i = 0; i < 50; i++)
		{
			var entry = table.PickEntry(1, rng);
			AssertObject(entry).IsNotNull();
			AssertBool(System.Array.IndexOf(table.Entries, entry) >= 0).IsTrue();
			AssertBool(entry.IsEligibleAt(1)).IsTrue();
		}

		var emptyTable = new LootTable();
		AssertObject(emptyTable.PickEntry(1, rng)).IsNull();
	}

	private static IEnumerable<Chest> FindChests(MapGenerator mapGenerator)
	{
		foreach (Node node in mapGenerator.NavigationRegion.GetChildren())
		{
			if (node is Chest chest)
			{
				yield return chest;
			}
		}
	}

	private static IEnumerable<Node3D> FindLootNodes(MapGenerator mapGenerator)
	{
		foreach (Node node in mapGenerator.NavigationRegion.GetChildren())
		{
			if (node is Chest or FloorTrap or LootableItem)
			{
				yield return (Node3D)node;
			}
		}
	}

	private static readonly Vector2I[] CardinalOffsets =
	{
		new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
	};

	private static List<string> FindChestPlacementFailures(MapGenerator mapGenerator, Chest chest, ulong seed)
	{
		var failures = new List<string>();
		Vector2I tile = mapGenerator.Map.WorldToTile(chest.GlobalPosition);
		MapData map = mapGenerator.Map;

		if (!map.IsRoom(tile.X, tile.Y))
		{
			failures.Add($"seed {seed}: chest at tile {tile} is not on a room tile");
			return failures;
		}

		foreach (var offset in CardinalOffsets)
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
		// faces South. Then assert the tile directly behind it -- the direction the
		// chest's back is turned to -- is actually a wall.
		Vector2I facing = Mathf.RoundToInt(chest.RotationDegrees.Y) switch
		{
			270 => Vector2I.Left,
			180 => Vector2I.Up,
			90 => Vector2I.Right,
			_ => Vector2I.Down,
		};
		Vector2I behind = tile - facing;
		bool behindIsWall = !map.IsWithinBounds(behind.X, behind.Y) || !map.IsWalkable(behind.X, behind.Y);
		if (!behindIsWall)
		{
			failures.Add($"seed {seed}: chest at tile {tile} faces {facing} but tile {behind} behind it isn't a wall");
		}

		return failures;
	}
}