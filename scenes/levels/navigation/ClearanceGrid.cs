using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// How much room there is around every point of a floor, on a sub-tile grid in world XZ. Clearance (the distance to
/// the nearest solid outline, capped at <see cref="MaxClearance"/>) is measured on a quarter-cell lattice: nine samples
/// per cell (its center, edge midpoints and corners, shared with its neighbors), and a cell's clearance is its roomiest
/// sample. A body fits in a cell when that clearance covers its radius and moves to a neighbor only through a shared
/// sample that does, so it is only ever routed through gaps it physically fits, to quarter-cell precision and wherever
/// the grid falls. Doors are gates, not solids: their cells close while the door is shut.
/// One grid serves every body size for monster navigation (built once per floor from the same collision outlines as
/// floor reachability, then frozen); capped at a single radius it is generation's <see cref="WalkabilityGrid"/>.
/// Plain C#: queries never throw for points off the grid; they report no room.
/// </summary>
public sealed class ClearanceGrid
{
	/// <summary>Eight cells per logical tile, matching exploration samples.</summary>
	public const float DefaultCellSize = Room.TileSize / 8f;
	/// <summary>Roomier than any body radius plus its preferred margin: past this a cell counts as open space.</summary>
	public const float DefaultMaxClearance = 3f;
	/// <summary>Solid geometry only blocks a body inside this height band: rugs and wall mounts do not.</summary>
	public const float MinimumBlockingHeight = .15f;
	public const float MaximumBlockingHeight = 1.8f;
	/// <summary>The eight neighbor offsets, orthogonal first.</summary>
	public static readonly Vector2I[] Steps = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)];
	private const float Epsilon = .001f;
	// A straight line may pass this much closer to a solid than the body radius: interpolating between lattice samples
	// slightly underestimates clearance, and a body slides along a wall it grazes.
	private const float LineTolerance = .05f;

	private readonly Vector2 _origin;
	private readonly bool[] _floor;
	private readonly int[] _gate;
	// Quarter-cell lattice, (2 * Size + 1) per axis; cell (x, z) owns samples 2x..2x+2, 2z..2z+2.
	private readonly Vector2I _samples;
	private readonly float[] _sample;
	// Per cell: its roomiest sample's clearance and which of its nine samples that is.
	private readonly float[] _best;
	private readonly byte[] _bestSample;
	// The cells whose roomiest sample may have changed since the last refresh, inclusive; empty while min > max.
	private Vector2I _staleMin = new(int.MaxValue, int.MaxValue), _staleMax = new(-1, -1);

	public ClearanceGrid(Rect2 bounds, float maxClearance = DefaultMaxClearance)
	{
		if (maxClearance <= 0) throw new ArgumentOutOfRangeException(nameof(maxClearance));
		MaxClearance = maxClearance;
		_origin = bounds.Position;
		Size = new(Mathf.Max(1, Mathf.CeilToInt(bounds.Size.X / CellSize)), Mathf.Max(1, Mathf.CeilToInt(bounds.Size.Y / CellSize)));
		_floor = new bool[Size.X * Size.Y];
		_gate = new int[Size.X * Size.Y];
		_best = new float[Size.X * Size.Y];
		_bestSample = new byte[Size.X * Size.Y];
		_samples = new(Size.X * 2 + 1, Size.Y * 2 + 1);
		_sample = new float[_samples.X * _samples.Y];
		Array.Fill(_sample, maxClearance);
		Array.Fill(_gate, -1);
		Array.Fill(_bestSample, (byte)4);
	}

	public float CellSize => DefaultCellSize;
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

	/// <summary>Cells whose centers lie strictly inside <paramref name="area"/> become floor.</summary>
	public void AddFloor(Rect2 area)
	{
		RequireLive();
		var (min, max) = CellsWithCenterIn(area);
		for (int z = min.Y; z <= max.Y; z++)
		for (int x = min.X; x <= max.X; x++)
			_floor[z * Size.X + x] = true;
		MarkStale(min, max);
	}

	/// <summary>A solid area, such as a tile no body can stand on.</summary>
	public void AddObstacle(Rect2 area) => AddObstacle(CollisionOutlines.Corners(area));

	/// <summary>Solid geometry counts only inside the blocking height band.</summary>
	public void AddObstacle(SolidOutline solid)
	{
		if (solid.Bottom >= MaximumBlockingHeight || solid.Top <= MinimumBlockingHeight) return;
		AddObstacle(solid.Polygon);
	}

	/// <summary>Lowers the clearance of every sample near a convex outline to its distance from that outline.</summary>
	public void AddObstacle(Vector2[] polygon)
	{
		RequireLive();
		if (polygon.Length == 0) return;
		var bounds = CollisionOutlines.Bounds(polygon).Grow(MaxClearance);
		float spacing = CellSize / 2;
		int left = Mathf.Max(0, Mathf.CeilToInt((bounds.Position.X - _origin.X) / spacing));
		int right = Mathf.Min(_samples.X - 1, Mathf.FloorToInt((bounds.End.X - _origin.X) / spacing));
		int top = Mathf.Max(0, Mathf.CeilToInt((bounds.Position.Y - _origin.Y) / spacing));
		int bottom = Mathf.Min(_samples.Y - 1, Mathf.FloorToInt((bounds.End.Y - _origin.Y) / spacing));
		if (left > right || top > bottom) return;
		for (int z = top; z <= bottom; z++)
		for (int x = left; x <= right; x++)
		{
			int index = z * _samples.X + x;
			if (_sample[index] <= 0) continue;
			float distance = CollisionOutlines.DistanceTo(polygon, SamplePoint(x, z));
			if (distance < _sample[index]) _sample[index] = distance;
		}
		// Sample s is shared by cells (s - 1) / 2 through s / 2.
		MarkStale(new((left - 1) / 2, (top - 1) / 2), new(Mathf.Min(Size.X - 1, right / 2), Mathf.Min(Size.Y - 1, bottom / 2)));
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
		var (min, max) = CellsWithCenterIn(CollisionOutlines.Bounds(footprint).Grow(CellSize));
		for (int z = min.Y; z <= max.Y; z++)
		for (int x = min.X; x <= max.X; x++)
			if (CollisionOutlines.DistanceTo(footprint, CellCenter(new Vector2I(x, z))) < CellSize / 2) _gate[z * Size.X + x] = gate;
		return gate;
	}

	public void Freeze()
	{
		RefreshBest();
		IsFrozen = true;
	}

	/// <summary>Sample snapshot for transactional edits; floor and gates are not part of it.</summary>
	public float[] Save()
	{
		RequireLive();
		return (float[])_sample.Clone();
	}

	public void Restore(float[] state)
	{
		RequireLive();
		Array.Copy(state, _sample, _sample.Length);
		MarkStale(Vector2I.Zero, Size - Vector2I.One);
	}

	/// <summary>Clearance at the cell's roomiest sample, capped at <see cref="MaxClearance"/>; 0 off the floor.</summary>
	public float ClearanceAt(Vector2I cell)
	{
		if (!Contains(cell)) return 0;
		RefreshBest();
		return _best[IndexOf(cell)];
	}

	/// <summary>The cell's roomiest sample point: where a body standing in the cell should stand.</summary>
	public Vector2 BestPoint(Vector2I cell)
	{
		if (!Contains(cell)) return CellCenter(cell);
		RefreshBest();
		int sample = _bestSample[IndexOf(cell)];
		return SamplePoint(cell.X * 2 + sample % 3, cell.Y * 2 + sample / 3);
	}

	/// <summary>Clearance at an arbitrary point, interpolated between lattice samples; 0 off the floor.</summary>
	public float ClearanceAt(Vector2 point)
	{
		var cell = CellAt(point);
		return Contains(cell) && _floor[IndexOf(cell)] ? Interpolate(point) : 0;
	}

	/// <summary>
	/// Whether a body of <paramref name="radius"/> can stand somewhere in the cell: floor, a sample with room for the
	/// whole body, and its gate (if any) open. A null <paramref name="openGates"/> treats every gate as open.
	/// </summary>
	public bool IsTraversable(Vector2I cell, float radius, bool[] openGates)
	{
		if (!Contains(cell)) return false;
		RefreshBest();
		int index = IndexOf(cell);
		return _best[index] >= radius - Epsilon && IsGateOpen(index, openGates);
	}

	/// <summary>
	/// Whether a body can move from <paramref name="from"/> into the neighboring cell <paramref name="to"/>: the target
	/// is traversable, a diagonal does not cut a corner (both cells beside it are traversable), and the cells share a
	/// sample with room for the body (<see cref="CanStep"/>).
	/// </summary>
	public bool CanMove(Vector2I from, Vector2I to, float radius, bool[] openGates) =>
		IsTraversable(to, radius, openGates)
		&& (from.X == to.X || from.Y == to.Y
			|| (IsTraversable(new Vector2I(to.X, from.Y), radius, openGates) && IsTraversable(new Vector2I(from.X, to.Y), radius, openGates)))
		&& CanStep(from, to, radius);

	/// <summary>
	/// Whether a body of <paramref name="radius"/> can pass between two neighboring cells: a sample they share (one of
	/// the three on a common edge, or the common corner of diagonal neighbors) has room for it. Two cells can each hold
	/// the body while a thin wall between them does not let it pass. Cell traversability is checked separately.
	/// </summary>
	public bool CanStep(Vector2I from, Vector2I to, float radius)
	{
		var offset = to - from;
		if (Math.Abs(offset.X) > 1 || Math.Abs(offset.Y) > 1 || offset == Vector2I.Zero) return false;
		// Lattice coordinates of the shared samples: the edge or corner on the side of from facing to.
		int x = from.X * 2 + 1 + offset.X, z = from.Y * 2 + 1 + offset.Y;
		if (offset.X != 0 && offset.Y != 0) return SampleAt(x, z) >= radius - Epsilon;
		for (int along = -1; along <= 1; along++)
			if (SampleAt(offset.X != 0 ? x : x + along, offset.X != 0 ? z + along : z) >= radius - Epsilon) return true;
		return false;
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

	/// <summary>Whether a body can walk the straight segment: every point along it, at quarter-cell steps, has room for it.</summary>
	public bool HasClearLine(Vector2 from, Vector2 to, float radius, bool[] openGates)
	{
		float length = from.DistanceTo(to);
		int steps = Mathf.Max(1, Mathf.CeilToInt(length / (CellSize / 4)));
		for (int step = 0; step <= steps; step++)
		{
			var point = from.Lerp(to, step / (float)steps);
			var cell = CellAt(point);
			if (!Contains(cell)) return false;
			int index = IndexOf(cell);
			if (!_floor[index] || !IsGateOpen(index, openGates) || Interpolate(point) < radius - LineTolerance) return false;
		}
		return true;
	}

	private float Interpolate(Vector2 point)
	{
		float spacing = CellSize / 2;
		float fx = (point.X - _origin.X) / spacing, fz = (point.Y - _origin.Y) / spacing;
		int x = Mathf.Clamp(Mathf.FloorToInt(fx), 0, _samples.X - 2), z = Mathf.Clamp(Mathf.FloorToInt(fz), 0, _samples.Y - 2);
		float tx = Mathf.Clamp(fx - x, 0, 1), tz = Mathf.Clamp(fz - z, 0, 1);
		float near = Mathf.Lerp(_sample[z * _samples.X + x], _sample[z * _samples.X + x + 1], tx);
		float far = Mathf.Lerp(_sample[(z + 1) * _samples.X + x], _sample[(z + 1) * _samples.X + x + 1], tx);
		return Mathf.Lerp(near, far, tz);
	}

	private float SampleAt(int x, int z) => x < 0 || z < 0 || x >= _samples.X || z >= _samples.Y ? 0 : _sample[z * _samples.X + x];

	private bool IsGateOpen(int index, bool[] openGates)
	{
		int gate = _gate[index];
		return gate < 0 || openGates == null || (gate < openGates.Length && openGates[gate]);
	}

	private Vector2 SamplePoint(int x, int z) => _origin + new Vector2(x * CellSize / 2, z * CellSize / 2);

	private void MarkStale(Vector2I min, Vector2I max)
	{
		if (min.X > max.X || min.Y > max.Y) return;
		_staleMin = new(Math.Min(_staleMin.X, Math.Max(0, min.X)), Math.Min(_staleMin.Y, Math.Max(0, min.Y)));
		_staleMax = new(Math.Max(_staleMax.X, max.X), Math.Max(_staleMax.Y, max.Y));
	}

	/// <summary>Recomputes the roomiest sample of every cell changed since the last refresh.</summary>
	private void RefreshBest()
	{
		if (_staleMin.X > _staleMax.X || _staleMin.Y > _staleMax.Y) return;
		for (int z = _staleMin.Y; z <= _staleMax.Y; z++)
		for (int x = _staleMin.X; x <= _staleMax.X; x++)
		{
			int index = z * Size.X + x;
			float best = 0;
			byte bestSample = 4;
			if (_floor[index])
				for (int sample = 0; sample < 9; sample++)
				{
					float clearance = _sample[(z * 2 + sample / 3) * _samples.X + x * 2 + sample % 3];
					// Ties keep the center (sample 4), then the first roomier sample.
					if (clearance > best + Epsilon || (sample == 4 && clearance >= best - Epsilon))
					{
						best = clearance;
						bestSample = (byte)sample;
					}
				}
			_best[index] = best;
			_bestSample[index] = bestSample;
		}
		_staleMin = new(int.MaxValue, int.MaxValue);
		_staleMax = new(-1, -1);
	}

	private void RequireLive()
	{
		if (IsFrozen) throw new InvalidOperationException("A frozen clearance grid cannot change.");
	}

	/// <summary>The inclusive cell range whose centers lie strictly inside <paramref name="area"/>; empty when min exceeds max.</summary>
	private (Vector2I Min, Vector2I Max) CellsWithCenterIn(Rect2 area) => (
		new(Mathf.Max(0, Mathf.CeilToInt((area.Position.X - _origin.X) / CellSize - .5f + Epsilon)),
			Mathf.Max(0, Mathf.CeilToInt((area.Position.Y - _origin.Y) / CellSize - .5f + Epsilon))),
		new(Mathf.Min(Size.X - 1, Mathf.FloorToInt((area.End.X - _origin.X) / CellSize - .5f - Epsilon)),
			Mathf.Min(Size.Y - 1, Mathf.FloorToInt((area.End.Y - _origin.Y) / CellSize - .5f - Epsilon))));
}