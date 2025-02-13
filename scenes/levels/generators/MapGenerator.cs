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
	[Export] public RoomFactoryStrategy RoomFactory { get; set => SetProperty(ref field, value); }
	[Export] public MobFactoryStrategy MobFactory { get; set => SetProperty(ref field, value); }
	[Export] public TileFactoryStrategy TileFactory { get; set => SetProperty(ref field, value); }

	public MapData Map;
	public GridMap FloorGridMap { get; private set; }
	public GridMap WallGridMap { get; private set; }
	public GridMap DecorationGridMap { get; private set; }
	public NavigationRegion3D NavigationRegion { get; private set; }

	// FIXME: Centralize the tile size to avoid hardcoding it in multiple places
	/// <summary>
	/// The size of each tile in the base map when translating to the GridMaps.
	/// </summary>
	public readonly uint TileSize = 4;

	public PlayerSpawnPoint PlayerSpawnPoint;
	public Array<SpawnPoint> EnemySpawnPoints;

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
				// Only check corridor tiles as rooms are already surrounded by walls
				if (Map.IsCorridor(x, z) || Map.IsConnector(x, z))
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
		var spawnPoints = new List<Vector3>();
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
			} while (!isValidPoint);

			spawnPoints.Add(point);

			var enemyScene = MobFactory.CreateEnemy(DungeonDepth);
			var spawnPointNode = new SpawnPoint();
			spawnPointNode.SpawnOnStart = true;
			spawnPointNode.Scenes = [enemyScene];
			AddChild(spawnPointNode);

			spawnPointNode.GlobalPosition = point;
			spawnPointNode.Rotation = new Vector3(0, (float)(GD.Randf() * 2 * Math.PI), 0);

			EnemySpawnPoints.Add(spawnPointNode);
		}
	}

	public void GenerateMap()
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
