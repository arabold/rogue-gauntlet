using System;
using System.Collections.Generic;
using Godot;

public enum MapTile
{
	Wall,
	Pathway,
	Room
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
			if (value < RoomMinSize) return;
			_mapWidth = value;
			OnPropertyChange();
		}
	}

	[Export]
	public int MapHeight
	{
		get => _mapHeight;
		set
		{
			if (value < 5 || value > 100) return;
			if (value < RoomMinSize) return;
			_mapHeight = value;
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
	public int RoomMinSize
	{
		get => _roomMinSize;
		set
		{
			if (value < 2) return;
			if (value > RoomMaxSize) return;
			if (value > Math.Min(MapWidth, MapHeight)) return;
			_roomMinSize = value;
			OnPropertyChange();
		}
	}

	[Export]
	public int RoomMaxSize
	{
		get => _roomMaxSize;
		set
		{
			if (value < 2) return;
			if (value < RoomMinSize) return;
			if (value > Math.Min(MapWidth, MapHeight)) return;
			_roomMaxSize = value;
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

	// Bias for straight corridors (0 to 1)
	[Export]
	public float CorridorStraightness
	{
		get => _corridorStraightness;
		set
		{
			if (value < 0 || value > 1) return;
			_corridorStraightness = value;
			OnPropertyChange();
		}
	}

	[Export] public GridMap FloorGridMap;
	[Export] public GridMap WallGridMap;
	[Export] public GridMap DecorationGridMap;
	[Export] public NavigationRegion3D NavigationRegion;

	[Signal]
	public delegate void LevelGeneratedEventHandler();

	private Random _random = new Random();
	private int _mapWidth = 30;
	private int _mapHeight = 30;
	private int _maxRooms = 5;
	private int _roomMinSize = 2;
	private int _roomMaxSize = 4;
	private float _corridorStraightness = 0.75f;
	private int _seed = 42;
	private List<Rect2I> _rooms = new List<Rect2I>();

	public MapTile[,] BaseMap { get; private set; }
	public Vector3 PlayerSpawnPoint { get; private set; }
	public List<EnemySpawnPoint> EnemySpawnPoints { get; private set; } = new List<EnemySpawnPoint>();
	public List<ItemSpawnPoint> ItemSpawnPoints { get; private set; } = new();

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

	private void AddRoom(string roomScenePath, Vector3 offset)
	{
		// Load the room scene
		var roomScene = ResourceLoader.Load<PackedScene>(roomScenePath);
		var roomInstance = roomScene.Instantiate<Node3D>();

		// Add the room temporarily to the scene to access its GridMaps
		AddChild(roomInstance);

		// Combine the Floor GridMap
		var roomFloorGridMap = roomInstance.GetNode<GridMap>("FloorGridMap");
		MergeGridMaps(roomFloorGridMap, FloorGridMap, offset);

		// Combine the Wall GridMap
		var roomWallGridMap = roomInstance.GetNode<GridMap>("WallGridMap");
		MergeGridMaps(roomWallGridMap, WallGridMap, offset);

		// Combine the Decoration GridMap
		var roomDecorationGridMap = roomInstance.GetNode<GridMap>("DecorationGridMap");
		MergeGridMaps(roomDecorationGridMap, DecorationGridMap, offset);

		// Remove the temporary room instance
		roomInstance.QueueFree();
	}

	private void MergeGridMaps(GridMap sourceGridMap, GridMap targetGridMap, Vector3 offset)
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
		for (int i = 0; i < MaxRooms; i++)
		{
			int roomWidth = _random.Next(RoomMinSize, RoomMaxSize);
			int roomHeight = _random.Next(RoomMinSize, RoomMaxSize);
			int roomX = _random.Next(1, MapWidth - roomWidth - 1);
			int roomY = _random.Next(1, MapHeight - roomHeight - 1);

			Rect2I newRoom = new Rect2I(roomX, roomY, roomWidth, roomHeight);

			// Check if the room overlaps with existing rooms
			bool overlaps = false;
			foreach (Rect2I room in _rooms)
			{
				if (newRoom.Intersects(room))
				{
					overlaps = true;
					break;
				}
			}

			// If there's no overlap, add the room
			if (!overlaps)
			{
				_rooms.Add(newRoom);
				CarveRoom(newRoom);
			}
		}
	}

	private void CarveRoom(Rect2I room)
	{
		for (int x = room.Position.X; x < room.Position.X + room.Size.X; x++)
		{
			for (int y = room.Position.Y; y < room.Position.Y + room.Size.Y; y++)
			{
				BaseMap[x, y] = MapTile.Room;
			}
		}
	}

	private void DrunkardsWalkConnect()
	{
		// Randomly select a room to start the drunkard's walk
		Rect2I startRoom = _rooms[_random.Next(_rooms.Count)];
		Vector2I startPoint = startRoom.Position + (startRoom.Size / 2);

		// Create a list of rooms to ensure all are visited
		HashSet<Rect2I> visitedRooms = new HashSet<Rect2I> { startRoom };

		// Drunkard's Walk: Start at the center of the starting room
		Vector2I drunkard = startPoint;

		// Initialize the preferred direction
		Vector2I preferredDirection = GetRandomDirection();

		while (visitedRooms.Count < _rooms.Count)
		{
			// Mark the current tile as a floor
			if (BaseMap[drunkard.X, drunkard.Y] == MapTile.Wall)
			{
				BaseMap[drunkard.X, drunkard.Y] = MapTile.Pathway;
			}

			// Check if we're in a new room and mark it as visited
			foreach (Rect2I room in _rooms)
			{
				if (room.HasPoint(drunkard) && !visitedRooms.Contains(room))
				{
					visitedRooms.Add(room);
					break;
				}
			}

			// Decide whether to continue in the current direction
			if (_random.NextDouble() < CorridorStraightness)
			{
				// Continue in the preferred direction
				drunkard += preferredDirection;
			}
			else
			{
				// Pick a new random direction
				preferredDirection = GetRandomDirection();
				drunkard += preferredDirection;
			}

			// Clamp the drunkard's position to the map boundaries
			drunkard.X = Math.Clamp(drunkard.X, 1, MapWidth - 2);
			drunkard.Y = Math.Clamp(drunkard.Y, 1, MapHeight - 2);
		}
	}

	private Vector2I GetRandomDirection()
	{
		// Return a random cardinal direction
		int direction = _random.Next(4);
		return direction switch
		{
			0 => new Vector2I(0, -1), // Up
			1 => new Vector2I(0, 1),  // Down
			2 => new Vector2I(-1, 0), // Left
			3 => new Vector2I(1, 0),  // Right
			_ => Vector2I.Zero,
		};
	}

	private void GeneratePlayerSpawnPoint()
	{
		// Find a random floor tile to place the player
		int playerX = 0;
		int playerZ = 0;
		while (!IsFloor(playerX, playerZ))
		{
			playerX = _random.Next(1, MapWidth - 1);
			playerZ = _random.Next(1, MapHeight - 1);
		}

		PlayerSpawnPoint = new Vector3(playerX, 0, playerZ);
	}

	private void GenerateEnemySpawnPoints()
	{
		// Clear the existing spawn points
		EnemySpawnPoints.Clear();

		// Add skeleton spawn points
		for (int i = 0; i < 20; i++)
		{
			int x = 0;
			int y = 0;
			while (!IsFloor(x, y))
			{
				x = _random.Next(1, MapWidth - 1);
				y = _random.Next(1, MapHeight - 1);
			}

			EnemySpawnPoints.Add(
				new EnemySpawnPoint(
					EnemyType.SkeletonMinion,
					new Vector3(x * 4, 0, y * 4),
					_random.Next(0, 360)));
		}
	}

	private void GenerateItemSpawnPoints()
	{
		ItemSpawnPoints.Clear();
		foreach (Rect2I room in _rooms)
		{
			int itemCount = _random.Next(1, 3);
			for (int i = 0; i < itemCount; i++)
			{
				int x = _random.Next(room.Position.X, room.Position.X + room.Size.X);
				int y = _random.Next(room.Position.Y, room.Position.Y + room.Size.Y);
				ItemSpawnPoints.Add(
					new ItemSpawnPoint(
						ItemType.Chest,
						new Vector3(x * 4, 0, y * 4),
						_random.Next(0, 360)));
			}
		}
	}

	public void GenerateMap()
	{
		GD.Print("Generating map...");

		Reset();

		// Step 1: Generate random rooms
		GenerateRooms();

		// Step 2: Use Drunkard's Walk to connect the rooms
		DrunkardsWalkConnect();

		// Step 3: Create spawn points
		GeneratePlayerSpawnPoint();
		GenerateEnemySpawnPoints();
		GenerateItemSpawnPoints();

		RenderMap();

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

		EmitSignal(SignalName.LevelGenerated);
	}

	private void Reset()
	{
		_random = new Random(Seed);

		// Initialize the map with walls
		BaseMap = new MapTile[MapWidth, MapHeight];
		for (int x = 0; x < MapWidth; x++)
		{
			for (int y = 0; y < MapHeight; y++)
			{
				BaseMap[x, y] = MapTile.Wall;
			}
		}
	}

	private void RenderMap()
	{
		FloorGridMap.Clear();
		WallGridMap.Clear();

		// Find a random floor tile to place the player
		int playerX = 0;
		int playerZ = 0;
		while (!IsFloor(playerX, playerZ))
		{
			playerX = _random.Next(1, MapWidth - 1);
			playerZ = _random.Next(1, MapHeight - 1);
		}

		// Set floor tiles in the GridMap
		for (int x = 0; x < MapWidth; x++)
		{
			for (int y = 0; y < MapHeight; y++)
			{
				var tile = BaseMap[x, y];
				if (tile == MapTile.Pathway)
				{
					FloorGridMap.SetCellItem(new Vector3I(x * 4, 0, y * 4), 0, 0);
				}
				else if (tile == MapTile.Room)
				{
					FloorGridMap.SetCellItem(new Vector3I(x * 4, 0, y * 4), 21, 0);
				}
			}
		}

		// Set wall tiles in the GridMap
		WallGridMap.Clear();
		for (int x = 0; x < MapWidth; x++)
		{
			for (int y = 0; y < MapHeight; y++)
			{
				if (IsFloor(x, y)) // Only check floor tiles
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
		Vector3I basePosition = new Vector3I(x * 4, 0, z * 4);

		// Check above (north)
		if (z > 0 && IsWall(x, z - 1)) // Wall above
		{
			WallGridMap.SetCellItem(basePosition + new Vector3I(0, 0, -2), 0, 0); // Vertical wall (no rotation)
		}

		// Check below (south)
		if (z < MapHeight - 1 && IsWall(x, z + 1)) // Wall below
		{
			WallGridMap.SetCellItem(basePosition + new Vector3I(0, 0, 2), 0, 0); // Vertical wall (no rotation)
		}

		// Check left (west)
		if (x > 0 && IsWall(x - 1, z)) // Wall to the left
		{
			WallGridMap.SetCellItem(basePosition + new Vector3I(-2, 0, 0), 0, 16); // Horizontal wall (rotated)
		}

		// Check right (east)
		if (x < MapWidth - 1 && IsWall(x + 1, z)) // Wall to the right
		{
			WallGridMap.SetCellItem(basePosition + new Vector3I(2, 0, 0), 0, 16); // Horizontal wall (rotated)
		}
	}

	private bool IsFloor(int x, int y)
	{
		return BaseMap[x, y] == MapTile.Pathway || BaseMap[x, y] == MapTile.Room;
	}

	private bool IsWall(int x, int y)
	{
		return BaseMap[x, y] == MapTile.Wall;
	}
}
