using Godot;

[GlobalClass]
public partial class GuardDefinition : Resource
{
	[Export] public float RaiseDuration { get; set; } = 0.03f;
	[Export] public float ParryWindow { get; set; } = 0.21f;
	[Export] public float ReleaseRecovery { get; set; } = 0.12f;
	[Export] public float RearmInterval { get; set; } = 0.65f;
	[Export] public float MinimumReleaseInterval { get; set; } = 0.15f;
	[Export] public float FacingSectorDegrees { get; set; } = 120f;
	[Export] public float MovementSpeedMultiplier { get; set; } = 0.35f;
	[Export] public float DamageMultiplier { get; set; } = 0.25f;
	[Export] public float ParryStaggerDuration { get; set; } = 0.35f;
	[Export] public bool CanParry { get; set; } = true;
}