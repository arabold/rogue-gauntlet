using Godot;

/// <summary>Authored particle choices for damage, guard contact and parry contact.</summary>
[Tool, GlobalClass]
public partial class ImpactSurfaceProfile : Resource
{
	[Export] public PackedScene DamageParticles { get; set; }
	[Export] public PackedScene GuardParticles { get; set; }
	[Export] public PackedScene ParryParticles { get; set; }
}
