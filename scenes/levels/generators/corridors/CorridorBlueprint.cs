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
/// Blueprint for decorating generated dungeon corridors after walls and secret branches have been placed.
/// Measures corridor wall runs, doorway thresholds, corners/T-junctions, and straight 1-tile-wide spans in the same
/// local tile coordinate frame as <see cref="RoomBlueprint"/> (origin at map tile (0, 0)).
/// </summary>
public sealed class CorridorBlueprint
{
	private const float Tolerance = .02f;
	private static readonly Vector2I[] Cardinals = [Vector2I.Up, Vector2I.Right, Vector2I.Down, Vector2I.Left];

	private readonly Dictionary<(Vector2I Tile, Vector2I Outward), (float Depth, bool Decoratable)> _walls = [];
	private readonly HashSet<(Vector2I Tile, Vector2I Outward)> _blockedEdges = [];
	private readonly List<Vector3> _doorPositions = [];
	private readonly List<PlacedPiece> _pieces = [];
	private readonly List<Vector3> _lightPositions = [];
	private readonly List<Vector3> _passagewayPositions = [];
	private readonly Dictionary<object, int> _uses = [];
	private WallRun[] _wallRuns;
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
		foreach (var (door, _) in generator.Floor.Doors)
		{
			if (GodotObject.IsInstanceValid(door))
			{
				_doorPositions.Add(door.GlobalPosition - originWorld);
			}
		}

		var secretConnectors = new HashSet<Vector2I>();
		foreach (var (_, host, connector, _) in generator.GeneratedSecrets)
		{
			secretConnectors.Add(connector);
			_blockedEdges.Add((host, connector - host));
			_blockedEdges.Add((connector, host - connector));
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
	public IReadOnlyList<Vector3> LightPositions => _lightPositions;
	public IReadOnlyList<Vector3> PassagewayPositions => _passagewayPositions;

	public IReadOnlyList<WallRun> WallRuns => _wallRuns ??= ComputeWallRuns().ToArray();
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

	private static bool HasNearby(IReadOnlyList<Vector3> points, Vector3 target, float minDistance)
	{
		if (minDistance <= 0f) return false;
		float minSq = minDistance * minDistance;
		var p = RoomBlueprint.XZ(target);
		for (int i = 0; i < points.Count; i++)
		{
			if (RoomBlueprint.XZ(points[i]).DistanceSquaredTo(p) < minSq) return true;
		}
		return false;
	}

	public bool HasNearbyLight(Vector3 localPosition, float minDistance) =>
		HasNearby(_lightPositions, localPosition, minDistance);

	public bool HasNearbyPassageway(Vector3 localPosition, float minDistance) =>
		HasNearby(_passagewayPositions, localPosition, minDistance);

	public bool HasNearbyDoor(Vector3 localPosition, float minDistance) =>
		HasNearby(_doorPositions, localPosition, minDistance);

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

		if (HasNearbyDoor(transform.Origin, 1.6f)) return false;
		if (minLightSpacing > 0f && IsLightProp(prop) && HasNearbyLight(transform.Origin, minLightSpacing)) return false;

		var envelope = transform * geometry.Envelope;
		if (!RoomBlueprint.HasWallSupport(envelope, run, IsDecoratable)) return false;
		if (!CanAcceptPiece(prop, envelope)) return false;

		AcceptPiece(prop, variant, transform, envelope);
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
		if (HasNearbyDoor(center, Room.TileSize * 0.85f)) return false;
		if (minSpacing > 0f && HasNearbyPassageway(center, minSpacing)) return false;

		int variant = PickVariant(prop, rng);
		var facing = new Basis(Vector3.Up, Mathf.Atan2(along.X, along.Y));
		var transform = new Transform3D(facing, center);
		var envelope = transform * prop.Geometry(variant).Envelope;
		if (!CanAcceptPiece(prop, envelope)) return false;

		AcceptPiece(prop, variant, transform, envelope);
		_passagewayPositions.Add(center);
		return true;
	}

	/// <summary>Instantiates all accepted corridor props under a CorridorProps node on <paramref name="parent"/>.</summary>
	public Node3D EmitProps(Node3D parent)
	{
		if (_pieces.Count == 0) return null;
		var root = new Node3D
		{
			Name = "CorridorProps",
			Position = Map.TileToWorld(0, 0),
		};
		parent.AddChild(root);
		for (int index = 0; index < _pieces.Count; index++)
		{
			var piece = _pieces[index];
			var node = RoomBlueprint.CreateNode(piece);
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
		var runs = WallRuns;
		for (int r = 0; r < runs.Count; r++)
		{
			var run = runs[r];
			if (run.Outward != outward) continue;
			for (int index = 0; index < run.Length; index++)
			{
				if (run.TileAt(index) == tile) return run;
			}
		}
		return default;
	}

	private bool CanAcceptPiece(Prop prop, Aabb envelope)
	{
		var area = RoomBlueprint.XZ(envelope);
		for (int i = 0; i < _pieces.Count; i++)
		{
			var other = _pieces[i];
			float gap = Mathf.Max(prop.Gap, other.Prop.Gap);
			bool vertical = envelope.Position.Y < other.Envelope.End.Y - .01f && envelope.End.Y > other.Envelope.Position.Y + .01f;
			if (vertical && RoomBlueprint.XZ(other.Envelope).Grow(gap - Tolerance).Intersects(area))
			{
				return false;
			}
		}
		return true;
	}

	private void AcceptPiece(Prop prop, int variant, Transform3D transform, Aabb envelope)
	{
		_pieces.Add(new(prop, variant, transform, envelope));
		_uses[prop] = _uses.GetValueOrDefault(prop) + 1;
		_uses[(prop, variant)] = _uses.GetValueOrDefault((prop, variant)) + 1;
		if (IsLightProp(prop))
		{
			_lightPositions.Add(transform.Origin);
		}
	}

	private void MeasureWalls()
	{
		MeshLibrary library = WallGridMap?.MeshLibrary;
		int half = Room.TileSize / 2;
		foreach (var tile in CorridorTiles)
		{
			var localCenter = RoomFootprint.TileCenter(tile);
			var worldCenter = Map.TileToWorld(tile);
			foreach (var outward in Cardinals)
			{
				if (_blockedEdges.Contains((tile, outward))) continue;
				var neighbor = tile + outward;
				if (Map.IsWithinBounds(neighbor.X, neighbor.Y) && !Map.IsWallOrEmpty(neighbor.X, neighbor.Y)) continue;

				var localEdge = localCenter + new Vector3(outward.X * half, 0, outward.Y * half);
				if (HasNearbyDoor(localEdge, 2.2f)) continue;

				var cell = worldCenter + new Vector3I(outward.X * half, 0, outward.Y * half);
				int item = WallGridMap?.GetCellItem(cell) ?? -1;
				if (item < 0) continue;

				string name = library?.GetItemName(item) ?? "";
				var mesh = library?.GetItemMesh(item);
				float depth = mesh == null ? .5f : mesh.GetAabb().End.Z;
				bool decoratable = Walls != null
					? Walls.Straight.ContainsKey(name) && !Walls.Accents.ContainsKey(name) && name != Walls.Pillar
					: !name.Contains("corner", StringComparison.OrdinalIgnoreCase)
						&& !name.Contains("half", StringComparison.OrdinalIgnoreCase)
						&& !name.Contains("doorway", StringComparison.OrdinalIgnoreCase);
				_walls[(tile, outward)] = (depth, decoratable);
			}
		}
	}

	private IEnumerable<WallRun> ComputeWallRuns()
	{
		foreach (var outward in Cardinals)
		{
			var along = outward.X == 0 ? Vector2I.Right : Vector2I.Down;
			var edges = CorridorTiles.Where(tile => HasWall(tile, outward)).ToHashSet();
			foreach (var first in edges.OrderBy(tile => tile.Y).ThenBy(tile => tile.X).ToArray())
			{
				if (!edges.Contains(first) || edges.Contains(first - along)) continue;
				int length = 0;
				while (edges.Remove(first + along * length))
				{
					length++;
				}
				yield return new(first, along, outward, length);
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
		foreach (var tile in CorridorTiles)
		{
			var openList = new List<Vector2I>(4);
			for (int i = 0; i < Cardinals.Length; i++)
			{
				var dir = Cardinals[i];
				if (!_blockedEdges.Contains((tile, dir)) && !HasWall(tile, dir)
					&& Map.IsWithinBounds(tile.X + dir.X, tile.Y + dir.Y)
					&& Map.IsWalkable(tile.X + dir.X, tile.Y + dir.Y))
				{
					openList.Add(dir);
				}
			}
			if (openList.Count == 3)
			{
				yield return new(tile, openList.ToArray(), IsTJunction: true);
			}
			else if (openList.Count == 2 && openList[0] + openList[1] != Vector2I.Zero)
			{
				yield return new(tile, openList.ToArray(), IsTJunction: false);
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
				if (!candidates.Contains(first) || candidates.Contains(first - along)) continue;
				int length = 0;
				while (candidates.Remove(first + along * length))
				{
					length++;
				}
				yield return new(first, along, length);
			}
		}
	}
}