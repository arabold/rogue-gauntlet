using Godot;

[GlobalClass]
public partial class DodgeDefinition : Resource
{
	[Export] public DefenseAnimationProfile AnimationProfile { get; set; }
	[Export] public float Distance { get; set; } = 1.2f;
	[Export] public float TravelDuration { get; set; } = 0.20f;
	[Export] public float ImmunityStart { get; set; } = 0.0f;
	[Export] public float ImmunityEnd { get; set; } = 0.20f;
	[Export] public float RecoveryDuration { get; set; } = 0.08f;
	/// <summary>Minimum seconds between two accepted dodges, even with charges left.</summary>
	[Export] public float CooldownDuration { get; set; } = 0.60f;

	/// <summary>
	/// Dodges available back to back. Each spends a charge that refills after <see cref="ChargeRechargeDuration"/>,
	/// one charge at a time. With no recharge duration, dodges spend nothing and only the interval applies.
	/// </summary>
	[Export(PropertyHint.Range, "1,5,1")] public int MaxCharges { get; set; } = 1;
	[Export] public float ChargeRechargeDuration { get; set; }

	/// <summary>
	/// Evading a contact within this many seconds of the dodge starting refunds the charge it spent, once per
	/// dodge. Zero disables the refund.
	/// </summary>
	[Export] public float PerfectWindow { get; set; }

	public bool HasValidCharges => MaxCharges >= 1
		&& float.IsFinite(ChargeRechargeDuration) && ChargeRechargeDuration >= 0
		&& float.IsFinite(PerfectWindow) && PerfectWindow >= 0;

	/// <summary>
	/// Physics layers removed from the actor's collision mask while it travels, so it can dodge through
	/// other actors' bodies to get behind them. Walls must stay out of this mask. Zero passes through nothing.
	/// </summary>
	[Export(PropertyHint.Layers3DPhysics)] public uint PassThroughLayers { get; set; }

	/// <summary>
	/// Longest time a dodge that ends inside a pass-through body keeps moving, without immunity, to come
	/// out the far side before its collision returns.
	/// </summary>
	[Export] public float PassThroughExitTime { get; set; } = 0.3f;

	/// <summary>Share of the dodge speed used while leaving a pass-through body.</summary>
	[Export(PropertyHint.Range, "0,1,0.05")] public float PassThroughExitSpeedScale { get; set; } = 0.5f;
}