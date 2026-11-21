namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Floor-generation helpers: the authored realms, a stress pool and the floor-level connectivity contract.</summary>
public static partial class TestFactories
{
	/// <summary>
	/// Gives a generator from <see cref="LoadGenerator"/> the authored realms instead of the Garrison it is pinned to, so
	/// its depth (see <see cref="RealmDepth"/>) picks each floor's rooms and corridors.
	/// </summary>
	public static void UseAuthoredRealms(MapGenerator generator)
	{
		generator.Realms = new[] { "garrison", "prison", "sewer", "mines", "cathedral" }.Select(id => Load<Realm>($"res://scenes/levels/realms/{id}.tres")).ToArray();
	}

	/// <summary>
	/// A depth for <paramref name="seed"/> that spreads a seed sweep over the realms: half Garrison (depth 1, where seed
	/// regressions were found), a quarter each Sewer (depth 11) and Mines (depth 16, the jungle theme). Layouts depend on the seed and theme, not the depth.
	/// </summary>
	public static uint RealmDepth(ulong seed) => (seed % 4) switch { 2 => 11u, 0 => 16u, _ => 1u };

	/// <summary>
	/// Stress profile where most doorways hold a door and some are concealed: tests doorway, frame and secret-wall fitting.
	/// </summary>
	public static FloorProfile ProfileWithDoorsAndSecrets(float doorChance = .75f, float secretChance = .3f)
	{
		var profile = ProfileWithSecrets(secretChance);
		profile.DoorChanceOverride = doorChance;
		profile.ApplySecretOverrideToProgressionRooms = true;
		return profile;
	}

	/// <summary>Simulates collecting reachable keys to unlock matching doors; returns an error message or null when solvable.</summary>
	public static string CheckKeyLockSolvability(MapGenerator generator, IReadOnlyList<(Door Door, Vector2I Connector)> lockedDoors)
	{
		if (generator.Floor.Arrival == null || generator.Floor.Descent == null) return "missing arrival/descent";
		var entry = generator.Map.WorldToTile(generator.Floor.Arrival.GlobalPosition);
		int exitRoom = generator.RoomRegionFor(generator.Floor.Descent);
		var keys = generator.FloorContent.GetChildren().OfType<LootableItem>()
			.Where(p => !p.IsQueuedForDeletion() && p.Item is KeyItem)
			.Select(p => (Tile: generator.Map.WorldToTile(p.GlobalPosition), Path: p.Item.DefinitionPath, Qty: p.Quantity))
			.ToList();

		var blocked = lockedDoors.Select(d => d.Connector).ToHashSet();
		var collected = new HashSet<int>();
		var held = new Dictionary<string, int>();
		while (true)
		{
			var reachable = AccessSolutionValidator.Reachable(generator.Map, entry, blocked);
			bool progressed = false;
			for (int i = 0; i < keys.Count; i++)
			{
				if (collected.Contains(i) || !reachable.Contains(keys[i].Tile)) continue;
				collected.Add(i);
				held[keys[i].Path] = held.GetValueOrDefault(keys[i].Path) + keys[i].Qty;
				progressed = true;
			}
			foreach (var (door, connector) in lockedDoors)
			{
				if (!blocked.Contains(connector)) continue;
				string needed = door.Lock.Definition.RequiredKey.DefinitionPath;
				if (held.TryGetValue(needed, out int count) && count > 0)
				{
					held[needed] = count - 1;
					blocked.Remove(connector);
					progressed = true;
				}
			}
			if (!progressed) break;
		}

		if (blocked.Count > 0) return $"{blocked.Count} locked door(s) remain unsolvable";
		var finalReached = AccessSolutionValidator.Reachable(generator.Map, entry, blocked);
		if (exitRoom < 0 || !generator.Floor.Rooms[exitRoom].Tiles.Any(finalReached.Contains))
			return "exit room unreachable after unlocking keys";
		return null;
	}

	/// <summary>True when a loot pickup/trap is reachable directly, or when a chest has reachable floor beside it.</summary>
	public static bool IsLootReachable(GeneratedFloor floor, Node3D node)
	{
		if (node is Chest || (node.HasMeta("persistent_id") && node.GetMeta("persistent_id").AsString().Contains(":chest:")))
		{
			for (int step = 0; step < 8; step++)
			{
				float angle = step * Mathf.Tau / 8;
				if (floor.IsReachable(node.GlobalPosition + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 1.75f))
				{
					return true;
				}
			}
			return false;
		}
		return floor.IsReachable(node.GlobalPosition);
	}

	/// <summary>
	/// Why a generated floor is not one connected dungeon: walkable tiles split into islands across the edges the wall
	/// pass leaves open, a doorway that opens onto no corridor, or fewer than three rooms. Empty when connected.
	/// </summary>
	public static List<string> ConnectivityFailures(MapGenerator generator)
	{
		var failures = new List<string>();
		MapData map = generator.Map;
		var walkable = new List<Vector2I>();
		for (int x = 0; x < map.Width; x++)
		for (int z = 0; z < map.Height; z++)
			if (map.IsWalkable(x, z)) walkable.Add(new Vector2I(x, z));
		if (walkable.Count == 0) return ["map has no walkable tiles at all"];

		var reached = new HashSet<Vector2I> { walkable[0] };
		var queue = new Queue<Vector2I>(reached);
		while (queue.Count > 0)
		{
			var tile = queue.Dequeue();
			foreach (var offset in MapData.CardinalOffsets)
			{
				var next = tile + offset;
				if (map.IsWithinBounds(next.X, next.Y) && map.IsWalkable(next.X, next.Y)
					&& !map.RequiresInteriorWall(tile.X, tile.Y, offset) && reached.Add(next))
					queue.Enqueue(next);
			}
		}
		if (reached.Count != walkable.Count) failures.Add($"only {reached.Count} of {walkable.Count} walkable tiles are connected");

		foreach (var tile in walkable.Where(tile => map.IsConnector(tile.X, tile.Y)))
		{
			bool connected = map.GetConnectorDirections(tile.X, tile.Y).Any(direction =>
			{
				var outside = tile + direction;
				return map.IsWithinBounds(outside.X, outside.Y) && map.IsCorridor(outside.X, outside.Y);
			});
			if (!connected) failures.Add($"doorway at {tile} is not connected to any corridor");
		}

		int rooms = walkable.Select(tile => generator.Floor.RoomIdAt(tile)).Where(id => id >= 0).Distinct().Count();
		if (rooms < 3) failures.Add($"only {rooms} rooms were placed");
		return failures;
	}
}
