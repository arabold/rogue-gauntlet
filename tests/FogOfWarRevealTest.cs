namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Generates many random dungeons and asserts that revealing a room's fog never uncovers
/// a tile the player has no legitimate line of sight to. A tile counts as legitimately
/// revealed only if it belongs to a room whose connector opened toward it, or is a
/// corridor tile reached by chaining through such sanctioned connector crossings --
/// mirroring <see cref="MapGenerator.RevealRoom"/>/<see cref="MapGenerator"/>'s own
/// FloodCorridors contract, but re-derived independently from <see cref="MapData"/>
/// rather than by calling into the method under test, so a regression in that method
/// can't also blind the check.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class FogOfWarRevealTest
{
	// Kept small: each (seed, room) pair fully regenerates the map (see below), the
	// existing generation-test suite already runs close to the shared test-run
	// timeout on its own, and (per extensive manual investigation while writing this
	// test) the specific leak this guards against needs an unusual room/corridor
	// adjacency that a handful of samples rarely hits anyway -- this is a permanent
	// safety net for a real, fixed inconsistency, not a sweep expected to find fresh
	// violations turn over turn.
	private const int SeedCount = 3;
	private const int RoomsPerSeed = 5;
	private const int TileSize = 4;

	private static readonly Vector2I[] CardinalOffsets =
	{
		new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
	};

	[TestCase]
	public async Task RevealingARoomNeverLeaksThroughAnUnsanctionedWall()
	{
		var (runner, mapGenerator) = await TestFactories.LoadGenerator(new PackedRoomLayout(), TestFactories.AllProceduralWithDoors());

		var violations = new List<string>();
		for (ulong seed = 1; seed <= SeedCount; seed++)
		{
			// One room at a time, each from a freshly regenerated (same-seed, so
			// identical layout) map: the leak this guards against is a TIMING bug --
			// FloodCorridors reveals a corridor segment through an unsanctioned wall
			// before the player has found its own legitimate entrance elsewhere. That
			// segment usually does become legitimately revealed too, once ITS OWN
			// connecting rooms are explored -- so revealing every room in one pass (as
			// an earlier version of this test did) reaches the same end state whether
			// or not the bug fires, hiding exactly the regression this exists to catch.
			// Checking after each single, isolated reveal makes the premature reveal
			// visible instead.
			for (int roomId = 0; roomId < RoomsPerSeed; roomId++)
			{
				mapGenerator.Seed = seed;
				mapGenerator.GenerateMap(includeGameplay: true); // real fog needs the occlusion pass
				mapGenerator.RevealRoom(roomId, animate: false);

				foreach (string violation in FindLeaks(mapGenerator))
				{
					violations.Add($"seed {seed} room {roomId}: {violation}");
				}

				// GenerateMap re-instantiates rooms each call; pump a frame so deferred
				// QueueFrees from the previous iteration don't pile up (see WallIntegrityTest).
				await runner.SimulateFrames(1);
			}
		}

		AssertArray(violations)
			.OverrideFailureMessage($"Found {violations.Count} fog leak(s) across {SeedCount} seeds x {RoomsPerSeed} rooms:\n  "
				+ string.Join("\n  ", violations))
			.IsEmpty();
	}

	private static List<string> FindLeaks(MapGenerator mapGenerator)
	{
		MapData map = mapGenerator.Map;
		bool IsRevealed(Vector2I tile) =>
			mapGenerator.OcclusionGridMap.GetCellItem(TileToWorld(map, tile)) < 0;

		// Rooms are never directly adjacent (RoomLayoutStrategy enforces at least a
		// 1-tile gap), so a connected blob of Room/Connector/Chasm tiles is exactly one
		// room -- flood-filling the whole map into blobs gives "which room" without
		// needing the generator's own private room-region bookkeeping.
		var blobId = new int[map.Width, map.Height];
		for (int x = 0; x < map.Width; x++)
		{
			for (int z = 0; z < map.Height; z++)
			{
				blobId[x, z] = -1;
			}
		}

		var blobs = new List<List<Vector2I>>();
		for (int x = 0; x < map.Width; x++)
		{
			for (int z = 0; z < map.Height; z++)
			{
				if (blobId[x, z] != -1 || !IsFloorTile(map, x, z))
				{
					continue;
				}

				var blob = new List<Vector2I>();
				var queue = new Queue<Vector2I>();
				queue.Enqueue(new Vector2I(x, z));
				blobId[x, z] = blobs.Count;
				while (queue.Count > 0)
				{
					var tile = queue.Dequeue();
					blob.Add(tile);
					foreach (var offset in CardinalOffsets)
					{
						var n = tile + offset;
						if (map.IsWithinBounds(n.X, n.Y) && blobId[n.X, n.Y] == -1 && IsFloorTile(map, n.X, n.Y))
						{
							blobId[n.X, n.Y] = blobs.Count;
							queue.Enqueue(n);
						}
					}
				}

				blobs.Add(blob);
			}
		}

		// Seed with whichever blob(s) are actually revealed -- RevealRoom always
		// reveals a queued room's full region unconditionally, so that part is a given,
		// not what this test is checking -- then expand legitimacy through corridors
		// ONLY via a connector's own sanctioned direction, recursively crossing into
		// further rooms the same way real play would.
		var legitBlobs = new HashSet<int>();
		var pendingBlobs = new Queue<int>();
		for (int i = 0; i < blobs.Count; i++)
		{
			if (blobs[i].Any(IsRevealed))
			{
				legitBlobs.Add(i);
				pendingBlobs.Enqueue(i);
			}
		}

		var legitTiles = new HashSet<Vector2I>();
		var visitedCorridor = new HashSet<Vector2I>();
		while (pendingBlobs.Count > 0)
		{
			var corridorFrontier = new Queue<Vector2I>();
			foreach (var tile in blobs[pendingBlobs.Dequeue()])
			{
				legitTiles.Add(tile);
				if (!map.IsConnector(tile.X, tile.Y))
				{
					continue;
				}

				foreach (var direction in map.GetConnectorDirections(tile.X, tile.Y))
				{
					var outside = tile + direction;
					if (map.IsWithinBounds(outside.X, outside.Y) && map.IsCorridor(outside.X, outside.Y)
						&& visitedCorridor.Add(outside))
					{
						corridorFrontier.Enqueue(outside);
					}
				}
			}

			while (corridorFrontier.Count > 0)
			{
				var tile = corridorFrontier.Dequeue();
				legitTiles.Add(tile);
				foreach (var offset in CardinalOffsets)
				{
					var n = tile + offset;
					if (!map.IsWithinBounds(n.X, n.Y))
					{
						continue;
					}

					if (map.IsCorridor(n.X, n.Y))
					{
						if (visitedCorridor.Add(n))
						{
							corridorFrontier.Enqueue(n);
						}
					}
					else if (map.IsConnector(n.X, n.Y) && map.GetConnectorDirections(n.X, n.Y).Contains(-offset))
					{
						int otherBlob = blobId[n.X, n.Y];
						if (otherBlob >= 0 && legitBlobs.Add(otherBlob))
						{
							pendingBlobs.Enqueue(otherBlob);
						}
					}
				}
			}
		}

		var violations = new List<string>();
		for (int x = 0; x < map.Width; x++)
		{
			for (int z = 0; z < map.Height; z++)
			{
				var tile = new Vector2I(x, z);
				if (IsRevealed(tile) && !legitTiles.Contains(tile))
				{
					violations.Add($"tile {tile} is revealed but not reachable via any sanctioned connector direction");
				}
			}
		}

		return violations;
	}

	private static bool IsFloorTile(MapData map, int x, int z) =>
		map.IsRoom(x, z) || map.IsConnector(x, z) || map.IsChasm(x, z);

	private static Vector3I TileToWorld(MapData map, Vector2I tile)
	{
		int centerX = map.Width / 2;
		int centerZ = map.Height / 2;
		return new Vector3I((tile.X - centerX) * TileSize, 0, (tile.Y - centerZ) * TileSize);
	}
}
