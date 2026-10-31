using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>A straight wall run in tile coordinates, split at doorways and concave corners. Along is +X or +Z.</summary>
public readonly record struct WallRun(Vector2I Start, Vector2I Along, Vector2I Outward, int Length)
{
	public float LengthUnits => Length * Room.TileSize;
	public Vector3 Tangent => new(Along.X, 0, Along.Y);
	public Vector3 Inward => new(-Outward.X, 0, -Outward.Y);
	public Vector2I TileAt(int index) => Start + Along * index;
	public Vector2I Last => TileAt(Length - 1);
	/// <summary>The run's start on the wall line (the tile edge), at its first tile's outer corner.</summary>
	public Vector3 LineStart => RoomFootprint.TileCenter(Start) - Inward * (Room.TileSize / 2f) - Tangent * (Room.TileSize / 2f);
	/// <summary>Rotation that turns local +Z into the room, so a prop's back (-Z) faces this wall.</summary>
	public Basis Facing => new(Vector3.Up, Mathf.Atan2(Inward.X, Inward.Z));
}

/// <summary>A prop accepted into the room, in room-local space.</summary>
public readonly record struct PlacedPiece(Prop Prop, int Variant, Transform3D Transform, Aabb Envelope);

/// <summary>
/// One room-assembly attempt: footprint, doorways, walls, walkability and accepted props. Every placement goes
/// through <see cref="TryPlace"/>, which checks floor support, walls, other props and circulation, and keeps all
/// doorways mutually reachable. Pockets of free space are allowed; the floor's reachability keeps spawns out.
/// Owns the candidate <see cref="Room"/> until it is emitted.
/// </summary>
public sealed class RoomBlueprint
{
	private const float Tolerance = .02f;
	private readonly Dictionary<(Vector2I Tile, Vector2I Outward), (float Depth, bool Decoratable)> _walls = [];
	private readonly HashSet<(Vector2I Tile, Vector2I Outward)> _doorwayEdges;
	private readonly List<Aabb> _wallBoxes = [];
	private readonly List<PlacedPiece> _pieces = [];
	private readonly List<Rect2> _reserved = [];
	private readonly Dictionary<object, int> _uses = [];
	private readonly Dictionary<object, object> _memo = [];
	private WallRun[] _wallRuns;

	public RoomBlueprint(Room room, RoomType type, RoomFootprint footprint, IReadOnlyList<RoomDoorway> doorways, ulong seed, float actorRadius)
	{
		Room = room;
		Type = type;
		Footprint = footprint;
		Doorways = doorways;
		Seed = seed;
		_doorwayEdges = doorways.Select(doorway => (doorway.Tile, doorway.Direction)).ToHashSet();
		float half = Room.TileSize / 2f;
		Interior = new Rect2(-half, -half, footprint.Width * Room.TileSize, footprint.Length * Room.TileSize);
		// One spare tile around the room keeps doorway thresholds and outer walls inside the grid.
		Grid = new WalkabilityGrid(Interior.Grow(Room.TileSize), actorRadius);
	}

	public Room Room { get; }
	public RoomType Type { get; }
	/// <summary>The axis choices this attempt made, for previews and diagnostics.</summary>
	public RoomShape Shape { get; init; }
	public WallStyle Walls { get; init; }
	public FloorStyle Floor { get; init; }
	public ColumnPattern Columns { get; internal set; }
	public RoomFootprint Footprint { get; }
	public IReadOnlyList<RoomDoorway> Doorways { get; }
	public ulong Seed { get; }
	public WalkabilityGrid Grid { get; }
	/// <summary>The footprint's bounding rectangle in local XZ, on the tile edges (wall lines).</summary>
	public Rect2 Interior { get; }
	public IReadOnlyList<PlacedPiece> Pieces => _pieces;
	public IReadOnlyList<Rect2> Reserved => _reserved;
	/// <summary>Why the most recent placement was refused, for failure messages and authoring diagnostics.</summary>
	public string LastRejection { get; private set; } = "";

	public bool IsDoorway(Vector2I tile, Vector2I outward) => _doorwayEdges.Contains((tile, outward));

	/// <summary>Records the wall piece on an edge: how far its face reaches into the room, and whether decor may hang on it.</summary>
	public void SetWall(Vector2I tile, Vector2I outward, float depth, bool decoratable) => _walls[(tile, outward)] = (depth, decoratable);

	public float WallDepth(Vector2I tile, Vector2I outward) => _walls.TryGetValue((tile, outward), out var wall) ? wall.Depth : .5f;
	public bool IsDecoratable(Vector2I tile, Vector2I outward) => _walls.TryGetValue((tile, outward), out var wall) && wall.Decoratable;
	public float RunDepth(WallRun run) => Enumerable.Range(0, run.Length).Max(index => WallDepth(run.TileAt(index), run.Outward));

	/// <summary>Registers floor, void and the placed wall geometry with the walkability grid. Call once, after walls.</summary>
	public void FinishStructure()
	{
		float half = Room.TileSize / 2f;
		for (int x = -1; x <= Footprint.Width; x++)
		for (int z = -1; z <= Footprint.Length; z++)
		{
			var center = RoomFootprint.TileCenter(new(x, z));
			var area = new Rect2(center.X - half, center.Z - half, Room.TileSize, Room.TileSize);
			if (Footprint.IsFloor(x, z)) Grid.AddFloor(area);
			else Grid.AddObstacle(area);
		}
		foreach (var outline in CollisionOutlines.FromGridMap(Room.WallGridMap, Transform3D.Identity)) Grid.AddObstacle(outline);
		// Furniture may stand in a corner: the corner piece's visual arms coincide with the straight walls, and its
		// convex collision hull only fills space no actor reaches anyway.
		var library = Room.WallGridMap.MeshLibrary;
		foreach (Vector3I cell in Room.WallGridMap.GetUsedCells())
		{
			int item = Room.WallGridMap.GetCellItem(cell);
			var mesh = library.GetItemMesh(item);
			if (mesh == null || library.GetItemName(item).Contains("corner")) continue;
			_wallBoxes.Add(WallFootprints.FootprintWorld(mesh.GetAabb(), Room.WallGridMap.GetCellItemOrientation(cell), cell));
		}
	}

	/// <summary>Straight wall runs, split at doorways and wherever the room continues past the wall line.</summary>
	public IReadOnlyList<WallRun> WallRuns => _wallRuns ??= ComputeWallRuns().ToArray();

	private IEnumerable<WallRun> ComputeWallRuns()
	{
		foreach (var outward in new[] { Vector2I.Up, Vector2I.Right, Vector2I.Down, Vector2I.Left })
		{
			var along = outward.X == 0 ? Vector2I.Right : Vector2I.Down;
			var edges = Footprint.Tiles.Where(tile => !Footprint.IsFloor(tile + outward) && !IsDoorway(tile, outward)).ToHashSet();
			foreach (var first in edges.OrderBy(tile => tile.Y).ThenBy(tile => tile.X).ToArray())
			{
				if (!edges.Contains(first) || edges.Contains(first - along)) continue;
				int length = 0;
				while (edges.Remove(first + along * length)) length++;
				yield return new(first, along, outward, length);
			}
		}
	}

	/// <summary>True when the run's start (or end) meets a perpendicular wall rather than a doorway or open floor.</summary>
	public bool StartsAtCorner(WallRun run) => !Footprint.IsFloor(run.Start - run.Along);
	public bool EndsAtCorner(WallRun run) => !Footprint.IsFloor(run.Last + run.Along);

	public RandomNumberGenerator Random(string key) => GenerationRandom.Create(Seed, GenerationRandom.Arrangements, $"{Type?.Id}:{key}");

	/// <summary>
	/// Weighted choice balanced over the room: each option scores (uses + a random fraction) / weight and the lowest wins.
	/// Over many picks the counts follow the weights (a weight-0.3 option comes about once per three of a weight-1
	/// option), while early picks stay random and equal options alternate instead of repeating. Draws once per option.
	/// </summary>
	public T Pick<T>(IReadOnlyList<T> options, RandomNumberGenerator rng) where T : Placeable
	{
		if (options == null) return null;
		T best = null;
		double bestScore = double.MaxValue;
		foreach (var option in options)
		{
			double fraction = rng.Randf();
			if (option == null || option.Weight <= 0) continue;
			double score = (_uses.GetValueOrDefault(option) + fraction) / option.Weight;
			if (score >= bestScore) continue;
			bestScore = score;
			best = option;
		}
		return best;
	}

	/// <summary>Prefers variants not yet placed in this room; ties break by the seeded draw.</summary>
	public int PickVariant(Prop prop, RandomNumberGenerator rng)
	{
		double roll = rng.Randf();
		if (prop.VariantCount <= 1) return 0;
		var weights = Enumerable.Range(0, prop.VariantCount).Select(variant => 1.0 / (1 + 4 * _uses.GetValueOrDefault((prop, variant)))).ToArray();
		roll *= weights.Sum();
		for (int variant = 0; variant < weights.Length; variant++)
		{
			roll -= weights[variant];
			if (roll < 0) return variant;
		}
		return weights.Length - 1;
	}

	public void RecordUse(Placeable placeable) => _uses[placeable] = _uses.GetValueOrDefault(placeable) + 1;

	/// <summary>A value computed once per room for <paramref name="key"/>, such as a step's single content choice.</summary>
	public T Remember<T>(object key, System.Func<T> create)
	{
		if (_memo.TryGetValue(key, out var value)) return (T)value;
		var created = create();
		_memo[key] = created;
		return created;
	}

	/// <summary>Every doorway can reach every other one.</summary>
	public bool IsConnected => Grid.AreConnected(Doorways.Select(doorway => XZ(doorway.Approach)).ToArray());

	/// <summary>Doorways stay connected and inaccessible pockets stay within the room type's <see cref="RoomType.MaxPocketShare"/>.</summary>
	public bool IsAcceptable
	{
		get
		{
			var approaches = Doorways.Select(doorway => XZ(doorway.Approach)).ToArray();
			var reached = Grid.Reachable(approaches.Take(1));
			foreach (var approach in approaches)
			{
				var cell = Grid.CellAt(approach);
				if (!Grid.Contains(cell) || !reached[cell.Y * Grid.Size.X + cell.X]) return false;
			}
			if (Type == null || Type.MaxPocketShare >= 1) return true;
			int standable = 0, reachable = 0;
			for (int index = 0; index < reached.Length; index++)
			{
				if (!Grid.IsStandable(new Vector2I(index % Grid.Size.X, index / Grid.Size.X))) continue;
				standable++;
				if (reached[index]) reachable++;
			}
			return standable == 0 || 1 - reachable / (float)standable <= Type.MaxPocketShare;
		}
	}

	/// <summary>Keeps floor props out of an area (doorway approaches, circulation, interaction space); actors may use it.</summary>
	public void Reserve(Rect2 area) => _reserved.Add(area);

	/// <summary>
	/// Places a plan at <paramref name="at"/>. Required pieces go in together or not at all and must keep the room
	/// <see cref="IsAcceptable"/>; optional pieces are then tried one by one. Leaves the blueprint unchanged on false.
	/// </summary>
	public bool TryPlace(PlacementPlan plan, Transform3D at)
	{
		if (plan.IsEmpty) return false;
		var start = Save();
		foreach (var piece in plan.Pieces.Where(piece => !piece.Optional))
		{
			if (TryAdd(piece, at * piece.Local, checkConnectivity: false)) continue;
			Restore(start);
			return false;
		}
		if (!IsAcceptable)
		{
			LastRejection = $"{plan.Pieces[0].Prop.Id}: would cut off a doorway or exceed the pocket budget";
			Restore(start);
			return false;
		}
		foreach (var piece in plan.Pieces.Where(piece => piece.Optional))
		{
			var before = Save();
			if (!TryAdd(piece, at * piece.Local, checkConnectivity: true)) Restore(before);
		}
		for (int index = start.Pieces; index < _pieces.Count; index++)
		{
			var placed = _pieces[index];
			_uses[placed.Prop] = _uses.GetValueOrDefault(placed.Prop) + 1;
			_uses[(placed.Prop, placed.Variant)] = _uses.GetValueOrDefault((placed.Prop, placed.Variant)) + 1;
		}
		return true;
	}

	/// <summary>
	/// Transform that stands a plan against a wall run, starting <paramref name="along"/> units from the run's start.
	/// Floor plans put their required pieces' back on the wall face; wall-mounted plans hang on the mounting surface at
	/// the anchor's height.
	/// </summary>
	public Transform3D AgainstWall(PlacementPlan plan, WallRun run, float along)
	{
		var anchor = plan.Pieces[0].Prop;
		var facing = new Transform3D(run.Facing, Vector3.Zero);
		var all = facing * plan.Bounds();
		var required = facing * plan.Bounds(includeOptional: false);
		var (alongMin, _) = Project(all, run.Tangent);
		var (inwardMin, _) = Project(required, run.Inward);
		float face = RunDepth(run) - (anchor.Mount == PropMount.Wall ? anchor.WallInset : 0);
		var origin = run.LineStart + run.Tangent * (along - alongMin) + run.Inward * (face - inwardMin);
		if (anchor.Mount == PropMount.Wall) origin.Y = anchor.MountHeight;
		return new Transform3D(run.Facing, origin);
	}

	/// <summary>
	/// Frame on a run's wall face: origin at the run's middle, +Z into the room, X along the wall. Local X spans
	/// -LengthUnits/2..+LengthUnits/2, so layouts built in this frame work on every wall regardless of direction.
	/// </summary>
	public Transform3D RunFrame(WallRun run) =>
		new(run.Facing, run.LineStart + run.Tangent * (run.LengthUnits / 2) + run.Inward * RunDepth(run));

	/// <summary>Distance along a run (from its start) of a room-local point.</summary>
	public static float AlongRun(WallRun run, Vector3 point) => (point - run.LineStart).Dot(run.Tangent);

	/// <summary>Extent of the plan along the run, including optional pieces.</summary>
	public float WidthAlong(PlacementPlan plan, WallRun run)
	{
		var (min, max) = Project(new Transform3D(run.Facing, Vector3.Zero) * plan.Bounds(), run.Tangent);
		return max - min;
	}

	/// <summary>Wall-mounted props only hang where every edge behind them carries a plain, decoratable wall.</summary>
	public bool HasWallSupport(Aabb envelope, WallRun run)
	{
		var (min, max) = Project(envelope, run.Tangent);
		float start = run.LineStart.Dot(run.Tangent);
		int first = Mathf.FloorToInt((min - start + Tolerance) / Room.TileSize);
		int last = Mathf.FloorToInt((max - start - Tolerance) / Room.TileSize);
		if (first < 0 || last >= run.Length) return false;
		for (int index = first; index <= last; index++)
			if (!IsDecoratable(run.TileAt(index), run.Outward)) return false;
		return true;
	}

	private bool TryAdd(PlannedPiece piece, Transform3D transform, bool checkConnectivity)
	{
		var geometry = piece.Prop.Geometry(piece.Variant);
		var envelope = transform * geometry.Envelope;
		var area = XZ(envelope);
		if (piece.Prop.Mount == PropMount.Floor)
		{
			if (!HasFloorSupport(area.Grow(-.05f))) return Reject(piece, "extends past the floor");
			if (_wallBoxes.Any(wall => XZ(wall).Grow(-Tolerance).Intersects(area))) return Reject(piece, "overlaps a wall");
			if (piece.Prop.Blocking && _reserved.Any(reserved => reserved.Intersects(area))) return Reject(piece, "blocks reserved circulation");
		}
		else if (!Interior.Grow(1).Encloses(area))
		{
			return Reject(piece, "hangs outside the room");
		}
		foreach (var other in _pieces)
		{
			if (piece.Prop.Structural && other.Prop.Structural) continue;
			if (piece.Prop.Covering != other.Prop.Covering) continue;
			float gap = Mathf.Max(piece.Prop.Gap, other.Prop.Gap);
			bool vertical = envelope.Position.Y < other.Envelope.End.Y - .01f && envelope.End.Y > other.Envelope.Position.Y + .01f;
			if (vertical && XZ(other.Envelope).Grow(gap - Tolerance).Intersects(area)) return Reject(piece, $"overlaps {other.Prop.Id}");
		}
		_pieces.Add(new(piece.Prop, piece.Variant, transform, envelope));
		foreach (var solid in geometry.Solids) Grid.AddObstacle(transform * solid);
		if (checkConnectivity && geometry.Solids.Count > 0 && !IsAcceptable) return Reject(piece, "would cut off a doorway or exceed the pocket budget");
		if (piece.Prop.Interactive)
		{
			// Keep the front clear so the prop stays usable after later placements.
			var front = transform * new Aabb(new Vector3(geometry.Envelope.Position.X, 0, geometry.Envelope.End.Z),
				new Vector3(geometry.Envelope.Size.X, 1, 1.4f));
			Reserve(XZ(front));
		}
		return true;
	}

	private bool Reject(PlannedPiece piece, string reason)
	{
		LastRejection = $"{piece.Prop.Id}: {reason}";
		return false;
	}

	private bool HasFloorSupport(Rect2 area)
	{
		float half = Room.TileSize / 2f;
		int left = Mathf.FloorToInt((area.Position.X + half) / Room.TileSize);
		int right = Mathf.FloorToInt((area.End.X + half) / Room.TileSize);
		int top = Mathf.FloorToInt((area.Position.Y + half) / Room.TileSize);
		int bottom = Mathf.FloorToInt((area.End.Y + half) / Room.TileSize);
		for (int x = left; x <= right; x++)
		for (int z = top; z <= bottom; z++)
			if (!Footprint.IsFloor(x, z)) return false;
		return true;
	}

	private readonly record struct Checkpoint(byte[] Grid, int Pieces, int Reserved);

	private Checkpoint Save() => new(Grid.Save(), _pieces.Count, _reserved.Count);

	private void Restore(Checkpoint checkpoint)
	{
		Grid.Restore(checkpoint.Grid);
		_pieces.RemoveRange(checkpoint.Pieces, _pieces.Count - checkpoint.Pieces);
		_reserved.RemoveRange(checkpoint.Reserved, _reserved.Count - checkpoint.Reserved);
	}

	/// <summary>
	/// Instantiates every accepted prop under a Props node of the room; mesh-library props get static collision.
	/// Interior doors (cell gates) get stable room-local identities from their prop and order.
	/// </summary>
	public void EmitProps()
	{
		var root = new Node3D { Name = "Props" };
		Room.AddChild(root);
		for (int index = 0; index < _pieces.Count; index++)
		{
			var piece = _pieces[index];
			var node = CreateNode(piece);
			node.Name = $"{piece.Prop.Id.Split('.').Last()}_{index}";
			node.SetMeta("prop_id", piece.Prop.Id);
			if (node is Door { Interior: not null } door) door.Interior.Id = $"{piece.Prop.Id}:{index}";
			root.AddChild(node);
		}
	}

	private static Node3D CreateNode(PlacedPiece piece)
	{
		var prop = piece.Prop;
		var turn = new Transform3D(prop.Turn, Vector3.Zero);
		if (prop.Scenes.Length > 0)
		{
			var instance = prop.Scenes[piece.Variant].Instantiate<Node3D>();
			instance.Transform = piece.Transform * turn;
			if (!prop.Blocking || instance.FindChildren("*", nameof(CollisionShape3D), true, false).Count > 0) return instance;
			// A bare model (an imported glTF) blocks with box colliders over its measured solids, like mesh-library props.
			var wrapper = new Node3D { Transform = piece.Transform };
			instance.Transform = turn;
			wrapper.AddChild(instance);
			var sceneBody = new StaticBody3D { Name = "Body", CollisionLayer = 16, CollisionMask = 0 };
			foreach (var solid in prop.Geometry(piece.Variant).Solids)
				sceneBody.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = solid.Size }, Position = solid.GetCenter() });
			wrapper.AddChild(sceneBody);
			return wrapper;
		}
		int item = prop.Library.FindItemByName(prop.Items[piece.Variant]);
		// Structural pieces may be stretched along a run; physics needs unscaled shapes, so the scale moves onto the
		// mesh and into box colliders sized from the measured solids.
		var scale = piece.Transform.Basis.Scale;
		bool scaled = !scale.IsEqualApprox(Vector3.One);
		var stretch = new Transform3D(Basis.FromScale(scale), Vector3.Zero);
		var node = new Node3D { Transform = scaled ? new Transform3D(piece.Transform.Basis.Orthonormalized(), piece.Transform.Origin) : piece.Transform };
		node.AddChild(new MeshInstance3D
		{
			Name = "Mesh", Mesh = prop.Library.GetItemMesh(item),
			Transform = (scaled ? stretch : Transform3D.Identity) * turn * prop.Library.GetItemMeshTransform(item),
		});
		var geometry = prop.Geometry(piece.Variant);
		if (!prop.Blocking && geometry.Shapes.Count == 0) return node;
		// Props share the arrangement collision layer: they block actors and navigation but not exploration sight.
		var body = new StaticBody3D { Name = "Body", CollisionLayer = 16, CollisionMask = 0 };
		node.AddChild(body);
		if (scaled)
		{
			foreach (var solid in geometry.Solids)
			{
				var box = stretch * solid;
				body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = box.Size }, Position = box.GetCenter() });
			}
		}
		else if (geometry.Shapes.Count > 0)
		{
			foreach (var (shape, transform) in geometry.Shapes) body.AddChild(new CollisionShape3D { Shape = shape, Transform = transform });
		}
		else
		{
			var box = geometry.Envelope;
			body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = box.Size }, Position = box.GetCenter() });
		}
		return node;
	}

	public static Vector2 XZ(Vector3 point) => new(point.X, point.Z);
	public static Rect2 XZ(Aabb box) => new(box.Position.X, box.Position.Z, box.Size.X, box.Size.Z);

	/// <summary>Interval a box covers along an axis-aligned unit direction (which may point down an axis).</summary>
	public static (float Min, float Max) Project(Aabb box, Vector3 axis)
	{
		float a = box.Position.Dot(axis), b = box.End.Dot(axis);
		return (Mathf.Min(a, b), Mathf.Max(a, b));
	}
}