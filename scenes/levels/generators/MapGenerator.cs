using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;

[Tool]
public partial class MapGenerator : Node3D
{
	[Export] public uint DungeonDepth { get; set => SetPropertyWithBounds<uint>(ref field, value, 1, 100); } = 1;
	[Export] public uint MapWidth { get; set => SetPropertyWithBounds<uint>(ref field, value, 20, 100); } = 20;
	[Export] public uint MapDepth { get; set => SetPropertyWithBounds<uint>(ref field, value, 20, 100); } = 20;
	[Export] public uint MaxRooms { get; set => SetPropertyWithBounds<uint>(ref field, value, 1, 100); } = 10;
	[Export] public ulong Seed { get; set => SetProperty(ref field, value); } = 42;

	[Export] public RoomLayoutStrategy RoomLayout { get; set => SetProperty(ref field, value); }
	[Export] public CorridorConnectorStrategy CorridorConnector { get; set => SetProperty(ref field, value); }
	[Export] public RoomFactory RoomFactory { get; set => SetProperty(ref field, value); }
	[Export] public MobFactory MobFactory { get; set => SetProperty(ref field, value); }
	[Export] public RoomArrangementPool ArrangementPool { get; set; }
	/// <summary>Disable all optional furnishing without changing shell/layout RNG, including coded recipes.</summary>
	[Export] public bool EnableFurnishings { get; set; } = true;
	public int ArrangementCount { get; private set; }
	private readonly HashSet<Vector2I> _arrangementReservations = new();

	[Export] public TileFactory TileFactory { get; set => SetProperty(ref field, value); }

	/// <summary>Chest prop placed against a room wall, facing into the room. Unset skips chest placement.</summary>
	[Export] public PackedScene ChestScene { get; set => SetProperty(ref field, value); }
	[Export(PropertyHint.Range, "0,10")] public int MinChests { get; set => SetProperty(ref field, value); } = 0;
	[Export(PropertyHint.Range, "0,10")] public int MaxChests { get; set => SetProperty(ref field, value); } = 2;

	/// <summary>Floor trap prop, placeable on any open room/corridor tile. Unset skips trap placement.</summary>
	[Export] public PackedScene TrapScene { get; set => SetProperty(ref field, value); }
	[Export(PropertyHint.Range, "0,10")] public int MinTraps { get; set => SetProperty(ref field, value); } = 0;
	[Export(PropertyHint.Range, "0,10")] public int MaxTraps { get; set => SetProperty(ref field, value); } = 3;

	/// <summary>Loose ground-item prop. Unset skips loose-item placement.</summary>
	[Export] public PackedScene LooseItemScene { get; set => SetProperty(ref field, value); }
	/// <summary>Weighted pool loose items are rolled from. Unset skips loose-item placement.</summary>
	[Export] public ItemCreationPolicy ItemCreationPolicy { get; set; }
	[Export] public LootTable LooseItemLootTable { get; set => SetProperty(ref field, value); }
	[Export(PropertyHint.Range, "0,10")] public int MinLooseItems { get; set => SetProperty(ref field, value); } = 0;
	[Export(PropertyHint.Range, "0,10")] public int MaxLooseItems { get; set => SetProperty(ref field, value); } = 4;

	public MapData Map;
	public GridMap FloorGridMap { get; private set; }
	public GridMap WallGridMap { get; private set; }
	public GridMap DecorationGridMap { get; private set; }
	public GridMap OcclusionGridMap { get; private set; }
	public NavigationRegion3D NavigationRegion { get; private set; }

	// Lazily-built translucent overlay of the baked navigation mesh, toggled by the debug menu.
	private MeshInstance3D _navigationDebugMesh;

	private readonly List<RoomRegion> _roomRegions = new();
	/// <summary>Full floor regions, including connector thresholds, for local generation diagnostics.</summary>
	internal IReadOnlyList<RoomRegion> RoomRegions => _roomRegions;
	// Interior floor tile -> room id, for "which room is the player in" detection.
	private readonly System.Collections.Generic.Dictionary<Vector2I, int> _tileToRoom = new();
	// Connectors guarded by a closed door; block sight and logical reachability.
	private readonly HashSet<Vector2I> _dooredConnectors = new();
	private readonly HashSet<Vector2I> _revealedTiles = new();
	// Shared unlit black cap mesh for permanent void coverage.
	private PlaneMesh _occlusionCapMesh;
	// Doors paired with the connector they guard, for toggling their x-ray indicator.
	private readonly List<(Door Door, Vector2I Connector)> _doorIndicators = new();
	// Doorway markers paired with the connector they mark, so a marker whose doorway
	// never got a corridor routed to it (see FinalizeDoors) can be hidden instead of
	// misleadingly pointing at what is now a solid generated wall.
	private readonly List<(DoorwayMarker Marker, Vector2I Connector)> _markerIndicators = new();

	private static readonly Vector2I[] CardinalOffsets =
	{
		new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
	};

	// FIXME: Centralize the tile size to avoid hardcoding it in multiple places
	/// <summary>
	/// The size of each tile in the base map when translating to the GridMaps.
	/// </summary>
	public readonly uint TileSize = 4;

	/// <summary>
	/// Y level the flat void occluder caps sit at (slightly above wall-top
	/// height at 4.0f to prevent z-fighting with the wall copings underneath).
	/// A horizontal cap has no tall front face, so it hides the void
	/// without clipping rooms behind it in the isometric view. Tune visually.
	/// </summary>
	public const float OcclusionCapHeight = 4.02f;

	private const int OcclusionItemId = 0;
	private const float EnemySpawnPlayerClearance = 20f;
	private const float EnemySpawnPointSpacing = 5f;
	private const float EnemySpawnBlockedAreaClearance = 7f;
	private const float EnemySpawnPropClearance = 2f;

	// Smaller than the enemy-spawn equivalents: loot close to the entrance is fine, and a
	// tight level shouldn't run out of valid spots just to keep loot far from the player.
	// Internal (not private) so LootPlacementTest can assert against the real thresholds.
	internal const float LootSpawnPlayerClearance = 10f;
	internal const float LootSpawnSpacing = 6f;

	// Physics layers a runtime spawn must stay clear of: world (1) | walls (2) | props (5).
	// Keeps items out of stairs, transition blockers, walls, and props via a single overlap test.
	private const uint SpawnObstructionMask = WorldSight.Blockers;
	private const uint EffectLandingSurfaceMask = 1u;
	private const float EffectLandingRayHeight = 10.0f;
	private const float EffectLandingRayDepth = 14.0f;

	/// <summary>
	/// Extra rows/columns of occluder caps placed beyond the map bounds so the
	/// outer walls of edge rooms are hidden against the void.
	/// </summary>
	private const int OcclusionMargin = 2;

	public PlayerSpawnPoint PlayerSpawnPoint;
	public Array<SpawnPoint> EnemySpawnPoints;

	public override void _Ready()
	{
		GD.Print("Initializing map generator...");
		FloorGridMap = GetNode<GridMap>("FloorGridMap");
		WallGridMap = GetNode<GridMap>("WallGridMap");
		DecorationGridMap = GetNode<GridMap>("DecorationGridMap");
		OcclusionGridMap = GetNode<GridMap>("OcclusionGridMap");
		OcclusionGridMap.MeshLibrary = BuildOcclusionMeshLibrary();
		NavigationRegion = GetNode<NavigationRegion3D>("NavigationRegion3D");

		if (Engine.IsEditorHint())
		{
			GenerateMap(false);
		}
	}

	protected void SetProperty<T>(ref T field, T value)
	{
		field = value;

		// Generate the map when the properties are set in the editor
		if (Engine.IsEditorHint() && IsNodeReady())
		{
			GenerateMap(false);
		}
	}

	protected void SetPropertyWithBounds<T>(ref T field, T value, T min, T max) where T : IComparable<T>
	{
		if (Comparer<T>.Default.Compare(value, min) < 0 || Comparer<T>.Default.Compare(value, max) > 0) return;
		SetProperty(ref field, value);
	}

	protected void MergeRoomGridMaps(Room room, Vector3I placement)
	{
		// Generate the tile map from the room scene
		var roomFloorGridMap = room.FloorGridMap;
		var roomWallGridMap = room.WallGridMap;
		var roomDecorationGridMap = room.DecorationGridMap;

		var roomOffset = new Vector3I(room.Bounds.Position.X, 0, room.Bounds.Position.Y);
		var gridMapOffset = TileToWorld(placement) - roomOffset;
		MergeGridMaps(roomFloorGridMap, FloorGridMap, gridMapOffset);
		MergeGridMaps(roomWallGridMap, WallGridMap, gridMapOffset);
		MergeGridMaps(roomDecorationGridMap, DecorationGridMap, gridMapOffset);

		roomFloorGridMap.QueueFree();
		roomWallGridMap.QueueFree();
		roomDecorationGridMap.QueueFree();
		room.FloorGridMap = null;
		room.WallGridMap = null;
		room.DecorationGridMap = null;
	}

	private void MergeGridMaps(GridMap sourceGridMap, GridMap targetGridMap, Vector3I offset)
	{
		// Get all used cells in the source GridMap
		GD.Print($"Merging GridMaps from {sourceGridMap.Name} to {targetGridMap.Name}");
		foreach (Vector3I cell in sourceGridMap.GetUsedCells())
		{
			// Get the tile index at this cell
			int tileIndex = sourceGridMap.GetCellItem(cell);
			if (tileIndex != -1) // Skip empty cells
			{
				int transformIndex = sourceGridMap.GetCellItemOrientation(cell);

				// Apply the offset and copy the tile to the target GridMap
				Vector3I targetCell = cell + new Vector3I((int)offset.X, (int)offset.Y, (int)offset.Z);
				targetGridMap.SetCellItem(targetCell, tileIndex, transformIndex);
			}
		}
	}

	private LevelGenerationContext _random;
	public int GenerationAttempt { get; private set; }

	/// <summary>Recreates a reward stream without advancing generation or gameplay RNGs.</summary>
	public RandomNumberGenerator CreateSourceLootRng(string sourceId)
	{
		return GenerationRandom.Create(Seed, GenerationRandom.Loot, sourceId);
	}

	private void GenerateRooms()
	{
		GD.Print("Generating rooms...");

		var roomPlacements = RoomLayout.GenerateRooms(
			Map, RoomFactory, MaxRooms, _random.Layout);
		PlaceRooms(roomPlacements);
	}

	private void PlaceRooms(List<RoomPlacement> roomPlacements)
	{
		GD.Print("Placing rooms...");
		for (int roomIndex = 0; roomIndex < roomPlacements.Count; roomIndex++)
		{
			var placement = roomPlacements[roomIndex];
			var room = placement.Room;
			var traps = new List<(FloorTrap Trap, string Id)>();
			foreach (Node source in room.FindChildren("*", "", true, false))
			{
				if (source is FloorTrap trap) traps.Add((trap, $"depth:{DungeonDepth}:room:{roomIndex}:{room.GetPathTo(source)}"));
				if (source is LootableItem)
				{ source.SetMeta("persistent_id", $"depth:{DungeonDepth}:room:{roomIndex}:{room.GetPathTo(source)}"); }
				if (source is LootTableComponent loot)
				{
					loot.SourceId = $"depth:{DungeonDepth}:room:{roomIndex}:{room.GetPathTo(source)}";
				}
				if (source is SpawnPoint spawn && string.IsNullOrEmpty(spawn.PersistentId))
				{
					spawn.PersistentId = $"depth:{DungeonDepth}:room:{roomIndex}:{room.GetPathTo(source)}";
				}
			}
			var position = new Vector3I(placement.Position.X, 0, placement.Position.Y);
			MergeRoomGridMaps(room, position);

			// Make the room a child of the navigation region, so
			// it is included in the navigation mesh and enemies
			// avoid obstactles in it.
			NavigationRegion.AddChild(room);

			var roomOffset = new Vector3I(room.Bounds.Position.X, 0, room.Bounds.Position.Y);
			room.Translate(TileToWorld(position) - roomOffset);
			// Placed and merged: each authored trap can now find the floor cell it conceals.
			foreach (var (trap, id) in traps)
				trap.Initialize(id, Seed, FloorGridMap, FloorGridMap.LocalToMap(FloorGridMap.ToLocal(trap.GlobalPosition)));

			RegisterRoomRegion(placement);
		}
	}

	/// <summary>
	/// Records which master tiles belong to a placed room so the room can later be
	/// looked up by tile and revealed by its exact (possibly non-rectangular) shape.
	/// </summary>
	private void RegisterRoomRegion(RoomPlacement placement)
	{
		var roomMap = placement.Room.Map;
		if (roomMap == null)
		{
			return;
		}

		// A procedurally built room (ProceduralRoomBuilder.BuildRoom) is a fresh `new
		// Room` with no backing scene file; an authored room is instantiated from a
		// PackedScene, which sets SceneFilePath on the instantiated root.
		var region = new RoomRegion(_roomRegions.Count) { IsProcedural = string.IsNullOrEmpty(placement.Room.SceneFilePath), Purpose = placement.Room.Purpose,
			InteriorPool = string.IsNullOrEmpty(placement.Room.SceneFilePath) ? placement.Room.Generator?.CreateInteriorPool() : null,
			Dressing = string.IsNullOrEmpty(placement.Room.SceneFilePath) ? placement.Room.Generator?.Dressing : null, LayoutAxis = placement.Room.LayoutAxis };
		for (int lx = 0; lx < roomMap.Width; lx++)
		{
			for (int lz = 0; lz < roomMap.Height; lz++)
			{
				bool isRoom = roomMap.IsRoom(lx, lz);
				bool isConnector = roomMap.IsConnector(lx, lz);
				// Chasms (interior pits) reveal with the room but are not walkable.
				bool isChasm = roomMap.IsChasm(lx, lz);
				if (!isRoom && !isConnector && !isChasm)
				{
					continue;
				}

				var tile = new Vector2I(placement.Position.X + lx, placement.Position.Y + lz);
				region.Tiles.Add(tile);

				// Only interior floor tiles count as "the player is in this room".
				// Connectors are doorway thresholds: counting them would announce room entry
				// when the player merely stands against its closed door, since the
				// position rounds onto the connector tile.
				if (isRoom)
				{
					_tileToRoom[tile] = region.Id;
				}

				if (isConnector)
				{
					region.ConnectorTiles.Add(tile);
				}
			}
		}

		foreach (var slot in placement.Room.GetChildren().OfType<RoomFurnishingSlot>())
			region.FurnishingSlots.Add(slot.Resolve(region.Id, WorldToTile(slot.GlobalPosition), GlobalTransform.AffineInverse() * slot.GlobalTransform));
		RegisterDoors(placement.Room, region);
		_roomRegions.Add(region);
	}

	/// <summary>
	/// Marks the connectors guarded by hand-placed Door props so fog reveal stops
	/// at them, and separately tracks every DoorwayMarker so one whose doorway never
	/// gets a corridor routed to it (see FinalizeDoors) can be hidden rather than left
	/// pointing at a solid wall. Doors sit in the wall opening rather than exactly on a
	/// connector tile, so they're matched to the nearest connector of the room. Markers
	/// are authored exactly on their own tile's center (see the coordinate-convention
	/// docs in Room.cs), so they're matched to that exact tile directly -- proximity
	/// matching would let a marker whose OWN doorway failed validation (its tile never
	/// became a connector at all) borrow a nearby, unrelated doorway's connected status
	/// instead of correctly always reading as unconnected.
	/// </summary>
	private void RegisterDoors(Room room, RoomRegion region)
	{
		if (region.ConnectorTiles.Count == 0)
		{
			return;
		}

		foreach (Node node in room.FindChildren("*", "", true, false))
		{
			if (node is Door door)
			{
				// Candidates only: which doorways are real passages isn't known until
				// corridors have been routed (see FinalizeDoors).
				if (TryFindNearestConnector(region, door.GlobalPosition, out var connector))
				{
					_doorIndicators.Add((door, connector));
				}
			}
			else if (node is DoorwayMarker marker)
			{
				var tile = WorldToTile(marker.GlobalPosition);
				if (region.ConnectorTiles.Contains(tile))
				{
					_markerIndicators.Add((marker, tile));
				}
				else
				{
					// The marker's own doorway validation failed outright (its tile
					// never became a connector at all) -- known now, without waiting on
					// corridor routing, so disable it immediately rather than leaving it
					// enabled forever (FinalizeMarkers only ever visits tracked markers).
					marker.Enabled = false;
				}
			}
		}
	}

	private bool TryFindNearestConnector(RoomRegion region, Vector3 worldPosition, out Vector2I connector)
	{
		connector = default;
		float bestDistance = float.MaxValue;
		foreach (var candidate in region.ConnectorTiles)
		{
			var center = TileToWorld(candidate.X, 0, candidate.Y);
			float dx = worldPosition.X - center.X;
			float dz = worldPosition.Z - center.Z;
			float distance = dx * dx + dz * dz;
			if (distance < bestDistance)
			{
				bestDistance = distance;
				connector = candidate;
			}
		}

		// Doors sit in the wall opening, up to ~1.5 tiles from the connector floor
		// tile they guard (plus tile-center rounding), so accept the nearest match
		// within a 2-tile radius and reject doors with no connector near them.
		float maxDistance = TileSize * 2f;
		return bestDistance <= maxDistance * maxDistance;
	}

	/// <summary>
	/// Resolves hand-placed doors against the routed corridors. A door whose doorway
	/// was connected becomes a real gating door (its connector seals the fog); a door
	/// at a doorway the connector step walled shut is removed so it does not linger as
	/// an interactable door embedded in a solid wall.
	/// </summary>
	private void FinalizeDoors()
	{
		var connectedDoors = new List<(Door Door, Vector2I Connector)>();
		foreach (var (door, connector) in _doorIndicators)
		{
			if (IsConnectorConnected(connector))
			{
				_dooredConnectors.Add(connector);
				connectedDoors.Add((door, connector));
			}
			else
			{
				door.QueueFree();
			}
		}

		_doorIndicators.Clear();
		_doorIndicators.AddRange(connectedDoors);
	}

	/// <summary>
	/// Hides the editor-only gizmo of every DoorwayMarker whose doorway never got a
	/// corridor routed to it. A doorway can validate fine (it becomes a real
	/// MapData connector) yet still end up walled shut -- e.g. a tightly packed room
	/// with more doorways than the layout had room to route corridors to, or one side
	/// of a corner tile after Room.Rotate -- and MapData keeps classifying that tile
	/// as a Connector either way (wall placement is pure geometry, it never touches
	/// tile classification). Without this, the marker's arrow keeps pointing at what
	/// is now a solid generated wall, which is confusing when inspecting a level in
	/// the editor even though it has no effect on actual gameplay (the arrow doesn't
	/// render outside the editor at all).
	/// </summary>
	private void FinalizeMarkers()
	{
		foreach (var (marker, connector) in _markerIndicators)
		{
			if (!IsConnectorConnected(connector))
			{
				marker.Enabled = false;
			}
		}

		_markerIndicators.Clear();
	}

	/// <summary>
	/// A connector is connected when a corridor was routed out of one of its open
	/// sides (rooms never sit adjacent, so a real link always has a corridor tile).
	/// </summary>
	private bool IsConnectorConnected(Vector2I connector)
	{
		foreach (var direction in Map.GetConnectorDirections(connector.X, connector.Y))
		{
			var outside = connector + direction;
			if (Map.IsWithinBounds(outside.X, outside.Y) && Map.IsCorridor(outside.X, outside.Y))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>True when one of the connector's own sanctioned open directions is <paramref name="direction"/>.</summary>
	private bool ConnectorOpensToward(Vector2I connector, Vector2I direction)
	{
		foreach (var openDirection in Map.GetConnectorDirections(connector.X, connector.Y))
		{
			if (openDirection == direction)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>Optional furnishings are planned after routing, before loot and navigation bake; runtime state stays local.</summary>
	private void PlaceArrangements()
	{
		if (!EnableFurnishings) return;
		var obstacles = RoomArrangement.GetGridBounds(DecorationGridMap, DecorationGridMap.Transform)
			.Concat(RoomArrangement.GetStaticPropBounds(NavigationRegion, GlobalTransform.AffineInverse() * NavigationRegion.GlobalTransform))
			.Concat(SecretThresholdBounds()).ToList();
		var walls = RoomArrangement.GetGridBounds(WallGridMap, WallGridMap.Transform);
		foreach (var room in _roomRegions.Where(room => room.IsProcedural))
		{
			var pool = room.InteriorPool ?? ArrangementPool;
			if (pool == null) continue;
			var errors = pool.Validate();
			if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors));
			if (NavigationRegion.NavigationMesh != null && pool.PassageRadius < NavigationRegion.NavigationMesh.AgentRadius)
				throw new InvalidOperationException("Arrangement PassageRadius must cover the navigation mesh AgentRadius.");
			var region = new RoomRegion(room.Id) { IsProcedural = true, Purpose = room.Purpose, LayoutAxis = room.LayoutAxis };
			region.FurnishingSlots.AddRange(room.FurnishingSlots);
			region.Tiles.AddRange(room.Tiles);
			// A secret pocket's connector opens onto its host, which may be a room tile rather than a corridor.
			region.ConnectorTiles.AddRange(room.ConnectorTiles.Where(tile => IsSecretConnector(tile) || IsConnectorConnected(tile)));
			var placements = pool.PlacementStrategy.Plan(Map, [region], pool, Seed,
				GenerationAttempt, tile => TileToWorld(tile.X, 0, tile.Y), obstacles, walls);
			if (room.Dressing != null) placements.AddRange(room.Dressing.Plan(Map, region, Seed, GenerationAttempt,
				tile => TileToWorld(tile.X, 0, tile.Y), obstacles.Concat(RoomDressingProfile.GetBounds(placements)).ToArray(), walls,
				obstacles.Concat(RoomDressingProfile.GetBounds(placements, true)).ToArray(), pool.PassageRadius));
			foreach (var placement in placements)
			{
				var instance = placement.Definition.Instantiate();
				instance.Name = $"Arrangement_{placement.RoomId}_{ArrangementCount}";
				instance.SetMeta("arrangement_id", placement.Definition.Id);
				instance.SetMeta("room_id", placement.RoomId);
				instance.SetMeta("anchor", placement.Anchor);
				instance.SetMeta("quarter_turns", placement.QuarterTurns);
				instance.SetMeta("is_room_detail", placement.IsDetail);
				NavigationRegion.AddChild(instance);
				instance.GlobalTransform = GlobalTransform * placement.Transform;
				obstacles.AddRange(instance.GetLocalBounds().Select(box => placement.Transform * box));
				_arrangementReservations.UnionWith(placement.Occupied);
				ArrangementCount++;
			}
		}
	}

	private void ConnectRooms()
	{
		GD.Print("Connecting rooms...");

		CorridorConnector.ConnectRooms(Map);

		// Explicit entrances are structural commitments, including both ends of a hall's aisle.
		// Never turn an unrouteable required opening into a wall and deliver a different room.
		foreach (var room in _roomRegions)
		foreach (var tile in room.ConnectorTiles)
			if (Map.IsDoorway(tile.X, tile.Y) && !IsConnectorConnected(tile))
				throw new InvalidOperationException($"Required doorway at {tile} could not be connected; the floor cannot be generated.");

		PlaceCorridors();
		PlaceWalls();
	}

	private void PlaceCorridors()
	{
		GD.Print("Placing corridors...");
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				var position = TileToWorld(x, 0, z);
				if ((Map.IsCorridor(x, z) || Map.IsConnector(x, z)) && FloorGridMap.GetCellItem(position) < 0)
				{
					int tileIndex = TileFactory.GetCorridorTileIndex(_random.Decoration);
					FloorGridMap.SetCellItem(position, tileIndex, 0);
				}
			}
		}
	}

	private void PlaceWalls()
	{
		var tiles = new List<Vector2I>();
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				tiles.Add(new Vector2I(x, z));
			}
		}

		PlaceWallsAround(tiles);
	}

	/// <summary>
	/// Seals every required edge of the given tiles that is not already covered by a wall. Pass tiles in x-then-z order:
	/// wall variants are drawn from the decoration stream in that order, matching a full-map pass.
	/// </summary>
	private void PlaceWallsAround(IEnumerable<Vector2I> tiles)
	{
		GD.Print("Placing walls...");
		var sourceTiles = tiles.Where(tile => Map.IsWalkable(tile.X, tile.Y)).Select(tile => (tile, TileToWorld(tile.X, 0, tile.Y)));
		WallFootprints.PlaceWalls(
			WallGridMap,
			(int)TileSize,
			sourceTiles,
			(tile, dx, dz) => NeedsWallToward(tile.X, tile.Y, dx, dz),
			() => TileFactory.GetWallTileIndex(_random.Decoration),
			() => TileFactory.GetWallHalfTileIndex(_random.Decoration),
			() => TileFactory.GetWallCornerTileIndex(_random.Decoration));
	}

	private bool NeedsGeneratedWallAgainst(int x, int z)
	{
		return !Map.IsWithinBounds(x, z) || Map.IsWallOrEmpty(x, z);
	}

	private bool NeedsWallToward(int x, int z, int dx, int dz)
	{
		return NeedsGeneratedWallAgainst(x + dx, z + dz)
			|| Map.RequiresInteriorWall(x, z, new Vector2I(dx, dz));
	}

	/// <summary>
	/// Builds the single-item mesh library used by the occlusion grid: an unlit
	/// black horizontal cap that covers one logical tile footprint at wall-top
	/// height. Contiguous void tiles tile into a continuous flat black roof.
	/// </summary>
	private MeshLibrary BuildOcclusionMeshLibrary()
	{
		var library = new MeshLibrary();
		library.CreateItem(OcclusionItemId);
		library.SetItemName(OcclusionItemId, "void_occluder");
		library.SetItemMesh(OcclusionItemId, OcclusionCapMesh);
		// Floor tiles are centered on their cell, so the cap centers in X/Z; lift it
		// to wall-top height so it forms a flat roof over the void.
		library.SetItemMeshTransform(
			OcclusionItemId,
			new Transform3D(Basis.Identity, new Vector3(0, OcclusionCapHeight, 0)));

		return library;
	}

	/// <summary>
	/// The shared unlit black cap covering one void tile footprint.
	/// Visible from both sides so the cap reads as black at any camera yaw.
	/// </summary>
	private PlaneMesh OcclusionCapMesh => _occlusionCapMesh ??= new PlaneMesh
	{
		Size = new Vector2(TileSize, TileSize),
		Material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = Colors.Black,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		},
	};

	/// <summary>Caps permanent void and the outer margin. Gameplay exploration is rendered separately.</summary>
	private void PlaceOcclusion()
	{
		GD.Print("Placing occlusion...");
		if (OcclusionGridMap == null)
		{
			return;
		}

		for (int x = -OcclusionMargin; x < Map.Width + OcclusionMargin; x++)
		{
			for (int z = -OcclusionMargin; z < Map.Height + OcclusionMargin; z++)
			{
				if (!IsVisibleInterior(x, z))
				{
					OcclusionGridMap.SetCellItem(TileToWorld(x, 0, z), OcclusionItemId, 0);
				}
			}
		}
	}

	/// <summary>Playable surfaces that fog can uncover; everything else is void that stays capped.</summary>
	private bool IsVisibleInterior(int x, int z)
	{
		return Map.IsWithinBounds(x, z)
			&& (Map.IsRoom(x, z) || Map.IsConnector(x, z)
				|| Map.IsCorridor(x, z) || Map.IsChasm(x, z));
	}

	/// <summary>Explicit debug/test reveal of one room, without revealing connected corridors or rooms.</summary>
	internal void RevealRoom(int roomId)
	{
		if (roomId < 0 || roomId >= _roomRegions.Count) return;
		foreach (var tile in _roomRegions[roomId].Tiles) RevealTile(tile);
		UpdateDoorIndicators();
	}

	/// <summary>
	/// Shows a door's x-ray indicator only once the area on at least one side of it
	/// has been revealed, so doors still buried in the fog don't leak their location.
	/// </summary>
	private void UpdateDoorIndicators()
	{
		foreach (var (door, connector) in _doorIndicators)
		{
			door.SetIndicatorVisible(HasRevealedNeighbor(connector));
		}
	}

	private bool HasRevealedNeighbor(Vector2I tile)
	{
		foreach (var offset in CardinalOffsets)
		{
			if (_revealedTiles.Contains(tile + offset))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Unseals the nearest doored connector for subsequent sight checks and enemy reachability.
	/// </summary>
	public void OpenDoorAt(Vector3 worldPosition)
	{
		if (TryFindNearestDoorConnector(worldPosition, out Vector2I connectorTile))
		{
			_dooredConnectors.Remove(connectorTile);
			UpdateDoorIndicators();
		}
	}

	/// <summary>
	/// Marks the connector guarded by a closed door as sealed again. Fog remains revealed;
	/// this blocks subsequent discovery rays and updates logical reachability for enemy decisions.
	/// </summary>
	public void CloseDoorAt(Vector3 worldPosition)
	{
		if (TryFindNearestDoorConnector(worldPosition, out Vector2I connectorTile))
		{
			_dooredConnectors.Add(connectorTile);
			UpdateDoorIndicators();
		}
	}

	private bool TryFindNearestDoorConnector(Vector3 worldPosition, out Vector2I connectorTile)
	{
		connectorTile = default;
		float bestDistance = float.MaxValue;
		bool found = false;
		foreach (var (door, connector) in _doorIndicators)
		{
			float distance = HorizontalDistance(worldPosition, door.GlobalPosition);
			if (distance < bestDistance)
			{
				bestDistance = distance;
				connectorTile = connector;
				found = true;
			}
		}

		return found;
	}


	/// <summary>
	/// Clears gameplay fog across the generated level for runtime debugging.
	/// </summary>
	public void RevealAllFog()
	{
		if (OcclusionGridMap == null || Map == null)
		{
			return;
		}

		// Mark the interior explored rather than only clearing caps, so exploration-gated
		// loot, traps and effects appear along with the cleared fog. Void caps stay.
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				if (IsVisibleInterior(x, z))
				{
					RevealTile(new Vector2I(x, z));
				}
			}
		}

		foreach (var (door, _) in _doorIndicators)
		{
			door.SetIndicatorVisible(true);
		}
	}

	private void SetPlayerSpawnPoint()
	{
		// Find the player spawn point on the map
		// TODO: This is a hack to find the player spawn point
		PlayerSpawnPoint = FindChild("PlayerSpawnPoint", true, false) as PlayerSpawnPoint;
		if (PlayerSpawnPoint == null)
		{
			GD.PrintErr("PlayerSpawnPoint not found in the scene.");
		}
	}

	/// <summary>
	/// Scatters chests, traps, and loose ground items across the finished map. Runs after
	/// walls/corridors are final (so wall-adjacency and occupancy checks are accurate) and
	/// before <see cref="BakeNavigationMesh"/> (so chests -- solid, unlike traps/loose items
	/// -- are added as children of <see cref="NavigationRegion"/> in time to be baked in as
	/// navmesh obstacles, the same way rooms already are). Every content type shares one
	/// running list of placed points so chests/traps/items all keep clear of each other, not
	/// just their own kind.
	/// </summary>
	private void PlaceLoot()
	{
		var placedLootPoints = new List<Vector3>(_accessPickupPoints);
		PlaceChests(placedLootPoints);
		PlaceTraps(placedLootPoints);
		PlaceLooseItems(placedLootPoints);
	}

	/// <summary>Later solid loot must not undo the furnishing pass's connected-free-space invariant.</summary>
	private bool ChestPreservesRoomPassage(Node3D chest, Vector3 point, float rotation)
	{
		var tile = WorldToTile(point);
		var room = _roomRegions.FirstOrDefault(region => region.IsProcedural && region.Tiles.Contains(tile));
		if (room == null) return false;
		var pool = room.InteriorPool ?? ArrangementPool;
		if (pool == null) return true;
		var obstacles = RoomArrangement.GetGridBounds(DecorationGridMap, DecorationGridMap.Transform)
			.Concat(RoomArrangement.GetGridBounds(WallGridMap, WallGridMap.Transform))
			.Concat(RoomArrangement.GetStaticPropBounds(NavigationRegion, GlobalTransform.AffineInverse() * NavigationRegion.GlobalTransform)).ToArray();
		var doors = room.ConnectorTiles.Where(connector => Map.GetConnectorDirections(connector.X, connector.Y)
			.Any(direction => { var next = connector + direction; return Map.IsWithinBounds(next.X, next.Y) && Map.IsCorridor(next.X, next.Y); }));
		var passage = new RoomPassageValidator(Map, room.Tiles.Where(t => Map.IsWalkable(t.X, t.Y)).ToHashSet(), doors,
			t => TileToWorld(t.X, 0, t.Y), obstacles, pool.PassageRadius);
		var proposed = GlobalTransform.AffineInverse() * new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(rotation)),
			point + Vector3.Up * GetFloorSurfaceHeight(point));
		return passage.IsConnected(RoomArrangement.GetStaticPropBounds(chest, proposed));
	}

	private void PlaceChests(List<Vector3> placedLootPoints)
	{
		if (ChestScene == null || MaxChests <= 0)
		{
			return;
		}

		var candidates = GetChestCandidates();
		if (candidates.Count == 0)
		{
			ReportNoLootCandidates("chests", "no valid wall-adjacent procedural room tiles found");
			return;
		}

		int count = _random.Items.RandiRange(MinChests, MaxChests);
		var remaining = new List<(Vector3 Point, float RotationDegrees)>(candidates);
		int placed = 0;
		while (placed < count && remaining.Count > 0)
		{
			int index = _random.Items.RandiRange(0, remaining.Count - 1);
			var candidate = remaining[index];
			remaining.RemoveAt(index);
			if (IsBlockedLootPoint(candidate.Point, placedLootPoints, LootSpawnSpacing))
			{
				continue;
			}

			var chest = ChestScene.Instantiate<Node3D>();
			if (!ChestPreservesRoomPassage(chest, candidate.Point, candidate.RotationDegrees))
			{
				chest.Free();
				continue;
			}
			chest.SetMeta("persistent_id", $"depth:{DungeonDepth}:chest:{placed}");
			// A chest is a solid StaticBody3D: parent it under NavigationRegion (like rooms
			// and the GridMap copies BakeNavigationMesh adds) so it becomes a navmesh
			// obstacle. Do NOT route this through SpawnPoint: it parents under Level instead
			// (missing the navmesh bake) and unconditionally applies an extra 180-degree
			// yaw on spawn, which would silently point the chest the wrong way.
			NavigationRegion.AddChild(chest);
			// Raised onto the floor's visual surface: a static prop doesn't settle via
			// physics the way characters do, so at y=0 its base sits buried inside the
			// floor mesh.
			chest.GlobalPosition = candidate.Point + Vector3.Up * GetFloorSurfaceHeight(candidate.Point);
			chest.RotationDegrees = new Vector3(0, candidate.RotationDegrees, 0);

			placedLootPoints.Add(candidate.Point);
			placed++;
		}

		if (placed < MinChests)
		{
			GD.PrintErr($"Placed only {placed}/{MinChests} minimum chests: ran out of valid spots.");
		}
	}

	private void PlaceTraps(List<Vector3> placedLootPoints)
	{
		if (TrapScene == null || MaxTraps <= 0)
		{
			return;
		}

		var candidates = GetOpenTileCandidates(proceduralRoomsOnly: true);
		if (candidates.Count == 0)
		{
			ReportNoLootCandidates("traps", "no valid open procedural room or corridor tiles found");
			return;
		}

		int count = _random.Items.RandiRange(MinTraps, MaxTraps);
		var remaining = new List<Vector3>(candidates);
		int placed = 0;
		while (placed < count && remaining.Count > 0)
		{
			int index = _random.Items.RandiRange(0, remaining.Count - 1);
			var point = remaining[index];
			remaining.RemoveAt(index);
			if (IsBlockedLootPoint(point, placedLootPoints, LootSpawnSpacing))
			{
				continue;
			}

			// Traps deliberately allow room/corridor chokepoint tiles (unlike chests) -- a
			// trap you're forced to cross is the point. No facing requirement either. The
			// floor plate mesh is square and its texture is grid-aligned, matching the
			// room's own floor tiles -- rotating it off-axis visibly breaks that seam
			// alignment (the plate's edges no longer line up with its floor neighbors),
			// which read as an oddly angled, "floating" tile rather than a natural part
			// of the floor. Leave it unrotated, like any other floor tile.
			var trap = TrapScene.Instantiate<Node3D>();
			NavigationRegion.AddChild(trap);
			trap.GlobalPosition = point;
			if (trap is FloorTrap floorTrap)
			{
				floorTrap.Initialize($"depth:{DungeonDepth}:trap:{placed}", Seed, FloorGridMap, FloorGridMap.LocalToMap(FloorGridMap.ToLocal(point)));
			}

			placedLootPoints.Add(point);
			placed++;
		}

		if (placed < MinTraps)
		{
			GD.PrintErr($"Placed only {placed}/{MinTraps} minimum traps: ran out of valid spots.");
		}
	}

	private void PlaceLooseItems(List<Vector3> placedLootPoints)
	{
		if (LooseItemScene == null || LooseItemLootTable == null || MaxLooseItems <= 0)
		{
			return;
		}

		var candidates = GetLooseItemCandidates();
		if (candidates.Count == 0)
		{
			ReportNoLootCandidates("loose items", "no valid procedural room tiles found");
			return;
		}

		uint depth = DungeonDepth;
		int count = _random.Items.RandiRange(MinLooseItems, MaxLooseItems);
		var remaining = new List<Vector3>(candidates);
		int placed = 0;
		while (placed < count && remaining.Count > 0)
		{
			int index = _random.Items.RandiRange(0, remaining.Count - 1);
			var point = remaining[index];
			remaining.RemoveAt(index);
			if (IsBlockedLootPoint(point, placedLootPoints, LootSpawnSpacing))
			{
				continue;
			}

			string sourceId = $"depth:{DungeonDepth}:loose:{placed}";
			using var rng = CreateSourceLootRng(sourceId);
			var entry = LooseItemLootTable.PickEntry(depth, rng);
			if (entry == null)
			{
				break; // Table is empty; no point trying further candidates.
			}

			int quantity = entry.QuantityAt(depth);
			if (quantity <= 0) { continue; }
			foreach (var record in ItemFactory.CreateBatch(entry.Item, quantity, ItemCreationPolicy, depth, rng, sourceId))
			{
				SpawnPickup(record.Item, record.Quantity, sourceId, point);
			}

			placedLootPoints.Add(point);
			placed++;
		}

		if (placed < MinLooseItems)
		{
			GD.PrintErr($"Placed only {placed}/{MinLooseItems} minimum loose items: ran out of valid spots.");
		}
	}

	/// <summary>
	/// Every interior room tile with a wall on at least one cardinal side, paired with the
	/// Y-rotation that faces a chest away from that wall into the room -- excludes tiles
	/// adjacent to a connector for extra margin from doorway traffic. A tile with exactly one
	/// wall-adjacent side (a flat wall run) is preferred over a corner (two sides) for the
	/// cleanest single-direction facing; corners are only used as a fallback when no flat
	/// spot exists anywhere on the map.
	/// </summary>
	private List<(Vector3 Point, float RotationDegrees)> GetChestCandidates()
	{
		var flatCandidates = new List<(Vector3, float)>();
		var cornerCandidates = new List<(Vector3, float)>();
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				if (!Map.IsRoom(x, z) || !IsProceduralRoomTile(x, z)
					|| IsOccupiedSpawnTile(x, z) || IsAdjacentToConnector(x, z))
				{
					continue;
				}

				var wallDirections = new List<Vector2I>();
				foreach (var offset in CardinalOffsets)
				{
					int nx = x + offset.X, nz = z + offset.Y;
					// Wall placement has already sealed every void-facing and unsanctioned
					// interior edge by this point in GenerateMap, so a non-walkable
					// neighbor -- including the map border -- is solid wall geometry to back
					// a chest against. Chasm is the one exception: it's deliberately left
					// unwalled (pits are open), so a chasm-adjacent tile has nothing behind
					// it and must not count as a wall direction.
					if (Map.IsWithinBounds(nx, nz) && Map.IsChasm(nx, nz))
					{
						continue;
					}

					if (!Map.IsWithinBounds(nx, nz) || !Map.IsWalkable(nx, nz))
					{
						wallDirections.Add(offset);
					}
				}

				if (wallDirections.Count == 0)
				{
					continue;
				}

				var point = TileToWorld(x, 0, z);
				// GetYRotationDegrees assumes its prop is authored facing North (0deg) --
				// true for doors/markers, but chest.tscn's lid/opening faces the opposite
				// way at rotation 0. Rotating toward the wall direction itself (not away
				// from it) compensates for that -- equivalent to "away + 180" but without
				// a sum that can exceed 360 (RotationDegrees.Y doesn't normalize on a
				// direct assignment, so a raw 450 would stay 450, not wrap to 90).
				float rotation = DoorwayMarker.GetYRotationDegrees(DoorwayMarker.GetDirectionFlag(wallDirections[0]));
				(wallDirections.Count == 1 ? flatCandidates : cornerCandidates).Add((point, rotation));
			}
		}

		return flatCandidates.Count > 0 ? flatCandidates : cornerCandidates;
	}

	private List<Vector3> GetLooseItemCandidates()
	{
		var candidates = new List<Vector3>();
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				if (Map.IsRoom(x, z) && IsProceduralRoomTile(x, z) && !IsOccupiedSpawnTile(x, z))
				{
					candidates.Add(TileToWorld(x, 0, z));
				}
			}
		}

		return candidates;
	}

	/// <summary>
	/// True when tile (x,z) belongs to a procedurally built room, or to no tracked room
	/// region at all (e.g. a corridor tile, which is never part of any room). False only
	/// for a tile that belongs to an authored room -- see RoomRegion.IsProcedural.
	/// </summary>
	private bool IsProceduralRoomTile(int x, int z)
	{
		return !_tileToRoom.TryGetValue(new Vector2I(x, z), out int roomId) || _roomRegions[roomId].IsProcedural;
	}

	/// <summary>
	/// Generation-time loot only goes into procedural rooms (authored rooms carry their
	/// own hand-placed content), so a map whose standard rooms all came from authored
	/// scenes legitimately places nothing -- report that quietly. Zero candidates on a
	/// map that DOES contain procedural rooms means something real broke, so that stays
	/// a loud error.
	/// </summary>
	private void ReportNoLootCandidates(string lootKind, string reason)
	{
		bool anyProceduralRoom = false;
		foreach (var region in _roomRegions)
		{
			if (region.IsProcedural)
			{
				anyProceduralRoom = true;
				break;
			}
		}

		if (anyProceduralRoom)
		{
			GD.PrintErr($"Cannot place {lootKind}: {reason}.");
		}
		else
		{
			GD.Print($"Skipping {lootKind}: no procedural rooms in this map (authored rooms keep their hand-placed content).");
		}
	}

	/// <summary>
	/// The walkable top surface of the floor at a world position, in world units above
	/// y=0 -- what a prop must be raised by to stand ON the floor instead of having its
	/// base buried inside the floor mesh (whose visual surface sits above its y=0 anchor
	/// plane; ~0.05 for tile/wood floors, ~0.11 for dirt). Measured from the actual
	/// floor mesh's AABB at that tile, capped because decorated variants (e.g.
	/// floor_dirt_large_rocky's scattered rocks) inflate the AABB far above the actual
	/// walkable plane.
	/// </summary>
	private float GetFloorSurfaceHeight(Vector3 worldPoint)
	{
		const float MaxSurfaceHeight = 0.12f;

		MeshLibrary library = FloorGridMap.MeshLibrary;
		if (library == null)
		{
			return 0f;
		}

		// A tile's floor is anchored somewhere within its 4x4 cell block: at the tile
		// center for tile-sized meshes, at center±1 for half-tile meshes.
		var center = new Vector3I(Mathf.RoundToInt(worldPoint.X), 0, Mathf.RoundToInt(worldPoint.Z));
		float height = 0f;
		for (int dx = -(int)TileSize / 2; dx < (int)TileSize / 2; dx++)
		{
			for (int dz = -(int)TileSize / 2; dz < (int)TileSize / 2; dz++)
			{
				int item = FloorGridMap.GetCellItem(center + new Vector3I(dx, 0, dz));
				if (item < 0)
				{
					continue;
				}

				Mesh mesh = library.GetItemMesh(item);
				if (mesh == null)
				{
					continue;
				}

				Aabb aabb = mesh.GetAabb();
				height = Mathf.Max(height, Mathf.Min(aabb.Position.Y + aabb.Size.Y, MaxSurfaceHeight));
			}
		}

		return height;
	}

	private bool IsAdjacentToConnector(int x, int z)
	{
		foreach (var offset in CardinalOffsets)
		{
			int nx = x + offset.X, nz = z + offset.Y;
			if (Map.IsWithinBounds(nx, nz) && Map.IsConnector(nx, nz))
			{
				return true;
			}
		}

		return false;
	}

	private bool IsBlockedLootPoint(Vector3 point, List<Vector3> placedLootPoints, float spacing)
	{
		if (PlayerSpawnPoint != null && HorizontalDistance(point, PlayerSpawnPoint.GlobalPosition) < LootSpawnPlayerClearance)
		{
			return true;
		}

		foreach (var placed in placedLootPoints)
		{
			if (HorizontalDistance(point, placed) < spacing)
			{
				return true;
			}
		}

		return false;
	}

	private void GenerateEnemySpawnPoints()
	{
		var encounter = MobFactory.CreateEncounter(DungeonDepth, _random.Encounters);
		int mobCount = encounter.Count;
		GD.Print($"Generating {mobCount} enemy spawn points...");

		// Candidate selection uses map tiles and collision clearance, not navmesh paths.
		// Keep the initial encounter baseline reproducible even in headless validation.
		var candidates = GetOpenTileCandidates(proceduralRoomsOnly: false);
		if (candidates.Count == 0)
		{
			GD.PrintErr("Cannot generate enemy spawn points: no valid map tiles found.");
			return;
		}

		var spawnPoints = new List<Vector3>();
		for (int i = 0; i < mobCount; i++)
		{
			if (!TryPickEnemySpawnPoint(candidates, spawnPoints, out var point))
			{
				GD.PrintErr("Could not find a valid enemy spawn point.");
				continue;
			}

			spawnPoints.Add(point);

			// Enemy scenes are loaded lazily by the factory to keep unused variants out of memory.
			var enemyScene = encounter[i];
			if (enemyScene == null)
			{
				continue;
			}

			var spawnPointNode = new SpawnPoint();
			spawnPointNode.SpawnOnStart = true;
			spawnPointNode.Scenes = [enemyScene];
			spawnPointNode.PersistentId = $"depth:{DungeonDepth}:monster:{i}";
			AddChild(spawnPointNode);

			spawnPointNode.GlobalPosition = point;
			spawnPointNode.Rotation = new Vector3(0, (float)(_random.Encounters.Randf() * 2 * Math.PI), 0);

			EnemySpawnPoints.Add(spawnPointNode);
		}
	}

	/// <summary>
	/// Every open (room or corridor), unoccupied tile's world position -- shared by enemy
	/// spawning and trap placement, both of which are happy to land on a chokepoint.
	/// Corridor tiles are never part of any room, authored or not, so <paramref
	/// name="proceduralRoomsOnly"/> only ever excludes a room tile -- pass true for
	/// generation-time loot (an authored room brings its own hand-placed content the
	/// generic scan knows nothing about), false for enemy spawning, which has always
	/// used every room regardless of origin and should keep doing so.
	/// </summary>
	private List<Vector3> GetOpenTileCandidates(bool proceduralRoomsOnly)
	{
		var candidates = new List<Vector3>();
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				bool isOpenRoomTile = Map.IsRoom(x, z) && (!proceduralRoomsOnly || IsProceduralRoomTile(x, z));
				if ((isOpenRoomTile || Map.IsCorridor(x, z)) && !IsOccupiedSpawnTile(x, z))
				{
					candidates.Add(TileToWorld(x, 0, z));
				}
			}
		}

		return candidates;
	}

	private bool IsOccupiedSpawnTile(int x, int z)
	{
		return _arrangementReservations.Contains(new Vector2I(x, z)) || HasDecorationInTile(x, z) || HasPropInTile(x, z);
	}

	/// <summary>Checks a room-local circular footprint against floor edges, decoration and authored props.</summary>
	public bool IsEncounterFootprintClear(Vector3 point, float radius, int roomId)
	{
		if (roomId < 0 || radius <= 0) return false;
		var center = WorldToTile(point);
		int extent = Mathf.CeilToInt(radius / TileSize + .5f);
		float half = TileSize * .5f;
		for (int x = center.X - extent; x <= center.X + extent; x++)
		{
			for (int z = center.Y - extent; z <= center.Y + extent; z++)
			{
				Vector3 tileCenter = TileToWorld(x, 0, z);
				float dx = Mathf.Max(0, Mathf.Abs(point.X - tileCenter.X) - half);
				float dz = Mathf.Max(0, Mathf.Abs(point.Z - tileCenter.Z) - half);
				if (dx * dx + dz * dz >= radius * radius) continue;
				if (GetRoomIdAt(new Vector2I(x, z)) != roomId || IsOccupiedSpawnTile(x, z)) return false;
			}
		}
		return true;
	}

	/// <summary>Prefer a roomy room interior near descent, keeping an oversized actor out of corridors.</summary>
	public bool TryFindLargeEncounterPosition(Vector3 reference, float minimumRadius, float preferredRadius, out Vector3 position)
	{
		position = default;
		float best = float.NegativeInfinity;
		var occupied = new List<Vector3>();
		var accessible = FreelyAccessibleTiles();
		foreach (var tilePoint in GetOpenTileCandidates(proceduralRoomsOnly: false))
		{
			// Half-tile offsets also find clear 2x2 spaces between cell centers.
			for (int dx = 0; dx <= 1; dx++)
			{
				for (int dz = 0; dz <= 1; dz++)
				{
					var point = tilePoint + new Vector3(dx * TileSize * .5f, 0, dz * TileSize * .5f);
					int roomId = GetRoomIdAt(WorldToTile(point));
					if (accessible != null && !accessible.Contains(WorldToTile(point))
						|| IsBlockedEnemySpawnPoint(point, occupied)
						|| !IsEncounterFootprintClear(point, minimumRadius, roomId)) continue;
					float clearance = minimumRadius;
					for (float radius = minimumRadius + .2f; radius <= preferredRadius; radius += .2f)
					{
						if (!IsEncounterFootprintClear(point, radius, roomId)) break;
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

	/// <summary>
	/// Picks a uniformly random free floor/corridor tile anywhere on the map, for effects
	/// like a teleport scroll that relocate the player rather than spawn near a point.
	/// Uses its own RNG rather than the run's seeded loot RNG, since a teleport's outcome
	/// should not consume or depend on the loot-roll sequence.
	/// </summary>
	public bool TryPickRandomFreePosition(out Vector3 position)
	{
		var accessible = FreelyAccessibleTiles();
		List<Vector3> candidates = GetOpenTileCandidates(proceduralRoomsOnly: false)
			.Where(p => accessible == null || accessible.Contains(WorldToTile(p))).ToList();
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
	/// Finds a free world position close to <paramref name="origin"/> for a runtime spawn. Probes the
	/// origin first, then samples points on expanding rings out to <paramref name="maxRadius"/> world
	/// units, returning the nearest one that sits on a room/corridor floor tile whose column is clear
	/// of any solid collider (walls, props, stairs, transition blockers, etc.). Falls back to
	/// <paramref name="origin"/> when nothing nearby is free, so the item still drops next to the
	/// player rather than teleporting across the map.
	/// </summary>
	public Vector3 FindFreeSpawnPositionNear(Vector3 origin, float maxRadius = 6f)
	{
		if (Map == null || IsSpawnPositionFree(origin))
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

	private bool IsSpawnPositionFree(Vector3 worldPosition)
	{
		if (!IsWalkableMapPosition(worldPosition))
		{
			return false;
		}

		return !IsColumnObstructed(worldPosition);
	}

	/// <summary>
	/// Finds a nearby floor/corridor point for effects that can land near props or decorations.
	/// Unlike item spawns, this deliberately does not require a clear item-sized collision column.
	/// </summary>
	public bool TryFindEffectLandingPositionNear(Vector3 origin, float maxRadius, out Vector3 landingPosition)
	{
		if (Map == null || IsEffectLandingPositionValid(origin))
		{
			landingPosition = origin;
			return true;
		}

		if (TryProjectEffectLandingPosition(origin, out landingPosition))
		{
			return true;
		}

		const int samplesPerRing = 16;
		const float ringStep = 0.5f;
		for (float radius = ringStep; radius <= maxRadius; radius += ringStep)
		{
			for (int i = 0; i < samplesPerRing; i++)
			{
				float angle = Mathf.Tau * i / samplesPerRing;
				var candidate = new Vector3(
					origin.X + Mathf.Cos(angle) * radius,
					origin.Y,
					origin.Z + Mathf.Sin(angle) * radius);

				if (IsEffectLandingPositionValid(candidate))
				{
					landingPosition = candidate;
					return true;
				}

				if (TryProjectEffectLandingPosition(candidate, out landingPosition))
				{
					return true;
				}
			}
		}

		landingPosition = default;
		return false;
	}

	public bool IsEffectLandingPositionValid(Vector3 worldPosition)
	{
		return Map != null && IsWalkableMapPosition(worldPosition);
	}

	private bool TryProjectEffectLandingPosition(Vector3 worldPosition, out Vector3 landingPosition)
	{
		PhysicsDirectSpaceState3D space = GetWorld3D()?.DirectSpaceState;
		if (space == null)
		{
			landingPosition = default;
			return false;
		}

		var query = PhysicsRayQueryParameters3D.Create(
			worldPosition + Vector3.Up * EffectLandingRayHeight,
			worldPosition + Vector3.Down * EffectLandingRayDepth,
			EffectLandingSurfaceMask);
		query.HitFromInside = false;
		var result = space.IntersectRay(query);
		if (result.Count == 0)
		{
			landingPosition = default;
			return false;
		}

		Vector3 normal = result["normal"].AsVector3();
		if (normal.Dot(Vector3.Up) < 0.35f)
		{
			landingPosition = default;
			return false;
		}

		Vector3 projectedPosition = result["position"].AsVector3();
		Node collider = result["collider"].As<Node>();
		if (!IsEffectLandingSurface(collider, projectedPosition))
		{
			landingPosition = default;
			return false;
		}

		landingPosition = projectedPosition;
		return true;
	}

	private bool IsEffectLandingSurface(Node collider, Vector3 worldPosition)
	{
		if (collider == FloorGridMap)
		{
			return IsWalkableMapPosition(worldPosition);
		}

		return collider?.IsInGroup("stairs") == true && IsWithinMapBounds(worldPosition);
	}

	private bool IsWalkableMapPosition(Vector3 worldPosition)
	{
		var tile = WorldToTile(worldPosition);
		if (!IsWithinMapBounds(tile))
		{
			return false;
		}

		return Map.IsRoom(tile.X, tile.Y) || Map.IsCorridor(tile.X, tile.Y);
	}

	private bool IsWithinMapBounds(Vector3 worldPosition)
	{
		return IsWithinMapBounds(WorldToTile(worldPosition));
	}

	private bool IsWithinMapBounds(Vector2I tile)
	{
		return Map != null && tile.X >= 0 && tile.Y >= 0 && tile.X < Map.Width && tile.Y < Map.Height;
	}

	/// <summary>
	/// Returns true when a solid collider occupies the column at <paramref name="worldPosition"/>.
	/// This is a physics overlap against the world, walls, and props layers, so it treats stairs,
	/// transition blockers, decorations, and props uniformly as "occupied" without enumerating each
	/// object category. The probe is item-sized and lifted off the floor so the flat floor collider
	/// is ignored and free space right beside an obstacle still qualifies.
	/// </summary>
	private bool IsColumnObstructed(Vector3 worldPosition)
	{
		PhysicsDirectSpaceState3D space = GetWorld3D()?.DirectSpaceState;
		if (space == null)
		{
			return false;
		}

		var shape = new BoxShape3D
		{
			Size = new Vector3(0.8f, 1.6f, 0.8f),
		};

		var query = new PhysicsShapeQueryParameters3D
		{
			Shape = shape,
			Transform = new Transform3D(Basis.Identity, worldPosition + Vector3.Up * 1.0f),
			CollisionMask = SpawnObstructionMask,
			CollideWithBodies = true,
			CollideWithAreas = false,
		};

		return space.IntersectShape(query, 1).Count > 0;
	}

	private bool HasDecorationInTile(int x, int z)
	{
		var center = TileToWorld(x, 0, z);
		int halfTileSize = (int)TileSize / 2;
		for (int dx = -halfTileSize; dx < halfTileSize; dx++)
		{
			for (int dz = -halfTileSize; dz < halfTileSize; dz++)
			{
				var cell = new Vector3I(center.X + dx, center.Y, center.Z + dz);
				if (DecorationGridMap.GetCellItem(cell) >= 0)
				{
					return true;
				}
			}
		}

		return false;
	}

	private bool HasPropInTile(int x, int z)
	{
		var center = TileToWorld(x, 0, z);
		foreach (Node node in GetTree().GetNodesInGroup("prop"))
		{
			if (node is Node3D node3D && HorizontalDistance(center, node3D.GlobalPosition) < EnemySpawnPropClearance)
			{
				return true;
			}
		}

		return false;
	}

	private bool TryPickEnemySpawnPoint(IReadOnlyList<Vector3> candidates, List<Vector3> spawnPoints, out Vector3 point)
	{
		point = Vector3.Zero;
		var remainingCandidates = new List<Vector3>(candidates);
		while (remainingCandidates.Count > 0)
		{
			int index = _random.Encounters.RandiRange(0, remainingCandidates.Count - 1);
			var candidate = remainingCandidates[index];
			remainingCandidates.RemoveAt(index);

			if (IsBlockedEnemySpawnPoint(candidate, spawnPoints))
			{
				continue;
			}

			point = candidate;
			return true;
		}

		return false;
	}

	private bool IsBlockedEnemySpawnPoint(Vector3 point, List<Vector3> spawnPoints)
	{
		if (PlayerSpawnPoint != null && HorizontalDistance(point, PlayerSpawnPoint.GlobalPosition) < EnemySpawnPlayerClearance)
		{
			return true;
		}

		foreach (var spawnPoint in spawnPoints)
		{
			if (HorizontalDistance(point, spawnPoint) < EnemySpawnPointSpacing)
			{
				return true;
			}
		}

		foreach (Node node in FindChildren("*", "", true, false))
		{
			if (node is not Node3D node3D)
			{
				continue;
			}

			if ((node is IProgressionAnchor || node.IsInGroup("stairs"))
				&& HorizontalDistance(point, node3D.GlobalPosition) < EnemySpawnBlockedAreaClearance)
			{
				return true;
			}
		}

		return false;
	}

	private static float HorizontalDistance(Vector3 a, Vector3 b)
	{
		return new Vector2(a.X, a.Z).DistanceTo(new Vector2(b.X, b.Z));
	}

	public void GenerateMap(bool includeGameplay = true)
	{
		for (GenerationAttempt = 0; GenerationAttempt < 8; GenerationAttempt++)
		{
			GenerateMapAttempt(includeGameplay);
			if (!includeGameplay || MobFactory is not RealmEncounterFactory realm
				|| !realm.IsGatekeeperFloor(DungeonDepth) || realm.GatekeeperFit == null
				|| GatekeeperSpawnMarker.Find(this.GetAncestorOrNull<Level>()) != null) return;
			if (TryFindLargeEncounterPosition(Vector3.Zero, realm.GatekeeperFit.MinimumClearance,
				realm.GatekeeperFit.PreferredClearance, out _)) return;
			GD.Print($"No room fits the gatekeeper; retrying layout (attempt {GenerationAttempt + 1}/8).");
		}
		throw new InvalidOperationException("No generated room can accommodate the gatekeeper after eight layout attempts.");
	}

	private void GenerateMapAttempt(bool includeGameplay)
	{
		Reset();

		GD.Print("Generating map...");
		if (RoomLayout == null || CorridorConnector == null
			|| RoomFactory == null || MobFactory == null || TileFactory == null
			|| FloorGridMap == null || WallGridMap == null || DecorationGridMap == null)
		{
			// This is especially important to check in the editor
			return;
		}

		// Step 1: Generate random rooms
		GenerateRooms();

		// Step 2: Connect the rooms
		ConnectRooms();

		// Step 2b: Resolve doors against the routed corridors: keep doors at connected
		// doorways (and gate fog there), drop doors at doorways that got walled shut.
		FinalizeDoors();

		// Step 2b.2: Hide doorway-marker gizmos left pointing at a wall for the same reason.
		FinalizeMarkers();
		AccessProfile?.Validate();
		CreateSecretBranches();
		PlaceArrangements();

		// Step 2c: Cap permanent void; gameplay additionally starts with an empty exploration mask.
		PlaceOcclusion();
		if (includeGameplay) InitializeDiscovery();

		if (!includeGameplay)
		{
			GD.Print("Map preview generated.");
			return;
		}

		// Step 3: Find the player spawn point (moved ahead of the navmesh bake: loot
		// placement below needs it for clearance, and it's just a scene lookup with no
		// dependency on anything the bake computes).
		SetPlayerSpawnPoint();

		// Step 3b: Scatter chests/traps/loose items. Must run before BakeNavigationMesh so
		// chests are included as navmesh obstacles, and before GenerateEnemySpawnPoints so
		// enemy-spawn clearance (which already checks the "prop" group) naturally avoids them.
		PlaceAccess();
		PlaceLoot();

		// Step 4: Bake navigation mesh
		BakeNavigationMesh();

		// Step 5: Create enemy spawn points
		GenerateEnemySpawnPoints();

		GD.Print("Map generated.");
	}

	private void BakeNavigationMesh()
	{
		// Add grid maps back to the NavigationRegion and rebake the navigation mesh
		// TODO: Not sure why we cannot make the GridMaps children of the NavigationRegion directly
		// If we try, the thread seems to block indefinitely when making updates to the GridMaps
		Node floorGridMapCopy = FloorGridMap.Duplicate();
		Node wallGripMapCopy = WallGridMap.Duplicate();
		Node decorationGridMapCopy = DecorationGridMap.Duplicate();
		try
		{
			NavigationRegion.AddChild(floorGridMapCopy);
			NavigationRegion.AddChild(wallGripMapCopy);
			NavigationRegion.AddChild(decorationGridMapCopy);
			NavigationRegion.BakeNavigationMesh(false);
		}
		finally
		{
			floorGridMapCopy.QueueFree();
			wallGripMapCopy.QueueFree();
			decorationGridMapCopy.QueueFree();
		}
	}

	/// <summary>
	/// Shows or hides a translucent overlay of the baked navigation mesh. The overlay is built lazily
	/// from the current <see cref="NavigationRegion"/> mesh so it reflects the active level, and is
	/// rebuilt automatically if a regeneration discarded the previous instance.
	/// </summary>
	public void SetNavigationDebugVisible(bool visible)
	{
		if (NavigationRegion?.NavigationMesh == null)
		{
			return;
		}

		// Rebuild from scratch each time so door-link colours reflect current open/closed state
		// rather than whatever it was when the overlay was first shown.
		if (_navigationDebugMesh != null && IsInstanceValid(_navigationDebugMesh))
		{
			_navigationDebugMesh.QueueFree();
			_navigationDebugMesh = null;
		}

		if (visible)
		{
			_navigationDebugMesh = BuildNavigationDebugMesh();
		}
	}

	private MeshInstance3D BuildNavigationDebugMesh()
	{
		var surfaceTool = new SurfaceTool();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

		// Main baked region in blue.
		bool hasGeometry = AppendRegionTriangles(
			surfaceTool, NavigationRegion, new Color(0.1f, 0.6f, 1f, 0.35f));

		// Per-door links drawn as flat strips between their endpoints, in the same blue as the main
		// mesh, so each doorway bridge is visible (whether the door is open or closed).
		foreach (Node node in GetTree().GetNodesInGroup("door"))
		{
			if (node is not Door door)
			{
				continue;
			}

			var doorLink = door.NavigationLink;
			if (doorLink != null)
			{
				hasGeometry |= AppendLinkStrip(
					surfaceTool, doorLink, new Color(0.1f, 0.6f, 1f, 0.35f));
			}
		}

		if (!hasGeometry)
		{
			return null;
		}

		var material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			VertexColorUseAsAlbedo = true,
		};

		var meshInstance = new MeshInstance3D
		{
			Name = "NavigationDebugMesh",
			Mesh = surfaceTool.Commit(),
			MaterialOverride = material,
			// Vertices are already baked in world space, so ignore the parent transform.
			TopLevel = true,
		};

		NavigationRegion.AddChild(meshInstance);
		return meshInstance;
	}

	/// <summary>
	/// Appends a navigation region's polygons to <paramref name="surfaceTool"/> in world space
	/// (lifted slightly to avoid z-fighting), tinted with <paramref name="color"/>. Returns false
	/// if the region has no navmesh geometry. Used by the debug overlay to draw the main mesh and
	/// each door's bridge patch together so their relative height and alignment are visible.
	/// </summary>
	private static bool AppendRegionTriangles(SurfaceTool surfaceTool, NavigationRegion3D region, Color color)
	{
		NavigationMesh navigationMesh = region?.NavigationMesh;
		if (navigationMesh == null)
		{
			return false;
		}

		Vector3[] vertices = navigationMesh.GetVertices();
		if (vertices.Length == 0)
		{
			return false;
		}

		Transform3D transform = region.GlobalTransform;
		transform.Origin += new Vector3(0, 0.05f, 0);

		for (int polygonIndex = 0; polygonIndex < navigationMesh.GetPolygonCount(); polygonIndex++)
		{
			int[] polygon = navigationMesh.GetPolygon(polygonIndex);
			// Fan-triangulate each convex navmesh polygon.
			for (int corner = 2; corner < polygon.Length; corner++)
			{
				surfaceTool.SetColor(color);
				surfaceTool.AddVertex(transform * vertices[polygon[0]]);
				surfaceTool.SetColor(color);
				surfaceTool.AddVertex(transform * vertices[polygon[corner - 1]]);
				surfaceTool.SetColor(color);
				surfaceTool.AddVertex(transform * vertices[polygon[corner]]);
			}
		}

		return true;
	}

	/// <summary>
	/// Appends a flat quad strip following a navigation link from its start to end endpoint, tinted
	/// with <paramref name="color"/>. The strip's height is snapped to the baked navmesh (via the
	/// nearest navmesh point) so it sits co-planar with the main mesh overlay rather than at the
	/// link's authored floor-level endpoints. Returns false if the endpoints coincide.
	/// </summary>
	private static bool AppendLinkStrip(SurfaceTool surfaceTool, NavigationLink3D link, Color color)
	{
		Transform3D transform = link.GlobalTransform;
		Vector3 start = transform * link.StartPosition;
		Vector3 end = transform * link.EndPosition;

		// Lift to the navmesh surface (+ the same small z-fight offset the main mesh uses) so the
		// debug strip lines up with the blue overlay instead of hugging the floor.
		Rid navigationMap = link.GetWorld3D().NavigationMap;
		if (navigationMap.IsValid)
		{
			start.Y = NavigationServer3D.MapGetClosestPoint(navigationMap, start).Y;
			end.Y = NavigationServer3D.MapGetClosestPoint(navigationMap, end).Y;
		}
		start.Y += 0.05f;
		end.Y += 0.05f;

		Vector3 along = end - start;
		if (along.LengthSquared() <= 0.0001f)
		{
			return false;
		}

		// A half-metre-wide ribbon centred on the link line, lying flat on the floor plane.
		Vector3 side = along.Normalized().Cross(Vector3.Up);
		if (side.LengthSquared() <= 0.0001f)
		{
			side = Vector3.Right;
		}
		side = side.Normalized() * 0.25f;

		Vector3 a = start + side;
		Vector3 b = start - side;
		Vector3 c = end - side;
		Vector3 d = end + side;

		foreach (Vector3 vertex in new[] { a, b, c, a, c, d })
		{
			surfaceTool.SetColor(color);
			surfaceTool.AddVertex(vertex);
		}

		return true;
	}

	private void Reset()
	{
		GD.Print("Resetting map generator...");
		_random = new LevelGenerationContext(Seed, GenerationAttempt);

		ResetAccess();
		_roomRegions.Clear();
		_arrangementReservations.Clear();
		ArrangementCount = 0;
		_tileToRoom.Clear();
		ClearDiscovery();
		_revealedTiles.Clear();
		_dooredConnectors.Clear();
		_doorIndicators.Clear();
		_markerIndicators.Clear();

		// Initialize the map with empty tiles and a walled border
		Map = new MapData((int)MapWidth, (int)MapDepth);
		Map.ResetToBorderedEmpty();

		FloorGridMap?.Clear();
		WallGridMap?.Clear();
		DecorationGridMap?.Clear();
		OcclusionGridMap?.Clear();

		PlayerSpawnPoint = null;
		if (EnemySpawnPoints != null)
		{
			foreach (var spawnPoint in EnemySpawnPoints)
			{
				RemoveChild(spawnPoint);
				spawnPoint.QueueFree();
			}
		}
		EnemySpawnPoints = new Array<SpawnPoint>();

		if (NavigationRegion != null)
		{
			// Empty the navigation region
			foreach (var node in NavigationRegion.GetChildren())
			{
				NavigationRegion.RemoveChild(node);
				node.QueueFree();
			}
			NavigationRegion.NavigationMesh.Clear();
		}

		RoomLayout?.Reset();
		CorridorConnector?.Reset();
		MobFactory?.Reset();
		RoomFactory?.Reset();
		TileFactory?.Reset();
	}

	/// <summary>
	/// Converts a world position to the master map tile that contains it. Inverse
	/// of <see cref="TileToWorld(int,int,int)"/>; tiles are centered on their cell.
	/// </summary>
	public Vector2I WorldToTile(Vector3 worldPosition)
	{
		var centerX = Map.Width / 2;
		var centerZ = Map.Height / 2;
		return new Vector2I(
			Mathf.RoundToInt(worldPosition.X / TileSize) + centerX,
			Mathf.RoundToInt(worldPosition.Z / TileSize) + centerZ);
	}

	/// <summary>
	/// Returns the id of the room that owns the given tile, or -1 if the tile is
	/// not part of any room (corridor, void, or out of bounds).
	/// </summary>
	public int GetRoomIdAt(Vector2I tile)
	{
		return _tileToRoom.TryGetValue(tile, out var id) ? id : -1;
	}

	/// <summary>
	/// Whether any sample on the tile under the given world position has been discovered.
	/// Used to gate monster/loot x-ray silhouettes to discovered areas, the
	/// same way <see cref="HasRevealedNeighbor"/> gates the door indicator.
	/// </summary>
	public bool IsRevealedAt(Vector3 worldPosition)
	{
		return _revealedTiles.Contains(WorldToTile(worldPosition));
	}

	private Vector3I TileToWorld(Vector3I tile)
	{
		return TileToWorld(tile.X, tile.Y, tile.Z);
	}

	private Vector3I TileToWorld(int x, int y, int z)
	{
		var centerX = (int)Map.Width / 2;
		var centerZ = (int)Map.Height / 2;
		return new Vector3I(
			(x - centerX) * (int)TileSize,
			y * (int)TileSize,
			(z - centerZ) * (int)TileSize);
	}

	/// <summary>
	/// Debug/tooling query: every connector tile's world position, whether it's an
	/// explicit doorway (guaranteed-connected) or an inferred edge (optional), and each
	/// open direction annotated with whether it actually leads to an open passage or is
	/// sealed by a generated wall. All-Godot-native return types so this is callable
	/// from a GDScript preview/verification script even though MapData itself (a plain
	/// C# class) can't marshal across that boundary -- see
	/// .agents/skills/godot-mcp/scripts/render_level_topdown.gd, which renders this
	/// over a top-down screenshot so doorway alignment can be checked visually instead
	/// of by reasoning about coordinates.
	/// </summary>
	public Godot.Collections.Array<Godot.Collections.Dictionary> GetConnectorDebugInfo()
	{
		var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				if (!Map.IsConnector(x, z))
				{
					continue;
				}

				var directionsInfo = new Godot.Collections.Array<Godot.Collections.Dictionary>();
				foreach (var direction in Map.GetConnectorDirections(x, z))
				{
					var outside = new Vector2I(x + direction.X, z + direction.Y);
					bool open = Map.IsWithinBounds(outside.X, outside.Y) && !Map.IsWallOrEmpty(outside.X, outside.Y);
					directionsInfo.Add(new Godot.Collections.Dictionary
					{
						{ "direction", new Vector3(direction.X, 0, direction.Y) },
						{ "open", open },
					});
				}

				result.Add(new Godot.Collections.Dictionary
				{
					{ "worldPosition", (Vector3)TileToWorld(x, 0, z) },
					{ "isDoorway", Map.IsDoorway(x, z) },
					{ "directions", directionsInfo },
				});
			}
		}

		return result;
	}
}