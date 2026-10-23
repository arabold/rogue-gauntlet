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
}