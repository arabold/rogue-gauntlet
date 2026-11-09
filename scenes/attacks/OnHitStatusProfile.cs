using System;
using Godot;

/// <summary>A status definition and chance applied only after a direct contact damages a living target.</summary>
[GlobalClass]
public partial class OnHitStatusProfile : Resource
{
	[Export] public Buff Effect { get; set; }
	[Export(PropertyHint.Range, "0,1,0.01")] public float Chance { get; set; } = 1;
	[Export] public string StackKey { get; set; } = "";

	public string CanonicalKey => Effect?.Stacking == BuffStacking.Refresh
		? (string.IsNullOrWhiteSpace(StackKey) ? Effect.ResourcePath : StackKey) : "";
	public bool IsValid => Effect != null && float.IsFinite(Effect.Duration) && Effect.Duration > 0
		&& float.IsFinite(Chance) && Chance is > 0 and <= 1 && Enum.IsDefined(Effect.Stacking)
		&& (Effect is not PeriodicBuff periodic || float.IsFinite(periodic.TicksPerSecond) && periodic.TicksPerSecond > 0)
		&& (Effect is not PoisonBuff poison || poison.DamagePointsPerTick > 0)
		&& (Effect.Stacking != BuffStacking.Refresh || (!string.IsNullOrWhiteSpace(CanonicalKey)
			&& CanonicalKey.StartsWith("res://", System.StringComparison.Ordinal) && CanonicalKey == Effect.ResourcePath
			&& !CanonicalKey.Contains("::")))
		&& (Effect.Stacking == BuffStacking.Refresh || string.IsNullOrWhiteSpace(StackKey));
}
