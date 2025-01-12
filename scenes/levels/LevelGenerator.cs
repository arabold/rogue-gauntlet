using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using Godot;
using Godot.Collections;

public enum MapTile
{
	Empty,
	Wall,
	Room,
	Hallway,
}

[Tool]
public partial class LevelGenerator : Node
{
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

	[Export] public GridMap BaseMap;
	[Export] public GridMap FloorGridMap;
	[Export] public GridMap WallGridMap;
	[Export] public GridMap DecorationGridMap;
	[Export] public NavigationRegion3D NavigationRegion;
	/// <summary>
	/// The size of each tile in the base map when translating to the GridMaps.
	/// </summary>
	[Export] public int TileSize = 4;

	[Signal]
	public delegate void LevelGeneratedEventHandler();

	private Random _random = new Random();
	private int _mapWidth = 30;
	private int _mapDepth = 30;
	private int _maxRooms = 5;
	private int _seed = 42;

	private MapTile[,] _tiles;
	private Node3D _roomsContainer;

	public Vector3 PlayerSpawnPoint { get; private set; } = Vector3.Zero;
	public float PlayerRotation { get; private set; } = 0;

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

	private void GenerateRooms()
	{
		_roomsContainer = new Node3D();
		AddChild(_roomsContainer);
		if (Engine.IsEditorHint())
		{
			_roomsContainer.Owner = GetTree().EditedSceneRoot;
		}

		var roomScenePaths = new[]
		{
			"res://scenes/levels/dungeon/rooms/cross_roads.tscn",
			"res://scenes/levels/dungeon/rooms/small_room.tscn",
		};

		for (int i = 0; i < MaxRooms; i++)
		{
			var roomScene = roomScenePaths[_random.Next(0, roomScenePaths.Length)];
			PlaceRoom(_roomsContainer, GD.Load<PackedScene>(roomScene));
		}
	}

	private void PlaceRoom(Node3D root, PackedScene roomScene)
	{
		var roomInstance = roomScene.Instantiate<Node3D>();
		var roomBaseMap = roomInstance.GetNode<GridMap>("BaseMap");
		var usedCells = roomBaseMap.GetUsedCells();

		// Top-left and bottom-right corners of the room
		int roomXMin = usedCells.MinBy(cell => cell.X).X;
		int roomXMax = usedCells.MaxBy(cell => cell.X).X;
		int roomZMin = usedCells.MinBy(cell => cell.Z).Z;
		int roomZMax = usedCells.MaxBy(cell => cell.Z).Z;
		int roomWidth = roomXMax - roomXMin + 1;
		int roomDepth = roomZMax - roomZMin + 1;
		GD.Print($"Room size: {roomWidth}x{roomDepth} - ({roomXMin},{roomZMin}) to ({roomXMax},{roomZMax})");

		// Place the room at a random position on the map
		int roomX = _random.Next(2 - roomXMin, MapWidth - roomWidth - 2);
		int roomZ = _random.Next(2 - roomZMin, MapDepth - roomDepth - 2);
		Vector3I position = new Vector3I(roomX, 0, roomZ);

		// Check if the room placement overlaps with any existing rooms
		bool overlaps = IsRoomOverlapping(usedCells, position);

		if (!overlaps)
		{
			GD.Print($"Placing room at {position}");
			MergeRoomGridMaps(roomInstance, position);
			UpdateBaseMap(roomBaseMap, position);
			roomBaseMap.QueueFree();

			root.AddChild(roomInstance);
			roomInstance.Position = TileToWorld(position);

			if (Engine.IsEditorHint())
			{
				_roomsContainer.Owner = GetTree().EditedSceneRoot;
			}

		}
		else
		{
			roomInstance.QueueFree();
			RemoveChild(roomInstance);
		}
	}

	private bool IsRoomOverlapping(Array<Vector3I> usedCells, Vector3I position)
	{
		bool overlaps = false;
		foreach (var cell in usedCells)
		{
			var baseX = position.X + cell.X;
			var baseZ = position.Z + cell.Z;
			if (!IsEmpty(baseX, baseZ))
			{
				GD.Print($"Room overlaps with existing room at ({baseX}, 0, {baseZ})");
				overlaps = true;
				break;
			}
		}

		return overlaps;
	}

	private void MergeRoomGridMaps(Node3D roomInstance, Vector3I position)
	{
		// Generate the tile map from the room scene
		var roomFloorGridMap = roomInstance.GetNode<GridMap>("FloorGridMap");
		var roomWallGridMap = roomInstance.GetNode<GridMap>("WallGridMap");
		var roomDecorationGridMap = roomInstance.GetNode<GridMap>("DecorationGridMap");

		var gridMapOffset = TileToWorld(position);
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

	private void UpdateBaseMap(GridMap roomBaseMap, Vector3I position)
	{
		var tiles = roomBaseMap.GetUsedCells();
		foreach (var tile in tiles)
		{
			var tileIndex = roomBaseMap.GetCellItem(tile);
			var baseX = position.X + tile.X;
			var baseZ = position.Z + tile.Z;
			switch (tileIndex)
			{
				case 0:
					_tiles[baseX, baseZ] = MapTile.Room;
					break;
				case 1:
					_tiles[baseX, baseZ] = MapTile.Hallway;
					break;
				case 2:
					_tiles[baseX, baseZ] = MapTile.Wall;
					break;
			}
		}
	}

	private void ConnectAllRooms()
	{
		// We use a simple A* pathfinding algorithm to connect the rooms.
		// Initialize AStarGrid2D
		AStarGrid2D astar = new AStarGrid2D();
		astar.DiagonalMode = AStarGrid2D.DiagonalModeEnum.Never;
		astar.Region = new Rect2I(0, 0, MapWidth, MapDepth);
		astar.CellSize = Vector2.One;
		astar.Update();

		// Initialize the weights
		astar.FillSolidRegion(new Rect2I(0, 0, MapWidth, MapDepth), solid: true);
		astar.FillSolidRegion(new Rect2I(1, 1, MapWidth - 1, MapDepth - 1), solid: false);
		astar.FillWeightScaleRegion(new Rect2I(0, 0, MapWidth, MapDepth), 5);

		// Find all floor tiles
		List<Vector2I> hallwayTiles = new List<Vector2I>();
		for (int x = 0; x < MapWidth; x++)
		{
			for (int z = 0; z < MapDepth; z++)
			{
				var node = new Vector2I(x, z);

				if (IsHallway(x, z))
				{
					hallwayTiles.Add(node);
					astar.SetPointWeightScale(node, 0);
				}

				if (IsRoom(x, z) || IsWall(x, z))
				{
					// Avoid walking through rooms
					astar.SetPointSolid(node, solid: true);
				}
			}
		}

		// Pick two random floor tiles to connect
		var maxTries = 100;
		while (hallwayTiles.Count > 1 && maxTries-- > 0)
		{
			ShuffleList(hallwayTiles);
			Vector2I tile1 = hallwayTiles[0];
			Vector2I tile2 = hallwayTiles[1];
			if (ConnectTiles(astar, tile1, tile2))
			{
				// remove the connected tiles
				hallwayTiles.Remove(tile1);
			}
		}

		if (maxTries <= 0)
		{
			GD.PrintErr("Failed to connect all rooms.");
		}
	}

	private bool ConnectTiles(AStarGrid2D astar, Vector2I tile1, Vector2I tile2)
	{
		var path = astar.GetIdPath(tile1, tile2);
		if (path.Count > 0)
		{
			foreach (var node in path)
			{
				if (IsEmpty(node.X, node.Y))
				{
					_tiles[node.X, node.Y] = MapTile.Hallway;
					astar.SetPointWeightScale(node, 0);
					FloorGridMap.SetCellItem(TileToWorld(node.X, 0, node.Y), 0, 0);
				}
			}
			return true;
		}
		return false;
	}

	private Vector3I TileToWorld(Vector3I tile)
	{
		return TileToWorld(tile.X, tile.Y, tile.Z);
	}

	private Vector3I TileToWorld(int x, int y, int z)
	{
		return new Vector3I(x * TileSize, y * TileSize, z * TileSize);
	}

	private void GeneratePlayerSpawnPoint()
	{
		// Find a random floor tile to place the player
		int x;
		int y;
		do
		{
			x = _random.Next(1, MapWidth - 1);
			y = _random.Next(1, MapDepth - 1);
		}
		while (!IsRoom(x, y));

		PlayerSpawnPoint = new Vector3(x * TileSize, 0, y * TileSize);
		PlayerRotation = _random.Next(0, 360);
	}

	// private void GenerateEnemySpawnPoints()
	// {
	// 	// Clear the existing spawn points
	// 	EnemySpawnPoints.Clear();

	// 	// Add skeleton spawn points
	// 	for (int i = 0; i < 20; i++)
	// 	{
	// 		int x;
	// 		int y;
	// 		do
	// 		{
	// 			x = _random.Next(1, MapWidth - 1);
	// 			y = _random.Next(1, MapDepth - 1);
	// 		}
	// 		while (!IsFloor(x, y));

	// 		EnemySpawnPoints.Add(
	// 			new EnemySpawnPoint(
	// 				EnemyType.SkeletonMinion,
	// 				new Vector3(x * TileSize, 0, y * TileSize),
	// 				_random.Next(0, 360)));
	// 	}
	// }

	// private void GenerateItemSpawnPoints()
	// {
	// 	ItemSpawnPoints.Clear();

	// 	// Add skeleton spawn points
	// 	for (int i = 0; i < 20; i++)
	// 	{
	// 		int x;
	// 		int y;
	// 		do
	// 		{
	// 			x = _random.Next(1, MapWidth - 1);
	// 			y = _random.Next(1, MapDepth - 1);
	// 		}
	// 		while (!IsRoom(x, y));

	// 		ItemSpawnPoints.Add(
	// 			new ItemSpawnPoint(
	// 				ItemType.Chest,
	// 				new Vector3(x * TileSize, 0, y * TileSize),
	// 				_random.Next(0, 360)));
	// 	}
	// }

	public void GenerateMap()
	{
		GD.Print("Generating map...");

		Reset();

		// Step 1: Generate random rooms
		GenerateRooms();

		// Step 2: Use Drunkard's Walk to connect the rooms
		// DrunkardsWalkConnect();

		// Step 3: Create spawn points
		GeneratePlayerSpawnPoint();
		// GenerateEnemySpawnPoints();
		// GenerateItemSpawnPoints();

		ConnectAllRooms();
		PlaceWalls();

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

		GD.Print("Map generated.");

		// Render the BaseMap for debugging
		if (Engine.IsEditorHint())
		{
			for (int x = 0; x < MapWidth; x++)
			{
				for (int y = 0; y < MapDepth; y++)
				{
					if (IsRoom(x, y))
					{
						BaseMap.SetCellItem(new Vector3I(x, 0, y), 0, 0);
					}
					else if (IsHallway(x, y))
					{
						BaseMap.SetCellItem(new Vector3I(x, 0, y), 1, 0);
					}
					else if (IsWallOrEmpty(x, y))
					{
						BaseMap.SetCellItem(new Vector3I(x, 0, y), 2, 0);
					}
				}
			}
		}
		else
		{
			BaseMap.QueueFree();
		}

		EmitSignal(SignalName.LevelGenerated);
	}

	// Check if coordinates are within map bounds
	private bool IsWithinBounds(int x, int y)
	{
		return x >= 0 && x < _mapWidth && y >= 0 && y < _mapDepth;
	}

	// Shuffle a list in place using Fisher-Yates algorithm
	private void ShuffleList<T>(List<T> list)
	{
		for (int i = list.Count - 1; i > 0; i--)
		{
			int j = _random.Next(i + 1);
			T temp = list[i];
			list[i] = list[j];
			list[j] = temp;
		}
	}

	private void Reset()
	{
		_random = new Random(Seed);

		// Initialize the map with empty tiles
		_tiles = new MapTile[MapWidth, MapDepth];
		for (int x = 0; x < MapWidth; x++)
		{
			for (int y = 0; y < MapDepth; y++)
			{
				_tiles[x, y] = MapTile.Empty;
			}
		}

		if (_roomsContainer != null)
		{
			RemoveChild(_roomsContainer);
			_roomsContainer.QueueFree();
		}

		BaseMap.Clear();
		FloorGridMap.Clear();
		WallGridMap.Clear();
		DecorationGridMap.Clear();
	}

	private void PlaceWalls()
	{
		// Set wall tiles in the GridMap
		for (int x = 0; x < MapWidth; x++)
		{
			for (int y = 0; y < MapDepth; y++)
			{
				if (IsHallway(x, y)) // Only check hallway tiles
				{
					// Check for wall adjacency and place walls
					PlaceWallIfNeeded(x, y);
				}
			}
		}
	}

	private void PlaceWallIfNeeded(int x, int z)
	{
		// Check each direction for wall adjacency
		Vector3I basePosition = new Vector3I(x * TileSize, 0, z * TileSize);

		// Check above (north)
		if (z > 0 && IsWallOrEmpty(x, z - 1)) // Wall above
		{
			WallGridMap.SetCellItem(basePosition + new Vector3I(0, 0, -2), 0, 0); // Vertical wall (no rotation)
		}

		// Check below (south)
		if (z < MapDepth - 1 && IsWallOrEmpty(x, z + 1)) // Wall below
		{
			WallGridMap.SetCellItem(basePosition + new Vector3I(0, 0, 2), 0, 0); // Vertical wall (no rotation)
		}

		// Check left (west)
		if (x > 0 && IsWallOrEmpty(x - 1, z)) // Wall to the left
		{
			WallGridMap.SetCellItem(basePosition + new Vector3I(-2, 0, 0), 0, 16); // Horizontal wall (rotated)
		}

		// Check right (east)
		if (x < MapWidth - 1 && IsWallOrEmpty(x + 1, z)) // Wall to the right
		{
			WallGridMap.SetCellItem(basePosition + new Vector3I(2, 0, 0), 0, 16); // Horizontal wall (rotated)
		}
	}

	private bool IsHallway(int x, int y)
	{
		return _tiles[x, y] == MapTile.Hallway;
	}

	private bool IsRoom(int x, int y)
	{
		return _tiles[x, y] == MapTile.Room;
	}

	private bool IsFloor(int x, int y)
	{
		return IsHallway(x, y) || IsRoom(x, y);
	}

	private bool IsWallOrEmpty(int x, int y)
	{
		return _tiles[x, y] == MapTile.Wall || _tiles[x, y] == MapTile.Empty;
	}

	private bool IsWall(int x, int y)
	{
		return _tiles[x, y] == MapTile.Wall;
	}

	private bool IsEmpty(int x, int y)
	{
		return _tiles[x, y] == MapTile.Empty;
	}
}
