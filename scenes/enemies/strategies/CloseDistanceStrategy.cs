using Godot;

/// <summary>
/// Approach until the configured attack is eligible, then hold for its execution. An approach that is
/// already in reach holds and turns instead (see <see cref="EnemyContext.ExecuteTactics"/>).
/// </summary>
[GlobalClass]
public partial class CloseDistanceStrategy : EnemyMovementStrategy
{
	public override EnemyMovementIntent Evaluate(EnemyContext context) =>
		context.Profile.AttackStrategy?.CanAttack(context) == true
			? new(EnemyMovementGoal.Hold, true) : new(EnemyMovementGoal.Approach, false);
}