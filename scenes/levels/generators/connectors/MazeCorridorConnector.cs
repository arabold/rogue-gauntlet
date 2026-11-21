using System;
using System.Collections.Generic;
using Godot;

public enum CorridorFillScope
{
	AllPlayable,
	InteriorGapsOnly,
}

/// <summary>
/// Carves 1-tile-wide winding corridor networks using sealed partition edges (<see cref="MapData.SealCorridorEdge"/>),
/// either across all non-room playable space (<see cref="CorridorFillScope.AllPlayable"/> for labyrinths) or inside
/// enclosed gaps after A* linking rooms (<see cref="CorridorFillScope.InteriorGapsOnly"/> for dense floors).
/// </summary>
[Tool]
[GlobalClass]
public partial class MazeCorridorConnector : CorridorConnectorStrategy
{
	private CorridorFillScope _fillScope = CorridorFillScope.AllPlayable;

	[Export]
	public CorridorFillScope FillScope
	{
		get => _fillScope;
		set
		{
			if (_fillScope == value) return;
			_fillScope = value;
			NotifyPropertyListChanged();
		}
	}

	[Export(PropertyHint.Range, "0,1,0.05")] public float WindingChance { get; set; } = 0.7f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float LoopChance { get; set; } = 0.1f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float DeadEndPruneChance { get; set; } = 0f;
	[Export(PropertyHint.Range, "0,40")] public int MinDeadEnds { get; set; } = 6;

	public override void _ValidateProperty(Godot.Collections.Dictionary property)
	{
		if (_fillScope == CorridorFillScope.InteriorGapsOnly)
		{
			StringName name = property["name"].AsStringName();
			if (name == PropertyName.DeadEndPruneChance || name == PropertyName.MinDeadEnds)
			{
				property["usage"] = (int)(property["usage"].As<PropertyUsageFlags>() & ~PropertyUsageFlags.Editor);
			}
		}
	}

	public override void ConnectRooms(MapData map, FloorPlan plan, IReadOnlyList<RoomRegion> rooms, RandomNumberGenerator rng)
	{
		if (plan?.ApprovedEdges.Count > 0)
		{
			throw new InvalidOperationException("MazeCorridorConnector does not support FloorPlan.ApprovedEdges.");
		}

		var protectedCorridors = plan?.ProtectedCorridors;
		int NextInt(int count) => count <= 1 ? 0 : rng.RandiRange(0, count - 1);
		float NextFloat() => rng.Randf();

		if (FillScope == CorridorFillScope.InteriorGapsOnly)
		{
			AStarCorridorConnector.Connect(map, plan, rooms);
			CarveInteriorGaps(map, protectedCorridors, WindingChance, LoopChance, NextInt, NextFloat);
			return;
		}

		CarveAllPlayable(map, protectedCorridors, WindingChance, LoopChance, DeadEndPruneChance, MinDeadEnds, NextInt, NextFloat);
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

		SealCorridorMask(map, isMaze, seeds);

		var visited = new bool[map.Width, map.Height];
		foreach (var seed in seeds)
		{
			if (visited[seed.X, seed.Y]) continue;
			CarveGrowingTree(map, isMaze, visited, seed, windingChance, nextInt, nextFloat);
		}

		OpenControlledLoops(map, isMaze, seeds, loopChance, nextInt, nextFloat, allowOtherCorridorEntry: false);

		if (deadEndPruneChance > 0f)
		{
			PruneDeadEnds(map, isMaze, deadEndPruneChance, Math.Max(0, minDeadEnds), nextFloat);
		}
	}

	/// <summary>Returns a mask of playable interior <see cref="MapTile.Empty"/> tiles connected to the outer boundary.</summary>
	internal static bool[,] FindExteriorEmpty(MapData map)
	{
		var exterior = new bool[map.Width, map.Height];
		var queue = new Queue<Vector2I>();
		for (int x = 1; x < map.Width - 1; x++)
		{
			for (int z = 1; z < map.Height - 1; z++)
			{
				if (x > 1 && x < map.Width - 2 && z > 1 && z < map.Height - 2) continue;
				if (!map.IsEmpty(x, z)) continue;
				exterior[x, z] = true;
				queue.Enqueue(new Vector2I(x, z));
			}
		}

		while (queue.Count > 0)
		{
			var p = queue.Dequeue();
			foreach (var dir in MapData.CardinalOffsets)
			{
				int nx = p.X + dir.X;
				int nz = p.Y + dir.Y;
				if (nx < 1 || nx >= map.Width - 1 || nz < 1 || nz >= map.Height - 1) continue;
				if (exterior[nx, nz] || !map.IsEmpty(nx, nz)) continue;
				exterior[nx, nz] = true;
				queue.Enqueue(new Vector2I(nx, nz));
			}
		}

		return exterior;
	}

	/// <summary>
	/// Leaves the map's exterior empty void untouched and fills every enclosed interior <see cref="MapTile.Empty"/> pocket
	/// (and pinched 1-tile interior crevices) with reachable 1-tile corridors linked into existing A* passages.
	/// </summary>
	internal static void CarveInteriorGaps(
		MapData map,
		IReadOnlySet<Vector2I> protectedCorridors,
		float windingChance,
		float loopChance,
		Func<int, int> nextInt,
		Func<float> nextFloat)
	{
		var exterior = FindExteriorEmpty(map);

		var gap = new bool[map.Width, map.Height];
		var gapTiles = new List<Vector2I>();
		for (int x = 1; x < map.Width - 1; x++)
		{
			for (int z = 1; z < map.Height - 1; z++)
			{
				if (map.IsEmpty(x, z) && !exterior[x, z])
				{
					gap[x, z] = true;
					gapTiles.Add(new Vector2I(x, z));
				}
			}
		}

		// Fill narrow 1-tile crevices pinched inside an irregular cluster hull (>= 3 filled sides inside 2..W-3).
		bool changed = true;
		while (changed)
		{
			changed = false;
			for (int x = 2; x < map.Width - 2; x++)
			{
				for (int z = 2; z < map.Height - 2; z++)
				{
					if (!map.IsEmpty(x, z) || gap[x, z]) continue;
					int boundedSides = 0;
					foreach (var dir in MapData.CardinalOffsets)
					{
						int nx = x + dir.X;
						int nz = z + dir.Y;
						if (!map.IsEmpty(nx, nz) || gap[nx, nz]) boundedSides++;
					}
					if (boundedSides >= 3)
					{
						gap[x, z] = true;
						gapTiles.Add(new Vector2I(x, z));
						changed = true;
					}
				}
			}
		}

		if (gapTiles.Count == 0) return;

		var compVisited = new bool[map.Width, map.Height];
		var compMask = new bool[map.Width, map.Height];
		var mazeVisited = new bool[map.Width, map.Height];
		foreach (var start in gapTiles)
		{
			if (compVisited[start.X, start.Y]) continue;
			var component = new List<Vector2I>();
			var bfs = new Queue<Vector2I>();
			compVisited[start.X, start.Y] = true;
			bfs.Enqueue(start);
			while (bfs.Count > 0)
			{
				var t = bfs.Dequeue();
				component.Add(t);
				foreach (var dir in MapData.CardinalOffsets)
				{
					int nx = t.X + dir.X;
					int nz = t.Y + dir.Y;
					if (nx < 1 || nx >= map.Width - 1 || nz < 1 || nz >= map.Height - 1) continue;
					if (compVisited[nx, nz] || !gap[nx, nz]) continue;
					compVisited[nx, nz] = true;
					bfs.Enqueue(new Vector2I(nx, nz));
				}
			}

			var entries = new List<(Vector2I GapTile, Vector2I Dir)>();
			foreach (var t in component)
			{
				foreach (var dir in MapData.CardinalOffsets)
				{
					var outside = t + dir;
					if (!map.IsWithinBounds(outside.X, outside.Y) || gap[outside.X, outside.Y]) continue;
					if (!map.IsCorridor(outside.X, outside.Y)) continue;
					if (protectedCorridors != null && protectedCorridors.Contains(outside)) continue;
					entries.Add((t, dir));
				}
			}

			if (entries.Count == 0) continue; // Completely room-enclosed hole with no touching corridor; keep empty so no orphan island forms.

			foreach (var t in component)
			{
				compMask[t.X, t.Y] = true;
				map.SetTile(t.X, t.Y, MapTile.Corridor);
			}

			SealCorridorMask(map, compMask, component);
			var primaryEntry = entries[nextInt(entries.Count)];
			map.ClearCorridorEdge(primaryEntry.GapTile, primaryEntry.Dir);

			CarveGrowingTree(map, compMask, mazeVisited, primaryEntry.GapTile, windingChance, nextInt, nextFloat);
			OpenControlledLoops(map, compMask, component, loopChance, nextInt, nextFloat, allowOtherCorridorEntry: true, protectedCorridors);

			foreach (var t in component) compMask[t.X, t.Y] = false;
		}
	}

	private static void SealCorridorMask(MapData map, bool[,] mask, IReadOnlyList<Vector2I> tiles)
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

	private static void CarveGrowingTree(
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

	private static void OpenControlledLoops(
		MapData map,
		bool[,] mask,
		IReadOnlyList<Vector2I> tiles,
		float loopChance,
		Func<int, int> nextInt,
		Func<float> nextFloat,
		bool allowOtherCorridorEntry,
		IReadOnlySet<Vector2I> protectedCorridors = null)
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

				if (mask[nx, nz])
				{
					// Canonical orientation so each internal edge is tested once.
					if (dir == Vector2I.Right || dir == Vector2I.Down)
					{
						edges.Add((a, dir));
					}
				}
				else if (allowOtherCorridorEntry && map.IsCorridor(nx, nz)
					&& (protectedCorridors == null || !protectedCorridors.Contains(new Vector2I(nx, nz))))
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
