using Godot;

/// <summary>
/// One step of a weapon's primary combo. It changes the motion and scales the weapon's own primary attack,
/// so every weapon of a family shares the combo while keeping its damage, speed and contact shape.
/// </summary>
[Tool]
[GlobalClass]
public partial class AttackComboStep : Resource
{
	/// <summary>Style requested from the wielder's animation profile; empty uses the weapon's primary style.</summary>
	[Export] public StringName AttackStyle { get; set; } = "";
	/// <summary>Scales the weapon's perform duration, so a finisher can wind up longer than the opener.</summary>
	[Export(PropertyHint.Range, "0.1,3,0.05")] public float DurationScale { get; set; } = 1f;
	[Export(PropertyHint.Range, "0,5,0.05")] public float DamageScale { get; set; } = 1f;
	[Export(PropertyHint.Range, "0,5,0.05")] public float PoiseDamageScale { get; set; } = 1f;
	[Export(PropertyHint.Range, "0,5,0.05")] public float HitStopScale { get; set; } = 1f;

	public bool IsValid => float.IsFinite(DurationScale) && DurationScale > 0
		&& float.IsFinite(DamageScale) && DamageScale >= 0
		&& float.IsFinite(PoiseDamageScale) && PoiseDamageScale >= 0
		&& float.IsFinite(HitStopScale) && HitStopScale >= 0;
}
