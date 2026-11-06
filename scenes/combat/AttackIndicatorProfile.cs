using Godot;

/// <summary>Shared visual treatment for actor-authored incoming-attack indicators.</summary>
[Tool, GlobalClass]
public partial class AttackIndicatorProfile : Resource
{
	[Export(PropertyHint.Range, "0,1,0.01")] public float AimingOpacity { get; set; } = 0.16f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float AimingStartOpacity { get; set; } = 0.04f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float CommittedOpacity { get; set; } = 0.38f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float AttackingOpacity { get; set; } = 0.7f;
	[Export] public Color NormalTint { get; set; } = new(1f, 0.26f, 0.16f);
	[Export] public Color HeavyTint { get; set; } = new(1f, 0.58f, 0.12f);
	[Export] public Color UnblockableTint { get; set; } = new(0.9f, 0.15f, 0.12f);
	[Export] public float GroundOffset { get; set; } = 0.035f;
	[Export] public float ProjectileWidth { get; set; } = 0.28f;
	[Export(PropertyHint.Range, "0.1,1,0.01")] public float AimConeStartLengthScale { get; set; } = 0.33f;
	[Export(PropertyHint.Range, "0.5,3,0.1")] public float AimConeGrowthExponent { get; set; } = 1.5f;
	[Export] public float ReleaseFadeDuration { get; set; } = 0.12f;
}
