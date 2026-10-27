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
	[Export] public float CooldownDuration { get; set; } = 0.60f;

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