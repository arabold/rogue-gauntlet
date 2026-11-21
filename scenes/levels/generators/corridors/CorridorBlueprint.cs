using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A corridor tile immediately outside a connected room doorway or opening.
/// <see cref="FromRoomDirection"/> is the cardinal unit step from the room connector into <see cref="Tile"/>.
/// </summary>
public readonly record struct CorridorThreshold(Vector2I Tile, Vector2I FromRoomDirection)
{
	public Vector2I LeftOutward => new(-FromRoomDirection.Y, FromRoomDirection.X);
	public Vector2I RightOutward => new(FromRoomDirection.Y, -FromRoomDirection.X);
}

/// <summary>
/// A corridor L-corner (two perpendicular open directions) or T-junction (three open directions).
/// </summary>
public readonly record struct CorridorJunction(Vector2I Tile, Vector2I[] OpenDirections, bool IsTJunction)
{
	/// <summary>For a T-junction, the outward direction of the wall directly opposite the stem.</summary>
	public Vector2I StemOppositeOutward => -(OpenDirections[0] + OpenDirections[1] + OpenDirections[2]);
}

/// <summary>
/// A maximal straight 1-tile-wide corridor span along <see cref="Along"/> (+X or +Z) where every tile is
/// bounded by corridor walls on both perpendicular sides.
/// </summary>
public readonly record struct CorridorSpan(Vector2I Start, Vector2I Along, int Length)
{
	public Vector2I TileAt(int index) => Start + Along * index;
	public Vector2I Perpendicular => new(-Along.Y, Along.X);
}

/// <summary>
/// Blueprint for decorating generated dungeon corridors after boundary walls have been placed.
/// Measures corridor wall runs, doorway thresholds, corners/T-junctions, and straight 1-tile-wide spans in the same
/// local tile coordinate frame as <see cref="RoomBlueprint"/> (origin at map tile (0, 0)).
/// </summary>
public sealed class CorridorBlueprint
{
	private const float Tolerance = .02f;
	private static readonly Vector2I[] Cardinals = [Vector2I.Up, Vector2I.Right, Vector2I.Down, Vector2I.Left];

	private readonly Dictionary<(Vector2I Tile, Vector2I Outward), (float Depth, bool Decoratable)> _walls = [];
	private readonly HashSet<(Vector2I Tile, Vector2I Outward)> _blockedEdges = [];
	private readonly List<(Vector3 Position, Vector2I Tile)> _doors = [];
	private readonly List<PlacedPiece> _pieces = [];
	private readonly Dictionary<Vector2I, List<(int Index, Vector2I Tile)>> _piecesByTile = [];
	private readonly HashSet<int> _seenPieceIndices = [];
	private readonly List<(Vector3 Position, Vector2I Tile)> _lights = [];
	private readonly List<(Vector3 Position, Vector2I Tile)> _passageways = [];
	private readonly Dictionary<object, int> _uses = [];
	private float _maxPieceGap;
	private WallRun[] _wallRuns;
	private Dictionary<(Vector2I Tile, Vector2I Outward), WallRun> _wallRunIndex;
	private CorridorThreshold[] _thresholds;
	private CorridorJunction[] _junctions;
	private CorridorSpan[] _spans;

	public CorridorBlueprint(MapGenerator generator, WallStyle walls, ulong seed)
	{
		ArgumentNullException.ThrowIfNull(generator);
		Map = generator.Map;
		WallGridMap = generator.WallGridMap;
		Walls = walls;
		Seed = seed;

		var originWorld = (Vector3)Map.TileToWorld(0, 0);
		var secretConnectors = new HashSet<Vector2I>();
		foreach (var (door, connector) in generator.Floor.Doors)
		{
			if (!GodotObject.IsInstanceValid(door)) continue;
			_doors.Add((door.GlobalPosition - originWorld, connector));
			if (door.Secret?.IsSecret != true) continue;
			secretConnectors.Add(connector);
			foreach (var direction in Map.GetConnectorDirections(connector.X, connector.Y))
			{
				var outside = connector + direction;
				_blockedEdges.Add((connector, direction));
				_blockedEdges.Add((outside, -direction));
			}
		}

		var corridorTiles = new List<Vector2I>();
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				if (Map.IsCorridor(x, z))
				{
					corridorTiles.Add(new Vector2I(x, z));
				}
			}
		}

		CorridorTiles = corridorTiles;
		MeasureWalls();
		_thresholds = ComputeThresholds(generator, secretConnectors).ToArray();
	}

	public MapData Map { get; }
	public GridMap WallGridMap { get; }
	public WallStyle Walls { get; }
	public ulong Seed { get; }
	public IReadOnlyList<Vector2I> CorridorTiles { get; }
	public IReadOnlyList<PlacedPiece> Pieces => _pieces;

	public IReadOnlyList<WallRun> WallRuns => _wallRuns ??= WallRun.Find(CorridorTiles, HasWall, CanStepAlong).ToArray();
	public IReadOnlyList<CorridorThreshold> Thresholds => _thresholds;
	public IReadOnlyList<CorridorJunction> Junctions => _junctions ??= ComputeJunctions().ToArray();
	public IReadOnlyList<CorridorSpan> Spans => _spans ??= ComputeSpans().ToArray();

	public bool HasWall(Vector2I tile, Vector2I outward) => _walls.ContainsKey((tile, outward));
	public float WallDepth(Vector2I tile, Vector2I outward) => _walls.TryGetValue((tile, outward), out var wall) ? wall.Depth : .5f;
	public bool IsDecoratable(Vector2I tile, Vector2I outward) => _walls.TryGetValue((tile, outward), out var wall) && wall.Decoratable;

	public float RunDepth(WallRun run)
	{
		float max = .5f;
		for (int index = 0; index < run.Length; index++)
		{
			float depth = WallDepth(run.TileAt(index), run.Outward);
			if (depth > max) max = depth;
		}
		return max;
	}

	public RandomNumberGenerator Random(string key) =>
		GenerationRandom.Create(Seed, GenerationRandom.Decoration, $"corridor:{Walls?.Id ?? "default"}:{key}");

	/// <summary>Balanced weighted selection across corridor placements, matching <see cref="RoomBlueprint.Pick{T}"/>.</summary>
	public T Pick<T>(IReadOnlyList<T> options, RandomNumberGenerator rng) where T : Placeable =>
		Weighted.PickBalanced(options, opt => _uses.GetValueOrDefault(opt), rng);

	/// <summary>Prefers variants not yet overused in corridors; ties break by the seeded draw.</summary>
	public int PickVariant(Prop prop, RandomNumberGenerator rng) =>
		Weighted.PickVariant(prop, v => _uses.GetValueOrDefault((prop, v)), rng);

	public static bool IsLightProp(Prop prop) => prop != null && prop.EmitsLight;

	private Vector2I LocalToTile(Vector3 localPosition) => new(
		Mathf.RoundToInt(localPosition.X / Room.TileSize),
		Mathf.RoundToInt(localPosition.Z / Room.TileSize));

	private bool CanStepAlong(Vector2I tile, Vector2I step) => !Map.HasWallToward(tile.X, tile.Y, step);

	private bool HasNearbyWithSight(List<(Vector3 Position, Vector2I Tile)> items, Vector3 target, Vector2I targetTile, float minDistance)
	{
		if (minDistance <= 0f) return false;
		float minSq = minDistance * minDistance;
		var p = RoomBlueprint.XZ(target);
		for (int i = 0; i < items.Count; i++)
		{
			if (RoomBlueprint.XZ(items[i].Position).DistanceSquaredTo(p) < minSq && Map.HasTileSight(targetTile, items[i].Tile))
			{
				return true;
			}
		}
		return false;
	}

	public bool HasNearbyLight(Vector3 localPosition, float minDistance) =>
		HasNearbyWithSight(_lights, localPosition, LocalToTile(localPosition), minDistance);

	public bool HasNearbyPassageway(Vector3 localPosition, float minDistance) =>
		HasNearbyWithSight(_passageways, localPosition, LocalToTile(localPosition), minDistance);

	public bool HasNearbyDoor(Vector3 localPosition, float minDistance) =>
		HasNearbyWithSight(_doors, localPosition, LocalToTile(localPosition), minDistance);

	private bool HasNearbyDoor(Vector2I targetTile, Vector3 localPosition, float minDistance) =>
		HasNearbyWithSight(_doors, localPosition, targetTile, minDistance);

	/// <summary>
	/// Attempts to hang a wall-mounted <paramref name="prop"/> centered on <paramref name="tile"/>'s
	/// <paramref name="outward"/> wall edge.
	/// </summary>
	public bool TryPlaceOnTileWall(
		Prop prop,
		Vector2I tile,
		Vector2I outward,
		RandomNumberGenerator rng,
		float minLightSpacing = 0f)
	{
		if (!IsDecoratable(tile, outward)) return false;
		var run = FindWallRun(tile, outward);
		if (run.Length == 0) return false;
		float centerAlong = RoomBlueprint.AlongRun(run, RoomFootprint.TileCenter(tile));
		return TryPlaceAgainstWall(prop, run, centerAlong, rng, minLightSpacing);
	}

	/// <summary>
	/// Attempts to hang a wall-mounted <paramref name="prop"/> on <paramref name="run"/> centered at
	/// <paramref name="centerAlong"/> units from the run's start.
	/// </summary>
	public bool TryPlaceAgainstWall(
		Prop prop,
		WallRun run,
		float centerAlong,
		RandomNumberGenerator rng,
		float minLightSpacing = 0f)
	{
		if (prop == null || prop.Mount != PropMount.Wall) return false;
		int variant = PickVariant(prop, rng);
		var geometry = prop.Geometry(variant);
		var facing = new Transform3D(run.Facing, Vector3.Zero);
		var all = facing * geometry.Envelope;
		var (alongMin, alongMax) = RoomBlueprint.Project(all, run.Tangent);
		float width = alongMax - alongMin;
		float along = centerAlong - width / 2f;
		if (along < -Tolerance || along + width > run.LengthUnits + Tolerance) return false;

		var (inwardMin, _) = RoomBlueprint.Project(all, run.Inward);
		float face = RunDepth(run) - (prop.Mount == PropMount.Wall ? prop.WallInset : 0);
		var origin = run.LineStart + run.Tangent * (along - alongMin) + run.Inward * (face - inwardMin);
		if (prop.Mount == PropMount.Wall) origin.Y = prop.MountHeight;
		var transform = new Transform3D(run.Facing, origin);

		var pieceTile = run.TileAt(Math.Clamp(Mathf.FloorToInt(centerAlong / Room.TileSize), 0, run.Length - 1));
		if (HasNearbyDoor(pieceTile, transform.Origin, 1.6f)) return false;
		if (minLightSpacing > 0f && IsLightProp(prop) && HasNearbyWithSight(_lights, transform.Origin, pieceTile, minLightSpacing)) return false;

		var envelope = transform * geometry.Envelope;
		if (!RoomBlueprint.HasWallSupport(envelope, run, IsDecoratable)) return false;
		if (!CanAcceptPiece(prop, envelope, pieceTile)) return false;

		AcceptPiece(prop, variant, transform, envelope, pieceTile);
		return true;
	}

	/// <summary>
	/// Attempts to place an open passageway archway across a straight 1-tile-wide corridor <paramref name="tile"/>
	/// running along <paramref name="along"/>.
	/// </summary>
	public bool TryPlacePassageway(
		Prop prop,
		Vector2I tile,
		Vector2I along,
		RandomNumberGenerator rng,
		float minSpacing = 0f)
	{
		if (prop == null) return false;
		var perp = new Vector2I(-along.Y, along.X);
		if (!HasWall(tile, perp) || !HasWall(tile, -perp)) return false;

		var center = RoomFootprint.TileCenter(tile);
		if (HasNearbyDoor(tile, center, Room.TileSize * 0.85f)) return false;
		if (minSpacing > 0f && HasNearbyWithSight(_passageways, center, tile, minSpacing)) return false;

		int variant = PickVariant(prop, rng);
		var facing = new Basis(Vector3.Up, Mathf.Atan2(along.X, along.Y));
		var transform = new Transform3D(facing, center);
		var envelope = transform * prop.Geometry(variant).Envelope;
		if (!CanAcceptPiece(prop, envelope, tile)) return false;

		AcceptPiece(prop, variant, transform, envelope, tile);
		_passageways.Add((center, tile));
		return true;
	}

	/// <summary>Instantiates all accepted corridor props under a CorridorProps node on <paramref name="parent"/>.</summary>
	public Node3D EmitProps(Node3D parent, float fixtureLitChance)
	{
		if (_pieces.Count == 0) return null;
		var root = new Node3D
		{
			Name = "CorridorProps",
			Position = Map.TileToWorld(0, 0),
		};
		using var lighting = GenerationRandom.Create(Seed, GenerationRandom.Cosmetic, "corridor-lighting");
		parent.AddChild(root);
		for (int index = 0; index < _pieces.Count; index++)
		{
			var piece = _pieces[index];
			var node = RoomBlueprint.CreateNode(piece);
			RoomBlueprint.ConfigureLighting(node, lighting, fixtureLitChance);
			int dot = piece.Prop.Id.LastIndexOf('.');
			string baseName = dot >= 0 ? piece.Prop.Id[(dot + 1)..] : piece.Prop.Id;
			node.Name = $"{baseName}_{index}";
			node.SetMeta("prop_id", piece.Prop.Id);
			root.AddChild(node);
		}
		return root;
	}

	private WallRun FindWallRun(Vector2I tile, Vector2I outward)
	{
		if (_wallRunIndex == null)
		{
			var index = new Dictionary<(Vector2I Tile, Vector2I Outward), WallRun>();
			foreach (var run in WallRuns)
			{
				for (int i = 0; i < run.Length; i++)
				{
					index[(run.TileAt(i), run.Outward)] = run;
				}
			}
			_wallRunIndex = index;
		}
		return _wallRunIndex.GetValueOrDefault((tile, outward));
	}

	private bool CanAcceptPiece(Prop prop, Aabb envelope, Vector2I tile)
	{
		var area = RoomBlueprint.XZ(envelope);
		float queryRadius = Mathf.Max(prop.Gap, _maxPieceGap) - Tolerance;
		var expanded = queryRadius > 0f ? area.Grow(queryRadius) : area;
		int minX = Mathf.FloorToInt(expanded.Position.X / Room.TileSize + 0.5f);
		int maxX = Mathf.FloorToInt(expanded.End.X / Room.TileSize + 0.5f);
		int minZ = Mathf.FloorToInt(expanded.Position.Y / Room.TileSize + 0.5f);
		int maxZ = Mathf.FloorToInt(expanded.End.Y / Room.TileSize + 0.5f);

		_seenPieceIndices.Clear();
		for (int tx = minX; tx <= maxX; tx++)
		{
			for (int tz = minZ; tz <= maxZ; tz++)
			{
				if (!_piecesByTile.TryGetValue(new Vector2I(tx, tz), out var bucket)) continue;
				foreach (var (pieceIndex, otherTile) in bucket)
				{
					if (!_seenPieceIndices.Add(pieceIndex)) continue;
					var other = _pieces[pieceIndex];
					float gap = Mathf.Max(prop.Gap, other.Prop.Gap);
					bool vertical = envelope.Position.Y < other.Envelope.End.Y - .01f && envelope.End.Y > other.Envelope.Position.Y + .01f;
					if (vertical && RoomBlueprint.XZ(other.Envelope).Grow(gap - Tolerance).Intersects(area) && Map.HasTileSight(tile, otherTile))
					{
						return false;
					}
				}
			}
		}
		return true;
	}

	private void AcceptPiece(Prop prop, int variant, Transform3D transform, Aabb envelope, Vector2I tile)
	{
		int index = _pieces.Count;
		_pieces.Add(new(prop, variant, transform, envelope));
		if (prop.Gap > _maxPieceGap) _maxPieceGap = prop.Gap;

		var area = RoomBlueprint.XZ(envelope);
		int minX = Mathf.FloorToInt(area.Position.X / Room.TileSize + 0.5f);
		int maxX = Mathf.FloorToInt(area.End.X / Room.TileSize + 0.5f);
		int minZ = Mathf.FloorToInt(area.Position.Y / Room.TileSize + 0.5f);
		int maxZ = Mathf.FloorToInt(area.End.Y / Room.TileSize + 0.5f);
		for (int tx = minX; tx <= maxX; tx++)
		{
			for (int tz = minZ; tz <= maxZ; tz++)
			{
				var key = new Vector2I(tx, tz);
				if (!_piecesByTile.TryGetValue(key, out var bucket)) _piecesByTile[key] = bucket = [];
				bucket.Add((index, tile));
			}
		}

		_uses[prop] = _uses.GetValueOrDefault(prop) + 1;
		_uses[(prop, variant)] = _uses.GetValueOrDefault((prop, variant)) + 1;
		if (IsLightProp(prop))
		{
			_lights.Add((transform.Origin, tile));
		}
	}

	private void MeasureWalls()
	{
		if (WallGridMap?.MeshLibrary == null) return;
		var itemCache = new Dictionary<int, (Aabb Bounds, string Name)>();
		int half = Room.TileSize / 2;
		foreach (var tile in CorridorTiles)
		{
			var localCenter = RoomFootprint.TileCenter(tile);
			var worldCenter = Map.TileToWorld(tile);
			foreach (var outward in Cardinals)
			{
				if (!Map.HasWallToward(tile.X, tile.Y, outward)) continue;

				var localEdge = localCenter + new Vector3(outward.X * half, 0, outward.Y * half);
				var edge = WallFootprints.MeasureEdge(WallGridMap, worldCenter + new Vector3I(outward.X * half, 0, outward.Y * half), -outward, Room.TileSize, itemCache);
				if (!edge.Covered) continue;
				bool nearDoor = HasNearbyDoor(tile, localEdge, 2.2f);
				_walls[(tile, outward)] = (edge.Depth, !nearDoor && Walls?.IsPlain(edge.Item) == true);
			}
		}
	}

	private IEnumerable<CorridorThreshold> ComputeThresholds(MapGenerator generator, HashSet<Vector2I> secretConnectors)
	{
		var seen = new HashSet<(Vector2I Tile, Vector2I Direction)>();
		foreach (var region in generator.Floor.Rooms)
		{
			foreach (var connector in region.ConnectorTiles)
			{
				if (secretConnectors.Contains(connector)) continue;
				foreach (var direction in Map.GetConnectorDirections(connector.X, connector.Y))
				{
					var outside = connector + direction;
					if (!Map.IsWithinBounds(outside.X, outside.Y) || !Map.IsCorridor(outside.X, outside.Y)) continue;
					if (seen.Add((outside, direction)))
					{
						yield return new(outside, direction);
					}
				}
			}
		}
	}

	private IEnumerable<CorridorJunction> ComputeJunctions()
	{
		var open = new Vector2I[4];
		foreach (var tile in CorridorTiles)
		{
			int count = 0;
			for (int i = 0; i < Cardinals.Length; i++)
			{
				var dir = Cardinals[i];
				if (!_blockedEdges.Contains((tile, dir))
					&& !Map.HasWallToward(tile.X, tile.Y, dir)
					&& Map.IsWalkable(tile.X + dir.X, tile.Y + dir.Y))
				{
					open[count++] = dir;
				}
			}
			if (count == 3)
			{
				yield return new(tile, [open[0], open[1], open[2]], IsTJunction: true);
			}
			else if (count == 2 && open[0] + open[1] != Vector2I.Zero)
			{
				yield return new(tile, [open[0], open[1]], IsTJunction: false);
			}
		}
	}

	private IEnumerable<CorridorSpan> ComputeSpans()
	{
		foreach (var along in new[] { Vector2I.Right, Vector2I.Down })
		{
			var perp = new Vector2I(-along.Y, along.X);
			var candidates = CorridorTiles
				.Where(tile => HasWall(tile, perp) && HasWall(tile, -perp))
				.ToHashSet();
			foreach (var first in candidates.OrderBy(tile => tile.Y).ThenBy(tile => tile.X).ToArray())
			{
				if (!candidates.Contains(first)) continue;
				if (candidates.Contains(first - along) && !Map.HasWallToward(first.X - along.X, first.Y - along.Y, along)) continue;
				candidates.Remove(first);
				var cur = first;
				int length = 1;
				while (!Map.HasWallToward(cur.X, cur.Y, along) && candidates.Remove(cur + along))
				{
					cur += along;
					length++;
				}
				yield return new(first, along, length);
			}
		}
	}
}