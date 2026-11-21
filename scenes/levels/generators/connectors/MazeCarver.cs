using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Pure-C# Growing-Tree maze carving, edge sealing and 2x2-free loop opening for <see cref="MazeCorridorConnector"/>.
/// </summary>
internal static class MazeCarver
{
	public static void SealCorridorMask(MapData map, bool[,] mask, IReadOnlyList<Vector2I> tiles)
	{
		foreach (var tile in tiles)
		{
			foreach (var dir in MapData.CardinalOffsets)
			{
				int nx = tile.X + dir.X;
				int nz = tile.Y + dir.Y;
				if (!map.IsWithinBounds(nx, nz)) continue;
				if (mask[nx, nz] || map.IsCorridor(nx, nz))
				{
					map.SealCorridorEdge(tile, dir);
				}
			}
		}
	}

	public static void CarveGrowingTree(
		MapData map,
		bool[,] mask,
		bool[,] visited,
		Vector2I start,
		float windingChance,
		Func<int, int> nextInt,
		Func<float> nextFloat)
	{
		var active = new List<Vector2I> { start };
		visited[start.X, start.Y] = true;
		var unvisitedNeighbors = new List<Vector2I>(4);

		while (active.Count > 0)
		{
			int activeIndex = active.Count == 1 || nextFloat() < windingChance
				? active.Count - 1
				: nextInt(active.Count);
			var current = active[activeIndex];

			unvisitedNeighbors.Clear();
			foreach (var dir in MapData.CardinalOffsets)
			{
				int nx = current.X + dir.X;
				int nz = current.Y + dir.Y;
				if (nx < 1 || nx >= map.Width - 1 || nz < 1 || nz >= map.Height - 1) continue;
				if (mask[nx, nz] && !visited[nx, nz])
				{
					unvisitedNeighbors.Add(dir);
				}
			}

			if (unvisitedNeighbors.Count == 0)
			{
				active.RemoveAt(activeIndex);
				continue;
			}

			var stepDir = unvisitedNeighbors[nextInt(unvisitedNeighbors.Count)];
			var next = current + stepDir;
			map.ClearCorridorEdge(current, stepDir);
			visited[next.X, next.Y] = true;
			active.Add(next);
		}
	}

	public static void OpenControlledLoops(
		MapData map,
		bool[,] mask,
		IReadOnlyList<Vector2I> tiles,
		float loopChance,
		Func<int, int> nextInt,
		Func<float> nextFloat)
	{
		if (loopChance <= 0f) return;

		var edges = new List<(Vector2I From, Vector2I Dir)>();
		foreach (var a in tiles)
		{
			if (!mask[a.X, a.Y]) continue;
			foreach (var dir in MapData.CardinalOffsets)
			{
				int nx = a.X + dir.X;
				int nz = a.Y + dir.Y;
				if (!map.IsWithinBounds(nx, nz)) continue;
				if (!map.IsCorridorEdgeSealed(a.X, a.Y, dir)) continue;

				// Canonical orientation so each internal edge is tested once.
				if (mask[nx, nz] && (dir == Vector2I.Right || dir == Vector2I.Down))
				{
					edges.Add((a, dir));
				}
			}
		}

		for (int i = edges.Count - 1; i > 0; i--)
		{
			int j = nextInt(i + 1);
			(edges[i], edges[j]) = (edges[j], edges[i]);
		}

		foreach (var (from, dir) in edges)
		{
			if (nextFloat() >= loopChance) continue;
			var to = from + dir;
			if (!WouldCreate2x2Open(map, from, to))
			{
				map.ClearCorridorEdge(from, dir);
			}
		}
	}

	private static bool WouldCreate2x2Open(MapData map, Vector2I a, Vector2I b)
	{
		var delta = b - a;
		if (delta.X != 0)
		{
			int leftX = Math.Min(a.X, b.X);
			return IsQuadOpenWithEdge(map, leftX, a.Y - 1, a, b)
				|| IsQuadOpenWithEdge(map, leftX, a.Y, a, b);
		}
		int topZ = Math.Min(a.Y, b.Y);
		return IsQuadOpenWithEdge(map, a.X - 1, topZ, a, b)
			|| IsQuadOpenWithEdge(map, a.X, topZ, a, b);
	}

	private static bool IsQuadOpenWithEdge(MapData map, int x0, int z0, Vector2I pendingA, Vector2I pendingB)
	{
		if (!map.IsWithinBounds(x0, z0) || !map.IsWithinBounds(x0 + 1, z0 + 1)) return false;
		if (!map.IsCorridor(x0, z0) || !map.IsCorridor(x0 + 1, z0)
			|| !map.IsCorridor(x0, z0 + 1) || !map.IsCorridor(x0 + 1, z0 + 1))
		{
			return false;
		}

		bool EdgeOpen(Vector2I p, Vector2I dir)
		{
			var q = p + dir;
			if ((p == pendingA && q == pendingB) || (p == pendingB && q == pendingA)) return true;
			return !map.IsCorridorEdgeSealed(p.X, p.Y, dir);
		}

		return EdgeOpen(new Vector2I(x0, z0), Vector2I.Right)
			&& EdgeOpen(new Vector2I(x0, z0 + 1), Vector2I.Right)
			&& EdgeOpen(new Vector2I(x0, z0), Vector2I.Down)
			&& EdgeOpen(new Vector2I(x0 + 1, z0), Vector2I.Down);
	}
}