using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// How much room there is around every point of a floor, on a sub-tile grid in world XZ: each cell stores the
/// distance from its center to the nearest solid outline, capped at <see cref="MaxClearance"/>. One grid serves every
/// body size: a cell is traversable for a radius when its clearance, plus the half-cell allowance
/// <see cref="WalkabilityGrid"/> uses, covers the radius. Doors are gates, not solids: their cells close while the door
/// is shut. Built once per floor from the same collision outlines as floor reachability, then frozen.
/// Plain C#: queries never throw for points off the grid; they report no room.
/// </summary>
public sealed class ClearanceGrid
{
	/// <summary>Eight cells per logical tile, matching <see cref="WalkabilityGrid"/> and exploration samples.</summary>
	public const float DefaultCellSize = WalkabilityGrid.DefaultCellSize;
	/// <summary>Roomier than any body radius plus its preferred margin: past this a cell counts as open space.</summary>
	public const float DefaultMaxClearance = 3f;
	private const float Epsilon = .001f;

	private readonly Vector2 _origin;
	private readonly bool[] _floor;
	private readonly float[] _clearance;
	private readonly int[] _gate;

	public ClearanceGrid(Rect2 bounds, float maxClearance = DefaultMaxClearance, float cellSize = DefaultCellSize)
	{
		if (cellSize <= 0 || maxClearance <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));
		CellSize = cellSize;
		MaxClearance = maxClearance;
		_origin = bounds.Position;
		Size = new(Mathf.Max(1, Mathf.CeilToInt(bounds.Size.X / cellSize)), Mathf.Max(1, Mathf.CeilToInt(bounds.Size.Y / cellSize)));
		_floor = new bool[Size.X * Size.Y];
		_clearance = new float[Size.X * Size.Y];
		_gate = new int[Size.X * Size.Y];
		Array.Fill(_clearance, maxClearance);
		Array.Fill(_gate, -1);
	}

	public float CellSize { get; }
	public float MaxClearance { get; }
	public Vector2I Size { get; }
	public int GateCount { get; private set; }
	public bool IsFrozen { get; private set; }

	public Vector2I CellAt(Vector2 point) => new(Mathf.FloorToInt((point.X - _origin.X) / CellSize), Mathf.FloorToInt((point.Y - _origin.Y) / CellSize));
	public Vector2I CellAt(Vector3 point) => CellAt(new Vector2(point.X, point.Z));
	public Vector2 CellCenter(Vector2I cell) => _origin + new Vector2((cell.X + .5f) * CellSize, (cell.Y + .5f) * CellSize);
	public bool Contains(Vector2I cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < Size.X && cell.Y < Size.Y;
	public int IndexOf(Vector2I cell) => cell.Y * Size.X + cell.X;
	public Vector2I CellOf(int index) => new(index % Size.X, index / Size.X);

	/// <summary>Cells whose centers lie inside <paramref name="area"/> become floor.</summary>
	public void AddFloor(Rect2 area)
	{
		RequireLive();
		foreach (int index in CellsWithCenterIn(area)) _floor[index] = true;
	}

	/// <summary>A solid area, such as a tile no actor can stand on.</summary>
	public void AddObstacle(Rect2 area) => AddObstacle(
		[area.Position, new(area.End.X, area.Position.Y), area.End, new(area.Position.X, area.End.Y)]);

	/// <summary>Solid geometry counts only inside the actor height band, as for <see cref="WalkabilityGrid"/>.</summary>
	public void AddObstacle(SolidOutline solid)
	{
		if (solid.Bottom >= WalkabilityGrid.MaximumBlockingHeight || solid.Top <= WalkabilityGrid.MinimumBlockingHeight) return;
		AddObstacle(solid.Polygon);
	}

	/// <summary>Lowers the clearance of every cell near a convex outline to its distance from that outline.</summary>
	public void AddObstacle(Vector2[] polygon)
	{
		RequireLive();
		if (polygon.Length == 0) return;
		foreach (int index in CellsWithCenterIn(Bounds(polygon).Grow(MaxClearance)))
		{
			float distance = CollisionOutlines.DistanceTo(polygon, CellCenter(CellOf(index)));
			if (distance < _clearance[index]) _clearance[index] = distance;
		}
	}

	/// <summary>
	/// Records a door's closed footprint and returns its gate index: the cells within half a cell of the footprint are
	/// traversable only while that gate is open. Gate indices count up from 0 in call order.
	/// </summary>
	public int AddGate(Vector2[] footprint)
	{
		RequireLive();
		int gate = GateCount++;
		if (footprint.Length == 0) return gate;
		foreach (int index in CellsWithCenterIn(Bounds(footprint).Grow(CellSize)))
			if (CollisionOutlines.DistanceTo(footprint, CellCenter(CellOf(index))) < CellSize / 2) _gate[index] = gate;
		return gate;
	}

	public void Freeze() => IsFrozen = true;

	/// <summary>Distance from the cell's center to the nearest solid, capped at <see cref="MaxClearance"/>; 0 off the floor.</summary>
	public float ClearanceAt(Vector2I cell)
	{
		if (!Contains(cell)) return 0;
		int index = IndexOf(cell);
		return _floor[index] ? _clearance[index] : 0;
	}

	/// <summary>The gate controlling the cell, or -1.</summary>
	public int GateAt(Vector2I cell) => Contains(cell) ? _gate[IndexOf(cell)] : -1;

	/// <summary>
	/// Whether a body of <paramref name="radius"/> can stand somewhere in the cell: floor, enough clearance with the
	/// half-cell allowance, and its gate (if any) open. A null <paramref name="openGates"/> treats every gate as open.
	/// </summary>
	public bool IsTraversable(Vector2I cell, float radius, bool[] openGates)
	{
		if (!Contains(cell)) return false;
		int index = IndexOf(cell);
		if (!_floor[index] || _clearance[index] + CellSize / 2 < radius - Epsilon) return false;
		int gate = _gate[index];
		return gate < 0 || openGates == null || (gate < openGates.Length && openGates[gate]);
	}

	/// <summary>The traversable cell nearest <paramref name="point"/> within <paramref name="searchRadius"/>.</summary>
	public bool TryFindTraversable(Vector2 point, float radius, float searchRadius, bool[] openGates, out Vector2I cell)
	{
		cell = CellAt(point);
		if (IsTraversable(cell, radius, openGates)) return true;
		int reach = Mathf.CeilToInt(searchRadius / CellSize);
		float best = float.MaxValue;
		var origin = cell;
		bool found = false;
		for (int x = origin.X - reach; x <= origin.X + reach; x++)
		for (int z = origin.Y - reach; z <= origin.Y + reach; z++)
		{
			var candidate = new Vector2I(x, z);
			float distance = CellCenter(candidate).DistanceSquaredTo(point);
			if (distance > searchRadius * searchRadius || distance >= best || !IsTraversable(candidate, radius, openGates)) continue;
			best = distance;
			cell = candidate;
			found = true;
		}
		return found;
	}

	/// <summary>Whether a body can walk the straight segment: every cell along it, sampled at half-cell steps, is traversable.</summary>
	public bool HasClearLine(Vector2 from, Vector2 to, float radius, bool[] openGates)
	{
		float length = from.DistanceTo(to);
		int steps = Mathf.Max(1, Mathf.CeilToInt(length / (CellSize / 2)));
		var previous = new Vector2I(int.MinValue, int.MinValue);
		for (int step = 0; step <= steps; step++)
		{
			var cell = CellAt(from.Lerp(to, step / (float)steps));
			if (cell == previous) continue;
			if (!IsTraversable(cell, radius, openGates)) return false;
			previous = cell;
		}
		return true;
	}

	private void RequireLive()
	{
		if (IsFrozen) throw new InvalidOperationException("A frozen clearance grid cannot change.");
	}

	private static Rect2 Bounds(Vector2[] polygon)
	{
		var bounds = new Rect2(polygon[0], Vector2.Zero);
		foreach (var point in polygon) bounds = bounds.Expand(point);
		return bounds;
	}

	private IEnumerable<int> CellsWithCenterIn(Rect2 area)
	{
		int left = Mathf.Max(0, Mathf.CeilToInt((area.Position.X - _origin.X) / CellSize - .5f + Epsilon));
		int right = Mathf.Min(Size.X - 1, Mathf.FloorToInt((area.End.X - _origin.X) / CellSize - .5f - Epsilon));
		int top = Mathf.Max(0, Mathf.CeilToInt((area.Position.Y - _origin.Y) / CellSize - .5f + Epsilon));
		int bottom = Mathf.Min(Size.Y - 1, Mathf.FloorToInt((area.End.Y - _origin.Y) / CellSize - .5f - Epsilon));
		for (int z = top; z <= bottom; z++)
		for (int x = left; x <= right; x++)
			yield return z * Size.X + x;
	}
}