using Godot;

[Tool]
[GlobalClass]
public partial class GuardDefinition : Resource
{
	[Export] public float RaiseDuration { get; set; } = 0.03f;
	[Export] public float ParryWindow { get; set; } = 0.21f;
	[Export] public float ReleaseRecovery { get; set; } = 0.12f;
	[Export] public float RearmInterval { get; set; } = 0.65f;
	[Export] public float MinimumReleaseInterval { get; set; } = 0.15f;
	[Export] public float FacingSectorDegrees { get; set; } = 120f;
	[Export] public float MovementSpeedMultiplier { get; set; } = 0.0f;
	[Export] public float DamageMultiplier { get; set; } = 0.25f;
	[Export] public float ParryStaggerDuration { get; set; } = 0.35f;
	[Export] public bool CanParry { get; set; } = true;
	/// <summary>Stagger after an unparried Heavy contact breaks this guard.</summary>
	[Export] public float GuardBreakStaggerDuration { get; set; } = 0.8f;
	/// <summary>Share of a guard-breaking contact's damage that still lands (before armor).</summary>
	[Export(PropertyHint.Range, "0,1,0.05")] public float GuardBreakDamageMultiplier { get; set; } = 0.5f;
	[Export] public DefenseAnimationProfile AnimationProfile { get; set; }
}