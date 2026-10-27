using Godot;

/// <summary>
/// Authored hit-reaction tuning for one kind of actor. Shared and immutable at runtime; each
/// <see cref="PoiseComponent"/> keeps its own meter.
/// </summary>
[GlobalClass]
public partial class PoiseDefinition : Resource
{
	/// <summary>Poise damage an actor absorbs as flinches before the next contact breaks it.</summary>
	[Export] public float MaxPoise { get; set; } = 20f;

	/// <summary>Seconds without poise damage before the meter starts refilling.</summary>
	[Export] public float RegenDelay { get; set; } = 2f;

	[Export] public float RegenPerSecond { get; set; } = 20f;

	/// <summary>Stagger length after a break. The meter stays empty until the stagger ends, then refills.</summary>
	[Export] public float BreakStaggerDuration { get; set; } = 1.2f;

	/// <summary>Damage multiplier for contacts landing while this actor is staggered (poise break or parry).</summary>
	[Export] public float StaggeredDamageMultiplier { get; set; } = 1.5f;

	/// <summary>Scale applied to incoming poise damage while the actor's current action is hyper-armored.</summary>
	[Export(PropertyHint.Range, "0,1,0.05")] public float ArmoredPoiseDamageScale { get; set; } = 0.5f;

	/// <summary>Show the meter under the floating health bar; intended for elites and bosses.</summary>
	[Export] public bool ShowBar { get; set; }

	public bool IsValid =>
		float.IsFinite(MaxPoise) && MaxPoise > 0
		&& float.IsFinite(RegenDelay) && RegenDelay >= 0
		&& float.IsFinite(RegenPerSecond) && RegenPerSecond >= 0
		&& float.IsFinite(BreakStaggerDuration) && BreakStaggerDuration > 0
		&& float.IsFinite(StaggeredDamageMultiplier) && StaggeredDamageMultiplier >= 0
		&& ArmoredPoiseDamageScale >= 0 && ArmoredPoiseDamageScale <= 1;
}