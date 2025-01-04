using System;
using System.Collections.Generic;
using Godot;

public partial class MapManager : Node
{
	[Export] public int MapWidth = 60;
	[Export] public int MapHeight = 60;
	[Export] public int MaxRooms = 10;
	[Export] public int RoomMinSize = 3;
	[Export] public int RoomMaxSize = 8;
	[Export] public int Seed = 42;
	// Bias for straight corridors (0 to 1)
	[Export] public float CorridorStraightness = 0.75f;

	private GridMap _floorGridMap;
	private GridMap _wallGridMap;
	private GridMap _decorationGridMap;
	private NavigationRegion3D _navigationRegion;

	private Random _random;
	private int[,] _map;
	private List<Rect2I> _rooms = new List<Rect2I>();

	public override void _Ready()
	{
		_random = new Random(Seed);
		_navigationRegion = GetNode<NavigationRegion3D>("NavigationRegion3D");

		// Get references to the GridMaps
		_floorGridMap = GetNode<GridMap>("FloorGridMap");
		_wallGridMap = GetNode<GridMap>("WallGridMap");
		_decorationGridMap = GetNode<GridMap>("DecorationGridMap");

		GenerateMap();
	}

	private void CreateFloor(int width, int depth)
	{
		// Create the floor GridMap
		_floorGridMap.Clear();
		for (int x = -width / 2; x < width / 2; x += 2)
		{
			for (int z = -depth / 2; z < depth / 2; z += 2)
			{
				_floorGridMap.SetCellItem(new Vector3I(x, 0, z), 0, 0);
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
		MergeGridMaps(roomFloorGridMap, _floorGridMap, offset);

		// Combine the Wall GridMap
		var roomWallGridMap = roomInstance.GetNode<GridMap>("WallGridMap");
		MergeGridMaps(roomWallGridMap, _wallGridMap, offset);

		// Combine the Decoration GridMap
		var roomDecorationGridMap = roomInstance.GetNode<GridMap>("DecorationGridMap");
		MergeGridMaps(roomDecorationGridMap, _decorationGridMap, offset);

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
				_map[x, y] = 0; // 0 = Floor
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
			_map[drunkard.X, drunkard.Y] = 0;

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

	private void GenerateMap()
	{
		GD.Print("Generating map...");

		// It seems that the GridMaps cannot be children of the NavigationRegion3D
		// when adding cells to them, so we remove them temporarily.
		_floorGridMap.GetParent().RemoveChild(_floorGridMap);
		_wallGridMap.GetParent().RemoveChild(_wallGridMap);
		_decorationGridMap.GetParent().RemoveChild(_decorationGridMap);

		// Initialize the map with walls
		_map = new int[MapWidth, MapHeight];
		for (int x = 0; x < MapWidth; x++)
		{
			for (int y = 0; y < MapHeight; y++)
			{
				_map[x, y] = 1; // 1 = Wall
			}
		}

		// Step 1: Generate random rooms
		GenerateRooms();

		// Step 2: Use Drunkard's Walk to connect the rooms
		DrunkardsWalkConnect();

		RenderMap();

		// Add grid maps back to the NavigationRegion and rebake the navigation mesh
		_navigationRegion.AddChild(_floorGridMap);
		_navigationRegion.AddChild(_wallGridMap);
		_navigationRegion.AddChild(_decorationGridMap);
		_navigationRegion.BakeNavigationMesh();

		GD.Print("Map generated.");
	}

	private void RenderMap()
	{
		_floorGridMap.Clear();
		_wallGridMap.Clear();

		// Find a random floor tile to place the player
		int playerX = 0;
		int playerZ = 0;
		while (_map[playerX, playerZ] != 0)
		{
			playerX = _random.Next(1, MapWidth - 1);
			playerZ = _random.Next(1, MapHeight - 1);
		}

		// Set floor tiles in the GridMap
		for (int x = 0; x < MapWidth; x++)
		{
			for (int z = 0; z < MapHeight; z++)
			{
				if (_map[x, z] == 0)
				{
					_floorGridMap.SetCellItem(new Vector3I((x - playerX) * 4, 0, (z - playerZ) * 4), 0, 0);
				}
			}
		}

		// Set wall tiles in the GridMap
		_wallGridMap.Clear();
		for (int x = 0; x < MapWidth; x++)
		{
			for (int z = 0; z < MapHeight; z++)
			{
				if (_map[x, z] == 0) // Only check floor tiles
				{
					// Check for wall adjacency and place walls
					PlaceWallIfNeeded(x, z, playerX, playerZ);
				}
			}
		}
	}

	private void PlaceWallIfNeeded(int x, int z, int playerX, int playerZ)
	{
		// Check each direction for wall adjacency
		Vector3I basePosition = new Vector3I((x - playerX) * 4, 0, (z - playerZ) * 4);

		// Check above (north)
		if (z > 0 && _map[x, z - 1] == 1) // Wall above
		{
			_wallGridMap.SetCellItem(basePosition + new Vector3I(0, 0, -2), 0, 0); // Vertical wall (no rotation)
		}

		// Check below (south)
		if (z < MapHeight - 1 && _map[x, z + 1] == 1) // Wall below
		{
			_wallGridMap.SetCellItem(basePosition + new Vector3I(0, 0, 2), 0, 0); // Vertical wall (no rotation)
		}

		// Check left (west)
		if (x > 0 && _map[x - 1, z] == 1) // Wall to the left
		{
			_wallGridMap.SetCellItem(basePosition + new Vector3I(-2, 0, 0), 0, 16); // Horizontal wall (rotated)
		}

		// Check right (east)
		if (x < MapWidth - 1 && _map[x + 1, z] == 1) // Wall to the right
		{
			_wallGridMap.SetCellItem(basePosition + new Vector3I(2, 0, 0), 0, 16); // Horizontal wall (rotated)
		}
	}
}
