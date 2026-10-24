using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Connectivity of conservative actor-center free space, partitioned at every rectangular obstacle edge.</summary>
public sealed class RoomPassageValidator
{
	private readonly Rect2 _domain;
	private readonly List<Rect2> _barriers = [];
	private readonly Vector2[] _openings;
	private readonly float _radius;

	public RoomPassageValidator(MapData map, HashSet<Vector2I> floor, IEnumerable<Vector2I> openings,
		Func<Vector2I, Vector3> center, IReadOnlyList<Aabb> obstacles, float radius)
	{
		_radius = radius;
		_openings = openings.Select(tile => Horizontal(center(tile))).ToArray();
		if (floor.Count == 0) return;
		int minX = floor.Min(t => t.X), maxX = floor.Max(t => t.X);
		int minZ = floor.Min(t => t.Y), maxZ = floor.Max(t => t.Y);
		var origin = Horizontal(center(new(minX, minZ))) - Vector2.One * (Room.TileSize / 2f);
		_domain = new Rect2(origin + Vector2.One * radius,
			new Vector2((maxX - minX + 1) * Room.TileSize, (maxZ - minZ + 1) * Room.TileSize) - Vector2.One * radius * 2);
		for (int x = minX; x <= maxX; x++)
			for (int z = minZ; z <= maxZ; z++)
			{
				var tile = new Vector2I(x, z);
				var point = Horizontal(center(tile));
				if (!floor.Contains(tile))
					_barriers.Add(new Rect2(point - Vector2.One * (Room.TileSize / 2f), Vector2.One * Room.TileSize).Grow(radius));
				else
					foreach (var direction in new[] { Vector2I.Right, Vector2I.Down })
						if (floor.Contains(tile + direction) && map.RequiresInteriorWall(x, z, direction))
						{
							var seam = point + new Vector2(direction.X, direction.Y) * (Room.TileSize / 2f);
							var size = direction == Vector2I.Right ? new Vector2(0, Room.TileSize) : new Vector2(Room.TileSize, 0);
							_barriers.Add(new Rect2(seam - size / 2, size).Grow(radius));
						}
			}
		_barriers.AddRange(Project(obstacles));
	}

	/// <summary>Reject all disconnected free regions, including pockets unrelated to door-to-door routes.</summary>
	public bool IsConnected(IReadOnlyList<Aabb> additional)
	{
		if (_domain.Size.X <= 0 || _domain.Size.Y <= 0) return false;
		var barriers = _barriers.Concat(Project(additional)).Select(rect => rect.Intersection(_domain))
			.Where(rect => rect.Size.X > 0 && rect.Size.Y > 0).ToArray();
		if (_openings.Any(point => !_domain.HasPoint(point) || barriers.Any(rect => ContainsClosed(rect, point)))) return false;
		float[] xs = barriers.SelectMany(rect => new[] { rect.Position.X, rect.End.X })
			.Append(_domain.Position.X).Append(_domain.End.X).Distinct().Order().ToArray();
		float[] zs = barriers.SelectMany(rect => new[] { rect.Position.Y, rect.End.Y })
			.Append(_domain.Position.Y).Append(_domain.End.Y).Distinct().Order().ToArray();
		int width = xs.Length - 1, depth = zs.Length - 1;
		var blocked = new bool[width * depth];
		foreach (var rect in barriers)
			for (int x = Array.BinarySearch(xs, rect.Position.X); x < Array.BinarySearch(xs, rect.End.X); x++)
				for (int z = Array.BinarySearch(zs, rect.Position.Y); z < Array.BinarySearch(zs, rect.End.Y); z++)
					blocked[z * width + x] = true;
		int start = Array.FindIndex(blocked, value => !value);
		if (start < 0) return false;
		int free = blocked.Count(value => !value), reached = 0;
		var queue = new Queue<int>();
		queue.Enqueue(start);
		blocked[start] = true;
		while (queue.TryDequeue(out int index))
		{
			reached++;
			int x = index % width, z = index / width;
			if (x > 0) Visit(index - 1);
			if (x + 1 < width) Visit(index + 1);
			if (z > 0) Visit(index - width);
			if (z + 1 < depth) Visit(index + width);
		}
		return reached == free;

		void Visit(int index)
		{
			if (blocked[index]) return;
			blocked[index] = true;
			queue.Enqueue(index);
		}
	}

	private IEnumerable<Rect2> Project(IEnumerable<Aabb> boxes) => boxes
		.Where(box => box.End.Y > .1f && box.Position.Y < 2.5f)
		.Select(box => new Rect2(new Vector2(box.Position.X, box.Position.Z), new Vector2(box.Size.X, box.Size.Z)).Grow(_radius));
	private static Vector2 Horizontal(Vector3 point) => new(point.X, point.Z);
	private static bool ContainsClosed(Rect2 rect, Vector2 point) => point.X >= rect.Position.X && point.X <= rect.End.X
		&& point.Y >= rect.Position.Y && point.Y <= rect.End.Y;
}