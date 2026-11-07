using Godot;

/// <summary>
/// Staging space for an oversized encounter: the room clearance its spawn site needs at minimum and prefers. How the
/// actor moves afterwards follows its body radius on the floor's clearance grid, not these margins.
/// </summary>
[Tool]
[GlobalClass]
public partial class LargeActorFitProfile : Resource
{
	[Export(PropertyHint.Range, "0.5,12,0.1")] public float MinimumClearance { get; set; } = 3.6f;
	[Export(PropertyHint.Range, "0.5,12,0.1")] public float PreferredClearance { get; set; } = 5.8f;
}