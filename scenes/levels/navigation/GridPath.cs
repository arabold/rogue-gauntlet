using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Point-to-point routes on a <see cref="ClearanceGrid"/> for one body radius, with the same step costs as
/// <see cref="FlowField"/>: A* to the goal, then shortened to the farthest points still reachable in a straight line.
/// For individual destinations (roaming, searching, ring points); shared chase targets use fields instead.
/// </summary>
public static class GridPath
{
	/// <summary>
	/// Fills <paramref name="waypoints"/> with the route from <paramref name="from"/> to <paramref name="to"/>, excluding
	/// the start and ending at <paramref name="to"/> (or at the traversable cell nearest it, within
	/// <paramref name="snapRadius"/>). False, with <paramref name="waypoints"/> cleared, when either end has no
	/// traversable cell nearby, the goal is unreachable, or the search expands more than <paramref name="maxExpansions"/>
	/// cells.
	/// </summary>
	public static bool TryFind(ClearanceGrid grid, Vector2 from, Vector2 to, float radius, bool[] openGates,
		NavigationCosts costs, int maxExpansions, float snapRadius, List<Vector2> waypoints)
	{
		waypoints.Clear();
		if (!grid.TryFindTraversable(from, radius, snapRadius, openGates, out var start)
			|| !grid.TryFindTraversable(to, radius, snapRadius, openGates, out var goal)) return false;
		var cells = new List<Vector2I>();
		if (!Search(grid, start, goal, radius, openGates, costs, maxExpansions, cells)) return false;
		var end = grid.ClearanceAt(to) >= radius && grid.IsTraversable(grid.CellAt(to), radius, openGates) ? to : grid.BestPoint(goal);
		// String-pull: from each anchor skip ahead while the route stays in a clear straight line.
		var anchor = from;
		int index = 0;
		while (index < cells.Count)
		{
			int farthest = index;
			for (int candidate = index + 1; candidate < cells.Count; candidate++)
			{
				if (!grid.HasClearLine(anchor, grid.BestPoint(cells[candidate]), radius, openGates)) break;
				farthest = candidate;
			}
			var point = farthest == cells.Count - 1 ? end : grid.BestPoint(cells[farthest]);
			waypoints.Add(point);
			anchor = point;
			index = farthest + 1;
		}
		return true;
	}

	/// <summary>Walking length from <paramref name="from"/> through <paramref name="waypoints"/>, starting at index <paramref name="first"/>.</summary>
	public static float Length(Vector2 from, IReadOnlyList<Vector2> waypoints, int first = 0)
	{
		float length = 0;
		for (int index = first; index < waypoints.Count; index++)
		{
			length += from.DistanceTo(waypoints[index]);
			from = waypoints[index];
		}
		return length;
	}

	/// <summary>A* over traversable cells; <paramref name="cells"/> receives the route after <paramref name="start"/>.</summary>
	private static bool Search(ClearanceGrid grid, Vector2I start, Vector2I goal, float radius, bool[] openGates,
		NavigationCosts costs, int maxExpansions, List<Vector2I> cells)
	{
		if (start == goal) { cells.Add(goal); return true; }
		var cost = new Dictionary<int, float>();
		var parent = new Dictionary<int, int>();
		var queue = new PriorityQueue<int, float>();
		int origin = grid.IndexOf(start), target = grid.IndexOf(goal);
		cost[origin] = 0;
		queue.Enqueue(origin, Heuristic(grid, start, goal));
		int expansions = 0;
		while (queue.TryDequeue(out int index, out float priority))
		{
			var cell = grid.CellOf(index);
			float settled = cost[index];
			if (priority > settled + Heuristic(grid, cell, goal) + .0001f) continue;
			if (index == target)
			{
				for (int at = target; at != origin; at = parent[at]) cells.Add(grid.CellOf(at));
				cells.Reverse();
				return true;
			}
			if (++expansions > maxExpansions) return false;
			foreach (var step in ClearanceGrid.Steps)
			{
				var next = cell + step;
				if (!grid.CanMove(cell, next, radius, openGates)) continue;
				int nextIndex = grid.IndexOf(next);
				float candidate = settled + costs.StepCost(grid, cell, next, radius);
				if (cost.TryGetValue(nextIndex, out float known) && candidate >= known) continue;
				cost[nextIndex] = candidate;
				parent[nextIndex] = index;
				queue.Enqueue(nextIndex, candidate + Heuristic(grid, next, goal));
			}
		}
		return false;
	}

	/// <summary>Octile distance: admissible because every step costs at least its length.</summary>
	private static float Heuristic(ClearanceGrid grid, Vector2I from, Vector2I to)
	{
		int dx = Math.Abs(from.X - to.X), dz = Math.Abs(from.Y - to.Y);
		return grid.CellSize * (Math.Max(dx, dz) + (Mathf.Sqrt2 - 1) * Math.Min(dx, dz));
	}
}