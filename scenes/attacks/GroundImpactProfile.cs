using Godot;

/// <summary>Presentation captured with a ground attack; it never owns contact or damage timing.</summary>
[GlobalClass]
public partial class GroundImpactProfile : Resource
{
	[Export] public WorldEffectDefinition Effect { get; set; }
	[Export(PropertyHint.Range, "0,3,0.01")] public float CameraImpulse { get; set; }
	[Export(PropertyHint.Range, "0.1,40,0.1")] public float CameraRadius { get; set; } = 8;

	public bool IsValid => (Effect != null || CameraImpulse > 0)
		&& float.IsFinite(CameraImpulse) && CameraImpulse >= 0
		&& float.IsFinite(CameraRadius) && CameraRadius > 0;

	public GroundImpactSnapshot Capture() => new(Effect, CameraImpulse, CameraRadius);
}

public readonly record struct GroundImpactSnapshot(WorldEffectDefinition Effect, float CameraImpulse, float CameraRadius);
