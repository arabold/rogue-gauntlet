using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Base class for a room in the level.
/// </summary>
[Tool]
public partial class Room : Node3D
{
	/// <summary>
	/// The tile map data for the room
	/// </summary>
	public MapData Map { get; private set; }

	/// <summary>
	/// The bounds of the room in grid coordinates
	/// </summary>
	public Rect2I Bounds { get; private set; }

	[Export] public GridMap FloorGridMap;
	[Export] public GridMap WallGridMap;
	[Export] public GridMap DecorationGridMap;
	private GridMap _debugGridMap;

	[Export]
	public bool ShowDebugOverlay
	{
		get;
		set
		{
			if (value != field)
			{
				field = value;
				if (value)
				{
					// Property is set in the editor, so we need to check 
					// if the room is initialized already
					if (Engine.IsEditorHint() && IsNodeReady() && GetTree()?.EditedSceneRoot == this)
					{
						BakeTileMap();
						CreateDebugOverlay();
					}
				}
				else if (_debugGridMap != null)
				{
					RemoveChild(_debugGridMap);
					_debugGridMap.QueueFree();
					_debugGridMap = null;
				}
			}
		}
	}

	/// <summary>
	/// The size of each tile in the grid map (must be 4x4)
	/// </summary>
	public readonly int TileSize = 4;

	public override void _Ready()
	{
		base._Ready();

		// Show the debug overlay if needed
		if (Engine.IsEditorHint() && GetTree().EditedSceneRoot == this && ShowDebugOverlay)
		{
			BakeTileMap();
			CreateDebugOverlay();
		}
	}

	public void InitGridMaps()
	{
		// Automatically find the grid maps if they are not set
		if (FloorGridMap == null)
		{
			FloorGridMap = GetNodeOrNull<GridMap>("FloorGridMap");
		}
		if (WallGridMap == null)
		{
			WallGridMap = GetNodeOrNull<GridMap>("WallGridMap");
		}
		if (DecorationGridMap == null)
		{
			DecorationGridMap = GetNodeOrNull<GridMap>("DecorationGridMap");
		}
	}

	/// <summary>
	/// Create a map of the room based on the floor and wall grid maps.
	/// <br/>
	/// This map will be used to determine which tiles are rooms and which
	/// are corridors that need to be connected with other rooms.
	/// </summary>
	public void BakeTileMap()
	{
		// Ensure the grid maps are set
		InitGridMaps();
		if (FloorGridMap == null || WallGridMap == null)
		{
			GD.PrintErr($"{Name}: FloorGridMap and WallGridMap must be set!");
			Map = null;
			return;
		}

		// Determine the bounds of the room
		var usedCells = FloorGridMap.GetUsedCells();
		if (usedCells.Count == 0)
		{
			GD.PrintErr($"{Name}: No floor tiles found in the room!");
			Map = null;
			return;
		}

		int halfTileSize = TileSize / 2;
		int roomXMin = Mathf.FloorToInt(
			usedCells.MinBy(cell => cell.X).X / halfTileSize) * halfTileSize;
		int roomXMax = Mathf.FloorToInt(
			usedCells.MaxBy(cell => cell.X).X / halfTileSize) * halfTileSize;
		int roomZMin = Mathf.FloorToInt(
			usedCells.MinBy(cell => cell.Z).Z / halfTileSize) * halfTileSize;
		int roomZMax = Mathf.FloorToInt(
			usedCells.MaxBy(cell => cell.Z).Z / halfTileSize) * halfTileSize;

		int roomWidth = roomXMax - roomXMin + TileSize;
		int roomDepth = roomZMax - roomZMin + TileSize;

		Bounds = new Rect2I(roomXMin, roomZMin, roomWidth, roomDepth);
		GD.Print($"Room bounds: {Bounds}");

		var mapSize = ToTilePosition(Bounds.Size.X, 0, Bounds.Size.Y);
		var map = new MapData(mapSize.X, mapSize.Y);

		// Each tile in our MapData maps 4x4 tiles in the GridMap (with TileSize = 4)
		// To identify a wall tile, we need to check the 4x4 area around the tile
		for (var x = 0; x < map.Width; x++)
		{
			for (var z = 0; z < map.Height; z++)
			{
				var gridPos = ToGridPosition(x, 0, z);
				var gridX = gridPos.X + Bounds.Position.X - TileSize / 2;
				var gridZ = gridPos.Z + Bounds.Position.Y - TileSize / 2;

				// Check for floors
				for (var dx = 0; dx < TileSize; dx++)
				{
					for (var dz = 0; dz < TileSize; dz++)
					{
						var floorVec = new Vector3I(gridX + dx, 0, gridZ + dz);
						var floorCell = FloorGridMap.GetCellItem(floorVec);
						if (floorCell != -1)
						{
							map.SetTile(x, z, MapTile.Room);
							break;
						}
					}
				}
			}
		}

		// Find holes in the room layout and mark them as chasms
		var holes = FindChasms(map);
		foreach (var hole in holes)
		{
			foreach (var cell in hole)
			{
				map.SetTile(cell.X, cell.Y, MapTile.Chasm);
			}
		}

		// Once we have the final room layout, we can check for 
		// walls and corridors that need to be connected
		for (var x = 0; x < map.Width; x++)
		{
			for (var z = 0; z < map.Height; z++)
			{
				if (map.IsRoom(x, z))
				{
					// A room tile becomes a connector tile if it is 
					// adjacent to an empty tile on any side.
					var isCorridor = CheckForCorridor(map, x, z);
					if (isCorridor)
					{
						map.SetTile(x, z, MapTile.Connector);
					}
				}
			}
		}

		Map = map;
		GD.Print($"Room map generated: {Map.Width}x{Map.Height}");
	}

	/// <summary>
	/// Check if the given tile is a corridor by checking if it is adjacent to an empty tile
	/// </summary>
	private bool CheckForCorridor(MapData map, int x, int z)
	{
		// The 4 adjacent tiles
		var adjacentOffsets = new (int x, int z)[]
		{
			(1, 0), (-1, 0), (0, 1), (0, -1)
		};

		foreach (var (dx, dz) in adjacentOffsets)
		{
			int adjX = x + dx;
			int adjZ = z + dz;

			if (!map.IsWithinBounds(adjX, adjZ) || map.IsEmpty(adjX, adjZ))
			{
				// We found an empty tile adjacent to the room. Check if there is a wall
				// separating the two tiles
				bool hasWall = HasWall(x, z, dx, dz);

				if (!hasWall)
				{
					// We found one open side of the tile, so treat it as a corridor
					// that needs to be connected to other rooms
					// GD.Print($"Tile at {x}, {z} is a corridor with open side at {adjX}, {adjZ}");
					return true;
				}
			}
		}

		return false;
	}

	private bool HasWall(int x, int z, int dx, int dz)
	{
		var gridPos = ToGridPosition(x, 0, z);
		int gridX = gridPos.X + Bounds.Position.X - TileSize / 2;
		int gridZ = gridPos.Z + Bounds.Position.Y - TileSize / 2;

		bool hasWall = false;
		if (dx < 0)
		{
			hasWall = HasWallZ(gridX, gridZ);
			// GD.Print($"Checking Z wall at {gridX}, {gridZ}: {hasWall}");
		}
		if (dx > 0)
		{
			hasWall = HasWallZ(gridX + TileSize, gridZ);
			// GD.Print($"Checking Z wall at {gridX + TileSize}, {gridZ}: {hasWall}");
		}
		if (dz < 0)
		{
			hasWall = HasWallX(gridX, gridZ);
			// GD.Print($"Checking X wall at {gridX}, {gridZ}: {hasWall}");
		}
		if (dz > 0)
		{
			hasWall = HasWallX(gridX, gridZ + TileSize);
			// GD.Print($"Checking X wall at {gridX}, {gridZ + TileSize}: {hasWall}");
		}

		return hasWall;
	}

	/// <summary>
	/// Check if there is a wall along the X axis at the given grid position
	/// </summary>
	private bool HasWallX(int gridX, int gridZ)
	{
		// Note that walls are placed at the center of the tile
		for (var dx = 0; dx < TileSize; dx++)
		{
			if (WallGridMap.GetCellItem(new Vector3I(gridX + dx, 0, gridZ)) != -1)
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// Check if there is a wall along the Z axis at the given grid position
	/// </summary>
	private bool HasWallZ(int gridX, int gridZ)
	{
		// Note that walls are placed at the center of the tile
		for (var dz = 0; dz < TileSize; dz++)
		{
			if (WallGridMap.GetCellItem(new Vector3I(gridX, 0, gridZ + dz)) != -1)
			{
				return true;
			}
		}
		return false;
	}

	private void CreateDebugOverlay()
	{
		if (Map == null)
		{
			GD.PrintErr("No map data available to generate overlay!");
			return;
		}

		if (_debugGridMap != null)
		{
			RemoveChild(_debugGridMap);
			_debugGridMap.QueueFree();
		}

		// We always create a new grid map to adapt to changes in the room layout
		_debugGridMap = new GridMap();
		_debugGridMap.Name = "DebugGridMap";
		_debugGridMap.MeshLibrary = GD.Load<MeshLibrary>("res://scenes/levels/BaseMapMeshLibrary.tres");
		_debugGridMap.CellSize = new Vector3(TileSize, TileSize, TileSize);
		_debugGridMap.CellCenterX = false;
		_debugGridMap.CellCenterY = false;
		_debugGridMap.CellCenterZ = false;
		_debugGridMap.Translate(new Vector3(Bounds.Position.X, 0.1f, Bounds.Position.Y));
		_debugGridMap.Visible = ShowDebugOverlay;
		AddChild(_debugGridMap);

		// TODO: Make this more efficient by only updating the overlay when needed
		// Add a refresh timer to update the overlay when the room layout changes
		// var timer = new Timer();
		// _debugGridMap.AddChild(timer);
		// timer.OneShot = true;
		// timer.WaitTime = 0.5f;
		// timer.Timeout += () =>
		// {
		// 	if (_showDebugOverlay)
		// 	{
		// 		RecreateMap();
		// 		CreateDebugOverlay();
		// 	}
		// };
		// timer.Start();

		GD.Print("Generating overlay...");
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				var position = new Vector3I(x, 0, z);
				if (Map.IsRoom(x, z))
				{
					_debugGridMap.SetCellItem(position, 0, 0);
				}
				else if (Map.IsConnector(x, z))
				{
					_debugGridMap.SetCellItem(position, 1, 0);
				}
				else
				{
					_debugGridMap.SetCellItem(position, 2, 0);
				}
			}
		}
	}

	/// <summary>
	/// Finds all holes in the TileMap.
	/// </summary>
	/// <returns>A list of lists, where each inner list contains cells forming a hole.</returns>
	public List<List<Vector2I>> FindChasms(MapData map)
	{
		var holes = new List<List<Vector2I>>();
		var visited = new bool[map.Width, map.Height];

		for (int i = 0; i < map.Width; i++)
		{
			for (int j = 0; j < map.Height; j++)
			{
				// If the cell is 0 and not visited, start BFS
				if (map.IsEmpty(i, j) && !visited[i, j])
				{
					var hole = BFS(map, i, j, visited);
					if (hole != null && hole.Count > 0)
					{
						holes.Add(hole);
					}
				}
			}
		}

		return holes;
	}

	/// <summary>
	/// Performs BFS to find all contiguous zeros connected to the starting cell.
	/// </summary>
	/// <param name="startX">Starting cell's X-coordinate.</param>
	/// <param name="startY">Starting cell's Y-coordinate.</param>
	/// <returns>A list of cells forming a hole, or null if not a hole.</returns>
	private List<Vector2I> BFS(MapData map, int startX, int startY, bool[,] visited)
	{
		var queue = new Queue<Vector2I>();
		var holeCells = new List<Vector2I>();
		bool isHole = true;

		queue.Enqueue(new Vector2I(startX, startY));
		visited[startX, startY] = true;

		// Directions: Up, Down, Left, Right
		int[] dX = { -1, 1, 0, 0 };
		int[] dY = { 0, 0, -1, 1 };

		while (queue.Count > 0)
		{
			var current = queue.Dequeue();
			holeCells.Add(current);

			// If the current cell is on the boundary, it's not a hole
			if (map.IsOnBoundary(current.X, current.Y))
			{
				isHole = false;
			}

			// Explore all four directions
			for (int dir = 0; dir < 4; dir++)
			{
				int newX = current.X + dX[dir];
				int newY = current.Y + dY[dir];

				if (map.IsWithinBounds(newX, newY) && !visited[newX, newY] && map.IsEmpty(newX, newY))
				{
					queue.Enqueue(new Vector2I(newX, newY));
					visited[newX, newY] = true;
				}
			}
		}

		return isHole ? holeCells : null;
	}

	protected Vector2I ToTilePosition(Vector3I position)
	{
		return ToTilePosition(position.X, 0, position.Z);
	}

	protected Vector2I ToTilePosition(int x, int y, int z)
	{
		var tileX = Mathf.FloorToInt(x / TileSize);
		var tileZ = Mathf.FloorToInt(z / TileSize);
		return new Vector2I(tileX, tileZ);
	}

	protected Vector3I ToGridPosition(Vector2I position)
	{
		return ToGridPosition(position.X, 0, position.Y);
	}

	protected Vector3I ToGridPosition(int x, int y, int z)
	{
		var gridX = x * TileSize;
		var gridZ = z * TileSize;
		return new Vector3I(gridX, y, gridZ);
	}

}
