namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Generation-correctness contracts for room layout strategies, swept across many seeds
/// (layout bugs are seed-dependent, so single-seed tests prove little):
/// <list type="bullet">
/// <item>Every walkable tile (Room/Connector/Corridor) is reachable from every other —
/// no disconnected rooms or orphaned corridors.</item>
/// <item>Every doorway opens onto a corridor tile — no doorway was silently walled
/// shut — and every room door stands in its doorway.</item>
/// <item>Generation is deterministic per seed — fog-of-war persistence replays reveals
/// against a regenerated map, so any nondeterminism corrupts saves.</item>
/// <item>The packed layout produces measurably denser maps than the simple layout —
/// its reason to exist.</item>
/// </list>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class LayoutConnectivityTest
{
	// Extended alongside WallIntegrityTest's sweep once rotated authored-room
	// placements became reachable (see that suite's SeedCount note).
	private const int DefaultSeedCount = 120;

	[TestCase]
	public async Task PackedLayoutDungeonsAreFullyConnected()
	{
		await AssertConnectedAcrossSeeds(new PackedRoomLayout(), DefaultSeedCount);
	}

	[TestCase]
	public async Task SimpleLayoutDungeonsAreFullyConnected()
	{
		await AssertConnectedAcrossSeeds(new SimpleRoomLayout { Retries = 3 }, 20);
	}

	[TestCase]
	public async Task PackedLayoutScalesToLargerMaps()
	{
		await AssertConnectedAcrossSeeds(new PackedRoomLayout(), 10, mapSize: 40, maxRooms: 25);
	}

	[TestCase]
	public async Task ProceduralRoomDungeonsAreFullyConnected()
	{
		await AssertConnectedAcrossSeeds(new PackedRoomLayout(), 30, factory: TestFactories.RoomPool());
	}

	[TestCase]
	public async Task ProceduralRoomsWithDoorsAreFullyConnected()
	{
		// Every doorway must end up force-connected by ConnectRemainingDoorways (checked by
		// FindConnectivityFailures below), whether it holds a door or a frame.
		await AssertConnectedAcrossSeeds(new PackedRoomLayout(), 30, factory: TestFactories.RoomPoolWithDoors());
	}

	/// <summary>
	/// Rooms set their doors into their doorways and hand each over with the doorway it fills, so after rotation and
	/// placement every registered room door still stands in the wall gap of a connected doorway, facing along it.
	/// </summary>
	[TestCase]
	public async Task RoomDoorsStandInTheirConnectedDoorways()
	{
		var (runner, mapGenerator) = await TestFactories.LoadGenerator(new PackedRoomLayout(), TestFactories.RoomPoolWithDoors());
		var failures = new List<string>();
		int doors = 0;

		for (ulong seed = 1; seed <= 40; seed++)
		{
			mapGenerator.Seed = seed;
			mapGenerator.GenerateMap(includeGameplay: false);
			MapData map = mapGenerator.Map;
			foreach (var (door, connector) in mapGenerator.Floor.Doors)
			{
				doors++;
				var directions = map.GetConnectorDirections(connector.X, connector.Y);
				if (directions.Count != 1)
				{
					failures.Add($"seed {seed}: door at connector {connector} has {directions.Count} doorway directions");
					continue;
				}

				var direction = directions[0];
				var outside = connector + direction;
				if (!map.IsWithinBounds(outside.X, outside.Y) || !map.IsCorridor(outside.X, outside.Y))
				{
					failures.Add($"seed {seed}: door at connector {connector} does not open onto a corridor");
				}

				Vector3 edge = map.TileToWorld(connector) + new Vector3(direction.X, 0, direction.Y) * (Room.TileSize / 2f);
				float turn = Mathf.AngleDifference(door.GlobalRotation.Y, Mathf.DegToRad(MapData.FacingYaw(direction)));
				if (door.GlobalPosition.DistanceTo(edge) > .01f || Mathf.Abs(turn) > .01f)
				{
					failures.Add($"seed {seed}: door at {door.GlobalPosition} does not stand in its doorway's gap at {edge} facing {direction}");
				}
			}

			await runner.SimulateFrames(1);
		}

		AssertInt(doors).IsGreater(0);
		AssertArray(failures)
			.OverrideFailureMessage($"Found {failures.Count} misplaced room door(s) across 40 seeds:\n  " + string.Join("\n  ", failures))
			.IsEmpty();
	}

	[TestCase]
	public async Task ProceduralRoomDungeonsAreDeterministicPerSeed()
	{
		var (_, mapGenerator) = await TestFactories.LoadGenerator(new PackedRoomLayout(), TestFactories.RoomPool());
		mapGenerator.Seed = 11;
		mapGenerator.GenerateMap(includeGameplay: false);
		string first = Fingerprint(mapGenerator.Map);

		mapGenerator.Seed = 11;
		mapGenerator.GenerateMap(includeGameplay: false);
		string second = Fingerprint(mapGenerator.Map);

		AssertString(second)
			.OverrideFailureMessage("Two generations with the same seed produced different maps with "
				+ "procedural rooms. All procedural decisions must draw from the explicit seeded layout RNG.")
			.IsEqual(first);
	}

	[TestCase]
	public async Task PackedLayoutIsDeterministicPerSeed()
	{
		var (_, mapGenerator) = await TestFactories.LoadGenerator(new PackedRoomLayout());
		mapGenerator.Seed = 7;
		mapGenerator.GenerateMap(includeGameplay: false);
		string first = Fingerprint(mapGenerator.Map);

		mapGenerator.Seed = 7;
		mapGenerator.GenerateMap(includeGameplay: false);
		string second = Fingerprint(mapGenerator.Map);

		AssertString(second)
			.OverrideFailureMessage("Two generations with the same seed produced different maps. "
				+ "Fog-of-war persistence depends on deterministic regeneration.")
			.IsEqual(first);
	}

	[TestCase]
	public async Task PackedLayoutIsDenserThanSimpleLayout()
	{
		const int seeds = 15;
		float packed = await MeanDensity(new PackedRoomLayout(), seeds);
		float simple = await MeanDensity(new SimpleRoomLayout { Retries = 3 }, seeds);
		GD.Print($"Mean walkable density over {seeds} seeds: packed={packed:0.000}, simple={simple:0.000}");

		AssertFloat(packed)
			.OverrideFailureMessage($"PackedRoomLayout (density {packed:0.000}) should pack rooms tighter "
				+ $"than SimpleRoomLayout (density {simple:0.000}).")
			.IsGreater(simple);
	}

	private static async Task AssertConnectedAcrossSeeds(
		RoomLayoutStrategy layout, int seedCount, uint mapSize = 0, uint maxRooms = 0,
		RoomFactory factory = null)
	{
		var (runner, mapGenerator) = await TestFactories.LoadGenerator(layout, factory);
		if (mapSize > 0)
		{
			mapGenerator.MapWidth = mapSize;
			mapGenerator.MapDepth = mapSize;
		}
		if (maxRooms > 0)
		{
			mapGenerator.MaxRooms = maxRooms;
		}

		var failures = new List<string>();
		for (ulong seed = 1; seed <= (ulong)seedCount; seed++)
		{
			mapGenerator.Seed = seed;
			mapGenerator.GenerateMap(includeGameplay: false);
			foreach (string failure in FindConnectivityFailures(mapGenerator))
			{
				failures.Add($"seed {seed}: {failure}");
			}

			// Let the deferred frees of the previous generation's room nodes run so they
			// don't pile up across the tight seed loop (see WallIntegrityTest).
			await runner.SimulateFrames(1);
		}

		AssertArray(failures)
			.OverrideFailureMessage($"Found {failures.Count} connectivity failure(s) across {seedCount} seeds:\n  "
				+ string.Join("\n  ", failures))
			.IsEmpty();
	}

	internal static List<string> FindConnectivityFailures(MapGenerator mapGenerator)
	{
		var failures = new List<string>();
		MapData map = mapGenerator.Map;

		// 1. All walkable tiles form a single connected component.
		var walkable = new List<Vector2I>();
		for (int x = 0; x < map.Width; x++)
		{
			for (int z = 0; z < map.Height; z++)
			{
				if (IsWalkable(map, x, z))
				{
					walkable.Add(new Vector2I(x, z));
				}
			}
		}

		if (walkable.Count == 0)
		{
			failures.Add("map has no walkable tiles at all");
			return failures;
		}

		int reached = FloodFillCount(map, walkable[0]);
		if (reached != walkable.Count)
		{
			failures.Add($"only {reached} of {walkable.Count} walkable tiles are connected");
		}

		// 2. Every room doorway opens onto a corridor (the corridor connector's contract:
		// doorways are built entrances and must all be connected). Secret pockets open
		// onto whatever wall they were cut behind instead.
		var secrets = mapGenerator.GeneratedSecrets.Select(secret => secret.Connector).ToHashSet();
		foreach (var tile in walkable)
		{
			if (!map.IsConnector(tile.X, tile.Y) || secrets.Contains(tile))
			{
				continue;
			}

			bool connected = false;
			foreach (var direction in map.GetConnectorDirections(tile.X, tile.Y))
			{
				var outside = tile + direction;
				if (map.IsWithinBounds(outside.X, outside.Y) && map.IsCorridor(outside.X, outside.Y))
				{
					connected = true;
					break;
				}
			}

			if (!connected)
			{
				failures.Add($"doorway at {tile} is not connected to any corridor");
			}
		}

		// 3. The map holds at least entrance + exit + one more room; fewer means
		// placement is failing and the level degenerated.
		var roomIds = new HashSet<int>();
		foreach (var tile in walkable)
		{
			int id = mapGenerator.Floor.RoomIdAt(tile);
			if (id >= 0)
			{
				roomIds.Add(id);
			}
		}

		if (roomIds.Count < 3)
		{
			failures.Add($"only {roomIds.Count} rooms were placed");
		}

		return failures;
	}

	/// <summary>
	/// Flood-fills only across edges the map generator actually leaves open: two
	/// walkable tiles are connected unless MapData.RequiresInteriorWall seals the edge
	/// between them, mirroring the real passability rule the wall pass enforces.
	/// </summary>
	private static int FloodFillCount(MapData map, Vector2I start)
	{
		var visited = new HashSet<Vector2I> { start };
		var queue = new Queue<Vector2I>();
		queue.Enqueue(start);
		Vector2I[] offsets = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

		while (queue.Count > 0)
		{
			var tile = queue.Dequeue();
			foreach (var offset in offsets)
			{
				var next = tile + offset;
				if (map.IsWithinBounds(next.X, next.Y) && IsWalkable(map, next.X, next.Y)
					&& !map.RequiresInteriorWall(tile.X, tile.Y, offset)
					&& visited.Add(next))
				{
					queue.Enqueue(next);
				}
			}
		}

		return visited.Count;
	}

	private static async Task<float> MeanDensity(RoomLayoutStrategy layout, int seedCount)
	{
		var (runner, mapGenerator) = await TestFactories.LoadGenerator(layout);
		float total = 0;
		for (ulong seed = 1; seed <= (ulong)seedCount; seed++)
		{
			mapGenerator.Seed = seed;
			mapGenerator.GenerateMap(includeGameplay: false);
			total += WalkableDensity(mapGenerator.Map);
			await runner.SimulateFrames(1);
		}

		return total / seedCount;
	}

	/// <summary>
	/// Walkable tiles divided by the area of their bounding box — how much of the space
	/// the dungeon actually spans is usable, the metric packing exists to improve.
	/// </summary>
	private static float WalkableDensity(MapData map)
	{
		int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
		int count = 0;
		for (int x = 0; x < map.Width; x++)
		{
			for (int z = 0; z < map.Height; z++)
			{
				if (!IsWalkable(map, x, z))
				{
					continue;
				}
				count++;
				minX = Mathf.Min(minX, x);
				maxX = Mathf.Max(maxX, x);
				minZ = Mathf.Min(minZ, z);
				maxZ = Mathf.Max(maxZ, z);
			}
		}

		if (count == 0)
		{
			return 0;
		}

		return count / (float)((maxX - minX + 1) * (maxZ - minZ + 1));
	}

	private static string Fingerprint(MapData map)
	{
		var builder = new StringBuilder(map.Width * map.Height + map.Height);
		for (int z = 0; z < map.Height; z++)
		{
			for (int x = 0; x < map.Width; x++)
			{
				builder.Append((char)('A' + (int)map.Tiles[x, z]));
			}
			builder.Append('\n');
		}

		return builder.ToString();
	}

	private static bool IsWalkable(MapData map, int x, int z)
	{
		return map.IsWalkable(x, z);
	}
}