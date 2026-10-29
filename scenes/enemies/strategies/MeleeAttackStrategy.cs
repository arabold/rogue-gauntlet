using Godot;

/// <summary>Request the authored melee attack only within its actual contact reach.</summary>
[GlobalClass]
public partial class MeleeAttackStrategy : EnemyAttackStrategy
{
	/// <summary>
	/// Widest angle between the actor's facing and the target at which a swing may start. A heavy actor turns
	/// toward the target first (at its movement turn rate), so getting behind it avoids swings. 180 = any.
	/// </summary>
	[Export(PropertyHint.Range, "0,180,1")] public float MaxStartAngle { get; set; } = 180;
	public override StringName Style => "melee";
	public override float EffectiveRange => Mathf.Min(EngagementDistance,
		Attack?.MeleeContact?.EngagementDistance ?? EngagementDistance);
	public override bool CanAttack(EnemyContext context) => IsInReach(context) && context.AngleToTarget <= MaxStartAngle;
	public override bool IsInReach(EnemyContext context) => context.Target != null
		&& GodotObject.IsInstanceValid(context.Target)
		&& context.HorizontalTargetDistance <= EffectiveRange
		&& context.HasLineOfSightToTarget();
}