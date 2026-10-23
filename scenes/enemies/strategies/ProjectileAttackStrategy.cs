using Godot;

/// <summary>A shooter has one projectile attack; close targets never cause an implicit melee attack.</summary>
[GlobalClass]
public partial class ProjectileAttackStrategy : EnemyAttackStrategy
{
	public override StringName Style => "ranged";
	public override float EffectiveRange => Mathf.Min(EngagementDistance, Attack?.Range ?? EngagementDistance);
	public override bool CanAttack(EnemyContext context) => context.Target != null
		&& GodotObject.IsInstanceValid(context.Target) && context.HorizontalTargetDistance <= EffectiveRange
		&& context.HasLineOfSightToTarget();
}