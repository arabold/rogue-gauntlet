using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// A Dijkstra map on a <see cref="ClearanceGrid"/> for one body radius: the cost of reaching a goal from every
/// traversable cell. Walking downhill leads to the goal along the cheapest route, which keeps to the middle of
/// passages because tight cells cost more (<see cref="NavigationCosts"/>). An <see cref="Away"/> field instead leads
/// away from a threat, preferring routes with somewhere to go over dead ends. Immutable once built; many agents of
/// one size share a field.
/// </summary>
public sealed class FlowField
{
	/// <summary>Orthogonal steps first, then diagonals; a diagonal never cuts a blocked corner.</summary>
	private static readonly Vector2I[] Steps = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)];

	private readonly float[] _cost;

	private FlowField(ClearanceGrid grid, float radius, float[] cost, bool isComplete, Vector2I goal)
	{
		Grid = grid;
		Radius = radius;
		_cost = cost;
		IsComplete = isComplete;
		Goal = goal;
	}

	public ClearanceGrid Grid { get; }
	public float Radius { get; }
	/// <summary>True when the search settled every cell it could reach; otherwise it stopped at its cost bound.</summary>
	public bool IsComplete { get; }
	/// <summary>The goal cell the field leads to; for an away field, the threat's cell.</summary>
	public Vector2I Goal { get; }

	/// <summary>Cost to the goal (for an away field: lower is safer); +inf where the search did not reach.</summary>
	public float CostAt(Vector2I cell) => Grid.Contains(cell) ? _cost[Grid.IndexOf(cell)] : float.PositiveInfinity;
	public float CostAt(Vector3 point) => CostAt(Grid.CellAt(point));
	public bool Reaches(Vector2I cell) => float.IsFinite(CostAt(cell));

	/// <summary>Whether the field reaches the cell or one next to it: a body pressed into a wall stands just outside.</summary>
	public bool ReachesNear(Vector2I cell)
	{
		if (Reaches(cell)) return true;
		foreach (var step in Steps)
			if (Reaches(cell + step)) return true;
		return false;
	}

	/// <summary>
	/// The neighbor of <paramref name="from"/> with the lowest cost, when it is lower than <paramref name="from"/>'s own.
	/// A cell the field does not cover (a body pressed into a wall) steps to any covered neighbor. False at the goal or
	/// at a local minimum such as a cornered flee.
	/// </summary>
	public bool TryDescend(Vector2I from, out Vector2I next)
	{
		next = from;
		float best = CostAt(from);
		for (int index = 0; index < Steps.Length; index++)
		{
			var step = Steps[index];
			var candidate = from + step;
			float cost = CostAt(candidate);
			if (!(cost < best)) continue;
			if (step.X != 0 && step.Y != 0 && !(Reaches(from + new Vector2I(step.X, 0)) && Reaches(from + new Vector2I(0, step.Y)))) continue;
			best = cost;
			next = candidate;
		}
		return next != from;
	}

	/// <summary>
	/// The field leading to <paramref name="goal"/> (snapped to the nearest traversable cell within
	/// <paramref name="snapRadius"/>), searched until costs exceed <paramref name="bound"/>. With no traversable goal
	/// nearby every cell is unreached.
	/// </summary>
	public static FlowField Toward(ClearanceGrid grid, Vector2 goal, float radius, bool[] openGates, NavigationCosts costs,
		float bound, float snapRadius)
	{
		var cost = new float[grid.Size.X * grid.Size.Y];
		Array.Fill(cost, float.PositiveInfinity);
		if (!grid.TryFindTraversable(goal, radius, snapRadius, openGates, out var start))
			return new FlowField(grid, radius, cost, true, grid.CellAt(goal));
		var queue = new PriorityQueue<int, float>();
		int origin = grid.IndexOf(start);
		cost[origin] = 0;
		queue.Enqueue(origin, 0);
		bool complete = Relax(grid, radius, openGates, costs, cost, queue, bound, null);
		return new FlowField(grid, radius, cost, complete, start);
	}

	/// <summary>
	/// The field leading away from <paramref name="chase"/>'s goal: every cell the chase field reached starts at
	/// -<paramref name="coefficient"/> × its chase cost and is relaxed again, so a cell's value also reflects how far
	/// it is from somewhere farther still. Downhill is away from the threat and out of dead ends rather than into them.
	/// Covers only the cells <paramref name="chase"/> reached.
	/// </summary>
	public static FlowField Away(FlowField chase, float coefficient, NavigationCosts costs, bool[] openGates)
	{
		var grid = chase.Grid;
		var cost = new float[grid.Size.X * grid.Size.Y];
		Array.Fill(cost, float.PositiveInfinity);
		var queue = new PriorityQueue<int, float>();
		var domain = new bool[cost.Length];
		for (int index = 0; index < cost.Length; index++)
		{
			float value = chase._cost[index];
			if (!float.IsFinite(value)) continue;
			domain[index] = true;
			cost[index] = -coefficient * value;
			queue.Enqueue(index, cost[index]);
		}
		Relax(grid, chase.Radius, openGates, costs, cost, queue, float.PositiveInfinity, domain);
		return new FlowField(grid, chase.Radius, cost, chase.IsComplete, chase.Goal);
	}

	/// <summary>Dijkstra relaxation over traversable cells (within <paramref name="domain"/> when given). True when exhausted.</summary>
	private static bool Relax(ClearanceGrid grid, float radius, bool[] openGates, NavigationCosts costs, float[] cost,
		PriorityQueue<int, float> queue, float bound, bool[] domain)
	{
		float diagonal = grid.CellSize * Mathf.Sqrt2;
		while (queue.TryDequeue(out int index, out float settled))
		{
			if (settled > cost[index]) continue;
			if (settled > bound) return false;
			var cell = grid.CellOf(index);
			for (int step = 0; step < Steps.Length; step++)
			{
				var offset = Steps[step];
				var next = cell + offset;
				if (!grid.Contains(next)) continue;
				int nextIndex = grid.IndexOf(next);
				if (domain != null && !domain[nextIndex]) continue;
				if (!grid.IsTraversable(next, radius, openGates)) continue;
				bool isDiagonal = offset.X != 0 && offset.Y != 0;
				if (isDiagonal && !(grid.IsTraversable(cell + new Vector2I(offset.X, 0), radius, openGates)
					&& grid.IsTraversable(cell + new Vector2I(0, offset.Y), radius, openGates))) continue;
				float candidate = settled + (isDiagonal ? diagonal : grid.CellSize) * costs.Multiplier(grid.ClearanceAt(next), radius);
				if (candidate >= cost[nextIndex]) continue;
				cost[nextIndex] = candidate;
				queue.Enqueue(nextIndex, candidate);
			}
		}
		return true;
	}
}