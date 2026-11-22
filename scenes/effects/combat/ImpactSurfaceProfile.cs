using Godot;

/// <summary>Authored particle and sound choices for contacts on this surface.</summary>
[Tool, GlobalClass]
public partial class ImpactSurfaceProfile : Resource
{
	[Export] public PackedScene DamageParticles { get; set; }
	[Export] public PackedScene GuardParticles { get; set; }
	[Export] public PackedScene ParryParticles { get; set; }
	[Export] public SoundCueProfile DamageSound { get; set; }
	[Export] public SoundCueProfile GuardSound { get; set; }
	[Export] public SoundCueProfile ParrySound { get; set; }
	[Export] public SoundCueProfile GuardBreakSound { get; set; }
	[Export] public Godot.Collections.Dictionary<DamageElement, SoundCueProfile> ElementDamageSounds { get; set; } = [];
}