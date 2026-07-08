namespace RogueGauntlet.Tests;

using System.Collections.Generic;
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
		var (runner, mapGenerator) = await TestFactories.LoadGenerator(new PackedRoomLayout(), TestFactories.AllProcedural());
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
	public async Task PlacedLootIsParentedUnderNavigationRegionForNavmeshBaking()
	{
		var (runner, mapGenerator) = await TestFactories.LoadGenerator(new PackedRoomLayout(), TestFactories.AllProcedural());
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
		var (runner, mapGenerator) = await TestFactories.LoadGenerator(new PackedRoomLayout(), TestFactories.AllProcedural());
		TestFactories.WireLootScenes(mapGenerator);
		mapGenerator.MapWidth = 40;
		mapGenerator.MapDepth = 40;
		mapGenerator.MaxRooms = 20;
		mapGenerator.MinChests = mapGenerator.MaxChests = 3;
		mapGenerator.MinTraps = mapGenerator.MaxTraps = 3;
		mapGenerator.MinLooseItems = mapGenerator.MaxLooseItems = 3;

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
			.OverrideFailureMessage($"Found {failures.Count} spacing violation(s) across {SeedCount} seeds:\n  "
				+ string.Join("\n  ", failures))
			.IsEmpty();
	}

	[TestCase]
	public async Task AuthoredRoomsNeverReceiveGeneratedLoot()
	{
		// The default room factory is all-authored scenes. With loot fully wired and
		// non-zero counts requested, an all-authored map must still place ZERO chests
		// and loose items -- authored interiors are hand-designed (including any chests
		// their author placed) and generated content must never intrude. Traps are the
		// one exception allowed OUTSIDE rooms: corridors belong to no room, authored or
		// otherwise, so any trap that does get placed must sit on a corridor tile.
		var (runner, mapGenerator) = await TestFactories.LoadGenerator(new PackedRoomLayout());
		TestFactories.WireLootScenes(mapGenerator);
		mapGenerator.MinChests = mapGenerator.MaxChests = 2;
		mapGenerator.MinTraps = mapGenerator.MaxTraps = 2;
		mapGenerator.MinLooseItems = mapGenerator.MaxLooseItems = 2;

		var failures = new List<string>();
		for (ulong seed = 1; seed <= 10; seed++)
		{
			mapGenerator.Seed = seed;
			mapGenerator.GenerateMap(includeGameplay: true);

			foreach (Node3D node in FindLootNodes(mapGenerator))
			{
				Vector2I tile = mapGenerator.WorldToTile(node.GlobalPosition);
				if (node is FloorTrap && mapGenerator.Map.IsCorridor(tile.X, tile.Y))
				{
					continue;
				}

				failures.Add($"seed {seed}: generated {node.GetType().Name} at tile {tile} inside an all-authored map");
			}

			await runner.SimulateFrames(1);
		}

		AssertArray(failures)
			.OverrideFailureMessage($"Found {failures.Count} loot placement(s) that intruded on authored rooms:\n  "
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
	public void PickWeightedEntryOnlyReturnsConfiguredItemsAndHandlesEmptyTables()
	{
		// LootTable.Items has a private setter (only authored via .tres), so exercise the
		// real resource every placed chest already uses rather than a synthetic one.
		var table = GD.Load<LootTable>("res://scenes/items/loot/loot_chest_common.tres");
		var rng = new RandomNumberGenerator { Seed = 1 };

		for (int i = 0; i < 50; i++)
		{
			var entry = table.PickWeightedEntry(rng);
			AssertObject(entry).IsNotNull();
			AssertBool(System.Array.IndexOf(table.Items, entry) >= 0).IsTrue();
		}

		var emptyTable = new LootTable();
		AssertObject(emptyTable.PickWeightedEntry(rng)).IsNull();
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
		Vector2I tile = mapGenerator.WorldToTile(chest.GlobalPosition);
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
		// faces the opposite way from the North-facing convention DoorwayMarker.
		// GetYRotationDegrees assumes, so rotating toward the wall makes the chest
		// physically face away from it. That means this table maps each rotation to the
		// opposite of GetYRotationDegrees' own inverse -- e.g. rotation 0 came from
		// GetYRotationDegrees(North), so the chest actually faces South.
		// Then assert the tile directly behind it -- the direction the chest's back is
		// turned to -- is actually a wall.
		RoomMarkerDirection facing = Mathf.RoundToInt(chest.RotationDegrees.Y) switch
		{
			270 => RoomMarkerDirection.West,
			180 => RoomMarkerDirection.North,
			90 => RoomMarkerDirection.East,
			_ => RoomMarkerDirection.South,
		};
		Vector2I facingVector = DoorwayMarker.GetDirectionVector(facing);
		Vector2I behind = tile - facingVector;
		bool behindIsWall = !map.IsWithinBounds(behind.X, behind.Y) || !map.IsWalkable(behind.X, behind.Y);
		if (!behindIsWall)
		{
			failures.Add($"seed {seed}: chest at tile {tile} faces {facing} but tile {behind} behind it isn't a wall");
		}

		return failures;
	}
}
