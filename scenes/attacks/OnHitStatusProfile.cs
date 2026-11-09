using Godot;

/// <summary>A status definition and chance applied only after a direct contact damages a living target.</summary>
[Tool]
[GlobalClass]
public partial class OnHitStatusProfile : Resource
{
	[Export] public Buff Effect { get; set; }
	[Export(PropertyHint.Range, "0,1,0.01")] public float Chance { get; set; } = 1;

	/// <summary>A valid timed buff and a chance in (0, 1].</summary>
	public bool IsValid => Effect?.IsValid == true && Effect.Duration > 0 && float.IsFinite(Chance) && Chance is > 0 and <= 1;
}
