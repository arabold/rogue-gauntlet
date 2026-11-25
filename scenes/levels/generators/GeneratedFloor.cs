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
	private readonly GridMap _decoration;
	private readonly List<RoomRegion> _rooms = new();
	// Interior floor tile -> room id, for "which room is this in" queries.
	private readonly Dictionary<Vector2I, int> _tileToRoom = new();
	private readonly List<(Door Door, Vector2I Connector)> _doors = new();
	private readonly List<ShopComponent> _shops = new();
	private WalkabilityGrid _walkability;
	private bool[] _reachable;
	private readonly Dictionary<string, (Door Door, DoorAccessSaveData Baseline)> _doorBaselines = new();
	private List<FloorTrap> _traps;
	private List<TrapGauntlet> _gauntlets = [];
	/// <summary>The tiles of every room holding a trap gauntlet, kept free of spawns and loot.</summary>
	private readonly HashSet<Vector2I> _gauntletTiles = [];
	private HashSet<Vector2I> _decoratedTiles;
	private readonly HashSet<Vector2I> _propTiles = new();
	private ulong _propTilesFrame = ulong.MaxValue;
	private int _propTilesCount = -1;
	private PhysicsShapeQueryParameters3D _columnQuery;

	/// <param name="world">The generator: the physics space, scene tree and the subtree holding progression anchors.</param>
	/// <param name="decoration">Read live until <see cref="Freeze"/>.</param>
	public GeneratedFloor(MapData map, Node3D world, GridMap decoration)
	{
		Map = map;
		_world = world;
		_decoration = decoration;
	}

	public MapData Map { get; }
	/// <summary>Full floor regions, including connector thresholds, indexed by room id.</summary>
	public IReadOnlyList<RoomRegion> Rooms => _rooms;
	/// <summary>Exterior doors paired with the connector they guard; interior gates are not listed.</summary>
	public IReadOnlyList<(Door Door, Vector2I Connector)> Doors => _doors;
	/// <summary>The descent arrival the floor is built around; null only for a layout without one.</summary>
	public PlayerSpawnPoint Arrival { get; internal set; }
	/// <summary>The arrival from the next floor, whose landing marks the walk-on end of the down stairs.</summary>
	public PlayerSpawnPoint ArrivalFromBelow { get; internal set; }
	/// <summary>The exit down to the next floor; null for a layout without one.</summary>
	public LevelTransitionTrigger Descent { get; internal set; }
	/// <summary>This floor's special encounters, decided before generation places anything; null on floors without any.</summary>
	public EncounterPlan Encounter { get; internal set; }
	/// <summary>Where the gatekeeper spawns, facing included: an authored marker, or the roomiest fit nearest the descent.
	/// Resolved once, before access and loot; null when the floor has no gatekeeper or it takes an ordinary point.</summary>
	public Transform3D? GatekeeperSite { get; internal set; }
	/// <summary>The spawn point that brings the gatekeeper in; null on floors without one.</summary>
	public SpawnPoint GatekeeperSpawn { get; internal set; }
	/// <summary>The shops generation stood on this floor; their keepers are solid props under the floor content.</summary>
	public IReadOnlyList<ShopComponent> Shops => _shops;
	/// <summary>Whether <paramref name="spawn"/> places one of this floor's nests rather than a monster.</summary>
	public bool IsNest(SpawnPoint spawn) => Encounter?.Nest != null && spawn.Scenes.Count > 0 && spawn.Scenes[0] == Encounter.Nest;
	public bool IsFrozen { get; private set; }
	/// <summary>Every door's generated access state, keyed by its stable connection id: what exploration saves diff against.</summary>
	public IReadOnlyDictionary<string, (Door Door, DoorAccessSaveData Baseline)> DoorBaselines => _doorBaselines;
	/// <summary>Authored and generated floor traps, collected when the floor freezes.</summary>
	public IReadOnlyList<FloorTrap> Traps => _traps ?? [];
	/// <summary>The floor's trap gauntlets, collected once rooms and corridors are decorated.</summary>
	public IReadOnlyList<TrapGauntlet> Gauntlets => _gauntlets;
	/// <summary>World XZ outlines of the solid wall pieces, from their mesh footprints rather than collision hulls: a corner
	/// counts as its two arms, so a room's inner corner stays open. Set once the walls are final.</summary>
	public IReadOnlyList<Vector2[]> SolidWalls { get; internal set; } = [];
	/// <summary>World XZ centerlines of fixed structures inside rooms, including cell walls and GridMap dividers.</summary>
	internal IReadOnlyList<(Vector2 Start, Vector2 End)> InteriorWallSegments { get; set; } = [];

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

	internal void AddDoor(Door door, Vector2I connector)
	{
		RequireLive();
		_doors.Add((door, connector));
	}

	/// <summary>Records a placed keeper's shop. Its spot becomes a spawn anchor, so no pack spawns beside the counter.</summary>
	internal void AddShop(ShopComponent shop, Vector3 position)
	{
		RequireLive();
		_shops.Add(shop);
		SpawnAnchors = [.. SpawnAnchors, position];
	}

	/// <summary>Records a door's seeded access state once its connection id is final.</summary>
	internal void RecordDoorBaseline(Door door)
	{
		RequireLive();
		_doorBaselines.Add(door.ConnectionId, (door, door.CaptureAccess()));
	}

	/// <summary>Actor-center walkability of the finished geometry, built once rooms and corridors exist.
	/// Null in previews and layouts without an arrival; queries then fall back to tile occupancy alone.</summary>
	internal WalkabilityGrid Walkability => _walkability;
	/// <summary>Per-cell reachability for <see cref="Walkability"/>; null alongside it.</summary>
	internal bool[] Reachable => _reachable;

	/// <summary>The frozen clearance grid walking monsters navigate on; null in previews and layouts generated without gameplay.</summary>
	public ClearanceGrid Clearance { get; private set; }
	/// <summary>The frozen grid flyers navigate on (<see cref="NavigationLayer.Air"/>): pits are open and low solids pass under them.</summary>
	public ClearanceGrid AirClearance { get; private set; }
	/// <summary>Every door, interior gates included, in gate order: gate i of either grid is door i.</summary>
	public IReadOnlyList<Door> NavigationGates { get; private set; } = [];

	/// <summary>Records the navigation grids once every solid is placed.</summary>
	internal void SetNavigation(ClearanceGrid ground, ClearanceGrid air, IReadOnlyList<Door> gates)
	{
		RequireLive();
		Clearance = ground;
		AirClearance = air;
		NavigationGates = gates;
	}

	/// <summary>The grid <paramref name="layer"/> navigates on.</summary>
	public ClearanceGrid ClearanceFor(NavigationLayer layer) => layer == NavigationLayer.Air ? AirClearance : Clearance;

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

	/// <summary>
	/// Caches decorated tiles once rooms and corridors have finished adding GridMap decorations, and collects the trap
	/// gauntlets with their rooms' tiles.
	/// </summary>
	internal void FreezeDecorations()
	{
		if (_decoratedTiles != null) return;
		_gauntlets = _world.FindChildren("*", "", true, false).OfType<TrapGauntlet>().ToList();
		foreach (var gauntlet in _gauntlets)
			if (gauntlet.GetAncestorOrNull<Room>()?.RegionId is int roomId and >= 0) _gauntletTiles.UnionWith(_rooms[roomId].Tiles);
		var decorated = new HashSet<Vector2I>();
		int half = Room.TileSize / 2;
		foreach (Vector3I cell in _decoration.GetUsedCells())
		{
			// A tile owns the cells within half a tile of its center on the floor layer, as HasDecorationInTile scans them.
			if (cell.Y != 0 || !IsSolidDecoration(cell)) continue;
			decorated.Add(new Vector2I(Mathf.FloorToInt((cell.X + half) / (float)Room.TileSize) + Map.Width / 2,
				Mathf.FloorToInt((cell.Z + half) / (float)Room.TileSize) + Map.Height / 2));
		}
		_decoratedTiles = decorated;
	}

	public void Freeze()
	{
		RequireLive();
		FreezeDecorations();
		_traps = _world.FindChildren("*", "", true, false).OfType<FloorTrap>().Where(trap => !trap.IsQueuedForDeletion()).ToList();
		IsFrozen = true;
	}

	/// <summary>The room owning an interior floor tile, or -1 for corridors, thresholds, void and out-of-bounds tiles.</summary>
	public int RoomIdAt(Vector2I tile) => _tileToRoom.TryGetValue(tile, out var id) ? id : -1;
	public int RoomIdAt(Vector3 point) => RoomIdAt(Map.WorldToTile(point));

	/// <summary>
	/// An unreachable tile center, decoration, nearby props and a trap gauntlet's room make a tile unusable for spawns and
	/// loot: a gauntlet is crossed on timing alone, with nothing to fight or pick up under its blades.
	/// </summary>
	internal bool IsOccupied(int x, int z)
	{
		RefreshPropTiles();
		return IsOccupiedNow(x, z);
	}

	/// <summary><see cref="IsOccupied"/> for loops that called <see cref="RefreshPropTiles"/> once up front.</summary>
	internal bool IsOccupiedNow(int x, int z)
	{
		var tile = new Vector2I(x, z);
		return !IsReachable(Map.TileToWorld(x, z)) || HasDecorationInTile(x, z) || _propTiles.Contains(tile) || _gauntletTiles.Contains(tile);
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
	internal bool IsBlockedSpawnPoint(Vector3 point, IReadOnlyList<Vector3> taken) =>
		IsInSpawnClearance(point) || taken.Any(other => point.HorizontalDistanceTo(other) < SpawnPointSpacing);

	/// <summary>Too close to where the hero arrives, or to a stair or progression anchor, for any monster to spawn.</summary>
	internal bool IsInSpawnClearance(Vector3 point) =>
		Arrival != null && point.HorizontalDistanceTo(Arrival.GlobalPosition) < SpawnArrivalClearance
		|| SpawnAnchors.Any(anchor => point.HorizontalDistanceTo(anchor) < SpawnAnchorClearance);

	/// <summary>
	/// A spot for one more pack member near <paramref name="anchor"/>: walkable for a monster's body, in the anchor's room
	/// (or corridor), within <paramref name="radius"/> and at least <paramref name="spacing"/> from every member already
	/// placed. Like the anchor, it stays off props and decoration and out of the spawn clearances. Rings widen outward from
	/// a random start angle; false when the room has no space left that close.
	/// </summary>
	public bool TryPlacePackMember(Vector3 anchor, IReadOnlyList<Vector3> pack, float radius, float spacing,
		RandomNumberGenerator rng, out Vector3 point)
	{
		int room = RoomIdAt(anchor);
		RefreshPropTiles();
		float start = rng.Randf() * Mathf.Tau;
		for (float ring = spacing; ring <= radius; ring += spacing * .5f)
		{
			int samples = Mathf.Max(6, Mathf.CeilToInt(Mathf.Tau * ring / spacing));
			for (int i = 0; i < samples; i++)
			{
				float angle = start + Mathf.Tau * i / samples;
				var candidate = new Vector3(anchor.X + Mathf.Cos(angle) * ring, anchor.Y, anchor.Z + Mathf.Sin(angle) * ring);
				// The grid is built for the largest ordinary body, so a reachable cell has room for any pack member.
				var tile = Map.WorldToTile(candidate);
				if (RoomIdAt(candidate) != room || !IsWalkableMapPosition(candidate) || !IsReachable(candidate)
					|| !Map.HasWorldSight(anchor, candidate) || IsOccupiedNow(tile.X, tile.Y) || IsInSpawnClearance(candidate)) continue;
				if (pack.Any(other => candidate.HorizontalDistanceTo(other) < spacing)) continue;
				point = candidate;
				return true;
			}
		}
		point = anchor;
		return false;
	}

	/// <summary>Checks a room-local circular footprint against floor edges, decoration and authored props.</summary>
	public bool IsFootprintClear(Vector3 point, float radius, int roomId)
	{
		if (roomId < 0 || radius <= 0) return false;
		RefreshPropTiles();
		return IsFootprintClearNow(point, radius, roomId);
	}

	private bool IsFootprintClearNow(Vector3 point, float radius, int roomId)
	{
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

	/// <summary>
	/// Prefer a roomy room interior near <paramref name="reference"/>, keeping an oversized actor out of corridors and the
	/// arrival's room. A floor with a boss slot keeps it to the boss room or the descent's room behind it.
	/// </summary>
	public bool TryFindLargeEncounterPosition(
		Vector3 reference,
		float minimumRadius,
		float preferredRadius,
		out Vector3 position,
		Func<int, bool> allowedRoom = null)
	{
		position = default;
		if (minimumRadius <= 0) return false;
		float best = float.NegativeInfinity;
		var accessible = ProgressionAccessibleTiles();
		if (allowedRoom == null)
		{
			int arrivalRoomId = Arrival?.GetAncestorOrNull<Room>()?.RegionId ?? -1;
			int descentRoomId = Descent?.GetAncestorOrNull<Room>()?.RegionId ?? -1;
			bool hasBossSlot = _rooms.Any(r => r.SlotId == "boss");
			allowedRoom = roomId => roomId >= 0 && roomId < _rooms.Count
				&& roomId != arrivalRoomId
				&& (!hasBossSlot || _rooms[roomId].SlotId == "boss" || roomId == descentRoomId);
		}

		// No candidate scores above its widest measured clearance, so that bounds the score before measuring.
		float widest = Mathf.Max(minimumRadius, preferredRadius) * 1000f;
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
					if (!allowedRoom(roomId) || (accessible != null && !accessible.Contains(tile))
						|| IsInSpawnClearance(point)) continue;
					float dist = point.DistanceTo(reference);
					if (widest - dist <= best) continue;
					if (!IsFootprintClearNow(point, minimumRadius, roomId)) continue;
					float clearance = minimumRadius;
					for (float radius = minimumRadius + .2f; radius <= preferredRadius; radius += .2f)
					{
						if (!IsFootprintClearNow(point, radius, roomId)) break;
						clearance = radius;
					}
					float score = clearance * 1000f - dist;
					if (score <= best) continue;
					best = score;
					position = point;
				}
			}
		}
		return float.IsFinite(best);
	}

	/// <summary>Tiles reachable after discovering secrets, without crossing locked doors; null without an arrival.</summary>
	private HashSet<Vector2I> ProgressionAccessibleTiles()
	{
		if (Arrival == null) return null;
		var blocked = _doors.Where(d => d.Door.Lock?.IsLocked == true).Select(d => d.Connector).ToHashSet();
		return AccessSolutionValidator.Reachable(Map, Map.WorldToTile(Arrival.GlobalPosition), blocked);
	}

	/// <summary>Tiles reachable without crossing a locked or undiscovered door; null without an arrival.
	/// This prevents teleport arrivals from stranding the player behind an undiscovered door.</summary>
	internal HashSet<Vector2I> FreelyAccessibleTiles()
	{
		if (Arrival == null) return null;
		var blocked = _doors.Where(d => d.Door.Lock?.IsLocked == true || !d.Door.IsDiscovered).Select(d => d.Connector).ToHashSet();
		return AccessSolutionValidator.Reachable(Map, Map.WorldToTile(Arrival.GlobalPosition), blocked);
	}

	/// <summary>
	/// Picks a uniformly random free floor/corridor tile anywhere on the map using <paramref name="rng"/>, for effects
	/// like a teleport scroll that relocate the player rather than spawn near a point.
	/// </summary>
	public bool TryPickRandomFreePosition(RandomNumberGenerator rng, out Vector3 position)
	{
		var accessible = FreelyAccessibleTiles();
		List<Vector3> candidates = OpenTileCandidates()
			.Where(p => accessible == null || accessible.Contains(Map.WorldToTile(p))).ToList();
		if (candidates.Count == 0)
		{
			position = default;
			return false;
		}

		GenerationRandom.Shuffle(candidates, rng);

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
	public Vector3 FindFreePositionNear(Vector3 origin, float maxRadius = 6f) => FindFreePositionNear(origin, maxRadius, null);

	/// <summary>As <see cref="FindFreePositionNear(Vector3, float)"/>, taking only ring points <paramref name="accept"/> allows.</summary>
	private Vector3 FindFreePositionNear(Vector3 origin, float maxRadius, System.Func<Vector3, bool> accept)
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

				if (IsSpawnPositionFree(candidate) && accept?.Invoke(candidate) != false)
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
	/// turned progressively further away, then the nearest free spot in sight (a source over water). Returns the source
	/// itself when nothing nearby qualifies, so a drop falls in place rather than through a wall or behind one, where the
	/// magnet could not reach it.
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

		// Nothing in throwing range: over water a flyer's drop lands on the nearest dry floor no wall stands in front of
		// instead. Falling from above the water, it clears the railings along a bank.
		return FindFreePositionNear(source, 6f, candidate => WorldSight.IsClear(world, source + sight, candidate + sight, mask: WorldSight.Structure));
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

	/// <summary>Decoration with collision occupies its tile; walk-over decoration such as rugs does not.</summary>
	private bool IsSolidDecoration(Vector3I cell)
	{
		int item = _decoration.GetCellItem(cell);
		return item >= 0 && _decoration.MeshLibrary.GetItemShapes(item).Count > 0;
	}

	private bool HasDecorationInTile(int x, int z)
	{
		if (_decoratedTiles != null)
		{
			return _decoratedTiles.Contains(new Vector2I(x, z));
		}

		var center = Map.TileToWorld(x, z);
		int half = Room.TileSize / 2;
		for (int dx = -half; dx < half; dx++)
		{
			for (int dz = -half; dz < half; dz++)
			{
				if (IsSolidDecoration(new Vector3I(center.X + dx, center.Y, center.Z + dz)))
				{
					return true;
				}
			}
		}

		return false;
	}

	/// <summary>
	/// Keeps the tiles within <see cref="SpawnPropClearance"/> of a "prop" node current. Placement loops ask about them
	/// many times, so they are cached. Generation only adds props, each positioned as it enters the tree, so while the
	/// floor is live a changed group size is the only staleness; a frozen floor also refreshes once per physics frame,
	/// because runtime props move, drop and get picked up.
	/// </summary>
	internal void RefreshPropTiles()
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