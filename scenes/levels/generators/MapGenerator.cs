using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;

[Tool]
public partial class MapGenerator : Node3D
{
	[Export]
	public uint DungeonDepth
	{
		get => _dungeonDepth;
		set => SetPropertyWithBounds<uint>(ref _dungeonDepth, value, 1, 100);
	}

	[Export]
	public uint MapWidth
	{
		get => _mapWidth;
		set => SetPropertyWithBounds<uint>(ref _mapWidth, value, 20, 100);
	}

	[Export]
	public uint MapDepth
	{
		get => _mapDepth;
		set => SetPropertyWithBounds<uint>(ref _mapDepth, value, 20, 100);
	}

	[Export]
	public uint MaxRooms
	{
		get => _maxRooms;
		set => SetPropertyWithBounds<uint>(ref _maxRooms, value, 1, 100);
	}

	[Export]
	public ulong Seed
	{
		get => _seed;
		set => SetProperty(ref _seed, value);
	}

	[Export] public RoomLayoutStrategy RoomLayout { get => _roomLayout; set => SetProperty(ref _roomLayout, value); }
	[Export] public CorridorConnectorStrategy CorridorConnector { get => _corridorConnector; set => SetProperty(ref _corridorConnector, value); }
	[Export] public RoomFactoryStrategy RoomFactory { get => _roomFactory; set => SetProperty(ref _roomFactory, value); }
	[Export] public MobFactoryStrategy MobFactory { get => _mobFactory; set => SetProperty(ref _mobFactory, value); }
	[Export] public TileFactoryStrategy TileFactory { get => _tileFactory; set => SetProperty(ref _tileFactory, value); }

	/// <summary>
	/// The maximum number of times to retry placing a room before giving up.
	/// Increasing this value may help to generate more complex maps at the
	/// expense of performance.
	/// </summary>
	[Export]
	public uint MaxRetries
	{
		get => _maxRetries;
		set => SetPropertyWithBounds<uint>(ref _maxRetries, value, 1, 10);
	}

	public MapData Map;
	public GridMap FloorGridMap { get; private set; }
	public GridMap WallGridMap { get; private set; }
	public GridMap DecorationGridMap { get; private set; }
	public NavigationRegion3D NavigationRegion { get; private set; }

	// public PlayerSpawnPoint PlayerSpawnPoint { get; private set; }
	public float PlayerRotation { get; private set; } = 0;

	// FIXME: Centralize the tile size to avoid hardcoding it in multiple places
	/// <summary>
	/// The size of each tile in the base map when translating to the GridMaps.
	/// </summary>
	public readonly uint TileSize = 4;

	private uint _dungeonDepth = 1;
	private uint _mapWidth = 30;
	private uint _mapDepth = 30;
	private uint _maxRooms = 5;
	private uint _maxRetries = 3;
	private ulong _seed = 42;

	private RoomLayoutStrategy _roomLayout;
	private CorridorConnectorStrategy _corridorConnector;
	private RoomFactoryStrategy _roomFactory;
	private MobFactoryStrategy _mobFactory;
	private TileFactoryStrategy _tileFactory;

	public PlayerSpawnPoint PlayerSpawnPoint;
	public Array<SpawnPoint> EnemySpawnPoints;
	public Array<Node3D> Items;

	public override void _Ready()
	{
		GD.Print("Initializing map generator...");
		FloorGridMap = GetNode<GridMap>("FloorGridMap");
		WallGridMap = GetNode<GridMap>("WallGridMap");
		DecorationGridMap = GetNode<GridMap>("DecorationGridMap");
		NavigationRegion = GetNode<NavigationRegion3D>("NavigationRegion3D");

		if (Engine.IsEditorHint())
		{
			GenerateMap();
		}
	}

	protected void SetProperty<T>(ref T field, T value)
	{
		field = value;

		// Generate the map when the properties are set in the editor
		if (Engine.IsEditorHint())
		{
			GenerateMap();
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
			MergeRoomGridMaps(placement.Room, placement.Position);

			// Make the room a child of the navigation region, so
			// it is included in the navigation mesh and enemies
			// avoid obstactles in it.
			NavigationRegion.AddChild(placement.Room);

			var roomOffset = new Vector3I(placement.Room.Bounds.Position.X, 0, placement.Room.Bounds.Position.Y);
			placement.Room.Translate(TileToWorld(placement.Position) - roomOffset);
		}
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
				if (Map.IsCorridor(x, z))
				{
					int tileIndex = TileFactory.GetCorridorTileIndex();
					if (FloorGridMap.GetCellItem(TileToWorld(x, 0, z)) == -1)
					{
						FloorGridMap.SetCellItem(TileToWorld(x, 0, z), tileIndex, 0);
					}
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
				// Only check corridor tiles as rooms are already surrounded by walls
				if (Map.IsCorridor(x, z))
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

	private void SetPlayerSpawnPoint()
	{
		// Find the player spawn point on the map
		// TODO: This is a hack to find the player spawn point
		PlayerSpawnPoint = FindChild("PlayerSpawnPoint", true, false) as PlayerSpawnPoint;
	}

	private void GenerateEnemySpawnPoints()
	{
		GD.Print("Generating enemy spawn points...");
		var points = NavigationRegion.NavigationMesh.GetVertices();
		var spawnPoints = new List<Vector3>();

		uint mobCount = 3 + _dungeonDepth % 5 + (GD.Randi() % 3);
		for (int i = 0; i < mobCount; i++)
		{
			Vector3 point;
			bool isValidPoint;

			do
			{
				isValidPoint = true;
				point = points[GD.Randi() % points.Length];
				point.Y = 0f; // Ensure the point is on the ground

				// Ensure the point is at least 20 meters away from the player
				if (point.DistanceTo(PlayerSpawnPoint.GlobalPosition) < 20)
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
			} while (!isValidPoint);

			spawnPoints.Add(point);

			var enemyScene = MobFactory.CreateEnemy(1);
			var spawnPointNode = new SpawnPoint();
			spawnPointNode.SpawnOnStart = true;
			spawnPointNode.Scenes = [enemyScene];
			AddChild(spawnPointNode);

			spawnPointNode.GlobalPosition = point;
			spawnPointNode.Rotation = new Vector3(0, (float)(GD.Randf() * 2 * Math.PI), 0);

			EnemySpawnPoints.Add(spawnPointNode);
		}
	}

	private void GenerateItems()
	{
		GD.Print("Generating items...");
		var points = NavigationRegion.NavigationMesh.GetVertices();
		var items = new List<Vector3>();

		// int itemCount = 3 + Random.;
	}

	public void GenerateMap()
	{
		GD.Print("Generating map...");

		Reset();

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

		// Step 3: Bake navigation mesh
		BakeNavigationMesh();

		// Step 4: Create spawn points
		SetPlayerSpawnPoint();
		GenerateEnemySpawnPoints();
		GenerateItems();

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

		// Initialize the map with empty tiles
		Map = new MapData((int)_mapWidth, (int)_mapDepth);

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

		PlayerSpawnPoint = null;
		EnemySpawnPoints = new Array<SpawnPoint>();
		Items = new Array<Node3D>();

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
