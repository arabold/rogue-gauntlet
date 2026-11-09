using Godot;

/// <summary>Shared eligibility and scheduling data for one authored enemy action.</summary>
[GlobalClass]
public abstract partial class EnemyActionProfile : Resource
{
	[ExportGroup("Eligibility")]
	[Export] public int Priority { get; set; }
	[Export(PropertyHint.Range, "0,120,0.1")] public float Cooldown { get; set; } = 1;
	[Export] public bool RequiresHostileTarget { get; set; } = true;
	[Export(PropertyHint.Range, "0,40,0.1")] public float MinimumDistance { get; set; }
	[Export(PropertyHint.Range, "0,60,0.1")] public float MaximumDistance { get; set; } = 3;
	[Export(PropertyHint.Range, "0,180,1")] public float MaximumStartAngle { get; set; } = 180;
	[Export(PropertyHint.Range, "0,30,0.1")] public float ClosePressureDuration { get; set; }
	[Export(PropertyHint.Range, "0,30,0.1")] public float ClosePressureRange { get; set; } = 2;
	[Export] public EnemyActionSlotKind SlotKind { get; set; } = EnemyActionSlotKind.Melee;
	[Export] public bool RiposteEligible { get; set; } = true;

	public virtual bool IsOffensive => true;
	public virtual bool IsSupport => false;
	public float EffectiveRange => EffectiveMaximumDistance;
	public virtual bool HasValidConfiguration => float.IsFinite(Cooldown) && Cooldown >= 0
		&& float.IsFinite(MinimumDistance) && MinimumDistance >= 0
		&& float.IsFinite(MaximumDistance) && MaximumDistance >= MinimumDistance
		&& float.IsFinite(MaximumStartAngle) && MaximumStartAngle >= 0 && MaximumStartAngle <= 180
		&& float.IsFinite(ClosePressureDuration) && ClosePressureDuration >= 0
		&& float.IsFinite(ClosePressureRange) && ClosePressureRange >= 0;

	/// <summary>Checks range, sight and any authored close-pressure gate using cached decision context.</summary>
	public virtual bool IsEligible(EnemyContext context, float pressureDuration = 0)
	{
		if (context == null) return false;
		if (RequiresHostileTarget && (!GodotObject.IsInstanceValid(context.Target) || !context.HasLineOfSightToTarget())) return false;
		if (GodotObject.IsInstanceValid(context.Target))
		{
			float distance = context.HorizontalTargetDistance;
			if (distance < MinimumDistance || distance > EffectiveMaximumDistance) return false;
			if (context.AngleToTarget > MaximumStartAngle) return false;
		}
		else if (RequiresHostileTarget) return false;
		return ClosePressureDuration <= 0 || pressureDuration >= ClosePressureDuration;
	}

	/// <summary>Whether this action's approach is within its authored distance band, independent of cooldown.</summary>
	public virtual bool IsInReach(EnemyContext context)
	{
		if (context == null || !GodotObject.IsInstanceValid(context.Target)) return false;
		return context.HorizontalTargetDistance >= MinimumDistance
			&& context.HorizontalTargetDistance <= EffectiveMaximumDistance
			&& context.HasLineOfSightToTarget();
	}

	protected virtual float EffectiveMaximumDistance => MaximumDistance;
}
