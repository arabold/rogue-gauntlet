using Godot;

/// <summary>Approach until the configured attack is eligible, then hold for its execution.</summary>
[GlobalClass]
public partial class CloseDistanceStrategy : EnemyMovementStrategy
{
	public override EnemyMovementIntent Evaluate(EnemyContext context) =>
		context.Profile.AttackStrategy?.CanAttack(context) == true
			? new(EnemyMovementGoal.Hold, true) : new(EnemyMovementGoal.Approach, false);
}