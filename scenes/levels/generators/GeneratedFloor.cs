using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// The handoff between floor generation and gameplay: the generated layout plus every spatial query gameplay asks of
/// it. <see cref="MapGenerator"/> creates one per attempt and runs its own placement checks against the same instance,
/// so generation-time and runtime answers cannot drift. It is live while generating; <see cref="Freeze"/> snapshots
/// decoration occupancy, after which queries read only snapshots and the per-frame prop cache. Not a Node: it owns no
/// ticking, rendering or lifetime of its own.
/// </summary>
public sealed class GeneratedFloor
{
	private const float SpawnArrivalClearance = 20f;
	private const float SpawnPointSpacing = 5f;
	private const float SpawnAnchorClearance = 7f;
	private const float SpawnPropClearance = 2f;
	// Physics layers a runtime spawn must stay clear of: world (1) | walls (2) | props (5).
	// Keeps items out of stairs, transition blockers, walls, and props via a single overlap test.
	private const uint SpawnObstructionMask = WorldSight.Blockers;

	private readonly Node3D _world;
	private readonly List<RoomRegion> _rooms = new();
	// Interior floor tile -> room id, for "which room is this in" queries.
	private readonly Dictionary<Vector2I, int> _tileToRoom = new();
	private readonly List<(Door Door, Vector2I Connector)> _doors = new();
	private WalkabilityGrid _walkability;
	private bool[] _reachable;
	private readonly Dictionary<string, (Door Door, DoorAccessSaveData Baseline)> _doorBaselines = new();
	private List<FloorTrap> _traps;
	private readonly HashSet<Vector2I> _propTiles = new();
	private ulong _propTilesFrame = ulong.MaxValue;
	private int _propTilesCount = -1;
	private PhysicsShapeQueryParameters3D _columnQuery;

	/// <param name="world">The generator: the physics space, scene tree and the subtree holding progression anchors.</param>
	public GeneratedFloor(MapData map, Node3D world)
	{
		Map = map;
		_world = world;
	}

	public MapData Map { get; }
	/// <summary>Full floor regions, including connector thresholds, indexed by room id.</summary>
	public IReadOnlyList<RoomRegion> Rooms => _rooms;
	/// <summary>Physical doors paired with the connector they guard. One connector can carry two doors.</summary>
	public IReadOnlyList<(Door Door, Vector2I Connector)> Doors => _doors;
	/// <summary>The descent arrival the floor is built around; null only for a layout without one.</summary>
	public PlayerSpawnPoint Arrival { get; internal set; }
	/// <summary>The exit down to the next floor; null for a layout without one.</summary>
	public LevelTransitionTrigger Descent { get; internal set; }
	/// <summary>This floor's special encounters, decided before generation places anything; null on floors without any.</summary>
	public EncounterPlan Encounter { get; internal set; }
	/// <summary>Where the gatekeeper spawns, facing included: an authored marker, or the roomiest fit nearest the descent.
	/// Resolved once, before access and loot; null when the floor has no gatekeeper or it takes an ordinary point.</summary>
	public Transform3D? GatekeeperSite { get; internal set; }
	/// <summary>The spawn point that brings the gatekeeper in; null on floors without one.</summary>
	public SpawnPoint GatekeeperSpawn { get; internal set; }
	public bool IsFrozen => _traps != null;
	/// <summary>Every door's generated access state, keyed by its stable connection id: what exploration saves diff against.</summary>
	public IReadOnlyDictionary<string, (Door Door, DoorAccessSaveData Baseline)> DoorBaselines => _doorBaselines;
	/// <summary>The floor's traps, collected when the floor freezes.</summary>
	public IReadOnlyList<FloorTrap> Traps => _traps ?? [];

	/// <summary>Progression anchors and stairs that keep spawns at a distance; collected with the arrival once rooms are placed.</summary>
	public IReadOnlyList<Vector3> SpawnAnchors { get; internal set; } = [];
	/// <summary>Where arrivals step off each staircase; generated loot and props never land on them.</summary>
	public IReadOnlyList<Vector3> StairLandings { get; internal set; } = [];

	internal void AddRoom(RoomRegion room)
	{
		RequireLive();
		_rooms.Add(room);
	}

	internal void AssignTile(Vector2I tile, int roomId)
	{
		RequireLive();
		_tileToRoom[tile] = roomId;
	}

	internal void UnassignTile(Vector2I tile)
	{
		RequireLive();
		_tileToRoom.Remove(tile);
	}

	internal void AddDoor(Door door, Vector2I connector)
	{
		RequireLive();
		_doors.Add((door, connector));
	}

	internal void RemoveDoors(Predicate<(Door Door, Vector2I Connector)> match)
	{
		RequireLive();
		_doors.RemoveAll(match);
	}

	/// <summary>Records a door's seeded access state once its connection id is final.</summary>
	internal void RecordDoorBaseline(Door door)
	{
		RequireLive();
		_doorBaselines.Add(door.ConnectionId, (door, door.CaptureAccess()));
	}

	/// <summary>Actor-center walkability of the finished geometry, built once rooms, corridors and secret pockets exist.
	/// Null in previews and layouts without an arrival; queries then fall back to tile occupancy alone.</summary>
	internal WalkabilityGrid Walkability => _walkability;
	/// <summary>Per-cell reachability for <see cref="Walkability"/>; null alongside it.</summary>
	internal bool[] Reachable => _reachable;

	/// <summary>Records which cells can be reached from the arrival with every door open. Spawns, loot, traps, teleports
	/// and drops only use reachable cells, so nothing lands in an inaccessible pocket behind furniture.</summary>
	internal void SetWalkability(WalkabilityGrid grid, bool[] reachable)
	{
		RequireLive();
		_walkability = grid;
		_reachable = reachable;
	}

	/// <summary>True when an actor centered here can walk to the arrival (doors open). Without walkability, every point is.</summary>
	public bool IsReachable(Vector3 point)
	{
		if (_walkability == null) return true;
		var cell = _walkability.CellAt(new Vector2(point.X, point.Z));
		return _walkability.Contains(cell) && _reachable[cell.Y * _walkability.Size.X + cell.X];
	}

	/// <summary>Reachable and clear of solid geometry within <paramref name="radius"/>, for actors larger than the grid's.</summary>
	private bool IsReachableArea(Vector3 point, float radius)
	{
		if (_walkability == null) return true;
		float extra = radius - _walkability.ActorRadius;
		if (extra <= 0) return IsReachable(point);
		int reach = Mathf.CeilToInt(extra / _walkability.CellSize);
		var center = _walkability.CellAt(new Vector2(point.X, point.Z));
		for (int x = -reach; x <= reach; x++)
		for (int z = -reach; z <= reach; z++)
		{
			var cell = center + new Vector2I(x, z);
			if (_walkability.CellCenter(cell).DistanceTo(new Vector2(point.X, point.Z)) > extra) continue;
			if (!_walkability.Contains(cell) || !_reachable[cell.Y * _walkability.Size.X + cell.X]) return false;
		}
		return true;
	}

	private void RequireLive()
	{
		if (IsFrozen) throw new InvalidOperationException("A frozen floor cannot change.");
	}

	/// <summary>Ends generation: snapshots the traps. Nothing writes to the floor after this.</summary>
	public void Freeze()
	{
		RequireLive();
		_traps = _world.FindChildren("*", "", true, false).OfType<FloorTrap>().Where(trap => !trap.IsQueuedForDeletion()).ToList();
	}

	/// <summary>The room owning an interior floor tile, or -1 for corridors, thresholds, void and out-of-bounds tiles.</summary>
	public int RoomIdAt(Vector2I tile) => _tileToRoom.TryGetValue(tile, out var id) ? id : -1;

	/// <summary>An unreachable tile center and nearby props make a tile unusable for spawns and loot.</summary>
	internal bool IsOccupied(int x, int z)
	{
		RefreshPropTiles();
		return IsOccupiedNow(x, z);
	}

	/// <summary><see cref="IsOccupied"/> for loops that refreshed the prop tiles once up front.</summary>
	private bool IsOccupiedNow(int x, int z)
	{
		var tile = new Vector2I(x, z);
		return !IsReachable(Map.TileToWorld(x, z)) || _propTiles.Contains(tile);
	}

	/// <summary>
	/// True within reach of the gatekeeper's minimum footprint at its site: a prop placed here would occupy one of the
	/// footprint's tiles (footprint radius, plus half a tile diagonal for the tiles it touches, plus prop reach).
	/// </summary>
	internal bool IsInGatekeeperArena(Vector3 point) => GatekeeperSite is Transform3D site && Encounter?.GatekeeperFit is { } fit
		&& point.HorizontalDistanceTo(site.Origin) < fit.MinimumClearance + Room.TileSize * Mathf.Sqrt2 / 2 + SpawnPropClearance;

	/// <summary>
	/// Every open (room or corridor), unoccupied tile's world position -- shared by enemy spawning and trap placement,
	/// both of which are happy to land on a chokepoint.
	/// </summary>
	internal List<Vector3> OpenTileCandidates()
	{
		var candidates = new List<Vector3>();
		RefreshPropTiles();
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				if ((Map.IsRoom(x, z) || Map.IsCorridor(x, z)) && !IsOccupiedNow(x, z))
				{
					candidates.Add(Map.TileToWorld(x, z));
				}
			}
		}

		return candidates;
	}

	/// <summary>Keeps a spawn clear of the arrival, of the points already <paramref name="taken"/> and of progression anchors.</summary>
	internal bool IsBlockedSpawnPoint(Vector3 point, IReadOnlyList<Vector3> taken)
	{
		if (Arrival != null && point.HorizontalDistanceTo(Arrival.GlobalPosition) < SpawnArrivalClearance)
		{
			return true;
		}

		foreach (var other in taken)
		{
			if (point.HorizontalDistanceTo(other) < SpawnPointSpacing)
			{
				return true;
			}
		}

		foreach (var anchor in SpawnAnchors)
		{
			if (point.HorizontalDistanceTo(anchor) < SpawnAnchorClearance)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>Checks a room-local circular footprint against floor edges, decoration and authored props.</summary>
	public bool IsFootprintClear(Vector3 point, float radius, int roomId)
	{
		if (roomId < 0 || radius <= 0) return false;
		RefreshPropTiles();
		var center = Map.WorldToTile(point);
		int extent = Mathf.CeilToInt(radius / Room.TileSize + .5f);
		float half = Room.TileSize * .5f;
		for (int x = center.X - extent; x <= center.X + extent; x++)
		{
			for (int z = center.Y - extent; z <= center.Y + extent; z++)
			{
				Vector3 tileCenter = Map.TileToWorld(x, z);
				float dx = Mathf.Max(0, Mathf.Abs(point.X - tileCenter.X) - half);
				float dz = Mathf.Max(0, Mathf.Abs(point.Z - tileCenter.Z) - half);
				if (dx * dx + dz * dz >= radius * radius) continue;
				if (RoomIdAt(new Vector2I(x, z)) != roomId || IsOccupiedNow(x, z)) return false;
			}
		}
		return IsReachableArea(point, radius);
	}

	/// <summary>Prefer a roomy room interior near <paramref name="reference"/>, keeping an oversized actor out of corridors.</summary>
	public bool TryFindLargeEncounterPosition(Vector3 reference, float minimumRadius, float preferredRadius, out Vector3 position)
	{
		position = default;
		float best = float.NegativeInfinity;
		var occupied = new List<Vector3>();
		var accessible = FreelyAccessibleTiles();
		foreach (var tilePoint in OpenTileCandidates())
		{
			// Half-tile offsets also find clear 2x2 spaces between cell centers.
			for (int dx = 0; dx <= 1; dx++)
			{
				for (int dz = 0; dz <= 1; dz++)
				{
					var point = tilePoint + new Vector3(dx * Room.TileSize * .5f, 0, dz * Room.TileSize * .5f);
					var tile = Map.WorldToTile(point);
					int roomId = RoomIdAt(tile);
					if (accessible != null && !accessible.Contains(tile)
						|| IsBlockedSpawnPoint(point, occupied)
						|| !IsFootprintClear(point, minimumRadius, roomId)) continue;
					float clearance = minimumRadius;
					for (float radius = minimumRadius + .2f; radius <= preferredRadius; radius += .2f)
					{
						if (!IsFootprintClear(point, radius, roomId)) break;
						clearance = radius;
					}
					float score = clearance * 1000 - point.DistanceTo(reference);
					if (score <= best) continue;
					best = score;
					position = point;
				}
			}
		}
		return float.IsFinite(best);
	}

	/// <summary>Tiles reachable from the arrival without crossing a locked or undiscovered door; null without an arrival.
	/// Optional access must not strand teleport arrivals or hide required encounter placement.</summary>
	internal HashSet<Vector2I> FreelyAccessibleTiles()
	{
		if (Arrival == null) return null;
		var blocked = _doors.Where(d => d.Door.Lock?.IsLocked == true || !d.Door.IsDiscovered).Select(d => d.Connector).ToHashSet();
		return AccessSolutionValidator.Reachable(Map, Map.WorldToTile(Arrival.GlobalPosition), blocked);
	}

	/// <summary>
	/// Picks a uniformly random free floor/corridor tile anywhere on the map, for effects like a teleport scroll that
	/// relocate the player rather than spawn near a point. Uses its own RNG rather than the run's seeded loot RNG, since
	/// a teleport's outcome should not consume or depend on the loot-roll sequence.
	/// </summary>
	public bool TryPickRandomFreePosition(out Vector3 position)
	{
		var accessible = FreelyAccessibleTiles();
		List<Vector3> candidates = OpenTileCandidates()
			.Where(p => accessible == null || accessible.Contains(Map.WorldToTile(p))).ToList();
		if (candidates.Count == 0)
		{
			position = default;
			return false;
		}

		var rng = new RandomNumberGenerator();
		rng.Randomize();
		for (int i = candidates.Count - 1; i > 0; i--)
		{
			int j = rng.RandiRange(0, i);
			(candidates[i], candidates[j]) = (candidates[j], candidates[i]);
		}

		foreach (Vector3 candidate in candidates)
		{
			if (IsSpawnPositionFree(candidate))
			{
				position = candidate;
				return true;
			}
		}

		position = default;
		return false;
	}

	/// <summary>
	/// Finds a free world position close to <paramref name="origin"/> for a runtime spawn. Probes the origin first, then
	/// samples points on expanding rings out to <paramref name="maxRadius"/> world units, returning the nearest one that
	/// sits on a room/corridor floor tile whose column is clear of any solid collider (walls, props, stairs, transition
	/// blockers, etc.). Returns <paramref name="origin"/> when nothing nearby is free, so the item still drops next to
	/// the player rather than teleporting across the map.
	/// </summary>
	public Vector3 FindFreePositionNear(Vector3 origin, float maxRadius = 6f)
	{
		if (IsSpawnPositionFree(origin))
		{
			return origin;
		}

		const int samplesPerRing = 12;
		const float ringStep = 1.0f;
		for (float radius = ringStep; radius <= maxRadius; radius += ringStep)
		{
			for (int i = 0; i < samplesPerRing; i++)
			{
				float angle = Mathf.Tau * i / samplesPerRing;
				var candidate = new Vector3(
					origin.X + Mathf.Cos(angle) * radius,
					origin.Y,
					origin.Z + Mathf.Sin(angle) * radius);

				if (IsSpawnPositionFree(candidate))
				{
					return candidate;
				}
			}
		}

		return origin;
	}

	/// <summary>Headings a thrown drop tries, in degrees from its preferred one: the closest turns first.</summary>
	private static readonly float[] DropTurns = [0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f];
	/// <summary>Shares of the full throw distance a drop tries along each heading, the full throw first.</summary>
	private static readonly float[] DropReaches = [1f, 0.7f, 0.4f];
	/// <summary>Height of the sight line from a drop's source to its landing spot: clear above a chest's body.</summary>
	private const float DropSightHeight = 1.0f;

	/// <summary>
	/// Where an item thrown from <paramref name="source"/> toward <paramref name="direction"/> lands: the first free item
	/// spot in sight of the source, trying the full <paramref name="distance"/> first, then shorter throws, then headings
	/// turned progressively further away. Returns the source itself when nothing nearby qualifies, so a drop falls in
	/// place rather than through a wall or behind one, where the magnet could not reach it.
	/// </summary>
	public Vector3 FindDropLanding(Vector3 source, Vector3 direction, float distance)
	{
		World3D world = _world.GetWorld3D();
		Vector3 preferred = (direction with { Y = 0 }).Normalized();
		if (world == null || preferred == Vector3.Zero)
		{
			return source;
		}

		Vector3 sight = Vector3.Up * DropSightHeight;
		foreach (float turn in DropTurns)
		{
			Vector3 heading = preferred.Rotated(Vector3.Up, Mathf.DegToRad(turn));
			foreach (float reach in DropReaches)
			{
				Vector3 candidate = source + heading * (distance * reach);
				if (IsSpawnPositionFree(candidate) && WorldSight.IsClear(world, source + sight, candidate + sight))
				{
					return candidate;
				}
			}
		}

		return source;
	}

	private bool IsSpawnPositionFree(Vector3 worldPosition)
	{
		return IsWalkableMapPosition(worldPosition) && IsReachable(worldPosition) && !IsColumnObstructed(worldPosition);
	}

	private bool IsWalkableMapPosition(Vector3 worldPosition)
	{
		var tile = Map.WorldToTile(worldPosition);
		return Map.IsWithinBounds(tile.X, tile.Y) && (Map.IsRoom(tile.X, tile.Y) || Map.IsCorridor(tile.X, tile.Y));
	}

	/// <summary>
	/// Returns true when a solid collider occupies the column at <paramref name="worldPosition"/>. This is a physics
	/// overlap against the world, walls, and props layers, so it treats stairs, transition blockers, decorations, and
	/// props uniformly as "occupied" without enumerating each object category. The probe is item-sized and lifted off
	/// the floor so the flat floor collider is ignored and free space right beside an obstacle still qualifies.
	/// </summary>
	internal bool IsColumnObstructed(Vector3 worldPosition)
	{
		PhysicsDirectSpaceState3D space = _world.GetWorld3D()?.DirectSpaceState;
		if (space == null)
		{
			return false;
		}

		_columnQuery ??= new PhysicsShapeQueryParameters3D
		{
			Shape = new BoxShape3D { Size = new Vector3(0.8f, 1.6f, 0.8f) },
			CollisionMask = SpawnObstructionMask,
			CollideWithBodies = true,
			CollideWithAreas = false,
		};
		_columnQuery.Transform = new Transform3D(Basis.Identity, worldPosition + Vector3.Up * 1.0f);
		return space.IntersectShape(_columnQuery, 1).Count > 0;
	}

	/// <summary>
	/// Keeps the tiles within <see cref="SpawnPropClearance"/> of a "prop" node current. Large actors ask about them every
	/// physics frame, so they are cached. Generation only adds props, each positioned as it enters the tree, so while the
	/// floor is live a changed group size is the only staleness; a frozen floor also refreshes once per physics frame,
	/// because runtime props move, drop and get picked up.
	/// </summary>
	private void RefreshPropTiles()
	{
		SceneTree tree = _world.GetTree();
		int count = tree.GetNodeCountInGroup("prop");
		ulong frame = IsFrozen ? Engine.GetPhysicsFrames() : 0;
		if (count == _propTilesCount && frame == _propTilesFrame)
		{
			return;
		}

		RebuildPropTiles(tree);
		_propTilesCount = count;
		_propTilesFrame = frame;
	}

	private void RebuildPropTiles(SceneTree tree)
	{
		_propTiles.Clear();
		int reach = Mathf.CeilToInt(SpawnPropClearance / Room.TileSize);
		foreach (Node node in tree.GetNodesInGroup("prop"))
		{
			if (node is not Node3D prop)
			{
				continue;
			}

			Vector2I tile = Map.WorldToTile(prop.GlobalPosition);
			for (int x = tile.X - reach; x <= tile.X + reach; x++)
			{
				for (int z = tile.Y - reach; z <= tile.Y + reach; z++)
				{
					if (((Vector3)Map.TileToWorld(x, z)).HorizontalDistanceTo(prop.GlobalPosition) < SpawnPropClearance)
					{
						_propTiles.Add(new Vector2I(x, z));
					}
				}
			}
		}
	}
}