using Godot;

/// <summary>Request the authored melee attack only within its actual contact reach.</summary>
[GlobalClass]
public partial class MeleeAttackStrategy : EnemyAttackStrategy
{
	public override StringName Style => "melee";
	public override float EffectiveRange => Mathf.Min(EngagementDistance,
		Attack?.MeleeContact?.EngagementDistance ?? EngagementDistance);
	public override bool CanAttack(EnemyContext context) => IsInReach(context) && context.AngleToTarget <= MaxStartAngle;
	public override bool IsInReach(EnemyContext context) => context.Target != null
		&& GodotObject.IsInstanceValid(context.Target)
		&& context.HorizontalTargetDistance <= EffectiveRange
		&& context.HasLineOfSightToTarget();
}