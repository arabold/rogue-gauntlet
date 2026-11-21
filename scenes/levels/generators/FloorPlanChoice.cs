using Godot;

/// <summary>One weighted entry of a realm's floor plans: the profile its ordinary floors may build from.</summary>
[Tool]
[GlobalClass]
public partial class FloorPlanChoice : Resource
{
	[Export] public FloorProfile Profile { get; set; }
	[Export(PropertyHint.Range, "0.01,10,0.01")] public float Weight { get; set; } = 1f;
}