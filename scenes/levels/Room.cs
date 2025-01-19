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

	public GridMap FloorGridMap;
	public GridMap WallGridMap;
	public GridMap DecorationGridMap;
	private GridMap _debugGridMap;

	[Export]
	public bool ShowDebugOverlay
	{
		get => _debugGridMap != null && _debugGridMap.Visible;
		set
		{
			if (_debugGridMap != null)
			{
				if (value && value != _debugGridMap.Visible)
				{
					GenerateOverlay();
				}
				_debugGridMap.Visible = value;
			}
		}
	}

	/// <summary>
	/// The size of each tile in the grid map (must be 4x4)
	/// </summary>
	public readonly int TileSize = 4;

	public override void _Ready()
	{
		Initialize();
	}

	public virtual void Initialize()
	{
		GD.Print("Initializing room...");
		FloorGridMap = GetNode<GridMap>("FloorGridMap");
		WallGridMap = GetNode<GridMap>("WallGridMap");
		DecorationGridMap = GetNode<GridMap>("DecorationGridMap");

		// Top-left and bottom-right corners of the room
		var usedCells = FloorGridMap.GetUsedCells();
		int roomXMin = usedCells.MinBy(cell => cell.X).X;
		int roomXMax = usedCells.MaxBy(cell => cell.X).X;
		int roomZMin = usedCells.MinBy(cell => cell.Z).Z;
		int roomZMax = usedCells.MaxBy(cell => cell.Z).Z;
		int roomWidth = roomXMax - roomXMin + TileSize;
		int roomDepth = roomZMax - roomZMin + TileSize;

		Bounds = new Rect2I(roomXMin, roomZMin, roomWidth, roomDepth);
		GD.Print($"Room bounds: {Bounds}");
		RecreateMap();

		if (Engine.IsEditorHint())
		{
			_debugGridMap = new GridMap();
			_debugGridMap.Name = "DebugGridMap";
			_debugGridMap.MeshLibrary = GD.Load<MeshLibrary>("res://scenes/levels/BaseMapMeshLibrary.tres");
			_debugGridMap.CellSize = new Vector3(TileSize, TileSize, TileSize);
			_debugGridMap.CellCenterX = false;
			_debugGridMap.CellCenterY = false;
			_debugGridMap.CellCenterZ = false;
			_debugGridMap.Translate(new Vector3(Bounds.Position.X, 0.1f, Bounds.Position.Y));

			_debugGridMap.Visible = false;
			AddChild(_debugGridMap);
		}
	}

	public void Clear()
	{
		FloorGridMap.Clear();
		WallGridMap.Clear();
		DecorationGridMap.Clear();
		// OverlayGridMap.Clear();
		Map = null;
		Bounds = new Rect2I();
	}

	/// <summary>
	/// Create a map of the room based on the floor and wall grid maps.
	/// <br/>
	/// This map will be used to determine which tiles are rooms and which
	/// are corridors that need to be connected with other rooms.
	/// </summary>
	private void RecreateMap()
	{
		// var usedCells = FloorGridMap
		var map = new MapData(Bounds.Size.X / TileSize, Bounds.Size.Y / TileSize);

		// Each tile in our MapData maps 4x4 tiles in the GridMap (with TileSize = 4)
		// To identify a wall tile, we need to check the 4x4 area around the tile
		for (var x = 0; x < map.Width; x++)
		{
			for (var z = 0; z < map.Height; z++)
			{
				var gridX = x * TileSize + Bounds.Position.X - TileSize / 2;
				var gridZ = z * TileSize + Bounds.Position.Y - TileSize / 2;

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

		// Once we have the room layout, we can check for walls
		for (var x = 0; x < map.Width; x++)
		{
			for (var z = 0; z < map.Height; z++)
			{
				if (map.Tiles[x, z] == MapTile.Room)
				{
					var isCorridor = CheckForCorridor(map, x, z);
					if (isCorridor)
					{
						map.SetTile(x, z, MapTile.Corridor);
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
				int gridX = x * TileSize + Bounds.Position.X - TileSize / 2;
				int gridZ = z * TileSize + Bounds.Position.Y - TileSize / 2;

				bool hasWall = false;
				if (dx < 0)
				{
					hasWall = hasWallZ(gridX, gridZ);
				}
				if (dx > 0)
				{
					hasWall = hasWallZ(gridX + TileSize, gridZ);
				}
				if (dz < 0)
				{
					hasWall = hasWallX(gridX, gridZ);
				}
				if (dz > 0)
				{
					hasWall = hasWallX(gridX, gridZ + TileSize);
				}

				if (!hasWall)
				{
					return true;
				}
			}
		}

		return false;
	}

	/// <summary>
	/// Check if there is a wall along the X axis at the given grid position
	/// </summary>
	private bool hasWallX(int gridX, int gridZ)
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
	private bool hasWallZ(int gridX, int gridZ)
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

	private void GenerateOverlay()
	{
		GD.Print("Generating overlay...");

		_debugGridMap.Clear();
		for (int x = 0; x < Map.Width; x++)
		{
			for (int z = 0; z < Map.Height; z++)
			{
				var position = new Vector3I(x, 0, z);
				if (Map.IsRoom(x, z))
				{
					_debugGridMap.SetCellItem(position, 0, 0);
				}
				else if (Map.IsCorridor(x, z))
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
}
