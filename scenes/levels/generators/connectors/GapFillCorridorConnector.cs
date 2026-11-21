using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Connects rooms via A* passages first, then fills enclosed interior pockets and narrow pinched crevices
/// with 1-tile-wide corridors while leaving the map's irregular exterior contour untouched.
/// </summary>
[Tool]
[GlobalClass]
public partial class GapFillCorridorConnector : CorridorConnectorStrategy
{
	[Export(PropertyHint.Range, "0,1,0.05")] public float WindingChance { get; set; } = 0.7f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float LoopChance { get; set; } = 0.1f;

	public override void ConnectRooms(MapData map, FloorPlan plan, IReadOnlyList<RoomRegion> rooms, RandomNumberGenerator rng)
	{
		if (plan?.ApprovedEdges.Count > 0)
		{
			throw new InvalidOperationException("GapFillCorridorConnector does not support FloorPlan.ApprovedEdges.");
		}

		AStarCorridorConnector.Connect(map, plan, rooms);
		CarveInteriorGaps(
			map,
			plan?.ProtectedCorridors,
			WindingChance,
			LoopChance,
			count => count <= 1 ? 0 : rng.RandiRange(0, count - 1),
			() => rng.Randf());
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
		var exterior = MazeCarver.FindExteriorEmpty(map);

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

			MazeCarver.SealCorridorMask(map, compMask, component);
			var primaryEntry = entries[nextInt(entries.Count)];
			map.ClearCorridorEdge(primaryEntry.GapTile, primaryEntry.Dir);

			MazeCarver.CarveGrowingTree(map, compMask, mazeVisited, primaryEntry.GapTile, windingChance, nextInt, nextFloat);
			MazeCarver.OpenControlledLoops(map, compMask, component, loopChance, nextInt, nextFloat, allowOtherCorridorEntry: true, protectedCorridors);

			foreach (var t in component) compMask[t.X, t.Y] = false;
		}
	}
}