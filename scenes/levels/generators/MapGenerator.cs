using System;
using System.Collections.Generic;
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
	[Export] public TileFactory TileFactory { get; set => SetProperty(ref field, value); }

	public MapData Map;
	public GridMap FloorGridMap { get; private set; }
	public GridMap WallGridMap { get; private set; }
	public GridMap DecorationGridMap { get; private set; }
	public GridMap OcclusionGridMap { get; private set; }
	public NavigationRegion3D NavigationRegion { get; private set; }

	private readonly List<RoomRegion> _roomRegions = new();
	// Interior floor tile -> room id, for "which room is the player in" detection.
	private readonly System.Collections.Generic.Dictionary<Vector2I, int> _tileToRoom = new();
	// Connector tile -> room id (every connector). Drives the reveal cascade.
	private readonly System.Collections.Generic.Dictionary<Vector2I, int> _connectorToRoom = new();
	// Connectors guarded by a door; the reveal cascade stops here until it opens.
	private readonly HashSet<Vector2I> _dooredConnectors = new();
	private readonly HashSet<Vector2I> _revealedTiles = new();
	// Doors paired with the connector they guard, for toggling their x-ray indicator.
	private readonly List<(Door Door, Vector2I Connector)> _doorIndicators = new();

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
	/// Y level the flat void occluder caps sit at (one tile up, i.e. wall-top
	/// height). A horizontal cap has no tall front face, so it hides the void
	/// without clipping rooms behind it in the isometric view. Tune visually.
	/// </summary>
	public readonly float OcclusionCapHeight = 4f;

	private const int OcclusionItemId = 0;

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

	private void GenerateRooms()
	{
		GD.Print("Generating rooms...");

		var roomPlacements = RoomLayout.GenerateRooms(
			Map, RoomFactory, MaxRooms);
		PlaceRooms(roomPlacements);
	}

	private void PlaceRooms(List<RoomPlacement> roomPlacements)
	{
		GD.Print("Placing rooms...");
		foreach (var placement in roomPlacements)
		{
			var room = placement.Room;
			var position = new Vector3I(placement.Position.X, 0, placement.Position.Y);
			MergeRoomGridMaps(room, position);

			// Make the room a child of the navigation region, so
			// it is included in the navigation mesh and enemies
			// avoid obstactles in it.
			NavigationRegion.AddChild(room);

			var roomOffset = new Vector3I(room.Bounds.Position.X, 0, room.Bounds.Position.Y);
			room.Translate(TileToWorld(position) - roomOffset);

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

		var region = new RoomRegion(_roomRegions.Count);
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
				// Connectors are doorway thresholds: counting them would reveal a room
				// when the player merely stands against its closed door, since the
				// position rounds onto the connector tile.
				if (isRoom)
				{
					_tileToRoom[tile] = region.Id;
				}

				if (isConnector)
				{
					region.ConnectorTiles.Add(tile);
					_connectorToRoom[tile] = region.Id;
				}
			}
		}

		RegisterDoors(placement.Room, region);
		_roomRegions.Add(region);
	}

	/// <summary>
	/// Marks the connectors guarded by hand-placed Door props so fog reveal stops
	/// at them. A door is matched to the nearest connector of its own room.
	/// </summary>
	private void RegisterDoors(Room room, RoomRegion region)
	{
		if (region.ConnectorTiles.Count == 0)
		{
			return;
		}

		foreach (Node node in room.FindChildren("*", "", true, false))
		{
			if (node is not Door door)
			{
				continue;
			}

			// Candidates only: which doorways are real passages isn't known until
			// corridors have been routed (see FinalizeDoors).
			if (TryFindNearestConnector(region, door.GlobalPosition, out var connector))
			{
				_doorIndicators.Add((door, connector));
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

	private void ConnectRooms()
	{
		GD.Print("Connecting rooms...");

		CorridorConnector.ConnectRooms(Map);

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
				if (Map.IsCorridor(x, z) && FloorGridMap.GetCellItem(position) < 0)
				{
					int tileIndex = TileFactory.GetCorridorTileIndex();
					FloorGridMap.SetCellItem(position, tileIndex, 0);
				}
			}
		}
	}

	private void PlaceWalls()
	{
		GD.Print("Placing walls...");
		// Set wall tiles in the GridMap
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				// Room scenes may intentionally omit perimeter wall segments for doorway
				// authoring. Fill exposed room edges after corridors have been routed so
				// unused doorway candidates are closed while connected doorways stay open.
				if (Map.IsRoom(x, z) || Map.IsConnector(x, z) || Map.IsCorridor(x, z))
				{
					// Check for wall adjacency and place walls
					PlaceWallIfNeeded(x, z);
				}
			}
		}
	}

	private void PlaceWallIfNeeded(int x, int z)
	{
		// Check each direction for wall adjacency
		Vector3I basePosition = TileToWorld(x, 0, z);
		int tileCenter = (int)TileSize / 2;
		int tileIndex = TileFactory.GetWallTileIndex();

		// Check above (north)
		if (z > 0 && Map.IsWallOrEmpty(x, z - 1)) // Wall above
		{
			WallGridMap.SetCellItem(basePosition + new Vector3I(0, 0, -tileCenter), tileIndex, 0);
		}

		// Check below (south)
		if (z < Map.Height - 1 && Map.IsWallOrEmpty(x, z + 1)) // Wall below
		{
			WallGridMap.SetCellItem(basePosition + new Vector3I(0, 0, tileCenter), tileIndex, 0);
		}

		// Check left (west)
		if (x > 0 && Map.IsWallOrEmpty(x - 1, z)) // Wall to the left
		{
			WallGridMap.SetCellItem(basePosition + new Vector3I(-tileCenter, 0, 0), tileIndex, 16);
		}

		// Check right (east)
		if (x < Map.Width - 1 && Map.IsWallOrEmpty(x + 1, z)) // Wall to the right
		{
			WallGridMap.SetCellItem(basePosition + new Vector3I(tileCenter, 0, 0), tileIndex, 16);
		}
	}

	/// <summary>
	/// Builds the single-item mesh library used by the occlusion grid: an unlit
	/// black horizontal cap that covers one logical tile footprint at wall-top
	/// height. Contiguous void tiles tile into a continuous flat black roof.
	/// </summary>
	private MeshLibrary BuildOcclusionMeshLibrary()
	{
		var material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = Colors.Black,
			// Visible from both sides so the cap reads as black at any camera yaw.
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};

		var plane = new PlaneMesh
		{
			Size = new Vector2(TileSize, TileSize),
			Material = material,
		};

		var library = new MeshLibrary();
		library.CreateItem(OcclusionItemId);
		library.SetItemName(OcclusionItemId, "void_occluder");
		library.SetItemMesh(OcclusionItemId, plane);
		// Floor tiles are centered on their cell, so the cap centers in X/Z; lift it
		// to wall-top height so it forms a flat roof over the void.
		library.SetItemMeshTransform(
			OcclusionItemId,
			new Transform3D(Basis.Identity, new Vector3(0, OcclusionCapHeight, 0)));

		return library;
	}

	/// <summary>
	/// Places the black occluder caps across the map (plus a margin band so edge
	/// rooms' outer walls stay hidden). When <paramref name="fog"/> is set every
	/// tile is covered so rooms start hidden and are revealed during play; without
	/// it only the void/border is covered and interior surfaces stay visible.
	/// </summary>
	private void PlaceOcclusion(bool fog)
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
				bool isVisibleInterior = Map.IsWithinBounds(x, z)
					&& (Map.IsRoom(x, z) || Map.IsConnector(x, z)
						|| Map.IsCorridor(x, z) || Map.IsChasm(x, z));
				if (fog || !isVisibleInterior)
				{
					OcclusionGridMap.SetCellItem(TileToWorld(x, 0, z), OcclusionItemId, 0);
				}
			}
		}
	}

	/// <summary>
	/// Reveals a room and everything reachable from it without crossing a door:
	/// the room footprint, its connected corridors, and any further rooms joined by
	/// door-free connectors. A doored connector blocks the cascade so the corridor
	/// beyond a closed door stays hidden, but the doorway tile itself (part of the
	/// room) is revealed; the closed door and the hidden corridor are the seal.
	/// </summary>
	public void RevealRoom(int roomId)
	{
		if (roomId < 0 || roomId >= _roomRegions.Count || OcclusionGridMap == null)
		{
			return;
		}

		var roomQueue = new Queue<int>();
		var queuedRooms = new HashSet<int> { roomId };
		roomQueue.Enqueue(roomId);

		while (roomQueue.Count > 0)
		{
			var region = _roomRegions[roomQueue.Dequeue()];
			foreach (var tile in region.Tiles)
			{
				RevealTile(tile);
			}

			foreach (var connector in region.ConnectorTiles)
			{
				if (_dooredConnectors.Contains(connector))
				{
					continue; // The door gates the cascade.
				}

				FloodCorridors(connector, roomQueue, queuedRooms);
			}
		}

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
	/// Unseals the doored connector nearest the opened door and reveals through it,
	/// so opening a door flows the fog into the corridor and rooms beyond. Returns
	/// the unsealed connector tile (for persistence), or null if none matched.
	/// </summary>
	public Vector2I? OpenDoorAt(Vector3 worldPosition)
	{
		Vector2I? nearest = null;
		float bestDistance = float.MaxValue;
		foreach (var connector in _dooredConnectors)
		{
			var center = TileToWorld(connector.X, 0, connector.Y);
			float dx = worldPosition.X - center.X;
			float dz = worldPosition.Z - center.Z;
			float distance = dx * dx + dz * dz;
			if (distance < bestDistance)
			{
				bestDistance = distance;
				nearest = connector;
			}
		}

		if (nearest is not Vector2I connectorTile)
		{
			return null;
		}

		OpenConnector(connectorTile);
		return connectorTile;
	}

	/// <summary>
	/// Restores a previously explored level: re-opens the doors that were opened and
	/// re-reveals the rooms that were entered. Combined with deterministic map
	/// generation, this reproduces the exact area the player had uncovered.
	/// </summary>
	public void RestoreReveal(IEnumerable<int> revealedRoomIds, IEnumerable<Vector2I> openedDoors)
	{
		// Unseal every opened door first so cascades can flow through all of them.
		foreach (var connector in openedDoors)
		{
			_dooredConnectors.Remove(connector);
		}

		foreach (var roomId in revealedRoomIds)
		{
			RevealRoom(roomId);
		}

		// Reveal through doors opened from the corridor side (no room was entered).
		foreach (var connector in openedDoors)
		{
			if (_connectorToRoom.TryGetValue(connector, out int roomId))
			{
				RevealRoom(roomId);
			}
		}
	}

	private void OpenConnector(Vector2I connector)
	{
		_dooredConnectors.Remove(connector);
		RevealRoom(_connectorToRoom[connector]);
	}

	private void FloodCorridors(Vector2I startConnector, Queue<int> roomQueue,
		HashSet<int> queuedRooms)
	{
		var queue = new Queue<Vector2I>();
		var visited = new HashSet<Vector2I>();

		foreach (var offset in CardinalOffsets)
		{
			var seed = startConnector + offset;
			if (Map.IsWithinBounds(seed.X, seed.Y) && Map.IsCorridor(seed.X, seed.Y)
				&& visited.Add(seed))
			{
				queue.Enqueue(seed);
			}
		}

		while (queue.Count > 0)
		{
			var tile = queue.Dequeue();
			RevealTile(tile);
			foreach (var offset in CardinalOffsets)
			{
				var n = tile + offset;
				if (!Map.IsWithinBounds(n.X, n.Y))
				{
					continue;
				}

				if (Map.IsCorridor(n.X, n.Y))
				{
					if (visited.Add(n))
					{
						queue.Enqueue(n);
					}
				}
				else if (Map.IsConnector(n.X, n.Y))
				{
					// Reached another room. A doored connector stays sealed (black); an
					// open one cascades into that room.
					if (!_dooredConnectors.Contains(n)
						&& _connectorToRoom.TryGetValue(n, out int otherRoom)
						&& queuedRooms.Add(otherRoom))
					{
						roomQueue.Enqueue(otherRoom);
					}
				}
			}
		}
	}

	private void RevealTile(Vector2I tile)
	{
		if (!_revealedTiles.Add(tile))
		{
			return;
		}

		// -1 clears the cell (GridMap.InvalidCellItem).
		OcclusionGridMap.SetCellItem(TileToWorld(tile.X, 0, tile.Y), -1);
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

	private void GenerateEnemySpawnPoints()
	{
		uint mobCount = 3 + DungeonDepth % 5 + (GD.Randi() % 3);
		GD.Print($"Generating {mobCount} enemy spawn points...");

		var points = NavigationRegion.NavigationMesh.GetVertices();
		if (points.Length == 0)
		{
			GD.PrintErr("Cannot generate enemy spawn points: navigation mesh has no vertices.");
			return;
		}

		var spawnPoints = new List<Vector3>();
		for (int i = 0; i < mobCount; i++)
		{
			Vector3 point;
			bool isValidPoint;
			int attempts = 100;

			do
			{
				isValidPoint = true;
				point = points[(int)(GD.Randi() % (ulong)points.Length)];
				point.Y = 0f; // Ensure the point is on the ground

				// Ensure the point is at least 20 meters away from the player
				if (PlayerSpawnPoint != null && point.DistanceTo(PlayerSpawnPoint.GlobalPosition) < 20)
				{
					isValidPoint = false;
					continue;
				}

				// Ensure the point is at least 5 meters away from other spawn points
				foreach (var spawnPoint in spawnPoints)
				{
					if (point.DistanceTo(spawnPoint) < 5)
					{
						isValidPoint = false;
						break;
					}
				}
			} while (!isValidPoint && attempts-- > 0);

			if (!isValidPoint)
			{
				GD.PrintErr("Could not find a valid enemy spawn point.");
				continue;
			}

			spawnPoints.Add(point);

			// Enemy scenes are loaded lazily by the factory to keep unused variants out of memory.
			var enemyScene = MobFactory.CreateEnemy(DungeonDepth);
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
			spawnPointNode.Rotation = new Vector3(0, (float)(GD.Randf() * 2 * Math.PI), 0);

			EnemySpawnPoints.Add(spawnPointNode);
		}
	}

	public void GenerateMap(bool includeGameplay = true)
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

		// Step 2c: Place the black occluder caps. In gameplay the whole map starts
		// covered (fog of war) and rooms are carved out as the player explores; in
		// the editor preview only the void is covered so the layout stays visible.
		PlaceOcclusion(includeGameplay);

		if (!includeGameplay)
		{
			GD.Print("Map preview generated.");
			return;
		}

		// Step 3: Bake navigation mesh
		BakeNavigationMesh();

		// Step 4: Create spawn points
		SetPlayerSpawnPoint();
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
		NavigationRegion.AddChild(floorGridMapCopy);
		NavigationRegion.AddChild(wallGripMapCopy);
		NavigationRegion.AddChild(decorationGridMapCopy);
		NavigationRegion.BakeNavigationMesh(false);
		floorGridMapCopy.QueueFree();
		wallGripMapCopy.QueueFree();
		decorationGridMapCopy.QueueFree();
	}

	private void Reset()
	{
		GD.Print("Resetting map generator...");
		GD.Seed(Seed);

		_roomRegions.Clear();
		_tileToRoom.Clear();
		_revealedTiles.Clear();
		_connectorToRoom.Clear();
		_dooredConnectors.Clear();
		_doorIndicators.Clear();

		// Initialize the map with empty tiles
		Map = new MapData((int)MapWidth, (int)MapDepth);

		// Initialize all tiles as empty and walls for borders
		for (int x = 0; x < Map.Width; x++)
		{
			for (int y = 0; y < Map.Height; y++)
			{
				Map.Tiles[x, y] = MapTile.Empty;

				if (x == 0 || x == Map.Width - 1 || y == 0 || y == Map.Height - 1)
				{
					Map.Tiles[x, y] = MapTile.Wall;
				}
			}
		}

		FloorGridMap?.Clear();
		WallGridMap?.Clear();
		DecorationGridMap?.Clear();
		OcclusionGridMap?.Clear();

		PlayerSpawnPoint = null;
		if (EnemySpawnPoints != null)
		{
			foreach (var spawnPoint in EnemySpawnPoints)
			{
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
}
