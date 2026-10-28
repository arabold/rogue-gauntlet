using Godot;

/// <summary>Immutable authored selection of stage walls and boundaries for a debug arrangement.</summary>
[GlobalClass]
public partial class DebugArrangement : Resource
{
	[Export] public string DisplayName { get; set; } = "Arrangement";
	[Export] public int[] Walls { get; set; } = [];
	[Export] public int[] BoundaryWalls { get; set; } = [];
	[Export] public bool HasWestWall { get; set; }
}