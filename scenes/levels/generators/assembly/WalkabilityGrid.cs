using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Where an actor's center can stand, on a sub-tile grid in world XZ. Obstacles are grown by the actor radius less
/// half a cell and rasterized by cell center, so a standable cell is one holding some point where an actor touches
/// nothing solid. Without that allowance a 2.4-unit gate would pass or fail for a 2-unit actor depending on where the
/// grid happens to fall.
/// Shared by room assembly (every doorway must stay reachable) and the generated floor (spawns and loot only use
/// reachable cells). Plain C#: one instance belongs to one assembly attempt or one floor.
/// </summary>
public sealed class WalkabilityGrid
{
	/// <summary>Eight cells per logical tile, matching the exploration sample grid.</summary>
	public const float DefaultCellSize = Room.TileSize / 8f;
	/// <summary>Solid geometry only blocks an actor inside this height band: rugs and wall mounts do not.</summary>
	public const float MinimumBlockingHeight = .15f;
	public const float MaximumBlockingHeight = 1.8f;
	private const float Epsilon = .001f;

	private readonly Vector2 _origin;
	private readonly float _erosion;
	private readonly bool[] _floor;
	private byte[] _blocked;

	public WalkabilityGrid(Rect2 bounds, float actorRadius, float cellSize = DefaultCellSize)
	{
		if (cellSize <= 0 || actorRadius < 0) throw new ArgumentOutOfRangeException(nameof(cellSize));
		CellSize = cellSize;
		ActorRadius = actorRadius;
		_erosion = Mathf.Max(0, actorRadius - cellSize / 2);
		_origin = bounds.Position;
		Size = new(Mathf.Max(1, Mathf.CeilToInt(bounds.Size.X / cellSize)), Mathf.Max(1, Mathf.CeilToInt(bounds.Size.Y / cellSize)));
		_floor = new bool[Size.X * Size.Y];
		_blocked = new byte[Size.X * Size.Y];
	}

	public float CellSize { get; }
	public float ActorRadius { get; }
	public Vector2I Size { get; }

	public Vector2I CellAt(Vector2 point) => new(Mathf.FloorToInt((point.X - _origin.X) / CellSize), Mathf.FloorToInt((point.Y - _origin.Y) / CellSize));
	public Vector2 CellCenter(Vector2I cell) => _origin + new Vector2((cell.X + .5f) * CellSize, (cell.Y + .5f) * CellSize);
	public bool Contains(Vector2I cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < Size.X && cell.Y < Size.Y;

	/// <summary>Cells whose centers lie inside <paramref name="area"/> become walkable floor.</summary>
	public void AddFloor(Rect2 area)
	{
		foreach (int index in CellsWithCenterIn(area)) _floor[index] = true;
	}

	/// <summary>Blocks every cell where an actor centered there would overlap <paramref name="footprint"/>.</summary>
	public void AddObstacle(Rect2 footprint) => AddObstacle(
		[footprint.Position, new(footprint.End.X, footprint.Position.Y), footprint.End, new(footprint.Position.X, footprint.End.Y)]);

	/// <summary>Projects solid geometry onto the floor when it lies inside the actor height band.</summary>
	public void AddObstacle(Aabb solid) => AddObstacle(CollisionOutlines.FromBox(solid));

	public void AddObstacle(SolidOutline solid)
	{
		if (solid.Bottom >= MaximumBlockingHeight || solid.Top <= MinimumBlockingHeight) return;
		AddObstacle(solid.Polygon);
	}

	/// <summary>Blocks cells whose centers lie within the erosion radius of a convex outline (its Minkowski sum with the actor).</summary>
	public void AddObstacle(Vector2[] polygon)
	{
		if (polygon.Length == 0) return;
		var bounds = new Rect2(polygon[0], Vector2.Zero);
		foreach (var point in polygon) bounds = bounds.Expand(point);
		foreach (int index in CellsWithCenterIn(bounds.Grow(_erosion)))
		{
			var center = CellCenter(new Vector2I(index % Size.X, index / Size.X));
			if (DistanceTo(polygon, center) < _erosion - Epsilon) _blocked[index] = 1;
		}
	}

	/// <summary>Zero inside a convex polygon (either winding), else the distance to its nearest edge.</summary>
	private static float DistanceTo(Vector2[] polygon, Vector2 point)
	{
		if (polygon.Length == 1) return point.DistanceTo(polygon[0]);
		bool inside = true;
		float sign = 0, best = float.MaxValue;
		for (int index = 0; index < polygon.Length; index++)
		{
			var a = polygon[index];
			var b = polygon[(index + 1) % polygon.Length];
			if (a == b) continue;
			float cross = (b - a).Cross(point - a);
			if (cross != 0)
			{
				if (sign == 0) sign = Mathf.Sign(cross);
				else if (Mathf.Sign(cross) != sign) inside = false;
			}
			best = Mathf.Min(best, point.DistanceTo(Geometry2D.GetClosestPointToSegment(point, a, b)));
		}
		return inside && polygon.Length >= 3 ? 0 : best;
	}

	public bool IsStandable(Vector2I cell)
	{
		if (!Contains(cell)) return false;
		int index = cell.Y * Size.X + cell.X;
		return _floor[index] && _blocked[index] == 0;
	}

	public bool IsStandable(Vector2 point) => IsStandable(CellAt(point));

	/// <summary>Whether every point lies in one standable component; a point off standable space fails.</summary>
	public bool AreConnected(IReadOnlyList<Vector2> points)
	{
		if (points.Count == 0) return true;
		var reachable = Reachable([points[0]]);
		foreach (var point in points)
		{
			var cell = CellAt(point);
			if (!Contains(cell) || !reachable[cell.Y * Size.X + cell.X]) return false;
		}
		return true;
	}

	/// <summary>Flood-fills standable cells from each source. A source off standable space snaps to the nearest standable
	/// cell within <paramref name="snapRadius"/> world units, or is ignored.</summary>
	public bool[] Reachable(IEnumerable<Vector2> sources, float snapRadius = 0)
	{
		var reached = new bool[_floor.Length];
		var queue = new Queue<int>();
		foreach (var source in sources)
		{
			if (!TryFindStandable(source, snapRadius, out var start)) continue;
			int index = start.Y * Size.X + start.X;
			if (reached[index]) continue;
			reached[index] = true;
			queue.Enqueue(index);
		}
		while (queue.TryDequeue(out int index))
		{
			int x = index % Size.X, z = index / Size.X;
			if (x > 0) Visit(index - 1);
			if (x + 1 < Size.X) Visit(index + 1);
			if (z > 0) Visit(index - Size.X);
			if (z + 1 < Size.Y) Visit(index + Size.X);
		}
		return reached;

		void Visit(int next)
		{
			if (reached[next] || !_floor[next] || _blocked[next] != 0) return;
			reached[next] = true;
			queue.Enqueue(next);
		}
	}

	public bool TryFindStandable(Vector2 point, float searchRadius, out Vector2I cell)
	{
		cell = CellAt(point);
		if (IsStandable(cell)) return true;
		int reach = Mathf.CeilToInt(searchRadius / CellSize);
		float best = float.MaxValue;
		var origin = cell;
		bool found = false;
		for (int x = origin.X - reach; x <= origin.X + reach; x++)
		for (int z = origin.Y - reach; z <= origin.Y + reach; z++)
		{
			var candidate = new Vector2I(x, z);
			if (!IsStandable(candidate)) continue;
			float distance = CellCenter(candidate).DistanceSquaredTo(point);
			if (distance > searchRadius * searchRadius || distance >= best) continue;
			best = distance;
			cell = candidate;
			found = true;
		}
		return found;
	}

	/// <summary>Blocked-state snapshot for transactional placement; floor never changes after setup.</summary>
	public byte[] Save() => (byte[])_blocked.Clone();
	public void Restore(byte[] state) => _blocked = (byte[])state.Clone();

	private IEnumerable<int> CellsWithCenterIn(Rect2 area)
	{
		// Strict containment: an actor exactly touching an obstacle still fits.
		int left = Mathf.Max(0, Mathf.CeilToInt((area.Position.X - _origin.X) / CellSize - .5f + Epsilon));
		int right = Mathf.Min(Size.X - 1, Mathf.FloorToInt((area.End.X - _origin.X) / CellSize - .5f - Epsilon));
		int top = Mathf.Max(0, Mathf.CeilToInt((area.Position.Y - _origin.Y) / CellSize - .5f + Epsilon));
		int bottom = Mathf.Min(Size.Y - 1, Mathf.FloorToInt((area.End.Y - _origin.Y) / CellSize - .5f - Epsilon));
		for (int z = top; z <= bottom; z++)
		for (int x = left; x <= right; x++)
			yield return z * Size.X + x;
	}
}