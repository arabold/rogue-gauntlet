using System.Linq;
using Godot;

public partial class Room : Node3D
{
	public Rect2I Bounds { get; private set; }
	public GridMap BaseMap;
	public GridMap floorGridMap;
	public GridMap wallGridMap;
	public GridMap decorationGridMap;

	public virtual void Initialize()
	{
		GD.Print("Initializing room...");
		BaseMap = GetNode<GridMap>("BaseMap");
		floorGridMap = GetNode<GridMap>("FloorGridMap");
		wallGridMap = GetNode<GridMap>("WallGridMap");
		decorationGridMap = GetNode<GridMap>("DecorationGridMap");

		var _usedCells = BaseMap.GetUsedCells();

		// Top-left and bottom-right corners of the room
		int roomXMin = _usedCells.MinBy(cell => cell.X).X;
		int roomXMax = _usedCells.MaxBy(cell => cell.X).X;
		int roomZMin = _usedCells.MinBy(cell => cell.Z).Z;
		int roomZMax = _usedCells.MaxBy(cell => cell.Z).Z;
		int roomWidth = roomXMax - roomXMin + 1;
		int roomDepth = roomZMax - roomZMin + 1;
		GD.Print($"Room size: {roomWidth}x{roomDepth} - ({roomXMin},{roomZMin}) to ({roomXMax},{roomZMax})");

		Bounds = new Rect2I(roomXMin, roomZMin, roomWidth, roomDepth);
	}

}
