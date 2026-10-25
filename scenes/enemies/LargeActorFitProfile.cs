using Godot;

/// <summary>Separate encounter staging space from the body clearance required during room-local movement.</summary>
[Tool]
[GlobalClass]
public partial class LargeActorFitProfile : Resource
{
	/// <summary>Physical movement radius; spawn staging margins must not immobilize the actor.</summary>
	[Export(PropertyHint.Range, "0.5,12,0.05")] public float MovementClearance { get; set; } = 1.0f;
	[Export(PropertyHint.Range, "0.5,12,0.1")] public float MinimumClearance { get; set; } = 3.6f;
	[Export(PropertyHint.Range, "0.5,12,0.1")] public float PreferredClearance { get; set; } = 5.8f;
}