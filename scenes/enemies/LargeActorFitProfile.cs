using Godot;

/// <summary>Shared immutable room-clearance policy used by placement and runtime movement.</summary>
[GlobalClass]
public partial class LargeActorFitProfile : Resource
{
	[Export(PropertyHint.Range, "0.5,12,0.1")] public float MinimumClearance { get; set; } = 3.6f;
	[Export(PropertyHint.Range, "0.5,12,0.1")] public float PreferredClearance { get; set; } = 5.8f;
}