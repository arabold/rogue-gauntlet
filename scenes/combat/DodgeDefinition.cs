using Godot;

[GlobalClass]
public partial class DodgeDefinition : Resource
{
	[Export] public float Distance { get; set; } = 3.6f;
	[Export] public float TravelDuration { get; set; } = 0.28f;
	[Export] public float ImmunityStart { get; set; } = 0.02f;
	[Export] public float ImmunityEnd { get; set; } = 0.20f;
	[Export] public float RecoveryDuration { get; set; } = 0.17f;
	[Export] public float CooldownDuration { get; set; } = 1.0f;
}