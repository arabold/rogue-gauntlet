using System.Collections.Generic;
using Godot;

/// <summary>
/// Where an actor's center can stand, on a sub-tile grid in world XZ: a <see cref="ClearanceGrid"/> asked about one
/// actor radius. A cell is standable when one of its nine lattice samples clears the actor, and two cells connect only
/// through a shared sample that does, so a gap is open exactly when the actor fits through it, to quarter-cell
/// precision and wherever the grid falls: a 2.4-unit gate admits a 2-unit actor, a 1.7-unit one does not. Monster
/// navigation applies the same rule, so what generation keeps reachable, the bodies it was checked for can walk.
/// Shared by room assembly (every doorway must stay reachable) and the generated floor (spawns and loot only use
/// reachable cells). Plain C#: one instance belongs to one assembly attempt or one floor.
/// </summary>
public sealed class WalkabilityGrid
{
	private readonly ClearanceGrid _clearance;

	public WalkabilityGrid(Rect2 bounds, float actorRadius)
	{
		ActorRadius = actorRadius;
		// Room past the actor radius never matters here, so samples only measure solids that close.
		_clearance = new ClearanceGrid(bounds, actorRadius);
	}

	public float CellSize => _clearance.CellSize;
	public float ActorRadius { get; }
	public Vector2I Size => _clearance.Size;

	public Vector2I CellAt(Vector2 point) => _clearance.CellAt(point);
	public Vector2 CellCenter(Vector2I cell) => _clearance.CellCenter(cell);
	public bool Contains(Vector2I cell) => _clearance.Contains(cell);

	/// <summary>Cells whose centers lie inside <paramref name="area"/> become walkable floor.</summary>
	public void AddFloor(Rect2 area) => _clearance.AddFloor(area);

	public void AddObstacle(Rect2 footprint) => _clearance.AddObstacle(footprint);
	public void AddObstacle(Aabb solid) => _clearance.AddObstacle(CollisionOutlines.FromBox(solid));
	/// <summary>Solid geometry blocks only inside the actor height band (<see cref="ClearanceGrid.MinimumBlockingHeight"/>).</summary>
	public void AddObstacle(SolidOutline solid) => _clearance.AddObstacle(solid);
	public void AddObstacle(Vector2[] polygon) => _clearance.AddObstacle(polygon);

	/// <summary>Floor where at least one of the cell's nine sample points clears the actor.</summary>
	public bool IsStandable(Vector2I cell) => _clearance.IsTraversable(cell, ActorRadius, null);
	public bool IsStandable(Vector2 point) => IsStandable(CellAt(point));

	/// <summary>Whether every point lies in one standable component; a point off standable space fails.</summary>
	public bool AreConnected(IReadOnlyList<Vector2> points)
	{
		if (points.Count == 0) return true;
		var reachable = Reachable([points[0]]);
		foreach (var point in points)
		{
			var cell = CellAt(point);
			if (!Contains(cell) || !reachable[_clearance.IndexOf(cell)]) return false;
		}
		return true;
	}

	/// <summary>Flood-fills standable cells from each source. A source off standable space snaps to the nearest standable
	/// cell within <paramref name="snapRadius"/> world units, or is ignored.</summary>
	public bool[] Reachable(IEnumerable<Vector2> sources, float snapRadius = 0)
	{
		var reached = new bool[Size.X * Size.Y];
		var queue = new Queue<Vector2I>();
		foreach (var source in sources)
		{
			if (!TryFindStandable(source, snapRadius, out var start) || reached[_clearance.IndexOf(start)]) continue;
			reached[_clearance.IndexOf(start)] = true;
			queue.Enqueue(start);
		}
		while (queue.TryDequeue(out var cell))
			for (int step = 0; step < 4; step++)
			{
				var next = cell + ClearanceGrid.Steps[step];
				if (!Contains(next) || reached[_clearance.IndexOf(next)] || !_clearance.CanMove(cell, next, ActorRadius, null)) continue;
				reached[_clearance.IndexOf(next)] = true;
				queue.Enqueue(next);
			}
		return reached;
	}

	public bool TryFindStandable(Vector2 point, float searchRadius, out Vector2I cell) =>
		_clearance.TryFindTraversable(point, ActorRadius, searchRadius, null, out cell);

	/// <summary>Snapshot for transactional placement; floor never changes after setup.</summary>
	public float[] Save() => _clearance.Save();
	public void Restore(float[] state) => _clearance.Restore(state);
}