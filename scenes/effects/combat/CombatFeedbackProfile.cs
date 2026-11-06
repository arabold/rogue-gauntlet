using Godot;

/// <summary>Immutable source-side presentation values captured when an attack is prepared.</summary>
[Tool, GlobalClass]
public partial class CombatFeedbackProfile : Resource
{
	[Export(PropertyHint.Range, "0,3,0.01")] public float Strength { get; set; } = 1;
	[Export] public Color FlashColor { get; set; } = new(1f, 0.42f, 0.3f);
	[Export(PropertyHint.Range, "0,1,0.01")] public float FlashAmount { get; set; } = 0.65f;
	[Export(PropertyHint.Range, "0.01,1,0.01")] public float FlashDuration { get; set; } = 0.12f;
	[Export(PropertyHint.Range, "0,2,0.01")] public float CameraImpulse { get; set; } = 0.16f;
	[Export(PropertyHint.Range, "0,0.3,0.005")] public float RecoilDistance { get; set; } = 0.035f;
	[Export(PropertyHint.Range, "0,0.5,0.01")] public float LungeDistance { get; set; } = 0.2f;
	[Export(PropertyHint.Range, "0.01,0.5,0.01")] public float MotionDuration { get; set; } = 0.1f;
	[Export(PropertyHint.Range, "0,2,0.01")] public float BlockedScale { get; set; } = 0.45f;
	[Export(PropertyHint.Range, "0,2,0.01")] public float ParriedScale { get; set; } = 0.65f;
	[Export(PropertyHint.Range, "0,2,0.01")] public float GuardBrokenScale { get; set; } = 1.25f;
	[Export(PropertyHint.Range, "0,2,0.01")] public float EmphasisScale { get; set; } = 1.2f;

	public bool IsValid => float.IsFinite(Strength) && Strength >= 0 && float.IsFinite(FlashAmount) && FlashAmount >= 0
		&& float.IsFinite(FlashDuration) && FlashDuration > 0 && float.IsFinite(CameraImpulse) && CameraImpulse >= 0
		&& float.IsFinite(RecoilDistance) && RecoilDistance >= 0 && float.IsFinite(LungeDistance) && LungeDistance >= 0
		&& float.IsFinite(MotionDuration) && MotionDuration > 0 && float.IsFinite(BlockedScale) && BlockedScale >= 0
		&& float.IsFinite(ParriedScale) && ParriedScale >= 0 && float.IsFinite(GuardBrokenScale) && GuardBrokenScale >= 0
		&& float.IsFinite(EmphasisScale) && EmphasisScale >= 0;

	public CombatFeedbackSettings Capture() => IsValid
		? new(true, Strength, FlashColor, FlashAmount, FlashDuration, CameraImpulse, RecoilDistance, LungeDistance,
			MotionDuration, BlockedScale, ParriedScale, GuardBrokenScale, EmphasisScale)
		: default;
}

/// <summary>Value snapshot; runtime consumers never read mutable shared profile resources.</summary>
public readonly record struct CombatFeedbackSettings(bool Enabled, float Strength, Color FlashColor, float FlashAmount,
	float FlashDuration, float CameraImpulse, float RecoilDistance, float LungeDistance, float MotionDuration,
	float BlockedScale, float ParriedScale, float GuardBrokenScale, float EmphasisScale)
{
	public float ScaleFor(DamageOutcome outcome, DamageEmphasis emphasis)
	{
		float scale = outcome switch
		{
			DamageOutcome.Damaged => 1,
			DamageOutcome.Blocked => BlockedScale,
			DamageOutcome.Parried => ParriedScale,
			DamageOutcome.GuardBroken => GuardBrokenScale,
			_ => 0
		};
		return scale * (emphasis == DamageEmphasis.None ? 1 : EmphasisScale);
	}
}
