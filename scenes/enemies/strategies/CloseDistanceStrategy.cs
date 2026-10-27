using Godot;

/// <summary>Approach until in reach, then hold and turn until the configured attack is eligible.</summary>
[GlobalClass]
public partial class CloseDistanceStrategy : EnemyMovementStrategy
{
	public override EnemyMovementIntent Evaluate(EnemyContext context)
	{
		var attack = context.Profile.AttackStrategy;
		if (attack?.CanAttack(context) == true) return new(EnemyMovementGoal.Hold, true);
		return attack?.IsInReach(context) == true ? new(EnemyMovementGoal.Hold, false) : new(EnemyMovementGoal.Approach, false);
	}
}