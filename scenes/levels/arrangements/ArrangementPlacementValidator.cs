using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>Shared physical checks for exact required slots and optional furnishing candidates. State belongs to one room pass.</summary>
public sealed class ArrangementPlacementValidator
{
	private readonly MapData _map;
	private readonly Vector3 _tileOrigin;
	private readonly RoomRegion _room;
	private readonly HashSet<Vector2I> _floor;
	private readonly List<Aabb> _occupied;
	private readonly List<Aabb> _passageObstacles;
	private readonly IReadOnlyList<Aabb> _walls;
	private readonly RoomPassageValidator _passage;
	private readonly List<Aabb> _accepted = [];
	private readonly Dictionary<RoomArrangementDefinition, (Aabb Envelope, IReadOnlyList<Aabb> Collision)> _geometry = [];

	public ArrangementPlacementValidator(MapData map, RoomRegion room, System.Func<Vector2I, Vector3> tileCenter,
		List<Aabb> occupied, IReadOnlyList<Aabb> walls, List<Aabb> passageObstacles, float radius)
	{
		_map = map;
		_tileOrigin = tileCenter(Vector2I.Zero);
		_room = room;
		_floor = room.Tiles.Where(tile => map.IsWalkable(tile.X, tile.Y)).ToHashSet();
		_occupied = occupied;
		_walls = walls;
		_passageObstacles = passageObstacles;
		_passage = new(map, _floor, room.ConnectorTiles, tileCenter, passageObstacles.Concat(walls).ToArray(), radius);
	}

	public string LastFailure { get; private set; } = "";

	private bool Reject(string reason) { LastFailure = reason; return false; }

	public bool IsConnected => _passage.IsConnected(_accepted);

	/// <summary>Resolve wall alignment and test the actual geometry without reserving a candidate.</summary>
	public bool TryPlace(RoomArrangementDefinition definition, Vector2I anchor, int turn, Transform3D transform,
		out RoomArrangementPlacement placement, bool checkPassage = true)
	{
		placement = null;
		bool wallMount = definition.Placement == ArrangementPlacement.Wall;
		var occupied = definition.OccupiedTiles.Select(tile => anchor + ConnectedArrangementStrategy.Rotate(tile, turn)).ToArray();
		if (!definition.AllowsPurpose(_room.Purpose) || occupied.Any(tile => !_floor.Contains(tile))) return Reject("unsupported floor or occupied entrance");
		var outward = ConnectedArrangementStrategy.Rotate(Vector2I.Up, turn);
		bool atWall = occupied.Where(tile => !occupied.Contains(tile + outward))
			.All(tile => ConnectedArrangementStrategy.IsClosedEdge(_map, tile, outward));
		if ((definition.RequiresWall || wallMount) && !atWall) return Reject("missing backing edge");
		var (envelope, collision) = GetGeometry(definition);
		var basis = transform.Basis;
		Vector3? backingPlane = definition.WallPlaneDistance > 0
			? transform.Origin + new Vector3(outward.X, 0, outward.Y) * definition.WallPlaneDistance : null;
		if (wallMount && !WallArrangementMount.TryPlace(envelope, basis, transform.Origin, outward,
			definition.MountHeight, _walls, out transform, definition.WallInset)) return Reject("missing wall mount support");
		if (definition.AlignToWall)
		{
			var support = envelope;
			support.Size = new(support.Size.X, support.End.Y, support.Size.Z);
			support.Position = new(support.Position.X, 0, support.Position.Z);
			if (!WallArrangementMount.TryPlace(support, basis, transform.Origin, outward, 0, _walls,
				out transform, definition.WallInset, definition.WallPlaneDistance)) return Reject("missing floor furniture wall support");
		}
		var box = transform * envelope;
		if (!HasFloorSupport(box)) return Reject("geometry extends beyond room floor");
		bool overlaps = wallMount
			? _occupied.Any(other => WallArrangementMount.Overlaps(box, other))
				|| _walls.Any(wall => WallArrangementMount.OverlapsWall(box, wall, outward, definition.WallInset))
			: definition.AlignToWall
				? _occupied.Any(other => OverlapsFurniture(box, other, definition.HorizontalClearance))
					|| _walls.Any(wall => WallArrangementMount.OverlapsWall(box, wall, outward, definition.WallInset, backingPlane))
				: _occupied.Any(other => OverlapsFurniture(box, other, definition.HorizontalClearance))
					|| _walls.Any(other => ConnectedArrangementStrategy.Overlaps(box, other, definition.HorizontalClearance));
		if (overlaps) return Reject("overlapping geometry");
		if (checkPassage && !_passage.IsConnected(_accepted.Concat(collision.Select(bounds => transform * bounds)).ToArray())) return Reject("disconnected free space");
		placement = new(definition, _room.Id, anchor, turn, wallMount ? [] : occupied, transform);
		return true;
	}

	private bool HasFloorSupport(Aabb box)
	{
		int left = Mathf.FloorToInt((box.Position.X - _tileOrigin.X + Room.TileSize / 2f + .001f) / Room.TileSize);
		int right = Mathf.FloorToInt((box.End.X - _tileOrigin.X + Room.TileSize / 2f - .001f) / Room.TileSize);
		int top = Mathf.FloorToInt((box.Position.Z - _tileOrigin.Z + Room.TileSize / 2f + .001f) / Room.TileSize);
		int bottom = Mathf.FloorToInt((box.End.Z - _tileOrigin.Z + Room.TileSize / 2f - .001f) / Room.TileSize);
		for (int x = left; x <= right; x++)
		for (int z = top; z <= bottom; z++) if (!_floor.Contains(new(x, z))) return false;
		return true;
	}

	private static bool OverlapsFurniture(Aabb a, Aabb b, float gap) => a.End.Y > b.Position.Y + .001f && b.End.Y > a.Position.Y + .001f
		&& ConnectedArrangementStrategy.Overlaps(a, b, gap);

	public void Accept(RoomArrangementPlacement placement)
	{
		var (envelope, collision) = GetGeometry(placement.Definition);
		_occupied.Add(placement.Transform * envelope);
		var blocking = collision.Select(box => placement.Transform * box).ToArray();
		_accepted.AddRange(blocking);
		_passageObstacles.AddRange(blocking);
	}

	private (Aabb Envelope, IReadOnlyList<Aabb> Collision) GetGeometry(RoomArrangementDefinition definition)
	{
		if (_geometry.TryGetValue(definition, out var geometry)) return geometry;
		var instance = definition.Instantiate();
		try { geometry = (instance.PlacementBounds, instance.GetBlockingBounds()); }
		finally { instance.Free(); }
		_geometry.Add(definition, geometry);
		return geometry;
	}
}