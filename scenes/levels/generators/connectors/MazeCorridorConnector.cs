using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Carves 1-tile-wide winding corridor networks across all non-room playable space using sealed partition
/// edges (<see cref="MapData.SealCorridorEdge"/>), linking room doorways and optionally pruning dead ends.
/// </summary>
[Tool]
[GlobalClass]
public partial class MazeCorridorConnector : CorridorConnectorStrategy
{
	[Export(PropertyHint.Range, "0,1,0.05")] public float WindingChance { get; set; } = 0.7f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float LoopChance { get; set; } = 0.1f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float DeadEndPruneChance { get; set; } = 0f;
	[Export(PropertyHint.Range, "0,40")] public int MinDeadEnds { get; set; } = 6;

	public override void ConnectRooms(MapData map, FloorPlan plan, IReadOnlyList<RoomRegion> rooms, RandomNumberGenerator rng)
	{
		if (plan?.ApprovedEdges.Count > 0)
		{
			throw new InvalidOperationException("MazeCorridorConnector does not support FloorPlan.ApprovedEdges.");
		}

		CarveAllPlayable(
			map,
			plan?.ProtectedCorridors,
			WindingChance,
			LoopChance,
			DeadEndPruneChance,
			MinDeadEnds,
			count => count <= 1 ? 0 : rng.RandiRange(0, count - 1),
			() => rng.Randf());
	}

	/// <summary>
	/// Fills all playable interior <see cref="MapTile.Empty"/> tiles with a 1-tile Growing-Tree maze, links external
	/// doorways, opens non-2x2 loops at <paramref name="loopChance"/>, and optionally prunes non-doorway dead ends.
	/// </summary>
	internal static void CarveAllPlayable(
		MapData map,
		IReadOnlySet<Vector2I> protectedCorridors,
		float windingChance,
		float loopChance,
		float deadEndPruneChance,
		int minDeadEnds,
		Func<int, int> nextInt,
		Func<float> nextFloat)
	{
		var isMaze = new bool[map.Width, map.Height];
		var seeds = new List<Vector2I>();
		var doorwayQueue = new Queue<Vector2I>();
		for (int x = 0; x < map.Width; x++)
		{
			for (int z = 0; z < map.Height; z++)
			{
				if (!map.IsConnector(x, z)) continue;
				foreach (var dir in map.GetConnectorDirections(x, z))
				{
					int ox = x + dir.X;
					int oz = z + dir.Y;
					if (ox < 1 || ox >= map.Width - 1 || oz < 1 || oz >= map.Height - 1) continue;
					var outside = new Vector2I(ox, oz);
					if (isMaze[ox, oz] || !map.IsEmpty(ox, oz) || (protectedCorridors != null && protectedCorridors.Contains(outside)))
					{
						continue;
					}
					isMaze[ox, oz] = true;
					map.SetTile(ox, oz, MapTile.Corridor);
					seeds.Add(outside);
					doorwayQueue.Enqueue(outside);
				}
			}
		}

		if (doorwayQueue.Count > 0)
		{
			while (doorwayQueue.Count > 0)
			{
				var p = doorwayQueue.Dequeue();
				foreach (var dir in MapData.CardinalOffsets)
				{
					int nx = p.X + dir.X;
					int nz = p.Y + dir.Y;
					if (nx < 1 || nx >= map.Width - 1 || nz < 1 || nz >= map.Height - 1) continue;
					var next = new Vector2I(nx, nz);
					if (isMaze[nx, nz] || !map.IsEmpty(nx, nz) || (protectedCorridors != null && protectedCorridors.Contains(next)))
					{
						continue;
					}
					isMaze[nx, nz] = true;
					map.SetTile(nx, nz, MapTile.Corridor);
					seeds.Add(next);
					doorwayQueue.Enqueue(next);
				}
			}
		}
		else
		{
			for (int x = 1; x < map.Width - 1; x++)
			{
				for (int z = 1; z < map.Height - 1; z++)
				{
					var tile = new Vector2I(x, z);
					if (!map.IsEmpty(x, z) || (protectedCorridors != null && protectedCorridors.Contains(tile)))
					{
						continue;
					}
					isMaze[x, z] = true;
					map.SetTile(x, z, MapTile.Corridor);
					seeds.Add(tile);
				}
			}
		}
		if (seeds.Count == 0) return;

		MazeCarver.SealCorridorMask(map, isMaze, seeds);

		var visited = new bool[map.Width, map.Height];
		foreach (var seed in seeds)
		{
			if (visited[seed.X, seed.Y]) continue;
			MazeCarver.CarveGrowingTree(map, isMaze, visited, seed, windingChance, nextInt, nextFloat);
		}

		MazeCarver.OpenControlledLoops(map, isMaze, seeds, loopChance, nextInt, nextFloat, allowOtherCorridorEntry: false);

		if (deadEndPruneChance > 0f)
		{
			PruneDeadEnds(map, isMaze, deadEndPruneChance, Math.Max(0, minDeadEnds), nextFloat);
		}
	}

	private static void PruneDeadEnds(
		MapData map,
		bool[,] isMaze,
		float pruneChance,
		int minDeadEnds,
		Func<float> nextFloat)
	{
		var protectedTiles = new HashSet<Vector2I>();
		for (int x = 0; x < map.Width; x++)
		{
			for (int z = 0; z < map.Height; z++)
			{
				if (!map.IsConnector(x, z)) continue;
				foreach (var dir in map.GetConnectorDirections(x, z))
				{
					var outside = new Vector2I(x + dir.X, z + dir.Y);
					if (map.IsWithinBounds(outside.X, outside.Y)) protectedTiles.Add(outside);
				}
			}
		}

		var queue = new Queue<Vector2I>();
		int deadEnds = 0;
		for (int x = 1; x < map.Width - 1; x++)
		{
			for (int z = 1; z < map.Height - 1; z++)
			{
				if (!isMaze[x, z]) continue;
				if (map.OpenNeighborCount(x, z) == 1)
				{
					deadEnds++;
					var tile = new Vector2I(x, z);
					if (!protectedTiles.Contains(tile)) queue.Enqueue(tile);
				}
			}
		}

		while (queue.Count > 0 && deadEnds > minDeadEnds)
		{
			var tile = queue.Dequeue();
			if (!isMaze[tile.X, tile.Y] || map.OpenNeighborCount(tile.X, tile.Y) != 1 || protectedTiles.Contains(tile))
			{
				continue;
			}
			if (nextFloat() >= pruneChance) continue;

			Vector2I openDir = Vector2I.Zero;
			bool foundNeighbor = false;
			foreach (var dir in MapData.CardinalOffsets)
			{
				int nx = tile.X + dir.X;
				int nz = tile.Y + dir.Y;
				if (map.IsWithinBounds(nx, nz) && map.IsWalkable(nx, nz) && !map.RequiresInteriorWall(tile.X, tile.Y, dir))
				{
					openDir = dir;
					foundNeighbor = true;
					break;
				}
			}
			if (!foundNeighbor) continue;

			var neighbor = tile + openDir;
			// Do not orphan a connector by turning the corridor tile outside it into Empty, or leave a junction disconnected.
			if (!isMaze[neighbor.X, neighbor.Y]) continue;

			isMaze[tile.X, tile.Y] = false;
			map.SetTile(tile.X, tile.Y, MapTile.Empty);
			deadEnds--;

			if (map.OpenNeighborCount(neighbor.X, neighbor.Y) == 1)
			{
				deadEnds++;
				if (!protectedTiles.Contains(neighbor)) queue.Enqueue(neighbor);
			}
		}
	}
}
