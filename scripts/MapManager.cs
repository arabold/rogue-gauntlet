using Godot;

public partial class MapManager : Node
{
	private GridMap _floorGridMap;
	private GridMap _wallGridMap;
	private GridMap _decorationGridMap;
	private NavigationRegion3D _navigationRegion;

	public override void _Ready()
	{
		_navigationRegion = GetNode<NavigationRegion3D>("NavigationRegion3D");

		// Get references to the GridMaps
		_floorGridMap = GetNode<GridMap>("FloorGridMap");
		_wallGridMap = GetNode<GridMap>("WallGridMap");
		_decorationGridMap = GetNode<GridMap>("DecorationGridMap");

		CreateFloor(60, 60);
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

	public void GenerateMap()
	{
		GD.Print("Generating map...");

		// It seems that the GridMaps cannot be children of the NavigationRegion3D
		// when adding cells to them, so we remove them temporarily.
		_floorGridMap.GetParent().RemoveChild(_floorGridMap);
		_wallGridMap.GetParent().RemoveChild(_wallGridMap);
		_decorationGridMap.GetParent().RemoveChild(_decorationGridMap);

		AddRoom("res://scenes/maps/dungeon/rooms/small_room.tscn", new Vector3(0, 0, 0));

		// Add grid maps back to the NavigationRegion and rebake the navigation mesh
		_navigationRegion.AddChild(_floorGridMap);
		_navigationRegion.AddChild(_wallGridMap);
		_navigationRegion.AddChild(_decorationGridMap);
		_navigationRegion.BakeNavigationMesh();

		GD.Print("Map generated.");
	}
}
