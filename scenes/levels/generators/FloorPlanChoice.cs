using Godot;

/// <summary>One weighted entry of a realm's floor plans: the profile its ordinary floors may build from, on the realm
/// floors of its window (1 is the realm's first).</summary>
[Tool]
[GlobalClass]
public partial class FloorPlanChoice : Resource
{
	[Export] public FloorProfile Profile { get; set; }
	[Export(PropertyHint.Range, "0.01,10,0.01")] public float Weight { get; set; } = 1f;
	[Export(PropertyHint.Range, "1,10")] public int FirstFloor { get; set; } = 1;
	[Export(PropertyHint.Range, "1,10")] public int LastFloor { get; set; } = 10;

	public bool Spans(int floor) => floor >= FirstFloor && floor <= LastFloor;
}