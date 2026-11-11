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
		generator.Realms = new[] { "garrison", "sewer", "jungle" }.Select(id => Load<Realm>($"res://scenes/levels/realms/{id}.tres")).ToArray();
	}

	/// <summary>
	/// A depth for <paramref name="seed"/> that spreads a seed sweep over the realms: half Garrison (depth 1, where seed
	/// regressions were found), a quarter each Sewer and Jungle. Layouts depend on the seed and realm, not the depth.
	/// </summary>
	public static uint RealmDepth(ulong seed) => (seed % 4) switch { 2 => 4u, 0 => 7u, _ => 1u };

	/// <summary>
	/// The production pool with most doorways holding a door and some of them concealed: the hardest stress on doorway,
	/// frame and secret-wall fitting. Room types are copied, so the shared definitions keep their own chances.
	/// </summary>
	public static AssembledRoomFactory RoomPoolWithDoorsAndSecrets(float doorChance = .75f, float secretChance = .3f)
	{
		var pool = RoomPool();
		var detection = Load<SecretDetectionProfile>("res://scenes/levels/access/proximity_detection.tres");
		RoomType[] Stressed(RoomType[] types) => types.Select(type =>
		{
			var copy = (RoomType)type.Duplicate();
			copy.DoorChance = doorChance;
			copy.SecretDoorChance = secretChance;
			copy.SecretDetection = detection;
			return copy;
		}).ToArray();
		return new AssembledRoomFactory
		{
			EntranceTypes = Stressed(pool.EntranceTypes),
			ExitTypes = Stressed(pool.ExitTypes),
			RoomTypes = Stressed(pool.RoomTypes),
		};
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
