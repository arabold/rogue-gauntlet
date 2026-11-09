using System;
using Godot;

/// <summary>A status definition and chance applied only after a direct contact damages a living target.</summary>
[Tool]
[GlobalClass]
public partial class OnHitStatusProfile : Resource
{
	[Export] public Buff Effect { get; set; }
	[Export(PropertyHint.Range, "0,1,0.01")] public float Chance { get; set; } = 1;

	/// <summary>
	/// Identity a refreshing status reapplies under: the buff's saved file, so every source of the same venom refreshes
	/// one activation and saves can restore it. Stacking statuses need none.
	/// </summary>
	public string RefreshKey => Effect?.Stacking == BuffStacking.Refresh ? Effect.ResourcePath : "";

	/// <summary>A positive finite duration and chance; a refreshing status must be a saved buff file, not a sub-resource.</summary>
	public bool IsValid => Effect != null && float.IsFinite(Effect.Duration) && Effect.Duration > 0
		&& float.IsFinite(Chance) && Chance is > 0 and <= 1 && Enum.IsDefined(Effect.Stacking)
		&& (Effect is not PeriodicBuff periodic || float.IsFinite(periodic.TicksPerSecond) && periodic.TicksPerSecond > 0)
		&& (Effect is not PoisonBuff poison || poison.DamagePointsPerTick > 0)
		&& (Effect.Stacking != BuffStacking.Refresh || RefreshKey.StartsWith("res://", StringComparison.Ordinal) && !RefreshKey.Contains("::"));
}
