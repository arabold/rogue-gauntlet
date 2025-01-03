using Godot;

public partial class MapManager : Node
{
	private GridMap _floorGridMap;
	private GridMap _wallGridMap;
	private GridMap _decorationGridMap;

	public override void _Ready()
	{
		// Get references to the GridMaps
		_floorGridMap = GetNode<GridMap>("FloorGridMap");
		_wallGridMap = GetNode<GridMap>("WallGridMap");
		_decorationGridMap = GetNode<GridMap>("DecorationGridMap");

		GenerateMap();
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
		AddRoom("res://scenes/maps/dungeon/rooms/small_room.tscn", new Vector3(0, 0, 0));
	}
}
