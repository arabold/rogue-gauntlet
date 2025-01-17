using System;
using System.Collections.Generic;
using Godot;

// [Tool]
public partial class MapGenerator : Node3D
{
	[Signal]
	public delegate void MapGeneratedEventHandler();

	[Export]
	public int MapWidth
	{
		get => _mapWidth;
		set
		{
			if (value < 5 || value > 100) return;
			_mapWidth = value;
			OnPropertyChange();
		}
	}

	[Export]
	public int MapDepth
	{
		get => _mapDepth;
		set
		{
			if (value < 5 || value > 100) return;
			_mapDepth = value;
			OnPropertyChange();
		}
	}

	[Export]
	public int MaxRooms
	{
		get => _maxRooms;
		set
		{
			if (value < 1) return;
			_maxRooms = value;
			OnPropertyChange();
		}
	}

	[Export]
	public int Seed
	{
		get => _seed;
		set
		{
			_seed = value;
			OnPropertyChange();
		}
	}

	/// <summary>
	/// The maximum number of times to retry placing a room before giving up.
	/// Increasing this value may help to generate more complex maps at the
	/// expense of performance.
	/// </summary>
	[Export]
	public int MaxRetries
	{
		get => _maxRetries;
		set
		{
			if (value < 1 || value > 10) return;
			_maxRetries = value;
			OnPropertyChange();
		}
	}

	public Node3D RoomsContainer { get; private set; }
	public GridMap BaseMap { get; private set; }
	public GridMap FloorGridMap { get; private set; }
	public GridMap WallGridMap { get; private set; }
	public GridMap DecorationGridMap { get; private set; }
	public NavigationRegion3D NavigationRegion { get; private set; }

	/// <summary>
	/// The size of each tile in the base map when translating to the GridMaps.
	/// </summary>
	public int TileSize = 4;

	private int _mapWidth = 30;
	private int _mapDepth = 30;
	private int _maxRooms = 5;
	private int _maxRetries = 3;
	private int _seed = 42;

	public Random Random = new Random();
	public MapData Map;
	public List<Node3D> Rooms = new List<Node3D>();

	public Vector3 PlayerSpawnPoint { get; private set; } = Vector3.Zero;
	public float PlayerRotation { get; private set; } = 0;

	public override void _Ready()
	{
		GD.Print("Initializing map generator...");
		RoomsContainer = GetNode<Node3D>("RoomsContainer");
		BaseMap = GetNode<GridMap>("BaseMap");
		FloorGridMap = GetNode<GridMap>("FloorGridMap");
		WallGridMap = GetNode<GridMap>("WallGridMap");
		DecorationGridMap = GetNode<GridMap>("DecorationGridMap");
		NavigationRegion = GetNode<NavigationRegion3D>("NavigationRegion3D");
	}

	private void OnPropertyChange()
	{
		if (Engine.IsEditorHint())
		{
			if (FloorGridMap != null && WallGridMap != null && DecorationGridMap != null)
			{
				GenerateMap();
			}
		}
	}

	protected void MergeRoomGridMaps(Node3D roomInstance, Vector3I placement)
	{
		// Generate the tile map from the room scene
		var roomFloorGridMap = roomInstance.GetNode<GridMap>("FloorGridMap");
		var roomWallGridMap = roomInstance.GetNode<GridMap>("WallGridMap");
		var roomDecorationGridMap = roomInstance.GetNode<GridMap>("DecorationGridMap");

		var gridMapOffset = TileToWorld(placement);
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

	// private void UpdateBaseMap(GridMap roomBaseMap, Vector3I position)
	// {
	// 	var tiles = roomBaseMap.GetUsedCells();
	// 	foreach (var tile in tiles)
	// 	{
	// 		var tileIndex = roomBaseMap.GetCellItem(tile);
	// 		var baseX = position.X + tile.X;
	// 		var baseZ = position.Z + tile.Z;
	// 		switch (tileIndex)
	// 		{
	// 			case 0:
	// 				Map.SetTile(baseX, baseZ, MapTile.Room);
	// 				break;
	// 			case 1:
	// 				Map.SetTile(baseX, baseZ, MapTile.Hallway);
	// 				break;
	// 			case 2:
	// 				Map.SetTile(baseX, baseZ, MapTile.Wall);
	// 				break;
	// 		}
	// 	}
	// }

	private void GenerateRooms()
	{
		GD.Print("Generating rooms...");

		var roomFactory = new DungeonRoomFactory();
		var roomLayout = new SimpleRoomLayout();
		var roomPlacements = roomLayout.GenerateRooms(Map, roomFactory, MaxRooms, MaxRetries, Random);

		PlaceRooms(roomPlacements);
	}

	private void ConnectRooms()
	{
		GD.Print("Connecting rooms...");
		var hallwayConnector = new AStarHallwayConnector();
		hallwayConnector.ConnectRooms(Map, Random);

		PlaceHallways();
		PlaceWalls();
	}

	private void GeneratePlayerSpawnPoint()
	{
		GD.Print("Generating player spawn point...");
		// Find a random floor tile to place the player
		int x;
		int y;
		do
		{
			x = Random.Next(1, MapWidth - 1);
			y = Random.Next(1, MapDepth - 1);
		}
		while (!Map.IsRoom(x, y));

		PlayerSpawnPoint = new Vector3(x * TileSize, 0, y * TileSize);
		PlayerRotation = Random.Next(0, 360);
	}

	public void GenerateMap()
	{
		GD.Print("Generating map...");

		Reset();

		// Step 1: Generate random rooms
		GenerateRooms();

		// Step 2: Connect the rooms
		ConnectRooms();

		// Step 3: Create spawn points
		GeneratePlayerSpawnPoint();

		// Add grid maps back to the NavigationRegion and rebake the navigation mesh
		// TODO: Not sure why we cannot make the GridMaps children of the NavigationRegion directly
		// If we try, the thread seems to block indefinitely when making updates to the GridMaps
		Node floorGridMapCopy = FloorGridMap.Duplicate();
		Node wallGripMapCopy = WallGridMap.Duplicate();
		Node decorationGridMapCopy = DecorationGridMap.Duplicate();
		NavigationRegion.AddChild(floorGridMapCopy);
		NavigationRegion.AddChild(wallGripMapCopy);
		NavigationRegion.AddChild(decorationGridMapCopy);
		NavigationRegion.BakeNavigationMesh();
		floorGridMapCopy.QueueFree();
		wallGripMapCopy.QueueFree();
		decorationGridMapCopy.QueueFree();

		// // Render the BaseMap for debugging
		// if (Engine.IsEditorHint())
		// {
		// 	for (int x = 0; x < MapWidth; x++)
		// 	{
		// 		for (int y = 0; y < MapDepth; y++)
		// 		{
		// 			if (Map.IsRoom(x, y))
		// 			{
		// 				BaseMap.SetCellItem(new Vector3I(x, 0, y), 0, 0);
		// 			}
		// 			else if (Map.IsHallway(x, y))
		// 			{
		// 				BaseMap.SetCellItem(new Vector3I(x, 0, y), 1, 0);
		// 			}
		// 			else if (Map.IsWallOrEmpty(x, y))
		// 			{
		// 				BaseMap.SetCellItem(new Vector3I(x, 0, y), 2, 0);
		// 			}
		// 		}
		// 	}
		// }
		// else
		// {
		// 	BaseMap.QueueFree();
		// }

		GD.Print("Map generated.");
		EmitSignal(SignalName.MapGenerated);
	}

	private void Reset()
	{
		GD.Print("Resetting map generator...");
		Random = new Random(Seed);

		// Initialize the map with empty tiles
		Map = new MapData(_mapWidth, _mapDepth);

		foreach (var room in RoomsContainer.GetChildren())
		{
			RoomsContainer.RemoveChild(room);
			room.QueueFree();
		}
		BaseMap.Clear();
		FloorGridMap.Clear();
		WallGridMap.Clear();
		DecorationGridMap.Clear();
	}

	private void PlaceRooms(List<RoomPlacement> roomPlacements)
	{
		GD.Print("Placing rooms...");
		foreach (var placement in roomPlacements)
		{
			MergeRoomGridMaps(placement.Room, placement.Position);
			// UpdateBaseMap(roomBaseMap, placement);
			// roomBaseMap.QueueFree();

			RoomsContainer.AddChild(placement.Room);
			placement.Room.Translate(TileToWorld(placement.Position));
		}
	}

	private void PlaceHallways()
	{
		GD.Print("Placing hallways...");
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				if (Map.IsHallway(x, z))
				{
					if (FloorGridMap.GetCellItem(TileToWorld(x, 0, z)) == -1)
					{
						FloorGridMap.SetCellItem(TileToWorld(x, 0, z), 0, 0);
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
				// Only check hallway tiles as rooms are already surrounded by walls
				if (Map.IsHallway(x, z))
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
		int tileCenter = TileSize / 2;
		int tileIndex = 0;

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

	private Vector3I TileToWorld(Vector3I tile)
	{
		return TileToWorld(tile.X, tile.Y, tile.Z);
	}

	private Vector3I TileToWorld(int x, int y, int z)
	{
		return new Vector3I(x * TileSize, y * TileSize, z * TileSize);
	}

}
