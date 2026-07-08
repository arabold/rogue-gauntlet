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
/// <item>Every explicit doorway opens onto a corridor tile — no doorway was silently
/// walled shut.</item>
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
		await AssertConnectedAcrossSeeds(new PackedRoomLayout(), 30, factory: TestFactories.AllProcedural());
	}

	[TestCase]
	public async Task ProceduralRoomsWithDoorsAreFullyConnected()
	{
		// Every explicit doorway must end up force-connected by ConnectRemainingDoorways
		// (checked by FindConnectivityFailures below), the same contract authored rooms
		// with DoorwayMarkers rely on.
		await AssertConnectedAcrossSeeds(new PackedRoomLayout(), 30, factory: TestFactories.AllProceduralWithDoors());
	}

	/// <summary>
	/// A doorway marker can validate fine (become a real MapData connector) yet still
	/// end up walled shut if the corridor router never reaches it -- MapData keeps
	/// classifying the tile as a Connector regardless of whether a corridor actually
	/// got routed there, since wall placement is pure geometry and never touches tile
	/// classification. MapGenerator.FinalizeMarkers hides (Enabled = false) exactly
	/// this case so an editor-visible marker never points at a solid wall; this test
	/// guards that every marker still Enabled after generation has at least one
	/// sanctioned direction that actually leads to a corridor.
	/// </summary>
	[TestCase]
	public async Task EnabledDoorwayMarkersAlwaysHaveAnOpenDirection()
	{
		var (runner, mapGenerator) = await TestFactories.LoadGenerator(new PackedRoomLayout(), TestFactories.AllProceduralWithDoors());
		var failures = new List<string>();

		for (ulong seed = 1; seed <= 40; seed++)
		{
			mapGenerator.Seed = seed;
			mapGenerator.GenerateMap(includeGameplay: false);
			MapData map = mapGenerator.Map;

			foreach (Node roomNode in mapGenerator.NavigationRegion.GetChildren())
			{
				if (roomNode is not Room room)
				{
					continue;
				}

				foreach (Node child in room.GetChildren())
				{
					if (child is not DoorwayMarker marker || !marker.Enabled)
					{
						continue;
					}

					if (!TryFindNearestMasterConnector(map, marker.GlobalPosition, out Vector2I connectorTile))
					{
						failures.Add($"seed {seed}: enabled marker {marker.Name} in {room.Name} has no master-map "
							+ "connector within range at all");
						continue;
					}

					bool hasOpenDirection = false;
					foreach (Vector2I direction in marker.GetDirectionVectors())
					{
						if (!map.GetConnectorDirections(connectorTile.X, connectorTile.Y).Contains(direction))
						{
							continue;
						}

						Vector2I outside = connectorTile + direction;
						if (map.IsWithinBounds(outside.X, outside.Y) && !map.IsWallOrEmpty(outside.X, outside.Y))
						{
							hasOpenDirection = true;
							break;
						}
					}

					if (!hasOpenDirection)
					{
						failures.Add($"seed {seed}: enabled marker {marker.Name} in {room.Name} at tile {connectorTile} "
							+ "has every sanctioned direction sealed by a wall");
					}
				}
			}

			await runner.SimulateFrames(1);
		}

		AssertArray(failures)
			.OverrideFailureMessage($"Found {failures.Count} marker(s) left enabled with no open direction "
				+ $"across 40 seeds:\n  " + string.Join("\n  ", failures))
			.IsEmpty();
	}

	/// <summary>
	/// Finds the connector tile nearest a world position, mirroring
	/// MapGenerator.TryFindNearestConnector's own matching (nearest connector within a
	/// 2-tile radius) since that private method isn't reachable from a test.
	/// </summary>
	private static bool TryFindNearestMasterConnector(MapData map, Vector3 worldPosition, out Vector2I connector)
	{
		int centerX = map.Width / 2;
		int centerZ = map.Height / 2;
		var approxTile = new Vector2I(
			Mathf.RoundToInt(worldPosition.X / 4f) + centerX, Mathf.RoundToInt(worldPosition.Z / 4f) + centerZ);

		connector = default;
		float bestDistanceSq = float.MaxValue;
		bool found = false;
		for (int dx = -2; dx <= 2; dx++)
		{
			for (int dz = -2; dz <= 2; dz++)
			{
				var candidate = new Vector2I(approxTile.X + dx, approxTile.Y + dz);
				if (!map.IsWithinBounds(candidate.X, candidate.Y) || !map.IsConnector(candidate.X, candidate.Y))
				{
					continue;
				}

				var candidateWorld = new Vector3((candidate.X - centerX) * 4f, 0, (candidate.Y - centerZ) * 4f);
				float distSq = worldPosition.DistanceSquaredTo(candidateWorld);
				if (distSq < bestDistanceSq)
				{
					bestDistanceSq = distSq;
					connector = candidate;
					found = true;
				}
			}
		}

		return found && bestDistanceSq <= (4f * 2f) * (4f * 2f);
	}

	[TestCase]
	public async Task ProceduralRoomDungeonsAreDeterministicPerSeed()
	{
		var (_, mapGenerator) = await TestFactories.LoadGenerator(new PackedRoomLayout(), TestFactories.AllProcedural());
		mapGenerator.Seed = 11;
		mapGenerator.GenerateMap(includeGameplay: false);
		string first = Fingerprint(mapGenerator.Map);

		mapGenerator.Seed = 11;
		mapGenerator.GenerateMap(includeGameplay: false);
		string second = Fingerprint(mapGenerator.Map);

		AssertString(second)
			.OverrideFailureMessage("Two generations with the same seed produced different maps with "
				+ "procedural rooms. All procedural decisions must draw from the seeded global RNG.")
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

	private static List<string> FindConnectivityFailures(MapGenerator mapGenerator)
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

		// 2. Every explicit doorway opens onto a corridor (the corridor connector's
		// contract: doorways are intentional entrances and must all be connected).
		foreach (var tile in walkable)
		{
			if (!map.IsConnector(tile.X, tile.Y) || !map.IsDoorway(tile.X, tile.Y))
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
			int id = mapGenerator.GetRoomIdAt(tile);
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
