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

	// Lazily-built translucent overlay of the baked navigation mesh, toggled by the debug menu.
	private MeshInstance3D _navigationDebugMesh;

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
	private const int HorizontalWallOrientation = 0;
	private const int HorizontalWallWestHalfOrientation = 10;
	private const int VerticalWallOrientation = 16;
	private const int VerticalWallSouthHalfOrientation = 22;
	private const int NorthWestCornerOrientation = 16;
	private const int NorthEastCornerOrientation = 0;
	private const int SouthWestCornerOrientation = 10;
	private const int SouthEastCornerOrientation = 22;
	private const float EnemySpawnPlayerClearance = 20f;
	private const float EnemySpawnPointSpacing = 5f;
	private const float EnemySpawnBlockedAreaClearance = 7f;
	private const float EnemySpawnPropClearance = 2f;

	// Physics layers a runtime spawn must stay clear of: world (1) | walls (2) | props (5).
	// Keeps items out of stairs, transition blockers, walls, and props via a single overlap test.
	private const uint SpawnObstructionMask = 1u | 2u | 16u;
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
				if ((Map.IsCorridor(x, z) || Map.IsConnector(x, z)) && FloorGridMap.GetCellItem(position) < 0)
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
		var cornerEdges = new System.Collections.Generic.Dictionary<Vector2I, WallCornerEdges>();
		var occupiedWallSpans = BuildOccupiedWallSpans();

		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				if (IsWallSourceTile(x, z))
				{
					PlaceWallModulesForTile(x, z, cornerEdges, occupiedWallSpans);
				}
			}
		}

		PlaceWallCorners(cornerEdges, occupiedWallSpans);
	}

	private void PlaceWallModulesForTile(int x, int z, System.Collections.Generic.Dictionary<Vector2I, WallCornerEdges> cornerEdges, HashSet<WallSpan> occupiedWallSpans)
	{
		int tileCenter = (int)TileSize / 2;
		var basePosition = TileToWorld(x, 0, z);
		bool north = NeedsGeneratedWallAgainst(x, z - 1);
		bool south = NeedsGeneratedWallAgainst(x, z + 1);
		bool west = NeedsGeneratedWallAgainst(x - 1, z);
		bool east = NeedsGeneratedWallAgainst(x + 1, z);
		int westX = basePosition.X - tileCenter;
		int eastX = basePosition.X + tileCenter;
		int northZ = basePosition.Z - tileCenter;
		int southZ = basePosition.Z + tileCenter;

		if (north)
		{
			AddHorizontalCornerEdge(cornerEdges, new Vector2I(westX, northZ), extendsEast: true);
			AddHorizontalCornerEdge(cornerEdges, new Vector2I(eastX, northZ), extendsEast: false);
			PlaceWallStraight(basePosition + new Vector3I(0, 0, -tileCenter), HorizontalWallOrientation, occupiedWallSpans);
		}

		if (south)
		{
			AddHorizontalCornerEdge(cornerEdges, new Vector2I(westX, southZ), extendsEast: true);
			AddHorizontalCornerEdge(cornerEdges, new Vector2I(eastX, southZ), extendsEast: false);
			PlaceWallStraight(basePosition + new Vector3I(0, 0, tileCenter), HorizontalWallOrientation, occupiedWallSpans);
		}

		if (west)
		{
			AddVerticalCornerEdge(cornerEdges, new Vector2I(westX, northZ), extendsSouth: true);
			AddVerticalCornerEdge(cornerEdges, new Vector2I(westX, southZ), extendsSouth: false);
			PlaceWallStraight(basePosition + new Vector3I(-tileCenter, 0, 0), VerticalWallOrientation, occupiedWallSpans);
		}

		if (east)
		{
			AddVerticalCornerEdge(cornerEdges, new Vector2I(eastX, northZ), extendsSouth: true);
			AddVerticalCornerEdge(cornerEdges, new Vector2I(eastX, southZ), extendsSouth: false);
			PlaceWallStraight(basePosition + new Vector3I(tileCenter, 0, 0), VerticalWallOrientation, occupiedWallSpans);
		}
	}

	private void AddHorizontalCornerEdge(System.Collections.Generic.Dictionary<Vector2I, WallCornerEdges> cornerEdges, Vector2I position, bool extendsEast)
	{
		cornerEdges.TryGetValue(position, out var edges);
		if (extendsEast)
		{
			edges.HorizontalEast = true;
		}
		else
		{
			edges.HorizontalWest = true;
		}

		cornerEdges[position] = edges;
	}

	private void AddVerticalCornerEdge(System.Collections.Generic.Dictionary<Vector2I, WallCornerEdges> cornerEdges, Vector2I position, bool extendsSouth)
	{
		cornerEdges.TryGetValue(position, out var edges);
		if (extendsSouth)
		{
			edges.VerticalSouth = true;
		}
		else
		{
			edges.VerticalNorth = true;
		}

		cornerEdges[position] = edges;
	}

	private void PlaceWallCorners(System.Collections.Generic.Dictionary<Vector2I, WallCornerEdges> cornerEdges, HashSet<WallSpan> occupiedWallSpans)
	{
		foreach (var (position, edges) in cornerEdges)
		{
			if (edges.HorizontalEast && edges.VerticalSouth)
			{
				PlaceWallCorner(position, NorthWestCornerOrientation, occupiedWallSpans);
			}
			else if (edges.HorizontalWest && edges.VerticalSouth)
			{
				PlaceWallCorner(position, NorthEastCornerOrientation, occupiedWallSpans);
			}
			else if (edges.HorizontalEast && edges.VerticalNorth)
			{
				PlaceWallCorner(position, SouthWestCornerOrientation, occupiedWallSpans);
			}
			else if (edges.HorizontalWest && edges.VerticalNorth)
			{
				PlaceWallCorner(position, SouthEastCornerOrientation, occupiedWallSpans);
			}
		}
	}

	private void PlaceWallCorner(Vector2I position, int orientation, HashSet<WallSpan> occupiedWallSpans)
	{
		var cellPosition = new Vector3I(position.X, 0, position.Y);
		var coverage = GetCornerWallCoverage(orientation);

		if (!WallCoverageOccupied(cellPosition, coverage, occupiedWallSpans)
			&& SetGeneratedWallCell(cellPosition, TileFactory.GetWallCornerTileIndex(), orientation, coverage, occupiedWallSpans))
		{
			return;
		}

		PlaceWallHalves(cellPosition, coverage, occupiedWallSpans);
	}

	private void PlaceWallStraight(Vector3I position, int orientation, HashSet<WallSpan> occupiedWallSpans)
	{
		var footprint = orientation == HorizontalWallOrientation
			? GeneratedWallFootprint.Horizontal
			: GeneratedWallFootprint.Vertical;
		bool startHalfOccupied = WallHalfSpanOccupied(position, footprint, startHalf: true, occupiedWallSpans);
		bool endHalfOccupied = WallHalfSpanOccupied(position, footprint, startHalf: false, occupiedWallSpans);

		if (!startHalfOccupied && !endHalfOccupied)
		{
			var coverage = GetStraightWallCoverage(footprint);
			if (SetGeneratedWallCell(position, TileFactory.GetWallTileIndex(), orientation, coverage, occupiedWallSpans))
			{
				return;
			}

			PlaceWallHalf(position, footprint, startHalf: true, occupiedWallSpans);
			PlaceWallHalf(position, footprint, startHalf: false, occupiedWallSpans);
			return;
		}

		if (!startHalfOccupied)
		{
			PlaceWallHalf(position, footprint, startHalf: true, occupiedWallSpans);
		}

		if (!endHalfOccupied)
		{
			PlaceWallHalf(position, footprint, startHalf: false, occupiedWallSpans);
		}
	}

	private void PlaceWallHalves(Vector3I position, WallCoverage coverage, HashSet<WallSpan> occupiedWallSpans)
	{
		if (coverage.HasFlag(WallCoverage.HorizontalWest))
		{
			PlaceWallHalf(position, GeneratedWallFootprint.Horizontal, startHalf: true, occupiedWallSpans);
		}

		if (coverage.HasFlag(WallCoverage.HorizontalEast))
		{
			PlaceWallHalf(position, GeneratedWallFootprint.Horizontal, startHalf: false, occupiedWallSpans);
		}

		if (coverage.HasFlag(WallCoverage.VerticalNorth))
		{
			PlaceWallHalf(position, GeneratedWallFootprint.Vertical, startHalf: true, occupiedWallSpans);
		}

		if (coverage.HasFlag(WallCoverage.VerticalSouth))
		{
			PlaceWallHalf(position, GeneratedWallFootprint.Vertical, startHalf: false, occupiedWallSpans);
		}
	}

	private bool PlaceWallHalf(Vector3I position, GeneratedWallFootprint footprint, bool startHalf, HashSet<WallSpan> occupiedWallSpans)
	{
		if (WallHalfSpanOccupied(position, footprint, startHalf, occupiedWallSpans))
		{
			return false;
		}

		int tileCenter = (int)TileSize / 2;
		int tileIndex = TileFactory.GetWallHalfTileIndex();

		// A wall_half covers one side of its anchor. If the center anchor is already
		// used by an authored wall, the same 2-cell span can be represented from the
		// opposite endpoint with the opposite orientation.
		if (footprint == GeneratedWallFootprint.Horizontal)
		{
			return startHalf
				? SetGeneratedWallCell(position, tileIndex, HorizontalWallWestHalfOrientation, WallCoverage.HorizontalWest, occupiedWallSpans)
					|| SetGeneratedWallCell(position + new Vector3I(-tileCenter, 0, 0), tileIndex, HorizontalWallOrientation, WallCoverage.HorizontalEast, occupiedWallSpans)
				: SetGeneratedWallCell(position, tileIndex, HorizontalWallOrientation, WallCoverage.HorizontalEast, occupiedWallSpans)
					|| SetGeneratedWallCell(position + new Vector3I(tileCenter, 0, 0), tileIndex, HorizontalWallWestHalfOrientation, WallCoverage.HorizontalWest, occupiedWallSpans);
		}

		return startHalf
			? SetGeneratedWallCell(position, tileIndex, VerticalWallOrientation, WallCoverage.VerticalNorth, occupiedWallSpans)
				|| SetGeneratedWallCell(position + new Vector3I(0, 0, -tileCenter), tileIndex, VerticalWallSouthHalfOrientation, WallCoverage.VerticalSouth, occupiedWallSpans)
			: SetGeneratedWallCell(position, tileIndex, VerticalWallSouthHalfOrientation, WallCoverage.VerticalSouth, occupiedWallSpans)
				|| SetGeneratedWallCell(position + new Vector3I(0, 0, tileCenter), tileIndex, VerticalWallOrientation, WallCoverage.VerticalNorth, occupiedWallSpans);
	}

	private bool SetGeneratedWallCell(Vector3I position, int tileIndex, int orientation, WallCoverage coverage, HashSet<WallSpan> occupiedWallSpans)
	{
		if (WallGridMap.GetCellItem(position) >= 0)
		{
			return false;
		}

		if (WallCoverageOccupied(position, coverage, occupiedWallSpans))
		{
			return false;
		}

		WallGridMap.SetCellItem(position, tileIndex, orientation);
		AddWallSpans(occupiedWallSpans, position, coverage);
		return true;
	}

	private HashSet<WallSpan> BuildOccupiedWallSpans()
	{
		var occupiedWallSpans = new HashSet<WallSpan>();
		foreach (Vector3I cell in WallGridMap.GetUsedCells())
		{
			int tileIndex = WallGridMap.GetCellItem(cell);
			if (tileIndex < 0)
			{
				continue;
			}

			AddWallSpans(occupiedWallSpans, cell, GetWallCoverage(tileIndex, WallGridMap.GetCellItemOrientation(cell)));
		}

		return occupiedWallSpans;
	}

	private void AddWallSpans(HashSet<WallSpan> occupiedWallSpans, Vector3I position, WallCoverage coverage)
	{
		if (coverage.HasFlag(WallCoverage.HorizontalWest))
		{
			occupiedWallSpans.Add(GetWallHalfSpan(position, GeneratedWallFootprint.Horizontal, startHalf: true));
		}

		if (coverage.HasFlag(WallCoverage.HorizontalEast))
		{
			occupiedWallSpans.Add(GetWallHalfSpan(position, GeneratedWallFootprint.Horizontal, startHalf: false));
		}

		if (coverage.HasFlag(WallCoverage.VerticalNorth))
		{
			occupiedWallSpans.Add(GetWallHalfSpan(position, GeneratedWallFootprint.Vertical, startHalf: true));
		}

		if (coverage.HasFlag(WallCoverage.VerticalSouth))
		{
			occupiedWallSpans.Add(GetWallHalfSpan(position, GeneratedWallFootprint.Vertical, startHalf: false));
		}
	}

	private bool WallCoverageOccupied(Vector3I position, WallCoverage coverage, HashSet<WallSpan> occupiedWallSpans)
	{
		return coverage.HasFlag(WallCoverage.HorizontalWest) && WallHalfSpanOccupied(position, GeneratedWallFootprint.Horizontal, startHalf: true, occupiedWallSpans)
			|| coverage.HasFlag(WallCoverage.HorizontalEast) && WallHalfSpanOccupied(position, GeneratedWallFootprint.Horizontal, startHalf: false, occupiedWallSpans)
			|| coverage.HasFlag(WallCoverage.VerticalNorth) && WallHalfSpanOccupied(position, GeneratedWallFootprint.Vertical, startHalf: true, occupiedWallSpans)
			|| coverage.HasFlag(WallCoverage.VerticalSouth) && WallHalfSpanOccupied(position, GeneratedWallFootprint.Vertical, startHalf: false, occupiedWallSpans);
	}

	private bool WallHalfSpanOccupied(Vector3I position, GeneratedWallFootprint footprint, bool startHalf, HashSet<WallSpan> occupiedWallSpans)
	{
		return occupiedWallSpans.Contains(GetWallHalfSpan(position, footprint, startHalf));
	}

	private WallSpan GetWallHalfSpan(Vector3I position, GeneratedWallFootprint footprint, bool startHalf)
	{
		int tileCenter = (int)TileSize / 2;
		if (footprint == GeneratedWallFootprint.Horizontal)
		{
			return startHalf
				? new WallSpan(position + new Vector3I(-tileCenter, 0, 0), position)
				: new WallSpan(position, position + new Vector3I(tileCenter, 0, 0));
		}

		return startHalf
			? new WallSpan(position + new Vector3I(0, 0, -tileCenter), position)
			: new WallSpan(position, position + new Vector3I(0, 0, tileCenter));
	}

	private WallCoverage GetWallCoverage(int tileIndex, int orientation)
	{
		if (tileIndex == TileFactory.GetWallHalfTileIndex())
		{
			return GetHalfWallCoverage(orientation);
		}

		if (tileIndex == TileFactory.GetWallCornerTileIndex())
		{
			return GetCornerWallCoverage(orientation);
		}

		return orientation == VerticalWallOrientation || orientation == VerticalWallSouthHalfOrientation
			? GetStraightWallCoverage(GeneratedWallFootprint.Vertical)
			: GetStraightWallCoverage(GeneratedWallFootprint.Horizontal);
	}

	private WallCoverage GetStraightWallCoverage(GeneratedWallFootprint footprint)
	{
		return footprint == GeneratedWallFootprint.Horizontal
			? WallCoverage.HorizontalWest | WallCoverage.HorizontalEast
			: WallCoverage.VerticalNorth | WallCoverage.VerticalSouth;
	}

	private WallCoverage GetHalfWallCoverage(int orientation)
	{
		return orientation switch
		{
			HorizontalWallWestHalfOrientation => WallCoverage.HorizontalWest,
			VerticalWallOrientation => WallCoverage.VerticalNorth,
			VerticalWallSouthHalfOrientation => WallCoverage.VerticalSouth,
			_ => WallCoverage.HorizontalEast,
		};
	}

	private WallCoverage GetCornerWallCoverage(int orientation)
	{
		return orientation switch
		{
			NorthWestCornerOrientation => WallCoverage.HorizontalEast | WallCoverage.VerticalSouth,
			NorthEastCornerOrientation => WallCoverage.HorizontalWest | WallCoverage.VerticalSouth,
			SouthWestCornerOrientation => WallCoverage.HorizontalEast | WallCoverage.VerticalNorth,
			_ => WallCoverage.HorizontalWest | WallCoverage.VerticalNorth,
		};
	}

	private enum GeneratedWallFootprint
	{
		Point,
		Horizontal,
		Vertical,
	}

	[Flags]
	private enum WallCoverage
	{
		None = 0,
		HorizontalWest = 1,
		HorizontalEast = 2,
		VerticalNorth = 4,
		VerticalSouth = 8,
	}

	private readonly record struct WallSpan(Vector3I Start, Vector3I End);

	private bool IsWallSourceTile(int x, int z)
	{
		return Map.IsWithinBounds(x, z)
			&& (Map.IsRoom(x, z) || Map.IsConnector(x, z) || Map.IsCorridor(x, z));
	}

	private bool NeedsGeneratedWallAgainst(int x, int z)
	{
		return !Map.IsWithinBounds(x, z) || Map.IsWallOrEmpty(x, z);
	}

	private struct WallCornerEdges
	{
		public bool HorizontalWest;
		public bool HorizontalEast;
		public bool VerticalNorth;
		public bool VerticalSouth;
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
		if (!TryFindNearestDoorConnector(worldPosition, out Vector2I connectorTile))
		{
			return null;
		}

		OpenConnector(connectorTile);
		return connectorTile;
	}

	/// <summary>
	/// Marks the connector guarded by a closed door as sealed again. Fog remains revealed;
	/// this only updates logical reachability for enemy decisions.
	/// </summary>
	public Vector2I? CloseDoorAt(Vector3 worldPosition)
	{
		if (!TryFindNearestDoorConnector(worldPosition, out Vector2I connectorTile))
		{
			return null;
		}

		_dooredConnectors.Add(connectorTile);
		UpdateDoorIndicators();
		return connectorTile;
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

	/// <summary>
	/// Clears gameplay fog across the generated level for runtime debugging.
	/// </summary>
	public void RevealAllFog()
	{
		if (OcclusionGridMap == null)
		{
			return;
		}

		OcclusionGridMap.Clear();
		PlaceOcclusion(false);

		foreach (var (door, _) in _doorIndicators)
		{
			door.SetIndicatorVisible(true);
		}
	}

	private void OpenConnector(Vector2I connector)
	{
		_dooredConnectors.Remove(connector);
		RevealRoom(_connectorToRoom[connector]);
	}

	/// <summary>
	/// Returns whether two world positions are connected by room/corridor tiles without
	/// crossing a connector that still has a closed door. This gates enemy target
	/// decisions; movement still follows the baked navmesh once a target is reachable.
	/// </summary>
	public bool CanReachWithoutOpeningDoors(Vector3 fromWorldPosition, Vector3 toWorldPosition)
	{
		if (Map == null)
		{
			return true;
		}

		Vector2I from = WorldToTile(fromWorldPosition);
		Vector2I to = WorldToTile(toWorldPosition);
		if (!IsDoorReachabilityTile(from) || !IsDoorReachabilityTile(to))
		{
			return true;
		}

		var queue = new Queue<Vector2I>();
		var visited = new HashSet<Vector2I> { from };
		queue.Enqueue(from);

		while (queue.Count > 0)
		{
			Vector2I tile = queue.Dequeue();
			if (tile == to)
			{
				return true;
			}

			foreach (Vector2I offset in CardinalOffsets)
			{
				Vector2I neighbor = tile + offset;
				if (visited.Contains(neighbor) || !IsDoorReachabilityTile(neighbor))
				{
					continue;
				}

				visited.Add(neighbor);
				queue.Enqueue(neighbor);
			}
		}

		return false;
	}

	private bool IsDoorReachabilityTile(Vector2I tile)
	{
		return Map.IsWithinBounds(tile.X, tile.Y)
			&& !_dooredConnectors.Contains(tile)
			&& (Map.IsRoom(tile.X, tile.Y) || Map.IsConnector(tile.X, tile.Y) || Map.IsCorridor(tile.X, tile.Y));
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

		if (NavigationRegion.NavigationMesh.GetVertices().Length == 0)
		{
			GD.PrintErr("Cannot generate enemy spawn points: navigation mesh has no vertices.");
			return;
		}

		var candidates = GetEnemySpawnCandidates();
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

	private List<Vector3> GetEnemySpawnCandidates()
	{
		var candidates = new List<Vector3>();
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				if ((Map.IsRoom(x, z) || Map.IsCorridor(x, z)) && !IsOccupiedSpawnTile(x, z))
				{
					candidates.Add(TileToWorld(x, 0, z));
				}
			}
		}

		return candidates;
	}

	private bool IsOccupiedSpawnTile(int x, int z)
	{
		return HasDecorationInTile(x, z) || HasPropInTile(x, z);
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
			int index = (int)(GD.Randi() % (ulong)remainingCandidates.Count);
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

			if ((node is PlayerSpawnPoint || node is LevelTransitionTrigger || node.IsInGroup("stairs"))
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
		SetDoorNavigationBakeCollisionEnabled(false);
		try
		{
			NavigationRegion.AddChild(floorGridMapCopy);
			NavigationRegion.AddChild(wallGripMapCopy);
			NavigationRegion.AddChild(decorationGridMapCopy);
			NavigationRegion.BakeNavigationMesh(false);
		}
		finally
		{
			SetDoorNavigationBakeCollisionEnabled(true);
			floorGridMapCopy.QueueFree();
			wallGripMapCopy.QueueFree();
			decorationGridMapCopy.QueueFree();
		}
	}

	private void SetDoorNavigationBakeCollisionEnabled(bool enabled)
	{
		foreach (var (door, _) in _doorIndicators)
		{
			door.SetNavigationBakeCollisionEnabled(enabled);
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

		if (visible && (_navigationDebugMesh == null || !IsInstanceValid(_navigationDebugMesh)))
		{
			_navigationDebugMesh = BuildNavigationDebugMesh();
		}

		if (_navigationDebugMesh != null && IsInstanceValid(_navigationDebugMesh))
		{
			_navigationDebugMesh.Visible = visible;
		}
	}

	private MeshInstance3D BuildNavigationDebugMesh()
	{
		NavigationMesh navigationMesh = NavigationRegion.NavigationMesh;
		Vector3[] vertices = navigationMesh.GetVertices();
		if (vertices.Length == 0)
		{
			return null;
		}

		var surfaceTool = new SurfaceTool();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);
		for (int polygonIndex = 0; polygonIndex < navigationMesh.GetPolygonCount(); polygonIndex++)
		{
			int[] polygon = navigationMesh.GetPolygon(polygonIndex);
			// Fan-triangulate each convex navmesh polygon.
			for (int corner = 2; corner < polygon.Length; corner++)
			{
				surfaceTool.AddVertex(vertices[polygon[0]]);
				surfaceTool.AddVertex(vertices[polygon[corner - 1]]);
				surfaceTool.AddVertex(vertices[polygon[corner]]);
			}
		}

		var material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			AlbedoColor = new Color(0.1f, 0.6f, 1f, 0.35f),
		};

		var meshInstance = new MeshInstance3D
		{
			Name = "NavigationDebugMesh",
			Mesh = surfaceTool.Commit(),
			MaterialOverride = material,
			// Lift slightly off the floor to avoid z-fighting with the ground meshes.
			Position = new Vector3(0, 0.1f, 0),
		};

		NavigationRegion.AddChild(meshInstance);
		return meshInstance;
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
